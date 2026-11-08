#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunLeaveChecks(Action<bool,string> check){
            var action=new SurvivalAction{epoch=4};check(ActionMatchesFrame(4,action)&&!ActionMatchesFrame(5,action)&&!ActionMatchesFrame(4,new SurvivalAction{epoch=4,version=99}),"Queued actions are revalidated against the current control epoch and schema after a seat/frame change");
            var go=new GameObject("Reliable leave authority fixture");var session=go.AddComponent<SurvivalNetworkSession>();
            try{
                session.MergePrivate(new SurvivalSnapshot{tick=10});session.MergePrivate(new SurvivalSnapshot{privateDetails=false,tick=20});
                check(session.PrivateStateTick==10,"New core acknowledgements cannot claim that streamed private inventory has caught up");
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};var actor=world.Register(1,new FramePose{body=1,y=10000});long inventory=actor.inventory.Used;
                var record=new SurvivalPlayerRecord{actor=1,identity="leave-fixture",attachedShip=100,controlledShip=100};world.multiplayer.players.Add(record);var ship=new SurvivalShipState{entity=100,owner=1,pilot=1};world.multiplayer.ships.Add(ship);
                var peer=new Peer{record=record,motion=new SurvivalMotion{actor=1,ship=ship}};peer.input.Enqueue(new SurvivalInput{sequence=1,y=1},1,30);peer.actions.Enqueue(new SurvivalAction{sequence=1,kind="use",item="water"});session.peers[7]=peer;session.connectedActors.Add(1);session.authority=new IndustrialAuthority(world);
                session.RetirePeer(7,peer);
                check(!session.peers.ContainsKey(7)&&!session.connectedActors.Contains(1)&&session.retiringConnections.ContainsKey(7)&&peer.input.Next().y==0&&peer.actions.Count==0,"Acknowledged leave revokes the old connection's commands and held inputs before transport teardown");
                check(record.controlledShip==0&&ship.pilot==0&&record.attachedShip==100&&world.multiplayer.players.Count==1&&actor.inventory.Used==inventory,"Acknowledged leave releases pilot authority while preserving the persistent character, ship attachment and inventory");
            }finally{session.authority?.Dispose();session.authority=null;DestroyImmediate(go);}
            go=new GameObject("Transport dependency lifetime fixture");session=go.AddComponent<SurvivalNetworkSession>();
            try{
                session.Setup();var transport=session.transport;var manager=session.manager;
                check(manager.gameObject!=transport.gameObject,"NGO and transport have separate ownership so component destroy order cannot dispose the driver first");
                DestroyImmediate(manager.gameObject);session.manager=null;
                check(transport!=null&&transport.gameObject!=null,"Destroying the NGO manager leaves transport alive through its shutdown callbacks");
            }finally{if(session.manager!=null)DestroyImmediate(session.manager.gameObject);if(session.transport!=null)DestroyImmediate(session.transport.gameObject);session.manager=null;session.transport=null;_ = session.services?.Leave();DestroyImmediate(go);}
        }
    }
}
#endif
