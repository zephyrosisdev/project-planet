using MassiveArmy.Networking;
using UnityEngine;

namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        // Set before Start: a network client never instantiates the offline authority/save adapters.
        public bool NetworkPresentation;
        Double3 networkFeet;bool networkViewReady;
        internal Double3 NetworkPresentedFeet=>networkFeet;
        internal void PresentNetworkObserver(SurvivalMotion state,bool snap){
            if(!Ready||player==null||state.pose.body!=definition.bodyId)return;
            if(state.vehicle!=null&&state.ship!=null&&state.vehicle.pose.frame==state.ship.entity){
                var vehicle=SurvivalShipInteriorModel.World(state.ship,state.vehicle.pose);networkFeet=new Double3(vehicle.x,vehicle.y,vehicle.z);networkViewReady=true;player.ApplyNetworkVehicleView(networkFeet,SurvivalShipInteriorModel.Rotation(state.ship)*SurvivalVehicleSimulation.Rotation(state.vehicle),state.vehicleDefinition,state.pitch,state.viewYaw);return;
            }
            if(state.interior&&state.ship!=null){
                var feet=SurvivalShipInteriorModel.World(state.ship,state.pose);networkFeet=new Double3(feet.x,feet.y,feet.z);networkViewReady=true;
                player.ApplyNetworkInteriorView(networkFeet,SurvivalShipInteriorModel.Rotation(state.ship),SurvivalMovement.Forward(state),state.pitch);return;
            }
            var target=new Double3(state.pose.x,state.pose.y,state.pose.z);
            if(snap||!networkViewReady){networkFeet=target;networkViewReady=true;}
            else networkFeet+= (target-networkFeet)*(1-System.Math.Exp(-25*Time.unscaledDeltaTime));
            if(state.ship!=null)player.ApplyNetworkShipView(networkFeet,new Quaternion(state.ship.rotationX,state.ship.rotationY,state.ship.rotationZ,state.ship.rotationW),SurvivalNetworkSession.Active?.Latest?.pilotedShip,SurvivalNetworkSession.Active?.Latest?.vitals.shipSeat??-1,state.pitch,state.viewYaw);
            else if(state.vehicle!=null)player.ApplyNetworkVehicleView(networkFeet,SurvivalVehicleSimulation.Rotation(state.vehicle),state.vehicleDefinition,state.pitch,state.viewYaw);
            else player.ApplyNetworkView(networkFeet,SurvivalMovement.Forward(state),state.pitch);
        }
    }
    public sealed partial class PlanetFirstPersonController {
        string networkCameraDefinition;VehicleCameraSettings networkVehicleCamera;
        internal void ApplyNetworkInteriorView(Double3 feet,Quaternion shipRotation,Vector3 localHeading,float viewPitch){
            station=null;attached=false;position=feet;forward=shipRotation*localHeading;pitch=viewPitch;var up=shipRotation*Vector3.up;
            origin.Anchor(surface.Definition.position.Add(feet+new Double3(up)*EyeHeight));body.position=origin.RenderPosition(surface.Definition.position.Add(feet));body.rotation=Quaternion.LookRotation(forward,up);
            camera.transform.SetPositionAndRotation(Vector3.zero,Quaternion.LookRotation(forward,up)*Quaternion.Euler(viewPitch,0,0));VehicleCameraSettings.ApplyOrDefault(null,camera,feet.Magnitude,.1f);
        }
        internal void ApplyNetworkShipView(Double3 ship,Quaternion rotation,MassiveArmy.Industrial.ShipAssembly modules,int seat,float viewPitch,float viewYaw){
            var cockpit=modules?.modules.Find(m=>m.definition=="cockpit");var localRotation=cockpit==null?Quaternion.identity:Quaternion.Euler(0,cockpit.pose.yaw,0);
            var centre=cockpit==null?new Vector3(0,-1,30):new Vector3((float)cockpit.pose.x,(float)cockpit.pose.y,(float)cockpit.pose.z);
            var eye=centre+localRotation*new Vector3(0,-.5f,1);var feet=centre+localRotation*new Vector3(0,-1.7f,0);
            if(seat>=0){var crew=MassiveArmy.Industrial.SurvivalShipAuthority.SeatFeet(modules,seat);feet=new Vector3((float)crew.x,(float)crew.y,(float)crew.z);eye=feet+Vector3.up*1.7f;localRotation=Quaternion.identity;}
            station=null;attached=false;position=ship+Double3.Rotate(rotation,new Double3(feet));forward=rotation*localRotation*Vector3.forward;pitch=viewPitch;
            origin.Anchor(surface.Definition.position.Add(ship+Double3.Rotate(rotation,new Double3(eye))));body.position=origin.RenderPosition(surface.Definition.position.Add(position));body.rotation=rotation*localRotation;
            camera.transform.SetPositionAndRotation(Vector3.zero,rotation*localRotation*Quaternion.Euler(viewPitch,viewYaw,0));
            if(networkCameraDefinition!="SpaceshipPilot"){networkCameraDefinition="SpaceshipPilot";networkVehicleCamera=Resources.Load<VehicleCameraSettings>("MassiveArmy/VehicleCameras/SpaceshipPilot");}
            VehicleCameraSettings.ApplyOrDefault(networkVehicleCamera,camera,ship.Magnitude,.1f);
        }
        internal void ApplyNetworkVehicleView(Double3 vehicle,Quaternion rotation,string definition,float viewPitch,float viewYaw){
            station=null;attached=false;position=vehicle+Double3.Rotate(rotation,new Double3(0,1.3,1.1));forward=rotation*Vector3.forward;pitch=viewPitch;
            var seat=definition=="truck"?new Vector3(0,2.3f,2.22f):new Vector3(definition=="excavator"?-.55f:0,2.3f,1.22f);
            origin.Anchor(surface.Definition.position.Add(vehicle+Double3.Rotate(rotation,new Double3(seat))));
            body.position=origin.RenderPosition(surface.Definition.position.Add(position));body.rotation=rotation;
            camera.transform.SetPositionAndRotation(Vector3.zero,rotation*Quaternion.Euler(viewPitch,viewYaw,0));
            if(networkCameraDefinition!=definition){networkCameraDefinition=definition;networkVehicleCamera=Resources.Load<VehicleCameraSettings>("MassiveArmy/VehicleCameras/"+definition);}
            VehicleCameraSettings.ApplyOrDefault(networkVehicleCamera,camera,vehicle.Magnitude);
        }
        internal void ApplyNetworkView(Double3 feet,Vector3 heading,float viewPitch){
            station=null;attached=false;position=feet;forward=heading;pitch=viewPitch;
            if(!active){CaptureCursor=true;SetActive(true);}PresentPlanet();
        }
    }
}
