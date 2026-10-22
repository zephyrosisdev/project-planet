using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public static class PlanetWeatherProjection {
        // Exact perspective tangent bounds. A centre +/- radius/z rectangle
        // misses the nearer silhouette of off-axis spheres.
        public static bool Bounds(Vector3 centre,float radius,float tanHalfFov,float aspect,out Vector4 bounds){
            bounds=new Vector4(-1,-1,1,1);if(centre.z+radius<=0)return false;
            if(centre.z<=radius)return true;
            double z=centre.z,r=radius,den=z*z-r*r;
            void Extent(double axis,double scale,out float low,out float high){double root=r*Math.Sqrt(Math.Max(0,axis*axis+den));low=(float)((axis*z-root)/den/scale);high=(float)((axis*z+root)/den/scale);}
            Extent(centre.x,tanHalfFov*aspect,out float left,out float right);Extent(centre.y,tanHalfFov,out float bottom,out float top);
            bounds=new Vector4(Mathf.Max(-1,left),Mathf.Max(-1,bottom),Mathf.Min(1,right),Mathf.Min(1,top));return bounds.x<bounds.z&&bounds.y<bounds.w;
        }
    }
}
