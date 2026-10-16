using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    /// <summary>Opt-in benchmark instrumentation; never installed by normal gameplay.</summary>
    internal sealed class PlanetSimulationFence : IDisposable
    {
        readonly ComputeShader shader;readonly GraphicsBuffer completion;readonly int kernel;
        public PlanetSimulationFence(GraphicsBuffer units)
        {
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetValidationFence"));kernel=shader.FindKernel("Complete");completion=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);
            shader.SetBuffer(kernel,"_Units",units);shader.SetBuffer(kernel,"_Completion",completion);
        }
        public void Wait(uint tick,int count)
        {
            shader.SetInt("_Count",count);shader.SetInt("_Tick",(int)tick);shader.Dispatch(kernel,1,1,1);
            var request=AsyncGPUReadback.Request(completion);request.WaitForCompletion();
            if(request.hasError||request.GetData<uint>()[0]!=tick)throw new InvalidOperationException("Simulation completion fence returned an error or stale tick");
        }
        public void Dispose(){completion.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
