using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace MassiveArmy.Networking {
    internal sealed class SurvivalNetworkMetrics {
        internal enum Phase {Players, Craft, Industry, Death, Snapshot, JournalSerialize, JournalFlush, Checkpoint}
        readonly List<double> tickMs=new List<double>(4096);
        readonly List<double>[] phases=new List<double>[Enum.GetValues(typeof(Phase)).Length];
        readonly Stopwatch elapsed=Stopwatch.StartNew();long ticks,sent,received,playerTotal,window,firstServerTick=-1,lastServerTick=-1;int minimumPlayers=int.MaxValue,maximumPlayers;
        internal SurvivalNetworkMetrics(){for(int i=0;i<phases.Length;i++)phases[i]=new List<double>(4096);}
        public long Begin()=>Stopwatch.GetTimestamp();
        public void End(long started,int players,long serverTick=-1){ticks++;if(firstServerTick<0)firstServerTick=serverTick;lastServerTick=serverTick;if(tickMs.Count<4096)tickMs.Add(Milliseconds(started));minimumPlayers=Math.Min(minimumPlayers,players);maximumPlayers=Math.Max(maximumPlayers,players);playerTotal+=players;}
        public void Measure(Phase phase,long started)=>Record(phase,Milliseconds(started));
        public void Record(Phase phase,double milliseconds){var samples=phases[(int)phase];if(samples.Count<4096)samples.Add(milliseconds);}
        public void Sent(int bytes){sent+=bytes;}public void Received(int bytes){received+=bytes;}
        public void Reset(){tickMs.Clear();foreach(var samples in phases)samples.Clear();ticks=sent=received=playerTotal=0;firstServerTick=lastServerTick=-1;minimumPlayers=int.MaxValue;maximumPlayers=0;elapsed.Restart();}
        static double Milliseconds(long started)=>(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        [Serializable] sealed class Timing {public string name;public int samples;public double medianMs,p95Ms,maximumMs,totalMs;}
        [Serializable] sealed class Report {
            public string scope="Server CPU tick and stage diagnostics. Journal/checkpoint stages may include admission/disconnect work outside ticks; nested timings must not be summed. Not client frame-time, army/fog or capacity acceptance";
            public long window,firstServerTick,lastServerTick;public int connectedPlayers,minimumPlayers,maximumPlayers,samples;public double meanPlayers,elapsedSeconds,simulationTicksPerSecond,tickMedianMs,tickP95Ms,sentBytesPerSecond,receivedBytesPerSecond;
            public Timing[] phases;public bool processMemoryAvailable;public string processMemoryStatus;
            public long managedBytes,unityAllocatedBytes,graphicsDriverAllocatedBytes,processPrivateBytes,processWorkingSetBytes;
        }
        public void Write(string path,int clients){
            var sorted=tickMs.ToArray();Array.Sort(sorted);double seconds=Math.Max(.001,elapsed.Elapsed.TotalSeconds);
            var report=new Report{window=++window,firstServerTick=firstServerTick,lastServerTick=lastServerTick,connectedPlayers=clients,minimumPlayers=ticks==0?clients:minimumPlayers,maximumPlayers=maximumPlayers,meanPlayers=ticks==0?clients:playerTotal/(double)ticks,samples=sorted.Length,elapsedSeconds=seconds,simulationTicksPerSecond=ticks/seconds,tickMedianMs=Percentile(sorted,.5),tickP95Ms=Percentile(sorted,.95),sentBytesPerSecond=sent/seconds,receivedBytesPerSecond=received/seconds,managedBytes=GC.GetTotalMemory(false),unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong(),graphicsDriverAllocatedBytes=Profiler.GetAllocatedMemoryForGraphicsDriver(),phases=new Timing[phases.Length]};
            for(int i=0;i<phases.Length;i++){var values=phases[i].ToArray();Array.Sort(values);double total=0;foreach(double value in values)total+=value;report.phases[i]=new Timing{name=((Phase)i).ToString(),samples=values.Length,medianMs=Percentile(values,.5),p95Ms=Percentile(values,.95),maximumMs=values.Length==0?0:values[values.Length-1],totalMs=total};}
            try{using(var process=Process.GetCurrentProcess()){report.processPrivateBytes=process.PrivateMemorySize64;report.processWorkingSetBytes=process.WorkingSet64;}report.processMemoryAvailable=report.processPrivateBytes>0&&report.processWorkingSetBytes>0;report.processMemoryStatus=report.processMemoryAvailable?"Measured":"Runtime returned zero; use runner Windows measurements";}catch(Exception e){report.processMemoryStatus="Unavailable: "+e.GetType().Name;}
            string json=JsonUtility.ToJson(report,true);File.WriteAllText(path,json);
            // Eight bounded historical windows preserve occupied steady intervals after players depart.
            File.WriteAllText(Path.ChangeExtension(path,null)+".window-"+(window%8)+".json",json);Reset();
        }
        static double Percentile(double[] data,double q)=>data.Length==0?0:data[Math.Min(data.Length-1,(int)Math.Ceiling(data.Length*q)-1)];
    }
}
