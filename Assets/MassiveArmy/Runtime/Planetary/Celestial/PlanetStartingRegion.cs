using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>One bounded startup search for a naturally connected mainland, before any base pads are applied.</summary>
    public static class PlanetStartingRegion
    {
        public static float CoastDistance(PlanetDefinition definition,Vector3 point){
            var east=Vector3.Cross(Mathf.Abs(point.y)>.95f?Vector3.right:Vector3.up,point).normalized;var north=Vector3.Cross(point,east);
            foreach(float distance in new[]{3000f,6000f,12000f})for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var d=(point+(east*Mathf.Cos(a)+north*Mathf.Sin(a))*(distance/definition.radius)).normalized;if(PlanetTerrainEdits.BaseHeight(definition,d)<definition.oceanHeight)return distance;}
            return 24000;
        }
        public static Vector3 SelectCoastal(PlanetDefinition definition,Vector3 preferred){
            Vector3 best=Vector3.zero;float score=float.MaxValue;
            foreach(var d in Candidates(definition,preferred))if(ExpansionPlain(definition,d)){float coast=CoastDistance(definition,d);if(coast<score){score=coast;best=d;}if(coast<=6000)return d;}
            return best.sqrMagnitude>.5f?best:Select(definition,preferred);
        }
        public static Vector3 Select(PlanetDefinition definition,Vector3 preferred){
            foreach(var d in Candidates(definition,preferred))if(ConnectedPlain(definition,d))return d;
            throw new InvalidOperationException("No connected dry starting region found; refusing to raise isolated ocean islands.");
        }
        internal static System.Collections.Generic.IEnumerable<Vector3> Candidates(PlanetDefinition definition,Vector3 preferred){
            preferred.Normalize();var east=Vector3.Cross(Mathf.Abs(preferred.y)>.9999f?Vector3.forward:Vector3.up,preferred).normalized;var north=Vector3.Cross(preferred,east);
            for(int i=0;i<256;i++){float a=i*2.39996323f,r=Mathf.Sqrt(i)*1800;yield return (preferred+(east*Mathf.Cos(a)+north*Mathf.Sin(a))*(r/definition.radius)).normalized;}
            for(int i=0;i<1024;i++){float y=1-2*(i+.5f)/1024,a=i*2.39996323f,r=Mathf.Sqrt(1-y*y);yield return new Vector3(r*Mathf.Cos(a),y,r*Mathf.Sin(a));}
        }
        // Search a deterministic 30-degree cap around the antipode. Never manufacture an ocean island.
        internal static System.Collections.Generic.IEnumerable<Vector3> OppositeCandidates(PlanetDefinition definition,Vector3 home){
            var opposite=-home.normalized;var east=Vector3.Cross(Mathf.Abs(opposite.y)>.95f?Vector3.right:Vector3.up,opposite).normalized;var north=Vector3.Cross(opposite,east);
            for(int i=0;i<2048;i++){float angle=i*2.39996323f,r=Mathf.Sqrt(i/2047f)*Mathf.Tan(30*Mathf.Deg2Rad);yield return (opposite+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*r).normalized;}
        }
        // Regional towns need a usable local site and a dry corridor, not the
        // perfectly uniform 3.6 km square required by the initial base fixture.
        internal static bool RegionalSite(PlanetDefinition definition,Vector3 home,Vector3 parent){
            var east=Vector3.Cross(Mathf.Abs(home.y)>.95f?Vector3.right:Vector3.up,home).normalized;var north=Vector3.Cross(home,east);float low=float.MaxValue,high=float.MinValue;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){var d=(home+(east*x+north*z)*(300/definition.radius)).normalized;float h=PlanetTerrainEdits.BaseHeight(definition,d);if(h<definition.oceanHeight+2)return false;low=Mathf.Min(low,h);high=Mathf.Max(high,h);if(high-low>160)return false;}
            for(int i=1;i<16;i++)if(PlanetTerrainEdits.BaseHeight(definition,Vector3.Slerp(parent,home,i/16f))<definition.oceanHeight+.2f)return false;return true;
        }
        internal static bool ExpansionPlain(PlanetDefinition definition,Vector3 home){
            var east=Vector3.Cross(Mathf.Abs(home.y)>.95f?Vector3.right:Vector3.up,home).normalized;var north=Vector3.Cross(home,east);float low=float.MaxValue,high=float.MinValue;
            for(int x=-1800;x<=1800;x+=450)for(int z=-1800;z<=1800;z+=450){var d=(home+(east*x+north*z)/definition.radius).normalized;
                if(PlanetTerrainEdits.MountainWeight(definition,d)>.1f)return false;float h=PlanetTerrainEdits.BaseHeight(definition,d);if(h<definition.oceanHeight+60)return false;
                low=Mathf.Min(low,h);high=Mathf.Max(high,h);if(high-low>300)return false;
            }return true;
        }
        public static bool ConnectedPlain(PlanetDefinition definition,Vector3 home){
            var east=Vector3.Cross(Mathf.Abs(home.y)>.9999f?Vector3.forward:Vector3.up,home).normalized;var north=Vector3.Cross(home,east);float low=float.MaxValue,high=float.MinValue;
            // Both 4.5 km-separated homes, expansion areas and a 4 km-wide land corridor must already be dry plains.
            for(int x=-2000;x<=6500;x+=500)for(int z=-2000;z<=2000;z+=500){var d=(home+(east*x+north*z)/definition.radius).normalized;
                if(PlanetTerrainEdits.MountainWeight(definition,d)>.1f)return false;float h=PlanetTerrainEdits.BaseHeight(definition,d);
                if(h<definition.oceanHeight+60)return false;low=Mathf.Min(low,h);high=Mathf.Max(high,h);if(high-low>300)return false;
            }return true;
        }
    }
}
