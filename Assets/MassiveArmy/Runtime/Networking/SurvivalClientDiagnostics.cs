using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal sealed class SurvivalClientDiagnostics {
        readonly Dictionary<string,List<double>> stages=new Dictionary<string,List<double>>();
        internal long Begin()=>Stopwatch.GetTimestamp();
        internal void End(string phase,long start){if(!stages.TryGetValue(phase,out var list))stages[phase]=list=new List<double>(4096);if(list.Count<8192)list.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);}
        internal void Frame(double seconds){if(!stages.TryGetValue("Frame",out var list))stages["Frame"]=list=new List<double>(4096);if(list.Count<8192)list.Add(seconds*1000);}
        internal void Reset()=>stages.Clear();
        [Serializable] internal sealed class Timing{public string phase;public int samples;public double medianMs,p95Ms;}
        [Serializable] internal sealed class Report {public string scope="One explicit 2560x1440 HDRP render per frame with GPU completion fence, UI/transport work and co-hosted dedicated server plus a headless peer. Functional comparison; not release FPS/capacity acceptance.";public int retainedShipSnapshots,replayedSnapshots;public bool baseline;public ulong rttMs;public long managedBytes,workingSetBytes,privateBytes;public List<Timing> stages=new List<Timing>();}
        internal Report Capture(bool baseline,ulong rtt){var result=new Report{baseline=baseline,rttMs=rtt,managedBytes=GC.GetTotalMemory(false)};using(var process=Process.GetCurrentProcess()){result.workingSetBytes=process.WorkingSet64;result.privateBytes=process.PrivateMemorySize64;}foreach(var pair in stages){var values=pair.Value.ToArray();Array.Sort(values);if(values.Length>0)result.stages.Add(new Timing{phase=pair.Key,samples=values.Length,medianMs=values[values.Length/2],p95Ms=values[Math.Min(values.Length-1,(int)Math.Ceiling(values.Length*.95)-1)]});}return result;}
    }
    public sealed partial class SurvivalNetworkSession {
        readonly SurvivalClientDiagnostics clientDiagnostics=new SurvivalClientDiagnostics();
        int retainedShipSnapshots,replayedSnapshots;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
        static readonly bool performanceArgument=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-performance")>=0;
#endif
        internal bool ClientDiagnosticsEnabled {
            get {
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
                return Probe!=null&&performanceArgument;
#else
                return false;
#endif
            }
        }
        internal void RecordClientFrame(double seconds)=>clientDiagnostics.Frame(seconds);
        internal void ResetClientDiagnostics(){clientDiagnostics.Reset();retainedShipSnapshots=replayedSnapshots=0;}
        internal SurvivalClientDiagnostics.Report ClientDiagnosticReport(){var report=clientDiagnostics.Capture(ClientBaseline,transport?.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId)??0);report.retainedShipSnapshots=retainedShipSnapshots;report.replayedSnapshots=replayedSnapshots;return report;}
    }
}
