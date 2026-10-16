using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class AiAccessValidation
    {
        static IEnumerator routine;static GameObject root;static Battlefield field;static GpuBattle battle;static RtsWorld world;static Camera view;static string error="";static double deadline;
        static readonly List<string> checks=new List<string>();const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        [Serializable]sealed class Report{public string status,error;public string[] checks;public string fortification;}
        static object Call(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,Private).Invoke(instance,args);
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        public static void Batch(){error="";checks.Clear();routine=Run();deadline=EditorApplication.timeSinceStartup+300;Application.logMessageReceived+=Log;EditorApplication.update+=Advance;}
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||text.Contains("Skipping dispatch"))error=text;}
        static void Step(){battle.AdvanceRtsValidation();battle.Update(view,BattleSettings.Tick,new uint[]{3,3,6,6},false);EditorApplication.QueuePlayerLoopUpdate();}
        static void Advance(){try{if(error.Length>0)throw new Exception(error);if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException();if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();}EditorApplication.update-=Advance;Application.logMessageReceived-=Log;
            Directory.CreateDirectory("TestResults/AiAccess");File.WriteAllText("TestResults/AiAccess/validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,checks=checks.ToArray(),fortification=world?.AiFortificationStatus[0]},true));world?.Dispose();battle?.Dispose();field?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(error.Length==0?0:1);}
        static IEnumerator Run(){
            root=new GameObject("AI access fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("Fixture camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.farClipPlane=8192;
            var cfg=new WorldGenerationSettings{size=4096,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,baseBuilding=true,spectatorMode=true,enemyAI=false,ambientZombies=0,capacity=128,activeUnits=128,combat=false,targeting=false,crowdPressure=false,simulationLod=false};settings.ScaleWorld();
            var map=new WorldMap(settings.navigationResolution,4096,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);var cat=RtsCatalog.Load();battle=new GpuBattle(settings,field,cat.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,cat);world.EnemyEnabled=false;world.Nodes.Clear();
            for(int i=0;i<10;i++){Step();yield return null;}
            var home=world.Buildings.First(b=>b.team==0);home.level=3;var producer=(RtsBuilding)Call(world,"Create",cat.Buildings["barracks"],0,home.position+new Vector2(80,0),-1,0);producer.construction=producer.definition.time;producer.health=producer.MaxHealth;
            var factory=(RtsBuilding)Call(world,"Create",cat.Buildings["factory"],0,home.position+new Vector2(0,72),-1,0);factory.construction=factory.definition.time;factory.health=factory.MaxHealth;
            world.Factions[0].tier=5;foreach(var tech in cat.technologies)world.Factions[0].technologies.Add(tech.id);world.Factions[0].wallet.Stock=new ResourceCost{materials=100000,fuel=100000,rareElements=100000,isotopes=100000,research=100000};
            var plans=(Array)typeof(RtsWorld).GetField("aiPlans",Private).GetValue(world);var plan=plans.GetValue(0);plan.GetType().GetField("cycle").SetValue(plan,1);
            Call(world,"UpdateAiObjective",0,home,plan);var objective=world.AiObjective(0);typeof(RtsWorld).GetField("matchTime",Private).SetValue(world,300f);
            for(int cycle=2;cycle<20;cycle++){plan.GetType().GetField("cycle").SetValue(plan,cycle);Call(world,"UpdateAiObjective",0,home,plan);Check(world.AiObjective(0)==objective,"Unfinished objective persists through AI cycle "+cycle);}
            plan.GetType().GetField("cycle").SetValue(plan,1);
            Check((bool)Call(world,"PlanAiCompound",0,home,world.Buildings.Where(b=>!b.removed&&b.team==0).ToArray(),plan),"Plans a complete compound around buildings that intersect the old 48m ring");
            Vector2 exit=producer.position+Vector2.up*(producer.definition.footprintDepth*4+cat.Units["infantry"].radius+12);
            Check(!(bool)Call(world,"AiPlacementClear",cat.Buildings["wall"],0,world.SnapPlacement(cat.Buildings["wall"],exit),0),"Rejects a wall at the production exit");
            var queue=(Queue<(string id,Vector2 position,int rotation)>)plan.GetType().GetField("defence").GetValue(plan);var perimeter=queue.ToArray();
            Call(world,"BeginObstacleBatch");try{foreach(var item in perimeter){var b=world.Construct(item.id,0,item.position,true,item.rotation);Check(b!=null,"Constructs reserved "+item.id+" "+item.position);b.construction=b.definition.time;b.health=b.MaxHealth;}}finally{Call(world,"EndObstacleBatch");}
            world.Update(.25f);Check(world.Buildings.Count(b=>b.team==0&&b.definition.id=="gate"&&b.gateTraversable)==4,"Four completed owner gates are traversable");
            var north=perimeter.First(i=>i.id=="gate"&&i.rotation==0&&field.Delta(home.position,i.position).y>0).position;var destination=world.StructureSafeDestination(north+Vector2.up*96,1,0);
            var units=new UnitState[128];int scout=cat.TypeIndex("scout");for(int i=0;i<16;i++){var p=world.StructureSafeDestination(exit+new Vector2((i%4)*3,(i/4)*3),1,0);units[i]=new UnitState{position=p,previous=p,destination=p,health=10000,packed=(uint)scout<<8,flags=1|8,target=uint.MaxValue,group=uint.MaxValue};}
            battle.SetValidationUnits(units);battle.RequestFactionAdvance(0,destination);Step();yield return null;uint sequence=battle.Navigation.GetOrder(3).sequence;
            for(int tick=0;tick<700;tick++){if(tick%120==0)battle.RequestFactionAdvance(0,destination);Step();yield return null;}
            Check(battle.Navigation.GetOrder(3).sequence==sequence,"Replenishment refreshes preserve the shared order sequence");
            bool done=false;UnitState[] sample=null;battle.ReadUnitSample(0,16,data=>{sample=data;done=true;});while(!done)yield return null;
            int escaped=sample.Count(u=>field.Delta(home.position,u.position).y>field.Delta(home.position,north).y+8);Check(escaped>=12,"At least 75% of troops exit the compound through owner gates ("+escaped+"/16)");
            Check(sample.All(u=>world.BuildingAtCell(BuildingGrid.Cells(cat.Buildings["wall"],u.position,field.Size).First())==null||world.BuildingAtCell(BuildingGrid.Cells(cat.Buildings["wall"],u.position,field.Size).First()).definition.id=="gate"),"Troops finish outside solid building and wall footprints");
            units[0]=sample[0];units[0].position=units[0].previous=units[0].destination;units[0].flags|=64;units[0].group=uint.MaxValue; // ARRIVED
            battle.SetValidationUnits(units);battle.RequestFactionAdvance(0,destination);Step();yield return null;done=false;battle.ReadUnitSample(0,1,data=>{sample=data;done=true;});while(!done)yield return null;
            Check(sample[0].destination==units[0].destination,"Refreshing an order does not send arrived troops to a new slot");
            // Normal RTS retains individual Red rally orders; its aggregate arrival
            // counters must not depend on spectator shared-group assignment.
            settings.spectatorMode=false;settings.enemyAI=true;var redHome=world.Buildings.First(b=>b.team==1);var redStart=world.StructureSafeDestination(redHome.position+Vector2.up*80,1,1);
            units=new UnitState[128];for(int i=0;i<16;i++){var p=redStart+new Vector2(i%4*3,i/4*3);units[i]=new UnitState{position=p,previous=p,destination=field.Canonical(p+Vector2.right*500),health=10000,packed=1u|((uint)scout<<8),flags=1|8,target=uint.MaxValue,group=uint.MaxValue};}
            battle.SetValidationUnits(units);uint[] counters=null;done=false;battle.RequestStatistics(data=>{counters=data;done=true;});while(!done)yield return null;
            // Query the frozen fixture before simulating; accelerated Editor updates
            // can otherwise complete the entire march between normal 5Hz readbacks.
            Check(counters[117]==16&&counters[115]==0,"Regular Red rallies count travelling troops without shared spectator groups ("+counters[117]+" / "+counters[115]+")");
            typeof(GpuBattle).GetProperty("Statistics").SetValue(battle,counters);
            var redPlan=plans.GetValue(1);Call(world,"UpdateAiObjective",1,redHome,redPlan);var redObjective=world.AiObjective(1);
            for(int cycle=1;cycle<8;cycle++){redPlan.GetType().GetField("cycle").SetValue(redPlan,cycle);Call(world,"UpdateAiObjective",1,redHome,redPlan);Check(world.AiObjective(1)==redObjective,"Regular Red rally objective persists while troops travel: "+cycle);}
            for(int i=0;i<16;i++){units[i].destination=units[i].position;units[i].flags|=64;}
            battle.SetValidationUnits(units);done=false;battle.RequestStatistics(data=>{counters=data;done=true;});while(!done)yield return null;
            Check(counters[115]==16,"Regular Red reports arrival from its existing individual orders");typeof(GpuBattle).GetProperty("Statistics").SetValue(battle,counters);
            typeof(RtsWorld).GetField("matchTime",Private).SetValue(world,400f);redPlan.GetType().GetField("cycle").SetValue(redPlan,30);Call(world,"UpdateAiObjective",1,redHome,redPlan);
            Check(world.AiObjective(1)!=redObjective,"Regular Red chooses a new exploration objective after arrival and the hold period");
        }
    }
}
