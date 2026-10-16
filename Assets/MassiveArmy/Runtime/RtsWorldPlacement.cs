using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        readonly Dictionary<int,RtsBuilding> occupiedCells=new Dictionary<int,RtsBuilding>();
        public int PlacementRevision {get;private set;}
        public float NavigationCost(Vector2 point,int movement,int team=0)=>obstacleCosts[(team*5+movement)*Field.Resolution*Field.Resolution+Field.Map.Index(point)];
        public RtsBuilding BuildingAtCell(int cell)=>occupiedCells.TryGetValue(cell,out var b)&&!b.removed?b:null;
        public Vector2 SnapPlacement(BuildingDefinition d,Vector2 p,int rotation=0)=>BuildingGrid.Snap(d,p,Field.Size,rotation);
        void RegisterFootprint(RtsBuilding b,bool add)
        {
            foreach(int cell in BuildingGrid.Cells(b.definition,b.position,Field.Size,b.rotation)){
                if(add)occupiedCells[cell]=b;else if(occupiedCells.TryGetValue(cell,out var owner)&&owner==b)occupiedCells.Remove(cell);
            }
            PlacementRevision++;
        }
        bool FootprintTerrainValid(BuildingDefinition d,Vector2 p,int rotation)
        {
            float min=float.MaxValue,max=float.MinValue;
            foreach(int cell in BuildingGrid.Cells(d,p,Field.Size,rotation)){
                Vector2 centre=BuildingGrid.CellCentre(cell,Field.Size);
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){
                    Vector2 q=centre+new Vector2(x,z)*(BuildingGrid.TileSize*.499f);int i=Field.Map.Index(q);
                    float h=Field.Height(q);min=Mathf.Min(min,h);max=Mathf.Max(max,h);
                    if(Field.Map.WaterData(i).g>.8f||Field.Map.Slope[i]>.65f||Field.Map.RockObstacle[i]||Field.Deployment.Costs[2*Field.Resolution*Field.Resolution+i]>=DeploymentMap.Blocked*.5f)return false;
                }
            }
            var size=BuildingGrid.Dimensions(d,rotation);
            return max-min<=Mathf.Min(size.x,size.y)*BuildingGrid.TileSize*.45f&&(d.terrain!=TerrainRule.Coast||WaterExit(p,Mathf.Max(size.x,size.y)*4,out _));
        }
        bool NodeIntersects(BuildingDefinition d,Vector2 p,int rotation,ResourceNode node)
        {
            var size=BuildingGrid.Dimensions(d,rotation);var delta=Field.Delta(p,node.position);
            float x=Mathf.Max(0,Mathf.Abs(delta.x)-size.x*4),z=Mathf.Max(0,Mathf.Abs(delta.y)-size.y*4);
            return x*x+z*z<node.radius*node.radius;
        }
        public bool GateReplacement(Vector2 point,int team,int rotation,List<RtsBuilding> walls,out string reason)
        {
            walls.Clear();reason="";var gate=Catalog.Buildings["gate"];int occupied=0,n=BuildingGrid.Resolution(Field.Size);
            foreach(int cell in BuildingGrid.Cells(gate,point,Field.Size,rotation)){
                var b=BuildingAtCell(cell);if(b==null)continue;occupied++;
                if(b.definition.id!="wall"||b.team!=team||!b.Complete){reason="Gate requires three completed walls owned by your team";return false;}walls.Add(b);
                int x=cell%n,z=cell/n;
                foreach(var step in (rotation&1)==0?new[]{Vector2Int.up,Vector2Int.down}:new[]{Vector2Int.left,Vector2Int.right}){
                    var side=BuildingAtCell(WorldTopology.Index(x+step.x,z+step.y,n));
                    if(side!=null&&(side.definition.id=="wall"||side.definition.id=="gate")){reason="Gate cannot replace a wall corner or junction";return false;}
                }
            }
            if(occupied!=0&&occupied!=3){reason="Gate must replace three consecutive walls or occupy empty cells";return false;}
            return true;
        }
        public int GateRotation(Vector2 point,int team,int preferred)
        {
            var candidates=new List<RtsBuilding>(3);
            for(int i=0;i<2;i++){int rotation=i==0?preferred:(preferred+1)&3;Vector2 p=SnapPlacement(Catalog.Buildings["gate"],point,rotation);
                if(GateReplacement(p,team,rotation,candidates,out _)&&candidates.Count==3)return rotation;}
            return preferred;
        }
        public ResourceCost PlacementCost(BuildingDefinition d,Vector2 p,int team,int rotation=0)
        {
            var cost=d.cost;if(d.id!="gate")return cost;var walls=new List<RtsBuilding>(3);
            if(GateReplacement(p,team,rotation,walls,out _))foreach(var wall in walls)for(int i=0;i<5;i++)cost[i]-=wall.definition.cost[i]*.45f;
            for(int i=0;i<5;i++)cost[i]=Mathf.Max(0,cost[i]);return cost;
        }
        PlacementResult ValidateGridPlacement(BuildingDefinition d,int team,Vector2 point,bool ignoreKnowledge,int rotation,bool checkWallet=true)
        {
            Vector2 p=SnapPlacement(d,point,rotation);var f=Factions[team];var replaced=new List<RtsBuilding>(3);
            if(d.id=="gate"&&!GateReplacement(p,team,rotation,replaced,out string gateReason))return new PlacementResult(false,gateReason);
            if(replaced.Count==0&&Buildings.Count>=GpuBattle.MaxBuildings&&Buildings.All(b=>!b.removed))return new PlacementResult(false,"Structure capacity reached");
            if(!f.HasTech(d.requiredTech)||f.tier<d.tier)return new PlacementResult(false,"Requires "+(!string.IsNullOrEmpty(d.requiredTech)?d.requiredTech:"Command tier "+d.tier));
            if(checkWallet&&!f.wallet.CanAfford(PlacementCost(d,p,team,rotation)))return new PlacementResult(false,"Insufficient resources");
            foreach(int cell in BuildingGrid.Cells(d,p,Field.Size,rotation)){
                Vector2 q=BuildingGrid.CellCentre(cell,Field.Size);
                if(!ignoreKnowledge&&!Battle.IsVisible(q,team,true))return new PlacementResult(false,"Scout the whole foundation first");
                bool controlled=SectorOwner[Sector(q)]==team||Buildings.Any(b=>!b.removed&&b.team==team&&b.Complete&&b.powered&&b.definition.buildRadius>0&&Field.Distance(q,b.position)<=b.definition.buildRadius*(1+(b.level-1)*.2f));
                if(!controlled)return new PlacementResult(false,"Outside controlled territory / build radius");
                var occupant=BuildingAtCell(cell);if(occupant!=null&&!replaced.Contains(occupant))return new PlacementResult(false,"Building footprint is occupied");
            }
            if(!FootprintTerrainValid(d,p,rotation))return new PlacementResult(false,d.terrain==TerrainRule.Coast?"Requires a stable coastline":"Water, rocks or steep foundation");
            int node=-1;
            if(d.terrain==TerrainRule.Node){
                var source=Nodes.FirstOrDefault(n=>n.claimedBy<0&&n.remaining>0&&d.nodes.Contains(n.kind)&&BuildingGrid.Contains(d,p,n.position,Field.Size,rotation));
                if(source==null)return new PlacementResult(false,"Foundation must cover an unclaimed "+string.Join(" / ",d.nodes)+" node");node=source.id;
            }else if(Nodes.Any(n=>NodeIntersects(d,p,rotation,n)))return new PlacementResult(false,"Keep resource deposit accessible");
            bool low=f.demand+d.consumption>f.generation+d.generation;
            string message=replaced.Count>0?"Replace 3 walls · net cost "+PlacementCost(d,p,team,rotation):low?"Valid · power deficit":"Valid placement";
            return new PlacementResult(true,message,node,low);
        }
        public bool WallCellBuildable(int cell,int team,bool ignoreKnowledge=false)
        {
            var b=BuildingAtCell(cell);if(b!=null)return b.team==team&&b.definition.id=="wall";
            return ValidateGridPlacement(Catalog.Buildings["wall"],team,BuildingGrid.CellCentre(cell,Field.Size),ignoreKnowledge,0,false).valid;
        }
        public PlacementResult ValidateWallChain(IReadOnlyList<int> cells,int team,bool ignoreKnowledge=false)
        {
            if(cells.Count==0||cells.Count>WallPreviewRouter.MaxSegments)return new PlacementResult(false,"No valid wall route");
            var unique=new HashSet<int>();int count=0,n=BuildingGrid.Resolution(Field.Size);
            for(int i=0;i<cells.Count;i++){
                int cell=cells[i];if(cell<0||cell>=n*n||!unique.Add(cell))return new PlacementResult(false,"Invalid wall route");
                if(i>0){Vector2 delta=Field.Delta(BuildingGrid.CellCentre(cells[i-1],Field.Size),BuildingGrid.CellCentre(cell,Field.Size));if(Mathf.Abs(delta.x)+Mathf.Abs(delta.y)>8.01f)return new PlacementResult(false,"Wall route must be connected");}
                if(!WallCellBuildable(cell,team,ignoreKnowledge))return new PlacementResult(false,"Wall route is blocked");if(BuildingAtCell(cell)==null)count++;
            }
            if(Buildings.Count(b=>!b.removed)+count>GpuBattle.MaxBuildings)return new PlacementResult(false,"Structure capacity reached");
            var cost=Catalog.Buildings["wall"].cost*count;
            return Factions[team].wallet.CanAfford(cost)?new PlacementResult(true,count+" new segments · "+cost):new PlacementResult(false,"Insufficient resources · "+count+" segments · "+cost);
        }
        public bool ConstructWallChain(IReadOnlyList<int> cells,int team,bool ignoreKnowledge=false)
        {
            var valid=ValidateWallChain(cells,team,ignoreKnowledge);Notice=valid.reason;if(!valid.valid)return false;
            int count=cells.Count(cell=>BuildingAtCell(cell)==null);if(!Factions[team].wallet.Spend(Catalog.Buildings["wall"].cost*count))return false;
            BeginObstacleBatch();try{foreach(int cell in cells)if(BuildingAtCell(cell)==null)Create(Catalog.Buildings["wall"],team,BuildingGrid.CellCentre(cell,Field.Size),-1);}finally{EndObstacleBatch();}
            return true;
        }
        Vector2 FindSeedFoundation(BuildingDefinition d,Vector2 desired)
        {
            for(int ring=0;ring<100;ring++)for(int k=0;k<Mathf.Max(1,ring*8);k++){
                float angle=k*Mathf.PI*2/Mathf.Max(1,ring*8);var p=SnapPlacement(d,desired+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*ring*8);
                if(FootprintTerrainValid(d,p,0)&&BuildingGrid.Cells(d,p,Field.Size).All(c=>BuildingAtCell(c)==null))return p;
            }
            throw new InvalidOperationException("No grid foundation near starting base");
        }
    }
}
