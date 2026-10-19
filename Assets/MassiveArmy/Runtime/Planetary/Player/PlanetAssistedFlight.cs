using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        public bool FlightAssistEnabled {get;private set;}=true;
        double heldClearance=88;bool wasTerrainFlight;
        public double GroundClearance=>pose.position.Magnitude-world.definition.radius-Math.Max(surface.SampleHeight(Direction),world.definition.oceanHeight);
        public void SetFlightAssistance(bool enabled){if(AutoFlightActive)return;FlightAssistEnabled=enabled;nextLandingRefresh=0;if(enabled){RotationLocked=false;heldClearance=Math.Max(60,GroundClearance);wasTerrainFlight=GroundClearance<6000;}nextOrbitRefresh=0;}
        void IntegrateAssisted(float dt,Vector3 thrust,Vector3 turn,bool brake,bool boost){
            var p=pose.position;var up=p.Float.normalized;bool lowFlight=GroundClearance<6000;if(lowFlight&&!wasTerrainFlight)heldClearance=Math.Max(60,GroundClearance);wasTerrainFlight=lowFlight;
            angularVelocity=Vector3.MoveTowards(angularVelocity,turn*35,90*dt);pose.rotation=(pose.rotation*Quaternion.Euler(angularVelocity*dt)).normalized;
            if(lowFlight&&turn.sqrMagnitude<.001f){var f=pose.rotation*Vector3.forward;var upright=Vector3.ProjectOnPlane(up,f);if(upright.sqrMagnitude>.01f)pose.rotation=Quaternion.Slerp(pose.rotation,Quaternion.LookRotation(f,upright),1-Mathf.Exp(-2*dt));}
            float speed=boost?360:120;Vector3 desired;
            if(lowFlight){var forward=pose.rotation*Vector3.forward;var tangent=Vector3.ProjectOnPlane(forward,up).normalized;var right=Vector3.ProjectOnPlane(pose.rotation*Vector3.right,up).normalized;
                float climb=brake?0:thrust.y*60+Vector3.Dot(forward,up)*thrust.z*speed;heldClearance=Math.Max(60,heldClearance+climb*dt);
                desired=brake?Vector3.zero:tangent*thrust.z*speed+right*thrust.x*60;
                var ahead=(p+new Double3(desired)*.75).Float.normalized;float terrain=Mathf.Max(Mathf.Max(surface.SampleHeight(up),surface.SampleHeight(ahead)),world.definition.oceanHeight);
                double error=world.definition.radius+terrain+heldClearance-p.Magnitude;desired+=up*Mathf.Clamp((float)error*2,-100,100);
            }else desired=brake?Vector3.zero:pose.rotation*Vector3.ClampMagnitude(thrust,1)*speed;
            if(!lowFlight&&world.Orbitals!=null){var station=world.Orbitals.Nearest(p,out double range);if(station!=null&&range<20000)desired+=station.Pose.linearVelocity.Float;}
            var velocity=pose.linearVelocity+new Double3(Vector3.ClampMagnitude(desired-pose.linearVelocity.Float,(brake?160:80)*dt));var next=p+velocity*dt;var nextUp=next.Float.normalized;
            double floor=world.definition.radius+Math.Max(surface.SampleHeight(nextUp),world.definition.oceanHeight)+(lowFlight?60:8);
            if(next.Magnitude<floor){next=new Double3(nextUp)*floor;float inward=Vector3.Dot(velocity.Float,nextUp);if(inward<0)velocity-=new Double3(nextUp)*inward;}
            if(lowFlight)pose.rotation=Quaternion.FromToRotation(up,nextUp)*pose.rotation;
            StationCollision(p,ref next,ref velocity);pose.position=next;pose.linearVelocity=velocity;pose.angularVelocity=pose.rotation*(angularVelocity*Mathf.Deg2Rad);shipFrame.Pose=pose;
        }
    }
}
