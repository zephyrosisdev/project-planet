using UnityEngine;
namespace MassiveArmy
{
    /// <summary>Half-open torus [-size/2,size/2). Exact half-period ties choose the negative direction.</summary>
    public static class WorldTopology
    {
        public static Vector3 ViewFocus(Camera camera){var rig=camera.GetComponent<RtsCamera>();if(rig!=null)return rig.Focus;var ray=camera.ViewportPointToRay(new Vector3(.5f,.5f));return Mathf.Abs(ray.direction.y)>.001f?ray.GetPoint(-ray.origin.y/ray.direction.y):camera.transform.position;}
        public static float Wrap(float p,float size)=>p-Mathf.Floor(p/size+.5f)*size;
        public static Vector2 Wrap(Vector2 p,float size)=>new Vector2(Wrap(p.x,size),Wrap(p.y,size));
        public static Vector2 Delta(Vector2 from,Vector2 to,float size)=>Wrap(to-from,size);
        public static float Distance(Vector2 a,Vector2 b,float size)=>Delta(a,b,size).magnitude;
        public static int Cell(int i,int resolution)=>((i%resolution)+resolution)%resolution;
        public static int Index(int x,int y,int resolution)=>Cell(x,resolution)+Cell(y,resolution)*resolution;
        public static Vector3 Nearest(Vector3 p,Vector3 reference,float size){var d=Delta(new Vector2(reference.x,reference.z),new Vector2(p.x,p.z),size);return new Vector3(reference.x+d.x,p.y,reference.z+d.y);}
    }
}
