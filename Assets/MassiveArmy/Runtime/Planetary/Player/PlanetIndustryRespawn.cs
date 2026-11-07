using MassiveArmy.Industrial;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        internal void StopForPlayerDeath(){CloseMenu();StopCableTool();placementArmed=false;placingPile=false;CancelRoutePlanning();if(driver!=null){driver=null;driverMotor=null;world.AutomaticPlayerStep=true;}}
        internal void StopConstructionTools(){StopCableTool();placementArmed=false;placingPile=false;}
        void RespawnDeployButtons(VisualElement parent){foreach(string item in new[]{"sleeping-bag","sleeping-pod"}){string id=item;UiButton(parent,"PLACE "+Nice(id).ToUpperInvariant()+" · "+actor.inventory.Count(id),()=>{StopConstructionTools();world.BeginRespawnPlacement(id);},"deploy-"+id).SetEnabled(Authority.World.creativeMode||actor.inventory.Count(id)>0);}}
    }
}

