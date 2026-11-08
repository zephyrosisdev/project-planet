using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        sealed class PlayerPresentation {internal SurvivalPlayerProxy proxy;internal FramePose local;internal bool parented;}
        readonly Dictionary<long,PlayerPresentation> clientPlayers=new Dictionary<long,PlayerPresentation>();
        readonly Dictionary<long,(long frame,int body,Double3 point)> playerLocalPositions=new Dictionary<long,(long,int,Double3)>();
        readonly Dictionary<long,(Double3 point,Quaternion rotation)> presentedShips=new Dictionary<long,(Double3,Quaternion)>();
        readonly Dictionary<long,Quaternion> shipPresentationRotations=new Dictionary<long,Quaternion>();
        void ReceivePlayers(SurvivalSnapshot snapshot){
            foreach(long id in snapshot.removed){clientPlayers.Remove(id);playerLocalPositions.Remove(id);}
            foreach(var entity in snapshot.changed)if(entity.kind=="player"){
                var proxy=JsonUtility.FromJson<SurvivalPlayerProxy>(entity.data);var entry=new PlayerPresentation{proxy=proxy};entry.parented=proxy.TryParentPose(out entry.local);entry.local.body=entity.pose.body;clientPlayers[entity.id]=entry;
            }
        }
        void PrepareShipPoses(){
            presentedShips.Clear();float blend=(float)(1-Math.Exp(-15*Time.unscaledDeltaTime));
            foreach(var entity in visible.Values)if(entity.kind=="ship"||entity.kind=="ship-proxy"){
                long id=entity.id/8;bool occupied=predicted?.ship?.entity==id;var state=occupied?predicted.ship:clientShips.GetValueOrDefault(id);if(state==null)continue;
                var point=occupied&&!predicted.interior&&predicted.vehicle==null?clientWorld.NetworkPresentedFeet:SurvivalVehicleSimulation.Position(state.pose);var rotation=ShipRotation(state);
                if(!occupied){if(entityPositions.TryGetValue(entity.id,out var before))point=before+(point-before)*blend;if(shipPresentationRotations.TryGetValue(id,out var previous))rotation=Quaternion.Slerp(previous,rotation,blend);}
                presentedShips[id]=(point,rotation);shipPresentationRotations[id]=rotation;
            }
        }
        bool ParentPlayerPose(long id,out Double3 point,out Vector3 up,out Quaternion rotation){
            point=default;up=Vector3.up;rotation=Quaternion.identity;
            if(!clientPlayers.TryGetValue(id,out var entry)||!entry.parented)return false;
            Double3 centre;Quaternion parent;long frame=entry.proxy.parent*8+(entry.proxy.parentKind=="ship"?5:2);
            if(entry.proxy.parentKind=="ship"){
                if(!presentedShips.TryGetValue(entry.proxy.parent,out var ship))return false;centre=ship.point;parent=ship.rotation;
            }else if(entry.proxy.parentKind=="vehicle"&&clientMachines.TryGetValue(entry.proxy.parent,out var vehicle)){
                centre=predicted?.vehicle?.entity==vehicle.id?SurvivalVehicleSimulation.Position(predicted.vehicle.pose):SurvivalVehicleSimulation.Position(vehicle.pose);
                parent=predicted?.vehicle?.entity==vehicle.id?SurvivalVehicleSimulation.Rotation(predicted.vehicle):Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(centre))*Quaternion.Euler(0,vehicle.pose.yaw,0);
            }else return false;
            var local=SurvivalVehicleSimulation.Position(entry.local);if(playerLocalPositions.TryGetValue(id,out var before)&&before.frame==frame&&before.body==entry.local.body)local=before.point+(local-before.point)*(1-Math.Exp(-15*Time.unscaledDeltaTime));playerLocalPositions[id]=(frame,entry.local.body,local);
            point=centre+Double3.Rotate(parent,local);up=parent*Vector3.up;rotation=parent*Quaternion.Euler(0,entry.local.yaw,0);return true;
        }
        void ClearParentPresentations(){clientPlayers.Clear();playerLocalPositions.Clear();presentedShips.Clear();shipPresentationRotations.Clear();}
    }
}
