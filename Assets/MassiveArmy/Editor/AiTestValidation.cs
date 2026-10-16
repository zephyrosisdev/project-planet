using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class AiTestValidation
    {
        static IEnumerator routine;static GameObject root;static GpuBattle battle;static RtsWorld world;static Battlefield field;static Camera view;static string error="";static double deadline;
        static readonly List<string> checks=new List<string>();
        [Serializable]sealed class Report{public string status,error;public string[] checks;public int[] decisions,buildings,techs,tiers,outposts,walls,gates,extractors;}
        public static void Batch(){checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+600;Application.logMessageReceived+=Log;EditorApplication.update+=Advance;}
        static void Log(string text,string stack,LogType type){if(text.Contains("Skipping dispatch")||text.Contains("Compute shader")&&text.Contains("not set")||text.Contains("Shader error"))error=text;}
        static void Check(bool valid,string text){if(!valid)throw new Exception(text);checks.Add(text);}
        static void Step(float dt){world.Update(dt);battle.AdvanceRtsValidation();battle.Update(view,BattleSettings.Tick,new uint[]{3,3,6,6},false);EditorApplication.QueuePlayerLoopUpdate();}
        static void Advance(){try{if(error.Length>0)throw new Exception(error);if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("AI fixture timeout");if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();Debug.LogException(e);}EditorApplication.update-=Advance;Application.logMessageReceived-=Log;
            var report=new Report{status=error.Length==0?"PASS":"FAIL",error=error,checks=checks.ToArray(),decisions=world?.AIDecisions,buildings=Enumerable.Range(0,2).Select(t=>world?.Buildings.Count(b=>!b.removed&&b.team==t)??0).ToArray(),techs=world?.Factions.Select(f=>f.technologies.Count).ToArray(),tiers=world?.Factions.Select(f=>f.tier).ToArray(),outposts=Count("outpost"),walls=Count("wall"),gates=Count("gate"),extractors=Enumerable.Range(0,2).Select(t=>world?.Buildings.Count(b=>!b.removed&&b.team==t&&b.node>=0)??0).ToArray()};
            Directory.CreateDirectory("TestResults/AiTest");File.WriteAllText("TestResults/AiTest/planner-validation.json",JsonUtility.ToJson(report,true));world?.Dispose();battle?.Dispose();field?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(error.Length==0?0:1);}
        static int[] Count(string id)=>Enumerable.Range(0,2).Select(t=>world?.Buildings.Count(b=>!b.removed&&b.team==t&&b.definition.id==id)??0).ToArray();
        static IEnumerator Run(){
            root=new GameObject("AI planner fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("AI fixture view").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.farClipPlane=8192;
            var cfg=new WorldGenerationSettings{size=2048,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,baseBuilding=true,spectatorMode=true,ambientZombies=0,capacity=4096,activeUnits=4096,enemyAI=true,simulationLod=false,combat=false};settings.ScaleWorld();
            var map=new WorldMap(settings.navigationResolution,2048,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);var rig=view.gameObject.AddComponent<RtsCamera>();rig.Field=field;rig.Spectator=true;rig.FullMap();
            var catalogue=RtsCatalog.Load();battle=new GpuBattle(settings,field,catalogue.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,catalogue);
            // Accelerated infrastructure fixture: normal wallets/costs and node depletion,
            // but ignore combat damage so it measures planner progression, not match balance.
            battle.OnStructureDamage=amounts=>{};
            for(int i=0;i<1600;i++){Step(.5f);if(i%80==0){Directory.CreateDirectory("TestResults/AiTest");File.WriteAllText("TestResults/AiTest/planner-progress.txt",world.TimeElapsed+" seconds · "+string.Join(" / ",world.AIActivity)+"\n"+string.Join(" / ",world.AiFortificationStatus));}yield return null;}
            for(int t=0;t<2;t++){
                int team=t;var owned=world.Buildings.Where(b=>!b.removed&&b.team==team).ToArray();Check(world.AIDecisions[t]>100,"Both factions run their own AI cycles: "+t);Check(world.Factions[t].technologies.Count>0&&world.Factions[t].tier>=2,"AI researches and upgrades its command tier: "+t);
                Check(new[]{"mine","barracks","lab","factory","autocannon","wall","gate","outpost"}.All(id=>owned.Any(b=>b.definition.id==id)),"AI develops industry, defences and expansion bases: "+t);
                Check(owned.Count(b=>b.node>=0)>1,"AI claims additional resource nodes: "+t);
            }
            var old=world.Buildings.Where(b=>!b.removed&&b.node>=0&&world.Nodes[b.node].resource==ResourceKind.Materials).Take(2).ToArray();foreach(var b in old)world.Nodes[b.node].remaining=0;
            for(int i=0;i<160;i++){Step(.5f);yield return null;}Check(old.All(b=>b.removed),"AI retires exhausted extractors instead of remaining locked to depleted nodes");
            Check(battle.IsVisible(new Vector2(500,500))&&!battle.IssueOrder(view,Vector2.zero,ArmyOrder.Stop,Vector2.zero),"Spectator sees the map and cannot issue player army commands");
            bool done=false;UnitState[] sample=null;battle.ReadUnitSample(0,8,d=>{sample=d;done=true;});while(!done)yield return null;
            Check(sample.Any(u=>(u.packed&255)==0&&u.group>=2&&u.group<7)&&sample.Any(u=>(u.packed&255)==1&&u.group>=7&&u.group<12),"Both armies receive movement-class-specific shared GPU orders");
            settings.spectatorMode=false;Check(!battle.IsVisible(new Vector2(500,500))||battle.IsKnown(new Vector2(500,500)),"Returning to regular mode removes the spectator visibility override");
        }
    }
}
