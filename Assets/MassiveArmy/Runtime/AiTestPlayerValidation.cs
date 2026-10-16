using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed class AiTestPlayerValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status,error,snapshot;public string[] checks;public int[] buildings,decisions,techs,tiers;public uint zombies,visible;}
        readonly List<string> checks=new List<string>();string error="";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-ai-test-validation")>=0)new GameObject("AI test player fixture").AddComponent<AiTestPlayerValidation>();}
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||text.Contains("Skipping dispatch"))error=text;}
        IEnumerator Start(){
            Application.logMessageReceived+=Log;var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-benchmark-output");string folder=at>=0?args[at+1]:Application.persistentDataPath;Directory.CreateDirectory(folder);
            var app=FindAnyObjectByType<BattleApp>();app.settings.ambientZombies=100000;app.settings.enemyAI=false;app.settings.world.size=4096;app.settings.world.seed=app.settings.seed=73129;app.StartFlatValidationBattle(50000, spectator:true);
            double deadline=Time.realtimeSinceStartupAsDouble+150;while((app.State=="loading"||app.State=="battle"&&(app.Battle.AmbientZombieRemaining>0||app.Battle.ZombieAlive<100000))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            try{Check(app.State=="battle"&&app.Spectator&&!app.settings.swarmMode,"Separate AI test mode starts without survival waves");Check(app.Battle.ZombieAlive==100000,"Test mode retains the default 100K ambient zombies");Check(app.Base.Buildings.Any(b=>b.team==0&&b.definition.id=="command")&&app.Base.Buildings.Any(b=>b.team==1&&b.definition.id=="command"),"Both AI command bases exist");Check(!app.Battle.IssueOrder(app.View,Vector2.zero,ArmyOrder.Stop,Vector2.zero)&&!app.Battle.IssueCombinedOrder(app.View,Vector2.zero,ArmyOrder.Stop,Vector2.zero),"Spectator cannot issue ordinary or combined army orders");Check(app.View.transform.parent.GetComponentsInChildren<VolumetricFogOfWar>().Length==0&&FindObjectsByType<VolumetricFogOfWar>(FindObjectsSortMode.None).Length==0,"Spectator creates no volumetric fog-of-war pass");app.View.GetComponent<RtsCamera>().FullMap();}catch(Exception e){error=e.ToString();}
            float start=app.Base?.TimeElapsed??0;while(error.Length==0&&app.Base.TimeElapsed<start+35&&app.Base.Outcome.Length==0)yield return null;
            var report=new Report{buildings=app.Base.Buildings.Where(b=>!b.removed).GroupBy(b=>b.team).OrderBy(g=>g.Key).Select(g=>g.Count()).ToArray(),decisions=(int[])app.Base.AIDecisions.Clone(),techs=app.Base.Factions.Select(f=>f.technologies.Count).ToArray(),tiers=app.Base.Factions.Select(f=>f.tier).ToArray(),zombies=app.Battle.ZombieAlive};
            if(error.Length==0)try{Check(report.decisions.All(n=>n>=5)&&report.buildings.Length==2&&report.buildings.All(n=>n>=3),"Both AI controllers develop their bases at normal gameplay speed");Check(app.Battle.IsVisible(app.Base.BasePosition(1))&&app.Battle.IsVisible(new Vector2(1500,1500)),"Spectator visibility includes both bases and remote map areas");app.ExportBalanceSnapshot();report.snapshot=app.LastBalanceReport;File.Copy(report.snapshot,Path.Combine(folder,"balance-snapshot.json"),true);Check(File.Exists(report.snapshot),"Balance snapshot exports both faction economies, tech, armies and configuration");}catch(Exception e){error=e.ToString();}
            bool done=false;uint[] draws=null;app.Battle.ReadDrawArguments(d=>{draws=d;done=true;});while(!done)yield return null;
            if(error.Length==0)try{report.visible=draws[1]+draws[6]+draws[11];Check(report.visible>=90000,"Full-map view draws the zombie population without a visibility veil");Check(app.Battle.PlayerVisibility!=null&&app.Battle.PlayerVisibility.Any(v=>v==0),"AI scouting remains limited while spectator rendering is omniscient");app.CaptureGridPlacementPreview(Path.GetFullPath(Path.Combine(folder,"spectator-map.png")));}catch(Exception e){error=e.ToString();}
            app.StartFlatValidationBattle(50000);deadline=Time.realtimeSinceStartupAsDouble+150;while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(error.Length==0)try{Check(app.State=="battle"&&!app.Spectator&&!app.settings.swarmMode,"Repeated transition restores regular player RTS mode");Check(app.Base.AIDecisions[0]==0,"Regular mode leaves Cyan under player control");}catch(Exception e){error=e.ToString();}
            report.status=error.Length==0?"PASS":"FAIL";report.error=error;report.checks=checks.ToArray();File.WriteAllText(Path.Combine(folder,"player-validation.json"),JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;Application.Quit(error.Length==0?0:1);
        }
    }
}
