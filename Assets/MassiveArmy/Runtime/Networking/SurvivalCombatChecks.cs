#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunCombatChecks(Action<bool,string> check){
            var contacts=new SurvivalContacts();var origin=new Double3(0,10000,0);
            contacts.Put(new SurvivalContacts.Shape{id=8,body=1,origin=origin,rotation=Quaternion.identity,radius=5,solids=new[]{BuildSolid.Box(-2,2,-.01f,.01f,0,3)}});
            check(contacts.RayBlocked(1,origin+new Double3(0,1,-4000),origin+new Double3(0,1,4000))&&!contacts.RayBlocked(2,origin+new Double3(0,1,-2),origin+new Double3(0,1,2)),"Canonical weapon rays traverse long cell sequences and respect planetary isolation");
            check(!contacts.RayBlocked(1,origin+new Double3(0,4,-2),origin+new Double3(0,4,2))&&contacts.RayBlocked(1,origin+new Double3(0,1,2),origin+new Double3(0,1,-2)),"Thin-wall ray obstruction works in both directions and allows shots above geometry");contacts.Clear();
            contacts.Put(new SurvivalContacts.Shape{id=12,body=1,origin=origin,rotation=Quaternion.identity,radius=5,solids=Array.Empty<BuildSolid>(),ramps=new[]{new SurvivalContacts.Ramp(-1,1,-1,1,0,2)}});
            check(!contacts.RayBlocked(1,origin+new Double3(-2,1.8,-.8),origin+new Double3(2,1.8,-.8))&&contacts.RayBlocked(1,origin+new Double3(-2,.1,-.8),origin+new Double3(2,.1,-.8)),"Weapon ramp queries clip the inclined solid rather than its bounding box");
            double hit=SurvivalWeaponGeometry.Capsule(new Double3(0,1.65,-5),new Double3(0,0,1),default,Vector3.up);
            check(hit>4.6&&hit<5&&double.IsPositiveInfinity(SurvivalWeaponGeometry.Capsule(new Double3(1,1,-5),new Double3(0,0,1),default,Vector3.up)),"Authoritative player capsule hits use the server ray and reject a parallel miss");
            const string path="TestResults/Multiplayer/combat-native.xml";foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(path+suffix))File.Delete(path+suffix);
            var go=new GameObject("Server weapon authority fixture");var session=go.AddComponent<SurvivalNetworkSession>();var definition=ScriptableObject.CreateInstance<PlanetDefinition>();IndustrialAuthority authority=null;
            try{
                definition.bodyId=1;definition.radius=10000;definition.terrainHeight=0;definition.stampTerrain=false;
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};var actor=world.Register(1,new FramePose{body=1,y=10000.05});var target=world.Register(2,new FramePose{body=1,y=10000.05,z=5});
                actor.inventory=new Inventory{slotLimit=40,activeSlot=0};actor.inventory.AddAt(0,"service-pistol#native",1,0);actor.inventory.Add("ammo-9mm",3);
                var player=new SurvivalPlayerRecord{actor=1,identity="weapon-owner"};world.multiplayer.players.Add(player);world.multiplayer.players.Add(new SurvivalPlayerRecord{actor=2,identity="weapon-target"});
                authority=new IndustrialAuthority(world,path);authority.Save();session.authority=authority;session.Config=new SurvivalServerConfig();session.surfaces[1]=new PlanetSurface(definition);
                var peer=new Peer{record=player,newestAction=200,motion=new SurvivalMotion{actor=1,pose=actor.pose,forwardZ=1}};session.peers[1]=peer;session.peers[2]=new Peer{record=world.multiplayer.players[1],motion=new SurvivalMotion{actor=2,pose=target.pose}};
                check(session.FireWeapon(peer,1)==null&&actor.inventory.Count("ammo-9mm")==2&&target.health==72&&actor.playerCombatSequence==1,"Server aim resolves the nearest player and atomically consumes catalogue ammunition and damage using the processed action sequence");
                check(session.FireWeapon(peer,1)!=null&&session.FireWeapon(peer,2)!=null&&actor.inventory.Count("ammo-9mm")==2&&target.health==72,"Replayed shots and extra packets inside the server cooldown cannot spend or damage again");
                authority.Dispose();authority=null;var recovered=IndustrialAuthority.Load(path);
                check(recovered.Actor(1).inventory.Count("ammo-9mm")==2&&recovered.Actor(2).health==72&&recovered.multiplayer.players[0].nextAttackTick>0,"Crash recovery restores ammunition, damage and cooldown from the same journal entry before its next checkpoint");
                world.multiplayer.serverTick=100;session.serverContacts.Put(new SurvivalContacts.Shape{id=80,body=1,origin=origin+new Double3(0,0,2.5),rotation=Quaternion.identity,radius=4,solids=new[]{BuildSolid.Box(-2,2,-.01f,.01f,0,3)}});
                // Continue using a writer after the simulated crash/reload check.
                authority=new IndustrialAuthority(world,path);session.authority=authority;
                check(session.FireWeapon(peer,2)==null&&target.health==72&&actor.inventory.Count("ammo-9mm")==1,"An authoritative constructed wall blocks damage while the fired shot still consumes ammunition");
                world.multiplayer.serverTick=200;peer.motion.forwardX=1;peer.motion.forwardZ=0;
                check(session.FireWeapon(peer,3)==null&&target.health==72&&actor.inventory.Count("ammo-9mm")==0,"A client action cannot choose its victim: a server-aimed miss still consumes the shot");
                world.multiplayer.serverTick=300;check(session.FireWeapon(peer,4)!=null&&target.health==72,"An empty weapon cannot damage a player");
            }finally{session.authority=null;authority?.Dispose();session.serverContacts.Clear();DestroyImmediate(go);DestroyImmediate(definition);}
        }
    }
}
#endif
