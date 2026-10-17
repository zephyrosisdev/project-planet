using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>One cube-sphere lattice for roads, rotated building footprints and farm parcels.</summary>
    public sealed class PlanetConstructionGrid {
        public readonly int Planet,Resolution;public readonly float Radius;
        public PlanetConstructionGrid(int planet,float radius,float tileSize){Planet=planet;Radius=radius;Resolution=Math.Max(8,(int)Math.Ceiling(2*radius/Math.Max(1,tileSize)));}
        public readonly struct Footprint {
            public readonly CubeFace face;public readonly int x,y,width,depth,rotation;
            public Footprint(CubeFace face,int x,int y,int width,int depth,int rotation){this.face=face;this.x=x;this.y=y;this.width=width;this.depth=depth;this.rotation=rotation&3;}
            public bool Contains(PlanetNavCoordinate cell)=>cell.face==face&&cell.x>=x&&cell.x<x+width&&cell.y>=y&&cell.y<y+depth;
            public bool Overlaps(Footprint other)=>face==other.face&&x<other.x+other.width&&x+width>other.x&&y<other.y+other.depth&&y+depth>other.y;
        }
        public PlanetNavCoordinate Cell(Vector3 direction)=>CubeSphere.Cell(Planet,direction,Resolution);
        public Vector3 Direction(PlanetNavCoordinate cell)=>CubeSphere.Direction(cell,Resolution);
        public Vector3 Point(CubeFace face,double x,double y)=>CubeSphere.Direction(face,new Vector2((float)(2*x/Resolution-1),(float)(2*y/Resolution-1)));
        public Footprint Area(BuildingDefinition definition,Vector3 direction,int rotation){
            CubeSphere.Project(direction,out var face,out var uv);bool odd=(rotation&1)!=0;int width=odd?definition.footprintDepth:definition.footprintWidth,depth=odd?definition.footprintWidth:definition.footprintDepth;
            // Even footprints centre on grid lines; odd footprints centre on cells.
            int x=Mathf.Clamp((int)Math.Round((uv.x+1)*.5*Resolution-width*.5,MidpointRounding.AwayFromZero),0,Resolution-width);
            int y=Mathf.Clamp((int)Math.Round((uv.y+1)*.5*Resolution-depth*.5,MidpointRounding.AwayFromZero),0,Resolution-depth);
            return new Footprint(face,x,y,width,depth,rotation);
        }
        public Vector3 Centre(Footprint area)=>Point(area.face,area.x+area.width*.5,area.y+area.depth*.5);
        public Vector3 Snap(BuildingDefinition definition,Vector3 direction,int rotation)=>Centre(Area(definition,direction,rotation));
        public Vector3 Point(Footprint area,float u,float v)=>Point(area.face,area.x+u*area.width,area.y+v*area.depth);
        public PlanetNavCoordinate Neighbour(PlanetNavCoordinate cell,int edge)=>CubeSphere.Neighbour(cell,edge==0?-1:edge==1?1:0,edge==2?-1:edge==3?1:0,Resolution);
        public PlanetNavCoordinate Door(Footprint area){
            int x=area.x,y=area.y;switch(area.rotation){case 0:x+=area.width/2;y+=area.depth-1;break;case 1:y+=area.depth/2;break;case 2:x+=(area.width-1)/2;break;default:x+=area.width-1;y+=(area.depth-1)/2;break;}
            return new PlanetNavCoordinate(Planet,area.face,x,y);
        }
        public PlanetNavCoordinate Port(Footprint area)=>Neighbour(Door(area),area.rotation==0?3:area.rotation==1?0:area.rotation==2?2:1);
        public Quaternion Rotation(Footprint area){var up=Centre(area);var east=Vector3.ProjectOnPlane(CubeSphere.Right[(int)area.face],up).normalized;var north=Vector3.Cross(up,east);return Quaternion.AngleAxis(area.rotation*90,up)*Quaternion.LookRotation(north,up);}
        public void WarpAxes(Footprint area,out Vector3 x,out Vector3 z,out float planeLength){
            var inverse=Quaternion.Inverse(Rotation(area));var right=CubeSphere.Right[(int)area.face];var north=CubeSphere.Up[(int)area.face];
            Vector3 axisX=area.rotation==0?right:area.rotation==1?north:area.rotation==2?-right:-north,axisZ=area.rotation==0?north:area.rotation==1?-right:area.rotation==2?-north:right;
            float step=2f/(Resolution*8);x=inverse*axisX*step;z=inverse*axisZ*step;planeLength=1/Vector3.Dot(Centre(area),CubeSphere.Normal[(int)area.face]);
        }
        public static Vector3 Warp(Vector3 local,Vector3 x,Vector3 z,float planeLength,float foundation){
            var up=(Vector3.up*planeLength+x*local.x+z*local.z).normalized;float radius=foundation+local.y;
            // Stable sagitta: avoid subtracting two planet-sized floats for local height.
            float sag=(up.x*up.x+up.z*up.z)/(1+up.y)*radius;return new Vector3(up.x*radius,local.y-sag,up.z*radius);
        }
    }
}
