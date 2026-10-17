using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    [CreateAssetMenu(menuName="Massive Army/Planet System Definition")]
    public sealed class PlanetSystemDefinition:ScriptableObject
    {
        public int seed=73129;
        public GalaxyDefinition galaxy;
        [Range(1,8)] public int planetCount=4;
        public Vector2 radiusRange=new Vector2(80000,160000);
        [Min(500000)] public float spacing=1200000;
        [Min(10000)] public float activationAltitude=80000;
        [Min(1000)] public float sleepHysteresis=20000;
        [Min(20000)] public float preparationDistance=200000;
        // Generate definitions only. No terrain, army buffers, edits or stamp fields for unvisited bodies.
        public PlanetDefinition[] Generate(PlanetDefinition home)
        {
            int count=Mathf.Clamp(planetCount,1,8);var bodies=new PlanetDefinition[count];bodies[0]=home;
            uint state=unchecked((uint)(seed^home.terrainSeed))+0x9e3779b9u;
            uint Next(){state^=state<<13;state^=state>>17;state^=state<<5;return state;}
            float Value()=>(Next()>>8)*(1f/16777216);
            for(int i=1;i<count;i++){
                var d=Instantiate(home);d.name="Generated planet "+(i+1);d.bodyId=home.bodyId+i;
                d.displayName="Planet "+(i+1);d.terrainSeed=(int)(Next()&0x7fffffff);
                d.radius=Mathf.Lerp(Mathf.Max(20000,radiusRange.x),Mathf.Max(20000,radiusRange.y),Value());
                d.terrainHeight=d.radius*.015f;d.oceanHeight=Mathf.Lerp(-200,120,Value());
                d.gravitationalParameter=9.81*d.radius*d.radius;d.atmosphereHeight=d.radius/12;
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
