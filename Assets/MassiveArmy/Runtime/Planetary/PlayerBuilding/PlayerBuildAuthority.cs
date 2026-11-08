using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Planetary {
    [Serializable] internal sealed class PlayerBuildCommand {
        public string operation;public long site,module,parent;public int socket,input,turn;public PlayerModuleKind kind;public FramePose pose;
    }
    // The server and offline adapter share profiles, resource receipts and the
    // existing industrial journal. No renderer or physics scene is required.
    internal sealed class PlayerBuildAuthority {
        readonly IndustrialAuthority authority;readonly Func<int,PlanetSurface> surface;
        internal readonly PlayerModuleProfile[] profiles=PlayerBuildingProfiles.Create(true);
        readonly Dictionary<int,(string json,PlayerBuildStorage storage)> cache=new Dictionary<int,(string,PlayerBuildStorage)>();
        internal PlayerBuildAuthority(IndustrialAuthority authority,Func<int,PlanetSurface> surface){this.authority=authority;this.surface=surface;}
        internal PlayerBuildStorage Storage(int body){var saved=authority.World.buildStates.Find(s=>s.body==body);string json=saved?.json;if(cache.TryGetValue(body,out var found)&&found.json==json)return found.storage;var storage=json==null?new PlayerBuildStorage():JsonUtility.FromJson<PlayerBuildStorage>(json);cache[body]=(json,storage);return storage;}
        internal static Double3 Point(PlayerBuildSite site,Vector3 local)=>site.anchor+Double3.Rotate(site.rotation,new Double3(local));
        internal static Quaternion Rotation(PlayerBuildSite site,PlayerModuleRecord record)=>site.rotation*Quaternion.Euler(0,record.yaw,0);
        internal static FramePose Pose(int body,PlayerBuildSite site,Vector3 local){var p=Point(site,local);return new FramePose{body=body,x=p.x,y=p.y,z=p.z};}
        internal static List<ItemStack> Refund(PlayerModuleRecord record){if(record.resourceConstruction)return record.constructionPaid;string item=record.constructionItem??"stone";int count=record.constructionCount>0?record.constructionCount:2;if(IndustrialCatalog.RetiredConstructionRecipe(item)&&IndustrialCatalog.Recipes.TryGetValue(item,out var recipe)){var result=IndustrialConstruction.WithActivity(recipe.input,record.constructionActivity);foreach(var stack in result)stack.count*=count;return result;}return new List<ItemStack>{new ItemStack(item,count){activity=record.constructionActivity}};}
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        internal bool Overlaps(int body,Double3 origin,Quaternion rotation,BuildSolid[] solids,double radius,long ignoreModule=0){
            foreach(var site in Storage(body).playerBuildSites){if((origin-site.anchor).Magnitude>PlayerBuildingProfiles.SiteRadius+32+radius)continue;var local=Double3.Rotate(Quaternion.Inverse(site.rotation),origin-site.anchor).Float;var q=Quaternion.Inverse(site.rotation)*rotation;
                foreach(var module in site.modules){if(module.id==ignoreModule)continue;var profile=profiles[(int)module.kind];if((module.position-local).magnitude>radius+profile.interactionBounds.center.magnitude+profile.interactionBounds.extents.magnitude)continue;foreach(var a in solids)foreach(var b in profile.Shape(module.open))if(PlayerBuildingProfiles.Overlap(a,local,q,b,module.position,Quaternion.Euler(0,module.yaw,0)))return true;}
            }return false;
        }
        internal string Execute(long identity,PlayerBuildCommand command){
            var actor=authority.World.Actor(identity);if(actor==null||actor.health<=0||command==null)return "Construction actor unavailable";
            string policy=authority.World.ConstructionPolicyError(identity);if(policy!=null)return policy;
            if(actor.pose.frame!=0||surface(actor.pose.body)==null)return "Construction requires a surface region";
            if(command.operation!="place"&&command.operation!="remove"&&command.operation!="door")return "Unknown construction operation";
            // Work on a detached candidate; a rejected edit never changes the live store.
            int body=actor.pose.body;var storage=JsonUtility.FromJson<PlayerBuildStorage>(JsonUtility.ToJson(Storage(body)));
            var site=storage.playerBuildSites.Find(s=>s.id==command.site);PlayerModuleRecord record=null;bool refund=false;
            if(command.operation!="place"){
                if(site==null||site.owner!=identity)return "Only the site owner can edit this building";
                record=site.modules.Find(m=>m.id==command.module);if(record==null)return "Module unavailable";
                if(Pose(body,site,record.position).Distance(actor.pose)>PlayerBuildingProfiles.Reach)return "Move within construction reach";
                if(command.operation=="remove"){
                    if(site.modules.Exists(m=>m.parent==record.id))return "Recover supported modules first";
                    if(authority.World.machines.Exists(m=>m.attachmentSite==site.id&&m.attachmentModule==record.id&&(!m.integratedLogistics||m.input.Used>0||authority.World.belts.Exists(b=>b.source==m.id||b.target==m.id))))return "Recover attached machinery and logistics first";
                    foreach(var person in authority.World.actors)if(!person.networkAbsent&&person.health>0&&person.pose.body==body&&person.pose.frame==0&&TouchesActor(site,record,person))return "Move clear before dismantling the module";
                    if(!actor.inventory.CanAddBatch(Refund(record)))return "Inventory has no room for the original materials";
                    site.modules.Remove(record);if(site.modules.Count==0)storage.playerBuildSites.Remove(site);refund=true;
                }else{
                    if(!profiles[(int)record.kind].Door)return "This module has no door";
                    record.open=!record.open;string error=Validate(storage,site,record,actor,true);if(error!=null)return error;
                }
            }else{
                if(!Enum.IsDefined(typeof(PlayerModuleKind),command.kind)||command.turn<0||command.turn>3)return "Invalid module or rotation";
                var profile=profiles[(int)command.kind];int count=0;foreach(var s in storage.playerBuildSites)count+=s.modules.Count;if(count>=4096)return "Region construction limit reached";
                if(site==null){
                    if(command.site!=0||command.parent!=0||!profile.Foundation&&!profile.Room)return "A free compatible socket is required";
                    if(storage.playerBuildSites.Count>=128)return "Region build-site limit reached";
                    var pose=command.pose;if(pose.body!=body||pose.frame!=0||!Finite(pose.x)||!Finite(pose.y)||!Finite(pose.z)||!Finite(pose.yaw)||pose.Distance(actor.pose)>PlayerBuildingProfiles.Reach)return "Invalid or out-of-reach ground position";
                    var planet=surface(body);var ground=new Double3(pose.x,pose.y,pose.z);if(ground.Magnitude<1)return "Invalid ground position";
                    ground*= (planet.Definition.radius+planet.SampleHeight(ground))/ground.Magnitude;var up=PlanetPrecisionPolicy.Direction(ground);
                    var rotation=Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,pose.yaw,0);
                    var anchor=profile.Room?ConstructionPlacement.FitRoomSurface(planet,ground,rotation,profile.bounds):ground-new Double3(up)*2.5;
                    site=new PlayerBuildSite{owner=identity,anchor=anchor,rotation=rotation};storage.playerBuildSites.Add(site);
                    record=new PlayerModuleRecord{kind=command.kind,parentSocket=-1};
                }else{
                    if(site.owner!=identity)return "Only the site owner can edit this building";
                    var parent=site.modules.Find(m=>m.id==command.parent);if(parent==null||command.socket<0||command.socket>=profiles[(int)parent.kind].outputs.Length||command.input<0||command.input>=profile.inputs.Length)return "Invalid structural socket";
                    if(site.modules.Exists(m=>m.parent==parent.id&&m.parentSocket==command.socket))return "Structural socket already occupied";
                    var output=profiles[(int)parent.kind].outputs[command.socket];var input=profile.inputs[command.input];if(input.kind!=output.kind)return "Incompatible structural socket";
                    float yaw=parent.yaw+output.yaw-input.yaw+(input.kind==BuildSocketKind.FoundationEdge||input.kind==BuildSocketKind.FloorEdge||input.kind==BuildSocketKind.HabitatPort?180:0);
                    if(input.kind==BuildSocketKind.StairCenter||input.kind==BuildSocketKind.FloorCenter)yaw+=command.turn*90;else if(profile.Wall||profile.Door)yaw+=(command.turn%2)*180;
                    record=new PlayerModuleRecord{kind=command.kind,parent=parent.id,parentSocket=command.socket,yaw=yaw,position=parent.position+Quaternion.Euler(0,parent.yaw,0)*output.point-Quaternion.Euler(0,yaw,0)*input.point};
                }
                string error=Validate(storage,site,record,actor,false);if(error!=null)return error;
                var cost=IndustrialCatalog.ModuleCost(command.kind.ToString());if(!actor.inventory.Has(cost))return "Construction materials unavailable";
                record.resourceConstruction=true;record.constructionPaid=IndustrialConstruction.Receipt(actor.inventory,cost,false);foreach(var paid in record.constructionPaid)record.constructionActivity+=paid.activity;
                if(site.id==0)site.id=authority.World.AllocateId();record.id=authority.World.AllocateId();site.modules.Add(record);
            }
            PlayerBuildProjection.Commit(authority,identity,body,storage,command.operation=="door"?new List<ItemStack>():refund?Refund(record):record.constructionPaid,refund);
            cache.Remove(body);return null;
        }
        bool TouchesActor(PlayerBuildSite site,PlayerModuleRecord record,IndustrialActor person){var feet=Double3.Rotate(Quaternion.Inverse(site.rotation),SurvivalPosition(person.pose)-site.anchor).Float;foreach(var shape in profiles[(int)record.kind].Shape(record.open))if(PlayerBuildingProfiles.Overlap(shape,record.position,Quaternion.Euler(0,record.yaw,0),BuildSolid.Box(-.35f,.35f,-.35f,.35f,.02f,1.8f),feet,Quaternion.identity))return true;return false;}
        static Double3 SurvivalPosition(FramePose p)=>new Double3(p.x,p.y,p.z);
        string Validate(PlayerBuildStorage storage,PlayerBuildSite site,PlayerModuleRecord record,IndustrialActor actor,bool door){
            var profile=profiles[(int)record.kind];int body=actor.pose.body;var centre=Point(site,record.position);var rotation=Rotation(site,record);
            if(record.position.magnitude>PlayerBuildingProfiles.SiteRadius||Pose(body,site,record.position).Distance(actor.pose)>PlayerBuildingProfiles.Reach)return "Move within construction reach and site extent";
            foreach(var otherSite in storage.playerBuildSites){if((centre-otherSite.anchor).Magnitude>PlayerBuildingProfiles.SiteRadius+32)continue;var position=Double3.Rotate(Quaternion.Inverse(otherSite.rotation),centre-otherSite.anchor).Float;var q=Quaternion.Inverse(otherSite.rotation)*rotation;
                foreach(var other in otherSite.modules){if(door&&other.id==record.id)continue;var shape=profiles[(int)other.kind];
                    if(!door&&site==otherSite&&record.parent!=0&&!profile.Room&&!shape.Room){if((other.position-position).sqrMagnitude<.0004f&&(other.kind==profile.kind||shape.Wall&&profile.Wall))return "Module socket already filled";continue;}
                    var turn=Quaternion.Euler(0,other.yaw,0);foreach(var a in profile.Shape(record.open))foreach(var b in shape.Shape(other.open))if(PlayerBuildingProfiles.Overlap(a,position,q,b,other.position,turn,profile.Wall&&shape.Wall?.055f:.006f))return "Intersects another building module";
                    foreach(var a in profile.clearance)foreach(var b in shape.Shape(other.open))if(PlayerBuildingProfiles.Overlap(a,position,q,b,other.position,turn))return "Stair headroom is obstructed";
                    foreach(var a in profile.Shape(record.open))foreach(var b in shape.clearance)if(PlayerBuildingProfiles.Overlap(a,position,q,b,other.position,turn))return "Keep stairwell clear";
                }
            }
            foreach(var person in authority.World.actors)if(!person.networkAbsent&&person.health>0&&person.pose.body==body&&person.pose.frame==0&&TouchesActor(site,record,person))return "Move clear of the module";
            foreach(var machine in authority.World.machines)if(!machine.integratedLogistics&&machine.pose.body==body&&machine.pose.frame==0){var local=Double3.Rotate(Quaternion.Inverse(rotation),SurvivalPosition(machine.pose)-centre).Float;var machineUp=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(SurvivalPosition(machine.pose)))*Quaternion.Euler(0,machine.pose.yaw,0);var box=BuildSolid.Box((float)-machine.Definition.Width*.5f,(float)machine.Definition.Width*.5f,(float)-machine.Definition.Depth*.5f,(float)machine.Definition.Depth*.5f,0,(float)IndustrialCablePath.Height(machine.definition));foreach(var shape in profile.Shape(record.open))if(PlayerBuildingProfiles.Overlap(shape,Vector3.zero,Quaternion.identity,box,local,Quaternion.Inverse(rotation)*machineUp))return "Keep construction clear of machinery";}
            if(authority.World.multiplayer!=null)foreach(var ship in authority.World.multiplayer.ships){if(ship.pose.body!=body||(centre-SurvivalPosition(ship.pose)).Magnitude>ship.collisionRadius+profile.interactionBounds.extents.magnitude+profile.interactionBounds.center.magnitude)continue;var assembly=authority.World.Ship(ship.owner);if(assembly==null)continue;var shipRotation=new Quaternion(ship.rotationX,ship.rotationY,ship.rotationZ,ship.rotationW).normalized;
                foreach(var module in assembly.modules){var spec=ShipModules.Definitions[module.definition];var modulePoint=SurvivalPosition(ship.pose)+Double3.Rotate(shipRotation,SurvivalPosition(module.pose));var local=Double3.Rotate(Quaternion.Inverse(rotation),modulePoint-centre).Float;var box=BuildSolid.Box((float)-spec.width*.5f,(float)spec.width*.5f,(float)-spec.depth*.5f,(float)spec.depth*.5f,(float)-spec.height*.5f,(float)spec.height*.5f);foreach(var shape in profile.Shape(record.open))if(PlayerBuildingProfiles.Overlap(shape,Vector3.zero,Quaternion.identity,box,local,Quaternion.Inverse(rotation)*shipRotation*Quaternion.Euler(0,module.pose.yaw,0)))return "Keep construction clear of ships";}
            }
            var planet=surface(body);
            if(profile.Room&&record.parent==0)for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){var point=centre+Double3.Rotate(rotation,new Double3(x*profile.bounds.extents.x,0,z*profile.bounds.extents.z));double gap=point.Magnitude-planet.Definition.radius-planet.SampleHeight(point);if(gap<-.02||gap>.65)return "Terrain too uneven for a standalone room";}
            if(profile.Foundation)foreach(var solid in profile.solids)foreach(var corner in solid.polygon){var point=centre+Double3.Rotate(rotation,new Double3(corner.x,0,corner.y));double gap=point.Magnitude-planet.Definition.radius-planet.SampleHeight(point);if(gap>.1||gap< -3.05)return "Foundation must contact terrain without burying its top";}
            return null;
        }
    }
}
