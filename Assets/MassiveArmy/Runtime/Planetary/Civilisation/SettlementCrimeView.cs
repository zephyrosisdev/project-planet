using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    // Reads saved society records only; never requests GPU unit positions or health.
    public static class SettlementCrimeView {
        public const int PageSize=12;
        public static bool Visible(CrimeCase record,bool observer)=>observer||record.reported;
        public static bool Known(ColonyState c,long person,bool observer)=>observer||c.crime!=null&&c.crime.cases.Exists(q=>q.reported&&q.suspect==person);
        public static string Cause(DeathCause cause){switch(cause){case DeathCause.OldAge:return "Old age";case DeathCause.Murder:return "Homicide";case DeathCause.Robbery:return "Robbery";case DeathCause.PoliceViolence:return "Police violence";default:return "Zombie attack";}}
        public static string Activity(CrimeActivity activity){switch(activity){case CrimeActivity.None:return "No recorded activity";case CrimeActivity.IndependentSexWorker:return "Independent adult sex work";case CrimeActivity.ExploitedSexWorker:return "Victim of exploitation";case CrimeActivity.Exploiter:return "Exploitation";case CrimeActivity.Begging:return "Begging";default:return activity.ToString();}}
        public static string Custody(CustodyStage stage){switch(stage){case CustodyStage.AwaitingCourt:return "Awaiting court";case CustodyStage.AwaitingPrison:return "Awaiting prison transfer";case CustodyStage.FederalPrison:return "Federal prison";case CustodyStage.CountyJail:return "County jail";default:return "Not in custody";}}
        static string Person(ColonyState c,long id)=>id==0?"Unknown":CivilisationRuntime.Person(c,id)?.name??"Resident #"+id;
        static Label Text(VisualElement parent,string text,string style="body",string name=null){var label=new Label(text){name=name};label.AddToClassList(style);parent.Add(label);return label;}
        static Button Action(VisualElement parent,string text,Action callback,string name){var button=new Button(callback){text=text,name=name,userData=callback};button.AddToClassList("action");parent.Add(button);return button;}
        public static void Resident(VisualElement parent,ColonyState c,ResidentState p,bool observer){
            var x=p.crime;if(x==null)return;var records=c.crime?.cases;int reported=records?.FindAll(q=>q.reported&&q.suspect==p.id).Count??0;
            Text(parent,reported==0?"No reported offences":$"Reported cases: {reported} · convictions: {x.convictions}","muted","resident-crime-"+p.id);
            if(x.custody!=CustodyStage.Free)Text(parent,Custody(x.custody)+" · facility #"+x.facility,"body");
            if(x.wanted>0&&reported>0)Text(parent,"Wanted for questioning · allegations require a court hearing","body");
            if(!observer)return;
            Text(parent,$"Observer: {Activity(x.activity)} · hardship {x.hardship:P0} · alcohol dependency {x.alcoholAddiction:P0} · drug dependency {x.drugAddiction:P0}","muted");
            Text(parent,$"Firearms owned {x.weapons} · ammunition issued/purchased {x.ammoPurchased} (not remaining rounds) · contraband {x.contraband:0.0}","muted");
            var gang=c.crime?.gangs.Find(g=>g.id==x.gang);if(gang!=null)Text(parent,"Gang: "+gang.name,"body");
            int limit=Math.Min(6,x.ties.Count);for(int i=0;i<limit;i++){var tie=x.ties[i];Text(parent,tie.kind+": "+Person(c,tie.resident)+" · "+tie.strength.ToString("P0"),"muted");}
        }
        public static void Build(VisualElement parent,ColonyState c,bool observer,string section,int page,Func<double,string> date,Action<string,int> navigate,Action<Vector2> locate){
            Text(parent,c.name.ToUpperInvariant()+" · CRIME & SECURITY","heading","crime-title");var state=c.crime??new CrimeState();
            int open=0,total=0,held=0;foreach(var q in state.cases)if(Visible(q,observer)){total++;if(!q.closed)open++;}foreach(var p in c.residents)if(p.alive&&CivilisationRuntime.Confined(p))held++;
            Text(parent,$"Reported crimes {state.reportedCrimes} · open cases {open} · arrests {state.arrests}\nPolice searches {state.searches} · in custody {held} · bodies discovered {state.discoveries}","body","crime-summary");
            Text(parent,observer?"OBSERVER · includes unreported activity, marked below":"POLICE RECORDS · unreported crimes and undiscovered bodies remain hidden. Allegations are not convictions.","muted");
            var tabs=new VisualElement();tabs.AddToClassList("row");parent.Add(tabs);foreach(string tab in new[]{"Incidents","Custody","Gangs","Businesses"}){string selected=tab;Action(tabs,tab.ToUpperInvariant(),()=>navigate(selected,0),"crime-tab-"+tab);}
            var entries=new List<Action>();
            if(section=="Custody")foreach(var p in c.residents){if(!p.alive||!CivilisationRuntime.Confined(p))continue;var resident=p;entries.Add(()=>{Text(parent,resident.name+" · "+Custody(resident.crime.custody),"subheading","crime-custody-"+resident.id);Text(parent,"Facility #"+resident.crime.facility+(resident.crime.releaseAt>0?" · sentence ends "+date(resident.crime.releaseAt):" · court hearing pending"),"body");});}
            else if(section=="Gangs")foreach(var g in state.gangs){int known=0;foreach(long member in g.members)if(Known(c,member,observer))known++;if(known==0)continue;var gang=g;int count=known;entries.Add(()=>{Text(parent,gang.name,"subheading","crime-gang-"+gang.id);Text(parent,observer?$"Leader {Person(c,gang.leader)} · {count} members":$"{count} members named in police reports · alleged association","body");foreach(long member in gang.members)if(Known(c,member,observer))Text(parent,Person(c,member),"muted");});}
            else if(section=="Businesses")foreach(var b in state.businesses){if(!Known(c,b.owner,observer))continue;var business=b;entries.Add(()=>{Text(parent,"Premises #"+business.building+(business.shutDown?" · SHUT DOWN":" · "+(observer?"ACTIVE":"SUSPECTED ACTIVITY")),"subheading","crime-business-"+business.id);Text(parent,"Operator: "+Person(c,business.owner)+" · "+(business.owned?"Owned premises":"Rented premises"),"body");if(observer)Text(parent,$"Production {business.produced:0.0} · sales {business.sales:0.00} credits","muted");});}
            else for(int i=state.cases.Count-1;i>=0;i--){var record=state.cases[i];if(!Visible(record,observer))continue;entries.Add(()=>{Text(parent,$"#{record.id} · {record.offence} · {date(record.time)}"+(record.reported?"":" · UNREPORTED"),"subheading","crime-case-"+record.id);Text(parent,$"Suspect: {Person(c,record.suspect)} · victim: {Person(c,record.victim)}\nOfficer: {Person(c,record.officer)} · "+(record.heard?record.convicted?"Convicted":"Acquitted":record.arrested?"Arrested · awaiting hearing":record.closed?"Closed":"Under investigation"),"body");if(!string.IsNullOrEmpty(record.outcome))Text(parent,record.outcome,"muted");Action(parent,"SHOW INCIDENT LOCATION",()=>locate(record.place),"crime-locate-"+record.id);});}
            int pages=Math.Max(1,(entries.Count+PageSize-1)/PageSize);page=Math.Clamp(page,0,pages-1);Text(parent,section+" · page "+(page+1)+" / "+pages+" · "+entries.Count+" entries","subheading","crime-page");
            var controls=new VisualElement();controls.AddToClassList("row");parent.Add(controls);int current=page;Action(controls,"PREVIOUS",()=>navigate(section,current-1),"crime-previous").SetEnabled(page>0);Action(controls,"NEXT",()=>navigate(section,current+1),"crime-next").SetEnabled(page+1<pages);
            if(entries.Count==0)Text(parent,"No "+section.ToLowerInvariant()+" recorded yet. Fresh settlements may have no crime; activity develops over simulated days as the population grows.","body","crime-empty");
            for(int i=page*PageSize;i<Math.Min(entries.Count,(page+1)*PageSize);i++)entries[i]();
        }
    }
}
