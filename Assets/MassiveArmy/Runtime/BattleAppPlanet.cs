using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        Planetary.PlanetDevelopment planetSession;
        public void StartSurvivalGame(){
            if(State=="loading")return;
            settings.baseBuilding=false;settings.swarmMode=false;settings.spectatorMode=false;
            StartPlanetMatch(true);
        }
        void StartPlanetMatch(bool survival=false)
        {
            if(!survival)throw new System.NotSupportedException("Planetary RTS has been retired. Use Survival.");
            DisposeBattle();Paused=false;State="battle";worldSetupOpen=false;
            var host=new GameObject(survival?"Space survival session":"Planetary RTS session");host.transform.SetParent(transform,false);
            planetSession=host.AddComponent<Planetary.PlanetDevelopment>();planetSession.currentRtsContent=true;
            planetSession.requestedCapacity=settings.capacity;planetSession.ambientZombies=settings.ambientZombies;
            planetSession.sessionMode=survival?Planetary.PlanetGameMode.Survival:settings.swarmMode?Planetary.PlanetGameMode.Swarm:settings.spectatorMode?Planetary.PlanetGameMode.AiTest:Planetary.PlanetGameMode.Rts;
            planetSession.configuredRadius=0;planetSession.streamedTerrain=true;planetSession.systemSizeMultiplier=0;
            planetSession.returnToMenu=ReturnMenu;planetSession.terrainSeed=settings.seed;
            planetSession.worldPreset=Resources.Load<Planetary.SurvivalWorldPreset>("MassiveArmy/Planets/DefaultWorldPreset");
            if(planetSession.worldPreset==null)throw new System.InvalidOperationException("Shared survival world preset is missing");
            View.enabled=false;rig.InputEnabled=false;if(ui?.Root!=null)ui.Root.style.display=UnityEngine.UIElements.DisplayStyle.None;
        }
    }
}
