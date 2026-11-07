using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    public enum PlanetPrecisionMode { CameraRelative, Compensated, ExtendedLocal }
    public static class PlanetPrecisionPolicy
    {
        public static double FloatSpacing(double radius)=>Math.Pow(2,Math.Floor(Math.Log(Math.Max(1,radius),2))-23);
        public static PlanetPrecisionMode Select(double radius)=>radius<65536?PlanetPrecisionMode.CameraRelative:radius<2097152?PlanetPrecisionMode.Compensated:PlanetPrecisionMode.ExtendedLocal;
        public static Vector3 Direction(Double3 position){double length=position.Magnitude;return length>1e-12?(position*(1/length)).Float:Vector3.up;}
        public static float NormalSampleAngle(float radius)=>(float)Math.Max(.4/Math.Max(1,radius),4.0/8388608);
    }
    [CreateAssetMenu(menuName="Massive Army/Planet Definition")]
    public sealed class PlanetDefinition : ScriptableObject
    {
        // Gameplay radius and the independent threshold for local streaming/navigation.
        public const float TargetRadius=120000,DefaultRadius=TargetRadius,RegionalNavigationRadius=6000;
        [NonSerialized]internal PlanetStampLibrary.Field stampField;
        [NonSerialized]internal PlanetTerrainEdits edits;
        public PlanetTerrainEdits Edits=>edits??(edits=new PlanetTerrainEdits(this));
        void OnDisable(){stampField?.Dispose();stampField=null;edits?.Dispose();edits=null;}
        public float ReliefBound=>Mathf.Abs(terrainHeight)*(stampTerrain&&radius>=20000?PlanetStampLibrary.Shared.MaximumDisplacement:1)+(edits?.MaximumDepth??0);
        public bool stampTerrain=false;
        public int bodyId=1,terrainSeed=73129;
        public string displayName="Development planet";
        [Min(10)]public float radius=500,terrainHeight=12;
        public float oceanHeight=-4;
        public double gravitationalParameter=2452500;
        public float rotationRate,axialTilt;
        public bool hasAtmosphere=true,breathable=true;
        public float atmosphereHeight=100,surfacePressure=1,oxygenLevel=.21f;
        public PlanetWeather weather=new PlanetWeather();
        public UniversePosition position;
        public Double3 velocity;
        [Header("Ocean surface")]
        [Min(0)]public float oceanWaveHeight=1.2f;
        [Min(4)]public float oceanWaveLength=40;
        [Min(0)]public float oceanWaveSpeed=6;
        [Range(0,.85f)]public float oceanWaveSteepness=.6f;
        [Tooltip("Width of the broken foam edge in metres, estimated from shore slope.")]
        [Min(.1f)]public float oceanFoamWidth=3;
        [Range(0,1)]public float oceanFoamStrength=.55f;
        [Min(.1f)]public float oceanFoamNoiseScale=2.5f;
        [Min(.1f)]public float oceanShoreAttenuationDepth=12,oceanFoamDepth=5;
        [Header("Experimental rendering precision")]
        [Tooltip("Experimental native GPU doubles for terrain position reconstruction and cloud shell calculations. Disabled by default; requires a supporting D3D11/12 driver and can be slow.")]
        public bool experimentalGpuDoublePrecision;
        [Min(0)]public float gpuDoubleMinimumRadius=2097152;
        public PlanetPrecisionMode PrecisionMode=>PlanetPrecisionPolicy.Select(radius);
        public double EstimatedFloatSpacing=>PlanetPrecisionPolicy.FloatSpacing(radius);
        public double Mass=>gravitationalParameter/6.67430e-11;
        public Double3 Gravity(UniversePosition p)
        {
            var d=position.RelativeTo(p);double r=Math.Max(1,d.Magnitude);return d*(gravitationalParameter/(r*r*r));
        }
        public bool BreathableAt(float altitude)=>hasAtmosphere&&breathable&&oxygenLevel>0&&altitude<atmosphereHeight;
    }
    [Serializable]public struct PlanetSurfacePose
    {
        public int planetId;
        public Vector3 surfaceDirection;
        public float altitude,heading;
        public Quaternion Rotation
        {
            get{CubeSphere.Project(surfaceDirection,out var face,out _);var up=surfaceDirection.normalized;
                var forward=Vector3.ProjectOnPlane(CubeSphere.Up[(int)face],up).normalized;
                return Quaternion.AngleAxis(heading,up)*Quaternion.LookRotation(forward,up);}
        }
    }
    public sealed class PlanetSurface
    {
        public readonly PlanetDefinition Definition;
        public PlanetSurface(PlanetDefinition definition){Definition=definition;}
        // Seamless directional field. Equivalent expression lives in PlanetSurface.hlsl.
        public float SampleHeight(Vector3 direction)
        {
            return Definition.edits!=null?Definition.edits.Height(direction):PlanetTerrainEdits.BaseHeight(Definition,direction);
        }
        public float SampleHeight(Double3 direction)=>Definition.edits!=null?Definition.edits.Height(direction):PlanetTerrainEdits.BaseHeight(Definition,PlanetPrecisionPolicy.Direction(direction));
        public Double3 GetSurfacePositionPrecise(Vector3 direction,double altitude=0)
        {
            var d=new Double3(direction);double length=d.Magnitude;
            if(length<1e-12)throw new ArgumentException("Surface direction must be nonzero");
            return d*((Definition.radius+(double)SampleHeight(direction)+altitude)/length);
        }
        public Double3 ProjectToSurface(Double3 position,double clearance=0){
            double length=position.Magnitude;if(length<1e-12)throw new ArgumentException("Surface position must be nonzero");
            var direction=position*(1/length);
            return direction*(Definition.radius+(double)SampleHeight(direction)+clearance);
        }
        public Vector3 GetSurfacePosition(Vector3 direction,float altitude=0)=>direction.normalized*(Definition.radius+SampleHeight(direction)+altitude);
        public Vector3 SampleNormal(Vector3 direction)
        {
            Vector3 d=direction.normalized;CubeSphere.Project(d,out var f,out _);
            var t=Vector3.ProjectOnPlane(CubeSphere.Right[(int)f],d).normalized;var b=Vector3.Cross(d,t);
            if(Definition.PrecisionMode==PlanetPrecisionMode.ExtendedLocal){
                float angle=PlanetPrecisionPolicy.NormalSampleAngle(Definition.radius);
                float dx=(SampleHeight((d+t*angle).normalized)-SampleHeight((d-t*angle).normalized))/(2*angle*Definition.radius);
                float dz=(SampleHeight((d+b*angle).normalized)-SampleHeight((d-b*angle).normalized))/(2*angle*Definition.radius);
                return (d-t*dx-b*dz).normalized;
            }
            const float e=.0001f;
            var a=GetSurfacePosition(d+t*e)-GetSurfacePosition(d-t*e);var c=GetSurfacePosition(d+b*e)-GetSurfacePosition(d-b*e);
            return Vector3.Cross(a,c).normalized;
        }
        public Biome SampleBiome(Vector3 direction)
        {
            var climate=PlanetClimate.Sample(direction,(Definition.terrainSeed%1009)*.017f);float h=SampleHeight(direction),t=Mathf.Clamp01(climate.x-Mathf.Max(0,h-Definition.oceanHeight)/Mathf.Max(1000,Mathf.Abs(Definition.terrainHeight)*1.7f)*.65f),m=climate.y;
            if(h<Definition.oceanHeight)return Biome.Ocean;
            if(h<Definition.oceanHeight+1)return Biome.Beach;
            if(t<.14f)return Biome.Glacier;if(t<.3f)return Biome.Tundra;
            if(t>.68f)return m<.38f?Biome.HotDesert:m<.53f?Biome.Savanna:m<.68f?Biome.SeasonalForest:Biome.Rainforest;
            return m<.3f?Biome.Steppe:m<.5f?Biome.Grassland:m>.7f?Biome.TemperateRainforest:Biome.TemperateForest;
        }
        public bool Raycast(Ray ray,out Vector3 hit)
        {
            // Outer bound followed by a sign bracket; handles mountains outside the mean sphere.
            hit=default;float r=Definition.radius+Definition.ReliefBound;
            float b=Vector3.Dot(ray.origin,ray.direction),c=ray.origin.sqrMagnitude-r*r,disc=b*b-c;
            if(disc<0)return false;float near=Mathf.Max(0,-b-Mathf.Sqrt(disc)),far=-b+Mathf.Sqrt(disc);if(far<=near)return false;
            float Signed(float t){var p=ray.GetPoint(t);return p.magnitude-Definition.radius-SampleHeight(p);}
            float a=near,sa=Signed(a);const int samples=128;
            for(int i=1;i<=samples;i++){
                float z=Mathf.Lerp(near,far,i/(float)samples),sz=Signed(z);
                if(sa*sz<=0){for(int k=0;k<24;k++){float mid=(a+z)*.5f,sm=Signed(mid);if(sm*sa>0){a=mid;sa=sm;}else z=mid;}hit=ray.GetPoint((a+z)*.5f);return true;}
                a=z;sa=sz;
            }
            return false;
        }
    }
}
