using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    public enum VehicleLocomotion { Walker, Wheeled, Tracked }
    public enum VehicleSeatRole { Driver, Pilot, Gunner, Passenger, Commander }
    [Flags] public enum VehicleSeatPermissions { None=0, Movement=1, Aim=2, Fire=4, Command=8 }
    public enum VehicleMotionState { Idle, Starting, Walking, Running, Turning, Stopping, Falling, Landing, Disabled }
    public enum WalkerFootState { Planted, Stepping, Unsupported }

    [Serializable] public sealed class VehicleSeatDefinition
    {
        public string name="Driver";
        public VehicleSeatRole role;
        public VehicleSeatPermissions permissions=VehicleSeatPermissions.Movement;
        public Vector3 position, eye, entry, exit;
        public bool aiByDefault;
    }
    [Serializable] public sealed class WalkerLegDefinition
    {
        public string name="Leg";
        public Vector3 hip, stance, pole=Vector3.forward;
        [Min(.1f)] public float upperLength=1, lowerLength=1;
        [Min(.01f)] public float footWidth=.4f, footLength=.7f;
        [Min(.01f)] public float stride=1, stepHeight=.4f, stepDuration=.3f, maxReach=2, compression=.6f, probeHeight=1, probeDepth=3, strength=1;
        [Range(1,175)] public float minimumKneeAngle=8, maximumKneeAngle=155;
        [Range(0,1)] public float damage;
    }
    [Serializable] public sealed class WalkerGaitDefinition
    {
        public string name="Walk";
        [Tooltip("One phase per leg. Equal values step together; any number of legs is supported.")]
        public int[] groups=Array.Empty<int>();
        [Min(1)] public int minimumGrounded=1;
        [Min(.05f)] public float cadence=1, strideMultiplier=1, maximumSpeed=10;
        [Range(0,.5f)] public float bodyBob=.04f;
    }
    [Serializable] public sealed class VehicleWheelDefinition
    {
        public string name="Wheel";
        public Vector3 position;
        public float radius=.45f, width=.25f, suspension=.35f;
        public bool steering, driven=true, brake=true;
    }
    [Serializable] public sealed class VehicleTraversalProfile
    {
        [Range(0,80)] public float maximumSlope=38;
        public float maximumStep=.7f, maximumDrop=1.5f, clearance=.25f;
    }
    [CreateAssetMenu(menuName="Synthetic Front/Vehicle definition")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        public string id, displayName;
        public VehicleLocomotion locomotion;
        public GameObject prefab;
        [Tooltip("Camera clipping profile for this vehicle. Edit the referenced asset to tune viewing range.")]
        public VehicleCameraSettings cameraSettings;
        [Tooltip("Full occupied envelope, including feet/wheels and weapons. Metres; pivot is at ground level.")]
        public Vector3 size=new Vector3(2.3f,2.5f,4.8f);
        public Vector3 chassisCentre=new Vector3(0,1,0), chassisSize=new Vector3(2,1,3), centreOfMass=new Vector3(0,1,0);
        public float mass=3500, maximumSpeed=15, reverseSpeed=6, acceleration=3, braking=7, turnSpeed=45, turnAcceleration=90;
        public float suspensionSpring=45, suspensionDamping=12, grip=6, torsoYaw=100, torsoPitch=35;
        public VehicleTraversalProfile traversal=new VehicleTraversalProfile();
        public VehicleSeatDefinition[] seats=Array.Empty<VehicleSeatDefinition>();
        public WalkerLegDefinition[] legs=Array.Empty<WalkerLegDefinition>();
        public WalkerGaitDefinition[] gaits=Array.Empty<WalkerGaitDefinition>();
        public VehicleWheelDefinition[] wheels=Array.Empty<VehicleWheelDefinition>();
        [Tooltip("Distance in metres for full, reduced and simplified articulation. Beyond the final distance only the baked proxy is shown.")]
        public Vector3 detailDistances=new Vector3(90,250,650);
        [Header("Jump booster")]
        [Min(0)] public float boostAcceleration=30;
        [Min(.1f)] public float boostFuelSeconds=1.5f,boostMaximumBurst=.65f,boostRechargeSeconds=5,boostRechargeDelay=1.25f;
        public bool debugDraw;

        public string ValidateDefinition()
        {
            if(string.IsNullOrEmpty(id)||size.x<=0||size.y<=0||size.z<=0||mass<=0)return "Identity, bounds and mass must be valid";
            if(prefab==null)return "Assign a rig prefab";
            if(seats==null||seats.Length==0)return "At least one seat is required";
            if(locomotion==VehicleLocomotion.Walker){
                if(legs==null||legs.Length<2)return "Walkers require at least two legs";
                foreach(var l in legs)if(l.upperLength<=0||l.lowerLength<=0||l.minimumKneeAngle>=l.maximumKneeAngle)return "Invalid leg lengths or joint limits";
                if(gaits==null||gaits.Length==0)return "Assign a gait";
                foreach(var g in gaits)if(g.groups==null||g.groups.Length!=legs.Length||g.minimumGrounded<1||g.minimumGrounded>=legs.Length)return "Gait must describe every leg and retain ground support";
            }
            if(locomotion==VehicleLocomotion.Wheeled&&(wheels==null||wheels.Length<4))return "Wheeled vehicles need at least four wheel definitions";
            return "";
        }
    }
}
