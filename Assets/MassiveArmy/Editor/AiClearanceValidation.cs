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
    public static class AiClearanceValidation
    {
        static IEnumerator routine;static GameObject root;static Battlefield field;static GpuBattle battle;static RtsWorld world;static Camera view;static string error="";static double deadline;
        static readonly List<string> checks=new List<string>();const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable]sealed class Report{public string status,error,gpu;public string[] checks;public int technologies;}
        static object Call(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,Private).Invoke(instance,args);
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks.Add(text);}
        public static void Batch(){checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+300;Application.logMessageReceived+=Log;EditorApplication.update+=Advance;}
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||text.Contains("Skipping dispatch"))error=text;}
        static void Step(){battle.AdvanceRtsValidation();battle.AdvanceSwarmValidation();battle.Update(view,BattleSettings.Tick,new uint[]{3,3,6,6},false);EditorApplication.QueuePlayerLoopUpdate();}
        static void Advance(){try{if(error.Length>0)throw new Exception(error);if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException();if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();}EditorApplication.update-=Advance;Application.logMessageReceived-=Log;
            Directory.CreateDirectory("TestResults/AiClearance");File.WriteAllText("TestResults/AiClearance/validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray(),technologies=world?.Factions[0].technologies.Count??0},true));world?.Dispose();battle?.Dispose();field?.Dispose();if(root)UnityEngine.Object.DestroyImmediate(root);EditorApplication.Exit(error.Length==0?0:1);}
        static IEnumerator Run(){
            root=new GameObject("AI clearance fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("Fixture camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.farClipPlane=8192;
            var cfg=new WorldGenerationSettings{size=4096,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,baseBuilding=true,spectatorMode=true,swarmMode=true,enemyAI=true,ambientZombies=0,capacity=128,activeUnits=128,movement=false,combat=false,targeting=false,crowdPressure=false,simulationLod=false};settings.ScaleWorld();
            var map=new WorldMap(settings.navigationResolution,4096,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);var cat=RtsCatalog.Load();battle=new GpuBattle(settings,field,cat.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,cat);world.EnemyEnabled=false;world.Nodes.Clear();
            var redHome=(RtsBuilding)Call(world,"Create",cat.Buildings["command"],1,settings.preview.Starts[1],-1,0);redHome.construction=redHome.definition.time;redHome.health=redHome.MaxHealth;Call(world,"Upload");
            for(int i=0;i<8;i++){Step();yield return null;}var home=world.Buildings.First(b=>b.team==0);home.level=5;world.Factions[0].tier=5;
            world.Factions[0].wallet.Stock=new ResourceCost{materials=10000,fuel=10000,rareElements=10000,isotopes=10000,research=10000};
            var p=field.Canonical(home.position+Vector2.right*176);var outpost=world.Construct("outpost",0,p,true);Check(outpost!=null,"Creates a normally paid expansion outpost");p=outpost.position;Call(world,"Remove",outpost,true);
            Check(world.AiClearing(0)&&world.AiLostSites(0)==1,"First combat loss initiates clearance and remembers the site");
            float before=world.Factions[0].wallet.Stock.materials;var plans=(Array)typeof(RtsWorld).GetField("aiPlans",Private).GetValue(world);var plan=plans.GetValue(0);
            for(int i=0;i<20;i++){Check(!(bool)Call(world,"ExpandAI",0,world.Buildings.Where(b=>!b.removed&&b.team==0).ToArray(),plan),"Expansion stays held on retry "+i);}
            Check(world.Factions[0].wallet.Stock.materials==before,"Repeated expansion attempts debit no materials");
            var blocked=(PlacementResult)Call(world,"AIPlacementAllowed",cat.Buildings["outpost"],0,p+Vector2.up*16,0);Check(!blocked.valid&&blocked.reason.Contains("Clear lost"),"Nearby offsets cannot bypass the failed-site exclusion");
            var units=new UnitState[128];int scout=cat.TypeIndex("scout");
            UnitState Unit(Vector2 at,uint team,int type)=>new UnitState{position=at,previous=at,destination=at,health=10000,packed=team|((uint)type<<8),flags=1|8|4|(team==2?262144u:0),target=uint.MaxValue,group=uint.MaxValue};
            for(int i=0;i<4;i++)units[i]=Unit(p+new Vector2(i*3,0),0,scout);for(int i=4;i<14;i++)units[i]=Unit(p+new Vector2(80+i*2,0),2,battle.ZombieType);units[14]=Unit(p+new Vector2(100,20),1,scout);
            battle.SetValidationUnits(units);uint sampleTick=0;AiAreaReport report=default;
            for(int i=0;i<120;i++){Step();yield return null;report=battle.GetAiAreaReport(0);if(report.valid&&report.hostiles==11&&report.combatants==4){sampleTick=report.tick;break;}}
            Check(report.valid&&report.hostiles==11&&report.combatants==4,"GPU area query counts ten visible zombies, one enemy and four armed friends");
            Call(world,"UpdateAiObjective",0,home,plan);var goal=world.AiObjective(0);battle.RequestAiAdvance(0,goal);Step();yield return null;
            bool done=false;UnitState[] sample=null;battle.ReadUnitSample(0,4,data=>{sample=data;done=true;});while(!done)yield return null;
            Check(sample.All(u=>u.group==3)&&field.Distance(battle.Navigation.GetOrder(3).destination,p)<32,"Spectator AI sends existing ground troops to clear the lost site");
            var lab=(RtsBuilding)Call(world,"Create",cat.Buildings["lab"],0,home.position-Vector2.right*80,-1,0);lab.construction=lab.definition.time;lab.health=lab.MaxHealth;
            for(int i=0;i<40;i++){world.Update(3);Call(world,"ThinkAI",0);Step();yield return null;}
            Check(world.AiClearing(0)&&!world.Buildings.Any(b=>!b.removed&&b.definition.id=="outpost"&&field.Distance(b.position,p)<160),"Danger keeps the outpost absent through repeated AI cycles");
            Check(world.Factions[0].technologies.Count>0||lab.queue.Any(q=>q.research),"Laboratory research progresses while expansion is held");
            Check(field.Distance(world.AiObjective(0),goal)<1,"Clearance target does not oscillate between AI cycles");
            // Unknown hostiles are not revealed by the spectator camera or a safety query.
            units=new UnitState[128];for(int i=0;i<4;i++)units[i]=Unit(p+Vector2.right*700+new Vector2(i*3,0),0,scout);for(int i=4;i<14;i++)units[i]=Unit(p+new Vector2(100+i,0),2,battle.ZombieType);
            battle.SetValidationUnits(units);battle.SetAiAreaQuery(0,p,200,true);for(int i=0;i<180;i++){Step();yield return null;report=battle.GetAiAreaReport(0);if(report.valid)break;}
            Call(world,"UpdateAiClearance",0,plan);Check(report.valid&&report.observed<report.cells*.9f&&world.AiClearing(0),"Incomplete scouting cannot approve rebuilding even if no hostile is reported ("+report.observed+"/"+report.cells+")");
            units=new UnitState[128];for(int i=0;i<4;i++)units[i]=Unit(p+new Vector2(i*3,0),0,scout);battle.SetValidationUnits(units);
            typeof(RtsWorld).GetField("matchTime",Private).SetValue(world,200f);
            battle.SetAiAreaQuery(0,p,200,true);for(int i=0;i<180;i++){Step();yield return null;report=battle.GetAiAreaReport(0);if(report.valid)break;}Call(world,"UpdateAiClearance",0,plan);
            Check(report.hostiles==0&&report.combatants==4&&report.observed>=report.cells*.9f&&world.AiClearing(0),"A single safe observation starts a hold rather than rebuilding immediately");
            typeof(RtsWorld).GetField("matchTime",Private).SetValue(world,214f);sampleTick=report.tick;for(int i=0;i<180;i++){Step();yield return null;report=battle.GetAiAreaReport(0);if(report.valid&&report.tick>sampleTick)break;}Call(world,"UpdateAiClearance",0,plan);
            Check(!world.AiClearing(0),"Fresh consecutive safe observations release the clearance operation");
            Check(!(bool)Call(world,"AiRiskBlocked",0,p),"Cleared site leaves the risk exclusion; normal placement rules still apply");
            // Regular Red uses reserved enemy group 1; player groups are untouched.
            settings.spectatorMode=false;var red=redHome;
            var redPost=(RtsBuilding)Call(world,"Create",cat.Buildings["outpost"],1,red.position+Vector2.right*176,-1,0);Call(world,"Remove",redPost,true);Check(world.AiClearing(1),"Regular Red combat loss triggers the same clearance policy");
            units=new UnitState[128];units[0]=Unit(red.position+Vector2.up*64,1,scout);units[1]=Unit(home.position+Vector2.up*64,0,scout);battle.SetValidationUnits(units);var human=battle.Navigation.GetOrder(2);battle.RequestAiAdvance(1,world.StructureSafeDestination(redPost.position,2,1));Step();yield return null;
            done=false;battle.ReadUnitSample(0,2,data=>{sample=data;done=true;});while(!done)yield return null;
            Check(sample[0].group==1&&sample[1].group==uint.MaxValue&&battle.Navigation.GetOrder(2).sequence==human.sequence,"Regular Red redirects existing troops through reserved group 1 without commanding the player");
            int lost=world.AiLostSites(1);var sold=(RtsBuilding)Call(world,"Create",cat.Buildings["outpost"],1,red.position-Vector2.right*176,-1,0);world.Sell(sold);Check(world.AiLostSites(1)==lost,"Voluntary sale does not record a combat loss");
            settings.spectatorMode=true;var again=(RtsBuilding)Call(world,"Create",cat.Buildings["outpost"],0,p+Vector2.up*16,-1,0);Call(world,"Remove",again,true);Check(world.AiClearing(0)&&world.AiLostSites(0)==1,"A later loss reactivates the remembered site rather than forgetting its history");
            Call(world,"UpdateAiObjective",0,home,plan);goal=world.AiObjective(0);var other=(RtsBuilding)Call(world,"Create",cat.Buildings["outpost"],0,p+Vector2.right*700,-1,0);Call(world,"Remove",other,true);Call(world,"UpdateAiObjective",0,home,plan);Check(world.AiLostSites(0)==2&&world.AiObjective(0)==goal,"A second dangerous site waits its turn instead of redirecting the clearing army");
            Check((float)Call(world,"AiExpansionReserve",0,world.Buildings.Where(b=>!b.removed&&b.team==0).ToArray())>=650,"Further outposts preserve research and strategic capital during clearance");
            var third=(RtsBuilding)Call(world,"Create",cat.Buildings["outpost"],0,p+Vector2.right*1400,-1,0);Call(world,"Remove",third,true);Check((bool)Call(world,"AiExpansionBacklogged",0),"Three unresolved loss sites pause further speculative outposts");
        }
    }
}
