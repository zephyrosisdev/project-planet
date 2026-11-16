using System;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal static class SurvivalClientPresentation {
        internal static bool SameFrame(SurvivalMotion a,SurvivalMotion b)=>a!=null&&b!=null&&a.epoch==b.epoch&&a.pose.body==b.pose.body&&a.pose.frame==b.pose.frame&&a.interior==b.interior&&a.ship?.entity==b.ship?.entity&&a.vehicle?.entity==b.vehicle?.entity;
        internal static bool Adjacent(SurvivalMotion a,SurvivalMotion b)=>a.sequence!=0?b.sequence==a.sequence+1:a.sequence==b.sequence&&a.tick+1==b.tick;
        internal static SurvivalMotion Sample(SurvivalMotion previous,SurvivalMotion current,float fraction,Vector2 pendingLook,Double3 correction,Double3 shipCorrection=default,Quaternion shipRotationCorrection=default){
            var view=current.Copy();if(current.ship!=null&&!current.interior&&current.vehicle==null&&current.ship.pilot==current.actor)pendingLook=Vector2.zero;fraction=Mathf.Clamp01(fraction);
            if(SameFrame(previous,current)&&Adjacent(previous,current)){
                view.pose.x=Mathf.LerpUnclamped(0,1,fraction)*(current.pose.x-previous.pose.x)+previous.pose.x;
                view.pose.y=previous.pose.y+(current.pose.y-previous.pose.y)*fraction;view.pose.z=previous.pose.z+(current.pose.z-previous.pose.z)*fraction;
                if(view.ship!=null){var a=SurvivalVehicleSimulation.Position(previous.ship.pose);var b=SurvivalVehicleSimulation.Position(current.ship.pose);var point=a+(b-a)*fraction;view.ship.pose.x=point.x;view.ship.pose.y=point.y;view.ship.pose.z=point.z;var q=Quaternion.Slerp(SurvivalShipInteriorModel.Rotation(previous.ship),SurvivalShipInteriorModel.Rotation(current.ship),fraction);view.ship.rotationX=q.x;view.ship.rotationY=q.y;view.ship.rotationZ=q.z;view.ship.rotationW=q.w;}
                if(view.vehicle!=null){var a=previous.vehicle.pose;var b=current.vehicle.pose;view.vehicle.pose.x=a.x+(b.x-a.x)*fraction;view.vehicle.pose.y=a.y+(b.y-a.y)*fraction;view.vehicle.pose.z=a.z+(b.z-a.z)*fraction;var q=Quaternion.Slerp(SurvivalVehicleSimulation.Rotation(previous.vehicle),SurvivalVehicleSimulation.Rotation(current.vehicle),fraction);view.vehicle.rotationX=q.x;view.vehicle.rotationY=q.y;view.vehicle.rotationZ=q.z;view.vehicle.rotationW=q.w;}
            }
            view.pose.x+=correction.x;view.pose.y+=correction.y;view.pose.z+=correction.z;
            if(view.vehicle!=null){view.vehicle.pose.x+=correction.x;view.vehicle.pose.y+=correction.y;view.vehicle.pose.z+=correction.z;}
            if(view.ship!=null){view.ship.pose.x+=shipCorrection.x;view.ship.pose.y+=shipCorrection.y;view.ship.pose.z+=shipCorrection.z;if(Quaternion.Dot(shipRotationCorrection,shipRotationCorrection)>.5f){var q=shipRotationCorrection*SurvivalShipInteriorModel.Rotation(view.ship);view.ship.rotationX=q.x;view.ship.rotationY=q.y;view.ship.rotationZ=q.z;view.ship.rotationW=q.w;}}
            if(view.vehicle!=null||view.ship!=null&&!view.interior){view.viewYaw=Mathf.Clamp(view.viewYaw+pendingLook.x,-100,100);view.pitch=Mathf.Clamp(view.pitch-pendingLook.y,-85,85);}
            else{var up=view.interior?Vector3.up:PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(view.pose));var heading=Quaternion.AngleAxis(pendingLook.x,up)*SurvivalMovement.Forward(view);view.forwardX=heading.x;view.forwardY=heading.y;view.forwardZ=heading.z;view.pitch=Mathf.Clamp(view.pitch-pendingLook.y,-85,85);}
            return view;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        SurvivalMotion previousRenderState,visualMotion;Double3 visualCorrection,visualShipCorrection;Quaternion visualShipRotationCorrection=Quaternion.identity;bool snapPresentation;
        internal SurvivalMotion VisualMotion=>visualMotion;
        void ReconcilePresentation(SurvivalMotion before){
            if(!SurvivalClientPresentation.SameFrame(before,predicted)||before.sequence!=predicted.sequence)return;
            float fraction=clientAccumulator*Config.tickRate;
            var oldView=SurvivalClientPresentation.Sample(previousRenderState,before,fraction,Vector2.zero,default);
            if(predicted.sequence>0&&predictionHistory.TryGetValue(predicted.sequence-1,out var prior)&&SurvivalClientPresentation.SameFrame(prior,predicted))previousRenderState=prior.Copy();
            var newView=SurvivalClientPresentation.Sample(previousRenderState,predicted,fraction,Vector2.zero,default);
            visualCorrection+=SurvivalVehicleSimulation.Position(oldView.pose)-SurvivalVehicleSimulation.Position(newView.pose);
            if(before.ship!=null){visualShipCorrection+=SurvivalVehicleSimulation.Position(oldView.ship.pose)-SurvivalVehicleSimulation.Position(newView.ship.pose);visualShipRotationCorrection=(visualShipRotationCorrection*SurvivalShipInteriorModel.Rotation(oldView.ship)*Quaternion.Inverse(SurvivalShipInteriorModel.Rotation(newView.ship))).normalized;}
        }
        void ResetPresentation(){previousRenderState=null;visualCorrection=visualShipCorrection=default;visualShipRotationCorrection=Quaternion.identity;snapPresentation=true;}
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
        static readonly bool baselineArgument=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-client-baseline")>=0;
#endif
        internal bool ClientBaseline {
            get {
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
                return Probe!=null&&baselineArgument;
#else
                return false;
#endif
            }
        }
        void PresentClientFrame(bool blocked){
            if(clientWorld==null)return;
            if(ClientBaseline){visualMotion=predicted;clientWorld.PresentNetworkObserver(predicted,snapPresentation);snapPresentation=false;return;}
            visualCorrection*=Math.Exp(-15*Time.unscaledDeltaTime);
            visualShipCorrection*=Math.Exp(-15*Time.unscaledDeltaTime);visualShipRotationCorrection=Quaternion.Slerp(visualShipRotationCorrection,Quaternion.identity,(float)(1-Math.Exp(-15*Time.unscaledDeltaTime)));
            visualMotion=SurvivalClientPresentation.Sample(previousRenderState,predicted,clientAccumulator*Config.tickRate,blocked?Vector2.zero:pendingLook,visualCorrection,visualShipCorrection,visualShipRotationCorrection);
#if SURVIVAL_GRAPHICS_PROBE
            Probe?.WeatherOrbitView(ref visualMotion);
#endif
            clientWorld.PresentNetworkObserver(visualMotion,snapPresentation,true);snapPresentation=false;
        }
    }
}
