using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    // One reusable tactical patch per shared order, not per unit or per planet tile.
    // Large-scale travel still uses PlanetPathfindingAdapter's coarse cube-sphere fields.
    public sealed class PlanetRegionalNavigation : IDisposable
    {
        public const int Resolution=128;public const float CellSize=16;
        public readonly GraphicsBuffer Frames,Costs,Field,Pick;
        readonly GraphicsBuffer other,changed,emptyBuildings,groups;
        readonly ComputeShader solver,generator;readonly PlanetDefinition definition;
        readonly int groupCount;readonly bool[] pending;readonly uint[] sequences;
        readonly Vector4[] frames;GraphicsBuffer buildings;int buildingCount;bool disposed,reading;double nextRead;
        readonly int reset,relax,clear,generate;
        public long BufferBytes=>groupCount*(Resolution*Resolution*12L+3*16+8)+16;
        public PlanetRegionalNavigation(PlanetDefinition definition,int count,GraphicsBuffer groups){
            this.definition=definition;groupCount=count;this.groups=groups;int cells=count*Resolution*Resolution;
            Frames=Buffer(count*3,16);Costs=Buffer(cells,4);Field=Buffer(cells,4);other=Buffer(cells,4);Pick=Buffer(count,4);changed=Buffer(count,4);emptyBuildings=Buffer(1,16);emptyBuildings.SetData(new Vector4[1]);buildings=emptyBuildings;
            frames=new Vector4[count*3];Frames.SetData(frames);pending=new bool[count];sequences=new uint[count];
            solver=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Navigation"));solver.DisableKeyword("PLANET_NAV");solver.DisableKeyword("BUILDING_GRID");solver.EnableKeyword("PLANET_LOCAL_NAV");
            generator=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetRegionalCosts"));PlanetDevelopment.BindSurface(generator,definition);
            reset=solver.FindKernel("ResetFields");relax=solver.FindKernel("RelaxFields");clear=solver.FindKernel("ClearChanged");generate=generator.FindKernel("Generate");
            solver.SetInt("_NavRes",Resolution);generator.SetInt("_Res",Resolution);generator.SetFloat("_CellSize",CellSize);
            foreach(int k in new[]{reset,relax}){solver.SetBuffer(k,"_Groups",groups);solver.SetBuffer(k,"_Costs",Costs);solver.SetBuffer(k,"_LocalGoal",Pick);}
            solver.SetBuffer(relax,"_Changed",changed);solver.SetBuffer(clear,"_Changed",changed);
            generator.SetBuffer(generate,"_Groups",groups);generator.SetBuffer(generate,"_Frames",Frames);generator.SetBuffer(generate,"_Buildings",buildings);generator.SetBuffer(generate,"_Costs",Costs);generator.SetBuffer(generate,"_Pick",Pick);
        }
        static GraphicsBuffer Buffer(int n,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,stride);
        public void SetBuildings(GraphicsBuffer buffer,int count){buildings=buffer;buildingCount=count;generator.SetBuffer(generate,"_Buildings",buildings);generator.SetInt("_BuildingCount",count);}
        public void SetGoal(int group,Vector3 direction,uint sequence){
            var right=Vector3.Cross(Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up,direction).normalized;var up=Vector3.Cross(direction,right);
            frames[group*3]=new Vector4(direction.x,direction.y,direction.z,1);frames[group*3+1]=right;frames[group*3+2]=up;Frames.SetData(frames,group*3,group*3,3);
            Pick.SetData(new[]{uint.MaxValue},0,group,1);generator.SetInt("_Group",group);generator.Dispatch(generate,Resolution*Resolution/128,1,1);
            solver.SetInt("_WorkGroup",group);foreach(var field in new[]{Field,other}){solver.SetBuffer(reset,"_FieldWrite",field);solver.Dispatch(reset,Resolution*Resolution/128,1,1);}
            pending[group]=true;sequences[group]=sequence;
        }
        public void RelaxPending(int iterations){
            if((iterations&1)!=0)iterations++;bool work=false;
            for(int group=0;group<groupCount;group++)if(pending[group]){work=true;solver.SetInt("_WorkGroup",group);solver.Dispatch(clear,1,1,1);
                for(int i=0;i<iterations;i++){solver.SetBuffer(relax,"_FieldRead",i%2==0?Field:other);solver.SetBuffer(relax,"_FieldWrite",i%2==0?other:Field);solver.Dispatch(relax,Resolution*Resolution/128,1,1);}}
            if(!work||reading||Time.realtimeSinceStartupAsDouble<nextRead)return;reading=true;nextRead=Time.realtimeSinceStartupAsDouble+.25;var issued=(uint[])sequences.Clone();
            AsyncGPUReadback.Request(changed,result=>{if(disposed)return;reading=false;if(result.hasError)return;var values=result.GetData<uint>();for(int i=0;i<groupCount;i++)if(issued[i]==sequences[i]&&values[i]==0)pending[i]=false;});
        }
        public void Bind(ComputeShader shader,int kernel){
            shader.SetInt("_RegionalResolution",Resolution);shader.SetFloat("_RegionalCellSize",CellSize);shader.SetInt("_RegionalBuildingCount",buildingCount);
            shader.SetBuffer(kernel,"_RegionalFrames",Frames);shader.SetBuffer(kernel,"_RegionalCosts",Costs);shader.SetBuffer(kernel,"_RegionalField",Field);shader.SetBuffer(kernel,"_RegionalPick",Pick);shader.SetBuffer(kernel,"_RegionalBuildings",buildings);
        }
        public void Dispose(){disposed=true;Frames.Dispose();Costs.Dispose();Field.Dispose();other.Dispose();Pick.Dispose();changed.Dispose();emptyBuildings.Dispose();ArmyLifetime.Destroy(solver);ArmyLifetime.Destroy(generator);}
    }
}
