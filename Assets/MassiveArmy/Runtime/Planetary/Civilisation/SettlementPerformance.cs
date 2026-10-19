using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Optional infrastructure timing only; never reads or mirrors GPU army records.
    internal static class SettlementPerformance {
        internal static readonly bool Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-economy-profile")>=0;
        [Serializable]sealed class Entry {public string name;public int count;public double totalMs,maximumMs;public List<double> samples=new List<double>();}
        [Serializable]sealed class Report {public List<Entry> entries=new List<Entry>();}
        static readonly Report report=new Report();static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        internal readonly struct Scope:IDisposable {readonly string name;readonly long start;internal Scope(string name){this.name=name;start=Enabled?Stopwatch.GetTimestamp():0;}public void Dispose(){if(!Enabled)return;double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;if(!entries.TryGetValue(name,out var entry)){entry=new Entry{name=name};entries.Add(name,entry);report.entries.Add(entry);}entry.count++;entry.totalMs+=ms;entry.maximumMs=Math.Max(entry.maximumMs,ms);entry.samples.Add(ms);}}
        internal static Scope Sample(string name)=>new Scope(name);
        internal static void Write(string output){if(Enabled)File.WriteAllText(Path.Combine(output,"economy-profile.json"),JsonUtility.ToJson(report,true));}
    }
}
