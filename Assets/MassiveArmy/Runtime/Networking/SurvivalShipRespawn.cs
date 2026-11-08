using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        string PlaceShipRespawn(Peer peer,SurvivalAction action){
            var ship=peer.motion.ship;var actor=authority.World.Actor(peer.record.actor);if(!peer.motion.interior||ship==null||ship.owner!=actor.id||action.command==null)return "Place respawn points on the deck of your own ship";
            var pose=action.command.pose;if(!FiniteInterior(pose)||pose.body!=ship.pose.body||pose.frame!=ship.entity||peer.motion.pose.Distance(pose)>12)return "Choose nearby owned deck space";
            var model=Interior(ship,authority.World.Ship(ship.owner));if(!model.contacts.ProbeGround(pose.body,SurvivalVehicleSimulation.Position(pose),Vector3.up,.5f,.5f,out var floor))return "Choose a supported deck";pose.y=floor.position.y+.05;pose.yaw=Mathf.Repeat((float)Math.Round(pose.yaw/90)*90,360);
            var rotation=Quaternion.Euler(0,pose.yaw,0);foreach(float x in new[]{-.75f,.75f})foreach(float z in new[]{-1.25f,1.25f}){var offset=rotation*new Vector3(x,0,z);var at=pose;at.x+=offset.x;at.z+=offset.z;if(!model.contacts.ProbeGround(pose.body,SurvivalVehicleSimulation.Position(at),Vector3.up,.2f,.2f,out var foot)||Math.Abs(foot.position.y-floor.position.y)>.1||!model.contacts.ClearAt(pose.body,SurvivalVehicleSimulation.Position(at),Vector3.up))return "The entire respawn footprint needs clear level deck space";}
            if(!ClearInteriorFeet(ship,model,actor.id,pose))return "Clear the respawn area";
            return authority.PlaceRespawnPoint(actor.id,action.item,pose,SurvivalShipInteriorModel.World(ship,pose));
        }
        string RespawnAboardShip(Peer peer,RespawnPoint point){
            var ship=authority.World.multiplayer.ships.Find(p=>p.entity==point.pose.frame&&p.owner==peer.record.actor);if(ship==null)return "Your respawn ship is unavailable";
            if(!surfaces.ContainsKey(ship.pose.body)||universe!=null&&!universe.Owns(ship.pose.body,Config.instanceId))return "Respawn ship region unavailable";
            var preferred=point.pose;preferred.x+=2;
            if(!ChooseInteriorFeet(ship,peer.record.actor,preferred,out var feet))return "Respawn deck is blocked";
            var world=SurvivalShipInteriorModel.World(ship,feet);string error=authority.UseShipRespawnPoint(peer.record.actor,point.id,ship.entity,feet,world);if(error!=null)return error;
            BeginInterior(peer,ship,feet);peer.motion.vehicleDefinition=null;return null;
        }
        FramePose ClientRespawnPose(RespawnPoint point){
            if(point.pose.frame==0)return point.pose;
            SurvivalShipState ship=predicted?.ship?.entity==point.pose.frame?predicted.ship:null;if(ship==null)clientShips.TryGetValue(point.pose.frame,out ship);return ship==null?new FramePose{body=-1}:SurvivalShipInteriorModel.World(ship,point.pose);
        }
    }
}
