using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSystemRuntime {
        PlanetDevelopment respawnCarrierWorld;
        internal PlanetDevelopment RespawnCarrierWorld=>respawnCarrierWorld??worlds?[0]??ActiveWorld;
        internal void PrepareRespawnPlanet(int body){for(int i=0;i<Bodies.Length;i++)if(Bodies[i].bodyId==body){PreparePlanet(i);return;}}
        internal PlanetDevelopment PreparedRespawnPlanet(int body){for(int i=0;i<Bodies.Length;i++)if(Bodies[i].bodyId==body)return worlds[i]!=null&&worlds[i].Ready?worlds[i]:null;return null;}
        internal void SelectRespawnWorld(PlanetDevelopment next){if(next==ActiveWorld)return;var previous=ActiveWorld;next.PlayerForValidation.CopySessionFrom(previous.PlayerForValidation);previous.SetResidentActive(false);next.SetResidentActive(true);ActiveWorld=next;arrivalTarget=-1;Physics.SyncTransforms();}
    }
}
