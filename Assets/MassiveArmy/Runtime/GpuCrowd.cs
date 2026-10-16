using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    // A unilateral density constraint: pressure can resist compression, never attract distant tanks.
    public sealed class GpuCrowd : IDisposable
    {
        readonly ComputeShader shader;
        readonly GraphicsBuffer accumulation,pressureA,pressureB;
        readonly Battlefield field;
        readonly int clear,splat,density,relax,finish,initialize;
        public readonly int Resolution;
        public GraphicsBuffer Grid {get;}
        public static int GridResolution(float size) => Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(size/8)),64,512);
        public GpuCrowd(Battlefield field,bool zombies=false)
        {
            this.field=field;Resolution=GridResolution(field.Size);int cells=Resolution*Resolution;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Crowd"));
            if(zombies)shader.EnableKeyword("ZOMBIES");
            initialize=shader.FindKernel("InitializePressure");clear=shader.FindKernel("ClearDensity");splat=shader.FindKernel("SplatDensity");
            density=shader.FindKernel("BuildDensity");relax=shader.FindKernel("RelaxPressure");finish=shader.FindKernel("BuildPressureGradient");
            accumulation=Buffer(cells,4,"Crowd fixed-point mass");pressureA=Buffer(cells,4,"Crowd pressure A");pressureB=Buffer(cells,4,"Crowd pressure B");
            Grid=Buffer(cells,16,"Crowd density, open cells and pressure gradient");
            using(var cmd=new CommandBuffer()) {
                Parameters(cmd);cmd.SetComputeBufferParam(shader,initialize,"_CrowdGrid",Grid);cmd.SetComputeBufferParam(shader,initialize,"_PressureWrite",pressureA);Dispatch(cmd,initialize,cells);
                cmd.SetComputeBufferParam(shader,initialize,"_PressureWrite",pressureB);Dispatch(cmd,initialize,cells);Graphics.ExecuteCommandBuffer(cmd);
            }
        }
        static GraphicsBuffer Buffer(int n,int stride,string name) => new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,stride){name=name};
        void Parameters(CommandBuffer cmd)
        {
            cmd.SetComputeIntParam(shader,"_CrowdRes",Resolution);cmd.SetComputeIntParam(shader,"_NavRes",field.Resolution);
            cmd.SetComputeFloatParam(shader,"_WorldSize",field.Size);cmd.SetComputeFloatParam(shader,"_Delta",BattleSettings.Tick);
        }
        void Dispatch(CommandBuffer cmd,int kernel,int n) => cmd.DispatchCompute(shader,kernel,(n+127)/128,1,1);
        public void Record(CommandBuffer cmd,GraphicsBuffer units,int active,GraphicsBuffer types,bool baseMode)
        {
            cmd.BeginSample(GpuSamples.Get("Army.CrowdPressure"));Parameters(cmd);int cells=Resolution*Resolution;
            cmd.SetComputeIntParam(shader,"_Active",active);cmd.SetComputeBufferParam(shader,clear,"_Mass",accumulation);Dispatch(cmd,clear,cells);
            cmd.SetComputeBufferParam(shader,splat,"_Units",units);cmd.SetComputeBufferParam(shader,splat,"_Mass",accumulation);
            cmd.SetComputeBufferParam(shader,splat,"_Types",types);cmd.SetComputeIntParam(shader,"_BaseMode",baseMode?1:0);
            cmd.SetComputeBufferParam(shader,splat,"_Costs",field.Costs);Dispatch(cmd,splat,active);
            cmd.SetComputeBufferParam(shader,density,"_Mass",accumulation);cmd.SetComputeBufferParam(shader,density,"_CrowdGrid",Grid);
            cmd.SetComputeBufferParam(shader,density,"_Costs",field.Costs);Dispatch(cmd,density,cells);
            cmd.SetComputeBufferParam(shader,relax,"_CrowdRead",Grid);
            for(int i=0;i<8;i++) {
                cmd.SetComputeBufferParam(shader,relax,"_PressureRead",i%2==0?pressureA:pressureB);
                cmd.SetComputeBufferParam(shader,relax,"_PressureWrite",i%2==0?pressureB:pressureA);Dispatch(cmd,relax,cells);
            }
            cmd.SetComputeBufferParam(shader,finish,"_PressureRead",pressureA);cmd.SetComputeBufferParam(shader,finish,"_CrowdGrid",Grid);Dispatch(cmd,finish,cells);
            cmd.EndSample(GpuSamples.Get("Army.CrowdPressure"));
        }
        public void Dispose() { accumulation.Dispose();pressureA.Dispose();pressureB.Dispose();Grid.Dispose();ArmyLifetime.Destroy(shader); }
    }
}
