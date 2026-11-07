using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // Building-production metadata only. No army records or poses are mirrored.
    internal sealed class ProductionExitState {
        internal const int CandidateCount=96;
        internal QueueOrder order;internal string type;internal int topology=-1,terrain=-1,search,probe,tested,token;
        internal readonly List<Vector3> sites=new List<Vector3>();
        internal Vector3 requested;internal bool queued,ready,clear,sleeping;internal double wake;
        internal uint movement;
        internal void ResetGeometry(int layout,int edits,string unit){topology=layout;terrain=edits;type=unit;search=probe=tested=0;sites.Clear();Invalidate();}
        internal void Invalidate(){token++;queued=ready=sleeping=false;wake=0;}
        internal bool CanWake(double now,uint movementVersion)=>!sleeping||now>=wake||movementVersion!=movement;
        internal void Sleep(double now,uint movementVersion){sleeping=true;wake=now+2;movement=movementVersion;tested=0;}
    }
    public sealed partial class PlanetRtsBase {
        readonly Dictionary<RtsBuilding,ProductionExitState> productionExits=new Dictionary<RtsBuilding,ProductionExitState>();
        readonly Queue<(RtsBuilding building,ProductionExitState state,int token)> productionProbes=new Queue<(RtsBuilding,ProductionExitState,int)>();
        int productionGeometryFrame=-1,productionGeometryBudget;
        internal static bool ExitAwaitingRelease(RtsBuilding b)=>b.queue.Count>0&&!b.queue[0].research&&b.queue[0].progress>=b.queue[0].duration;
        bool SpawnNear(RtsBuilding b,UnitDefinition d){using var timing=SettlementPerformance.Sample("ProductionExit");
            if(!productionExits.TryGetValue(b,out var s)){s=new ProductionExitState();productionExits.Add(b,s);}
            int topology=unchecked(Buildings.Count*397+placementEpoch),edits=fixture.definition.Edits.Revision;
            if(s.topology!=topology||s.terrain!=edits||s.type!=d.id)s.ResetGeometry(topology,edits,d.id);
            var order=b.queue[0];if(s.order!=order){s.order=order;s.Invalidate();s.tested=0;Revision++;}
            double now=Time.realtimeSinceStartupAsDouble;uint movement=fixture.ProductionMovementVersion;
            if(!s.CanWake(now,movement)||s.queued)return false;
            if(s.sleeping){s.sleeping=false;s.tested=0;}
            if(s.ready){s.ready=false;s.tested++;
                // Recheck immediately before spawning: the asynchronous result can
                // be stale if an army unit moved into the exit in the meantime.
                if(s.clear&&fixture.TrySpawnUnit(Catalog.TypeIndex(d.id),s.requested,b.team)){s.probe=Math.Max(0,s.probe-1);s.order=null;s.Invalidate();return true;}
            }
            if(!fixture.ProductionCapacityAvailable){s.Sleep(now,movement);return false;}
            if(s.search>=ProductionExitState.CandidateCount&&s.sites.Count==0){s.Sleep(now,movement);return false;}
            // A failed complete sweep sleeps. Subsequent wakeups sample a few cached
            // exits, rather than rebuilding or exhaustively searching the same ring.
            int sweep=s.search<ProductionExitState.CandidateCount?ProductionExitState.CandidateCount:Math.Min(s.sites.Count,4);
            if(s.tested>=sweep&&s.search>=ProductionExitState.CandidateCount){s.Sleep(now,movement);return false;}
            if(productionGeometryFrame!=Time.frameCount){productionGeometryFrame=Time.frameCount;productionGeometryBudget=4;}
            while(s.probe>=s.sites.Count&&s.search<ProductionExitState.CandidateCount&&productionGeometryBudget>0){
                productionGeometryBudget--;int candidate=s.search++;var direction=CubeSphere.Decode(b.position);var east=Vector3.Cross(Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up,direction).normalized;var north=Vector3.Cross(direction,east);float angle=(candidate%16)*Mathf.PI/8;
                var point=(direction+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*(FootprintRadius(b.definition)+12+(candidate/16)*12)/fixture.definition.radius).normalized;
                bool clear=true;foreach(var other in Buildings)if(!other.removed&&Distance(point,CubeSphere.Decode(other.position))<FootprintRadius(other.definition)+d.radius+4){clear=false;break;}
                if(clear&&fixture.UnitSpawnTerrainClear(d,point))s.sites.Add(point);
            }
            if(s.sites.Count==0)return false;
            if(s.probe>=s.sites.Count){if(s.search<ProductionExitState.CandidateCount)return false;s.probe=0;}
            s.requested=s.sites[s.probe++];s.queued=true;productionProbes.Enqueue((b,s,s.token));return false;
        }
        void PumpProductionProbes(){
            if(fixture.ProductionProbeBusy)return;
            // FIFO gives crowded factories equal opportunities; one outstanding
            // request per producer prevents a retry queue from growing each tick.
            for(int skipped=0;skipped<8&&productionProbes.Count>0;skipped++){
                var job=productionProbes.Peek();var b=job.building;var s=job.state;
                if(b.removed||s.token!=job.token||b.queue.Count==0||b.queue[0]!=s.order){productionProbes.Dequeue();if(s.token==job.token)s.Invalidate();continue;}
                if(!fixture.TryProductionProbe(s.requested,Catalog.Units[s.type],clear=>{if(b.removed||s.token!=job.token)return;s.queued=false;s.ready=true;s.clear=clear;}))return;
                productionProbes.Dequeue();return;
            }
        }
        void ForgetProductionExit(RtsBuilding b){if(productionExits.TryGetValue(b,out var state)){state.Invalidate();productionExits.Remove(b);}}
        void DisposeProductionExits(){foreach(var state in productionExits.Values)state.Invalidate();productionExits.Clear();productionProbes.Clear();}
    }
    public sealed partial class PlanetDevelopment {
        GraphicsBuffer productionClearance;bool productionProbePending,productionProbeDisposed;readonly uint[] productionClearanceReset={0};
        internal bool ProductionCapacityAvailable=>unitCount<capacity;
        internal bool ProductionProbeBusy=>productionProbePending||productionProbeDisposed;
        internal uint ProductionMovementVersion=>commands?.MovementRevision??0;
        internal bool TryProductionProbe(Vector3 direction,UnitDefinition unit,Action<bool> receive){
            if(productionProbePending||productionProbeDisposed)return false;
            if(unitCount==0){receive(true);return true;}
            productionClearance??=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);productionClearance.SetData(productionClearanceReset);
            int kernel=movement.FindKernel("CheckClearance");movement.SetBuffer(kernel,"_PlacementOccupied",productionClearance);movement.SetVector("_PlacementDirection",direction);movement.SetFloat("_PlacementRadius",unit.radius);movement.SetInt("_PlacementAir",unit.movement==MovementClass.Air?1:0);
            movement.Dispatch(kernel,(unitCount+127)/128,1,1);movement.SetBuffer(kernel,"_PlacementOccupied",placementProbe);productionProbePending=true;
            AsyncGPUReadback.Request(productionClearance,request=>{productionProbePending=false;if(productionProbeDisposed){productionClearance?.Dispose();productionClearance=null;return;}receive(!request.hasError&&request.GetData<uint>()[0]==0);});return true;
        }
        void DisposeProductionProbe(){productionProbeDisposed=true;if(!productionProbePending){productionClearance?.Dispose();productionClearance=null;}}
    }
}
