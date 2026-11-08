using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        static FramePose VehicleSeatLocal(SurvivalVehicleState vehicle){var pose=vehicle.pose;var offset=Double3.Rotate(SurvivalVehicleSimulation.Rotation(vehicle),new Double3(0,1.3,1.1));pose.x+=offset.x;pose.y+=offset.y;pose.z+=offset.z;return pose;}
        bool FindShipDriverExit(SurvivalVehicleState vehicle,long actor,out FramePose exit){
            exit=default;var ship=authority.World.multiplayer.ships.Find(s=>s.entity==vehicle.pose.frame&&s.pose.body==vehicle.pose.body);if(ship==null)return false;
            foreach(var offset in new[]{Vector3.right*4,Vector3.left*4,Vector3.back*6,Vector3.right*6,Vector3.left*6}){
                var point=SurvivalVehicleSimulation.Position(vehicle.pose)+Double3.Rotate(SurvivalVehicleSimulation.Rotation(vehicle),new Double3(offset));var preferred=new FramePose{body=vehicle.pose.body,frame=ship.entity,x=point.x,y=point.y,z=point.z};
                if(ChooseInteriorFeet(ship,actor,preferred,out exit)&&exit.Distance(vehicle.pose)<=12)return true;
            }return false;
        }
    }
}
