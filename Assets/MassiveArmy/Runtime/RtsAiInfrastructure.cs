using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        sealed class AiCompound
        {
            public int anchor;public Vector2 centre;public int x,z;
            public readonly List<(string id,Vector2 position,int rotation)> perimeter=new List<(string,Vector2,int)>();
        }
        public Vector2 AiObjective(int team)=>aiPlans[team].objective;
        public readonly string[] AiFortificationStatus={"Unplanned","Unplanned"};
        void UpdateAiObjective(int team,RtsBuilding command,AiPlan plan)
        {
            if(plan.clearance!=null){if(!plan.hasObjective){plan.objectiveAnchor=plan.clearance.position;plan.objective=StructureSafeDestination(plan.clearance.position,2,team);plan.objectiveBuilding=-1;plan.objectiveSince=matchTime;plan.hasObjective=true;}return;}
            bool destroyed=plan.objectiveBuilding>=0&&(plan.objectiveBuilding>=Buildings.Count||Buildings[plan.objectiveBuilding].removed||Buildings[plan.objectiveBuilding].team==team||Field.Distance(Buildings[plan.objectiveBuilding].position,plan.objectiveAnchor)>8);
            var stats=Battle.Statistics;bool arrived=(Battle.Settings.spectatorMode||team==1&&Battle.Settings.enemyAI)&&stats!=null&&stats[116+team]>=4&&stats[114+team]>=stats[116+team]*.6f;
            // A discovered target is pursued until destroyed. Exploration only advances
            // after a majority of the ground army actually reaches its objective.
            if(plan.hasObjective&&!destroyed&&!(plan.objectiveBuilding<0&&arrived&&matchTime-plan.objectiveSince>=45))return;
            var enemy=Buildings.Where(b=>!b.removed&&b.team!=team&&Battle.IsKnown(b.position,team)).OrderBy(b=>b.definition.id=="command"?0:1).ThenBy(b=>Field.Distance(command.position,b.position)).FirstOrDefault();
            Vector2 next=enemy!=null?enemy.position:SectorPosition((Sector(command.position)+8+plan.cycle/4)%16);
            if(plan.hasObjective&&Field.Distance(next,plan.objective)<Field.Size/Field.Resolution)next=SectorPosition((Sector(next)+1)%16);
            plan.objectiveAnchor=next;plan.objective=StructureSafeDestination(next,2,team);plan.objectiveBuilding=enemy?.index??-1;plan.objectiveSince=matchTime;plan.hasObjective=true;
        }
        public Vector2 StructureSafeDestination(Vector2 desired,int movement,int team)
        {
            bool Clear(Vector2 p){float hull=movement==0?.4f:2.2f;for(int i=0;i<5;i++){var q=p+(i==0?Vector2.zero:new Vector2((i&1)==0?hull:-hull,(i&2)==0?hull:-hull));
                if(NavigationCost(q,movement,team)>=DeploymentMap.Blocked*.5f)return false;
                q=Field.Canonical(q);var b=BuildingAtCell(BuildingGrid.CellIndex(Mathf.FloorToInt((q.x+Field.Size*.5f)/8),Mathf.FloorToInt((q.y+Field.Size*.5f)/8),Field.Size));
                if(b!=null&&(b.definition.id!="gate"||b.team!=team||!b.gateTraversable))return false;
            }return true;}
            desired=Field.Canonical(desired);if(movement==3||Clear(desired))return desired;
            int n=Field.Resolution,index=Field.Map.Index(desired),x=index%n,z=index/n;
            for(int ring=0;ring<=12;ring++){float best=float.MaxValue;Vector2 found=desired;
                for(int dz=-ring;dz<=ring;dz++)for(int dx=-ring;dx<=ring;dx++){if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=ring)continue;var p=Field.Map.Position(WorldTopology.Index(x+dx,z+dz,n));if(!Clear(p))continue;float distance=Field.Distance(p,desired);if(distance<best){best=distance;found=p;}}
                if(best<float.MaxValue)return found;
            }return desired;
        }
        float AiClearance=>Mathf.Max(24,Field.Size/Field.Resolution*2+8);
        bool RectOverlap(Vector2 a,Vector2 halfA,Vector2 b,Vector2 halfB,float gap=0)
        {var delta=Field.Delta(a,b);return Mathf.Abs(delta.x)<halfA.x+halfB.x+gap-.01f&&Mathf.Abs(delta.y)<halfA.y+halfB.y+gap-.01f;}
        static Vector2 Half(BuildingDefinition d,int rotation){var size=BuildingGrid.Dimensions(d,rotation);return new Vector2(size.x,size.y)*4;}
        bool AiExitRect(BuildingDefinition d,Vector2 p,int rotation,out Vector2 centre,out Vector2 half)
        {
            centre=p;half=Vector2.zero;
            var ground=d.production.Where(id=>Catalog.Units.TryGetValue(id,out var u)&&u.movement<MovementClass.Air).Select(id=>Catalog.Units[id]).ToArray();if(ground.Length==0)return false;
            float front=d.footprintDepth*4,depth=ground.Max(u=>u.radius)+12+Field.Size/Field.Resolution*1.5f;
            centre=Field.Canonical(p+BuildingGrid.Forward(rotation)*(front+depth*.5f));
            // Reserve all five possible production bays, rather than just the current bay.
            half=(rotation&1)==0?new Vector2(32,depth*.5f):new Vector2(depth*.5f,32);return true;
        }
        bool AiPlacementClear(BuildingDefinition d,int team,Vector2 p,int rotation)
        {
            Vector2 half=Half(d,rotation);bool fence=d.id=="wall"||d.id=="gate";
            foreach(var b in Buildings){if(b.removed)continue;bool otherFence=b.definition.id=="wall"||b.definition.id=="gate";
                if(!(fence&&otherFence)&&RectOverlap(p,half,b.position,Half(b.definition,b.rotation),AiClearance))return false;
                if(AiExitRect(b.definition,b.position,b.rotation,out var exit,out var exitHalf)&&RectOverlap(p,half,exit,exitHalf,8))return false;
                if(AiExitRect(d,p,rotation,out exit,out exitHalf)&&RectOverlap(b.position,Half(b.definition,b.rotation),exit,exitHalf,8))return false;
            }
            foreach(var compound in aiPlans[team].compounds){
                if(fence&&compound.perimeter.Any(i=>i.id==d.id&&Field.Distance(i.position,p)<.1f))continue;
                foreach(var item in compound.perimeter){
                    bool same=fence&&item.id==d.id&&Field.Distance(p,item.position)<.1f;
                    if(!same&&!(fence&&item.id=="wall")&&RectOverlap(p,half,item.position,Half(Catalog.Buildings[item.id],item.rotation),AiClearance))return false;
                    if(!fence&&item.id=="wall"&&RectOverlap(p,half,item.position,Vector2.one*4,AiClearance))return false;
                    if(item.id=="gate"){
                        Vector2 laneHalf=(item.rotation&1)==0?new Vector2(AiClearance, AiClearance*2):new Vector2(AiClearance*2,AiClearance);
                        if(!same&&RectOverlap(p,half,item.position,laneHalf))return false;
                    }
                }
            }
            return true;
        }
        bool PlanAiCompound(int team,RtsBuilding anchor,RtsBuilding[] owned,AiPlan plan)
        {
            Vector2 centre=SnapPlacement(Catalog.Buildings["wall"],anchor.position);float margin=AiClearance+4;
            Vector2 needed=Half(anchor.definition,anchor.rotation)+Vector2.one*margin;
            // Include the local cluster, production exits and a walking aisle. Expand
            // past nearby buildings instead of accepting holes in an arbitrary ring.
            var cluster=owned.Where(b=>b.definition.id!="wall"&&b.definition.id!="gate"&&Field.Distance(b.position,anchor.position)<Mathf.Max(80,anchor.definition.buildRadius*.55f)).ToArray();
            foreach(var b in cluster){var delta=Field.Delta(centre,b.position);needed=Vector2.Max(needed,new Vector2(Mathf.Abs(delta.x),Mathf.Abs(delta.y))+Half(b.definition,b.rotation)+Vector2.one*margin);
                if(AiExitRect(b.definition,b.position,b.rotation,out var exit,out var half)){delta=Field.Delta(centre,exit);needed=Vector2.Max(needed,new Vector2(Mathf.Abs(delta.x),Mathf.Abs(delta.y))+half+Vector2.one*margin);}}
            int x=Mathf.Max(8,Mathf.CeilToInt(needed.x/8)),z=Mathf.Max(8,Mathf.CeilToInt(needed.y/8));
            int limit=Mathf.Min(28,Mathf.FloorToInt(anchor.definition.buildRadius*(1+(anchor.level-1)*.2f)/8)-1);
            // One bounded candidate per AI cycle. Alternate expansion axis on failure.
            x+=plan.cycle%3;z+=(plan.cycle/3)%3;if(x>limit||z>limit){AiFortificationStatus[team]="Cluster exceeds build radius: "+x+" / "+z+" limit "+limit;return false;}
            var c=new AiCompound{anchor=anchor.index,centre=centre,x=x,z=z};
            for(int side=0;side<4;side++){var normal=BuildingGrid.Forward(side);c.perimeter.Add(("gate",centre+normal*(side%2==0?z:x)*8,side%2));}
            // Deduplicate corner cells; each side deliberately omits its three gate cells.
            var cells=new HashSet<int>();for(int side=0;side<4;side++)for(int i=-(side%2==0?x:z);i<=(side%2==0?x:z);i++){
                if(Math.Abs(i)<=1)continue;Vector2 p=side%2==0?centre+new Vector2(i*8,(side==0?1:-1)*z*8):centre+new Vector2((side==1?1:-1)*x*8,i*8);
                int cell=BuildingGrid.Cells(Catalog.Buildings["wall"],p,Field.Size).First();if(cells.Add(cell))c.perimeter.Add(("wall",Field.Canonical(p),0));
            }
            if(owned.Length+c.perimeter.Count>230||Buildings.Count(b=>!b.removed)+c.perimeter.Count>GpuBattle.MaxBuildings-48){AiFortificationStatus[team]="Structure budget";return false;}
            foreach(var item in c.perimeter){var d=Catalog.Buildings[item.id];var p=SnapPlacement(d,item.position,item.rotation);
                if(AiRiskBlocked(team,p)){AiFortificationStatus[team]="Clear hostile outpost site";return false;}
                if(!AiPlacementClear(d,team,p,item.rotation)){AiFortificationStatus[team]="Clearance: "+item.id+" "+p;return false;}
                var result=ValidateGridPlacement(d,team,p,true,item.rotation,false);if(!result.valid){AiFortificationStatus[team]=result.reason+": "+item.id+" "+p;return false;}
            }
            if(!AiCompoundConnected(c,team,cluster)){AiFortificationStatus[team]="Disconnected exits or gates";return false;}
            AiFortificationStatus[team]="Planned "+c.perimeter.Count+" segments";
            plan.compounds.Add(c);plan.defenceAnchor=anchor.index;foreach(var item in c.perimeter)plan.defence.Enqueue(item);
            return true;
        }
        bool AiCompoundGatesReady(AiCompound c,int team)=>c.perimeter.Where(i=>i.id=="gate").All(i=>Buildings.Any(b=>!b.removed&&b.team==team&&b.definition.id=="gate"&&Field.Distance(b.position,i.position)<.1f&&b.Complete&&b.gateTraversable));
        void RepairAiCompoundGates(int team,AiPlan plan){var c=plan.compounds.FirstOrDefault(p=>p.anchor==plan.defenceAnchor);if(c==null)return;
            foreach(var item in c.perimeter.Where(i=>i.id=="gate")){if(Buildings.Any(b=>!b.removed&&b.team==team&&b.definition.id=="gate"&&Field.Distance(b.position,item.position)<.1f))continue;
                if(AIPlacementAllowed(Catalog.Buildings["gate"],team,item.position,item.rotation).valid)Construct("gate",team,item.position,true,item.rotation);return;}}
        bool AiPlacementPreservesAccess(BuildingDefinition d,int team,Vector2 p,int rotation)
        {
            if(d.id=="wall"||d.id=="gate")return true;
            foreach(var c in aiPlans[team].compounds){if(!RectOverlap(p,Half(d,rotation),c.centre,new Vector2(c.x,c.z)*8))continue;
                var cluster=Buildings.Where(b=>!b.removed&&b.team==team&&RectOverlap(b.position,Half(b.definition,b.rotation),c.centre,new Vector2(c.x-2,c.z-2)*8)).Append(new RtsBuilding{definition=d,position=p,rotation=rotation,team=team}).ToArray();
                if(!AiCompoundConnected(c,team,cluster,d,p,rotation))return false;
            }return true;
        }
        bool AiCompoundConnected(AiCompound c,int team,RtsBuilding[] cluster,BuildingDefinition addition=null,Vector2 point=default,int rotation=0)
        {
            // Construction-only flood fill over immutable terrain and exact foundation
            // occupancy. Soldiers continue using the existing GPU flow fields.
            int n=BuildingGrid.Resolution(Field.Size),border=Mathf.CeilToInt(AiClearance/8)+2;
            var wall=Catalog.Buildings["wall"];int origin=BuildingGrid.Cells(wall,c.centre,Field.Size).First(),ox=origin%n,oz=origin/n;
            var blocked=new HashSet<int>();foreach(var item in c.perimeter.Where(i=>i.id=="wall"))foreach(int cell in BuildingGrid.Cells(wall,item.position,Field.Size))blocked.Add(cell);
            if(addition!=null)foreach(int cell in BuildingGrid.Cells(addition,point,Field.Size,rotation))blocked.Add(cell);
            int tiles=Mathf.RoundToInt(Field.Size/Field.Resolution/8);var coarseSolid=new HashSet<int>();var gateCells=new HashSet<int>();var rows=new Dictionary<int,int[]>();
            foreach(var item in c.perimeter.Where(i=>i.id=="gate"))foreach(int cell in BuildingGrid.Cells(Catalog.Buildings["gate"],item.position,Field.Size,item.rotation))gateCells.Add(Field.Map.Index(BuildingGrid.CellCentre(cell,Field.Size)));
            foreach(var item in c.perimeter.Where(i=>i.id=="wall"))foreach(int cell in BuildingGrid.Cells(wall,item.position,Field.Size)){
                int coarse=Field.Map.Index(BuildingGrid.CellCentre(cell,Field.Size));if(!rows.TryGetValue(coarse,out var counts))rows[coarse]=counts=new int[tiles*2];counts[(cell%n)%tiles]++;counts[tiles+(cell/n)%tiles]++;
            }
            foreach(var pair in rows)if(!gateCells.Contains(pair.Key)&&pair.Value.Any(count=>count>=tiles))coarseSolid.Add(pair.Key);
            bool Clear(int dx,int dz){if(Math.Abs(dx)>c.x+border||Math.Abs(dz)>c.z+border)return false;int cell=WorldTopology.Index(ox+dx,oz+dz,n);if(blocked.Contains(cell))return false;
                var p=BuildingGrid.CellCentre(cell,Field.Size);int coarse=Field.Map.Index(p);if(coarseSolid.Contains(coarse)||Field.Deployment.Costs[2*Field.Resolution*Field.Resolution+coarse]>=DeploymentMap.Blocked*.5f)return false;
                if(navigationOccupants.TryGetValue(coarse,out var occupants)&&occupants.Any(b=>!b.removed&&b.definition.id!="wall"&&b.definition.id!="gate"))return false;
                if(addition!=null&&RectOverlap(Field.Map.Position(coarse),Vector2.one*(Field.Size/Field.Resolution*.5f),point,Half(addition,rotation)))return false;
                var b=BuildingAtCell(cell);return b==null||b.definition.id=="gate"&&b.team==team&&b.powered;
            }
            var visited=new HashSet<int>();var queue=new Queue<Vector2Int>();
            // Seed every outer-edge cell, so terrain outside the enclosure is checked too.
            for(int dz=-c.z-border;dz<=c.z+border;dz++)for(int dx=-c.x-border;dx<=c.x+border;dx++)if((Math.Abs(dx)==c.x+border||Math.Abs(dz)==c.z+border)&&Clear(dx,dz)){int cell=WorldTopology.Index(ox+dx,oz+dz,n);if(visited.Add(cell))queue.Enqueue(new Vector2Int(dx,dz));}
            var steps=new[]{Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};while(queue.Count>0){var p=queue.Dequeue();foreach(var step in steps){var q=p+step;if(!Clear(q.x,q.y))continue;int cell=WorldTopology.Index(ox+q.x,oz+q.y,n);if(visited.Add(cell))queue.Enqueue(q);}}
            foreach(var item in c.perimeter.Where(i=>i.id=="gate"))foreach(int cell in BuildingGrid.Cells(Catalog.Buildings["gate"],item.position,Field.Size,item.rotation))if(!visited.Contains(cell))return false;
            foreach(var b in cluster){if(AiExitRect(b.definition,b.position,b.rotation,out _,out _)){
                foreach(var id in b.definition.production){var unit=Catalog.Units[id];if(unit.movement>=MovementClass.Air)continue;
                    for(int bay=0;bay<5;bay++){var forward=BuildingGrid.Forward(b.rotation);var p=b.position+forward*(b.definition.footprintDepth*4+unit.radius+12)+new Vector2(forward.y,-forward.x)*bay*7;
                        int cell=BuildingGrid.Cells(wall,p,Field.Size).First();if(!visited.Contains(cell))return false;
                    }
                }
            }}return true;
        }
    }
}
