using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy
{
    // Shared unarmed humanoid shell. Vertex UV.x identifies exposed skin; UV.y
    // identifies left/right legs for inexpensive GPU gait. No unit GameObjects.
    public static class ZombieGeometry
    {
        public static Mesh Create()
        {
            var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            void Box(Vector3 centre,Vector3 size,Quaternion rotation,bool skin=false,float limb=0){
                var corners=new Vector3[8];for(int i=0;i<8;i++)corners[i]=centre+rotation*Vector3.Scale(new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f),size);
                var faces=new[]{0,2,3,1,4,5,7,6,0,4,6,2,1,3,7,5,0,1,5,4,2,6,7,3};
                for(int face=0;face<6;face++){
                    int a=faces[face*4],b=faces[face*4+1],c=faces[face*4+2],d=faces[face*4+3];Vector3 normal=Vector3.Cross(corners[b]-corners[a],corners[c]-corners[a]).normalized;
                    foreach(int corner in new[]{a,b,c,a,c,d}){triangles.Add(positions.Count);positions.Add(corners[corner]);normals.Add(normal);uv.Add(new Vector2(skin?1:0,limb));}
                }
            }
            Box(new Vector3(0,1.32f,.10f),new Vector3(.63f,.78f,.34f),Quaternion.Euler(12,0,0));
            Box(new Vector3(0,1.94f,.26f),new Vector3(.35f,.42f,.36f),Quaternion.Euler(9,0,-9),true);
            for(int side=-1;side<=1;side+=2){
                Box(new Vector3(side*.17f,.48f,0),new Vector3(.22f,.9f,.24f),Quaternion.Euler(side*8,0,side*4),false,side);
                Box(new Vector3(side*.17f,.08f,.12f),new Vector3(.25f,.17f,.43f),Quaternion.identity,false,side);
                Box(new Vector3(side*.42f,1.43f,.35f),new Vector3(.19f,.6f,.19f),Quaternion.Euler(-48,0,side*14),false);
                Box(new Vector3(side*.47f,1.28f,.73f),new Vector3(.16f,.5f,.17f),Quaternion.Euler(-78,0,side*10),true);
            }
            var mesh=new Mesh{name="Shared ragged unarmed zombie"};mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
        }
    }
}
