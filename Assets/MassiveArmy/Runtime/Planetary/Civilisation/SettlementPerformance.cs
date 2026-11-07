using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Optional infrastructure timing only; never reads or mirrors GPU army records.
    internal static class SettlementPerformance {
        internal static readonly bool Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-economy-profile")>=0;
        [Serializable]sealed class Entry {public string name;public int count;public double totalMs,maximumMs;public long allocatedBytes,maximumAllocatedBytes;public int sampleCursor;public List<double> samples=new List<double>();}
        [Serializable]sealed class Report {public List<Entry> entries=new List<Entry>();}
        static readonly Report report=new Report();static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        internal readonly struct Scope:IDisposable {readonly string name;readonly long start,allocated;internal Scope(string name){this.name=name;UnityEngine.Profiling.Profiler.BeginSample(name);start=Enabled?Stopwatch.GetTimestamp():0;allocated=Enabled?GC.GetAllocatedBytesForCurrentThread():0;}public void Dispose(){UnityEngine.Profiling.Profiler.EndSample();if(!Enabled)return;double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;if(!entries.TryGetValue(name,out var entry)){entry=new Entry{name=name};entries.Add(name,entry);report.entries.Add(entry);}entry.count++;entry.totalMs+=ms;entry.maximumMs=Math.Max(entry.maximumMs,ms);entry.allocatedBytes+=bytes;entry.maximumAllocatedBytes=Math.Max(entry.maximumAllocatedBytes,bytes);if(entry.samples.Count<4096)entry.samples.Add(ms);else {entry.samples[entry.sampleCursor]=ms;entry.sampleCursor=(entry.sampleCursor+1)%4096;}}}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){entries.Clear();report.entries.Clear();}
        internal static Scope Sample(string name)=>new Scope(name);
        internal static void Write(string output){if(Enabled)File.WriteAllText(Path.Combine(output,"economy-profile.json"),JsonUtility.ToJson(report,true));}
    }
}
