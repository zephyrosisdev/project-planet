using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    /// <summary>Same group integration solver as GpuNavigation; six faces with independent render topology.</summary>
    public sealed class PlanetPathfindingAdapter : IDisposable
    {
        public readonly int Resolution,CellCount,GroupCount;
        public GraphicsBuffer Costs {get;}
        public GraphicsBuffer Groups {get;}
        public GraphicsBuffer Global=>a;
        public GraphicsBuffer Coarse {get;}
        GraphicsBuffer a,b,changed;
        readonly ComputeShader shader,goalShader;readonly GraphicsBuffer goalPick;readonly uint[] picked={uint.MaxValue,uint.MaxValue};
        readonly int reset,relax,clear,coarse;
        readonly GroupOrder[] orders;
        readonly bool[] pending;
        bool readbackPending,disposed;double nextReadback;
        internal Vector2 GoalForValidation(int group)=>orders[group].destination;
        public bool HasGoal(int group)=>orders[group].sequence!=0;
        bool terrainRefreshReading;int terrainEpoch;readonly HashSet<int> terrainRefresh=new HashSet<int>();
        internal bool TerrainRefreshPendingForValidation=>terrainRefreshReading||terrainRefresh.Count>0;
        internal uint GoalSequenceForValidation(int group)=>orders[group].sequence;
        public void InvalidateAsync(){terrainEpoch++;for(int i=0;i<GroupCount;i++)if(HasGoal(i))terrainRefresh.Add(i);}
        void RefreshTerrainGoal(){if(terrainRefreshReading||terrainRefresh.Count==0)return;int group=-1;foreach(int candidate in terrainRefresh)if(group<0||candidate<group)group=candidate;terrainRefresh.Remove(group);if(!HasGoal(group))return;
            var direction=CubeSphere.Decode(orders[group].destination);var movement=(MovementClass)orders[group].movement;uint sequence=orders[group].sequence;int epoch=terrainEpoch;
            picked[0]=picked[1]=uint.MaxValue;goalPick.SetData(picked);goalShader.SetInt("_Res",Resolution);goalShader.SetInt("_Movement",(int)movement);goalShader.SetVector("_Direction",direction);
            foreach(string name in new[]{"Closest","Resolve"}){int k=goalShader.FindKernel(name);goalShader.SetBuffer(k,"_Costs",Costs);goalShader.SetBuffer(k,"_Pick",goalPick);goalShader.Dispatch(k,(CellCount+127)/128,1,1);}
            terrainRefreshReading=true;AsyncGPUReadback.Request(goalPick,result=>{if(disposed)return;terrainRefreshReading=false;if(result.hasError){terrainRefresh.Add(group);return;}if(epoch!=terrainEpoch||orders[group].sequence!=sequence)return;uint choice=result.GetData<uint>()[1];ApplyResolvedGoal(group,direction,movement,true,choice);});
        }
        public void Invalidate(){for(int i=0;i<GroupCount;i++)if(HasGoal(i))SetGoal(i,CubeSphere.Decode(orders[i].destination),(MovementClass)orders[i].movement,true);}
        readonly float radius;
        public PlanetRegionalNavigation Regional {get;private set;}
        public void EnableRegional(PlanetDefinition definition){Regional=new PlanetRegionalNavigation(definition,GroupCount,Groups);}
        public void SetRegionalBuildings(GraphicsBuffer buffer,int count)=>Regional?.SetBuildings(buffer,count);
        public long BufferBytes=>CellCount*(5L*4+GroupCount*8L)+GroupCount*(GroupOrder.Stride+4L+6*32*32*4L);
        public PlanetPathfindingAdapter(int resolution,int groups,float radius,GraphicsBuffer costs)
        {
            if(resolution<4||resolution>512||groups<1||groups>BattleSettings.Groups)throw new ArgumentOutOfRangeException();
            goalShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetGoal"));goalPick=Buffer(2,4);
            Resolution=resolution;CellCount=6*resolution*resolution;GroupCount=groups;this.radius=radius;Costs=costs;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Navigation"));shader.EnableKeyword("PLANET_NAV");shader.DisableKeyword("BUILDING_GRID");
            reset=shader.FindKernel("ResetFields");relax=shader.FindKernel("RelaxFields");clear=shader.FindKernel("ClearChanged");coarse=shader.FindKernel("BuildCoarse");
            a=Buffer(groups*CellCount,4);b=Buffer(groups*CellCount,4);changed=Buffer(groups,4);Groups=Buffer(groups,GroupOrder.Stride);Coarse=Buffer(groups*6*32*32,4);orders=new GroupOrder[groups];pending=new bool[groups];Groups.SetData(orders);changed.SetData(new uint[groups]);
        }
        static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
        void Bind(int group)
        {
            shader.SetInt("_NavRes",Resolution);shader.SetInt("_WorkGroup",group);shader.SetFloat("_PlanetRadius",radius);
            foreach(int k in new[]{reset,relax}){shader.SetBuffer(k,"_Groups",Groups);shader.SetBuffer(k,"_Costs",Costs);}
            shader.SetBuffer(relax,"_Changed",changed);shader.SetBuffer(clear,"_Changed",changed);
        }
        public void SetGoal(int group,Vector3 direction,MovementClass movement=MovementClass.HeavyVehicle,bool force=false)
        {
            direction.Normalize();if(!force&&HasGoal(group)&&orders[group].movement==(uint)movement&&(CubeSphere.Decode(orders[group].destination)-direction).sqrMagnitude<1e-10f)return;
            picked[0]=picked[1]=uint.MaxValue;goalPick.SetData(picked);goalShader.SetInt("_Res",Resolution);goalShader.SetInt("_Movement",(int)movement);goalShader.SetVector("_Direction",direction);
            foreach(string name in new[]{"Closest","Resolve"}){int k=goalShader.FindKernel(name);goalShader.SetBuffer(k,"_Costs",Costs);goalShader.SetBuffer(k,"_Pick",goalPick);goalShader.Dispatch(k,(CellCount+127)/128,1,1);}goalPick.GetData(picked);
            ApplyResolvedGoal(group,direction,movement,force,picked[1]);
        }
        void ApplyResolvedGoal(int group,Vector3 direction,MovementClass movement,bool force,uint choice){
            if(choice==uint.MaxValue&&Regional==null)return;
            var coarseDirection=direction;
            if(choice!=uint.MaxValue&&choice!=(uint)CubeSphere.Cell(0,direction,Resolution).Index(Resolution)){int cell=(int)choice;coarseDirection=CubeSphere.Direction(new PlanetNavCoordinate(0,(CubeFace)(cell/(Resolution*Resolution)),cell%Resolution,cell/Resolution%Resolution),Resolution);}
            if(Regional==null)direction=coarseDirection;
            var p=CubeSphere.Encode(direction);if(!force&&HasGoal(group)&&orders[group].movement==(uint)movement&&(orders[group].destination-p).sqrMagnitude<1e-10f)return;orders[group]=new GroupOrder{destination=p,localOrigin=CubeSphere.Encode(coarseDirection),sequence=orders[group].sequence+1,movement=(uint)movement,count=1,columns=1};pending[group]=true;Groups.SetData(orders);
            Regional?.SetGoal(group,direction,orders[group].sequence);
            Bind(group);foreach(var buffer in new[]{a,b}){shader.SetBuffer(reset,"_FieldWrite",buffer);shader.Dispatch(reset,(CellCount+127)/128,1,1);}
        }
        public void Relax(int group,int iterations)
        {
            // Other group slices must remain in the current buffer. Finish on the same buffer.
            if((iterations&1)!=0)iterations++;
            Bind(group);shader.Dispatch(clear,1,1,1);
            for(int i=0;i<iterations;i++){shader.SetBuffer(relax,"_FieldRead",a);shader.SetBuffer(relax,"_FieldWrite",b);shader.Dispatch(relax,(CellCount+127)/128,1,1);var swap=a;a=b;b=swap;}
            shader.SetBuffer(coarse,"_FieldRead",a);shader.SetBuffer(coarse,"_CoarseField",Coarse);shader.Dispatch(coarse,48,1,1);
        }
        public void RelaxPending(int iterations)
        {
            RefreshTerrainGoal();Regional?.RelaxPending(iterations);
            bool work=false;for(int group=0;group<GroupCount;group++)if(pending[group]){Relax(group,iterations);work=true;}
            if(!work||readbackPending||Time.realtimeSinceStartupAsDouble<nextReadback)return;
            var sequences=new uint[GroupCount];for(int i=0;i<GroupCount;i++)sequences[i]=pending[i]?orders[i].sequence:0;
            readbackPending=true;nextReadback=Time.realtimeSinceStartupAsDouble+.25;
            AsyncGPUReadback.Request(changed,result=>{
                if(disposed)return;readbackPending=false;if(result.hasError)return;var values=result.GetData<uint>();
                for(int i=0;i<GroupCount;i++)if(sequences[i]!=0&&orders[i].sequence==sequences[i]&&values[i]==0)pending[i]=false;
            });
        }
        public void Dispose(){disposed=true;Regional?.Dispose();goalPick.Dispose();ArmyLifetime.Destroy(goalShader);a.Dispose();b.Dispose();changed.Dispose();Groups.Dispose();Coarse.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
