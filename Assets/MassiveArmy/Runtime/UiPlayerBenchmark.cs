using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MassiveArmy
{
    // Presented-player companion to the explicit off-screen GPU benchmark. Opt-in only.
    public sealed class UiPlayerBenchmark : MonoBehaviour
    {
        BattleApp app;
        string folder, scenario;
        bool begun, captured;
        double deadline;
        long allocated, reserved;
        static string Arg(string key,string fallback)
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);
            return i>=0&&i+1<args.Length?args[i+1]:fallback;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-benchmark")>=0)
                new GameObject("Presented UI benchmark").AddComponent<UiPlayerBenchmark>();
        }
        void Start()
        {
            app=FindAnyObjectByType<BattleApp>();folder=Arg("-benchmark-output",Application.persistentDataPath);
            Directory.CreateDirectory(folder);scenario=Arg("-benchmark-scenario","overview");
            app.settings.world.seed=app.settings.seed=int.Parse(Arg("-benchmark-seed","73129"));
            app.settings.world.size=int.Parse(Arg("-benchmark-world-size","4096"));
            app.View.transform.parent.GetComponent<BattleRenderQuality>().Scale=1;
            Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-benchmark-gpu-profile")>=0){Profiler.SetAreaEnabled(ProfilerArea.GPU,true);Profiler.enabled=true;}
            int count=int.Parse(Arg("-ui-benchmark","50000"));
            if(scenario.StartsWith("base"))app.StartFlatValidationBattle(count);else app.StartBattle(count);
            deadline=Time.realtimeSinceStartupAsDouble+240;
        }
        void Update()
        {
            allocated=Math.Max(allocated,Profiler.GetTotalAllocatedMemoryLong());
            reserved=Math.Max(reserved,Profiler.GetTotalReservedMemoryLong());
            if(app.State=="battle"&&!begun)
            {
                begun=true;
                if(scenario=="base")app.View.GetComponent<RtsCamera>().ResetOverview();
                if(scenario.Contains("close")||scenario.Contains("queue")||scenario.Contains("research"))app.View.GetComponent<RtsCamera>().FocusBase(app.Base.BasePosition(0));
                if(scenario.Contains("queue")||scenario.Contains("research"))
                {
                    var producer=UiBenchmarkFixture.Prepare(app.Base,out var lab);app.SelectBuildingForUiBenchmark(scenario.Contains("research")?lab:producer);
                }
                app.BeginBenchmark(false);UiFogTelemetry.Begin(app);
            }
            if(begun&&!captured&&app.Battle.Tick>180)
            {
                captured=true;ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(folder,"presented.png")));
            }
            if(begun&&!app.Telemetry.Benchmarking&&app.Telemetry.LastReport.EndsWith(".csv"))
            {
                var source=app.Telemetry.LastReport;
                File.Copy(source,Path.Combine(folder,"frames.csv"),true);
                File.Copy(Path.ChangeExtension(source,".json"),Path.Combine(folder,"settings.json"),true);
                File.WriteAllText(Path.Combine(folder,"memory.json"),$"{{\"peakAllocatedBytes\":{allocated},\"peakReservedBytes\":{reserved},\"note\":\"Unity tracked memory, not total dedicated VRAM\"}}");
                UiFogTelemetry.Finish(folder);Application.Quit();
            }
            if(Time.realtimeSinceStartupAsDouble>deadline)
            {
                File.WriteAllText(Path.Combine(folder,"error.txt"),app.State+" "+app.Loading);Application.Quit(2);
            }
        }
    }
}
