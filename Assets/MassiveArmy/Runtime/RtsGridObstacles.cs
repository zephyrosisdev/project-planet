using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        readonly HashSet<int> dirtyNavigationCells=new HashSet<int>(),dirtyStructureCells=new HashSet<int>();
        readonly Dictionary<int,HashSet<RtsBuilding>> navigationOccupants=new Dictionary<int,HashSet<RtsBuilding>>();
        int obstacleBatch;
        void RefreshCraterCosts(Bounds region)
        {
            presentation.RefreshTerrain(region);
            float cell=Field.Size/Field.Resolution;
            int x0=Mathf.FloorToInt((region.min.x+Field.Size*.5f)/cell),x1=Mathf.CeilToInt((region.max.x+Field.Size*.5f)/cell);
            int z0=Mathf.FloorToInt((region.min.z+Field.Size*.5f)/cell),z1=Mathf.CeilToInt((region.max.z+Field.Size*.5f)/cell);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)dirtyNavigationCells.Add(WorldTopology.Index(x,z,Field.Resolution));
            FlushGridObstacles();
        }
        void BeginObstacleBatch()=>obstacleBatch++;
        void EndObstacleBatch(){if(--obstacleBatch==0)FlushGridObstacles();}
        void SetObstacle(RtsBuilding b,bool add)
        {
            int n=Field.Resolution;float cell=Field.Size/n;var size=BuildingGrid.Dimensions(b.definition,b.rotation);
            Vector2 half=new Vector2(size.x,size.y)*4;
            int x0=Mathf.FloorToInt((b.position.x-half.x+Field.Size*.5f)/cell),x1=Mathf.FloorToInt((b.position.x+half.x-.001f+Field.Size*.5f)/cell);
            int z0=Mathf.FloorToInt((b.position.y-half.y+Field.Size*.5f)/cell),z1=Mathf.FloorToInt((b.position.y+half.y-.001f+Field.Size*.5f)/cell);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                int index=WorldTopology.Index(x,z,n);
                if(!navigationOccupants.TryGetValue(index,out var occupants))navigationOccupants[index]=occupants=new HashSet<RtsBuilding>();
                if(add)occupants.Add(b);else occupants.Remove(b);if(occupants.Count==0)navigationOccupants.Remove(index);
                dirtyNavigationCells.Add(index);
            }
            foreach(int index in BuildingGrid.Cells(b.definition,b.position,Field.Size,b.rotation))dirtyStructureCells.Add(index);
            if(obstacleBatch==0)FlushGridObstacles();
        }
        void FlushGridObstacles()
        {
            if(dirtyNavigationCells.Count==0)return;int total=Field.Resolution*Field.Resolution;
            var indices=new List<int>(dirtyNavigationCells);indices.Sort();
            foreach(int index in indices){
                navigationOccupants.TryGetValue(index,out var occupants);
                for(int team=0;team<Field.CostTeams;team++){
                    bool solid=false,wall=false,friendlyGate=false;
                    if(occupants!=null)foreach(var b in occupants){if(b.removed)continue;
                        if(b.definition.id=="gate"&&b.team==team&&b.gateTraversable)friendlyGate=true;
                        else if(b.definition.id=="wall")wall=true;else solid=true;
                    }
                    // A friendly gate reserves a portal in coarse wall cells; exact 8 m collision
                    // below still blocks the surrounding wall. Other structures never get carved.
                    bool blocked=solid||(wall&&!friendlyGate&&WallCutsCell(index,occupants));
                    for(int movement=0;movement<5;movement++){
                        int offset=(team*5+movement)*total;
                        obstacleCosts[offset+index]=blocked&&movement!=3?DeploymentMap.Blocked:Field.MovementCost(movement,Field.Map.Position(index));
                    }
                }
            }
            for(int layer=0;layer<Field.CostTeams*5;layer++)for(int k=0;k<indices.Count;){int start=indices[k],end=start;k++;while(k<indices.Count&&indices[k]==end+1)end=indices[k++];Field.Costs.SetData(obstacleCosts,layer*total+start,layer*total+start,end-start+1);}
            var fine=new List<int>(dirtyStructureCells);fine.Sort();
            foreach(int index in fine){var b=BuildingAtCell(index);uint mask=b==null?0u:b.definition.id=="gate"&&b.gateTraversable?3u^(1u<<b.team):3u;Field.StructureCellData[index]=mask;}
            for(int k=0;k<fine.Count;){int start=fine[k],end=start;k++;while(k<fine.Count&&fine[k]==end+1)end=fine[k++];Field.StructureCells.SetData(Field.StructureCellData,start,start,end-start+1);}
            // Refresh only changed rectangles. Group solver, resolution and dispatch policy are unchanged.
            float cell=Field.Size/Field.Resolution;
            for(int k=0;k<indices.Count;){int start=indices[k],end=start;k++;while(k<indices.Count&&indices[k]==end+1&&indices[k]/Field.Resolution==start/Field.Resolution)end=indices[k++];
                Vector2 a=Field.Map.Position(start),b=Field.Map.Position(end);Battle.RefreshNavigation(new Bounds(new Vector3((a.x+b.x)*.5f,0,a.y),new Vector3((end-start+1)*cell,1,cell)));}
            dirtyNavigationCells.Clear();dirtyStructureCells.Clear();
        }
        bool WallCutsCell(int index,HashSet<RtsBuilding> occupants)
        {
            int tiles=Mathf.RoundToInt(Field.Size/Field.Resolution/BuildingGrid.TileSize),n=BuildingGrid.Resolution(Field.Size);
            int x0=index%Field.Resolution*tiles,z0=index/Field.Resolution*tiles;
            var rows=new int[tiles];var columns=new int[tiles];
            foreach(var b in occupants)if(!b.removed&&b.definition.id=="wall"){
                var a=BuildingGrid.Anchor(b.definition,b.position,Field.Size);int x=WorldTopology.Cell(a.x-x0,n),z=WorldTopology.Cell(a.y-z0,n);
                if(x<tiles&&z<tiles){if(++rows[z]>=tiles||++columns[x]>=tiles)return true;}
            }return false;
        }
        public void RefreshWallConnections(Vector2 point)
        {
            var d=Catalog.Buildings["wall"];int n=BuildingGrid.Resolution(Field.Size);
            var anchor=BuildingGrid.Anchor(d,point,Field.Size);
            for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++){
                var b=BuildingAtCell(WorldTopology.Index(anchor.x+x,anchor.y+z,n));if(b==null||b.definition.id!="wall")continue;
                var a=BuildingGrid.Anchor(d,b.position,Field.Size);int mask=0;
                var directions=new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
                for(int i=0;i<4;i++){var other=BuildingAtCell(WorldTopology.Index(a.x+directions[i].x,a.y+directions[i].y,n));if(other!=null&&other.team==b.team&&(other.definition.id=="wall"||other.definition.id=="gate"))mask|=1<<i;}
                if(mask!=b.wallConnections){b.wallConnections=mask;presentation.UpgradeBuilding(b);}
            }
        }
    }
}
