using UnityEngine;
namespace MassiveArmy.Planetary
{
    [CreateAssetMenu(menuName="Massive Army/Galaxy Definition")]
    public sealed class GalaxyDefinition:ScriptableObject
    {
        public int seed=90173;
        [Range(2,256)] public int starSystems=48;
        [Range(1,6)] public int planetsPerSystem=4;
        [Range(2,6)] public int spiralArms=4;
        [Range(0,1)] public float binaryFraction=.4f;
        [Tooltip("Extra unshadowed illumination from the second star. Adds to the scene's authored exposure.")]public bool secondaryStarlight=false;
        public double galaxyRadius=8e9,blackHoleMu=1e19;
        public double systemOrbitRadius=16e6,binarySeparation=800000,stellarMu=6e14;
        [Min(1)] public float skyRefreshSeconds=10;
        [Min(1000)] public float skyTravelThreshold=100000;
        [Min(1000000)] public float resolvedStarDistance=100000000;
        [Header("Background sky (visual only; travel catalogue above)")]
        [Range(0,100000)] public int backgroundStars=30000;
        [Range(0,2)] public float backgroundStarBrightness=.65f;
        public bool nebulae=true;
        [Range(0,2)] public float nebulaBrightness=.8f;
        [Tooltip("Cached 3D cloud integration; rebuilt only after substantial travel and a minimum interval.")]
        [Min(10)] public float nebulaRefreshSeconds=30;
        [Range(2,16)] public int terrainPreviewBudget=8;
        [Range(.2f,2)] public float predictionBudgetMs=.75f;
        [Range(60,7200)] public float predictionHorizon=1800;
        [Tooltip("Finite generated catalogue. Visited worlds retain their frozen GPU army state; budget army capacity accordingly.")]
        public bool enabledInGame=true;
    }
}
