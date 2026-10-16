using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MassiveArmy
{
    [CreateAssetMenu(menuName="Massive Army/Battle visual settings")]
    public sealed class BattleVisualSettings : ScriptableObject
    {
        [Header("Daylight")]
        [Min(0)] public float sunLux=50000;
        public Color sunColour=new Color(1,.965f,.9f);
        [Tooltip("Fixed camera exposure in EV. Higher values darken the image.")]
        [Range(0,20)] public float exposure=12.5f;
        [Tooltip("Sky exposure in stops. Higher values brighten indirect light.")]
        [Range(0,18)] public float skyExposure=12;
        [Range(0,1)] public float shadowStrength=.85f;
        [Range(0,1)] public float bloom=.06f;
        [Header("Volumetric fog of war")]
        public bool fogOfWar=true;
        [Tooltip("Extinction per metre. Increase to hide more terrain beneath the cloud.")]
        [Range(.001f,.15f)] public float density=.07f;
        [Tooltip("Ground mist depth above the terrain in metres.")]
        [Range(4,180)] public float height=12;
        [Range(0,1)] public float exploredDensity=.18f;
        [Tooltip("Light scenic mist inside visible areas, so wakes can curl around passing units. Does not change gameplay visibility.")]
        [Range(0,.3f)] public float visibleMistDensity=.14f;
        [Tooltip("Seconds for newly visible mist to disperse. Unit wakes push through it during this transition.")]
        [Range(.1f,3)] public float mistClearSeconds=.9f;
        [Tooltip("Visibility smoothing radius in map cells; the physical width scales with the world.")]
        [Range(.1f,4)] public float edgeSoftness=1.5f;
        [Tooltip("Size of the drifting density features in metres.")]
        [Range(10,200)] public float noiseScale=22;
        [Range(0,1)] public float noiseStrength=.8f;
        [Tooltip("Cloud drift along world X/Z in metres per second.")]
        public Vector2 wind=new Vector2(.25f,.12f);
        [Tooltip("Ambient curl distortion in metres. Passing-unit wakes remain independent.")]
        [Range(0,20)] public float flowStrength=.65f;
        [Tooltip("Size of 3D curl eddies in metres.")]
        [Range(30,500)] public float flowScale=160;
        [Tooltip("Seconds per ambient curl-feature evolution. Higher values keep the mist calmer.")]
        [Range(4,120)] public float flowCycle=54;
        [Range(0,1)] public float unexploredDarkness=.96f;
        [Range(0,1)] public float exploredDarkness=.35f;
        public Color fogColour=new Color(.55f,.62f,.67f);
        [Tooltip("Approximate brightness of the sunlit cloud; decrease for darker fog.")]
        [Range(.001f,1)] public float scattering=.15f;
        [HideInInspector] public int raySteps=32; // Legacy serialization; effective steps follow the fixed camera LOD budget.
        [Header("Persistent volumetric wakes")]
        [Range(0,4)] public float swirlStrength=.55f;
        [Range(0,4)] public float wakeStrength=1.25f;
        [Range(4,80)] public float wakeRadius=12;
        [Range(.5f,30)] public float wakeDecay=6;
        [Range(-2,2)] public float detailBias=0;
        public int LightingHash => System.HashCode.Combine(sunLux,sunColour,exposure,skyExposure,shadowStrength,bloom);
        public void ApplyLighting(VolumeProfile profile,Light sun)
        {
            T Get<T>() where T:VolumeComponent {if(!profile.TryGet<T>(out var value))value=profile.Add<T>();return value;}
            var exp=Get<Exposure>();exp.mode.Override(ExposureMode.Fixed);exp.fixedExposure.Override(exposure);
            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var glow=Get<Bloom>();glow.intensity.Override(bloom);glow.threshold.Override(1.2f);
            Get<VisualEnvironment>().skyType.Override((int)SkyType.Gradient);
            var sky=Get<GradientSky>();sky.top.Override(new Color(.24f,.38f,.56f));sky.middle.Override(new Color(.65f,.72f,.8f));sky.bottom.Override(new Color(.25f,.26f,.25f));sky.exposure.Override(skyExposure);
            Get<Fog>().enabled.Override(false); // Spatial fog of war is composited by its own volume pass.
            sun.color=sunColour;sun.shadows=LightShadows.Soft;
            var hd=sun.GetComponent<HDAdditionalLightData>();if(hd==null)hd=sun.gameObject.AddComponent<HDAdditionalLightData>();
            hd.SetIntensity(Mathf.Max(0,sunLux),LightUnit.Lux);hd.shadowDimmer=shadowStrength;
            ApplyArmyLighting(sun);
            (RenderPipelineManager.currentPipeline as HDRenderPipeline)?.RequestSkyEnvironmentUpdate();
        }
        public void ApplyArmyLighting(Light sun,VolumeProfile profile=null)
        {
            // These instanced shaders bypass HDRP's light loop and pre-exposure. Keep their
            // inexpensive shared lighting in the same exposure space as the terrain.
            float actualExposure=profile!=null&&profile.TryGet<Exposure>(out var cameraExposure)?cameraExposure.fixedExposure.value-cameraExposure.compensation.value:exposure;
            float actualSky=profile!=null&&profile.TryGet<GradientSky>(out var sky)?sky.exposure.value:skyExposure;
            float exposureScale=Mathf.Pow(2,12.5f-actualExposure);
            Color direct=(sun!=null?sun.color:sunColour).linear*(sun!=null&&sun.enabled&&sun.gameObject.activeInHierarchy?Mathf.Max(0,sun.intensity)/50000f:0)*exposureScale;
            if(sun!=null&&sun.TryGetComponent<HDAdditionalLightData>(out var lightData))direct*=lightData.lightDimmer;
            float ambient=Mathf.Pow(2,actualSky-12)*exposureScale;
            if(profile!=null&&profile.TryGet<VisualEnvironment>(out var environment)&&environment.skyType.value==0)ambient=0;
            Shader.SetGlobalVector("_ArmyLightDirection",sun!=null?-sun.transform.forward:Vector3.up);
            Shader.SetGlobalVector("_ArmyDirectLight",new Vector4(direct.r,direct.g,direct.b,0));
            Shader.SetGlobalFloat("_ArmyAmbientLight",ambient);
        }
    }
}
