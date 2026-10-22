using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        public const double CruiseSpeed=100000000;
        public static string CruiseSpeedReadout=>FormatFlightSpeed(CruiseSpeed);
        public string SpeedReadout=>FormatFlightSpeed(pose.linearVelocity.Magnitude);
        internal static string FormatFlightSpeed(double metresPerSecond)=>Math.Abs(metresPerSecond)>=1000?$"{metresPerSecond/1000:#,##0.##} km/s":$"{metresPerSecond:0.0} m/s";
        public bool CruiseActive {get;private set;}
        Double3 cruiseReturnVelocity;
        internal Double3 NormalFlightVelocity=>CruiseActive?cruiseReturnVelocity:pose.linearVelocity;
        static double CruiseDot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        double CruiseBoundary=>world.definition.radius+Math.Max(world.definition.ReliefBound+6000,world.definition.hasAtmosphere?world.definition.atmosphereHeight:0);
        internal bool CruiseAvailable=>!Landed&&!Docked&&!Docking&&!AutoFlightActive&&pose.position.Magnitude>CruiseBoundary+2;
        // Warp is swept analytically, so its travel time need not use the normal
        // physics step cap. At 10 FPS a 50 ms cap would halve actual travel speed.
        internal float FlightStepDuration(float elapsed,bool cruise,bool brake)=>cruise&&!brake&&CruiseAvailable?elapsed:Mathf.Min(elapsed,.05f);
        void EndCruise(){if(!CruiseActive)return;pose.linearVelocity=cruiseReturnVelocity;CruiseActive=false;shipFrame.Pose=pose;nextOrbitRefresh=0;nextArcRefresh=0;}
        bool IntegrateCruise(float dt,Vector3 turn){
            // Transit is a held speed override, not permanent orbital acceleration.
            // A conservative sphere prevents a long step cutting through the atmosphere
            // or terrain. Existing station hull sweeps still protect orbital structures.
            double boundary=CruiseBoundary;
            if(pose.position.Magnitude<=boundary+2)return false;
            if(!CruiseActive){cruiseReturnVelocity=pose.linearVelocity;CruiseActive=true;nextOrbitRefresh=0;nextArcRefresh=0;}
            if(RotationLocked){angularVelocity=Vector3.zero;pose.rotation=LockedAttitude(pose.position);}
            else {angularVelocity=Vector3.MoveTowards(angularVelocity,turn*35,(FlightAssistEnabled?90:70)*dt);pose.rotation=(pose.rotation*Quaternion.Euler(angularVelocity*dt)).normalized;}
            var velocity=Double3.Rotate(pose.rotation,new Double3(0,0,CruiseSpeed));var delta=velocity*dt;var from=pose.position;
            double length=delta.Magnitude;var direction=delta*(1/length);double along=-CruiseDot(from,direction);
            var side=from+direction*along;double disc=boundary*boundary-CruiseDot(side,side);
            bool stopped=false;var next=from+delta;
            if(disc>=0){double entry=along-Math.Sqrt(disc);if(entry>=0&&entry<=length){next=from+direction*Math.Max(0,entry-1);stopped=true;}}
            var intended=next;StationCollision(from,ref next,ref velocity);
            if((next-intended).Magnitude>.001){CruiseActive=false;}
            pose.position=next;pose.linearVelocity=velocity;
            if(RotationLocked)pose.rotation=LockedAttitude(next);
            pose.angularVelocity=RotationLocked?Double3.Cross(next,velocity).Float/(float)(next.Magnitude*next.Magnitude):pose.rotation*(angularVelocity*Mathf.Deg2Rad);shipFrame.Pose=pose;
            if(stopped)EndCruise();return true;
        }
    }
}
