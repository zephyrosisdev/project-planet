using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    // Read-only replay of public celestial definitions from a captured weather report.
    public static class PlanetWeatherCaptureAnalysis {
        [Serializable] sealed class Clock {public double simulationSeconds;}
        [Serializable] sealed class Row {public int frame,star,second;public double seconds,ratio,light;public Vector3 sun;}
        [Serializable] sealed class Report {public double fittedTime,positionError,minimumPowerRatio;public int sourceChanges;public List<Row> frames=new List<Row>();}
        public static void Run(){
            var args=Environment.GetCommandLineArgs();string Argument(string key)=>args[Array.IndexOf(args,key)+1];string folder=Argument("-weather-capture-folder");
            var history=JsonUtility.FromJson<PlanetWeatherRenderer.WeatherHistory>(File.ReadAllText(Argument("-weather-capture-trace")));
            var serializer=new XmlSerializer(typeof(SurvivalUniverseState),new XmlRootAttribute("universe"));SurvivalUniverseState state;using(var reader=File.OpenRead(Path.Combine(folder,"public-universe.xml")))state=(SurvivalUniverseState)serializer.Deserialize(reader);
            var type=typeof(PlanetDevelopment).Assembly.GetType("MassiveArmy.Networking.SurvivalUniverse");const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            object universe=type.GetMethod("Restore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{state,state.worldId,state.seed});var target=ScriptableObject.CreateInstance<PlanetDefinition>();
            try{
                JsonUtility.FromJsonOverwrite(history.definition,target);var galaxy=(GalaxyModel)type.GetField("Galaxy",flags).GetValue(universe);var bodies=(PlanetDefinition[])type.GetField("Bodies",flags).GetValue(universe);var body=Array.Find(bodies,b=>b.bodyId==target.bodyId);var advance=type.GetMethod("Advance",flags);
                double Error(double time){advance.Invoke(universe,new object[]{time});return body.position.RelativeTo(target.position).Magnitude;}
                var clock=JsonUtility.FromJson<Clock>(File.ReadAllText(Path.Combine(folder,"public-clock.json")));double bestTime=0,best=double.MaxValue;
                for(double time=0;time<=clock.simulationSeconds+100;time+=1){double error=Error(time);if(error<best){best=error;bestTime=time;}}
                double low=Math.Max(0,bestTime-1),high=bestTime+1;for(int i=0;i<60;i++){double a=low+(high-low)/3,b=high-(high-low)/3;if(Error(a)<Error(b))high=b;else low=a;}
                bestTime=(low+high)*.5;var report=new Report{fittedTime=bestTime,positionError=Error(bestTime),minimumPowerRatio=double.MaxValue};int previous=-1;double end=history.frames[history.frames.Length-1].seconds;
                foreach(var sample in history.frames){double time=Math.Max(0,bestTime+(sample.seconds-end)*8);advance.Invoke(universe,new object[]{time});var observer=body.position.Add(sample.bodyEye).RelativeTo(galaxy.Origin);int first=-1,second=-1;double power=0,next=0;
                    for(int i=0;i<galaxy.Nodes.Length;i++)if(galaxy.Nodes[i].kind==CelestialKind.Star){var delta=galaxy.Current[i].position-observer;double p=galaxy.Nodes[i].mu/Math.Max(1,delta.Magnitude*delta.Magnitude);if(p>power){next=power;second=first;power=p;first=i;}else if(p>next){next=p;second=i;}}
                    double ratio=power/Math.Max(1e-30,next);if(previous>=0&&previous!=first)report.sourceChanges++;previous=first;report.minimumPowerRatio=Math.Min(report.minimumPowerRatio,ratio);
                    report.frames.Add(new Row{frame=sample.frame,seconds=sample.seconds,star=first,second=second,ratio=ratio,light=sample.terrainWeatherLight,sun=(galaxy.Current[first].position-observer).Float.normalized});
                }
                File.WriteAllText(Path.Combine(folder,"sun-selection.json"),JsonUtility.ToJson(report,true));Debug.Log("Weather capture sun replay: error="+report.positionError+", changes="+report.sourceChanges+", minimum ratio="+report.minimumPowerRatio);
            }finally{((IDisposable)universe).Dispose();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
