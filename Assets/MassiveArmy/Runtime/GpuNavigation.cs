using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed class GpuNavigation : IDisposable
    {
        readonly ComputeShader shader;
        readonly GraphicsBuffer costs,changed;
        readonly int resolution,reset,relax,clear,coarseKernel,invalidate;
        readonly float worldSize;
        readonly Battlefield field;
        GraphicsBuffer globalA,globalB;
        public GraphicsBuffer Groups {get;}
        public GraphicsBuffer Coarse {get;}
        public GraphicsBuffer Global => globalA;
        readonly GroupOrder[] orders=new GroupOrder[BattleSettings.Groups+1];
        readonly bool[] pending=new bool[BattleSettings.Groups];
        readonly uint[] recordedSequence=new uint[BattleSettings.Groups];
        bool disposed,readbackPending;
        double nextReadback;
        public int RoutingCount { get { int n=0;foreach(bool p in pending)if(p)n++;return n; } }
        public GroupOrder GetOrder(int group) => orders[group];
        public GpuNavigation(BattleSettings settings,Battlefield field)
        {
            this.field=field;costs=field.Costs;resolution=settings.navigationResolution;worldSize=settings.worldSize;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Navigation"));
            if(settings.baseBuilding)shader.EnableKeyword("BUILDING_GRID");
            invalidate=shader.FindKernel("InvalidateRegion");reset=shader.FindKernel("ResetFields");relax=shader.FindKernel("RelaxFields");clear=shader.FindKernel("ClearChanged");coarseKernel=shader.FindKernel("BuildCoarse");
            Groups=Buffer(orders.Length,GroupOrder.Stride,"Army formation commands");
            globalA=Buffer(BattleSettings.Groups*resolution*resolution,4,"Connected integration A");
            globalB=Buffer(BattleSettings.Groups*resolution*resolution,4,"Connected integration B");
            Coarse=Buffer(BattleSettings.Groups*32*32,4,"Coarse lookahead from exact terrain integration");
            changed=Buffer(BattleSettings.Groups,4,"Route convergence flags");
            int perTeam=(settings.activeUnits+1)/2;
            for(int team=0;team<2;team++)
            {
                Vector2 home=settings.preview?.Starts[team]??field.StrategicSites[team*8],enemy=settings.preview?.Starts[1-team]??field.StrategicSites[(1-team)*8];
                Vector2 forward=WorldTopology.Delta(home,enemy,worldSize).normalized;
                orders[team]=Create(new FormationLayout(enemy,new Vector2(forward.y,-forward.x),worldSize*.6f),(uint)team,ArmyOrder.AttackMove,1);
                orders[team].count=(uint)(team==0?perTeam:settings.activeUnits-perTeam);
                orders[team].columns=Math.Min(orders[team].columns,Math.Max(1u,orders[team].count));
                orders[team].localOrigin=field.TraversableDestination(Centre(orders[team]));pending[team]=true;
            }
            if(settings.baseBuilding)for(int i=0;i<2;i++){orders[i].sequence=0;pending[i]=false;}
            Groups.SetData(orders);
            using(var cmd=new CommandBuffer{name="Initialize shared connected navigation"})
            {
                for(int group=0;group<BattleSettings.Groups;group++)Reset(cmd,group);
                Record(cmd,resolution*2);Graphics.ExecuteCommandBuffer(cmd);
            }
        }
        static GraphicsBuffer Buffer(int n,int stride,string name) => new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,stride){name=name};
        public static Vector2 Centre(GroupOrder g)
        {
            uint rows=(g.count+Math.Max(1u,g.columns)-1)/Math.Max(1u,g.columns);
            return g.destination-new Vector2(-g.right.y,g.right.x)*Math.Max(0,(int)rows-1)*g.spacing*.5f;
        }
        GroupOrder Create(FormationLayout layout,uint team,ArmyOrder order,uint sequence) => new GroupOrder {
            destination=layout.Front,localOrigin=layout.Front,right=layout.Right,width=layout.Width,
            spacing=layout.Spacing,columns=(uint)Mathf.Max(1,Mathf.FloorToInt(layout.Width/layout.Spacing)+1),
            arrivalRadius=BattleSettings.FormationArrivalRadius,sequence=sequence,movement=(uint)MovementClass.HeavyVehicle,order=(uint)order,team=team
        };
        public int AllocatePlayerGroup(uint[] occupancy)
        {
            // Exact command-time GPU counts. Retargeting an entire group reuses its field immediately.
            for(int i=2;i<BattleSettings.Groups;i++)
                if(occupancy[i]>0 && occupancy[i]==occupancy[BattleSettings.Groups+i])return i;
            for(int i=2;i<BattleSettings.Groups;i++)
                if(occupancy[i]==0)return i;
            return -1;
        }
        public void UploadFormation(CommandBuffer cmd,int group,FormationLayout layout,uint team,ArmyOrder order,int movement=2)
        {
            orders[group]=Create(layout,team,order,orders[group].sequence+1);orders[group].movement=(uint)movement;
            cmd.SetBufferData(Groups,orders,group,group,1);
            if(group<BattleSettings.Groups)pending[group]=true;
        }
        public void StageStrategicOrder(CommandBuffer cmd,int group,Vector2 target,uint team,ArmyOrder order)
        {
            var g=orders[group];g.destination=target;g.localOrigin=target;g.order=(uint)order;g.sequence++;g.padding=0;
            cmd.SetBufferData(Groups,new[]{g},0,BattleSettings.PreviewGroup,1);
        }
        public void SetOrder(CommandBuffer cmd,int group,Vector2 target,uint team,ArmyOrder order)
        {
            var g=orders[group];g.destination=target;g.localOrigin=field.TraversableDestination(Centre(g));g.order=(uint)order;g.padding=0;g.sequence++;
            orders[group]=g;pending[group]=true;cmd.SetBufferData(Groups,orders,group,group,1);
        }
        public bool SetFactionOrder(CommandBuffer cmd,int group,int team,int movement,Vector2 target)
        {
            var old=orders[group];if(old.sequence!=0&&old.team==team&&old.movement==movement&&field.Distance(old.destination,target)<worldSize/resolution)return false;
            var g=Create(new FormationLayout(target,Vector2.right,32),(uint)team,ArmyOrder.AttackMove,old.sequence+1);g.movement=(uint)movement;g.count=1;g.columns=1;g.localOrigin=target;
            orders[group]=g;pending[group]=true;cmd.SetBufferData(Groups,orders,group,group,1);return true;
        }
        void Parameters(CommandBuffer cmd,int group)
        {
            cmd.SetComputeIntParam(shader,"_NavRes",resolution);cmd.SetComputeFloatParam(shader,"_WorldSize",worldSize);
            cmd.SetComputeIntParam(shader,"_WorkGroup",group);cmd.SetComputeBufferParam(shader,relax,"_Groups",Groups);
            cmd.SetComputeBufferParam(shader,relax,"_Costs",costs);cmd.SetComputeBufferParam(shader,relax,"_Changed",changed);
        }
        public void Reset(CommandBuffer cmd,int group)
        {
            Parameters(cmd,group);cmd.SetComputeBufferParam(shader,reset,"_Groups",Groups);cmd.SetComputeBufferParam(shader,reset,"_Costs",costs);
            foreach(var field in new[]{globalA,globalB}) { cmd.SetComputeBufferParam(shader,reset,"_FieldWrite",field);cmd.DispatchCompute(shader,reset,(resolution*resolution+127)/128,1,1); }
            BuildCoarse(cmd,group);
        }
        void BuildCoarse(CommandBuffer cmd,int group)
        {
            cmd.SetComputeIntParam(shader,"_WorkGroup",group);cmd.SetComputeIntParam(shader,"_NavRes",resolution);
            cmd.SetComputeBufferParam(shader,coarseKernel,"_FieldRead",globalA);cmd.SetComputeBufferParam(shader,coarseKernel,"_CoarseField",Coarse);
            cmd.DispatchCompute(shader,coarseKernel,8,1,1);
        }
        public void InvalidateLocal(CommandBuffer cmd,Bounds region)
        {
            float cell=worldSize/resolution;
            int x0=Mathf.FloorToInt((region.min.x/worldSize+.5f)*resolution)-12,y0=Mathf.FloorToInt((region.min.z/worldSize+.5f)*resolution)-12;
            int x1=Mathf.CeilToInt((region.max.x/worldSize+.5f)*resolution)+12,y1=Mathf.CeilToInt((region.max.z/worldSize+.5f)*resolution)+12;
            // At most four rectangles for a seam-crossing footprint and its route margin.
            for(int sy=-1;sy<=1;sy++)for(int sx=-1;sx<=1;sx++){
                int ax=Mathf.Max(0,x0+sx*resolution),ay=Mathf.Max(0,y0+sy*resolution),bx=Mathf.Min(resolution-1,x1+sx*resolution),by=Mathf.Min(resolution-1,y1+sy*resolution);if(ax>bx||ay>by)continue;
                cmd.SetComputeVectorParam(shader,"_DirtyRegion",new Vector4(ax,ay,bx,by));
                for(int group=0;group<BattleSettings.Groups;group++)if(orders[group].sequence!=0){
                    Parameters(cmd,group);pending[group]=true;cmd.SetComputeBufferParam(shader,invalidate,"_Groups",Groups);
                    foreach(var buffer in new[]{globalA,globalB}){cmd.SetComputeBufferParam(shader,invalidate,"_FieldWrite",buffer);cmd.DispatchCompute(shader,invalidate,((bx-ax+1)*(by-ay+1)+127)/128,1,1);}
                }
            }
        }

        public void Record(CommandBuffer cmd,int iterations=8)
        {
            // Iteration count is even: every group keeps its own stable ping-pong ownership.
            iterations=(iterations+1)&~1;
            cmd.BeginSample(GpuSamples.Get("Army.Navigation"));
            for(int group=0;group<BattleSettings.Groups;group++)if(pending[group])
            {
                Parameters(cmd,group);cmd.SetComputeBufferParam(shader,clear,"_Changed",changed);cmd.DispatchCompute(shader,clear,1,1,1);
                for(int i=0;i<iterations;i++)
                {
                    cmd.SetComputeBufferParam(shader,relax,"_FieldRead",i%2==0?globalA:globalB);
                    cmd.SetComputeBufferParam(shader,relax,"_FieldWrite",i%2==0?globalB:globalA);
                    cmd.DispatchCompute(shader,relax,(resolution*resolution+127)/128,1,1);
                }
                BuildCoarse(cmd,group);
                recordedSequence[group]=orders[group].sequence;
            }
            cmd.EndSample(GpuSamples.Get("Army.Navigation"));
        }
        public void RequestConvergence()
        {
            if(disposed || readbackPending || Time.realtimeSinceStartupAsDouble<nextReadback || RoutingCount==0)return;
            readbackPending=true;nextReadback=Time.realtimeSinceStartupAsDouble+.25;
            var sequence=new uint[BattleSettings.Groups];for(int i=0;i<sequence.Length;i++)sequence[i]=recordedSequence[i]==orders[i].sequence?orders[i].sequence:uint.MaxValue;
            AsyncGPUReadback.Request(changed,r=> {
                if(disposed)return;readbackPending=false;if(r.hasError)return;
                var data=r.GetData<uint>();for(int i=0;i<data.Length;i++)if(sequence[i]!=uint.MaxValue && orders[i].sequence==sequence[i] && data[i]==0)pending[i]=false;
            });
        }
        public void Dispose() { if(disposed)return;disposed=true;Groups.Dispose();Coarse.Dispose();globalA.Dispose();globalB.Dispose();changed.Dispose();ArmyLifetime.Destroy(shader); }
    }
}
