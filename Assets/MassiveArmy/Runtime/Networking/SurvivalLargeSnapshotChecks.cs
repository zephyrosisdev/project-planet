#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.Text;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunLargeSnapshotChecks(Action<bool,string> check){
            if(Active!=null)throw new InvalidOperationException("Large snapshot checks require an isolated session");
            var go=new GameObject("Bounded snapshot fixture");var session=go.AddComponent<SurvivalNetworkSession>();
            try{
                session.Config=new SurvivalServerConfig{packetBytesPerSecond=4096};var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};session.authority=new IndustrialAuthority(world);
                var actor=world.Register(1,new FramePose{body=1,y=10000});actor.inventory.Add("iron-bar",37);var other=world.Register(2,new FramePose{body=1,x=5,y=10000});
                var record=new SurvivalPlayerRecord{actor=1,name="Builder",identity="owner"};var observer=new SurvivalPlayerRecord{actor=2,name="Peer",identity="peer"};world.multiplayer.players.Add(record);world.multiplayer.players.Add(observer);
                session.peers[1]=new Peer{record=record,motion=new SurvivalMotion{actor=1,epoch=1,pose=actor.pose}};session.peers[2]=new Peer{record=observer,motion=new SurvivalMotion{actor=2,epoch=1,pose=other.pose}};
                session.welcome=new SurvivalWelcome{actor=1};
                for(int i=0;i<128;i++)world.respawnPoints.Add(new RespawnPoint{id=1000+i,owner=1,name=new string((char)('A'+i%26),48),definition="sleeping-pod",pose=new FramePose{body=1,x=100+i*4,y=10000}});
                var assembly=ShipModules.Starter(2);while(assembly.modules.Count<64)assembly.modules.Add(new ShipModuleRecord{id=assembly.nextModule++,definition="corridor",parent=1,pose=new FramePose{x=assembly.modules.Count*20}});assembly.revision++;world.ships.Add(assembly);
                world.multiplayer.ships.Add(new SurvivalShipState{entity=700,owner=2,pose=new FramePose{body=1,x=50,y=10000}});
                int frames=0,chunks=0,remoteUpdates=0;bool alwaysBounded=true,partial=false;long lastPrivateTransfer=0;
                void Capture(bool fail=false,bool refill=true){
                    session.clock.Advance(.1,30,()=>{});foreach(var peer in session.peers.Values)peer.tokens=refill?4096:Math.Min(4096,peer.tokens+409.6);
                    session.Publish((id,packet,payload)=>{
                        if(id==1){if(fail)throw new InvalidOperationException("Injected transport failure");frames++;alwaysBounded&=payload.Length+4<=4096;var decoded=JsonUtility.FromJson<SurvivalSnapshot>(Encoding.UTF8.GetString(payload));decoded.NormalizeReferences();if(decoded.chunk!=null){chunks++;lastPrivateTransfer=decoded.chunk.transfer;}partial|=!decoded.privateDetails;remoteUpdates+=decoded.changed.FindAll(entity=>entity.kind=="player").Count;session.Receive(decoded);}
                        return payload.Length+4;
                    });
                }
                bool failed=false;try{Capture(true);}catch(InvalidOperationException){failed=true;}
                check(failed&&session.peers[1].large?.sender.Preview(10).offset==0,"Transport failure does not commit large-state fragment progress");
                for(int i=0;i<1500;i++){
                    actor.pose.x=i*.001;session.peers[1].motion.pose=actor.pose;other.pose.x=5+(i%5)*.1;session.peers[2].motion.pose=other.pose;
                    Capture();
                    if(session.PrivateStateReady&&session.visible.ContainsKey(5606)&&session.peers[1].large==null)break;
                }
                check(frames>2&&chunks>2&&alwaysBounded&&partial,"Oversized private state and public layouts use multiple actually budgeted snapshot frames");
                check(session.privateSnapshot?.respawnPoints.Count==128&&session.latest.self.inventory.Count("iron-bar")==37,"Complete private state reconstructs exact inventory and all respawn metadata");
                check(session.visible.TryGetValue(5606,out var layout)&&JsonUtility.FromJson<ShipAssembly>(layout.data).modules.Count==64,"A 64-module public ship layout completes through bounded chunk delivery");
                check(Math.Abs(session.latest.self.pose.x-actor.pose.x)<1e-8&&session.latest.tick==session.clock.Tick&&remoteUpdates>2,"Movement, core ticks and nearby remote updates continue without rollback while large state streams");
                long transfer=session.peers[1].nextLarge;for(int i=0;i<5;i++)Capture();check(session.peers[1].nextLarge==transfer,"Unchanged oversized private state is not retransmitted after completion");
                var machine=new IndustrialMachine{id=900,owner=1,definition="store",pose=new FramePose{body=1,x=5,y=10000}};for(int i=0;i<250;i++)machine.input.items.Add(new ItemStack("fixture-private-"+i,1));world.machines.Add(machine);
                Capture();check(session.peers[1].large?.target?.id==7202,"An oversized owned machine buffer enters the bounded transfer path");
                var privateTransfer=session.peers[1].large;
                actor.pose.x=40;session.peers[1].motion.pose=actor.pose;Capture();
                check(!ReferenceEquals(privateTransfer,session.peers[1].large)&&(session.peers[1].large==null||session.peers[1].large.access==0),"Leaving private interaction reach immediately cancels an unfinished authorized transfer");
                for(int i=0;i<200&&!session.clientMachines.ContainsKey(900);i++)Capture();
                check(session.clientMachines.TryGetValue(900,out var redacted)&&redacted.input.Used==0&&alwaysBounded,"Budgeted follow-up delivery exposes redacted state without leaking the abandoned private buffer");
                session.ClearLargeSnapshots();check(!session.PrivateStateReady&&session.largeReceiver.BufferedBytes==0,"Disconnect releases reassembly and private caches");
                foreach(var peerState in session.peers.Values){peerState.delivery=new SurvivalInterestDelivery();peerState.visible.Clear();peerState.large=null;peerState.privateSignature=null;peerState.tokens=0;}session.visible.Clear();
                for(int i=0;i<5000;i++){Capture(false,false);if(session.PrivateStateReady&&session.visible.ContainsKey(5606)&&session.peers[1].large==null)break;}
                check(session.PrivateStateReady&&session.visible.ContainsKey(5606)&&session.peers[1].large==null&&alwaysBounded,"Actual 4096-byte/second replenishment completes oversized private state and public layout without core-only starvation");
            }finally{session.authority?.Dispose();session.authority=null;DestroyImmediate(go);}
        }
    }
}
#endif
