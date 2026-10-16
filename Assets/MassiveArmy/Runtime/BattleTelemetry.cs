using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace MassiveArmy
{
    public sealed class BattleTelemetry
    {
        public static readonly string[] Markers={"Army.SpatialGrid","Army.Navigation","Army.CrowdPressure","Army.MovementAvoidance","Army.ContactGrid","Army.ContactResolution","Army.TargetAcquisition","Army.Combat","Army.Projectiles","Army.Statistics","Army.Visibility","Army.ForestVisibility","Army.Rendering","Army.Fog","Army.SwarmRoutes","Army.SwarmSpawnAndOrders","Army.ZombieHearing"};
        readonly Dictionary<string,Recorder> recorders=new Dictionary<string,Recorder>();
        readonly FrameTiming[] timings=new FrameTiming[1];
        readonly List<string> rows=new List<string>();
        double benchmarkStart;
        Vector3 benchmarkCameraPosition,benchmarkCameraAngles;
        int benchmarkWidth,benchmarkHeight;
        float benchmarkFov,benchmarkOrthoSize;bool benchmarkOrthographic;
        public float Fps {get;private set;}
        public double CpuMs {get;private set;}
        public double GpuMs {get;private set;}
        public double EngineGpuMs {get;private set;}
        public bool Benchmarking {get;private set;}
        public float MeasurementSeconds {get;set;}=20;
        public string LastReport {get;private set;}="";
        public void Update(GpuBattle battle)
        {
            Fps=Mathf.Lerp(Fps,1/Mathf.Max(Time.unscaledDeltaTime,.0001f),.06f);
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)>0) { CpuMs=timings[0].cpuFrameTime;EngineGpuMs=timings[0].gpuFrameTime; }
            foreach(string marker in Markers) if(!recorders.ContainsKey(marker))
            {
                var recorder=GpuSamples.Get(marker).GetRecorder();if(recorder.isValid) { recorder.enabled=true;recorders.Add(marker,recorder); }
            }
            GpuMs=0;foreach(string marker in Markers)if(marker!="Army.Fog")GpuMs+=GpuMilliseconds(marker); // Fog is nested inside the full camera rendering scope.
            if(!Benchmarking || battle==null)return;
            double elapsed=Time.realtimeSinceStartupAsDouble-benchmarkStart;
            if(elapsed>5)
            {
                var s=battle.Statistics;var row=new StringBuilder();
                row.Append(elapsed.ToString("F4",CultureInfo.InvariantCulture)).Append(',').Append(Time.unscaledDeltaTime.ToString("F6",CultureInfo.InvariantCulture));
                row.Append(',').Append(CpuMs.ToString("F4",CultureInfo.InvariantCulture)).Append(',').Append(GpuMs.ToString("F4",CultureInfo.InvariantCulture));
                row.Append(',').Append(EngineGpuMs.ToString("F4",CultureInfo.InvariantCulture));
                row.Append(',').Append(battle.Tick).Append(',').Append(s==null?0:s[0]+s[1]+s[112]).Append(',').Append(s==null?0:s[8]+s[9]+s[10]);
                foreach(string marker in Markers)row.Append(',').Append(GpuMilliseconds(marker).ToString("F4",CultureInfo.InvariantCulture));
                rows.Add(row.ToString());
            }
            if(elapsed>=5+Mathf.Clamp(MeasurementSeconds,20,120))Finish(battle);
        }
        public double GpuMilliseconds(string name) => recorders.TryGetValue(name,out var r) && r.gpuSampleBlockCount>0 ? r.gpuElapsedNanoseconds/1000000.0 : 0;
        public static string TimeLabel(double value) => value>0?$"{value:F2} ms":"unavailable";
        public void Begin(Camera view)
        {
            benchmarkCameraPosition=view.transform.position;benchmarkCameraAngles=view.transform.eulerAngles;
            benchmarkWidth=view.pixelWidth;benchmarkHeight=view.pixelHeight;benchmarkFov=view.fieldOfView;benchmarkOrthoSize=view.orthographicSize;benchmarkOrthographic=view.orthographic;
            rows.Clear();benchmarkStart=Time.realtimeSinceStartupAsDouble;Benchmarking=true;LastReport=$"Warming up 5 s, then recording {Mathf.Clamp(MeasurementSeconds,20,120)} s";
        }
        public void Finish(GpuBattle battle)
        {
            if(!Benchmarking)return;Benchmarking=false;
            string folder=Path.Combine(Application.persistentDataPath,"Benchmarks");Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,$"army-{battle.Settings.activeUnits}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
            string header="elapsed_s,frame_s,cpu_ms,gpu_ms,engine_gpu_ms,tick,alive,visible,"+string.Join(",",Markers)+"\n";
            File.WriteAllText(path,header+string.Join("\n",rows));
            File.WriteAllText(Path.ChangeExtension(path,".json"),JsonUtility.ToJson(new BenchmarkInfo{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion,os=SystemInfo.operatingSystem,memoryMiB=SystemInfo.graphicsMemorySize,estimatedBytes=MemoryBudget.Bytes(battle.Settings),settings=battle.Settings,frames=rows.Count,width=benchmarkWidth,height=benchmarkHeight,cameraPosition=benchmarkCameraPosition,cameraAngles=benchmarkCameraAngles,cameraFov=benchmarkFov,orthographic=benchmarkOrthographic,orthographicHalfHeight=benchmarkOrthoSize,targetFrameRate=Application.targetFrameRate},true));
            LastReport=path;Debug.Log("Benchmark saved: "+path);
        }
        [Serializable] sealed class BenchmarkInfo { public string gpu,api,unity,os;public int memoryMiB,frames,width,height,targetFrameRate;public bool orthographic;public float orthographicHalfHeight,cameraFov;public Vector3 cameraPosition,cameraAngles;public long estimatedBytes;public BattleSettings settings; }
    }
}
