using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    public enum CubeFace { PositiveX,NegativeX,PositiveY,NegativeY,PositiveZ,NegativeZ }
    [Serializable] public struct PlanetNavCoordinate
    {
        public int planetId;
        public CubeFace face;
        public int x,y;
        public PlanetNavCoordinate(int planet,CubeFace face,int x,int y){planetId=planet;this.face=face;this.x=x;this.y=y;}
        public int Index(int resolution)=>(int)face*resolution*resolution+y*resolution+x;
    }
    public static class CubeSphere
    {
        public static readonly Vector3[] Normal={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
        public static readonly Vector3[] Right={Vector3.back,Vector3.forward,Vector3.right,Vector3.right,Vector3.right,Vector3.left};
        public static readonly Vector3[] Up={Vector3.up,Vector3.up,Vector3.back,Vector3.forward,Vector3.up,Vector3.up};
        public static Vector3 Direction(CubeFace face,Vector2 uv){int f=(int)face;return (Normal[f]+Right[f]*uv.x+Up[f]*uv.y).normalized;}
        public static void Project(Vector3 direction,out CubeFace face,out Vector2 uv)
        {
            if(direction.sqrMagnitude<1e-20f)throw new ArgumentException("Planet direction is zero");
            var a=new Vector3(Mathf.Abs(direction.x),Mathf.Abs(direction.y),Mathf.Abs(direction.z));
            int f=a.x>=a.y&&a.x>=a.z?(direction.x>=0?0:1):a.y>=a.z?(direction.y>=0?2:3):(direction.z>=0?4:5);
            face=(CubeFace)f;float d=Vector3.Dot(direction,Normal[f]);uv=new Vector2(Vector3.Dot(direction,Right[f]),Vector3.Dot(direction,Up[f]))/d;
        }
        public static PlanetNavCoordinate Cell(int planet,Vector3 direction,int resolution)
        {
            Project(direction,out var face,out var uv);
            return new PlanetNavCoordinate(planet,face,Mathf.Clamp(Mathf.FloorToInt((uv.x*.5f+.5f)*resolution),0,resolution-1),Mathf.Clamp(Mathf.FloorToInt((uv.y*.5f+.5f)*resolution),0,resolution-1));
        }
        public static Vector3 Direction(PlanetNavCoordinate c,int resolution)=>Direction(c.face,new Vector2((c.x+.5f)*2/resolution-1,(c.y+.5f)*2/resolution-1));
        // edge order: west,east,south,north. Target edge plus reversal of the edge parameter.
        // The same table is used in PlanetTopology.hlsl. All 24 transitions are tested.
        static readonly int[,] Faces={{4,5,3,2},{5,4,3,2},{1,0,4,5},{1,0,5,4},{1,0,3,2},{0,1,3,2}};
        static readonly int[,] Edges={{1,0,1,1},{1,0,0,0},{3,3,3,3},{2,2,2,2},{1,0,3,2},{1,0,2,3}};
        static readonly bool[,] Reverse={{false,false,true,false},{false,false,false,true},{true,false,false,true},{false,true,true,false},{false,false,false,false},{false,false,true,true}};
        public static PlanetNavCoordinate Neighbour(PlanetNavCoordinate c,int dx,int dy,int resolution)
        {
            if(Math.Abs(dx)+Math.Abs(dy)!=1)throw new ArgumentException("Use a cardinal seam step; corners are traversed through edge neighbours");
            int x=c.x+dx,y=c.y+dy;if(x>=0&&x<resolution&&y>=0&&y<resolution)return new PlanetNavCoordinate(c.planetId,c.face,x,y);
            int edge=x<0?0:x>=resolution?1:y<0?2:3,f=(int)c.face,t=edge<2?c.y:c.x;
            if(Reverse[f,edge])t=resolution-1-t;
            int e=Edges[f,edge];return new PlanetNavCoordinate(c.planetId,(CubeFace)Faces[f,edge],e<2?(e==0?0:resolution-1):t,e>=2?(e==2?0:resolution-1):t);
        }
        public static float ArcDistance(Vector3 a,Vector3 b,float radius)=>Mathf.Atan2(Vector3.Cross(a,b).magnitude,Vector3.Dot(a,b))*radius;
        // Six faces in a horizontal atlas: compact float2 surface positions, never physical metres.
        public static Vector2 Encode(Vector3 direction){Project(direction,out var f,out var uv);return new Vector2((int)f+Mathf.Clamp((uv.x+1)*.5f,0,1-1e-6f),Mathf.Clamp01((uv.y+1)*.5f));}
        public static Vector3 Decode(Vector2 p){int f=Mathf.Clamp(Mathf.FloorToInt(p.x),0,5);return Direction((CubeFace)f,new Vector2((p.x-f)*2-1,p.y*2-1));}
    }
}
