#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Text;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        // Offline serialized-packet fixture. Production publication supplies NGO's send callback.
        internal static void RunInterestChecks(Action<bool,string> check){
            if(Active!=null)throw new InvalidOperationException("Interest checks require no active session");
            var go=new GameObject("Serialized interest fixture");var session=go.AddComponent<SurvivalNetworkSession>();
            try{
                session.Config=new SurvivalServerConfig{surfaceRadius=50,shipRadius=200,proxyRadius=500,hysteresis=10};session.clock.Restore(10);
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="interest-fixture"}};session.authority=new IndustrialAuthority(world);
                for(int i=1;i<=4;i++){
                    var pose=new FramePose{body=i==4?2:1,x=i==2?20:i==3?400:0,y=10000};var actor=world.Register(i,pose);
                    var record=new SurvivalPlayerRecord{actor=i,identity="fixture-"+i,name="Player "+i};world.multiplayer.players.Add(record);
                    session.peers.Add((ulong)i,new Peer{record=record,motion=new SurvivalMotion{actor=i,name=record.name,pose=pose}});
                }
                var foreign=new IndustrialMachine{id=500,owner=2,definition="store",pose=new FramePose{body=1,x=5,y=10000}};foreign.input.Add("copper-bar",17);world.machines.Add(foreign);
                var owned=new IndustrialMachine{id=501,owner=1,definition="store",pose=new FramePose{body=1,x=5,y=10000}};owned.input.Add("iron-bar",9);world.machines.Add(owned);
                var point=new RespawnPoint{id=800,owner=1,definition="sleeping-bag",pose=new FramePose{body=1,x=25,y=10000}};world.respawnPoints.Add(point);
                var ship=new SurvivalShipState{entity=700,owner=2,pose=new FramePose{body=1,x=180,y=10000},physics="PRIVATE-PHYSICS"};ship.invitedActors.Add(12345);
                world.multiplayer.ships.Add(ship);world.ships.Add(new ShipAssembly{id=700,owner=2});
                var distant=new SurvivalShipState{entity=701,owner=3,pose=new FramePose{body=1,x=300,y=10000},physics="PRIVATE-DISTANT"};world.multiplayer.ships.Add(distant);world.ships.Add(new ShipAssembly{id=701,owner=3});
                SurvivalSnapshot Capture(bool refill=true){
                    if(refill)foreach(var peer in session.peers.Values)peer.tokens=65536;
                    session.clock.Advance(1.0/30,30,()=>{});SurvivalSnapshot captured=null;
                    session.Publish((client,packet,payload)=>{if(client==1)captured=JsonUtility.FromJson<SurvivalSnapshot>(Encoding.UTF8.GetString(payload));return payload.Length+4;});return captured;
                }
                var first=Capture();
                check(first.changed.Exists(e=>e.id==17)&&!first.changed.Exists(e=>e.id==25||e.id==33),"Published packets filter distant surface players and other planetary bodies");
                var foreignWire=JsonUtility.FromJson<IndustrialMachine>(first.changed.Find(e=>e.id==4002).data);var ownedWire=JsonUtility.FromJson<IndustrialMachine>(first.changed.Find(e=>e.id==4010).data);
                check(foreignWire.input.Used==0&&ownedWire.input.Count("iron-bar")==9,"Published machine inventories are private to an owner in interaction reach");
                string playerWire=first.changed.Find(e=>e.id==17).data;
                check(!playerWire.Contains("inventory")&&!playerWire.Contains("identity"),"Remote player wire data excludes backpack and authenticated identity records");
                var detailed=first.changed.Find(e=>e.id==5605);var coarse=first.changed.Find(e=>e.id==5613);
                var layoutFrame=first.changed.Find(e=>e.id==5606).pose;check(layoutFrame.body==1&&layoutFrame.frame==700&&layoutFrame.x==0&&layoutFrame.y==0&&layoutFrame.z==0,"Ship layout geometry is transmitted in its stable parent frame");
                check(detailed.kind=="ship"&&first.changed.Exists(e=>e.id==5606)&&!detailed.data.Contains("PRIVATE-PHYSICS")&&!detailed.data.Contains("12345"),"Detailed ship entry includes its layout without physics or invitation secrets");
                check(coarse.kind=="ship-proxy"&&!coarse.data.Contains("physics")&&!coarse.data.Contains("owner")&&!first.changed.Exists(e=>e.id==5614),"Distant ship wire data contains only a coarse presentation proxy");
                owned.pose.x=30;point.pose.x=26;var moved=Capture();
                check(JsonUtility.FromJson<IndustrialMachine>(moved.changed.Find(e=>e.id==4010).data).input.Used==0,"Moving an owned machine outside interaction reach publishes redacted inventory");
                check(moved.changed.Exists(e=>e.id==6407&&e.pose.x==26&&e.version==1&&e.revision>first.tick),"Pose-only changes produce a versioned canonical delta");
                world.Actor(2).pose.x=55;check(!Capture().removed.Contains(17),"Surface subscriptions retain players inside the exit hysteresis band");
                world.Actor(2).pose.x=70;session.peers[1].tokens=0;check(Capture(false)==null,"A token-starved snapshot is not passed to the transport callback");
                check(Capture().removed.Contains(17),"A surface removal survives a token-starved publication and is sent on retry");
                world.Actor(2).pose.x=20;check(Capture().changed.Exists(e=>e.id==17),"Surface re-entry restores the full serialized player state");
                ship.pose.x=205;var retainedLayout=Capture();check(!retainedLayout.removed.Contains(5606),"Detailed ship layouts survive the detailed-range hysteresis band");
                check(!retainedLayout.changed.Exists(e=>e.id==5606),"Moving a ship does not retransmit unchanged parent-local layout geometry");
                ship.pose.x=211;var downgrade=Capture();check(downgrade.removed.Contains(5606)&&downgrade.changed.Exists(e=>e.id==5605&&e.kind=="ship-proxy"),"Leaving detailed ship interest removes layout and replaces it with a coarse proxy");
                ship.pose.x=505;check(!Capture().removed.Contains(5605),"Coarse ships survive the proxy-range hysteresis band");
                ship.pose.x=511;check(Capture().removed.Contains(5605),"Leaving proxy interest publishes ship removal");
                ship.pose.x=490;check(Capture().changed.Exists(e=>e.id==5605&&e.kind=="ship-proxy"),"Ship proxy re-entry restores state without a stale proxy throttle");
                for(int i=0;i<300;i++){long id=1000000000000000000L+i;var entry=session.peers[1].delivery.Stage(id,"old",session.clock.Tick,2,0,1);session.peers[1].delivery.Commit(new[]{entry});}
                session.peers[1].tokens=4096;var smallBatch=Capture(false);
                check(smallBatch!=null&&smallBatch.removed.Count>0&&smallBatch.removed.Count<256&&Encoding.UTF8.GetByteCount(JsonUtility.ToJson(smallBatch))+4<=4096,"Large-ID boundary removals make bounded progress even with only 4096 available bytes");
            }finally{session.authority?.Dispose();session.authority=null;DestroyImmediate(go);}
        }
    }
}

#endif
