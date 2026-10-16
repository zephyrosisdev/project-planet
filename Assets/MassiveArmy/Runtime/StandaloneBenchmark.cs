using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy
{
    // Opt-in standalone harness. No effect in normal play or editor.
    [DefaultExecutionOrder(-100)]
    public sealed class StandaloneBenchmark : MonoBehaviour
    {
        BattleApp app;bool begun;RenderTexture target;RenderPipeline.StandardRequest request;int renderedFrames;string destination,scenario;double deadline;long peakAllocated,peakReserved;
        bool nuclear;int nuclearCount;double nextNuclear;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-army-benchmark")>=0)new GameObject("Standalone benchmark harness").AddComponent<StandaloneBenchmark>();}
        string Arg(string name,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        void Start()
        {
            destination=Arg("-benchmark-output",Application.persistentDataPath);Directory.CreateDirectory(destination);
            scenario=Arg("-benchmark-scenario","overview");app=FindAnyObjectByType<BattleApp>();
            nuclear=Array.IndexOf(Environment.GetCommandLineArgs(),"-benchmark-nuclear")>=0;
            app.Telemetry.MeasurementSeconds=int.Parse(Arg("-benchmark-seconds","20"));
            app.settings.shadows=Arg("-benchmark-shadows","off")=="on";
            app.settings.ambientZombies=int.Parse(Arg("-ambient-zombies","100000"));
            app.View.transform.parent.GetComponent<BattleRenderQuality>().Scale=1;
            if(Debug.isDebugBuild&&Array.IndexOf(Environment.GetCommandLineArgs(),"-benchmark-gpu-profile")>=0){Profiler.SetAreaEnabled(ProfilerArea.GPU,true);Profiler.enabled=true;}
            app.settings.world.size=int.Parse(Arg("-benchmark-world-size","4096"));
            target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf){name="Benchmark 1440p explicit HDRP target"};target.Create();
            app.View.enabled=false;app.View.targetTexture=target;request=new RenderPipeline.StandardRequest{destination=target};
            Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;deadline=Time.realtimeSinceStartupAsDouble+180;
            int count=int.Parse(Arg("-army-benchmark","50000"));
            if(scenario=="ai-test")app.StartFlatValidationBattle(count, spectator:true);else if(scenario.StartsWith("swarm"))app.StartFlatValidationBattle(count, swarm:true);else if(scenario=="base"||scenario=="base-close"||scenario=="base-seam")app.StartFlatValidationBattle(count);else app.StartBattle(count);
        }
        void LateUpdate()
        {
            if(app==null||app.State!="battle")return;
            if(!RenderPipeline.SupportsRenderRequest(app.View,request))throw new InvalidOperationException("HDRP explicit render requests are unavailable.");
            RenderPipeline.SubmitRenderRequest(app.View,request);
            // Occluded windows do not provide a reliable present fence. Complete one pixel
            // explicitly so timings cannot count queued submissions as rendered frames.
            var completion=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);completion.WaitForCompletion();if(completion.hasError)throw new InvalidOperationException("Benchmark render fence failed.");renderedFrames++;
        }
        void OnDestroy(){if(target!=null){target.Release();Destroy(target);}}
        void Update()
        {
            if(app.State=="battle"&&scenario.Contains("seam")){var rig=app.View.GetComponent<RtsCamera>();float half=app.settings.world.size*.5f;rig.FocusSector(new Vector2(half+Mathf.Sin(Time.realtimeSinceStartup)*200,half+Mathf.Cos(Time.realtimeSinceStartup)*200));}
            peakAllocated=Math.Max(peakAllocated,Profiler.GetTotalAllocatedMemoryLong());peakReserved=Math.Max(peakReserved,Profiler.GetTotalReservedMemoryLong());
            if(app.State=="battle"&&!begun){begun=true;var camera=app.View.GetComponent<RtsCamera>();
                if(app.Swarm!=null){foreach(var building in app.Base.Buildings)building.health=1e9f;app.Swarm.Schedule.Advance(630);}
                if(app.Swarm!=null&&scenario.Contains("heavy")){while(app.Swarm.Schedule.WaveSize<128000){app.Swarm.Schedule.ConfirmSpawn(app.Swarm.Schedule.Remaining);app.Swarm.Schedule.TryClear(0,false);app.Swarm.Schedule.Advance(630);}}
                if(scenario=="dense"){
                    var dense=Instantiate(Resources.Load<ComputeShader>("MassiveArmy/BenchmarkSetup"));
                    Vector2 centre=app.settings.preview.Starts[0];dense.SetInt("_Active",app.settings.activeUnits);dense.SetFloat("_WorldSize",app.Battle.Field.Size);
                    dense.SetVector("_Centre",new Vector4(centre.x,centre.y,0,0));dense.SetBuffer(0,"_Units",app.Battle.Units);dense.Dispatch(0,(app.settings.activeUnits+127)/128,1,1);Destroy(dense);camera.FocusSector(centre);
                }
                if(scenario=="ai-test")camera.FullMap();if(scenario.Contains("close"))camera.FocusBase(app.Base!=null?app.Base.BasePosition(0):app.settings.preview.Starts[0]);
                if(app.Base!=null&&Array.IndexOf(Environment.GetCommandLineArgs(),"-ai-area-probe")>=0)for(int team=0;team<2;team++)app.Battle.SetAiAreaQuery(team,app.Battle.Field.TraversableDestination(app.Base.BasePosition(team)+Vector2.right*880),200,true);
                app.BeginBenchmark(false);nextNuclear=Time.realtimeSinceStartupAsDouble+6;}
            if(begun&&nuclear&&nuclearCount<2&&Time.realtimeSinceStartupAsDouble>=nextNuclear){var home=app.Base.BasePosition(0);Vector2 impact=app.Battle.Field.Canonical(home+new Vector2(nuclearCount*50,150));app.View.GetComponent<RtsCamera>().FocusSector(impact);app.Battle.RequestTestNuke(impact);nuclearCount++;nextNuclear+=1;}
            if(begun&&renderedFrames>0&&!app.Telemetry.Benchmarking&&app.Telemetry.LastReport.EndsWith(".csv")){
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var active=RenderTexture.active;RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=active;File.WriteAllBytes(Path.Combine(destination,scenario+"-frame.png"),image.EncodeToPNG());Destroy(image);
                string source=app.Telemetry.LastReport;File.Copy(source,Path.Combine(destination,scenario+".csv"),true);File.Copy(Path.ChangeExtension(source,".json"),Path.Combine(destination,scenario+".json"),true);
                File.WriteAllText(Path.Combine(destination,scenario+"-memory.json"),"{\"peakAllocatedBytes\":"+peakAllocated+",\"peakReservedBytes\":"+peakReserved+",\"note\":\"Unity tracked allocation; not total dedicated VRAM\",\"renderedFrames\":"+renderedFrames+"}");
                app.WriteCombatBenchmarkEvidence(Path.Combine(destination,scenario+"-combat.json"));
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ai-area-probe")>=0)for(int team=0;team<2;team++)File.WriteAllText(Path.Combine(destination,"ai-area-"+team+".json"),JsonUtility.ToJson(app.Battle.GetAiAreaReport(team),true));
                Application.Quit();
            }
            if(Time.realtimeSinceStartupAsDouble>deadline){File.WriteAllText(Path.Combine(destination,scenario+"-error.txt"),"Benchmark timed out: "+app.State+" "+app.Loading);Application.Quit(2);}
        }
    }
}
