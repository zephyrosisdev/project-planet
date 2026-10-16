using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    /// <summary>Construction geometry only. Army routing remains on its existing navigation grid.</summary>
    public static class BuildingGrid
    {
        public const float TileSize=8;
        public static int Rotation(int rotation)=>((rotation%4)+4)%4;
        public static int Resolution(float worldSize)=>Mathf.RoundToInt(worldSize/TileSize);
        public static Vector2Int Dimensions(BuildingDefinition d,int rotation=0)=>
            (rotation&1)==0?new Vector2Int(d.footprintWidth,d.footprintDepth):new Vector2Int(d.footprintDepth,d.footprintWidth);
        public static Vector2Int Anchor(BuildingDefinition d,Vector2 point,float worldSize,int rotation=0)
        {
            var size=Dimensions(d,rotation);int n=Resolution(worldSize);
            var uv=(WorldTopology.Wrap(point,worldSize)+Vector2.one*(worldSize*.5f))/TileSize;
            return new Vector2Int(WorldTopology.Cell(Mathf.RoundToInt(uv.x-size.x*.5f),n),WorldTopology.Cell(Mathf.RoundToInt(uv.y-size.y*.5f),n));
        }
        public static Vector2 Centre(BuildingDefinition d,Vector2Int anchor,float worldSize,int rotation=0)
        {
            var size=Dimensions(d,rotation);
            return WorldTopology.Wrap((new Vector2(anchor.x,anchor.y)+new Vector2(size.x,size.y)*.5f)*TileSize-Vector2.one*(worldSize*.5f),worldSize);
        }
        public static Vector2 Snap(BuildingDefinition d,Vector2 point,float worldSize,int rotation=0)=>Centre(d,Anchor(d,point,worldSize,rotation),worldSize,rotation);
        public static Vector2Int ResourceDimensions(NodeKind kind,RtsCatalog catalog=null)
        {
            string id=kind==NodeKind.Oil?"oil":kind==NodeKind.Gas?"gas":kind==NodeKind.RareEarth?"rare":kind==NodeKind.Uranium?"isotope":"mine";
            if(catalog!=null)return Dimensions(catalog.Buildings[id]);
            int cells=kind==NodeKind.Oil||kind==NodeKind.Gas?2:3;return new Vector2Int(cells,cells);
        }
        public static Vector2 SnapResource(NodeKind kind,Vector2 point,float worldSize,RtsCatalog catalog=null)
        {
            var size=ResourceDimensions(kind,catalog);var half=new Vector2(size.x,size.y)*.5f;
            var uv=(WorldTopology.Wrap(point,worldSize)+Vector2.one*(worldSize*.5f))/TileSize-half;
            return WorldTopology.Wrap((new Vector2(Mathf.RoundToInt(uv.x),Mathf.RoundToInt(uv.y))+half)*TileSize-Vector2.one*(worldSize*.5f),worldSize);
        }
        public static int CellIndex(int x,int y,float worldSize)=>WorldTopology.Index(x,y,Resolution(worldSize));
        public static Vector2 CellCentre(int index,float worldSize)
        {
            int n=Resolution(worldSize);return (new Vector2(index%n,index/n)+Vector2.one*.5f)*TileSize-Vector2.one*(worldSize*.5f);
        }
        public static IEnumerable<int> Cells(BuildingDefinition d,Vector2 point,float worldSize,int rotation=0)
        {
            var a=Anchor(d,point,worldSize,rotation);var size=Dimensions(d,rotation);
            for(int z=0;z<size.y;z++)for(int x=0;x<size.x;x++)yield return CellIndex(a.x+x,a.y+z,worldSize);
        }
        public static bool Contains(BuildingDefinition d,Vector2 centre,Vector2 point,float worldSize,int rotation=0,float margin=0)
        {
            var delta=WorldTopology.Delta(centre,point,worldSize);var size=Dimensions(d,rotation);
            return Mathf.Abs(delta.x)<size.x*TileSize*.5f+margin&&Mathf.Abs(delta.y)<size.y*TileSize*.5f+margin;
        }
        public static Vector2 Forward(int rotation)
        {
            switch(rotation&3){case 1:return Vector2.right;case 2:return Vector2.down;case 3:return Vector2.left;default:return Vector2.up;}
        }
    }

    /// <summary>Bounded four-neighbour A* for a construction gesture, never for soldiers.</summary>
    public sealed class WallPreviewRouter
    {
        public const int MaxSegments=128,MaxExpansions=16384;
        readonly Dictionary<int,int> distance=new Dictionary<int,int>(),parent=new Dictionary<int,int>();
        readonly List<(int cell,int score,int cost)> heap=new List<(int,int,int)>();
        readonly List<int> reversed=new List<int>();
        static readonly Vector2Int[] directions={Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
        int Heuristic(int a,int b,int n)
        {
            int x=Math.Abs(a%n-b%n),y=Math.Abs(a/n-b/n);return Math.Min(x,n-x)+Math.Min(y,n-y);
        }
        void Push((int cell,int score,int cost) item)
        {
            int i=heap.Count;heap.Add(item);while(i>0){int p=(i-1)/2;if(heap[p].score<=item.score)break;heap[i]=heap[p];i=p;}heap[i]=item;
        }
        (int cell,int score,int cost) Pop()
        {
            var result=heap[0];var item=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);
            if(heap.Count>0){int i=0;while(i*2+1<heap.Count){int child=i*2+1;if(child+1<heap.Count&&heap[child+1].score<heap[child].score)child++;if(heap[child].score>=item.score)break;heap[i]=heap[child];i=child;}heap[i]=item;}return result;
        }
        public bool Find(int start,int end,int resolution,Func<int,bool> buildable,List<int> output,out string reason)
        {
            output.Clear();distance.Clear();parent.Clear();heap.Clear();reversed.Clear();reason="";
            if(Heuristic(start,end,resolution)>=MaxSegments){reason="Wall route exceeds 128 cells";return false;}
            if(!buildable(start)||!buildable(end)){reason="Wall endpoint is blocked";return false;}
            distance[start]=0;Push((start,Heuristic(start,end,resolution),0));int expanded=0;
            while(heap.Count>0&&expanded<MaxExpansions){
                var current=Pop();if(distance[current.cell]!=current.cost)continue;expanded++;
                if(current.cell==end){
                    int c=end;reversed.Add(c);while(c!=start){c=parent[c];reversed.Add(c);}if(reversed.Count>MaxSegments){reason="Wall route exceeds 128 cells";return false;}
                    for(int i=reversed.Count-1;i>=0;i--)output.Add(reversed[i]);return true;
                }
                if(current.cost+Heuristic(current.cell,end,resolution)>=MaxSegments)continue;
                foreach(var step in directions){int next=WorldTopology.Index(current.cell%resolution+step.x,current.cell/resolution+step.y,resolution),cost=current.cost+1;
                    if(Heuristic(start,next,resolution)+Heuristic(next,end,resolution)>Heuristic(start,end,resolution)+32)continue;
                    if((distance.TryGetValue(next,out int old)&&old<=cost)||!buildable(next))continue;
                    distance[next]=cost;parent[next]=current.cell;Push((next,cost+Heuristic(next,end,resolution),cost));
                }
            }
            reason=expanded>=MaxExpansions?"Wall preview search limit reached":"No buildable wall route";return false;
        }
    }
}
