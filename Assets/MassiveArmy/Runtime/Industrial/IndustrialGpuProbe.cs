using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Industrial {
    /// <summary>Opt-in graphics-enabled server-process feasibility probe. Never creates a CPU fleet mirror.</summary>
    public sealed class IndustrialGpuProbe:MonoBehaviour {
        [Serializable] sealed class Report {public bool passed;public string device,api,error;public int units,stride;public long bytes,unityAllocatedBytes,managedBytes;}
        public static bool Requested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-industry-gpu-probe")>=0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-industry-gpu-probe")<0)return;var host=new GameObject("Industrial GPU server feasibility probe");DontDestroyOnLoad(host);host.AddComponent<IndustrialGpuProbe>();}
        IEnumerator Start(){
            yield return null;foreach(var app in FindObjectsByType<BattleApp>(FindObjectsSortMode.None))app.enabled=false;
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-industry-output");string directory=index>=0&&index+1<args.Length?args[index+1]:"TestResults/Industrial/GpuProbe";Directory.CreateDirectory(directory);
            var report=new Report{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),units=4096,stride=UnitState.Stride};GraphicsBuffer original=null,restored=null;ComputeShader shader=null;
            try {
                if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!SystemInfo.supportsComputeShaders)throw new InvalidOperationException("A working compute graphics device is required; do not launch with -nographics");
                original=new GraphicsBuffer(GraphicsBuffer.Target.Structured,report.units,UnitState.Stride);shader=Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));if(shader==null)throw new InvalidOperationException("PlanetDevelopment compute asset missing from player");
                shader.SetInt("_PlanetUnitCount",report.units);shader.SetFloat("_PlanetRadius",6000);shader.SetFloat("_PlanetSpacing",5.2f);shader.SetVector("_PlanetStart",new Vector4(.5f,.5f,0,0));shader.SetVector("_PlanetGoal",new Vector4(.6f,.5f,0,0));int kernel=shader.FindKernel("Spawn");shader.SetBuffer(kernel,"_PlanetUnits",original);shader.Dispatch(kernel,(report.units+127)/128,1,1);
            }catch(Exception error){report.error=error.ToString();}
            if(report.error==null){
                // Serialization is a transient diagnostic checkpoint, not a maintained CPU simulation copy.
                var readback=AsyncGPUReadback.Request(original);while(!readback.done)yield return null;
                if(readback.hasError)report.error="GPU checkpoint readback failed";
                else try {
                    var checkpoint=readback.GetData<UnitState>().ToArray();var handle=GCHandle.Alloc(checkpoint,GCHandleType.Pinned);byte[] bytes=new byte[checkpoint.Length*UnitState.Stride];try{Marshal.Copy(handle.AddrOfPinnedObject(),bytes,0,bytes.Length);}finally{handle.Free();}string path=Path.Combine(directory,"gpu-checkpoint.bin");File.WriteAllBytes(path,bytes);Array.Clear(checkpoint,0,checkpoint.Length);
                    bytes=File.ReadAllBytes(path);handle=GCHandle.Alloc(checkpoint,GCHandleType.Pinned);try{Marshal.Copy(bytes,0,handle.AddrOfPinnedObject(),bytes.Length);}finally{handle.Free();}
                    restored=new GraphicsBuffer(GraphicsBuffer.Target.Structured,report.units,UnitState.Stride);restored.SetData(checkpoint);Array.Clear(checkpoint,0,checkpoint.Length);report.bytes=bytes.Length;
                }catch(Exception error){report.error=error.ToString();}
                if(report.error==null){var verify=AsyncGPUReadback.Request(restored);while(!verify.done)yield return null;if(verify.hasError)report.error="GPU restore readback failed";else{var data=verify.GetData<byte>();var expected=File.ReadAllBytes(Path.Combine(directory,"gpu-checkpoint.bin"));if(data.Length!=expected.Length)report.error="GPU checkpoint size mismatch";else for(int n=0;n<data.Length;n++)if(data[n]!=expected[n]){report.error="GPU checkpoint byte mismatch";break;}report.passed=report.error==null;}}
            }
            report.unityAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();report.managedBytes=GC.GetTotalMemory(false);original?.Dispose();restored?.Dispose();ArmyLifetime.Destroy(shader);File.WriteAllText(Path.Combine(directory,"result.json"),JsonUtility.ToJson(report,true));Application.Quit(report.passed?0:1);
        }
    }
}
