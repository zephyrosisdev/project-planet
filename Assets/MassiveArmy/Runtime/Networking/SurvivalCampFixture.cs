#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalCampFixtureSpec {public int version=1;public bool seeded;public string configPath,error,phase="trade";public long owner,passenger,ship,camp;}
#if UNITY_EDITOR || UNITY_SERVER
    public sealed partial class SurvivalNetworkSession {
        internal static bool SeedCampFixture(string path){
            SurvivalCampFixtureSpec spec=null;SurvivalUniverse savedUniverse=null;
            try{
                if(!DiagnosticPath(path))throw new InvalidOperationException("Camp fixtures require disposable Live saves");spec=JsonUtility.FromJson<SurvivalCampFixtureSpec>(File.ReadAllText(path));
                if(spec==null||spec.version!=1||spec.seeded||!DiagnosticPath(spec.configPath)||!(spec.phase=="trade"||spec.phase=="landing"||spec.phase=="eva"))throw new InvalidOperationException("Invalid camp fixture");
                var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();if(!DiagnosticPath(config.savePath))throw new InvalidOperationException("Fixture save escaped Live");
                var world=IndustrialAuthority.Load(config.savePath);var ship=world.multiplayer.ships.Find(s=>s.owner==spec.owner);if(ship==null||world.Actor(spec.passenger)==null)throw new InvalidOperationException("Fixture needs two existing characters and an owner ship");
                savedUniverse=SurvivalUniverse.Restore(world.universe,config.worldId,config.seed);var surface=savedUniverse.Surface(ship.pose.body);var camp=world.camps.Find(c=>c.pose.body==ship.pose.body);if(camp==null)throw new InvalidOperationException("No generated camp for this body");spec.camp=camp.id;spec.ship=ship.entity;
                var pad=camp.pads[0].pose;var point=SurvivalCampGeneration.Point(pad);var up=PlanetPrecisionPolicy.Direction(point);var rotation=SurvivalCampGeneration.Rotation(pad);var velocity=default(Double3);
                if(spec.phase=="eva"){point=new Double3(up)*(surface.Definition.radius+Math.Max(surface.Definition.atmosphereHeight+10000,80000));velocity=new Double3(rotation*Vector3.right)*Math.Sqrt(surface.Definition.gravitationalParameter/point.Magnitude);}
                else point+=new Double3(up)*150;
                var physics=new PlanetShipSimulationSnapshot{pose=new SpacePose{position=point,rotation=rotation,linearVelocity=velocity},gear=true,assist=spec.phase!="eva",heldClearance=158};ship.pose=new FramePose{body=pad.body,x=point.x,y=point.y,z=point.z};ship.rotationX=rotation.x;ship.rotationY=rotation.y;ship.rotationZ=rotation.z;ship.rotationW=rotation.w;ship.velocityX=velocity.x;ship.velocityY=velocity.y;ship.velocityZ=velocity.z;ship.spinX=ship.spinY=ship.spinZ=0;ship.physics=JsonUtility.ToJson(physics);ship.landed=ship.autoFlight=ship.recalling=false;ship.pilot=ship.dockedStation=ship.landingCamp=0;ship.landingPad=-1;ship.rearOpen=spec.phase=="eva";ship.innerOpen=false;ship.recallReadyTick=0;if(!ship.invitedActors.Contains(spec.passenger))ship.invitedActors.Add(spec.passenger);
                foreach(long id in new[]{spec.owner,spec.passenger}){var actor=world.Actor(id);var record=world.multiplayer.players.Find(p=>p.actor==id);record.attachedShip=record.controlledShip=record.controlledEntity=record.pendingVehicleExit=0;record.interiorWalking=record.eva=false;record.shipSeat=-1;record.verticalSpeed=record.walkVelocityX=record.walkVelocityY=record.walkVelocityZ=0;record.evaVelocityX=record.evaVelocityY=record.evaVelocityZ=record.evaSuitX=record.evaSuitY=record.evaSuitZ=0;actor.health=100;record.oxygen=100;record.hunger=record.thirst=100;
                    if(spec.phase=="trade"||id==spec.passenger&&spec.phase=="landing"){var at=SurvivalCampGeneration.Point(camp.shops[0].pose)+Double3.Rotate(rotation,new Double3(id==spec.owner?-1:1,0,-3));at=surface.ProjectToSurface(at,.05);actor.pose=new FramePose{body=pad.body,x=at.x,y=at.y,z=at.z};actor.inventory.Add("stone",64);}
                    else {var local=id==spec.owner?(spec.phase=="eva"?ShipModules.P(0,-3.95,-37):PilotExitLocal(world.Ship(id))):ShipModules.P(5,-3.95,-20);local.body=pad.body;local.frame=ship.entity;local.yaw=spec.phase=="eva"&&id==spec.owner?180:0;record.attachedShip=ship.entity;record.interiorWalking=true;record.interiorPose=local;record.shipSeat=id==spec.owner?-1:0;actor.pose=SurvivalShipInteriorModel.World(ship,local);}
                }
                using(var store=new IndustrialAuthority(world,config.savePath))store.Save();spec.seeded=true;spec.error="";return true;
            }catch(Exception error){if(spec!=null)spec.error=error.Message;Debug.LogError("Camp fixture: "+error);return false;}
            finally{savedUniverse?.Dispose();if(spec!=null)File.WriteAllText(path,JsonUtility.ToJson(spec,true));}
        }
    }
#endif
}
#endif
