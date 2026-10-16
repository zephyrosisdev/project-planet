using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy
{
    public sealed class BattleAtmosphere : MonoBehaviour
    {
        public BattleVisualSettings settings;
        VolumeProfile profile;Light sun;int lastHash;
        public void Initialize(BattleVisualSettings source,VolumeProfile volume,Light light)
        {settings=source;profile=volume;sun=light;settings.ApplyLighting(profile,sun);lastHash=settings.LightingHash;}
        void Update(){if(settings==null||profile==null||sun==null)return;if(lastHash!=settings.LightingHash){settings.ApplyLighting(profile,sun);lastHash=settings.LightingHash;}settings.ApplyArmyLighting(sun);}
    }
}
