using UnityEngine;
namespace MassiveArmy.Planetary
{
    // Integer-hashed 3D value noise. The shader uses the same hash/interpolation;
    // coordinates belong to the body, independent of cube faces and render origins.
    public static class PlanetClimate
    {
        static float Hash(int x,int y,int z){unchecked{uint h=(uint)x*1597334677u^(uint)y*3812015801u^(uint)z*2798796415u;h^=h>>16;h*=2246822519u;h^=h>>13;return (h&0xffffffu)/16777215f;}}
        public static float Noise(Vector3 p){int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);var f=p-new Vector3(x,y,z);f=new Vector3(f.x*f.x*(3-2*f.x),f.y*f.y*(3-2*f.y),f.z*f.z*(3-2*f.z));
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(Hash(x,y,z),Hash(x+1,y,z),f.x),Mathf.Lerp(Hash(x,y+1,z),Hash(x+1,y+1,z),f.x),f.y),Mathf.Lerp(Mathf.Lerp(Hash(x,y,z+1),Hash(x+1,y,z+1),f.x),Mathf.Lerp(Hash(x,y+1,z+1),Hash(x+1,y+1,z+1),f.x),f.y),f.z);}
        public static Vector2 Sample(Vector3 direction,float seed){var d=direction.normalized;var p=new Vector3(d.x*.8f+d.y*.6f,-d.x*.48f+d.y*.64f+d.z*.6f,d.x*.36f-d.y*.48f+d.z*.8f);p+=new Vector3(seed,seed*.37f,-seed*.61f);
            float temperature=Mathf.Clamp01(1-Mathf.Abs(d.y)+(Noise(p*3.1f+new Vector3(17,43,11))-.5f)*.24f);
            float moisture=Mathf.Clamp01(Noise(p*4.3f)*.75f+Noise(p*9.7f+new Vector3(31,7,53))*.25f);return new Vector2(temperature,moisture);}
    }
}
