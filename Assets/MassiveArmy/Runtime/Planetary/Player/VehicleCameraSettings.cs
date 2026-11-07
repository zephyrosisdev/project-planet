using UnityEngine;
namespace MassiveArmy.Planetary {
    [CreateAssetMenu(menuName="Synthetic Front/Vehicle camera settings")]
    public sealed class VehicleCameraSettings:ScriptableObject {
        [Tooltip("Metres. Smaller values retain nearby cockpit geometry; this is not the viewing distance.")]
        [Min(.01f)] public float nearClipPlane=.06f;
        [Tooltip("Minimum viewing distance in metres. Actual far plane is the larger of this and body-centre distance times the multiplier.")]
        [Min(1000)] public float minimumFarClipPlane=30000;
        [Tooltip("Multiplies distance from the active planet's centre, not altitude. Does not change terrain detail budgets.")]
        [Min(1)] public float bodyDistanceMultiplier=3;
        [Tooltip("Exclude atmosphere inside sealed ship compartments. Disable temporarily to diagnose pilot-only weather flicker; it can allow haze into the cabin.")]
        public bool maskWeatherInsideCabin=true;
        public void Apply(Camera camera,double bodyDistance){
            camera.nearClipPlane=float.IsFinite(nearClipPlane)?Mathf.Clamp(nearClipPlane,.01f,10):.06f;
            camera.farClipPlane=PlanetCameraClipping.FarPlane(bodyDistance,minimumFarClipPlane,bodyDistanceMultiplier);
        }
        internal static void ApplyOrDefault(VehicleCameraSettings settings,Camera camera,double bodyDistance,float near=.06f){
            if(settings!=null)settings.Apply(camera,bodyDistance);
            else{camera.nearClipPlane=near;camera.farClipPlane=PlanetCameraClipping.FarPlane(bodyDistance);}
        }
    }
    public sealed partial class PlanetDevelopment {
        [Header("Spaceship pilot camera")]
        [Tooltip("Optional override. Otherwise uses Resources/MassiveArmy/VehicleCameras/SpaceshipPilot. Live asset edits are read each frame.")]
        public VehicleCameraSettings spacecraftCameraSettings;
        VehicleCameraSettings defaultSpacecraftCameraSettings;
        internal VehicleCameraSettings PilotCameraSettings=>spacecraftCameraSettings!=null?spacecraftCameraSettings:
            defaultSpacecraftCameraSettings!=null?defaultSpacecraftCameraSettings:
            defaultSpacecraftCameraSettings=Resources.Load<VehicleCameraSettings>("MassiveArmy/VehicleCameras/SpaceshipPilot");
    }
}
