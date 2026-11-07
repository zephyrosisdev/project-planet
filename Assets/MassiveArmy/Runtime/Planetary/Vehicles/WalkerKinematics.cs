using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>Analytic, allocation-free two-link mechanical IK. Coordinates are relative to the observer/vehicle, never astronomical floats.</summary>
    public static class WalkerKinematics
    {
        public static float Reach(WalkerLegDefinition leg)=>Mathf.Min(leg.maxReach,Mathf.Sqrt(leg.upperLength*leg.upperLength+leg.lowerLength*leg.lowerLength+2*leg.upperLength*leg.lowerLength*Mathf.Cos(leg.minimumKneeAngle*Mathf.Deg2Rad)));
        public static float WalkingSpeed(VehicleDefinition definition){
            float speed=definition.maximumSpeed;
            foreach(var leg in definition.legs){float reach=Reach(leg),height=leg.hip.y-leg.stance.y,lateral=leg.hip.x-leg.stance.x;
                float travel=Mathf.Sqrt(Mathf.Max(.01f,reach*reach-height*height-lateral*lateral))*.65f;
                speed=Mathf.Min(speed,Mathf.Min(leg.stride,travel)/(Mathf.Max(.1f,leg.stepDuration)*2));}
            return speed;
        }
        public static bool Solve(Vector3 hip,Vector3 target,Vector3 pole,Vector3 up,WalkerLegDefinition leg,out Vector3 knee,out Vector3 ankle){
            float a=leg.upperLength,b=leg.lowerLength;var delta=target-hip;float wanted=delta.magnitude;
            var direction=wanted>.0001f?delta/wanted:-up;
            float Reach(float bend)=>Mathf.Sqrt(Mathf.Max(.0001f,a*a+b*b+2*a*b*Mathf.Cos(bend*Mathf.Deg2Rad)));
            float minimum=Reach(leg.maximumKneeAngle),maximum=Mathf.Min(Reach(leg.minimumKneeAngle),leg.maxReach);
            float distance=Mathf.Clamp(wanted,minimum,Mathf.Max(minimum,maximum));
            var bendDirection=Vector3.ProjectOnPlane(pole,direction);
            if(bendDirection.sqrMagnitude<.0001f)bendDirection=Vector3.ProjectOnPlane(Vector3.Cross(up,direction),direction);
            if(bendDirection.sqrMagnitude<.0001f)bendDirection=Vector3.ProjectOnPlane(Vector3.right,direction);
            if(bendDirection.sqrMagnitude<.0001f)bendDirection=Vector3.ProjectOnPlane(Vector3.forward,direction);
            bendDirection.Normalize();float along=(a*a+distance*distance-b*b)/(2*distance);
            float height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            knee=hip+direction*along+bendDirection*height;ankle=hip+direction*distance;
            return Mathf.Abs(wanted-distance)<.03f;
        }
        public static void Present(WalkerLegRig rig,Vector3 hip,Vector3 knee,Vector3 ankle,Vector3 forward,Vector3 groundNormal){
            rig.upper.SetPositionAndRotation(hip,Quaternion.FromToRotation(Vector3.down,knee-hip));
            rig.lower.SetPositionAndRotation(knee,Quaternion.FromToRotation(Vector3.down,ankle-knee));
            var tangent=Vector3.ProjectOnPlane(forward,groundNormal);if(tangent.sqrMagnitude<.0001f)tangent=Vector3.ProjectOnPlane(Vector3.right,groundNormal);
            rig.foot.SetPositionAndRotation(ankle,Quaternion.LookRotation(tangent.normalized,groundNormal));
        }
        public static Vector3 StepArc(Vector3 start,Vector3 end,Vector3 up,float progress,float clearance){
            float t=Mathf.Clamp01(progress),smooth=t*t*(3-2*t);
            return Vector3.LerpUnclamped(start,end,smooth)+up*(Mathf.Sin(t*Mathf.PI)*clearance);
        }
    }
}
