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
        public float roadTileSize=8,roadTileCost=1,roadMaximumStepHeight=3;
        public int roadSearchBudget=4096,maximumRoadTiles=32768,maximumCargoVehicles=64;
        public float groundCargoSpeed=12,airCargoSpeed=55,cargoCapacity=80,cargoHandlingSeconds=3;
        public float railTileCost=2,trainCargoCapacity=480,trainCargoSpeed=35,trainCargoCost=750;
        public float shipCargoCapacity=240,airFreightCapacity=160,shipCargoSpeed=18,freightAircraftSpeed=90,shipCargoCost=620,freightAircraftCost=900;
        public float inputBufferBatches=3,outputBufferBatches=2;
        public float cargoVehicleSeconds=30,cargoVehicleMaterialCost=120,airCargoVehicleMaterialCost=350;
        public float groundFuelPerMetre=.0001f,airFuelPerMetre=.001f;
        [Header("Farming")]
        public float farmRainEfficiency=.5f,soilRecoveryPerSecond=.0002f;
        [Header("Population and upkeep")]
        public int startingPopulation=24;
        public float populationFoodPerSecond=.002f,populationGrowthSeconds=30,buildingInputReserveSeconds=120;
        [Header("Households and daily life")]
        [Min(60)]public float settlementDaySeconds=600;
        [Min(1)]public int calendarDaysPerYear=365,maximumCivilianAgents=16384;
        public float civilianWalkingSpeed=2.8f,civilianDrawDistance=1800;
        [Header("Dormant settlements")]
        public float catchupBudgetMilliseconds=.5f,catchupMaximumStepSeconds=60;
        public int catchupStepsPerFrame=4;
    }
}
