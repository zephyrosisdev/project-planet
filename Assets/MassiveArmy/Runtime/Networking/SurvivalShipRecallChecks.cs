#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunShipRecallChecks(Action<bool,string> check){
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            var go=new GameObject("Headless ship recall fixture");var session=go.AddComponent<SurvivalNetworkSession>();var definition=ServerDefinition(73129);definition.radius=10000;definition.terrainHeight=0;definition.stampTerrain=false;definition.oceanHeight=-35;
            var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="recall-native"}};var store=new IndustrialAuthority(world);session.authority=store;session.Config=new SurvivalServerConfig{proxyRadius=100};session.surfaces.Add(1,new PlanetSurface(definition));
            try{
                var actor=world.Register(1,new FramePose{body=1,y=10000.05});var record=new SurvivalPlayerRecord{actor=1,identity="recall-owner"};world.multiplayer.players.Add(record);var peer=new Peer{record=record,motion=new SurvivalMotion{actor=1,pose=actor.pose}};session.peers.Add(1,peer);
                var guest=new SurvivalPlayerRecord{actor=2,identity="recall-guest"};world.multiplayer.players.Add(guest);world.Register(2,new FramePose{body=1,y=-10000});var crew=new Peer{record=guest,motion=new SurvivalMotion{actor=2}};
                var layout=ShipModules.Starter(1);world.ships.Add(layout);var ship=new SurvivalShipState{entity=layout.id,owner=1,landed=false,pose=new FramePose{body=1,x=2000,y=12000},physics=JsonUtility.ToJson(new PlanetShipSimulationSnapshot{pose=new SpacePose{position=new Double3(2000,12000,0),rotation=Quaternion.identity},orbitHold=true})};world.multiplayer.ships.Add(ship);
                check(session.RecallShip(crew,ship.entity)!=null,"Only the authenticated owner can call a ship");
                guest.attachedShip=ship.entity;check(session.RecallShip(peer,ship.entity)!=null,"Disconnected passengers also prevent remote ship recall");guest.attachedShip=0;
                ship.dockedStation=99;check(session.RecallShip(peer,ship.entity)!=null,"Recall cannot steal a ship from a station berth");ship.dockedStation=0;
                ship.pose.body=2;check(session.RecallShip(peer,ship.entity)!=null,"Recall rejects cross-planet travel");ship.pose.body=1;
                actor.pose.y+=20;check(session.RecallShip(peer,ship.entity)!=null,"An airborne owner cannot request a surface landing");actor.pose.y-=20;
                var start=ship.pose;check(session.RecallShip(peer,ship.entity)==null&&ship.recalling&&ship.autoFlight&&!ship.landed&&ship.pose.Distance(start)==0,"Recall plans a server-selected landing without teleporting the ship");
                check(session.RecallShip(peer,ship.entity)!=null&&session.CancelShipRecall(crew,ship.entity)!=null,"Repeated recalls and non-owner cancellation are rejected");
                var planned=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);check(!planned.orbitHold&&!planned.rotationLocked&&planned.recallActive&&planned.recallDestination.Magnitude>10008,"Recall releases orbital hold and persists a supported landing destination");
                var caller=actor.pose;actor.pose=new FramePose{body=1,x=planned.recallDestination.x,y=planned.recallDestination.y,z=planned.recallDestination.z};check(!session.RecallPathClear(ship,planned.recallDestination,planned.recallDestination,0),"Players entering the landing footprint block recall instead of being crushed");actor.pose=caller;
                double maxStep=0;bool restarted=false;
                for(int i=0;i<6000&&ship.recalling;i++){
                    var before=ship.pose;session.simulationStep++;session.StepServerShips(1f/30);maxStep=Math.Max(maxStep,ship.pose.Distance(before));
                    if(i==50){var restored=IndustrialAuthority.Restore(IndustrialAuthority.Snapshot(world));check(restored.multiplayer.ships[0].recalling&&JsonUtility.FromJson<PlanetShipSimulationSnapshot>(restored.multiplayer.ships[0].physics).recallActive,"Checkpoint recovery includes an in-flight recall route");session.DisposeShipSimulations();restarted=true;}
                }
                check(restarted&&ship.landed&&!ship.recalling&&!ship.autoFlight&&maxStep<=session.Config.shipRecallSpeed/30+.1,"Recall continues beyond AOI and across simulation reconstruction, then lands with bounded per-tick travel");
                check(ship.pose.Distance(actor.pose)>100&&ship.pose.Distance(actor.pose)<800,"Recall lands near the caller with room for the hull and boarding ramp");
                check((SurvivalVehicleSimulation.Position(ship.pose)-planned.recallDestination).Magnitude<.01,"Recall finishes at its validated canonical landing destination");
                var shipSimulation=session.GetShipSimulation(ship,layout,definition);ship.landed=false;var airborne=shipSimulation.ship.CaptureSimulation();airborne.landed=false;airborne.pose.position=new Double3(1000,12000,0);shipSimulation.ship.RestoreSimulation(airborne);check(shipSimulation.ship.BeginRecall(planned.recallDestination,planned.recallRotation,1000),"A new recall can be planned after arrival");CaptureShip(ship,shipSimulation);
                check(session.CancelShipRecall(peer,ship.entity)==null&&!ship.recalling&&!ship.autoFlight,"Owner cancellation clears the persisted flight job");
                using(var blocked=new PlanetSpacecraft(definition,layout,(a,b,q)=>false)){blocked.RestoreSimulation(airborne);blocked.BeginRecall(planned.recallDestination,planned.recallRotation,1000);blocked.StepSimulation(1f/30,Vector3.zero,Vector3.zero,false,false,false,false,false);var stopped=blocked.CaptureSimulation();check(!stopped.recallActive&&!stopped.autoFlight&&(stopped.pose.position-airborne.pose.position).Magnitude==0,"A blocked recall stops at its last safe pose");}
                check(meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&cameras==Resources.FindObjectsOfTypeAll<Camera>().Length,"Recall authority creates no render resources");
            }finally{session.DisposeShipSimulations();session.authority=null;store.Dispose();DestroyImmediate(go);DestroyImmediate(definition);}
        }
    }
}
#endif
