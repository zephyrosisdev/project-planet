#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunBuildingChecks(Action<bool,string> check){
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            var cpu=PlayerBuildingProfiles.Create(true);var normal=PlayerBuildingProfiles.Create();bool same=cpu.Length==normal.Length;
            for(int i=0;i<cpu.Length;i++){same&=cpu[i].bounds==normal[i].bounds&&cpu[i].interactionBounds==normal[i].interactionBounds&&cpu[i].inputs.Length==normal[i].inputs.Length&&cpu[i].outputs.Length==normal[i].outputs.Length&&cpu[i].solids.Length==normal[i].solids.Length;
                for(int n=0;n<cpu[i].solids.Length;n++){var a=cpu[i].solids[n];var b=normal[i].solids[n];same&=a.bottom==b.bottom&&a.top==b.top&&a.polygon.Length==b.polygon.Length;for(int v=0;v<a.polygon.Length;v++)same&=a.polygon[v]==b.polygon[v];}
                if(cpu[i].Room){var profile=ColonyPrototypeLibrary.StoredProfile("room-"+cpu[i].kind);same&=profile.vertices==null&&profile.normals==null&&profile.triangles==null;}
            }
            check(same,"CPU construction catalogue matches offline solids, bounds and socket counts without deserializing render arrays");
            const string path="TestResults/Multiplayer/building-native.xml";Directory.CreateDirectory(Path.GetDirectoryName(path));foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(path+suffix))File.Delete(path+suffix);
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.bodyId=1;definition.radius=10000;definition.terrainHeight=0;definition.oceanHeight=-35;definition.stampTerrain=false;
            IndustrialAuthority authority=null;
            try{
                var surface=new PlanetSurface(definition);var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};var actor=world.Register(1,new FramePose{body=1,y=10000,z=-6});var guest=world.Register(2,new FramePose{body=1,x=20,y=10000,z=-6});actor.inventory=new Inventory();guest.inventory=new Inventory();world.multiplayer.players.Add(new SurvivalPlayerRecord{actor=1,identity="builder"});world.multiplayer.players.Add(new SurvivalPlayerRecord{actor=2,identity="guest"});actor.inventory.Add("stone",4,.25);actor.inventory.Add("iron-bar",12,.5);actor.inventory.Add("gear",1,.125);guest.inventory.Add("stone",2,.75);
                authority=new IndustrialAuthority(world,path);authority.Save();var buildings=new PlayerBuildAuthority(authority,body=>body==1?surface:null);long sequence=0;
                string Run(PlayerBuildCommand command)=>authority.ExecuteSurvivalAction(1,++sequence,()=>buildings.Execute(1,command));
                PlayerBuildCommand Foundation()=>new PlayerBuildCommand{operation="place",kind=PlayerModuleKind.FoundationSquare,pose=new FramePose{body=1,y=10000}};
                var invalid=Foundation();invalid.pose.frame=99;check(Run(invalid)!=null&&actor.inventory.Count("stone")==4,"Construction rejects client floating-origin or foreign-frame positions before spending");
                invalid=Foundation();invalid.pose.x=100;check(Run(invalid)!=null,"Construction rejects remote placement");
                check(Run(Foundation())==null&&actor.inventory.Count("stone")==2,"Fresh foundations are terrain-fitted and paid through the industrial journal");
                var site=buildings.Storage(1).playerBuildSites[0];long root=site.modules[0].id,siteId=site.id;long replay=sequence;
                check(authority.ExecuteSurvivalAction(1,replay,()=>buildings.Execute(1,Foundation()))!=null&&buildings.Storage(1).playerBuildSites[0].modules.Count==1,"Duplicate construction actions cannot duplicate modules or spend twice");
                check(buildings.Execute(2,new PlayerBuildCommand{operation="remove",site=siteId,module=root})!=null,"Other authenticated players cannot dismantle the owner's building");
                var doorway=new PlayerBuildCommand{operation="place",site=siteId,parent=root,socket=1,input=0,kind=PlayerModuleKind.DoorwaySingle};
                check(Run(doorway)==null&&Run(doorway)!=null,"Structural placement derives its pose on the server and rejects a second occupant of the same socket");
                site=buildings.Storage(1).playerBuildSites[0];long doorwayId=site.modules[1].id;
                check(Run(new PlayerBuildCommand{operation="place",site=siteId,parent=doorwayId,socket=2,input=0,kind=PlayerModuleKind.DoorSingle})==null,"A compatible door is constructed at the authoritative doorway socket");
                site=buildings.Storage(1).playerBuildSites[0];long doorId=site.modules[2].id;
                check(Run(new PlayerBuildCommand{operation="remove",site=siteId,module=root})!=null,"Supporting modules cannot be recovered before their children");
                check(Run(new PlayerBuildCommand{operation="door",site=siteId,module=doorId})==null,"Owner door changes use the same persisted construction store");
                site=buildings.Storage(1).playerBuildSites[0];var door=site.modules.Find(m=>m.id==doorId);var feet=PlayerBuildAuthority.Point(site,door.position);actor.pose=new FramePose{body=1,x=feet.x,y=feet.y,z=feet.z};
                check(Run(new PlayerBuildCommand{operation="door",site=siteId,module=doorId})!=null&&buildings.Storage(1).playerBuildSites[0].modules.Find(m=>m.id==doorId).open,"Closing a door onto a living player is rejected without changing the saved door");actor.pose=new FramePose{body=1,y=10000,z=-6};
                check(buildings.Execute(2,new PlayerBuildCommand{operation="place",kind=PlayerModuleKind.FoundationSquare,pose=new FramePose{body=1,x=20,y=10000}})==null&&buildings.Storage(1).playerBuildSites.Count==2&&world.mounts.Exists(m=>m.owner==1)&&world.mounts.Exists(m=>m.owner==2),"A second owner preserves every site's state and correctly owned industrial mounts on the same body");
                RunBuildingPublicationChecks(authority,definition,check);
                authority.Save();authority.Dispose();authority=new IndustrialAuthority(IndustrialAuthority.Load(path),path);buildings=new PlayerBuildAuthority(authority,body=>body==1?surface:null);actor=authority.World.Actor(1);
                check(buildings.Storage(1).playerBuildSites.Count==2&&buildings.Storage(1).playerBuildSites.Find(s=>s.id==siteId).modules.Find(m=>m.id==doorId).open,"Restart restores multiple owners and the authoritative open door");
                check(Run(new PlayerBuildCommand{operation="remove",site=siteId,module=doorId})==null&&Run(new PlayerBuildCommand{operation="remove",site=siteId,module=doorwayId})==null&&Run(new PlayerBuildCommand{operation="remove",site=siteId,module=root})==null,"Recovery follows supported structure order through the journal after restart");
                check(actor.inventory.Count("stone")==4&&actor.inventory.Count("iron-bar")==12&&actor.inventory.Count("gear")==1&&Math.Abs(actor.inventory.ActivityOf("stone",4)-.25)<1e-9&&Math.Abs(actor.inventory.ActivityOf("iron-bar",12)-.5)<1e-9&&buildings.Storage(1).playerBuildSites.Count==1,"Building recovery conserves original materials and activity and preserves the other owner's site");
                RunContactChecks(check);
                check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Camera>().Length==cameras,"Authoritative construction and contacts allocate no meshes, materials or cameras");
            }finally{authority?.Dispose();DestroyImmediate(definition);}
        }
        static void RunBuildingPublicationChecks(IndustrialAuthority authority,PlanetDefinition definition,Action<bool,string> check){
            var go=new GameObject("Headless building publication fixture");var session=go.AddComponent<SurvivalNetworkSession>();
            try{
                session.authority=authority;session.Config=new SurvivalServerConfig();session.surfaces[1]=new PlanetSurface(definition);
                foreach(var record in authority.World.multiplayer.players){var actor=authority.World.Actor(record.actor);session.peers[(ulong)record.actor]=new Peer{record=record,motion=new SurvivalMotion{actor=actor.id,pose=actor.pose}};}
                SurvivalSnapshot packet=null;void Publish()=>session.Publish((client,snapshot,payload)=>{if(client==1)packet=JsonUtility.FromJson<SurvivalSnapshot>(System.Text.Encoding.UTF8.GetString(payload));return payload.Length+4;});
                Publish();var ids=new List<long>();bool redacted=true;foreach(var entity in packet.changed)if(entity.kind=="building"){ids.Add(entity.id);redacted&=entity.id<0&&!entity.data.Contains("constructionPaid")&&!entity.data.Contains("constructionActivity")&&entity.pose.body==1&&entity.pose.frame==0;}
                check(ids.Count==4&&redacted,"Nearby building entry publishes canonical structural state for both owners without payment receipts");
                session.ReceiveBuildings(packet);session.ReceiveContacts(packet);check(session.clientBuildings.Count==4,"Client building/contact caches restore the complete bounded initial subscription");
                var actorOne=authority.World.Actor(1);var saved=actorOne.pose;actorOne.pose.x=2000;session.peers[1].motion.pose=actorOne.pose;Publish();bool exits=true;foreach(long id in ids)exits&=packet.removed.Contains(id);session.ReceiveBuildings(packet);session.ReceiveContacts(packet);check(exits&&session.clientBuildings.Count==0,"AOI exit removes structural state and CPU contacts from the client");
                actorOne.pose=saved;session.peers[1].motion.pose=saved;Publish();session.ReceiveBuildings(packet);session.ReceiveContacts(packet);check(session.clientBuildings.Count==4,"AOI re-entry restores complete door and ownership state after removal");
            }finally{session.authority=null;session.ClearEntityViews();DestroyImmediate(go);}
        }
        static void RunContactChecks(Action<bool,string> check){
            var contacts=new SurvivalContacts();var origin=new Double3(0,10000,0);
            contacts.Put(new SurvivalContacts.Shape{id=1,body=1,origin=origin,rotation=Quaternion.identity,radius=8,solids=new[]{BuildSolid.Box(-5,5,-5,5,0,.5f),BuildSolid.Box(-5,5,.9f,1,0,3)}});
            var previous=new FramePose{body=1,y=10000.55};var state=new SurvivalMotion{pose=new FramePose{body=1,y=10000.50,z=.1},grounded=true,verticalSpeed=-1,forwardZ=1};contacts.Resolve(state,previous,true);
            check(state.grounded&&Math.Abs(state.pose.y-10000.55)<.001,"CPU contacts support walking on a constructed floor above terrain");
            previous=state.pose;state.pose.z=2;contacts.Resolve(state,previous,true);check(state.pose.z<.65&&state.pose.z>.3,"Swept CPU contacts prevent tunnelling through a thin wall");
            var replay=new SurvivalMotion{pose=new FramePose{body=1,y=10000.50,z=.1},grounded=true,verticalSpeed=-1,forwardZ=1};contacts.Resolve(replay,new FramePose{body=1,y=10000.55},true);var before=replay.pose;replay.pose.z=2;contacts.Resolve(replay,before,true);check(replay.pose.Distance(state.pose)<1e-8,"Prediction replay reproduces CPU contact resolution in canonical coordinates");
            var embeddedStart=new FramePose{body=1,y=10000.55,z=.57};var embedded=new SurvivalMotion{pose=new FramePose{body=1,x=.2,y=10000.54,z=.77},grounded=true,verticalSpeed=-1,forwardZ=1};contacts.Resolve(embedded,embeddedStart,true);
            check(embedded.pose.x>.1&&embedded.pose.z<.56,"A shallow existing wall overlap recovers and permits tangential walking instead of trapping the character");
            var other=new SurvivalMotion{pose=new FramePose{body=2,y=10000.50,z=2},verticalSpeed=-1};contacts.Resolve(other,new FramePose{body=2,y=10000.55},true);check(other.pose.z==2,"CPU contact subscriptions isolate independent planetary bodies");
            check(contacts.ProbeGround(1,origin+new Double3(0,.7,0),Vector3.up,.5f,1,out var support)&&Math.Abs(support.position.y-10000.5)<.001&&support.normal==Vector3.up,"CPU vehicle probes select the constructed floor with the same canonical support normal");
            check(!contacts.ProbeGround(1,origin+new Double3(0,.7,0),Vector3.up,.5f,1,out _,1),"A vehicle ground query excludes its own indexed body without excluding other world contacts");
            var vehicle=ScriptableObject.CreateInstance<VehicleDefinition>();try{vehicle.chassisCentre=new Vector3(0,1.5f,0);vehicle.chassisSize=new Vector3(2.3f,1.6f,4.5f);var allowed=contacts.SweepVehicle(1,origin+new Double3(0,0,-3),Quaternion.identity,vehicle,Vector3.forward*6,out var normal);check(allowed.z>0&&allowed.z<2&&normal.z<-.9f,"The shared vehicle chassis sweep stops at a thin constructed wall without render colliders");var ignored=contacts.SweepVehicle(1,origin+new Double3(0,0,-3),Quaternion.identity,vehicle,Vector3.forward*6,out _,1);check(ignored.z==6,"A vehicle chassis sweep cannot collide with its own indexed body");}finally{DestroyImmediate(vehicle);}
            contacts.Remove(1);state.pose.z=2;contacts.Resolve(state,previous,true);check(state.pose.z==2,"Interest removal releases stale collision state");
            var stairs=BuildingShape(1,new PlayerBuildSite{id=4,anchor=origin,rotation=Quaternion.identity},new PlayerModuleRecord{id=5,kind=PlayerModuleKind.StairsStraight},PlayerBuildingProfiles.Create(true)[(int)PlayerModuleKind.StairsStraight]);contacts.Put(stairs);
            var climbing=new SurvivalMotion{pose=new FramePose{body=1,y=10000.05,z=-2},grounded=true,forwardZ=1};
            for(int i=0;i<45;i++){var start=climbing.pose;climbing.pose.z+=.1;climbing.verticalSpeed=-1;climbing.pose.y-=.02;contacts.Resolve(climbing,start,true);}
            check(climbing.pose.z>1.4&&climbing.pose.y>10002.8,"CPU walking climbs the same continuous stair ramp used by offline contact meshes");
            check(stairs.solids.Length==0&&stairs.ramps.Length==1,"Headless stairs use continuous ramp contacts rather than render-step risers");
        }
    }
}
#endif
