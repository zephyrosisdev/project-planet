using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    public static class ArmyMeshes
    {
        sealed class Builder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colours = new List<Color>();
            public void Box(Vector3 centre, Vector3 size, bool turret, float emission, int part=0)
            {
                Vector3 h=size*.5f;
                var p=new[] { new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(-h.x,h.y,-h.z),new Vector3(-h.x,-h.y,h.z),new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z) };
                int[] faces={0,3,2,1, 5,6,7,4, 4,7,3,0, 1,2,6,5, 3,7,6,2, 4,0,1,5};
                for(int face=0;face<6;face++)
                {
                    int n=vertices.Count;
                    for(int j=0;j<4;j++) { vertices.Add(centre+p[faces[face*4+j]]); uv.Add(new Vector2(turret?1:0,0)); colours.Add(new Color(emission,part,0,1)); }
                    triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                }
            }
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,float shade)
            {
                int n=vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); triangles.AddRange(new[]{n,n+1,n+2});
                for(int j=0;j<3;j++) { uv.Add(Vector2.zero); colours.Add(new Color(shade,0,0,1)); }
            }
            public Mesh Build(string name)
            {
                var mesh=new Mesh{name=name}; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetColors(colours); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
        public static Mesh Tank(bool near)
        {
            var b=new Builder();
            b.Box(new Vector3(0,.5f,0),new Vector3(1.8f,.65f,3),false,0);
            b.Box(new Vector3(-1,.4f,0),new Vector3(.45f,.7f,3.4f),false,.03f,1);
            b.Box(new Vector3(1,.4f,0),new Vector3(.45f,.7f,3.4f),false,.03f,2);
            b.Box(new Vector3(0,1.1f,0),new Vector3(1.3f,.55f,1.4f),true,.05f,3);
            b.Box(new Vector3(0,1.15f,1.6f),new Vector3(.17f,.17f,2.2f),true,.5f,4);
            if(near)
            {
                b.Box(new Vector3(-.8f,.85f,0),new Vector3(.08f,.08f,2.6f),false,1,5);
                b.Box(new Vector3(.8f,.85f,0),new Vector3(.08f,.08f,2.6f),false,1,5);
                b.Box(new Vector3(0,1.42f,-.2f),new Vector3(.7f,.06f,.16f),true,1,5);
                b.Box(new Vector3(0,.5f,-1.55f),new Vector3(.9f,.25f,.08f),false,1,5);
            }
            return b.Build(near?"Neon tank • chassis tracks turret barrel":"Simplified tank");
        }
        public static Mesh Quad()
        {
            var m=new Mesh{name="GPU billboard"};
            m.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            m.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}; m.colors=new[]{Color.white,Color.white,Color.white,Color.white}; m.triangles=new[]{0,2,1,0,3,2};m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        public static Mesh Tree()
        {
            var b=new Builder();
            for(int layer=0;layer<3;layer++)
            {
                float baseY=.5f+layer*.75f,r=1-layer*.23f;
                for(int side=0;side<5;side++)
                {
                    float a=side*Mathf.PI*2/5,c=(side+1)*Mathf.PI*2/5;
                    b.Triangle(new Vector3(Mathf.Cos(a)*r,baseY,Mathf.Sin(a)*r),new Vector3(0,baseY+1.7f,0),new Vector3(Mathf.Cos(c)*r,baseY,Mathf.Sin(c)*r),layer/3f+.2f);
                }
            }
            return b.Build("Synthetic conifer");
        }
        public static Mesh Scenery(int style)
        {
            if(style==0)return Tree();
            var b=new Builder();
            void Crown(Vector3 centre,Vector3 scale,int sides=7)
            {
                for(int j=0;j<sides;j++) {
                    float a=j*Mathf.PI*2/sides,c=(j+1)*Mathf.PI*2/sides;
                    Vector3 p=centre+Vector3.Scale(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),scale),q=centre+Vector3.Scale(new Vector3(Mathf.Cos(c),0,Mathf.Sin(c)),scale);
                    b.Triangle(p,centre+Vector3.up*scale.y,q,.9f);
                    b.Triangle(q,centre-Vector3.up*scale.y*.45f,p,.7f);
                }
            }
            if(style==5) { Crown(new Vector3(0,.6f,0),new Vector3(1.25f,1.05f,.85f),7);Crown(new Vector3(.6f,.2f,.3f),new Vector3(.8f,.7f,.9f),5); }
            else if(style==6) { Crown(new Vector3(0,.35f,0),new Vector3(.8f,.8f,.7f),6); }
            else if(style==4) {
                b.Box(new Vector3(0,1.25f,0),new Vector3(.4f,2.5f,.4f),false,.7f);
                b.Box(new Vector3(.5f,1.45f,0),new Vector3(.8f,.3f,.3f),false,.8f);
                b.Box(new Vector3(.8f,1.8f,0),new Vector3(.3f,1,.3f),false,.85f);
                b.Box(new Vector3(-.5f,.95f,0),new Vector3(.8f,.3f,.3f),false,.75f);
                b.Box(new Vector3(-.8f,1.3f,0),new Vector3(.3f,1,.3f),false,.9f);
            }
            else {
                float h=style==2?3.9f:style==3?2.2f:2.5f;
                b.Box(new Vector3(0,h*.45f,0),new Vector3(.23f,h*.9f,.23f),false,.2f);
                if(style==2) {
                    for(int j=0;j<8;j++) { float a=j*Mathf.PI/4;Vector3 tip=new Vector3(Mathf.Cos(a)*1.7f,h-.3f,Mathf.Sin(a)*1.7f);
                        b.Triangle(new Vector3(0,h,0),tip,new Vector3(Mathf.Cos(a+.35f)*.6f,h+.1f,Mathf.Sin(a+.35f)*.6f),.9f); }
                }
                else Crown(new Vector3(0,h,0),new Vector3(style==3?1.8f:1.25f,style==3?.5f:1.15f,style==3?1.5f:1.1f));
            }
            return b.Build(new[]{"Conifer","Broadleaf","Palm","Acacia","Cactus","Boulder","Shrub"}[style]);
        }
        public static Texture2D Atlas()
        {
            const int tile=32;
            var texture=new Texture2D(tile*8,tile,TextureFormat.RGBA32,false,true){name="Tank 8-direction pixel atlas",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[tile*tile*8];
            for(int direction=0;direction<8;direction++) for(int y=0;y<tile;y++) for(int x=0;x<tile;x++)
            {
                float a=direction*Mathf.PI/4,px=(x+ .5f-tile*.5f)/tile*5,pz=(y+.5f-tile*.5f)/tile*5;
                float rx=Mathf.Cos(a)*px-Mathf.Sin(a)*pz,rz=Mathf.Sin(a)*px+Mathf.Cos(a)*pz;
                Color c=Color.clear;
                if(Mathf.Abs(rx)<1.15f && Mathf.Abs(rz)<1.7f) c=new Color(.25f,.35f,.45f,1);
                if(Mathf.Abs(rx)<.85f && Mathf.Abs(rz)<1.5f) c=new Color(.4f,.5f,.65f,1);
                if(Mathf.Abs(rx)<.7f && Mathf.Abs(rz)<.75f) c=new Color(.7f,.8f,1,1);
                if(Mathf.Abs(rx)<.14f && rz>.5f && rz<2.5f) c=Color.white;
                if(Mathf.Abs(Mathf.Abs(rx)-.8f)<.13f && Mathf.Abs(rz)<1.4f) c=Color.white;
                pixels[direction*tile+x+y*tile*8]=c;
            }
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }
    }
}
