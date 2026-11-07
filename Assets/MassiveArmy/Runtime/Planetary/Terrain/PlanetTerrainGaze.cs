using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public static class PlanetTerrainGaze {
        static double Dot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        static bool Hit(Double3 eye,Double3 ray,double radius,out Double3 point){
            double along=-Dot(eye,ray);var side=eye+ray*along;double disc=radius*radius-Dot(side,side);point=default;
            if(disc<0)return false;double root=Math.Sqrt(disc),t=along-root;if(t<0)t=along+root;if(t<0)return false;
            point=eye+ray*t;return true;
        }
        // A bounded visual focus query, not a physics raycast. Four radial
        // refinements account for relief without a 128-sample CPU terrain march.
        public static bool TryFocus(PlanetSurface surface,Double3 eye,Vector3 forward,out Vector3 focus){
            focus=default;double length=eye.Magnitude;if(double.IsNaN(length)||double.IsInfinity(length)||length<1||!float.IsFinite(forward.x)||!float.IsFinite(forward.y)||!float.IsFinite(forward.z)||forward.sqrMagnitude<1e-8f)return false;
            var ray=new Double3(forward.normalized);double rayLength=ray.Magnitude;ray=ray*(1/rayLength);
            var body=surface.Definition;double radius=body.radius;
            if(!Hit(eye,ray,radius+Math.Max(0,body.oceanHeight),out var point)&&!Hit(eye,ray,radius+Math.Max(body.ReliefBound,body.oceanHeight),out point))return false;
            for(int i=0;i<4;i++){double height=Math.Max(body.oceanHeight,surface.SampleHeight(point));if(!Hit(eye,ray,radius+height,out var next))break;point=next;}
            var direction=point*(1/point.Magnitude);double ground=radius+Math.Max(body.oceanHeight,surface.SampleHeight(direction));
            double clearance=Math.Max(16,length-radius-Math.Max(body.oceanHeight,surface.SampleHeight(eye)));
            // Move the LOD priority to the viewed latitude/longitude, retaining
            // the real observer's altitude so orbital views do not request 1 m
            // triangles at their look target.
            focus=(direction*(ground+clearance)).Float;return true;
        }
    }
}
