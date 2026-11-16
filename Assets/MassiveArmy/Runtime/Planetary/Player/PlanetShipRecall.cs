using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        bool recallActive;int recallStage;Double3 recallDestination;Quaternion recallRotation;double recallCruiseRadius;float recallSpeed,recallMaxSpeed;
        internal bool RecallPowered {get{assembly?.UpdatePower();return CockpitPowered&&EnginePower>0;}}
        internal bool FindRecallLanding(Double3 near,int candidate,out Double3 destination,out Quaternion rotation){
            var saved=CaptureSimulation();destination=default;rotation=default;
            try{
                var up=PlanetPrecisionPolicy.Direction(near);var east=Vector3.Cross(Math.Abs(up.y)>.9?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);
                double angle=candidate*2.3999632297,radius=140+80*(candidate/16);
                var point=surface.ProjectToSurface(near+new Double3(east)*(Math.Cos(angle)*radius)+new Double3(north)*(Math.Sin(angle)*radius),200);
                var heading=Vector3.ProjectOnPlane((point-near).Float,PlanetPrecisionPolicy.Direction(point)).normalized;
                pose=new SpacePose{position=point,rotation=Quaternion.LookRotation(heading,PlanetPrecisionPolicy.Direction(point))};
                Landed=AutoFlightActive=false;RefreshLandingPreview(true);
                if(!LandingAvailable)return false;destination=LandingPosition;rotation=LandingRotation;return true;
            }finally{RestoreSimulation(saved);}
        }
        internal bool BeginRecall(Double3 destination,Quaternion rotation,float maxSpeed){
            if(!simulationOnly||!RecallPowered||AutoFlightActive)return false;
            EndCruise();StopOrbitalVelocityHold();recallDestination=destination;recallRotation=rotation;recallMaxSpeed=maxSpeed;recallSpeed=0;recallStage=0;
            recallCruiseRadius=Math.Max(pose.position.Magnitude,Math.Max(destination.Magnitude+1000,SimulationDefinition.radius+Math.Max(SimulationDefinition.ReliefBound,SimulationDefinition.oceanHeight)+1000));
            recallActive=AutoFlightActive=true;Landed=false;RotationLocked=false;GearDeployed=true;SetFlightAssistance(true);pose.linearVelocity=default;angularVelocity=pose.angularVelocity=Vector3.zero;LandingStatus="RECALL · CLIMBING";return true;
        }
        internal void CancelRecall(string reason){recallActive=AutoFlightActive=false;pose.linearVelocity=default;angularVelocity=pose.angularVelocity=Vector3.zero;SetFlightAssistance(true);shipFrame.Pose=pose;LandingStatus=reason;}
        void StepRecall(float dt){
            assembly?.UpdatePower();if(!CockpitPowered||EnginePower<=0){CancelRecall("RECALL STOPPED · NO POWER");return;}
            var up=pose.position*(1/pose.position.Magnitude);var targetUp=recallDestination*(1/recallDestination.Magnitude);
            Double3 target=recallStage==0?up*recallCruiseRadius:recallStage==1?targetUp*recallCruiseRadius:recallDestination+targetUp*300;
            double distance=(target-pose.position).Magnitude;
            if(recallStage==1)distance=recallCruiseRadius*Math.Atan2(Vector3.Cross(up.Float,targetUp.Float).magnitude,Math.Max(-1,Math.Min(1,Vector3.Dot(up.Float,targetUp.Float))));
            float acceleration=simulationLimits.assistAcceleration;recallSpeed=Mathf.MoveTowards(recallSpeed,Math.Min(recallMaxSpeed,(float)Math.Sqrt(2*acceleration*distance)),acceleration*dt);
            double step=Math.Min(distance,recallSpeed*dt);bool arrived=distance<=Math.Max(.01,step);
            Double3 next;
            if(arrived)next=target;
            else if(recallStage==1)next=new Double3(Vector3.Slerp(up.Float,targetUp.Float,(float)(step/distance)).normalized)*recallCruiseRadius;
            else next=pose.position+(target-pose.position)*(step/distance);
            var rotation=recallStage==0?pose.rotation:Quaternion.Slerp(pose.rotation,recallRotation,1-Mathf.Exp(-dt));
            bool safe=HullSweep(pose.position,next,pose.rotation)&&HullSweep(pose.position,next,rotation);
            for(int z=-1;z<=1&&safe;z++)for(int x=-1;x<=1;x++)if(FootClearance(next+Double3.Rotate(rotation,new Double3(x*26,-8,z*50)))<-.35){safe=false;break;}
            if(!safe){CancelRecall("RECALL STOPPED · PATH BLOCKED");return;}
            pose.linearVelocity=(next-pose.position)*(1/dt);pose.position=next;pose.rotation=rotation;pose.angularVelocity=Vector3.zero;shipFrame.Pose=pose;
            LandingStatus="RECALL · "+(recallStage==0?"CLIMBING":recallStage==1?"APPROACHING":"DESCENDING");
            if(!arrived)return;recallStage++;recallSpeed=0;
            if(recallStage==3){
                if(!CheckApproach(recallDestination,recallRotation)){CancelRecall("RECALL STOPPED · LANDING AREA BLOCKED");return;}
                BeginAutoFlight(recallDestination,recallRotation,false);
            }
        }
    }
}
