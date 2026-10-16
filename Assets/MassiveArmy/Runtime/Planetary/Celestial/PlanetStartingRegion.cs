using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>One bounded startup search for a naturally connected mainland, before any base pads are applied.</summary>
    public static class PlanetStartingRegion
    {
        public static Vector3 Select(PlanetDefinition definition,Vector3 preferred){
            foreach(var d in Candidates(definition,preferred))if(ConnectedPlain(definition,d))return d;
            throw new InvalidOperationException("No connected dry starting region found; refusing to raise isolated ocean islands.");
        }
        internal static System.Collections.Generic.IEnumerable<Vector3> Candidates(PlanetDefinition definition,Vector3 preferred){
            preferred.Normalize();var east=Vector3.Cross(Mathf.Abs(preferred.y)>.9999f?Vector3.forward:Vector3.up,preferred).normalized;var north=Vector3.Cross(preferred,east);
            for(int i=0;i<256;i++){float a=i*2.39996323f,r=Mathf.Sqrt(i)*1800;yield return (preferred+(east*Mathf.Cos(a)+north*Mathf.Sin(a))*(r/definition.radius)).normalized;}
            for(int i=0;i<1024;i++){float y=1-2*(i+.5f)/1024,a=i*2.39996323f,r=Mathf.Sqrt(1-y*y);yield return new Vector3(r*Mathf.Cos(a),y,r*Mathf.Sin(a));}
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
