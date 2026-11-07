using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class ConstructionPlacement {
        internal static float PreviewDistance(Bounds bounds)=>Mathf.Clamp(Mathf.Max(3+new Vector2(bounds.extents.x,bounds.extents.z).magnitude,bounds.size.y*1.2f),4,11);
        internal static Double3 ForwardGround(PlanetDevelopment world,PlanetSurface surface,Ray ray,Double3 point,float distance){var eye=world.RenderToBody(ray.origin);var up=PlanetPrecisionPolicy.Direction(eye);var offset=Vector3.ProjectOnPlane((point-eye).Float,up);if(offset.magnitude>=distance)return point;var heading=Vector3.ProjectOnPlane(ray.direction,up).normalized;if(heading.sqrMagnitude<.01f)heading=Vector3.ProjectOnPlane(world.View.transform.forward,up).normalized;if(heading.sqrMagnitude<.01f)heading=Vector3.Cross(Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up,up).normalized;return surface.ProjectToSurface(eye+new Double3(heading)*distance);}
        internal static Double3 FitRoomSurface(PlanetSurface surface,Double3 centre,Quaternion rotation,Bounds bounds){var up=PlanetPrecisionPolicy.Direction(centre);double lift=0;for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){var point=centre+Double3.Rotate(rotation,new Double3(new Vector3(bounds.center.x+x*bounds.extents.x,0,bounds.center.z+z*bounds.extents.z)));var ground=surface.ProjectToSurface(point);lift=System.Math.Max(lift,(ground.x-point.x)*up.x+(ground.y-point.y)*up.y+(ground.z-point.z)*up.z);}return centre+new Double3(up)*(lift+.04);}
    }
}
