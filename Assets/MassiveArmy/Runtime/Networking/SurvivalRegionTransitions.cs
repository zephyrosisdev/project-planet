using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        void PublishRegion(ulong client,Peer peer,int body){
            if(peer.regionBody==body)return;
            if(manager!=null)Send(RegionMessage,client,new SurvivalRegion{definition=JsonUtility.ToJson(surfaces[body].Definition),simulationSeconds=authority.World.multiplayer.simulationSeconds});
            peer.regionBody=body;peer.delivery=new SurvivalInterestDelivery();peer.visible.Clear();peer.nextProxyTick.Clear();peer.privateSignature=null;
            if(peer.large!=null){peer.cancelTransfer=peer.large.sender.Id;peer.large=null;}
        }
        void ChangeClientRegion(SurvivalRegion region){
            if(welcome==null||region?.version!=1||!SurvivalServerConfig.Finite(region.simulationSeconds)||region.simulationSeconds<0)throw new InvalidDataException("Invalid region transition");
            var definition=universe.ApplyDefinition(region.definition);universe.Advance(region.simulationSeconds);welcome.definition=region.definition;
            visible.Clear();ClearEntityViews();DisposeVehicleSimulations();DisposeShipSimulations();ClearLargeSnapshots();unacknowledged.Clear();predictionHistory.Clear();predicted=null;clientAccumulator=0;
            if(HeadlessProbeClient){if(headlessDefinition!=null)Destroy(headlessDefinition);headlessDefinition=Instantiate(definition);}else app.StartNetworkSurvival(region.definition,this);
        }
        static void ReframeRoute(System.Collections.Generic.List<FramePose> route,int body){if(route==null)return;for(int i=0;i<route.Count;i++){var pose=route[i];pose.body=body;route[i]=pose;}}
        void TransitionShipRegion(SurvivalShipState ship){
            if(universe==null||ship.landed||ship.autoFlight)return;
            int target=universe.ClosestRegion(ship.pose.body,SurvivalVehicleSimulation.Position(ship.pose));
            if(target==ship.pose.body||!universe.Owns(target,Config.instanceId)||!surfaces.ContainsKey(target))return;
            int previous=ship.pose.body;var physics=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);
            var position=physics.pose.position;var velocity=physics.cruiseActive?physics.cruiseReturnVelocity:physics.pose.linearVelocity;
            universe.Reframe(previous,target,ref position,ref velocity);physics.pose.position=position;physics.pose.linearVelocity=velocity;physics.autoFlight=physics.cruiseActive=false;physics.cruiseInhibited=true;
            ship.pose=new FramePose{body=target,x=position.x,y=position.y,z=position.z,yaw=ship.pose.yaw};ship.velocityX=velocity.x;ship.velocityY=velocity.y;ship.velocityZ=velocity.z;ship.physics=JsonUtility.ToJson(physics);
            foreach(var player in authority.World.multiplayer.players)if(player.attachedShip==ship.entity)player.interiorPose.body=target;
            foreach(var peer in peers.Values)if(peer.record.attachedShip==ship.entity){peer.motion.pose.body=target;peer.motion.ship=ship;peer.motion.epoch++;peer.input.Clear();peer.actions.Clear();}
            foreach(var machine in authority.World.machines)if(machine.pose.frame==ship.entity){var pose=machine.pose;pose.body=target;machine.pose=pose;ReframeRoute(machine.powerRoute,target);ReframeRoute(machine.waterRoute,target);}
            foreach(var belt in authority.World.belts)if(authority.World.Machine(belt.source)?.pose.frame==ship.entity){ReframeRoute(belt.route,target);belt.path=null;}authority.World.connectionRevision++;
            foreach(var vehicle in authority.World.multiplayer.vehicles)if(vehicle.pose.frame==ship.entity){var pose=vehicle.pose;pose.body=target;vehicle.pose=pose;}
            foreach(var point in authority.World.respawnPoints)if(point.pose.frame==ship.entity){var pose=point.pose;pose.body=target;point.pose=pose;}
            foreach(var drop in authority.World.droppedItems)if(drop.pose.frame==ship.entity){var pose=drop.pose;pose.body=target;drop.pose=pose;}
        }
    }
}
