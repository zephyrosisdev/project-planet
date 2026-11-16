using System;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class SurvivalCampGeneration {
        internal static readonly BuildSolid[] ExclusionVolume={BuildSolid.Box(-260,260,-260,260,-30,30)};
        internal static Quaternion Rotation(FramePose pose){var up=PlanetPrecisionPolicy.Direction(new Double3(pose.x,pose.y,pose.z));var north=Vector3.ProjectOnPlane(Math.Abs(up.y)>.9?Vector3.forward:Vector3.up,up).normalized;return Quaternion.LookRotation(north,up)*Quaternion.Euler(0,pose.yaw,0);}
        internal static Double3 Point(FramePose pose)=>new Double3(pose.x,pose.y,pose.z);
        static FramePose Pose(int body,Double3 point,float yaw=0)=>new FramePose{body=body,x=point.x,y=point.y,z=point.z,yaw=yaw};
        internal static SurvivalCampState Generate(PlanetSurface surface,FramePose near,long id,int seed,Func<Double3,bool> clear=null){
            var center=surface.ProjectToSurface(Point(near),.05);var up=PlanetPrecisionPolicy.Direction(center);var east=Vector3.Cross(Math.Abs(up.y)>.9?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);int phase=(seed^near.body*7919)&1023;
            using(var probe=new PlanetSpacecraft(surface.Definition,ShipModules.Starter(0),(a,b,q)=>true)){
                for(int attempt=0;attempt<48;attempt++){
                    double angle=(attempt+phase)*2.3999632297,radius=1100+160*(attempt/8);var at=surface.ProjectToSurface(center+new Double3(east)*(Math.Cos(angle)*radius)+new Double3(north)*(Math.Sin(angle)*radius),.05);
                    if(surface.SampleHeight(at)<=surface.Definition.oceanHeight+2||clear!=null&&!clear(at))continue;
                    var camp=new SurvivalCampState{id=id,seed=seed,pose=Pose(near.body,at),name="Wayfarer Camp "+near.body};var q=Rotation(camp.pose);bool valid=true;
                    for(int i=0;i<2;i++){var pad=surface.ProjectToSurface(at+Double3.Rotate(q,new Double3(i==0?-90:90,0,0)),.05);if(!probe.ProbePadSite(pad,q,out var landing,out _)){valid=false;break;}camp.pads.Add(new SurvivalCampPad{index=i,pose=Pose(near.body,landing,camp.pose.yaw)});}
                    if(!valid)continue;
                    for(int i=0;i<3;i++){var shop=surface.ProjectToSurface(at+Double3.Rotate(q,new Double3((i-1)*64,0,125)),.05);if(surface.SampleHeight(shop)<=surface.Definition.oceanHeight+2){valid=false;break;}camp.shops.Add(new SurvivalCampShop{index=i,name=i==0?"Survival Supplies":i==1?"Materials Exchange":"Workshop",pose=Pose(near.body,shop,camp.pose.yaw),stock=SurvivalBarterCatalogue.InitialStock(i)});}
                    if(valid&&camp.Valid)return camp;
                }
            }return null;
        }
    }
    public sealed partial class PlanetSpacecraft {
        internal bool ProbePadSite(Double3 center,Quaternion heading,out Double3 destination,out Quaternion rotation){
            var saved=CaptureSimulation();bool available=LandingAvailable;var point=LandingPosition;var turn=LandingRotation;string status=LandingStatus;float refresh=nextLandingRefresh;int revision=landingRevision;destination=default;rotation=default;try{pose=new SpacePose{position=surface.ProjectToSurface(center,200),rotation=heading};Landed=AutoFlightActive=false;RefreshLandingPreview(true);if(!LandingAvailable)return false;destination=LandingPosition;rotation=LandingRotation;return true;}finally{RestoreSimulation(saved);LandingAvailable=available;LandingPosition=point;LandingRotation=turn;LandingStatus=status;nextLandingRefresh=refresh;landingRevision=revision;}
        }
        internal string BeginPadLanding(Double3 center,Quaternion heading){
            if(Landed||AutoFlightActive||CruiseActive||!RecallPowered)return "Use a powered, free-flying ship to approach the pad";
            if((pose.position-center).Magnitude>1000||GroundClearance>LandingMaxAltitude||Speed>LandingMaxSpeed)return "Approach within 1 km, below 600 m clearance and 180 m/s";
            if(!ProbePadSite(center,heading,out var target,out var rotation))return "Pad terrain or approach is no longer suitable";
            if(!CheckApproach(target,rotation))return "Pad approach is blocked; reposition above the pad";
            EndCruise();StopOrbitalVelocityHold();BeginAutoFlight(target,rotation,false);return null;
        }
        internal string BeginPadLaunch(){
            if(!Landed||AutoFlightActive||!RecallPowered)return "Use a powered landed ship to launch";
            var up=Direction;var rotation=Quaternion.FromToRotation(pose.rotation*Vector3.up,up)*pose.rotation;var end=pose.position+new Double3(up)*100-Double3.Rotate(rotation,new Double3(0,0,220));
            if(!HullSweep(pose.position,end,pose.rotation)||OverlapsHull(end,rotation))return "Launch corridor is obstructed";
            BeginAutoFlight(end,rotation,true);return null;
        }
    }
}
