using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy
{
    // One joint mask per shared catalogue mesh, never one mesh or actor per unit.
    public static class BuildingTurretGeometry
    {
        public static bool Articulated(BuildingDefinition definition)=>definition.weaponDamage>0&&definition.id!="silo";
        public static Mesh WithJoints(Mesh source)
        {
            var vertices=source.vertices;var triangles=source.triangles;
            var parents=new int[vertices.Length];for(int i=0;i<parents.Length;i++)parents[i]=i;
            int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            void Join(int a,int b){parents[Find(a)]=Find(b);}
            var welded=new Dictionary<Vector3,int>();
            for(int i=0;i<vertices.Length;i++){if(welded.TryGetValue(vertices[i],out int other))Join(i,other);else welded.Add(vertices[i],i);}
            for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
            var minimum=new Dictionary<int,float>();for(int i=0;i<vertices.Length;i++){int part=Find(i);minimum[part]=minimum.TryGetValue(part,out float y)?Mathf.Min(y,vertices[i].y):vertices[i].y;}
            // All nine authored defence shells have a fixed bearing below y=6.5
            // and independent head/barrel/launcher shells above that bearing.
            var joints=new List<Vector2>(vertices.Length);for(int i=0;i<vertices.Length;i++)joints.Add(new Vector2(minimum[Find(i)]>=6.49f?1:0,0));
            var result=Object.Instantiate(source);result.name=source.name+" · GPU turret joint";result.SetUVs(1,joints);
            float radius=0;foreach(var p in vertices)radius=Mathf.Max(radius,new Vector2(p.x,p.z).magnitude);
            var bounds=source.bounds;bounds.center=new Vector3(0,bounds.center.y,0);bounds.size=new Vector3(radius*2,bounds.size.y,radius*2);result.bounds=bounds;return result;
        }
    }
}
