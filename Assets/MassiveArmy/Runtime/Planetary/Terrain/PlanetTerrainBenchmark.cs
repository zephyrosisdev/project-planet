using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlanetTerrainBenchmark : MonoBehaviour
    {
        PlanetSimulationFence simulationFence;PlanetDevelopment fixture;RenderTexture target,uiTarget,combined;Material uiComposite;RenderPipeline.StandardRequest request;string output,scenario;
        readonly List<double> frameMs=new List<double>(),simulationMs=new List<double>();readonly System.Diagnostics.Stopwatch simulationTimer=new System.Diagnostics.Stopwatch();double start,previous;uint firstTick,lastTick;int frames;long allocated,reserved;
        [Serializable]sealed class Report{public string gpu,api,renderer,scenario,memoryScope,limitation;public int width,height,units,navResolution,frames,seed,buildings;public float radius,errorPixels,cameraDistance;public double medianMs,p95Ms,simulationHz,simulationMedianMs,simulationP95Ms;public int residentPatches,patchBudget,deepestLevel;public bool budgetLimited;public long peakAllocatedBytes,peakReservedBytes,terrainBufferBytes;public uint activeTriangles,visibleTriangles,saturatedTriangles;public bool converged,rtsContent;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-benchmark")>=0)new GameObject("Planet terrain benchmark").AddComponent<PlanetTerrainBenchmark>();}
        string Arg(string key,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        void Start(){output=Arg("-planet-output",Application.persistentDataPath);scenario=Arg("-planet-scenario","surface");Directory.CreateDirectory(output);fixture=FindAnyObjectByType<PlanetDevelopment>();Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
        void Update(){if(scenario=="tour"&&fixture!=null&&fixture.Ready&&target!=null)fixture.Rig.SetFocusDirection(Quaternion.AngleAxis(Time.deltaTime*2,Vector3.up)*fixture.Rig.FocusDirection);}
        void LateUpdate()
        {
            if(fixture==null||!fixture.Ready)return;
            try{
            if(target==null){target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();fixture.View.enabled=false;fixture.View.targetTexture=target;request=new RenderPipeline.StandardRequest{destination=target};fixture.Rig.InputEnabled=false;fixture.Rig.Distance=scenario=="orbit"?fixture.definition.radius*3.4f:fixture.Base!=null?300:180;
                if(fixture.Base!=null){uiTarget=new RenderTexture(2560,1440,0,RenderTextureFormat.ARGB32);uiTarget.Create();combined=new RenderTexture(2560,1440,0,RenderTextureFormat.ARGBHalf);combined.Create();fixture.Hud.SetRenderTarget(uiTarget);uiComposite=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUiComposite"));uiComposite.SetTexture("_Ui",uiTarget);
                    var centre=fixture.PlanetRenderCentre;fixture.Commands.Select(fixture.View.projectionMatrix*fixture.View.worldToCameraMatrix*Matrix4x4.Translate(centre),fixture.View.transform.position-centre,new Vector2(2560,1440),Vector2.zero,new Vector2(2560,1440));if(!fixture.OrderSelected(fixture.Base.Offset(new Vector2(0,80))))throw new InvalidOperationException("RTS benchmark move order rejected");}
                simulationFence=new PlanetSimulationFence(fixture.UnitsForValidation);fixture.SimulationBeginForValidation=()=>simulationTimer.Restart();fixture.SimulationEndForValidation=()=>{if(fixture.SimulationTicks!=lastTick)simulationFence.Wait(fixture.SimulationTicks,fixture.unitCount);simulationTimer.Stop();if(Time.realtimeSinceStartupAsDouble-start>5&&fixture.SimulationTicks!=lastTick)simulationMs.Add(simulationTimer.Elapsed.TotalMilliseconds);};
                if(scenario=="fps")fixture.SetFirstPerson(true);start=previous=Time.realtimeSinceStartupAsDouble;}
                RenderPipeline.SubmitRenderRequest(fixture.View,request);if(combined!=null)Graphics.Blit(target,combined,uiComposite);var fence=AsyncGPUReadback.Request(combined!=null?combined:target,0,0,1,0,1,0,1);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("Terrain benchmark render fence failed");
                double now=Time.realtimeSinceStartupAsDouble,elapsed=now-start;frames++;allocated=Math.Max(allocated,Profiler.GetTotalAllocatedMemoryLong());reserved=Math.Max(reserved,Profiler.GetTotalReservedMemoryLong());
                if(elapsed>5){if(frameMs.Count==0)firstTick=fixture.SimulationTicks;frameMs.Add((now-previous)*1000);lastTick=fixture.SimulationTicks;}previous=now;
                if(elapsed<25)return;
                var ordered=frameMs.ToArray();Array.Sort(ordered);double Percentile(double p){double k=(ordered.Length-1)*p;int i=(int)k;return ordered[i]+(ordered[Math.Min(i+1,ordered.Length-1)]-ordered[i])*(k-i);}
                var stats=new uint[8];fixture.Cbt?.Statistics.GetData(stats);var stream=fixture.StreamedTerrain;
                if(stream!=null){stats[0]=(uint)(stream.GpuLeafCount*PlanetStreamedTerrain.Grid*PlanetStreamedTerrain.Grid*2);stats[1]=(uint)(stream.ResidentPatches*PlanetStreamedTerrain.Grid*PlanetStreamedTerrain.Grid*2);}
                var sim=simulationMs.ToArray();Array.Sort(sim);double SimPercentile(double p)=>sim.Length==0?0:sim[(int)((sim.Length-1)*p)];
                var report=new Report{rtsContent=fixture.Base!=null,buildings=fixture.Base?.Buildings.Count??0,cameraDistance=fixture.Rig.Distance,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),renderer=fixture.StreamedTerrain!=null?"sparse-patches":fixture.Cbt==null?"uniform":"implicit-CBT",scenario=scenario,width=2560,height=1440,units=fixture.unitCount,navResolution=fixture.navigationResolution,frames=frameMs.Count,seed=fixture.definition.terrainSeed,radius=fixture.definition.radius,errorPixels=fixture.terrainErrorPixels,medianMs=Percentile(.5),p95Ms=Percentile(.95),simulationMedianMs=SimPercentile(.5),simulationP95Ms=SimPercentile(.95),residentPatches=stream?.ResidentPatches??0,patchBudget=stream?.PatchBudget??0,deepestLevel=stream?.DeepestLevel??0,budgetLimited=stream?.BudgetLimited??false,simulationHz=(lastTick-firstTick)/(elapsed-5),peakAllocatedBytes=allocated,peakReservedBytes=reserved,terrainBufferBytes=fixture.StreamedTerrain?.BufferBytes??fixture.Cbt?.BufferBytes??0,activeTriangles=stats[0],visibleTriangles=stats[1],saturatedTriangles=stats[5],converged=stream!=null?!stream.BudgetLimited:fixture.Cbt?.Converged??true,memoryScope="Measured Unity tracked allocation/reservation, not total VRAM",limitation="Development terrain comparison; does not certify the million-unit planetary army or fog target. Timings include explicit rendering and one-pixel GPU completion."};
                File.WriteAllText(Path.Combine(output,"benchmark.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(output,"frames.csv"),"frame_ms\n"+string.Join("\n",frameMs.ConvertAll(x=>x.ToString("F6",CultureInfo.InvariantCulture))));File.WriteAllText(Path.Combine(output,"simulation.csv"),string.Join("\n",simulationMs.ConvertAll(x=>x.ToString("F6",CultureInfo.InvariantCulture))));Capture();Application.Quit();
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);}
        }
        void Capture(){var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var previousTarget=RenderTexture.active;RenderTexture.active=combined!=null?combined:target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previousTarget;File.WriteAllBytes(Path.Combine(output,"frame.png"),image.EncodeToPNG());Destroy(image);}
        void OnDestroy(){simulationFence?.Dispose();if(fixture!=null){fixture.SimulationBeginForValidation=null;fixture.SimulationEndForValidation=null;}foreach(var texture in new[]{target,uiTarget,combined})if(texture!=null){texture.Release();Destroy(texture);}if(uiComposite!=null)Destroy(uiComposite);}
    }
}

