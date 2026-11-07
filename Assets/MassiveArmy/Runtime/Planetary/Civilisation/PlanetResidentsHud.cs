using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        bool residentsView;
        void OpenResidents(){crimeView=false;orbitalsView=false;residentsView=true;resourcesView=false;if(civilisationOpen)civilisationOpen=false;ToggleCivilisation();}
        void ArchiveLookup(VisualElement parent,CivilisationRuntime simulation,ColonyState colony){
            var row=new VisualElement();row.AddToClassList("row");parent.Add(row);
            var id=new TextField("History ID");row.Add(id);var kind=new DropdownField(new System.Collections.Generic.List<string>{"Person","Death","Case"},0);row.Add(kind);
            var result=Label(parent,"Look up older records by their permanent ID.","muted");
            Button(row,"LOOK UP",async ()=>{
                if(!long.TryParse(id.value,out long value)||value<=0){result.text="Enter a positive record ID";return;}
                int type=kind.index;result.text="Loading history…";
                try{
                    string json=type==0?await simulation.ReadArchivedResident(colony.id,value):await simulation.ReadArchivedHistory(colony.id,value,type==1);
                    if(json==null){result.text="No archived record with that ID";return;}
                    if(type==0){var p=JsonUtility.FromJson<ResidentState>(json);result.text=p.name+" · deceased · household #"+p.household;}
                    else if(type==1){var d=JsonUtility.FromJson<DeathRecord>(json);result.text=d.name+" · "+SettlementCrimeView.Cause(d.cause)+" · "+simulation.CivicDate(d.time);}
                    else {var q=JsonUtility.FromJson<CrimeCase>(json);if(!SettlementCrimeView.Visible(q,Observer)){result.text="No public record with that ID";return;}result.text="#"+q.id+" · "+q.offence+" · "+simulation.CivicDate(q.time)+" · "+q.outcome;}
                }catch(Exception){result.text="Unable to read this archived record.";}
            });
        }
        void HouseholdContext(VisualElement parent,RtsBuilding building){
            var colony=world.Settlements?[building.team];if(colony==null||building.definition.housingCapacity<=0)return;
            var h=colony.households.Find(x=>x.home==building.persistentId);if(h==null){Label(parent,"VACANT FAMILY HOME Â· up to six residents","body");return;}
            Label(parent,h.surname+" household Â· "+h.tenure,"subheading");var owner=CivilisationRuntime.Person(colony,h.owner);Label(parent,"Owner: "+(h.tenure==HomeTenure.SocialHousing?"Local council":owner?.name??"Unassigned"),"muted");
            foreach(long id in h.members){var p=CivilisationRuntime.Person(colony,id);if(p!=null&&p.alive)Label(parent,$"{p.name}, {(int)p.age} Â· {p.relationship} Â· {(p.age<18?"Child / education":p.role.ToString())}","body");}
        }
        void RefreshResidents(CivilisationRuntime simulation,float scroll){
            var colony=simulation.Colony(simulation.BodyId(fixture.definition.bodyId),inspectedFaction);if(colony==null){Label(civilisationContent,"No settlement on this world","body");return;}
            Label(civilisationContent,$"{colony.name.ToUpperInvariant()} Â· LOCAL TIME {simulation.LocalHour:00.0}h","heading");
            Label(civilisationContent,$"{colony.population} residents Â· {colony.households.Count} households Â· {colony.employed}/{colony.workforce} working-age residents employed","body");
            Label(civilisationContent,"COUNCIL & LOCAL ENTERPRISE","subheading");var council=colony.council;
            Label(civilisationContent,$"Treasury {council.treasury:0.00} credits Â· wage tax {council.taxRate:P0} Â· taxes collected {council.taxesCollected:0.00}\nEducation {council.educationSpent:0.00} Â· welfare {council.welfareSpent:0.00} Â· projects {council.infrastructureSpent:0.00}\nPriority: {council.priority} Â· {council.decision}","body").name="council-summary";
            if(fixture.sessionMode!=PlanetGameMode.AiTest&&inspectedFaction==0){var row=new VisualElement();row.AddToClassList("row");civilisationContent.Add(row);foreach(CivicPriority choice in Enum.GetValues(typeof(CivicPriority))){var priority=choice;Button(row,"FUND "+choice.ToString().ToUpperInvariant(),()=>{council.priority=priority;council.decision="Resident mandate: "+priority;nextCivilisationRefresh=0;});}}
            Label(civilisationContent,"One household per home. School age 5â€“17; adults qualify through education and experience. Households select an adult shopper; any parent can take that role. Homes share residential streets with nearby shops. Shifts have staggered starts; errands follow work, school ends in the afternoon, and some residents visit the council before returning home.","muted");
            Label(civilisationContent,"Select a civilian in the world to inspect their identity and activity. The settlement-wide live resident directory is disabled.","muted");
            Label(civilisationContent,$"Archived history: {colony.archivedResidents} biographies · {colony.archivedDeaths} burials · {colony.archivedCases} closed cases","muted");
            ArchiveLookup(civilisationContent,simulation,colony);
            RecentDeaths(civilisationContent,colony,20);
            Label(civilisationContent,"LOCAL BUSINESSES","subheading");int shown=0;foreach(var e in colony.enterprises){if(shown++>=20)break;Label(civilisationContent,$"{e.name} Â· cash {e.balance:0.00} Â· payroll {e.wagesPaid:0.00} Â· sales {e.sales:0.00}","muted");}
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
    }
}
