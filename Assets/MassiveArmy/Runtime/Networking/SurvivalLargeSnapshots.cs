using System;
using System.Collections.Generic;
using System.Text;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal sealed class LargeSnapshot {
            internal SurvivalChunkSender sender;internal SurvivalInterestDelivery.Candidate target;
            internal long epoch,access,ship,shipRevision;internal string kind,privateSignature;internal bool privateLarge;
        }
        [Serializable] sealed class PrivateSignatureData {public long actor,nextCraft;public Inventory inventory;public IndustrialCraftJob craft;public List<IndustrialCraftJob> queue;public List<RespawnPoint> respawns;public List<SurvivalShipInvite> invites;public List<SurvivalIncomingInvitation> incoming;}
        static string PrivateSignature(SurvivalSnapshot packet)=>JsonUtility.ToJson(new PrivateSignatureData{actor=packet.self.id,nextCraft=packet.self.nextCraftId,inventory=packet.self.inventory,craft=packet.self.craft,queue=packet.self.craftQueue,respawns=packet.respawnPoints,invites=packet.shipInvites,incoming=packet.incomingInvitations});
        readonly SurvivalChunkReceiver largeReceiver=new SurvivalChunkReceiver();SurvivalSnapshot privateSnapshot;long privateTick=-1;
        internal bool PrivateStateReady=>privateSnapshot!=null;
        internal long PrivateStateTick=>privateTick;
        static IndustrialActor LeanActor(IndustrialActor actor){var copy=actor.CopyState();copy.inventory=new Inventory{capacity=0};copy.craft=null;copy.craftQueue=new List<IndustrialCraftJob>();if(copy.lastCommand?.Length>256)copy.lastCommand=copy.lastCommand.Substring(0,256);return copy;}
        static IndustrialActor MergeActor(IndustrialActor core,IndustrialActor details){
            if(details==null)return core;var copy=details.CopyState();copy.pose=core.pose;copy.health=core.health;copy.dose=core.dose;copy.exposure=core.exposure;copy.protection=core.protection;copy.wearingProtection=core.wearingProtection;copy.collisionRadius=core.collisionRadius;copy.vehicleObstacle=core.vehicleObstacle;copy.vehicleObstacleRadius=core.vehicleObstacleRadius;copy.lastSequence=core.lastSequence;return copy;
        }
        static SurvivalSnapshot Envelope(SurvivalSnapshot source)=>new SurvivalSnapshot{simulationSeconds=source.simulationSeconds,tick=source.tick,motion=source.motion,self=source.self,vitals=source.vitals,pilotedShip=source.pilotedShip,industryTick=source.industryTick,respawnPoints=source.respawnPoints,ownedShip=source.ownedShip,shipInvites=source.shipInvites,incomingInvitations=source.incomingInvitations};
        static SurvivalSnapshot LeanSnapshot(SurvivalSnapshot source)=>new SurvivalSnapshot{privateDetails=false,simulationSeconds=source.simulationSeconds,tick=source.tick,motion=source.motion,self=LeanActor(source.self),vitals=source.vitals,industryTick=source.industryTick,ownedShip=source.ownedShip};
        long LargeAccess(Peer peer,long id,string kind){var actor=authority.World.Actor(peer.record.actor);if(kind=="machine"){var machine=authority.World.Machine(id/8);return machine!=null&&machine.owner==actor.id&&authority.World.InteractionDistance(actor.pose,machine.pose)<=12?actor.id:0;}if(kind=="loot"){var drop=authority.World.Dropped(id/8);return drop!=null&&actor.health>0&&actor.pose.Distance(ResolveNetworkInventoryPose(drop.pose)??drop.resolvedPose)<=12?actor.id:0;}return 0;}
        void RetainLarge(Peer peer,HashSet<long> nearby){var large=peer.large;if(large!=null&&(large.epoch!=peer.motion.epoch||large.target!=null&&(!nearby.Contains(large.target.id)||LargeAccess(peer,large.target.id,large.kind)!=large.access))){peer.cancelTransfer=large.sender.Id;peer.large=null;}}
        bool BeginLarge(Peer peer,SurvivalSnapshot source,SurvivalInterestDelivery.Candidate target,SurvivalEntity entity,int maximum,bool includePrivate=true,string signature=null){
            var bulk=includePrivate?Envelope(source):LeanSnapshot(source);bulk.pilotedShip=source.pilotedShip;if(entity!=null)bulk.changed.Add(entity);
            byte[] bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(bulk));if(bytes.Length>SurvivalChunkSender.MaximumBytes)return false;
            var withoutLayout=Envelope(source);withoutLayout.pilotedShip=null;
            peer.large=new LargeSnapshot{sender=new SurvivalChunkSender(++peer.nextLarge,bytes),target=target,epoch=peer.motion.epoch,kind=entity?.kind,access=entity==null?0:LargeAccess(peer,entity.id,entity.kind),privateLarge=Encoding.UTF8.GetByteCount(JsonUtility.ToJson(withoutLayout))+4>maximum,ship=peer.motion.ship?.entity??0,shipRevision=source.pilotedShip?.revision??-1,privateSignature=includePrivate?signature??PrivateSignature(source):null};return true;
        }
        SurvivalSnapshot StreamingEnvelope(Peer peer,SurvivalSnapshot source){if(peer.large?.privateLarge==true)return LeanSnapshot(source);var packet=Envelope(source);packet.pilotedShip=null;return packet;}
        SurvivalChunk FitChunk(Peer peer,SurvivalSnapshot packet,int maximum){
            if(peer.large==null)return null;
            int count=Math.Min(SurvivalChunkSender.MaximumChunkBytes,Math.Max(0,(maximum-SurvivalSnapshotEncoding.Encode(packet).Length-300)*3/4));
            while(count>0){var chunk=peer.large.sender.Preview(count);packet.chunk=chunk;if(SurvivalSnapshotEncoding.Encode(packet).Length+4<=maximum)return chunk;count/=2;}
            packet.chunk=null;return null;
        }
        void CommitLarge(Peer peer,SurvivalChunk chunk){
            if(chunk==null)return;var large=peer.large;large.sender.Commit(chunk);if(!large.sender.Complete)return;
            if(large.target!=null)peer.delivery.Commit(new[]{large.target});
            if(large.privateSignature!=null)peer.privateSignature=large.privateSignature;
            if(large.ship!=0&&large.shipRevision>=0){peer.privateShip=large.ship;peer.privateShipRevision=large.shipRevision;}peer.large=null;
        }
        void MergePrivate(SurvivalSnapshot packet){
            if(packet.privateDetails){privateSnapshot=packet;privateTick=packet.tick;return;}
            if(privateSnapshot==null)return;
            packet.self=MergeActor(packet.self,privateSnapshot.self);packet.respawnPoints=privateSnapshot.respawnPoints;packet.shipInvites=privateSnapshot.shipInvites;packet.incomingInvitations=privateSnapshot.incomingInvitations;
        }
        void ReceiveLarge(SurvivalChunk chunk){
            byte[] bytes=largeReceiver.Accept(chunk,Time.realtimeSinceStartupAsDouble);if(bytes==null)return;
            var bulk=JsonUtility.FromJson<SurvivalSnapshot>(Encoding.UTF8.GetString(bytes));bulk.NormalizeReferences();
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
            Probe?.LargeSnapshotApplied(bulk);
#endif
            if(bulk.self?.id!=ActorId||bulk.motion?.actor!=ActorId||latest==null||bulk.motion.epoch!=latest.motion.epoch)return;
            if(bulk.privateDetails&&bulk.tick>=privateTick){privateSnapshot=bulk;privateTick=bulk.tick;latest.self=MergeActor(latest.self,bulk.self);latest.respawnPoints=bulk.respawnPoints;latest.shipInvites=bulk.shipInvites;latest.incomingInvitations=bulk.incomingInvitations;}
            if(bulk.pilotedShip!=null&&bulk.motion.ship?.entity==latest.motion.ship?.entity&&(!clientShipAssemblies.TryGetValue(bulk.motion.ship.entity,out var layout)||layout.revision<=bulk.pilotedShip.revision)){latest.pilotedShip=bulk.pilotedShip;clientShipAssemblies[bulk.motion.ship.entity]=bulk.pilotedShip;}
            bulk.pilotedShip=null;bulk.changed.RemoveAll(entity=>visible.TryGetValue(entity.id,out var newer)&&newer.revision>entity.revision);bulk.removed.Clear();
            ReceiveMachineStates(bulk);ReceiveShipStates(bulk);ReceivePlayers(bulk);ReceiveFactoryLinks(bulk);ReceiveBuildings(bulk);ReceiveContacts(bulk);foreach(var entity in bulk.changed)visible[entity.id]=entity;
        }
        void ClearLargeSnapshots(){largeReceiver.Clear();privateSnapshot=null;privateTick=-1;}
    }
}
