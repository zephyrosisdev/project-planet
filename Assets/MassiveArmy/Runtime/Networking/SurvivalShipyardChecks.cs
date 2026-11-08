#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunShipyardChecks(Action<bool,string> check){
            if(Active!=null)throw new InvalidOperationException("Shipyard fixture requires an isolated session");
            const string path="TestResults/Multiplayer/shipyard-native.xml";Directory.CreateDirectory(Path.GetDirectoryName(path));foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(path+suffix))File.Delete(path+suffix);
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            var go=new GameObject("Authoritative shipyard fixture");var session=go.AddComponent<SurvivalNetworkSession>();PlanetDefinition definition=null;
            try{
                session.Config=new SurvivalServerConfig();definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.bodyId=1;definition.radius=10000;definition.terrainHeight=0;definition.oceanHeight=-35;definition.stampTerrain=false;session.surfaces.Add(1,new PlanetSurface(definition));
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};var actor=world.Register(1,new FramePose{body=1,y=10000,z=38});var outsider=world.Register(2,new FramePose{body=1,y=10000,z=38,x=2});
                actor.inventory.Add("hull-panel",24,.25);actor.inventory.Add("electronics",16,.125);actor.inventory.Add("wire",100,.375);
                var record=new SurvivalPlayerRecord{actor=1,identity="builder",name="Builder"};world.multiplayer.players.Add(record);world.multiplayer.players.Add(new SurvivalPlayerRecord{actor=2,identity="other",name="Other"});
                var peer=new Peer{record=record,motion=new SurvivalMotion{actor=1,pose=actor.pose}};var otherPeer=new Peer{record=world.multiplayer.players[1],motion=new SurvivalMotion{actor=2,pose=outsider.pose}};session.peers[1]=peer;session.peers[2]=otherPeer;
                var assembly=ShipModules.Starter(1);world.ships.Add(assembly);var state=new SurvivalShipState{entity=assembly.id,owner=1,pose=new FramePose{body=1,y=10008}};world.multiplayer.ships.Add(state);
                session.authority=new IndustrialAuthority(world,path);session.authority.Save();
                var cockpit=assembly.modules.Find(module=>module.definition=="cockpit");var core=assembly.modules.Find(module=>module.definition=="power-core");long sequence=0;
                IndustrialCommand Place()=>new IndustrialCommand{kind="ship-place",source=cockpit.id,item="forward",definition="cockpit",operationRole="aft"};
                string Run(IndustrialCommand command)=>session.authority.ExecuteSurvivalAction(1,++sequence,()=>session.ExecuteNetworkIndustrial(peer,command));
                state.landed=false;check(Run(Place())!=null&&actor.inventory.Count("hull-panel")==24,"Ship construction rejects airborne edits without spending materials");state.landed=true;
                actor.pose.x=1000;check(Run(Place())!=null,"Ship construction rejects out-of-reach sockets");actor.pose.x=0;
                check(session.ExecuteNetworkIndustrial(otherPeer,Place())!=null&&assembly.modules.Count==ShipModules.Starter(1).modules.Count,"Another authenticated actor cannot edit this ship through local module IDs");
                int original=assembly.modules.Count;check(Run(Place())==null&&assembly.modules.Count==original+1&&actor.inventory.Count("hull-panel")==12&&actor.inventory.Count("electronics")==8,"A validated structural command pays exact manufactured components through the existing authority");
                var added=assembly.modules[assembly.modules.Count-1];long paidSequence=sequence;check(session.authority.ExecuteSurvivalAction(1,paidSequence,()=>session.ExecuteNetworkIndustrial(peer,Place()))!=null&&assembly.modules.Count==original+1,"Replayed ship construction cannot add a second section or charge twice");
                var occupant=world.Register(3,new FramePose{body=1,y=10008+added.pose.y,z=added.pose.z});check(Run(new IndustrialCommand{kind="ship-remove",source=added.id})!=null,"Dismantling a section containing a living player is rejected");occupant.health=0;
                check(Run(new IndustrialCommand{kind="ship-wire",source=core.id,target=added.id,route=new List<FramePose>{new FramePose{body=1,frame=999,x=0,y=0,z=10}}})!=null,"Power routing rejects a different ship reference frame");
                long wiresBefore=actor.inventory.Count("wire");check(Run(new IndustrialCommand{kind="ship-wire",source=core.id,target=added.id,route=new List<FramePose>{new FramePose{body=1,frame=state.entity,x=0,y=0,z=10}}})==null&&actor.inventory.Count("wire")<wiresBefore,"A canonical local-frame power route is validated, normalized and paid authoritatively");
                check(Run(new IndustrialCommand{kind="ship-remove",source=added.id})!=null,"Connected power cables must be recovered before dismantling a section");
                session.authority.Save();session.authority.Dispose();session.authority=null;var recovered=IndustrialAuthority.Load(path);session.authority=new IndustrialAuthority(recovered,path);actor=recovered.Actor(1);assembly=recovered.Ship(1);peer.record=recovered.multiplayer.players.Find(player=>player.actor==1);state=recovered.multiplayer.ships[0];session.ResetPilotLeases();
                check(assembly.Module(added.id)!=null&&assembly.wires.Exists(wire=>wire.target==added.id)&&state.collisionRadius==SurvivalShipAuthority.CollisionRadius(assembly),"Restart restores built sections, power connections and derived collision bounds");
                check(Run(new IndustrialCommand{kind="ship-unwire",source=core.id,target=added.id})==null&&Run(new IndustrialCommand{kind="ship-remove",source=added.id})==null,"Power recovery and section recovery use validated journalled commands after restart");
                check(actor.inventory.Count("hull-panel")==24&&actor.inventory.Count("electronics")==16&&actor.inventory.Count("wire")==100&&Math.Abs(actor.inventory.ActivityOf("hull-panel",24)-.25)<1e-9&&Math.Abs(actor.inventory.ActivityOf("wire",100)-.375)<1e-9,"Construction/recovery conserves exact components and resource activity across restart");
                check(Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Camera>().Length==cameras,"Headless ship construction creates no meshes, materials or cameras");
            }finally{session.authority?.Dispose();session.authority=null;DestroyImmediate(go);if(definition!=null)DestroyImmediate(definition);}
        }
    }
}
#endif
