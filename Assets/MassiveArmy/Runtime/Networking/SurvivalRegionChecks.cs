#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunRegionChecks(Action<bool,string> check){
            var go=new GameObject("Headless region transition fixture");var session=go.AddComponent<SurvivalNetworkSession>();IndustrialAuthority store=null;
            try{
                session.Config=new SurvivalServerConfig{worldId="region-native",instanceId="native-owner"};session.universe=SurvivalUniverse.Create(session.Config.worldId,session.Config.instanceId,73129,ServerDefinition(73129));session.universe.Advance(123);
                int target=session.universe.Bodies[1].bodyId;foreach(int body in new[]{1,target})session.surfaces.Add(body,session.universe.Surface(body));
                var local=new Double3(0,session.universe.Definition(target).radius+500,0);var absolute=local;var velocity=new Double3(3,4,5);session.universe.Reframe(target,1,ref absolute,ref velocity);
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId=session.Config.worldId,instanceId=session.Config.instanceId}};store=new IndustrialAuthority(world);session.authority=store;var actor=world.Register(77,new FramePose{body=1,x=absolute.x,y=absolute.y,z=absolute.z});
                var ship=new SurvivalShipState{entity=997,owner=77,pose=actor.pose,landed=false,rotationW=1};var physics=new PlanetShipSimulationSnapshot{pose=new SpacePose{position=absolute,linearVelocity=velocity,rotation=Quaternion.identity},assist=false};ship.physics=JsonUtility.ToJson(physics);world.multiplayer.ships.Add(ship);
                var record=new SurvivalPlayerRecord{actor=77,attachedShip=ship.entity,interiorWalking=true,interiorPose=new FramePose{body=1,frame=ship.entity,x=3,y=-3.95,z=-20}};world.multiplayer.players.Add(record);
                var peer=new Peer{record=record,motion=new SurvivalMotion{actor=77,ship=ship,interior=true,epoch=10,pose=record.interiorPose}};peer.actions.Enqueue(new SurvivalAction{epoch=10});peer.input.Enqueue(new SurvivalInput{sequence=1,y=1},0,30);session.peers.Add(1,peer);
                world.respawnPoints.Add(new RespawnPoint{id=9,owner=77,pose=record.interiorPose});world.droppedItems.Add(new DroppedInventory{id=10,pose=record.interiorPose});
                session.TransitionShipRegion(ship);var result=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);
                check(ship.pose.body==target&&(SurvivalVehicleSimulation.Position(ship.pose)-local).Magnitude<.01&&(result.pose.linearVelocity-new Double3(3,4,5)).Magnitude<1e-6,"Server region handoff preserves absolute position and velocity in the destination body's canonical frame");
                check(peer.motion.epoch==11&&peer.actions.Count==0&&peer.input.Next().y==0&&peer.motion.pose.frame==ship.entity&&peer.motion.pose.body==target&&record.interiorPose.x==3,"A region handoff invalidates old inputs and actions while retaining ship-local passenger coordinates");
                check(world.respawnPoints[0].pose.body==target&&world.droppedItems[0].pose.body==target&&world.respawnPoints[0].pose.frame==ship.entity,"Ship-local respawn points and recoverable death containers follow the stable parent into its destination region");
                peer.regionBody=1;session.PublishRegion(1,peer,target);check(peer.regionBody==target&&peer.visible.Count==0,"Region changes restart per-client subscriptions without transferring simulation ownership");
            }finally{session.authority=null;store?.Dispose();session.DisposeUniverse();DestroyImmediate(go);}
        }
    }
}
#endif
