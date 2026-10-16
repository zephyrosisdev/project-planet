using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed partial class GpuBattle
    {
        ComputeShader swarmNavigation;
        GraphicsBuffer swarmA,swarmB,swarmPublished,swarmSeeds,swarmChanged,swarmCounts,swarmScratch,swarmTypes,swarmSites;
        bool swarmDirty,swarmRouting,swarmRoutePending,swarmPending,swarmHasRoute;
        uint swarmRevision,swarmTargetHash,swarmRequest,swarmSeed,swarmSerial,swarmFrontPopulation=1000;
        int swarmTypeCount,swarmSiteCount;
        double swarmNextStatus;
        Action<uint> swarmCallback;
        public uint SwarmAlive {get;private set;}
        public string SwarmError {get;private set;}="";
        public bool SwarmPending=>swarmPending||swarmRequest>0;
        public int SwarmEntryCount=>swarmSiteCount;
        public void ConfigureSwarm(uint[] roster,Vector2[] sites)
        {
            if(!Settings.baseBuilding)throw new InvalidOperationException("Invalid swarm configuration.");
            if(swarmA!=null){swarmTypes.Dispose();swarmSites.Dispose();swarmTypes=Buffer(roster.Length,4,"Zombie wave roster");swarmTypes.SetData(roster);swarmTypeCount=roster.Length;swarmSites=Buffer(sites.Length,8,"Zombie wave fronts");swarmSites.SetData(sites);swarmSiteCount=sites.Length;swarmDirty=true;return;}
            swarmNavigation=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SwarmNavigation"));
            int cells=Field.Resolution*Field.Resolution;
            swarmA=Buffer(cells,8,"Swarm reachable building integration A");swarmB=Buffer(cells,8,"Swarm reachable building integration B");
            swarmPublished=Buffer(cells,8,"Published zombie building routes");var empty=new Vector2[cells];for(int i=0;i<cells;i++)empty[i]=new Vector2(1e20f,0);swarmPublished.SetData(empty);
            swarmSeeds=Buffer(cells,4,"Swarm deterministic building route seeds");
            swarmChanged=Buffer(1,4,"Swarm route convergence");swarmChanged.SetData(new uint[1]);
            swarmCounts=Buffer(4,4,"Swarm spawn and live reductions");swarmCounts.SetData(new uint[4]);
            swarmScratch=Buffer(16384,4,"Swarm spawn reclaim scratch");
            swarmTypes=Buffer(roster.Length,4,"Swarm combat roster");swarmTypes.SetData(roster);swarmTypeCount=roster.Length;
            swarmSites=Buffer(sites.Length,8,"Swarm perimeter entry sites");swarmSites.SetData(sites);swarmSiteCount=sites.Length;
            swarmDirty=true;
        }
        public bool RequestSwarm(uint count,uint seed,Action<uint> acknowledged,ulong population=0)
        {
            if(swarmA==null||SwarmError.Length>0||SwarmPending||count==0)return false;
            swarmRequest=Math.Min(16384u,count);swarmSeed=seed;swarmCallback=acknowledged;swarmFrontPopulation=(uint)Math.Min((ulong)Settings.capacity,population>0?population:count);return true;
        }
        void SwarmTargetsChanged(GpuStructure[] data,int count)
        {
            if(!Settings.baseBuilding)return;
            uint hash=2166136261;
            for(int i=0;i<count;i++)if(data[i].health>0&&(!Settings.swarmMode||data[i].team==0))
                unchecked{hash=(hash^(uint)i)*16777619;hash=(hash^(uint)data[i].position.GetHashCode())*16777619;hash=(hash^(uint)data[i].radius.GetHashCode())*16777619;}
            if(hash!=swarmTargetHash){swarmTargetHash=hash;swarmDirty=true;}
        }
        void SwarmBind(int kernel)
        {
            RtsBind(kernel);
            foreach(var pair in new[]{("_SwarmField",swarmPublished),("_SwarmCounts",swarmCounts),("_SwarmScratch",swarmScratch),("_SwarmTypes",swarmTypes),("_SwarmSites",swarmSites),("_SwarmSlots",freeSlots)})cmd.SetComputeBufferParam(rtsShader,kernel,pair.Item1,pair.Item2);
            cmd.SetComputeIntParam(rtsShader,"_SwarmTypeCount",swarmTypeCount);cmd.SetComputeIntParam(rtsShader,"_SwarmSiteCount",swarmSiteCount);
            cmd.SetComputeIntParam(rtsShader,"_SwarmRequest",(int)swarmRequest);cmd.SetComputeIntParam(rtsShader,"_SwarmSeed",unchecked((int)swarmSeed));cmd.SetComputeIntParam(rtsShader,"_SwarmSerial",unchecked((int)swarmSerial));
            cmd.SetComputeIntParam(rtsShader,"_ZombieType",ZombieType);cmd.SetComputeIntParam(rtsShader,"_SwarmAmbient",spawningAmbient?1:0);
            cmd.SetComputeFloatParam(rtsShader,"_SwarmFrontRadius",Mathf.Min(Field.Size*.12f,Mathf.Max(Field.Size*.018f,Mathf.Sqrt(swarmFrontPopulation)*.8f)));
            if(spawningAmbient){cmd.SetComputeBufferParam(rtsShader,kernel,"_SwarmSites",ambientSites);cmd.SetComputeIntParam(rtsShader,"_SwarmSiteCount",ambientSiteCount);}
        }
        void SwarmDispatch(string name,int count)
        {
            int k=rtsShader.FindKernel(name);SwarmBind(k);cmd.DispatchCompute(rtsShader,k,(Math.Max(1,count)+127)/128,1,1);
        }
        bool spawningAmbient;
        void RecordSwarm(bool paused)
        {
            if(swarmA==null||paused)return;
            cmd.BeginSample(GpuSamples.Get("Army.SwarmRoutes"));
            int cells=Field.Resolution*Field.Resolution;
            if(swarmDirty){swarmRevision++;swarmDirty=false;swarmRouting=true;
                int reset=swarmNavigation.FindKernel("ResetSwarmFields");SwarmNavBind(reset);
                foreach(var buffer in new[]{swarmA,swarmB}){cmd.SetComputeBufferParam(swarmNavigation,reset,"_FieldWrite",buffer);cmd.DispatchCompute(swarmNavigation,reset,(cells+127)/128,1,1);}
                int seed=swarmNavigation.FindKernel("SeedSwarmBuildings");SwarmNavBind(seed);cmd.DispatchCompute(swarmNavigation,seed,(Math.Max(1,structureCount)+127)/128,1,1);
                int apply=swarmNavigation.FindKernel("ApplySwarmSeeds");SwarmNavBind(apply);foreach(var buffer in new[]{swarmA,swarmB}){cmd.SetComputeBufferParam(swarmNavigation,apply,"_FieldWrite",buffer);cmd.DispatchCompute(swarmNavigation,apply,(cells+127)/128,1,1);}}
            if(swarmRouting)
            {
                int clear=swarmNavigation.FindKernel("ClearSwarmChanged"),relax=swarmNavigation.FindKernel("RelaxSwarmFields");
                SwarmNavBind(clear);cmd.DispatchCompute(swarmNavigation,clear,1,1,1);SwarmNavBind(relax);
                for(int i=0;i<8;i++){cmd.SetComputeBufferParam(swarmNavigation,relax,"_FieldRead",i%2==0?swarmA:swarmB);cmd.SetComputeBufferParam(swarmNavigation,relax,"_FieldWrite",i%2==0?swarmB:swarmA);cmd.DispatchCompute(swarmNavigation,relax,(cells+127)/128,1,1);}
                int publish=swarmNavigation.FindKernel("PublishSwarmField");SwarmNavBind(publish);cmd.SetComputeBufferParam(swarmNavigation,publish,"_FieldRead",swarmA);cmd.SetComputeBufferParam(swarmNavigation,publish,"_Published",swarmPublished);cmd.DispatchCompute(swarmNavigation,publish,(cells+127)/128,1,1);
                if(!swarmRoutePending){swarmRoutePending=true;uint revision=swarmRevision;cmd.RequestAsyncReadback(swarmChanged,r=>{if(disposed)return;swarmRoutePending=false;if(!r.hasError&&revision==swarmRevision&&r.GetData<uint>()[0]==0){swarmRouting=false;swarmHasRoute=true;}});}
            }
            cmd.EndSample(GpuSamples.Get("Army.SwarmRoutes"));
            if(swarmPending||SwarmError.Length>0)return;
            // Wait for a complete route before spawning; this prevents unreachable island arrivals.
            spawningAmbient=AmbientZombieRemaining>0;
            if(Time.realtimeSinceStartupAsDouble<swarmNextStatus)return;
            uint waveRequest=swarmRequest;
            if(spawningAmbient)swarmRequest=Math.Min(16384u,AmbientZombieRemaining);
            bool spawn=swarmRequest>0&&(spawningAmbient||swarmHasRoute||!swarmRouting);
            swarmNextStatus=Time.realtimeSinceStartupAsDouble+.25;swarmPending=true;
            cmd.BeginSample(GpuSamples.Get("Army.SwarmSpawnAndOrders"));
            if(spawn){SwarmDispatch("ReserveSwarmSlots",1);SwarmDispatch("SpawnSwarm",(int)swarmRequest);SwarmDispatch("ReclaimSwarmSlots",1);}
            SwarmDispatch("ResetSwarmAlive",1);SwarmDispatch("SwarmOrders",Settings.activeUnits);
            cmd.EndSample(GpuSamples.Get("Army.SwarmSpawnAndOrders"));
            bool ambient=spawningAmbient;var callback=spawn&&!ambient?swarmCallback:null;
            if(spawn){swarmRequest=ambient?waveRequest:0;if(!ambient)swarmCallback=null;}
            cmd.RequestAsyncReadback(swarmCounts,r=>
            {
                if(disposed)return;swarmPending=false;
                if(r.hasError){ReadbackFailed=true;SwarmError="Swarm GPU acknowledgement failed; restart the match to avoid duplicating attackers.";Notice=SwarmError;return;}
                var data=r.GetData<uint>();SwarmAlive=data[1];
                // Advance failed attempts too: an unreachable movement class must not
                // repeat the same random draw forever when only one attacker remains.
                if(spawn){swarmSerial+=data[2];if(data[0]>0)swarmNextStatus=0;if(ambient)AmbientZombieRemaining-=Math.Min(AmbientZombieRemaining,data[0]);else callback?.Invoke(data[0]);}
            });
        }
        void SwarmNavBind(int k)
        {
            cmd.SetComputeIntParam(swarmNavigation,"_NavRes",Field.Resolution);cmd.SetComputeFloatParam(swarmNavigation,"_WorldSize",Field.Size);cmd.SetComputeIntParam(swarmNavigation,"_BuildingCount",structureCount);
            cmd.SetComputeIntParam(swarmNavigation,"_OnlyPlayer",Settings.swarmMode?1:0);
            cmd.SetComputeBufferParam(swarmNavigation,k,"_Costs",Field.Costs);cmd.SetComputeBufferParam(swarmNavigation,k,"_Structures",structures);cmd.SetComputeBufferParam(swarmNavigation,k,"_Changed",swarmChanged);
            cmd.SetComputeBufferParam(swarmNavigation,k,"_Seeds",swarmSeeds);
        }
        void DisposeSwarm()
        {
            foreach(var buffer in new[]{swarmA,swarmB,swarmPublished,swarmSeeds,swarmChanged,swarmCounts,swarmScratch,swarmTypes,swarmSites})buffer?.Dispose();ArmyLifetime.Destroy(swarmNavigation);
        }
#if UNITY_EDITOR
        public void AdvanceSwarmValidation()=>swarmNextStatus=0;
        public void DamageSwarmForValidation(ComputeShader validation)
        {
            int k=validation.FindKernel("DamageSwarm");validation.SetInt("_Active",Settings.activeUnits);validation.SetBuffer(k,"_Units",current);validation.SetBuffer(k,"_Damage",damage);validation.Dispatch(k,(Settings.activeUnits+127)/128,1,1);
        }
#endif
    }
}
