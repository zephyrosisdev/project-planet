using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    [CreateAssetMenu(menuName="Massive Army/Planet System Definition")]
    public sealed class PlanetSystemDefinition:ScriptableObject
    {
        [Tooltip("Applied when a new session starts. Scales planet/star sizes and orbital spacing; keeps surface gravity and local objects unchanged. 10 and 100 are experimental stress settings.")]
        [Range(1,1000)] public float sizeMultiplier=1;
        public static float AutomaticCloudNoiseScale(float worldMultiplier){float scale=Mathf.Max(1,worldMultiplier);return scale*Mathf.Sqrt(scale);}
        public float CloudNoiseSizeScale=>AutomaticCloudNoiseScale(RuntimeSizeMultiplier);
        [NonSerialized] public float RuntimeSizeMultiplier=1;
        // Runtime copies keep authored assets and repeated Play sessions unchanged.
        public PlanetSystemDefinition CreateScaledCopy(float multiplier){
            float scale=float.IsNaN(multiplier)||float.IsInfinity(multiplier)?1:Mathf.Clamp(multiplier,1,1000);
            var copy=Instantiate(this);copy.RuntimeSizeMultiplier=scale;copy.radiusRange*=scale;copy.spacing*=scale;
            if(galaxy!=null){
                copy.galaxy=Instantiate(galaxy);var g=copy.galaxy;
                g.galaxyRadius*=scale;g.systemOrbitRadius*=scale;g.binarySeparation*=scale;
                g.stellarMu*=(double)scale*scale;g.blackHoleMu*=(double)scale*scale;
                g.resolvedStarDistance*=scale;g.skyTravelThreshold*=scale;
            }
            return copy;
        }
        public int seed=73129;
        public GalaxyDefinition galaxy;
        [Range(1,8)] public int planetCount=4;
        public Vector2 radiusRange=new Vector2(80000,160000);
        [Min(500000)] public float spacing=1200000;
        [Tooltip("Surface simulation interest altitude in metres. Army/base ticks sleep above this; it does not control planet gravity or the visual detail switch.")]
        [Min(10000)] public float activationAltitude=80000;
        [Min(1000)] public float sleepHysteresis=20000;
        [Min(20000)] public float preparationDistance=200000;
        [Header("Planet approach detail")]
        [Tooltip("Minimum altitude in metres for switching the viewed planet to full terrain. Independent of surface simulation interest.")]
        [Min(10000)] public float detailActivationAltitude=500000;
        [Tooltip("Full-detail altitude as a multiple of this planet's radius; the larger of this and Detail Activation Altitude wins.")]
        [Min(0)] public float detailActivationRadius=1;
        [Tooltip("Additional preparation lead as a multiple of planet radius; at least Preparation Distance is used. Only one automatic preparation runs at a time.")]
        [Min(0)] public float preparationRadius=1;
        public double DetailAltitude(PlanetDefinition body)=>Math.Max(Math.Max(10000,activationAltitude),Math.Max(detailActivationAltitude,body.radius*Math.Max(0,detailActivationRadius)));
        public double PreparationAltitude(PlanetDefinition body)=>DetailAltitude(body)+Math.Max(Math.Max(20000,preparationDistance),body.radius*Math.Max(0,preparationRadius));
        public bool ShouldUseFullDetail(PlanetDefinition body,UniversePosition observer)=>observer.RelativeTo(body.position).Magnitude-body.radius<=DetailAltitude(body);
        // Generate definitions only. No terrain, army buffers, edits or stamp fields for unvisited bodies.
        public PlanetDefinition[] Generate(PlanetDefinition home)
        {
            int count=Mathf.Clamp(planetCount,1,8);var bodies=new PlanetDefinition[count];bodies[0]=home;
            int nameSeed=seed^home.terrainSeed;var usedNames=new System.Collections.Generic.HashSet<string>();if(PlanetNames.Placeholder(home.displayName))home.displayName=PlanetNames.Unique(nameSeed,0,usedNames);else usedNames.Add(home.displayName);
            uint state=unchecked((uint)(seed^home.terrainSeed))+0x9e3779b9u;
            uint Next(){state^=state<<13;state^=state>>17;state^=state<<5;return state;}
            float Value()=>(Next()>>8)*(1f/16777216);
            for(int i=1;i<count;i++){
                var d=Instantiate(home);d.name="Generated planet "+(i+1);d.bodyId=home.bodyId+i;
                d.displayName=PlanetNames.Unique(nameSeed,i,usedNames);d.name=d.displayName;d.terrainSeed=(int)(Next()&0x7fffffff);
                d.radius=Mathf.Lerp(Mathf.Max(20000,radiusRange.x),Mathf.Max(20000,radiusRange.y),Value());
                d.terrainHeight=d.radius/RuntimeSizeMultiplier*.015f;d.oceanHeight=Mathf.Lerp(-200,120,Value());
                d.gravitationalParameter=9.81*d.radius*d.radius;d.atmosphereHeight=d.radius/RuntimeSizeMultiplier/12;
                PlanetWeather.Generate(d);
                // Disjoint radial shells guarantee separation, irrespective of random longitude.
                double distance=(Math.Max(spacing,4*Math.Max(home.radius,Math.Max(radiusRange.x,radiusRange.y))))*i;
                double angle=Value()*Math.PI*2;d.position=home.position.Add(new Double3(Math.Cos(angle)*distance,(Value()-.5)*distance*.25,Math.Sin(angle)*distance));
                d.velocity=new Double3();bodies[i]=d;
            }
            return bodies;
        }
        public bool ShouldSimulate(PlanetDefinition body,UniversePosition observer,bool wasActive)
            =>observer.RelativeTo(body.position).Magnitude-body.radius<=Math.Max(10000,activationAltitude)+(wasActive?Math.Max(1000,sleepHysteresis):0);
    }
}
