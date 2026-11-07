using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    /// <summary>Exports existing captured frames; never restarts or mutates a game.</summary>
    public static class OvernightProfileCapture {
        const string Root="TestResults/OvernightProfile";
        [Serializable]sealed class Sample {public int frame;public string thread,name,path;public double totalMs,selfMs;public int children;}
        [Serializable]sealed class Frame {public int frame;public string thread;public double timeMs,gpuMs;public int samples;public List<Sample> expensive=new List<Sample>();}
        [Serializable]sealed class Report {public int first,last;public string error="";public List<Frame> frames=new List<Frame>();}
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Root+"/request.txt"))return;string request=File.ReadAllText(Root+"/request.txt").Trim();File.Delete(Root+"/request.txt");if(request=="validate")ValidateChanges();else Capture();}
        static void ValidateChanges(){
            var checks=new List<string>();
            try{
                var method=typeof(MassiveArmy.Planetary.CubeSphere).GetMethod("TryFormationRight",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
                foreach(float radius in new[]{120000f,12000000f,120000000f})for(int face=0;face<6;face++){
                    var start=MassiveArmy.Planetary.CubeSphere.Direction((MassiveArmy.Planetary.CubeFace)face,Vector2.zero);
                    var end=(start+MassiveArmy.Planetary.CubeSphere.Right[face]*(4/radius)).normalized;
                    object[] args={start,end,Vector3.zero};if(!(bool)method.Invoke(null,args))throw new Exception("Four-metre formation drag rejected at radius "+radius);
                    var right=(Vector3)args[2];if(Math.Abs(right.magnitude-1)>1e-5||Math.Abs(Vector3.Dot(right,(start+end).normalized))>1e-5)throw new Exception("Invalid tangent");
                    var encoded=MassiveArmy.Planetary.CubeSphere.Encode(right);if(float.IsNaN(encoded.x))throw new Exception("Invalid encoded tangent");
                    args=new object[]{start,start,Vector3.zero};if((bool)method.Invoke(null,args))throw new Exception("Click accepted as formation");
                }
                checks.Add("PASS: 4m formation drags and ordinary clicks across six cube faces at 1x, 100x and 1000x radii");
                var catalogue=MassiveArmy.RtsCatalog.Load();MassiveArmy.Planetary.CivilisationCatalogue.Load().Extend(catalogue);var lists=(string[][])typeof(MassiveArmy.Planetary.SettlementDirector).GetField("DistrictBuildings",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
                foreach(var list in lists)foreach(string id in list)if(!catalogue.Buildings.ContainsKey(id))throw new Exception("Unknown planner building: "+id);
                checks.Add("PASS: every district growth choice resolves in the real building catalogue");
                File.WriteAllText(Root+"/validation.txt",string.Join("\n",checks));
            }catch(Exception e){File.WriteAllText(Root+"/validation.txt",string.Join("\n",checks)+"\nFAIL: "+e);}
        }
        [MenuItem("Synthetic Front/Diagnostics/Export captured overnight profile")]
        public static void Capture(){
            Directory.CreateDirectory(Root);var report=new Report{first=ProfilerDriver.firstFrameIndex,last=ProfilerDriver.lastFrameIndex};
            try{
                if(report.first<0||report.last<0)throw new InvalidOperationException("Profiler contains no captured frames");
                ProfilerDriver.SaveProfile(Path.GetFullPath(Root+"/overnight.raw"));
                for(int frame=Math.Max(report.first,report.last-99);frame<=report.last;frame++)for(int thread=0;thread<128;thread++){
                    using var data=ProfilerDriver.GetRawFrameDataView(frame,thread);if(!data.valid)break;
                    if(data.threadName!="Main Thread"&&data.threadName!="Render Thread"&&data.threadName.IndexOf("GPU",StringComparison.OrdinalIgnoreCase)<0)continue;
                    var record=new Frame{frame=frame,thread=data.threadName,timeMs=data.frameTimeMs,gpuMs=data.frameGpuTimeMs,samples=data.sampleCount};var samples=new List<Sample>();var parents=new Stack<(int end,string path)>();
                    for(int i=0;i<data.sampleCount;i++){
                        while(parents.Count>0&&i>parents.Peek().end)parents.Pop();string name=data.GetSampleName(i),path=parents.Count==0?name:parents.Peek().path+"/"+name;double total=data.GetSampleTimeMs(i),self=total;int children=data.GetSampleChildrenCount(i),recursive=data.GetSampleChildrenCountRecursive(i),next=i+1;
                        for(int child=0;child<children;child++){self-=data.GetSampleTimeMs(next);next+=1+data.GetSampleChildrenCountRecursive(next);}
                        samples.Add(new Sample{frame=frame,thread=data.threadName,name=name,path=path,totalMs=total,selfMs=Math.Max(0,self),children=children});
                        if(recursive>0)parents.Push((i+recursive,path));
                    }
                    record.expensive=samples.OrderByDescending(s=>s.selfMs).Take(100).ToList();report.frames.Add(record);
                }
            }catch(Exception e){report.error=e.ToString();}
            File.WriteAllText(Root+"/profile-summary.json",JsonUtility.ToJson(report,true));
        }
    }
}

