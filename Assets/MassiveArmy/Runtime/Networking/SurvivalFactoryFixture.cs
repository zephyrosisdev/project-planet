using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalFactoryFixtureSpec {
        public int version=1;public bool seeded;public string configPath,error;
        public bool largeSnapshots,buildings;public FramePose buildingGround,buildingStaging;
        public bool shipyard;public long shipEntity,shipParent,shipCore,shipAdded,shipWireCost;public int shipModules,shipWires;public FramePose shipGoal,factoryReturn;
        public long owner,observer,source,target,generator,consumer,pump,waterTarget,beltCost;
        public long initialPlates=100,initialGears=100,cargo=4;public double cargoActivity=.125;
        public List<FramePose> beltRoute=new List<FramePose>(),powerRoute=new List<FramePose>(),waterRoute=new List<FramePose>();
    }
#if UNITY_EDITOR || UNITY_SERVER
    public sealed partial class SurvivalNetworkSession {
        static bool DiagnosticPath(string path)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        // Explicit offline CLI setup only. It adds no server admission bypass or wire command.
        internal static bool SeedFactoryFixture(string path){
            SurvivalFactoryFixtureSpec spec=null;GameObject go=null;PlanetDefinition definition=null;
            try{
                if(!DiagnosticPath(path))throw new InvalidOperationException("Factory fixtures are limited to diagnostic Live saves");
                spec=JsonUtility.FromJson<SurvivalFactoryFixtureSpec>(File.ReadAllText(path));
                if(spec==null||spec.version!=1||spec.seeded||!DiagnosticPath(spec.configPath))throw new InvalidOperationException("Invalid or already seeded factory fixture");
                var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();
                string expected=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"industry.xml");
                if(Path.GetFullPath(config.savePath)!=expected||!config.worldId.StartsWith("probe-",StringComparison.Ordinal))throw new InvalidOperationException("Fixture must own its diagnostic save");
                var world=IndustrialAuthority.Load(expected);
                if(spec.largeSnapshots){config.packetBytesPerSecond=16384;File.WriteAllText(spec.configPath,JsonUtility.ToJson(config,true));foreach(var point in world.respawnPoints)if(point.owner==spec.owner)throw new InvalidOperationException("Large-state fixture needs no existing owned respawn points");for(int i=0;i<128;i++)world.respawnPoints.Add(new RespawnPoint{id=world.AllocateId(),owner=spec.owner,definition="sleeping-pod",name=new string((char)('A'+i%26),48),pose=new FramePose{body=1,x=i*30,y=120001}});}
                if(world.multiplayer==null||world.multiplayer.players.Count!=2||world.machines.Count!=0||world.belts.Count!=0||world.Actor(spec.owner)==null||world.Actor(spec.observer)==null||spec.owner==spec.observer)throw new InvalidOperationException("Fixture requires an empty completed two-character world");
                go=new GameObject("Offline factory fixture setup");var session=go.AddComponent<SurvivalNetworkSession>();session.Config=config;session.authority=new IndustrialAuthority(world);definition=ServerDefinition(config.seed);var surface=new PlanetSurface(definition);session.surfaces.Add(1,surface);
                var original=SurvivalVehicleSimulation.Position(world.Actor(spec.owner).pose);var normal=PlanetPrecisionPolicy.Direction(original);var east=Vector3.Cross(Mathf.Abs(normal.y)>.9f?Vector3.right:Vector3.up,normal).normalized;var north=Vector3.Cross(normal,east);
                bool found=false;string reason="No candidate";long first=world.nextId;
                for(int attempt=0;attempt<4096&&!found;attempt++){
                    double angle=attempt*2.3999632297,radius=250+(attempt/32)*30;var centre=surface.ProjectToSurface(original+new Double3(east)*(Math.Cos(angle)*radius)+new Double3(north)*(Math.Sin(angle)*radius),.05);var up=PlanetPrecisionPolicy.Direction(centre);var rotation=Quaternion.FromToRotation(Vector3.up,up);
                    FramePose Point(double x,double z,double lift=0){var p=surface.ProjectToSurface(centre+Double3.Rotate(rotation,new Double3(x,0,z)),.05);p+=new Double3(PlanetPrecisionPolicy.Direction(p))*lift;return new FramePose{body=1,x=p.x,y=p.y,z=p.z};}
                    world.machines.Clear();
                    IndustrialMachine Machine(string kind,int index,double x,double z){var machine=new IndustrialMachine{id=first+index,owner=spec.owner,definition=kind,pose=Point(x,z),recipe=IndustrialCatalog.Machines[kind].recipe};world.machines.Add(machine);return machine;}
                    var source=Machine("store",0,0,10);var target=Machine("store",1,0,-10);var generator=Machine("generator",2,8,10);var consumer=Machine("workbench",3,8,-10);var pump=Machine("pump",4,-8,10);var waterTarget=Machine("water-reservoir",5,-8,-10);
                    world.Actor(spec.owner).pose=Point(3,-12);world.Actor(spec.observer).pose=Point(-3,-12);
                    bool clear=true;foreach(var machine in world.machines){var p=SurvivalVehicleSimulation.Position(machine.pose);if(surface.SampleHeight(p)<definition.oceanHeight+.2){clear=false;break;}foreach(var ship in world.multiplayer.ships)if(machine.pose.Distance(ship.pose)<ship.collisionRadius+20){clear=false;break;}}
                    if(!clear)continue;
                    var belt=new IndustrialCommand{kind="belt",source=source.id,target=target.id,route=new List<FramePose>{Point(0,0,2.5)}};
                    var power=new IndustrialCommand{kind="power",source=generator.id,target=consumer.id,route=new List<FramePose>{Point(8,0,6)}};
                    var water=new IndustrialCommand{kind="water",source=pump.id,target=waterTarget.id,route=new List<FramePose>{Point(-8,0,6)}};
                    var actor=world.Actor(spec.owner);actor.health=100;actor.inventory=new Inventory{capacity=2000};actor.inventory.EnableSlots(40);actor.inventory.Add("plate",spec.initialPlates);actor.inventory.Add("gear",spec.initialGears);
                    // Validation canonicalizes the command; retain the original user points for clients.
                    var powerUser=new List<FramePose>(power.route);var waterUser=new List<FramePose>(water.route);
                    reason=session.ValidateFactoryConnection(actor,belt)??session.ValidateFactoryConnection(actor,power)??session.ValidateFactoryConnection(actor,water);if(reason!=null)continue;
                    spec.source=source.id;spec.target=target.id;spec.generator=generator.id;spec.consumer=consumer.id;spec.pump=pump.id;spec.waterTarget=waterTarget.id;spec.beltRoute=belt.route;spec.powerRoute=powerUser;spec.waterRoute=waterUser;spec.beltCost=(long)Math.Ceiling(new IndustrialConveyorPath(source,target,belt.route).Length/5);
                    spec.factoryReturn=target.pose;spec.buildingGround=Point(3,-19);spec.buildingStaging=Point(8,-22);
                    source.input.Add("iron-bar",spec.cargo,spec.cargoActivity);source.constructionPaid.Add(new ItemStack("plate",5));world.nextId=first+6;found=true;
                }
                if(!found)throw new InvalidOperationException("No valid clear factory patch: "+reason);
                if(spec.buildings){var stock=world.Actor(spec.owner).inventory;if(!stock.Add("stone",4,.25)||!stock.Add("iron-bar",12,.5))throw new InvalidOperationException("Building fixture inventory capacity");}
                if(spec.shipyard){
                    var ship=world.multiplayer.ships.Find(value=>value.owner==spec.owner);var modules=world.Ship(spec.owner);var cockpit=modules.modules.Find(module=>module.definition=="cockpit");var core=modules.modules.Find(module=>module.definition=="power-core");
                    spec.shipEntity=ship.entity;spec.shipParent=cockpit.id;spec.shipCore=core.id;spec.shipAdded=modules.nextModule;spec.shipModules=modules.modules.Count;spec.shipWires=modules.wires.Count;
                    var socket=ShipPoint(ship,ShipModules.Transform(cockpit.pose,ShipModules.Socket(cockpit,"forward").pose));var feet=surface.ProjectToSurface(socket,.05);spec.shipGoal=new FramePose{body=ship.pose.body,x=feet.x,y=feet.y,z=feet.z};
                    var addedPose=ShipModules.Attach(cockpit,"forward","cockpit","aft");var from=ShipModules.Transform(core.pose,ShipModules.Socket(core,"power").pose);var to=ShipModules.Transform(addedPose,Array.Find(ShipModules.Definitions["cockpit"].sockets,value=>value.name=="power").pose);spec.shipWireCost=Math.Max(1,(long)Math.Ceiling(from.Distance(to)/5));
                    var inventory=world.Actor(spec.owner).inventory;if(!inventory.Add("hull-panel",24)||!inventory.Add("electronics",16)||!inventory.Add("wire",100))throw new InvalidOperationException("Shipyard fixture inventory capacity");
                }
                foreach(var player in world.multiplayer.players){player.attachedShip=player.controlledShip=player.controlledEntity=player.pendingVehicleExit=0;player.shipSeat=-1;player.initialSpawnPending=false;player.oxygen=player.hunger=player.thirst=100;player.spawn=world.Actor(player.actor).pose;world.Actor(player.actor).health=100;}
                world.creativeMode=false;session.authority.Dispose();session.authority=null;
                using(var authority=new IndustrialAuthority(world,expected))authority.Save();
                spec.seeded=true;spec.error="";File.WriteAllText(path,JsonUtility.ToJson(spec,true));Debug.Log("Offline factory fixture prepared");return true;
            }catch(Exception e){Debug.LogError("Factory fixture setup: "+e.Message);if(spec!=null){spec.error=e.Message;File.WriteAllText(path,JsonUtility.ToJson(spec,true));}return false;}
            finally{if(go!=null){var session=go.GetComponent<SurvivalNetworkSession>();session.authority?.Dispose();session.authority=null;DestroyImmediate(go);}if(definition!=null)DestroyImmediate(definition);}
        }
    }
#endif
}
#endif
