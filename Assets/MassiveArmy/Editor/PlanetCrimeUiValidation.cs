using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.EditorTools {
    public static class PlanetCrimeUiValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate crime reports UI")]
        public static void Run(){var report=new Report();void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                var c=new ColonyState{name="Report fixture",population=120};c.crime=new CrimeState{reportedCrimes=1,arrests=1,searches=3};
                var named=new ResidentState{id=1,name="Reported suspect",alive=true,age=30,crime=new ResidentCrime{custody=CustodyStage.AwaitingCourt,facility=8}};
                var secret=new ResidentState{id=2,name="Unreported dealer",alive=true,age=40,crime=new ResidentCrime{activity=CrimeActivity.Dealer,gang=91}};c.residents.Add(named);c.residents.Add(secret);
                c.crime.cases.Add(new CrimeCase{id=10,suspect=1,victim=0,officer=0,reported=true,arrested=true,offence=Offence.Robbery,place=new Vector2(.2f,.4f)});
                c.crime.cases.Add(new CrimeCase{id=11,suspect=2,reported=false,offence=Offence.Dealing});
                c.crime.gangs.Add(new GangState{id=91,name="Hidden gang",leader=2,members=new List<long>{2}});c.crime.businesses.Add(new BlackMarketBusiness{id=92,owner=2,building=15});
                string before=JsonUtility.ToJson(c);var root=new VisualElement();Vector2 focused=default;string nextSection=null;int nextPage=-1;
                void Draw(bool observer,string section,int page=0){root.Clear();SettlementCrimeView.Build(root,c,observer,section,page,t=>"Day 1",(s,p)=>{nextSection=s;nextPage=p;},p=>focused=p);}
                Draw(false,"Incidents");Check(root.Q("crime-case-10")!=null&&root.Q("crime-case-11")==null,"Police reports show reported cases and hide undiscovered activity");
                Check(root.Q<Label>("crime-summary").text.Contains("open cases 1"),"Player summary does not count unreported cases");
                ((Action)root.Q<Button>("crime-locate-10").userData)();Check(focused==new Vector2(.2f,.4f),"Incident focus sends the saved event location, with no live unit lookup");
                ((Action)root.Q<Button>("crime-tab-Custody").userData)();Check(nextSection=="Custody"&&nextPage==0,"Custody tab navigation resets paging");
                Draw(false,"Custody");Check(root.Q("crime-custody-1")!=null,"Named custody record exposes awaiting-court status");
                Draw(false,"Gangs");Check(root.Q("crime-gang-91")==null&&root.Q("crime-empty")!=null,"Unknown gangs are hidden from police view");
                Draw(false,"Businesses");Check(root.Q("crime-business-92")==null,"Unreported black-market premises remain hidden");
                Draw(true,"Incidents");Check(root.Q("crime-case-11")!=null,"Spectator can inspect unreported incidents");
                Draw(true,"Gangs");Check(root.Q("crime-gang-91")!=null,"Spectator can inspect gang membership");
                Draw(true,"Businesses");Check(root.Q("crime-business-92")!=null,"Spectator can inspect illicit businesses");
                root.Clear();SettlementCrimeView.Resident(root,c,secret,false);Check(root.Q<Label>("resident-crime-2").text=="No reported offences"&&root.Query<Label>().ToList().Count==1,"Normal resident profile does not label a hidden dealer as a known criminal");
                root.Clear();SettlementCrimeView.Resident(root,c,secret,true);Check(root.Query<Label>().ToList().Exists(l=>l.text.Contains("Observer: Dealer")),"Spectator resident profile includes underlying activity");
                Check(JsonUtility.ToJson(c)==before,"Inspecting reports and navigating never alters simulation records");
                for(int i=0;i<40;i++)c.crime.cases.Add(new CrimeCase{id=100+i,reported=true});Draw(false,"Incidents",999);Check(root.Q<Label>("crime-page").text.Contains("page 4 / 4"),"Case pagination clamps stale page state");Check(!root.Q<Button>("crime-next").enabledSelf,"Last page disables next navigation");
                Check(root.Query<Button>().ToList().FindAll(b=>b.name.StartsWith("crime-locate-")).Count<=SettlementCrimeView.PageSize,"Case rendering creates at most twelve incident rows");
                Check(SettlementCrimeView.Cause(DeathCause.Murder)=="Homicide"&&SettlementCrimeView.Cause(DeathCause.Robbery)=="Robbery"&&SettlementCrimeView.Cause(DeathCause.PoliceViolence)=="Police violence","Crime deaths no longer display as zombie attacks");
                Check(SettlementCrimeView.Activity(CrimeActivity.ExploitedSexWorker)=="Victim of exploitation","Exploitation victims are described as victims");
                var empty=new ColonyState{name="Fresh settlement"};root.Clear();SettlementCrimeView.Build(root,empty,false,"Incidents",0,t=>"",(s,p)=>{},p=>{});Check(root.Q("crime-empty")!=null,"Fresh settlements explain the empty crime report");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/Planetary/Crime/UI");File.WriteAllText("TestResults/Planetary/Crime/UI/editor.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Crime UI PASS: "+report.checks.Count+" checks");
        }
    }
}
