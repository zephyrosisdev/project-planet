using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy {
    // Editor/batched static geometry only. There is no module GameObject or per-unit mesh.
    public sealed class ModularGeometry {
        readonly List<Vector3> positions=new List<Vector3>(),normals=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<Color> colours=new List<Color>();readonly List<int> indices=new List<int>();
        public int Triangles=>indices.Count/3;
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color colour){int first=positions.Count;var n=Vector3.Cross(b-a,c-a).normalized;positions.AddRange(new[]{a,b,c,d});for(int i=0;i<4;i++){normals.Add(n);colours.Add(colour);}uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});indices.AddRange(new[]{first,first+1,first+2,first,first+2,first+3});}
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color colour){int first=positions.Count;var n=Vector3.Cross(b-a,c-a).normalized;positions.AddRange(new[]{a,b,c});for(int i=0;i<3;i++){normals.Add(n);colours.Add(colour);}uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one});indices.AddRange(new[]{first,first+1,first+2});}
        public void Box(Vector3 centre,Vector3 size,Color colour,Quaternion? rotation=null){var q=rotation??Quaternion.identity;var h=size*.5f;
            foreach(var n in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back}){var u=Mathf.Abs(n.y)>.5f?Vector3.right:Vector3.Cross(Vector3.up,n);var v=Vector3.Cross(n,u);Vector3 P(float x,float y)=>centre+q*Vector3.Scale(n+u*x+v*y,h);Quad(P(-1,-1),P(1,-1),P(1,1),P(-1,1),colour);}}
        public void Mesh(Mesh mesh,Vector3 centre,Vector3 size,Quaternion rotation,Color colour){if(mesh==null){Box(centre,size,colour,rotation);return;}if(!mesh.isReadable)throw new InvalidOperationException(mesh.name+": enable model Read/Write before baking modules.");var bounds=mesh.bounds;var scale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));var p=mesh.vertices;var n=mesh.normals;var coords=mesh.uv;int start=positions.Count;
            for(int i=0;i<p.Length;i++){positions.Add(centre+rotation*Vector3.Scale(p[i]-bounds.center,scale));var normal=n.Length==p.Length?n[i]:Vector3.up;normals.Add(rotation*new Vector3(normal.x/scale.x,normal.y/scale.y,normal.z/scale.z).normalized);uv.Add(coords.Length==p.Length?coords[i]:Vector2.zero);colours.Add(colour);}foreach(int i in mesh.triangles)indices.Add(start+i);}
        public Mesh Build(string name){var mesh=new Mesh{name=name,indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetColors(colours);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;}
        public void Text(string text,Vector3 centre,float height,Color colour,Quaternion rotation){float pitch=height*.72f;float left=-(text.Length-1)*pitch*.5f;for(int i=0;i<text.Length;i++){int mask=Segments(text[i]);for(int segment=0;segment<7;segment++)if((mask&(1<<segment))!=0){bool horizontal=segment==0||segment==3||segment==6;Vector3 p=segment==0?new Vector3(0,.45f,0):segment==3?Vector3.zero:segment==6?new Vector3(0,-.45f,0):new Vector3(segment==1||segment==4?-.24f:.24f,segment<3?.23f:-.23f,0);Box(centre+rotation*(new Vector3(left+i*pitch,0,0)+p*height),new Vector3(horizontal?.45f:.07f,horizontal?.07f:.4f,.025f)*height,colour,rotation);}}}
        static int Segments(char c){switch(c){case '0':case 'O':return 119;case '1':return 36;case '2':return 93;case '3':return 109;case '4':return 46;case '5':case 'S':return 107;case '6':return 123;case '7':return 37;case '8':return 127;case '9':return 111;case 'P':return 31;case 'T':return 90;default:return 0;}}
    }
}
