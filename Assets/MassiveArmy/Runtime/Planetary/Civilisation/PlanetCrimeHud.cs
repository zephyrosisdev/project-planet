using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        bool crimeView,crimeMap;int crimePage;string crimeSection="Incidents";
        void OpenCrime(){orbitalsView=residentsView=resourcesView=false;crimeView=true;if(civilisationOpen)civilisationOpen=false;ToggleCivilisation();}
        void RefreshCrime(CivilisationRuntime simulation,float scroll){
            var colony=simulation.Colony(simulation.BodyId(fixture.definition.bodyId),inspectedFaction);if(colony==null){Label(civilisationContent,"No settlement on this world","body");return;}
            Button(civilisationContent,"INCIDENT MAP: "+(crimeMap?"ON":"OFF"),()=>{crimeMap=!crimeMap;nextCivilisationRefresh=0;},"crime-map-toggle");
            SettlementCrimeView.Build(civilisationContent,colony,Observer,crimeSection,crimePage,simulation.CivicDate,(section,page)=>{crimeSection=section;crimePage=page;((ScrollView)civilisationContent).scrollOffset=Vector2.zero;nextCivilisationRefresh=0;},place=>{ToggleCivilisation();fixture.Rig.SetFocusDirection(CubeSphere.Decode(place));fixture.Rig.Distance=250*world.Grid.PresentationScale;fixture.Rig.RefreshPose();world.Rebase();world.Notice="Incident location · orange crosses on the minimap show recent reported incidents";crimeMap=true;});
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
    }
}
