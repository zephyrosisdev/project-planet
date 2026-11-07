using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        bool crimeView,crimeMap;int crimePage;string crimeSection="Incidents";
        void OpenCrime(){orbitalsView=residentsView=resourcesView=false;crimeView=true;if(civilisationOpen)civilisationOpen=false;ToggleCivilisation();}
        void RefreshCrime(CivilisationRuntime simulation,float scroll){
            var colony=simulation.Colony(simulation.BodyId(fixture.definition.bodyId),inspectedFaction);if(colony==null){Label(civilisationContent,"No settlement on this world","body");return;}
            Button(civilisationContent,"INCIDENT MAP: "+(crimeMap?"ON":"OFF"),()=>{crimeMap=!crimeMap;nextCivilisationRefresh=0;},"crime-map-toggle");
            var frontier=simulation.Planet(fixture.definition.bodyId);
            Label(civilisationContent,"FRONTIER · "+frontier.raiders.FindAll(x=>x.health>0).Count+" raider camps · "+frontier.frontierIncidents.Count+" shipment incidents","subheading");
            foreach(var camp in frontier.raiders)if(camp.health>0){var current=camp;Button(civilisationContent,current.name+" · stolen cargo "+current.stolen.ToString("0.0"),()=>{fixture.Rig.SetFocusDirection(CubeSphere.Decode(current.position));fixture.Rig.Distance=500;fixture.Rig.RefreshPose();ToggleCivilisation();});}
            for(int i=System.Math.Max(0,frontier.frontierIncidents.Count-8);i<frontier.frontierIncidents.Count;i++)Label(civilisationContent,simulation.CivicDate(frontier.frontierIncidents[i].time)+" · "+frontier.frontierIncidents[i].description,"body");
            foreach(var outbreak in frontier.outbreaks)Label(civilisationContent,"OUTBREAK · settlement "+(outbreak.faction+1)+" · estimated infected "+outbreak.infected.ToString("0")+" · distant zombies "+outbreak.zombies.ToString("0"),"body");
            SettlementCrimeView.Build(civilisationContent,colony,Observer,crimeSection,crimePage,simulation.CivicDate,(section,page)=>{crimeSection=section;crimePage=page;((ScrollView)civilisationContent).scrollOffset=Vector2.zero;nextCivilisationRefresh=0;},place=>{ToggleCivilisation();fixture.Rig.SetFocusDirection(CubeSphere.Decode(place));fixture.Rig.Distance=250*world.Grid.PresentationScale;fixture.Rig.RefreshPose();world.Rebase();world.Notice="Incident location · orange crosses on the minimap show recent reported incidents";crimeMap=true;});
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
    }
}
