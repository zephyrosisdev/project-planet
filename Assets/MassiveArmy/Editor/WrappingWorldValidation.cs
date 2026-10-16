using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class WrappingWorldValidation
    {
        [Serializable] sealed class Report {public string status,error;public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Validate Wrapping Worlds")]
        public static void Run()
        {
            var report=new Report{status="PASS",error=""};
            void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);report.checks.Add(message);}
            try{
                Check(WorldTopology.Wrap(1024,2048)==-1024,"Half-world ties resolve to the negative image");
                Check(WorldTopology.Distance(new Vector2(1020,1020),new Vector2(-1020,-1020),2048)<12,"Shortest corner displacement wraps both axes");
                Check(WorldTopology.Index(-1,-1,256)==65535,"Negative cell indices wrap to the opposite corner");
                foreach(WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))if(preset!=WorldPreset.Custom){
                    var cfg=new WorldGenerationSettings{size=2048,seed=73129};cfg.ApplyPreset(preset);
                    var world=WorldSetupResult.Generate(cfg);var map=world.Map;
                    Check(!string.IsNullOrEmpty(TransportProgression.Validate(world,RtsCatalog.Load())),preset+": real catalogue transport progression");
                    Check(world.Starts.Length==2&&WorldTopology.Distance(world.Starts[0],world.Starts[1],map.Size)>=map.Size*.3f,preset+": wrapped start separation");
                    Check(world.Resources.Take(10).GroupBy(n=>n.kind).All(g=>g.Count()==2&&g.Select(n=>n.capacity).Distinct().Count()==1),preset+": paired progression deposits");
                    for(int i=0;i<16;i++){
                        Vector2 p=new Vector2(-map.Size*.5f,map.Size*(i/16f-.5f));Vector2 opposite=p+Vector2.right*map.Size;
                        Check(Mathf.Abs(map.SampleHeight(p)-map.SampleHeight(opposite))<.001f,preset+": periodic height sample "+i);
                        int a=map.Index(p),b=map.Index(opposite);map.SurfaceLayers(a,out var aa,out var ab);map.SurfaceLayers(b,out var ba,out var bb);
                        Check(aa==ba&&ab==bb&&map.WaterData(a)==map.WaterData(b)&&world.Deployment.Components[a]==world.Deployment.Components[b],preset+": water / material / component samples "+i);
                    }
                    var replay=WorldSetupResult.Generate(cfg);
                    Check(map.Height.SequenceEqual(replay.Map.Height)&&world.Starts.SequenceEqual(replay.Starts),preset+": deterministic settings / seed / version replay");
                }
                var invalid=new WorldGenerationSettings{oceanCoverage=0,lakeAbundance=0,riverAbundance=50};bool rejected=false;try{invalid.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"Rivers without water sinks are rejected");
                Check(FogWakeField.ResourceBytes<=64L*1024*1024,"Wake resource budget is bounded below 64 MiB");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/wrapping-world-validation.json",JsonUtility.ToJson(report,true));Debug.Log("Wrapping world validation "+report.status);
        }
    }
}
