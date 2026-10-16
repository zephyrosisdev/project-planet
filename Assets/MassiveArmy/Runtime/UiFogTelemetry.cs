using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MassiveArmy
{
    // Extra opt-in measurement, including intel/readback/minimap work outside the fog draw pass.
    public sealed class UiFogTelemetry : MonoBehaviour
    {
        public static readonly bool Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-benchmark-fog-total")>=0;
        public static readonly CustomSampler Intel=CustomSampler.Create("Front.FogIntel",true);
        static readonly CustomSampler DrawCpu=CustomSampler.Create("Front.FogCpu");
        static readonly CustomSampler Readback=CustomSampler.Create("Front.FogReadback"),Minimap=CustomSampler.Create("Front.MinimapFog");
        static readonly List<string> rows=new List<string>();
        static BattleApp app;static double started;
        static Recorder intelRecorder,readbackRecorder,minimapRecorder,drawCpuRecorder;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){if(Enabled)new GameObject("Total fog telemetry").AddComponent<UiFogTelemetry>();}
        public readonly struct CpuScope : IDisposable
        {
            readonly CustomSampler sampler;
            public CpuScope(CustomSampler sampler){this.sampler=Enabled?sampler:null;this.sampler?.Begin();}
            public void Dispose(){sampler?.End();}
        }
        public static CpuScope DrawScope()=>new CpuScope(DrawCpu);
        public static CpuScope ReadbackScope()=>new CpuScope(Readback);
        public static CpuScope MinimapScope()=>new CpuScope(Minimap);
        public static void Begin(BattleApp battleApp)
        {
            if(!Enabled)return;app=battleApp;rows.Clear();started=Time.realtimeSinceStartupAsDouble;
            intelRecorder=Intel.GetRecorder();readbackRecorder=Readback.GetRecorder();minimapRecorder=Minimap.GetRecorder();drawCpuRecorder=DrawCpu.GetRecorder();drawCpuRecorder.enabled=intelRecorder.enabled=readbackRecorder.enabled=minimapRecorder.enabled=true;
        }
        void LateUpdate()
        {
            if(app==null||!app.Telemetry.Benchmarking||Time.realtimeSinceStartupAsDouble-started<=5)return;
            double drawCpu=drawCpuRecorder.elapsedNanoseconds/1e6;double draw=app.Telemetry.GpuMilliseconds("Army.Fog"),intel=intelRecorder.gpuSampleBlockCount>0?intelRecorder.gpuElapsedNanoseconds/1e6:0,readback=readbackRecorder.elapsedNanoseconds/1e6,minimap=minimapRecorder.elapsedNanoseconds/1e6;
            rows.Add(string.Join(",",new[]{Time.realtimeSinceStartupAsDouble-started,draw,intel,readback,minimap,drawCpu,draw+intel+readback+minimap+drawCpu}.SelectInvariant()));
        }
        public static void Finish(string folder)
        {
            if(!Enabled)return;File.WriteAllText(Path.Combine(folder,"total-fog.csv"),"elapsed_s,draw_gpu_ms,intel_gpu_ms,readback_cpu_ms,minimap_cpu_ms,draw_cpu_ms,total_ms\n"+string.Join("\n",rows));
        }
    }
    static class UiFogNumbers
    {
        public static IEnumerable<string> SelectInvariant(this IEnumerable<double> values){foreach(double value in values)yield return value.ToString("F6",CultureInfo.InvariantCulture);}
    }
}
