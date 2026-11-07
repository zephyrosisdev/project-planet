using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum PlanetCloudQuality { Low, Medium, High, Ultra }
    public enum PlanetCloudSystem { None, Scattered, Broken, Overcast }
    [Serializable] public sealed class PlanetWeather
    {
        [Tooltip("Live render quality. Medium preserves the original half-resolution / 12-step budget. High and Ultra cost more GPU time.")]
        public PlanetCloudQuality quality=PlanetCloudQuality.Medium;
        public static float RenderScale(PlanetCloudQuality q)=>q==PlanetCloudQuality.Ultra?1:q==PlanetCloudQuality.High?.75f:.5f;
        public static int MarchSteps(PlanetCloudQuality q)=>q==PlanetCloudQuality.Low?6:q==PlanetCloudQuality.High?16:q==PlanetCloudQuality.Ultra?24:12;
        [Header("Live resolution overrides (zero uses quality preset)")]
        [Range(0,1)]public float renderScaleOverride;
        [Range(0,64)]public int marchStepsOverride;
        [Tooltip("Lighting/shadow map width; rounded to a power of two. Zero uses 64/128/256/512 by quality. Maximum override 4096. At 4096, the layered RFloat cache uses 256 MiB with 8 layers or 512 MiB with 16 layers, and can be very expensive to rebuild.")]
        [Range(0,4096)]public int shadowResolutionOverride;
        [Range(0,16)]public int shadowLayersOverride;
        [Header("Cloud light cache refresh")]
        [Tooltip("Maximum light-cache voxels evaluated per rendered frame. Resolution and shadow detail are preserved; larger caches take more frames to refresh.")]
        [Range(65536,4194304)]public int shadowUpdateVoxelsPerFrame=1048576;
        [Tooltip("Seconds to reuse a completed light cache before checking for wind or sunlight changes. Live density/shape edits invalidate it immediately.")]
        [Range(0,2)]public float shadowRefreshInterval=.25f;
        [Tooltip("Keep full cloud detail while planet diameter exceeds this fraction of screen height, irrespective of absolute distance.")]
        [Range(.05f,1)]public float fullDetailScreenFraction=.2f;
        public float EffectiveRenderScale=>renderScaleOverride>0?Mathf.Clamp(renderScaleOverride,.25f,1):RenderScale(quality);
        public int EffectiveMarchSteps=>marchStepsOverride>0?Mathf.Clamp(marchStepsOverride,4,64):MarchSteps(quality);
        public int ShadowWidth=>shadowResolutionOverride>0?Mathf.Clamp(Mathf.ClosestPowerOfTwo(shadowResolutionOverride),64,4096):quality==PlanetCloudQuality.Low?64:quality==PlanetCloudQuality.High?256:quality==PlanetCloudQuality.Ultra?512:128;
        public int ShadowLayers=>shadowLayersOverride>0?Mathf.Clamp(shadowLayersOverride,4,16):quality==PlanetCloudQuality.Ultra?16:quality==PlanetCloudQuality.High?12:8;
        public PlanetCloudSystem clouds=PlanetCloudSystem.Scattered;
        [Range(0,1)]public float coverage=.52f;
        [Min(0)]public float cloudAltitude=2200,cloudThickness=1600,windSpeed=25;
        [Range(.1f,3)]public float cloudDensity=1;
        [Header("Automatic nearby cloud detail")]
        [Tooltip("Preserve the broad orbital profile and blend in metre-sized detail as the observer approaches the cloud layer.")]
        public bool adaptiveLocalDetail=true;
        [Min(100)]public float localDetailDistance=50000,localDetailFeatureSize=350;
        [Range(0,1)]public float localDetailStrength=.25f;
        public float LocalDetailBlend(double distance)=>adaptiveLocalDetail?Mathf.Clamp01(localDetailStrength)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(localDetailDistance*.25f,Mathf.Max(100,localDetailDistance),(float)distance))):0;
        [Header("Live cloud noise tuning")]
        [Tooltip("Feature size on top of automatic world scaling. Higher = broader cloud detail; lower = smaller, more numerous features. Start here before increasing octaves.")]
        [Range(.05f,32)]public float noiseFeatureSize=1;
        [Tooltip("Maximum detail-noise octaves near the resident planet. Distant clouds retain two. More than three increases texture sampling cost; very fine octaves are precision-limited and may be below visible resolution.")]
        [Range(2,16)]public int detailNoiseOctaves=3;
        [Tooltip("Strength retained by each octave after the first fine octave.")]
        [Range(.1f,.85f)]public float octavePersistence=.5f;
        [Tooltip("Frequency multiplier between successive fine octaves.")]
        [Range(1.2f,4)]public float octaveLacunarity=2;
        [Tooltip("Multiplies noise frequency after automatic world scaling. Higher values make smaller, more numerous features.")]
        [Range(.1f,8)]public float detailNoiseFrequency=1;
        [Tooltip("How strongly noise cuts holes and wisps out of the cloud banks.")]
        [Range(0,1)]public float detailErosion=.52f;
        [Tooltip("Total strength of fine octaves on nearby clouds. Zero disables all fine-octave texture samples.")]
        [Range(0,.5f)]public float fineNoiseStrength=.22f;
        [Tooltip("First fine-octave frequency relative to the main detail. Higher values produce finer breakup.")]
        [Range(2,20)]public float fineNoiseFrequency=8.4f;
        [Range(0,1)]public float noiseContrast=.65f;
        [Tooltip("Detail noise rotates opposite to the cloud banks at this fraction of their angular wind speed.")]
        [Range(0,1)]public float detailCounterDrift=.2f;
        [Range(0,.6f)]public float interiorSoftness=.25f;
        [Range(0,1)]public float interiorExtinction=.4f;
        [Header("Live cloud lighting")]
        [Tooltip("Approximation of light scattered repeatedly inside the cloud. Higher values soften and brighten interior shadows.")]
        [Range(0,1)]public float multipleScattering=.32f;
        [Tooltip("How strongly sunlight scatters forward through cloud edges.")]
        [Range(0,.85f)]public float forwardScattering=.65f;
        [Range(.25f,2)]public float cloudBrightness=1;
        [Tooltip("Minimum diffuse daylight in heavily shadowed cloud. Lower values deepen interior shadows.")]
        [Range(.02f,.6f)]public float interiorAmbient=.28f;
        [Tooltip("Blend between simple lighting and internal scattering.")]
        [Range(0,1)]public float lightScattering=.85f;
        [Tooltip("Ground shadow darkness: 0 disables cloud shadows; higher values darken the terrain beneath thick clouds.")]
        [Range(0,1)]public float cloudShadowStrength=.18f;
        [Range(.5f,3)]public float internalShadowContrast=1.65f;
        [Range(.5f,2)]public float cloudDetailDistance=1;
        [Range(.05f,2)]public float haze=.45f;
        [Tooltip("Wavelength-dependent Rayleigh and aerosol single scattering. Disable to compare the original tinted haze.")]
        public bool physicalAtmosphere=true;
        [Tooltip("Use white light and a white primary-star disc; atmospheric scattering supplies the horizon colour.")]public bool neutralSunlight;
        [Range(0,4)]public float rayleighStrength=1;
        [Range(0,4)]public float aerosolStrength=1;
        [Header("Upper atmosphere variation")]
        [Tooltip("Strength of broad atmosphere density variation. Zero disables the noise lookup. This affects gas scattering, independently of cloud noise.")]
        [Range(0,.15f)]public float airDensityNoise=.06f;
        [Tooltip("Atmosphere density noise frequency over the planet's unit sphere. Lower values make broader features; higher values make smaller features. Default 8. Uses one noise layer, independently of cloud octaves.")]
        [Range(2,32)]public float airDensityNoiseScale=8;
        [Range(0,.08f)]public float airSolarTide=.025f;
        [Range(0,.08f)]public float airGravityTide=.012f;
        public Color atmosphereColour=new Color(.18f,.4f,.85f),cloudColour=new Color(.95f,.97f,1);
        public bool HasClouds=>clouds!=PlanetCloudSystem.None&&coverage>0&&cloudThickness>0;
        internal static uint Hash(uint x){unchecked{x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}}
        static float Value(uint x)=>(Hash(x)&0xffffff)/16777216f;
        public static void Generate(PlanetDefinition body,bool? atmosphere=null,bool? breathable=null,float pressure=1,float oxygen=.21f){
            uint key=unchecked((uint)body.terrainSeed)^0xc3416b3du;
            body.hasAtmosphere=atmosphere??Value(key)>.28f;body.breathable=body.hasAtmosphere&&(breathable??Value(key+1)>.48f);
            body.surfacePressure=body.hasAtmosphere?(atmosphere.HasValue?pressure:Mathf.Lerp(.15f,2,Value(key+2))):0;body.oxygenLevel=body.breathable?oxygen:0;
            var weather=new PlanetWeather();float chance=Value(key+3);
            weather.clouds=!body.hasAtmosphere||chance<.25f?PlanetCloudSystem.None:chance<.6f?PlanetCloudSystem.Scattered:chance<.88f?PlanetCloudSystem.Broken:PlanetCloudSystem.Overcast;
            weather.coverage=weather.clouds==PlanetCloudSystem.None?0:weather.clouds==PlanetCloudSystem.Scattered?.42f:weather.clouds==PlanetCloudSystem.Broken?.62f:.82f;
            weather.cloudAltitude=Mathf.Clamp(body.radius*.018f,600,5000);weather.cloudThickness=Mathf.Clamp(body.radius*.013f,300,3000);weather.windSpeed=Mathf.Lerp(12,55,Value(key+4));
            weather.haze=Mathf.Lerp(.25f,.65f,Value(key+5));weather.cloudDensity=Mathf.Lerp(.65f,1.3f,Value(key+6));
            if(!body.breathable){weather.atmosphereColour=Color.Lerp(new Color(.72f,.35f,.13f),new Color(.33f,.27f,.7f),Value(key+7));weather.cloudColour=Color.Lerp(Color.white,new Color(.85f,.67f,.38f),Value(key+8)*.5f);}
            body.weather=weather;
        }
        internal static float NearDetail(float altitude,float atmosphereHeight)=>1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(atmosphereHeight*1.5f,atmosphereHeight*3,altitude));
        public float Detail(float cloudDistance,float atmosphereHeight,float diameterPixels,float screenHeight){
            const float extendedRange=5;
            float range=Mathf.Max(1,atmosphereHeight)*Mathf.Clamp(cloudDetailDistance,.5f,2)*extendedRange;
            float distanceDetail=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(range*.5f,range*12,cloudDistance));
            float fraction=diameterPixels/Mathf.Max(1,screenHeight);
            float visibleDetail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.012f,.02f,fraction));
            float screenDetail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.02f,Mathf.Max(.05f,fullDetailScreenFraction),fraction));
            return visibleDetail*Mathf.Max(distanceDetail,screenDetail);
        }
        public void Layer(PlanetDefinition body,out float bottom,out float thickness){bottom=Mathf.Max(0,cloudAltitude);thickness=Mathf.Max(1,cloudThickness);}

    }
}

