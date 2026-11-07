using UnityEngine;
namespace MassiveArmy.Planetary
{
    [CreateAssetMenu(menuName="Massive Army/Civilisation Settings")]
    public sealed class CivilisationSettings:ScriptableObject {
        public bool enabledInGame=true;
        [Min(.25f)]public float economyTickSeconds=1;
        [Min(1)]public float strategyTickSeconds=5;
        [Min(1)]public int maximumCatchupTicks=8;
        [Range(.01f,.5f)]public float bootstrapAbundanceFloor=.12f;
        [Range(0,1)]public float homeStrategicAbundance=.005f;
        [Min(1000)]public float depositCapacity=40000;
        [Min(.01f)]public float geologicalMinimumEfficiency=.15f;
        [Min(1)]public int geologicalDepositsPerFaction=96;
        public float observationSeconds=60,spectroscopySeconds=120,probeSurveySeconds=180;
        public float launchPreparationSeconds=30,launchAscentSeconds=120;
        public float logisticsReserveSeconds=120,shortageForecastSeconds=600;
        public float expansionResourceWeight=10,expansionHabitabilityWeight=2,expansionDistanceWeight=1;
        [Header("Autonomous settlement supply")]
        public float aiConstructionReserve=600,aiLogisticsFuelReserve=30;
        [Min(1)]public int aiMinimumMaterialMines=2,aiMaximumMaterialMines=4;
        [Header("Settlement logistics")]
        public bool autoExtendSettlementRoads=true;
        [Min(1)]public int roadExtensionsPerTick=1;
        [Min(32)]public float roadTileSize=32;
        public float roadTileCost=1,roadMaximumStepHeight=3;
        public int roadSearchBudget=4096,maximumRoadTiles=0,maximumCargoVehicles=64;
        [Min(.1f)]public float roadMeshBudgetMilliseconds=.75f;
        [Header("Navigation memory budgets")]
        [Tooltip("Higher concurrency avoids queuing busy fleets; the separate scratch budget still caps allocations.")]
        [Range(1,512)]public int maximumCargoSearches=256;
        [Range(1,512)]public int cargoSearchMemoryMiB=192,civilianFlowMemoryMiB=32;
        [Range(4,128)]public int maximumCivilianFlowGoals=128;
        [Min(1)]public float dormantResourceReleaseSeconds=15;
        [Range(64,4096)]public int recentHistoryRecords=256;
        public float groundCargoSpeed=12,airCargoSpeed=55,cargoCapacity=80,cargoHandlingSeconds=3;
        public float railTileCost=2,trainCargoCapacity=480,trainCargoSpeed=35,trainCargoCost=750;
        public float shipCargoCapacity=240,airFreightCapacity=160,shipCargoSpeed=18,freightAircraftSpeed=90,shipCargoCost=620,freightAircraftCost=900;
        public float inputBufferBatches=3,outputBufferBatches=2;
        public float cargoVehicleSeconds=30,cargoVehicleMaterialCost=120,airCargoVehicleMaterialCost=350;
        public float groundFuelPerMetre=.0001f,airFuelPerMetre=.001f;
        [Header("Frontier factions and strategic AI LOD")]
        public bool raidersEnabled=true;
        [Range(0,8)]public int raiderCampCount=3;
        [Min(1)]public float frontierTickSeconds=10;
        [Min(300)]public float frontierDetailRange=3000,raiderAmbushRange=2500;
        [Min(30)]public float raiderRaidSeconds=90;
        [Header("Bio-warfare outbreaks")]
        [Min(1)]public float distantOutbreakTickSeconds=10,infectionConversionSeconds=30;
        [Min(0)]public float infectionContactRate=.002f;
        [Header("Oil and gas pipelines")]
        [Min(.01f)]public float pipelineUnitsPerSecond=8,pipelineMaterialPerMetre=.02f;
        [Range(0,1)]public float pipelineFuelFraction=.1f;
        [Range(1,256)]public int maximumPipelineLinks=128;
        [Header("Farming")]
        public float farmRainEfficiency=.5f,soilRecoveryPerSecond=.0002f;
        [Header("Population and upkeep")]
        public int startingPopulation=24;
        public float populationFoodPerSecond=.002f,populationGrowthSeconds=30,buildingInputReserveSeconds=120;
        [Header("Regional settlement planning")]
        public bool regionalSettlements=true;
        [Range(1,32)]public int maximumRegionalSettlements=12;
        [Min(10)]public float regionalExpansionSeconds=120;
        [Min(100)]public float regionalFoundingReserve=1200;
        [Range(8,100)]public int regionalExpansionBuildings=24;
        [Min(500)]public float regionalTownRadius=3500;
        [Header("Bounded outdoor life")]
        public bool ambientSettlementLife=true;
        [Range(0,512)]public int outdoorCitizensPerSettlement=64;
        [Range(0,2048)]public int outdoorCitizenLimit=256;
        [Range(0,8)]public int ambientCartsPerSettlement=2;
        [Range(0,32)]public int ambientCartLimit=8;
        [Min(5)]public float ambientCartInterval=30;
        [Range(64,32768)]public int maximumLandTiles=8192;
        [Range(0,512)]public int visibleCropTiles=128;
        [Range(2,12)]public int cropClumpsPerSide=6;
        [Range(1,16)]public int maximumHarvesters=4;
        [Min(100)]public float cropDrawDistance=1200;
        [Min(1)]public float landTileMaterialCost=1,harvestSweepSeconds=20;
        [Header("Households and daily life")]
        [Min(60)]public float settlementDaySeconds=600;
        [Min(1)]public int calendarDaysPerYear=365,maximumCivilianAgents=16384;
        public float civilianWalkingSpeed=2.8f,civilianDrawDistance=1800;
        [Header("Dormant settlements")]
        [Min(1)]public float backgroundTickSeconds=5;
        [Header("Colony deliveries")]
        [Range(2,60)]public float colonyDeliverySeconds=15;
        public float colonySupplyMaterials=600,colonySupplyFuel=100,colonySupplyFood=100;
        [Header("Starting settlement prebuild")]
        [Range(0,30)]public float startingPrebuildSeconds=3;
        [Min(0)]public int startingPrebuildMaximumAdditions=512;
        public float catchupBudgetMilliseconds=.5f,catchupMaximumStepSeconds=60;
        public int catchupStepsPerFrame=4;
    }
}
