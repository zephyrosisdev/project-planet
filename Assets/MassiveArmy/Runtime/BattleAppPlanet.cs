using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        Planetary.PlanetDevelopment planetSession;
        void StartPlanetMatch()
        {
            DisposeBattle();Paused=false;State="battle";worldSetupOpen=false;
            var host=new GameObject("Planetary RTS session");host.transform.SetParent(transform,false);
            planetSession=host.AddComponent<Planetary.PlanetDevelopment>();planetSession.currentRtsContent=true;
            planetSession.requestedCapacity=settings.capacity;planetSession.ambientZombies=settings.ambientZombies;
            planetSession.sessionMode=settings.swarmMode?Planetary.PlanetGameMode.Swarm:settings.spectatorMode?Planetary.PlanetGameMode.AiTest:Planetary.PlanetGameMode.Rts;
            planetSession.configuredRadius=Planetary.PlanetDefinition.DefaultRadius;planetSession.streamedTerrain=true;
            planetSession.returnToMenu=ReturnMenu;planetSession.terrainSeed=settings.seed;
            var definition=Resources.Load<Planetary.PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");
            if(definition!=null)planetSession.ConfigureDefinition(definition,settings.seed);
            View.enabled=false;rig.InputEnabled=false;if(ui?.Root!=null)ui.Root.style.display=UnityEngine.UIElements.DisplayStyle.None;
        }
    }
}
