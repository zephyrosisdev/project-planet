using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        public bool RotationLocked {get;private set;}
        Vector3 lockNormal;Quaternion lockOffset;
        static Quaternion OrbitAxes(Double3 position,Vector3 normal){var up=position.Float.normalized;var tangent=Vector3.Cross(normal,up).normalized;if(tangent.sqrMagnitude<.1f)tangent=Vector3.ProjectOnPlane(Mathf.Abs(up.y)>.95f?Vector3.forward:Vector3.up,up).normalized;return Quaternion.LookRotation(tangent,up);}
        Quaternion LockedAttitude(Double3 position)=>(OrbitAxes(position,lockNormal)*lockOffset).normalized;
        public void ToggleRotationLock(){if(Landed||AutoFlightActive)return;RotationLocked=!RotationLocked;angularVelocity=Vector3.zero;if(RotationLocked){SetFlightAssistance(false);var momentum=Double3.Cross(pose.position,pose.linearVelocity);lockNormal=momentum.Magnitude>1e-6?momentum.Float.normalized:Vector3.Cross(pose.position.Float.normalized,pose.rotation*Vector3.up).normalized;if(lockNormal.sqrMagnitude<.1f)lockNormal=Vector3.Cross(pose.position.Float.normalized,pose.rotation*Vector3.right).normalized;lockOffset=Quaternion.Inverse(OrbitAxes(pose.position,lockNormal))*pose.rotation;}nextOrbitRefresh=0;}
        internal void SetFlightStateForValidation(SpacePose state)=>ResetFlightState(state);
        void ResetFlightState(SpacePose state){EndCruise();dockStation=0;dockProgress=0;galaxyPrediction?.Invalidate();AutoFlightActive=false;pose=state;pose.rotation=pose.rotation.normalized;angularVelocity=Vector3.zero;RotationLocked=false;FlightAssistEnabled=false;Landed=false;nextLandingRefresh=0;GearDeployed=false;shipFrame.Pose=pose;nextArcRefresh=0;nextOrbitRefresh=0;}
    }
}
