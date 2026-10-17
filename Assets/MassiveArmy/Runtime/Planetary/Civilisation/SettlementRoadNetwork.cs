using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum RoadShape {Isolated,End,Straight,Corner,Tee,Cross}
    [Serializable] public sealed class RoadTile {public PlanetNavCoordinate cell;public int faction;public float height;public int connections;
        public RoadShape Shape=>Classify(connections);
        public static RoadShape Classify(int mask){int count=0;for(int i=0;i<4;i++)count+=(mask>>i)&1;return count==4?RoadShape.Cross:count==3?RoadShape.Tee:count==2?(mask==3||mask==12?RoadShape.Straight:RoadShape.Corner):count==1?RoadShape.End:RoadShape.Isolated;}}
    /// <summary>Sparse spherical road graph, independent of the coarse GPU army navigation field.</summary>
    public sealed class SettlementRoadNetwork {
        readonly PlanetDevelopment world;readonly PlanetRtsBase local;readonly List<RoadTile> saved;readonly Dictionary<long,int> tiles=new Dictionary<long,int>();
        readonly int resolution;readonly List<(RtsBuilding building,PlanetConstructionGrid.Footprint area)> occupied=new List<(RtsBuilding,PlanetConstructionGrid.Footprint)>();public int Revision {get;private set;}
        readonly Dictionary<long,int> indices=new Dictionary<long,int>();int[] components=Array.Empty<int>();
        internal struct GpuNode {public Vector4 position;public int west,east,south,north;}
        internal GpuNode[] Nodes {get;private set;}=Array.Empty<GpuNode>();
        public IReadOnlyList<RoadTile> Tiles=>saved;
        public SettlementRoadNetwork(PlanetDevelopment world,PlanetRtsBase local,List<RoadTile> saved,float tileSize){this.world=world;this.local=local;this.saved=saved;resolution=local.Grid.Resolution;RefreshFootprints();foreach(var tile in saved)tiles[Key(tile.cell)]=tile.faction;RebuildGraph();}
        static long Key(PlanetNavCoordinate c)=>((long)(int)c.face<<48)|((long)c.x<<24)|(uint)c.y;
        public Vector3 Direction(PlanetNavCoordinate cell)=>CubeSphere.Direction(cell,resolution);
        PlanetNavCoordinate Cell(Vector3 direction)=>CubeSphere.Cell(world.definition.bodyId,direction,resolution);
        static PlanetNavCoordinate Neighbour(PlanetNavCoordinate cell,int index,int resolution)=>CubeSphere.Neighbour(cell,index==0?-1:index==1?1:0,index==2?-1:index==3?1:0,resolution);
        public Vector3 Port(RtsBuilding building)=>Direction(local.Grid.Port(local.Area(building)));
        public bool HasRoad(PlanetNavCoordinate cell)=>tiles.ContainsKey(Key(cell));
        void RefreshFootprints(){occupied.Clear();foreach(var b in local.Buildings)if(!b.removed)occupied.Add((b,local.Area(b)));}
        public void RefreshBuildings(){RefreshFootprints();if(saved.Count>0){Revision++;RebuildGraph();}}
        bool Allowed(PlanetNavCoordinate cell,int faction,PlanetSurface surface){
            if(tiles.TryGetValue(Key(cell),out int owner))return owner==faction;var direction=Direction(cell);double height=surface.SampleHeight(direction);if(height<world.definition.oceanHeight+.1)return false;
            foreach(var entry in occupied)if(entry.area.Contains(cell))return false;return true;
        }
        public bool Place(Vector3 start,Vector3 finish,int faction,out string reason,bool starter=false){
            var surface=new PlanetSurface(world.definition);var a=Cell(start);var b=Cell(finish);var path=Search(a,b,faction,surface);
            if(path==null){reason="No dry, clear road route within the placement search budget";return false;}
            int additions=0;foreach(var cell in path)if(!tiles.ContainsKey(Key(cell)))additions++;
            var settings=world.SystemRuntime.Civilisation.Settings;if(saved.Count+additions>settings.maximumRoadTiles){reason="Road capacity reached";return false;}
            if(!starter&&!local.Factions[faction].wallet.Spend(new ResourceCost{materials=additions*settings.roadTileCost})){reason="Insufficient construction materials for this road";return false;}
            foreach(var cell in path)if(!tiles.ContainsKey(Key(cell))){tiles.Add(Key(cell),faction);saved.Add(new RoadTile{cell=cell,faction=faction,height=surface.SampleHeight(Direction(cell))});}
            if(additions>0){Revision++;RebuildGraph();}reason=additions+" road tiles placed";return true;
        }
        List<PlanetNavCoordinate> Search(PlanetNavCoordinate start,PlanetNavCoordinate finish,int faction,PlanetSurface surface){
            long goal=Key(finish),origin=Key(start);
            if(!Allowed(start,faction,surface)||!Allowed(finish,faction,surface))return null;
            var open=new SortedSet<(double score,long key)>();var costs=new Dictionary<long,double>();var cells=new Dictionary<long,PlanetNavCoordinate>();var previous=new Dictionary<long,long>();var closed=new HashSet<long>();
            double Heuristic(PlanetNavCoordinate cell)=>CubeSphere.ArcDistance(Direction(cell),Direction(finish),world.definition.radius);
            costs[origin]=0;cells[origin]=start;open.Add((Heuristic(start),origin));int budget=world.SystemRuntime.Civilisation.Settings.roadSearchBudget;
            while(open.Count>0&&budget-->0){var best=open.Min;open.Remove(best);long key=best.key;if(!closed.Add(key))continue;var cell=cells[key];
                if(key==goal){var path=new List<PlanetNavCoordinate>();for(long cursor=goal;;cursor=previous[cursor]){path.Add(cells[cursor]);if(cursor==origin)break;}path.Reverse();return path;}
                for(int i=0;i<4;i++){var neighbour=Neighbour(cell,i,resolution);long next=Key(neighbour);if(closed.Contains(next))continue;
                    if(!Allowed(neighbour,faction,surface))continue;float rise=Mathf.Abs(surface.SampleHeight(Direction(neighbour))-surface.SampleHeight(Direction(cell)));if(rise>world.SystemRuntime.Civilisation.Settings.roadMaximumStepHeight)continue;
                    double distance=CubeSphere.ArcDistance(Direction(cell),Direction(neighbour),world.definition.radius),cost=costs[key]+distance;
                    if(costs.TryGetValue(next,out var old)&&old<=cost)continue;costs[next]=cost;cells[next]=neighbour;previous[next]=key;open.Add((cost+Heuristic(neighbour),next));
                }
            }return null;
        }
        // CPU component labels describe infrastructure connectivity; live cargo paths are searched only by SettlementRoad.compute.
        public bool Connected(RtsBuilding from,RtsBuilding to){int a=Node(Port(from)),b=Node(Port(to));return from.team==to.team&&a>=0&&b>=0&&saved[a].faction==from.team&&saved[b].faction==from.team&&components[a]==components[b];}
        internal int Node(Vector3 direction)=>indices.TryGetValue(Key(Cell(direction)),out int value)?value:-1;
        void RebuildGraph(){
            indices.Clear();for(int i=0;i<saved.Count;i++)indices[Key(saved[i].cell)]=i;Nodes=new GpuNode[saved.Count];components=new int[saved.Count];Array.Fill(components,-1);
            for(int i=0;i<saved.Count;i++){var tile=saved[i];int[] neighbours={-1,-1,-1,-1};tile.connections=0;for(int edge=0;edge<4;edge++)if(indices.TryGetValue(Key(Neighbour(tile.cell,edge,resolution)),out int other)&&saved[other].faction==tile.faction){neighbours[edge]=other;tile.connections|=1<<edge;}Vector3 p=Direction(tile.cell)*(world.definition.radius+tile.height+1);Nodes[i]=new GpuNode{position=new Vector4(p.x,p.y,p.z,tile.faction),west=neighbours[0],east=neighbours[1],south=neighbours[2],north=neighbours[3]};}
            // Entrances add a visible road arm, never a traversable node inside a building.
            foreach(var entry in occupied){var port=local.Grid.Port(entry.area);if(!indices.TryGetValue(Key(port),out int node)||saved[node].faction!=entry.building.team)continue;long door=Key(local.Grid.Door(entry.area));for(int edge=0;edge<4;edge++)if(Key(Neighbour(port,edge,resolution))==door)saved[node].connections|=1<<edge;}
            var pending=new Queue<int>();for(int i=0;i<saved.Count;i++){if(components[i]>=0)continue;components[i]=i;pending.Enqueue(i);while(pending.Count>0){int node=pending.Dequeue();var n=Nodes[node];Visit(n.west,i);Visit(n.east,i);Visit(n.south,i);Visit(n.north,i);}}
            void Visit(int node,int component){if(node<0||components[node]>=0)return;components[node]=component;pending.Enqueue(node);}
        }
        internal Vector3 Corner(RoadTile tile,int x,int y)=>CubeSphere.Direction(tile.cell.face,new Vector2((tile.cell.x+x)*2f/resolution-1,(tile.cell.y+y)*2f/resolution-1));
        public bool ConnectToNetwork(RtsBuilding building,RtsBuilding warehouse,out string reason){
            if(building.team!=warehouse.team){reason="Cannot connect enemy infrastructure";return false;}
            if(Connected(building,warehouse)){reason="Already connected";return true;}
            int root=Node(Port(warehouse));Vector3 target=Port(warehouse),start=Port(building);float best=float.MaxValue;
            if(root>=0)for(int i=0;i<saved.Count;i++)if(saved[i].faction==building.team&&components[i]==components[root]){Vector3 d=Direction(saved[i].cell);float distance=(d-start).sqrMagnitude;if(distance<best){best=distance;target=d;}}
            if(local.Factions[building.team].wallet.Stock.materials<world.SystemRuntime.Civilisation.Settings.roadTileCost){reason="Reserve materials needed for HQ road connection";return false;}
            return Place(start,target,building.team,out reason);
        }
        public bool Connect(RtsBuilding from,RtsBuilding to,out string reason,bool starter=false){if(from.team!=to.team){reason="Cannot connect enemy infrastructure";return false;}return Place(Port(from),Port(to),from.team,out reason,starter);}
    }
}
