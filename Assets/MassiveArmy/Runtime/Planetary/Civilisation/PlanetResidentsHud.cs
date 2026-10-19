using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        bool residentsView;int residentPage;
        void OpenResidents(){orbitalsView=false;residentsView=true;resourcesView=false;if(civilisationOpen)civilisationOpen=false;ToggleCivilisation();}
        void HouseholdContext(VisualElement parent,RtsBuilding building){
            var colony=world.Settlements?[building.team];if(colony==null||building.definition.housingCapacity<=0)return;
            var h=colony.households.Find(x=>x.home==building.persistentId);if(h==null){Label(parent,"VACANT FAMILY HOME · up to six residents","body");return;}
            Label(parent,h.surname+" household · "+h.tenure,"subheading");var owner=CivilisationRuntime.Person(colony,h.owner);Label(parent,"Owner: "+(h.tenure==HomeTenure.SocialHousing?"Local council":owner?.name??"Unassigned"),"muted");
            foreach(long id in h.members){var p=CivilisationRuntime.Person(colony,id);if(p!=null&&p.alive)Label(parent,$"{p.name}, {(int)p.age} · {p.relationship} · {(p.age<18?"Child / education":p.role.ToString())}","body");}
        }
        void RefreshResidents(CivilisationRuntime simulation,float scroll){
            var colony=simulation.Colony(simulation.BodyId(fixture.definition.bodyId),inspectedFaction);if(colony==null){Label(civilisationContent,"No settlement on this world","body");return;}
            Label(civilisationContent,$"{colony.name.ToUpperInvariant()} · LOCAL TIME {simulation.LocalHour:00.0}h","heading");
            Label(civilisationContent,$"{colony.population} residents · {colony.households.Count} households · {colony.employed}/{colony.workforce} working-age residents employed","body");
            Label(civilisationContent,"COUNCIL & LOCAL ENTERPRISE","subheading");var council=colony.council;
            Label(civilisationContent,$"Treasury {council.treasury:0.00} credits · wage tax {council.taxRate:P0} · taxes collected {council.taxesCollected:0.00}\nEducation {council.educationSpent:0.00} · welfare {council.welfareSpent:0.00} · projects {council.infrastructureSpent:0.00}\nPriority: {council.priority} · {council.decision}","body").name="council-summary";
            if(fixture.sessionMode!=PlanetGameMode.AiTest&&inspectedFaction==0){var row=new VisualElement();row.AddToClassList("row");civilisationContent.Add(row);foreach(CivicPriority choice in Enum.GetValues(typeof(CivicPriority))){var priority=choice;Button(row,"FUND "+choice.ToString().ToUpperInvariant(),()=>{council.priority=priority;council.decision="Resident mandate: "+priority;nextCivilisationRefresh=0;});}}
            Label(civilisationContent,"One household per home. School age 5–17; adults qualify through education and experience. Households select an adult shopper; any parent can take that role. Homes share residential streets with nearby shops. Shifts have staggered starts; errands follow work, school ends in the afternoon, and some residents visit the council before returning home.","muted");
            var pages=new VisualElement();pages.AddToClassList("row");civilisationContent.Add(pages);Button(pages,"PREVIOUS",()=>{residentPage=Math.Max(0,residentPage-1);nextCivilisationRefresh=0;});Button(pages,"NEXT",()=>{residentPage=Math.Min(Math.Max(0,(colony.residents.Count-1)/20),residentPage+1);nextCivilisationRefresh=0;});
            int start=Math.Min(residentPage*20,Math.Max(0,colony.residents.Count-1)),end=Math.Min(start+20,colony.residents.Count);
            for(int i=start;i<end;i++){var p=colony.residents[i];if(!p.alive)continue;var h=CivilisationRuntime.Family(colony,p.household);var job=CivilisationRuntime.SocietyBuilding(world,p.workplace);
                Label(civilisationContent,$"{p.name} · age {(int)p.age} · {(p.age<18?"Child":p.role.ToString())} · {simulation.Routine(colony,p,simulation.SimulationTime)}","subheading").name="resident-"+p.id;
                Label(civilisationContent,$"{h?.surname} family · {h?.tenure} · {(h?.home>0?"Home #"+h.home:"Awaiting a home")}\n{p.relationship} · {p.background} · education {p.education:P0} · reputation {p.reputation:P0}\n{(p.age<18?"School / dependent":job?.definition.name??"Available for work")} · wage {p.wage:0.00}/day · last pay {p.lastPay:0.00} · savings {p.savings:0.00}","body");
                if(job!=null)Label(civilisationContent,$"Shift {simulation.WorkStart(p):00.0}–{simulation.WorkStart(p)+8:00.0}h · walks via connected roads","muted");
            }
            Label(civilisationContent,"LOCAL BUSINESSES","subheading");foreach(var e in colony.enterprises)Label(civilisationContent,$"{e.name} · cash {e.balance:0.00} · payroll {e.wagesPaid:0.00} · sales {e.sales:0.00}","muted");
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
    }
}
