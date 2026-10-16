using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
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
        public UniversePosition position;
        public Double3 velocity;
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
        public Double3 GetSurfacePositionPrecise(Vector3 direction,double altitude=0)
        {
            var d=new Double3(direction);double length=d.Magnitude;
            if(length<1e-12)throw new ArgumentException("Surface direction must be nonzero");
            return d*((Definition.radius+(double)SampleHeight(direction)+altitude)/length);
        }
        public Vector3 GetSurfacePosition(Vector3 direction,float altitude=0)=>direction.normalized*(Definition.radius+SampleHeight(direction)+altitude);
        public Vector3 SampleNormal(Vector3 direction)
        {
            Vector3 d=direction.normalized;CubeSphere.Project(d,out var f,out _);
            var t=Vector3.ProjectOnPlane(CubeSphere.Right[(int)f],d).normalized;var b=Vector3.Cross(d,t);
            const float e=.0001f;
            var a=GetSurfacePosition(d+t*e)-GetSurfacePosition(d-t*e);var c=GetSurfacePosition(d+b*e)-GetSurfacePosition(d-b*e);
            return Vector3.Cross(a,c).normalized;
        }
        public Biome SampleBiome(Vector3 direction)
        {
            var climate=PlanetClimate.Sample(direction,(Definition.terrainSeed%1009)*.017f);float h=SampleHeight(direction),t=climate.x,m=climate.y;
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
