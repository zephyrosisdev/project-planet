using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.Rendering;

namespace MassiveArmy.Planetary

{

    public enum PlanetGameMode { Rts, Swarm, AiTest, Survival }

    /// <summary>Planetary survival runtime with opt-in legacy RTS validation fixtures.</summary>

    public sealed partial class PlanetDevelopment : MonoBehaviour

    {

        public bool deformTerrainOnNuke=true;
        public PlanetGameMode sessionMode;public int requestedCapacity,ambientZombies=0,terrainSeed=73129;

        public bool IsSurvival=>sessionMode==PlanetGameMode.Survival;
        internal string SurvivalNotice="";
        internal PlanetPathfindingAdapter NavigationForValidation=>navigation;

        internal System.Action SimulationBeginForValidation,SimulationEndForValidation;

        public bool allowLegacyZombieSpawning;
        public bool Paused,AttackMoveArmed,NuclearTargetArmed;public System.Action returnToMenu;

        public string ModeStatus=>sessionMode.ToString()+" · Radius "+(definition!=null?(definition.radius/1000).ToString("0.#"):"6")+" km · "+(encounter?.Status??"Planetary base operations");

        PlanetWeatherRenderer weatherRenderer;
        internal PlanetWeatherRenderer WeatherForValidation=>weatherRenderer;
        readonly Vector4[] terrainPrecisionUv=new Vector4[6],terrainPrecisionBase=new Vector4[6],terrainRoadAnchor=new Vector4[6];
        void PrepareTerrainPrecision(){
            var observer=origin.Origin.RelativeTo(definition.position);
            double Dot(Double3 p,Vector3 v)=>p.x*v.x+p.y*v.y+p.z*v.z;
            for(int face=0;face<6;face++){
                double denominator=System.Math.Max(1,System.Math.Abs(Dot(observer,CubeSphere.Normal[face])));
                float u=Mathf.Clamp((float)(Dot(observer,CubeSphere.Right[face])/denominator),-1,1),v=Mathf.Clamp((float)(Dot(observer,CubeSphere.Up[face])/denominator),-1,1);
                var cube=new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*u+new Double3(CubeSphere.Up[face])*v;
                var anchor=cube*(definition.radius/cube.Magnitude);
                terrainPrecisionUv[face]=new Vector4(u,v,0,0);terrainPrecisionBase[face]=(anchor-observer).Float;
                double resolution=Base?.Grid?.Resolution??0,cx=((double)u+1)*.5*resolution,cy=((double)v+1)*.5*resolution;double ix=System.Math.Floor(cx),iy=System.Math.Floor(cy);terrainRoadAnchor[face]=new Vector4((float)ix,(float)iy,(float)(cx-ix),(float)(cy-iy));
            }
            terrainMaterial?.SetVectorArray("_PlanetRoadAnchor",terrainRoadAnchor);terrainMaterial?.SetFloat("_PlanetRoadAnchored",Base?.Grid!=null?1:0);
        }
        PlanetEncounter encounter;PlanetLighting lighting;PlanetSpacecraft spacecraft;PlanetHeadquartersHud headquartersHud;
        internal PlanetHeadquartersHud HeadquartersForValidation=>headquartersHud;

        public bool StrategyEnabled {get;private set;}
        internal Vector3 OpposingHome {get;private set;}

        public void FocusFaction(int team){if(Base==null||team<0||team>=Base.Factions.Length)return;rig.SetFocusDirection(Base.Homes[team]);rig.Distance=300;}

        internal PlanetSpacecraft ShipForValidation=>spacecraft;
        internal PlanetFirstPersonController PlayerForValidation=>player;int shipInteraction=-1;internal bool AutomaticPlayerStep=true;
        public string InteractionPrompt=>IndustrialDriving?"F - LEAVE INDUSTRIAL VEHICLE":IndustrialInteractionPrompt.Length>0?IndustrialInteractionPrompt:PersonalPrompt.Length>0?PersonalPrompt:VehicleInteractionPrompt.Length>0?VehicleInteractionPrompt:IsPiloting?"F - LEAVE PILOT SEAT":IsFirstPerson&&StationInteractionPrompt().Length>0?StationInteractionPrompt():IsFirstPerson&&spacecraft!=null?spacecraft.InteractionPrompt(shipInteraction):"";

        public bool IsPiloting=>modes!=null&&modes.Mode==PlayerControlMode.Spacecraft;

        internal PlanetLighting LightingForValidation=>lighting;

        internal PlanetEncounter EncounterForValidation=>encounter;
        internal void TickCivicCombatForValidation(){encounter.BeforeTick();encounter.AfterTick();SimulationTicks++;}

        public void ReturnToMenu(){if(returnToMenu!=null)returnToMenu();else UnityEngine.SceneManagement.SceneManager.LoadScene("MassiveBattle");}

        public PlanetDefinition definition;
        // Sessions own a clone, so terrain edits and launch overrides never mutate the saved preset.
        internal void ConfigureDefinition(PlanetDefinition template,int? seed=null){definition=Instantiate(template);ownsDefinition=true;if(seed.HasValue)definition.terrainSeed=seed.Value;}


        public PlanetDefinition[] distantBodies;

        [Range(8,128)]public int navigationResolution=32;

        [Range(1,1000000)]public int unitCount=32;

        public float unitSpeed=16;

        public bool adaptiveTerrain=true;

        public bool streamedTerrain;

        [Header("System size experiment (restart Play to apply)")]
        [Tooltip("Scales every planet/star radius and celestial separation. 1 = normal, 10 = ten times larger, 100 = one hundred times larger. Keeps local terrain relief, vehicles, army capacity and surface gravity unchanged. High scales are experimental.")]
        [Range(1,1000)] public float systemSizeMultiplier=1;
        [ContextMenu("System size/Normal (1x)")] void NormalSystemSize()=>systemSizeMultiplier=1;
        [ContextMenu("System size/Large (10x)")] void LargeSystemSize()=>systemSizeMultiplier=10;
        [ContextMenu("System size/Huge (100x)")] void HugeSystemSize()=>systemSizeMultiplier=100;
        [Min(0)]public float configuredRadius=PlanetDefinition.DefaultRadius;

        public PlanetStreamedTerrain StreamedTerrain=>streamed;

        PlanetStreamedTerrain streamed;PlanetTerrainRenderer terrainRenderer;PlanetFlightHudRenderer flightHudRenderer;int renderResolution;

        public bool currentRtsContent=true;

        [Tooltip("GPU pressure and predictive traffic steering; measured costs are in TestResults/Planetary/Crowd/README.md.")]public bool restoredCrowd=true;

        [Range(.25f,8)]public float terrainErrorPixels=2;
        [Tooltip("Extra grid rows retained around each terrain LOD band. GPU patch budget stays fixed.")]
        [Range(0,8)]public int terrainLodBufferLayers=2;
        [Tooltip("Split/merge hysteresis: larger values retain refined patches longer during camera movement.")]
        [Range(.05f,.5f)]public float terrainLodHysteresis=.3f;

        PlanetSurface surface;PlanetRtsCamera rig;Camera view;FloatingOrigin origin;

        PlanetPathfindingAdapter navigation;ComputeShader movement;

        GraphicsBuffer costs,units,arguments,biomeColours;

        Material terrainMaterial,unitMaterial;Mesh tank;

        Vector3 goal;float accumulator;int moveKernel;bool ownsDefinition;

        PlanetFirstPersonController player;PlayerControlModeManager modes;

        MaterialPropertyBlock oceanProperties,landProperties;

        ScaledSpaceRenderer scaledRenderer;PlanetDefinition validationDistantBody;
        internal PlanetSystemRuntime SystemRuntime;
        internal bool ResidentActive=true;
        public bool SimulationActive {get;private set;}=true;
        public PlanetSystemDefinition systemDefinition;

        PlanetCbtTerrain cbt;bool showTerrainDebug,showTerrainTriangles;

        PlanetArmyCommands commands;bool selecting;Vector2 selectionStart;bool legacyCommands;

        internal bool SettlementVisualsReadyForValidation=>settlementRenderer?.ReadyForValidation??true;
        SettlementRenderer settlementRenderer;PlanetResourceDecals resourceDecals;PlanetCatalogRenderer catalogRenderer;PlanetRtsHud hud;GraphicsBuffer archetypes,traits,buildingFootprints;int capacity;

        internal Material TerrainMaterial=>terrainMaterial;

        public PlanetRtsBase Base {get;private set;}

        public PlanetRtsHud Hud=>hud;

        internal GraphicsBuffer UnitsForValidation=>units;internal GraphicsBuffer ArchetypesForCivicCombat=>archetypes;internal GraphicsBuffer ArmyForCivicCombat=>units;

        GraphicsBuffer placementProbe;readonly uint[] occupied=new uint[1];

        public bool IsFootprintClear(Vector3 direction,float radius,bool air=false)

        {

            if(unitCount==0)return true;if(placementProbe==null)return false;occupied[0]=0;placementProbe.SetData(occupied);int probe=movement.FindKernel("CheckClearance");movement.SetVector("_PlacementDirection",direction);movement.SetFloat("_PlacementRadius",radius);movement.SetInt("_PlacementAir",air?1:0);movement.Dispatch(probe,(unitCount+127)/128,1,1);placementProbe.GetData(occupied);return occupied[0]==0;

        }

        internal Vector3 InterestDirection=>IsDriving&&groundVehicles?.Controlled!=null?groundVehicles.Controlled.Motor.Pose.position.Float.normalized:IsPiloting?spacecraft.Direction:IsFirstPerson?player.Direction:rig.FocusDirection;

        internal float InterestRadius=>IsFirstPerson||IsDriving?600:Mathf.Clamp(rig.Distance*1.5f,600,1200);

        public bool IsFirstPerson=>modes!=null&&modes.Mode==PlayerControlMode.FirstPerson;

        public PlanetArmyCommands Commands=>commands;

        public Vector3 PlanetRenderCentre=>origin.RenderPosition(definition.position);
        public Vector3 RenderBodyPosition(Double3 bodyPosition)=>origin.RenderPosition(definition.position.Add(bodyPosition));
        public Double3 RenderToBody(Vector3 renderPosition)=>origin.RenderToUniverse(renderPosition).RelativeTo(definition.position);

        public bool LaunchNuke(Vector3 direction){NuclearTargetArmed=false;bool launched=encounter!=null&&encounter.LaunchNuke(Base.Selected,direction);Base.Notice=launched?"NUCLEAR MISSILE LAUNCHED · 12 seconds to impact":"Launch unavailable: check range, fuel, isotopes, technology and silo cooldown";return launched;}

        public bool OrderSelected(Vector3 direction,bool attackMove=false,Vector3? formationEnd=null)

        {

            if(sessionMode==PlanetGameMode.AiTest)return false;
            var cell=CubeSphere.Cell(definition.bodyId,direction,navigationResolution);

            bool land=surface.SampleHeight(navigation.Regional!=null?direction:CubeSphere.Direction(cell,navigationResolution))>=definition.oceanHeight;

            return commands!=null&&commands.Move(navigation,direction,m=>m==3||(m==4?!land:land),attackMove||AttackMoveArmed,formationEnd);

        }

        internal bool UnitSpawnTerrainClear(UnitDefinition d,Vector3 direction)
        {
            bool land=surface.SampleHeight(navigation.Regional!=null?direction:CubeSphere.Direction(CubeSphere.Cell(definition.bodyId,direction,navigationResolution),navigationResolution))>=definition.oceanHeight;

            if(d.movement!=MovementClass.Air&&(d.movement==MovementClass.Water?land:!land))return false;

            if(d.movement!=MovementClass.Air){var cellDirection=navigation.Regional!=null?direction:CubeSphere.Direction(CubeSphere.Cell(definition.bodyId,direction,navigationResolution),navigationResolution);foreach(var b in Base.Buildings){float radius=(Base?.FootprintRadius(b.definition)??Mathf.Sqrt(b.definition.footprintWidth*b.definition.footprintWidth+b.definition.footprintDepth*b.definition.footprintDepth)*(SystemRuntime?.Civilisation?.Settings.roadTileSize??PlanetConstructionGrid.DefaultTileSize)*.5f)+2+(navigation.Regional!=null?d.radius:definition.radius/navigationResolution);if(!b.removed&&CubeSphere.ArcDistance(cellDirection,CubeSphere.Decode(b.position),definition.radius)<radius)return false;}}

            return true;
        }
        public bool TrySpawnUnit(int type,Vector3 direction,int team=0)

        {

            if(Base==null||unitCount>=capacity)return false;var d=Base.Catalog.units[type];if(!UnitSpawnTerrainClear(d,direction))return false;
            if(!IsFootprintClear(direction,d.radius,d.movement==MovementClass.Air))return false;

            int originalTeam=team;team=Base.CombatTeam(team);movement.SetInt("_PlanetSpawnFlags",originalTeam!=0?33554432:0);int spawn=movement.FindKernel("SpawnCatalog");movement.SetBuffer(spawn,"_PlanetUnits",units);movement.SetBuffer(spawn,"_Archetypes",archetypes);movement.SetInt("_PlanetSpawnSlot",unitCount);movement.SetInt("_PlanetSpawnType",type);movement.SetInt("_PlanetSpawnTeam",team);movement.SetInt("_PlanetSpawnGroup",originalTeam!=0?16:sessionMode==PlanetGameMode.AiTest?15:0);movement.SetVector("_PlanetStart",CubeSphere.Encode(direction));movement.Dispatch(spawn,1,1,1);

            unitCount++;movement.SetInt("_PlanetUnitCount",unitCount);commands.SetCount(unitCount);return true;

        }

        internal void SetArmyCount(int count){unitCount=count;movement.SetInt("_PlanetUnitCount",count);commands.SetCount(count);}

        public void RefreshBuildingCosts(System.Collections.Generic.List<RtsBuilding> buildings,bool asynchronous=false)

        {

            int cells=navigation.CellCount;int generate=movement.FindKernel("GenerateCosts");movement.Dispatch(generate,(cells+127)/128,1,1);

            var footprints=new Vector4[buildings.Count];for(int i=0;i<buildings.Count;i++){var b=buildings[i];var d=CubeSphere.Decode(b.position);footprints[i]=new Vector4(d.x,d.y,d.z,b.removed||b.definition.id=="gate"&&b.gateOpen?0:(Base?.FootprintRadius(b.definition)??Mathf.Sqrt(b.definition.footprintWidth*b.definition.footprintWidth+b.definition.footprintDepth*b.definition.footprintDepth)*(SystemRuntime?.Civilisation?.Settings.roadTileSize??PlanetConstructionGrid.DefaultTileSize)*.5f)+2);}

            if(buildingFootprints.count<Mathf.Max(1,buildings.Count)){buildingFootprints.Dispose();buildingFootprints=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.NextPowerOfTwo(buildings.Count),16);}
            buildingFootprints.SetData(footprints);int stamp=movement.FindKernel("StampBuildings");movement.SetBuffer(stamp,"_PlanetBuildings",buildingFootprints);movement.SetBuffer(stamp,"_PlanetCosts",costs);movement.SetInt("_PlanetBuildingCount",buildings.Count);movement.Dispatch(stamp,(cells+127)/128,1,1);navigation.SetRegionalBuildings(buildingFootprints,buildings.Count);navigation.Regional?.Bind(movement,moveKernel);if(asynchronous)navigation.InvalidateAsync();else navigation.Invalidate();

        }

        public PlanetCbtTerrain Cbt=>cbt;

        public Camera View=>view;

        public PlanetRtsCamera Rig=>rig;

        static readonly string[] simulationSpeedLabels={"0.25x","0.5x","1x","2x","4x","8x","16x","32x","64x"};
        public string SimulationSpeedLabel=>simulationSpeedLabels[simulationSpeedIndex];
        static readonly float[] simulationSpeeds={.25f,.5f,1,2,4,8,16,32,64};int simulationSpeedIndex=2;
        public float SimulationSpeed=>simulationSpeeds[simulationSpeedIndex];
        public float SimulationDelta=>Time.deltaTime*SimulationSpeed;
        public void ChangeSimulationSpeed(int step){simulationSpeedIndex=Mathf.Clamp(simulationSpeedIndex+System.Math.Sign(step),0,simulationSpeeds.Length-1);if(Base!=null)Base.Notice=$"Simulation {SimulationSpeed:0.##}x (numpad +/-)";}
        public uint SimulationTicks {get;private set;}

        bool initialized;internal bool DeferredInitialization;
        public bool Ready=>initialized;

        System.Collections.IEnumerator Start(){if(!DeferredInitialization){var steps=InitializeSteps();while(steps.MoveNext())yield return steps.Current;}}
        System.Collections.IEnumerator InitializeSteps()
        {
            if(Ready)yield break;
            var launchArgs=System.Environment.GetCommandLineArgs();
            if(System.Array.IndexOf(launchArgs,"-planet-deform-terrain")>=0)deformTerrainOnNuke=true;
            if(System.Array.IndexOf(launchArgs,"-planet-no-deform-terrain")>=0)deformTerrainOnNuke=false;

            StrategyEnabled=true;foreach(var arg in launchArgs)if((arg.EndsWith("-validation")&&arg!="-planet-weather-validation"&&arg!="-planet-gameplay-validation"&&arg!="-planet-menu-validation"&&arg!="-planet-ship-validation"&&arg!="-planet-landing-validation"&&arg!="-planet-hq-terrain-validation"&&arg!="-planet-system-validation"&&arg!="-galaxy-validation"&&arg!="-civilisation-validation"&&arg!="-planet-economy-ai-validation"&&arg!="-planet-household-validation"&&arg!="-planet-orbital-validation")||arg=="-planet-benchmark"||arg=="-planet-fixture")StrategyEnabled=false;

            if(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0)configuredRadius=0;

            if(System.Array.IndexOf(launchArgs,"-planet-restored-crowd")>=0)restoredCrowd=true;

            if(System.Array.IndexOf(launchArgs,"-planet-legacy-steering")>=0)restoredCrowd=false;

            if(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0)currentRtsContent=false;

            if(System.Array.IndexOf(launchArgs,"-planet-rts")>=0)currentRtsContent=true;

            if(System.Array.IndexOf(launchArgs,"-planet-visual-validation")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-rts-validation")>=0)ambientZombies=0;

            if(System.Array.IndexOf(launchArgs,"-planet-crowd-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.Rts;ambientZombies=0;requestedCapacity=8192;configuredRadius=2000;streamedTerrain=true;}

            if(System.Array.IndexOf(launchArgs,"-planet-ai-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.AiTest;ambientZombies=0;requestedCapacity=8192;configuredRadius=0;}

            if(System.Array.IndexOf(launchArgs,"-planet-interest-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.AiTest;ambientZombies=0;requestedCapacity=1048576;configuredRadius=2000;streamedTerrain=true;}

            if(IsSurvival){StrategyEnabled=false;currentRtsContent=false;unitCount=0;ambientZombies=0;requestedCapacity=0;spawnStartingTank=false;}
            oceanProperties=new MaterialPropertyBlock();landProperties=new MaterialPropertyBlock();

            // Compact validation fixtures deliberately use their original generated definition.
            if(configuredRadius==0&&(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0||System.Array.IndexOf(launchArgs,"-planet-ai-validation")>=0))definition=null;
            if(definition!=null&&!ownsDefinition)ConfigureDefinition(definition);
            if(definition==null){definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.oceanHeight=-8;definition.terrainSeed=terrainSeed;ownsDefinition=true;}

            if(System.Array.IndexOf(launchArgs,"-planet-interest-validation")>=0||System.Array.IndexOf(launchArgs,"-planet-crowd-validation")>=0){definition.terrainHeight=0;definition.oceanHeight=-8;}

            if(System.Array.IndexOf(launchArgs,"-planet-legacy-terrain")>=0)definition.stampTerrain=false;
            if(System.Array.IndexOf(launchArgs,"-planet-stamp-terrain")>=0)definition.stampTerrain=true;
            int radiusArg=System.Array.IndexOf(launchArgs,"-planet-radius");

            if(radiusArg>=0&&radiusArg+1<launchArgs.Length&&float.TryParse(launchArgs[radiusArg+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float requestedRadius))configuredRadius=Mathf.Clamp(requestedRadius,100,PlanetDefinition.TargetRadius*1000);

            if(configuredRadius>0){if(!ownsDefinition){definition=Instantiate(definition);ownsDefinition=true;}definition.radius=configuredRadius;definition.gravitationalParameter=9.81*definition.radius*definition.radius;navigationResolution=Mathf.Clamp(Mathf.CeilToInt(definition.radius/15.625f),32,128);}

            if(SystemRuntime==null){
                systemDefinition=systemDefinition!=null?systemDefinition:Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");
                if(systemSizeMultiplier==1&&systemDefinition!=null)systemSizeMultiplier=systemDefinition.sizeMultiplier;
                int scaleArg=System.Array.IndexOf(launchArgs,"-planet-size-multiplier");
                if(scaleArg>=0&&scaleArg+1<launchArgs.Length&&float.TryParse(launchArgs[scaleArg+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float requestedScale))systemSizeMultiplier=requestedScale;
                systemSizeMultiplier=float.IsNaN(systemSizeMultiplier)||float.IsInfinity(systemSizeMultiplier)?1:Mathf.Clamp(systemSizeMultiplier,1,1000);
                if(systemSizeMultiplier!=1){definition.radius*=systemSizeMultiplier;definition.gravitationalParameter*=(double)systemSizeMultiplier*systemSizeMultiplier;configuredRadius=definition.radius;}
                Debug.Log($"Planet system size: {systemSizeMultiplier:0.##}x; home radius {definition.radius/1000:0.##} km; diameter {definition.radius/500:0.##} km. Army capacity {requestedCapacity} unchanged.");
            }
            var startingHome=new Vector3(1,.12f,1.3f).normalized;
            if(configuredRadius>=PlanetDefinition.RegionalNavigationRadius&&System.Array.IndexOf(launchArgs,"-planet-interest-validation")<0&&System.Array.IndexOf(launchArgs,"-planet-crowd-validation")<0){if(SystemRuntime==null){definition.terrainHeight=definition.radius>=20000?1800:240;definition.oceanHeight=-35;}if(definition.radius>=20000&&SystemRuntime==null)definition.atmosphereHeight=Mathf.Max(10000,definition.atmosphereHeight);var home=startingHome;
                if(definition.radius>=20000){
                    if(AlliedStart)home=PlanetStartingRegion.SelectCoastal(definition,startingHome);
                    else if(DeferredInitialization){bool found=false;foreach(var candidate in PlanetStartingRegion.Candidates(definition,startingHome)){if(PlanetStartingRegion.ConnectedPlain(definition,candidate)){home=candidate;found=true;break;}yield return null;}if(!found)throw new System.InvalidOperationException("No connected dry starting region found");}
                    else home=PlanetStartingRegion.Select(definition,startingHome);
                }
                startingHome=home;var right=Vector3.Cross(Vector3.up,home).normalized;var enemy=(home+right*(4500/definition.radius)).normalized;
                if(!AlliedStart&&StrategyEnabled&&sessionMode!=PlanetGameMode.Swarm&&(SystemRuntime?.Civilisation==null||SystemRuntime.Civilisation.IsHome(definition.bodyId))){bool found=false;int work=0;
                    foreach(var candidate in PlanetStartingRegion.OppositeCandidates(definition,home)){if(PlanetStartingRegion.ExpansionPlain(definition,candidate)){enemy=candidate;found=true;break;}if(DeferredInitialization&&++work%16==0)yield return null;}
                    if(!found)throw new System.InvalidOperationException("No dry mainland found opposite the first base");OpposingHome=enemy;
                }
                if(!IsSurvival)definition.Edits.PrepareStartingAreas(home,AlliedStart?home:enemy);}
            if(System.Array.IndexOf(launchArgs,"-planet-interest-relief")>=0){definition.terrainHeight=1800;definition.oceanHeight=-35;var right=Vector3.Cross(Vector3.up,startingHome).normalized;definition.Edits.PrepareStartingAreas(startingHome,(startingHome+right*(4500/definition.radius)).normalized);}
            yield return null;
            surface=new PlanetSurface(definition);origin=new FloatingOrigin(definition.position);

            view=new GameObject(IsSurvival?"Player camera":"Planet RTS camera").AddComponent<Camera>();view.transform.SetParent(transform);view.backgroundColor=new Color(.004f,.007f,.015f);view.clearFlags=CameraClearFlags.SolidColor;

            rig=view.gameObject.AddComponent<PlanetRtsCamera>();rig.FocusDirection=startingHome;rig.Initialize(surface,origin);if(ResidentActive)lighting=new PlanetLighting(view,transform,rig.FocusDirection);

            player=new PlanetFirstPersonController(surface,origin,view,transform){CaptureCursor=!Application.isBatchMode,SurfaceSupport=direction=>Base?.PlayerEntryRadius(direction)??0};modes=new PlayerControlModeManager();

            modes.Bind(PlayerControlMode.RTS,_=>{});modes.Bind(PlayerControlMode.FirstPerson,enabled=>player.SetActive(enabled));InitializeVehicleControl();

            yield return null;
            // Keep the navigation/army fixture intact without allocating it in survival.
            if(!IsSurvival){
            int cells=6*navigationResolution*navigationResolution;

            if(SystemRuntime?.Civilisation!=null&&!SystemRuntime.Civilisation.IsHome(definition.bodyId))unitCount=0;
            capacity=currentRtsContent?Mathf.Max(Mathf.Max(4096,requestedCapacity),unitCount):unitCount;

            costs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells*5,4);units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity,UnitState.Stride);

            movement=Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));BindSurface(movement,definition);movement.SetInt("_PlanetNavRes",navigationResolution);movement.SetInt("_PlanetUnitCount",unitCount);

            movement.SetFloat("_PlanetSpacing",BattleSettings.FormationSpacing);

            int seed=movement.FindKernel("GenerateCosts");movement.SetBuffer(seed,"_PlanetCosts",costs);movement.Dispatch(seed,(cells+127)/128,1,1);

            legacyCommands=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-planet-legacy-commands")>=0;

            navigation=new PlanetPathfindingAdapter(navigationResolution,legacyCommands?1:BattleSettings.Groups,definition.radius,costs);

            if(definition.radius>=PlanetDefinition.RegionalNavigationRadius&&System.Array.IndexOf(launchArgs,"-planet-no-regional-nav")<0){navigation.EnableRegional(definition);movement.EnableKeyword("PLANET_REGIONAL_NAV");}

            yield return null;
            goal=new Vector3(1.3f,.12f,1).normalized;navigation.SetGoal(0,goal);
            if(DeferredInitialization){for(int iteration=0;iteration<navigationResolution*6;iteration+=16){navigation.Relax(0,Mathf.Min(16,navigationResolution*6-iteration));yield return null;}}
            else navigation.Relax(0,navigationResolution*6);

            int spawn=movement.FindKernel("Spawn");movement.SetBuffer(spawn,"_PlanetUnits",units);movement.SetVector("_PlanetStart",CubeSphere.Encode(rig.FocusDirection));movement.SetVector("_PlanetGoal",CubeSphere.Encode(goal));movement.Dispatch(spawn,Mathf.Max(1,(unitCount+127)/128),1,1);

            moveKernel=movement.FindKernel("Move");navigation.Regional?.Bind(movement,moveKernel);movement.SetBuffer(moveKernel,"_PlanetUnits",units);movement.SetBuffer(moveKernel,"_PlanetCosts",costs);

            if(!legacyCommands){movement.EnableKeyword("PLANET_GROUP_COMMANDS");movement.SetBuffer(moveKernel,"_Groups",navigation.Groups);commands=new PlanetArmyCommands(definition,units,unitCount);}

            yield return null;
            tank=ArmyMeshes.Tank(true);arguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size);

            arguments.SetData(new[]{new GraphicsBuffer.IndirectDrawIndexedArgs{indexCountPerInstance=tank.GetIndexCount(0),instanceCount=(uint)unitCount,startIndex=tank.GetIndexStart(0),baseVertexIndex=tank.GetBaseVertex(0)}});

            }
            biomeColours=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Biomes.Colours.Length,16);biomeColours.SetData(Biomes.Colours);

            terrainMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTerrain"));terrainMaterial.SetBuffer("_BiomeColours",biomeColours);

            if(System.Array.IndexOf(launchArgs,"-planet-flat-shading")<0){Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary").Bind(terrainMaterial);terrainMaterial.EnableKeyword("PLANET_SURFACE_TEXTURES");}

            yield return null;
            if(definition.radius>=PlanetDefinition.RegionalNavigationRadius)streamedTerrain=true;

            var args=System.Environment.GetCommandLineArgs();if(System.Array.IndexOf(args,"-planet-streamed")>=0)streamedTerrain=true;if(System.Array.IndexOf(args,"-planet-cbt")>=0){adaptiveTerrain=true;streamedTerrain=false;}if(System.Array.IndexOf(args,"-planet-uniform-terrain")>=0)adaptiveTerrain=false;

            if(streamedTerrain){streamed=new PlanetStreamedTerrain(definition){BufferLayers=terrainLodBufferLayers,MergeThreshold=1-terrainLodHysteresis};streamed.Bind(terrainMaterial);streamed.Update(view,origin.RenderPosition(definition.position),terrainErrorPixels);}

            else if(adaptiveTerrain){cbt=new PlanetCbtTerrain(definition);cbt.Bind(terrainMaterial);for(int i=0;i<14;i++)cbt.Update(view,origin.RenderPosition(definition.position),terrainErrorPixels);}

            if(cbt!=null&&System.Array.IndexOf(args,"-planet-terrain-debug")>=0){showTerrainTriangles=true;terrainMaterial.EnableKeyword("PLANET_CBT_DEBUG");}

            terrainRenderer=new PlanetTerrainRenderer(this);flightHudRenderer=new PlanetFlightHudRenderer(this);terrainMaterial.SetColor("_TintOverride",Color.clear);scaledRenderer=new ScaledSpaceRenderer(biomeColours);if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-planet-weather-control")<0)weatherRenderer=new PlanetWeatherRenderer(this);

            if(System.Array.IndexOf(args,"-planet-system-baseline")>=0){validationDistantBody=ScriptableObject.CreateInstance<PlanetDefinition>();validationDistantBody.bodyId=2;validationDistantBody.radius=10000000;validationDistantBody.position=definition.position.Add(new Double3(-40000000,10000000,-20000000));distantBodies=new[]{validationDistantBody};}
            if(SystemRuntime==null&&(distantBodies==null||distantBodies.Length==0)&&System.Array.IndexOf(args,"-planet-single-body")<0){
                systemDefinition=systemDefinition!=null?systemDefinition:Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");
                if(systemDefinition!=null){SystemRuntime=gameObject.AddComponent<PlanetSystemRuntime>();SystemRuntime.Initialize(this,systemDefinition);}
            }
            if(distantBodies==null)distantBodies=System.Array.Empty<PlanetDefinition>();

            yield return null;
            if(!IsSurvival){unitMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUnits")){enableInstancing=true};unitMaterial.SetBuffer("_PlanetUnits",units);}

            if(currentRtsContent){

                var catalog=RtsCatalog.Load();var art=RtsModelArt.Load();for(int i=0;i<catalog.units.Length;i++)if(art.unitIds[i]!=catalog.units[i].id)throw new System.InvalidOperationException("RTS catalogue and model bank type order disagree");

                var gpuTypes=new UnitArchetype[catalog.units.Length+1+PlanetVehiclePool.Capacity];var gpuTraits=new UnitTraits[catalog.units.Length+1+PlanetVehiclePool.Capacity];for(int i=0;i<catalog.units.Length;i++){gpuTypes[i]=catalog.units[i].Gpu;gpuTraits[i]=catalog.units[i].Traits;}

                var infectedType=ZombieDefinition.Archetype;if(!ClassicCountryFixture)infectedType.speed=3.5f;gpuTypes[catalog.units.Length]=infectedType;gpuTraits[catalog.units.Length]=ZombieDefinition.Traits;

                archetypes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,gpuTypes.Length,64);archetypes.SetData(gpuTypes);traits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,gpuTraits.Length,UnitTraits.Stride);traits.SetData(gpuTraits);buildingFootprints=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128,16);

                movement.EnableKeyword("PLANET_RTS_CONTENT");movement.SetBuffer(moveKernel,"_Archetypes",archetypes);movement.SetBuffer(moveKernel,"_Traits",traits);int configure=movement.FindKernel("ConfigureCatalog");movement.SetBuffer(configure,"_PlanetUnits",units);movement.SetBuffer(configure,"_Archetypes",archetypes);movement.Dispatch(configure,Mathf.Max(1,(unitCount+127)/128),1,1);commands.BindCatalog(archetypes,traits,catalog);

                catalogRenderer=new PlanetCatalogRenderer(definition,units,traits,capacity,BindInstancePrecision);

                placementProbe=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);int probe=movement.FindKernel("CheckClearance");movement.SetBuffer(probe,"_PlanetUnits",units);movement.SetBuffer(probe,"_Archetypes",archetypes);movement.SetBuffer(probe,"_PlacementOccupied",placementProbe);

            yield return null;
                Base=new PlanetRtsBase(this,surface);if(SystemRuntime?.Civilisation!=null)settlementRenderer=new SettlementRenderer(this,catalogRenderer);yield return null;
                PlanetResourceDecals.Lookup lookup=null;if(DeferredInitialization){lookup=new PlanetResourceDecals.Lookup();var prepare=lookup.Prepare(Base,definition);while(prepare.MoveNext())yield return null;}
                resourceDecals=new PlanetResourceDecals(terrainMaterial,Base,definition,lookup);yield return null;
                hud=new PlanetRtsHud(this,Base);rig.Distance=300;

                yield return null;
                PlanetEncounter.Deployment deployment=null;
                if(DeferredInitialization){deployment=new PlanetEncounter.Deployment();var prepare=deployment.Prepare(definition,rig.FocusDirection,navigationResolution);while(prepare.MoveNext())yield return null;}
                if(currentRtsContent)encounter=new PlanetEncounter(this,units,archetypes,traits,navigation,capacity,movement,ambientZombies,deployment);
                if(DeferredInitialization&&encounter!=null){yield return null;var forces=encounter.SeedInitialForces(ambientZombies);while(forces.MoveNext())yield return null;}

            yield return null;
                if(StrategyEnabled){spacecraft=new PlanetSpacecraft(this,surface,origin,view,transform);modes.Bind(PlayerControlMode.Spacecraft,enabled=>spacecraft.SetActive(enabled));player.BindShip(spacecraft);if(sessionMode==PlanetGameMode.AiTest){rig.SetFocusDirection(Base.Homes[0]);rig.Distance=650;}}

            }

            var prebuild=PrebuildSettlement();while(prebuild.MoveNext())yield return null;
            initialized=true;
            if(IsSurvival){spacecraft=new PlanetSpacecraft(this,surface,origin,view,transform);modes.Bind(PlayerControlMode.Spacecraft,enabled=>spacecraft.SetActive(enabled));player.BindShip(spacecraft);if(ResidentActive)SetFirstPerson(true);if(SystemRuntime?.ActiveWorld==null||SystemRuntime.ActiveWorld==this)DeployPersonalTestVehicles();}
            else DeployStartingTank();
        }

        public bool Interact(){if(LocalPlayerDead)return false;if(IsFirstPerson&&InteractGroundLoot())return true;if(IsFirstPerson&&InteractRespawnPoint())return true;if(IndustrialDriving)return industry.ExitVehicle();if(RemoteView)return false;if(IsDriving)return groundVehicles.Interact();if(IsFirstPerson&&groundVehicles?.Interact()==true)return true;if(IsFirstPerson&&industry?.InteractPlaced()==true)return true;if(IsFirstPerson&&playerConstruction?.InteractDoor()==true)return true;if(!IsPiloting&&!IsFirstPerson&&(hud?.TextFocused==true||hud?.ModalOpen==true))return false;if(IsPiloting){spacecraft.Rebase();Physics.SyncTransforms();if(!spacecraft.CanExitSeat(player.Collision,out var local)){if(Base!=null)Base.Notice="Pilot exit is obstructed";else SurvivalNotice="Pilot exit is obstructed";return false;}modes.Switch(PlayerControlMode.FirstPerson);player.EnterShip(spacecraft,local,Vector3.forward);spacecraft.Rebase();Base?.Rebase();Orbitals?.Tick();Physics.SyncTransforms();return true;}if(!IsFirstPerson){SetFirstPerson(true);return true;}Physics.SyncTransforms();if(Base!=null&&Base.InteractModularDoor(new Ray(view.transform.position,view.transform.forward)))return true;if(InteractStation())return true;if(spacecraft==null)return false;Physics.SyncTransforms();int action=spacecraft.FindInteraction(view.transform.position,view.transform.forward);if(action<0)return false;if(action<spacecraft.DoorCount)return spacecraft.ToggleDoor(action);SetShipControl(true);return true;}
        public void SetShipControl(bool enabled){if(RemoteView&&enabled||IsSurvival&&enabled&&(PlayerStatus.Health<=0||!HasRespawnCarrier))return;if(spacecraft==null)return;if(enabled&&!spacecraft.CockpitPowered){SurvivalNotice="Connect power to a cockpit before piloting";return;}if(enabled){player.SetActive(false);modes.Switch(PlayerControlMode.Spacecraft);}else if(IsSurvival){Interact();return;}else{rig.SetFocusDirection(spacecraft.Direction);modes.Switch(PlayerControlMode.RTS);rig.RefreshPose();}spacecraft.Rebase();Base?.Rebase();Orbitals?.Tick();Physics.SyncTransforms();}

        internal void ApplyShipRocketImpact(Vector3 direction)=>encounter?.RocketImpact(direction);
        internal bool TryTestNukeAtCursor(Vector2 screen){if(hud?.ModalOpen==true||hud?.TextFocused==true||hud?.Blocks(screen)==true||!rig.GroundPoint(screen,out var direction))return false;encounter?.Detonate(direction,craterRadius:PlanetEncounter.TestNukeCraterRadius);return encounter!=null;}

        public void SetFirstPerson(bool enabled)

        {

            if(IsSurvival&&!enabled)return;
            if(RemoteView&&enabled)return;if(IsDriving){if(!enabled)VehicleToRts();return;}if(!Ready||!enabled&&!IsFirstPerson&&!IsPiloting)return;
            if(IsPiloting&&!enabled){SetShipControl(false);return;}
            selecting=false;
            if(enabled){Base?.CancelPlacement();if(player.AttachedToShip||player.AttachedToStation){modes.Switch(PlayerControlMode.FirstPerson);player.Resume();}else{player.Enter(rig.FocusDirection,view.transform.forward);modes.Switch(PlayerControlMode.FirstPerson);}}
            else{rig.SetFocusDirection(player.Direction);modes.Switch(PlayerControlMode.RTS);rig.RefreshPose();}
            spacecraft?.Rebase();Base?.Rebase();Orbitals?.Tick();Physics.SyncTransforms();

        }

        // Spectator selection deliberately never enters unit selection, orders or placement.
        internal bool InspectAiBuilding(Vector2 position){
            if(sessionMode!=PlanetGameMode.AiTest||Base==null||hud?.ModalOpen==true||hud?.TextFocused==true||hud?.Blocks(position)==true)return false;
            bool picked=Base.Pick(position);if(!picked)Base.ClearSelection();return picked;
        }

        void InspectHearse(CargoVehicleState vehicle){var centre=PlanetRenderCentre;var matrix=view.projectionMatrix*view.worldToCameraMatrix*Matrix4x4.Translate(centre);commands?.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));Base.SelectHearse(vehicle);}
        long followedCitizen;double nextCitizenTrack,nextCorpseHover;UnityEngine.UIElements.Label corpseHover;
        public long FollowedCitizen=>followedCitizen;public void FollowCitizen(long id){followedCitizen=id;nextCitizenTrack=0;}
        void UpdateCitizenFollow(){if(modes.Mode!=PlayerControlMode.RTS){if(corpseHover!=null)corpseHover.style.display=UnityEngine.UIElements.DisplayStyle.None;return;}var keys=Keyboard.current;var mouse=Mouse.current;if(keys!=null&&(keys.wKey.isPressed||keys.aKey.isPressed||keys.sKey.isPressed||keys.dKey.isPressed)||mouse!=null&&(mouse.middleButton.isPressed||mouse.rightButton.isPressed))followedCitizen=0;if(followedCitizen!=0&&Time.unscaledTimeAsDouble>=nextCitizenTrack){nextCitizenTrack=Time.unscaledTimeAsDouble+.15;long person=followedCitizen;Base?.Citizens?.TrackPerson(person,d=>{if(followedCitizen==person&&modes.Mode==PlayerControlMode.RTS){if(d.sqrMagnitude>.5f)rig.SetFocusDirection(d);else followedCitizen=0;}});}if(mouse==null||mouse.leftButton.isPressed||mouse.leftButton.wasReleasedThisFrame||hud==null||Time.unscaledTimeAsDouble<nextCorpseHover)return;nextCorpseHover=Time.unscaledTimeAsDouble+.25;if(corpseHover==null){corpseHover=new UnityEngine.UIElements.Label{pickingMode=UnityEngine.UIElements.PickingMode.Ignore};corpseHover.style.position=UnityEngine.UIElements.Position.Absolute;corpseHover.style.backgroundColor=new Color(.02f,.03f,.05f,.95f);corpseHover.style.color=Color.white;hud.Root.Add(corpseHover);}corpseHover.style.display=UnityEngine.UIElements.DisplayStyle.None;Vector2 screen=mouse.position.ReadValue();if(hud.Blocks(screen))return;Base?.Citizens?.PickPerson(view.ScreenPointToRay(screen),(colony,person)=>{if(person.alive||modes.Mode!=PlayerControlMode.RTS)return;var death=colony.deaths.Find(d=>d.resident==person.id);corpseHover.text=person.name+(death==null?"":" � "+death.cause+" � "+Base.Civilisation.CivicDate(death.time));corpseHover.style.left=screen.x+18;corpseHover.style.top=Screen.height-screen.y+14;corpseHover.style.display=UnityEngine.UIElements.DisplayStyle.Flex;});}
        Vector2 hearseClickStart;uint inspectionSerial;
        void InspectCitizen(ColonyState colony,ResidentState person){var centre=PlanetRenderCentre;var matrix=view.projectionMatrix*view.worldToCameraMatrix*Matrix4x4.Translate(centre);commands?.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));Base.SelectCitizen(colony,person);Base.Notice=person.name;}

        void SelectionInput(Mouse mouse,Keyboard keyboard)

        {

            if(hud?.ModalOpen==true||hud?.TextFocused==true||mouse==null||!rig.InputEnabled||!Application.isFocused)return;

            Vector2 position=mouse.position.ReadValue();

            bool blocked=hud!=null&&hud.Blocks(position);if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)inspectionSerial++;
            if(mouse.leftButton.wasPressedThisFrame){hearseClickStart=position;inspectionSerial++;}
            if(mouse.leftButton.wasReleasedThisFrame&&!blocked&&(position-hearseClickStart).sqrMagnitude<25&&Base?.Placing==null&&Base?.RoadPlacing!=true&&!NuclearTargetArmed){uint request=inspectionSerial;Base?.CargoNavigation?.PickHearse(view.ScreenPointToRay(position),vehicle=>{if(request==inspectionSerial&&modes.Mode==PlayerControlMode.RTS)InspectHearse(vehicle);});Base?.Citizens?.PickPerson(view.ScreenPointToRay(position),(colony,person)=>{if(request==inspectionSerial&&modes.Mode==PlayerControlMode.RTS)InspectCitizen(colony,person);});}

            if(sessionMode==PlanetGameMode.AiTest){
                selecting=false;
                if(mouse.leftButton.wasPressedThisFrame&&!blocked)InspectAiBuilding(position);
                if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)Base?.ClearSelection();
                return;
            }
            if(commands==null||SystemRuntime?.Civilisation?.CatchingUp(definition.bodyId)==true)return;

            if(BiologicalTargetArmed){if(mouse.rightButton.wasPressedThisFrame||keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)BiologicalTargetArmed=false;else if(mouse.leftButton.wasPressedThisFrame&&!blocked&&rig.GroundPoint(position,out var bioImpact))LaunchBiologicalWeapon(bioImpact);return;}
            if(NuclearTargetArmed){if(mouse.rightButton.wasPressedThisFrame||keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)NuclearTargetArmed=false;else if(mouse.leftButton.wasPressedThisFrame&&!blocked&&rig.GroundPoint(position,out var impact))LaunchNuke(impact);return;}

            if(Base?.RoadPlacing==true){selecting=false;if(mouse.rightButton.wasPressedThisFrame||keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){Base.CancelRoadPlacement();return;}if(!blocked&&mouse.leftButton.wasPressedThisFrame&&rig.GroundPoint(position,out var roadPoint))Base.RoadClick(roadPoint);return;}
            if(Base!=null&&Base.Placing!=null){selecting=false;if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){Base.CancelPlacement();return;}if(keyboard!=null&&keyboard.rKey.wasPressedThisFrame)Base.Rotate();if(mouse.rightButton.wasPressedThisFrame){Base.CancelPlacement();return;}if(!blocked&&rig.GroundPoint(position,out var ground)){Base.Preview(ground);if(mouse.leftButton.wasPressedThisFrame)Base.Construct(ground);}return;}

            var centre=origin.RenderPosition(definition.position);

            var matrix=view.projectionMatrix*view.worldToCameraMatrix*Matrix4x4.Translate(centre);

            int operation=keyboard!=null&&(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)?2:keyboard!=null&&(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed)?1:0;

            if(mouse.leftButton.wasPressedThisFrame&&!blocked&&position.y<Screen.height-85){if(Base!=null&&Base.Pick(position)){commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));selecting=false;return;}selectionStart=position;selecting=true;}

            if(selecting&&mouse.leftButton.wasReleasedThisFrame){commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),selectionStart,position,operation,(position-selectionStart).sqrMagnitude<25);if(commands.SelectedCount>0||operation==0)Base?.ClearSelection();selecting=false;}

            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){selecting=false;Base?.ClearSelection();commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));}

            if(keyboard!=null&&keyboard.xKey.wasPressedThisFrame)commands.Stop();

        }

        internal void TerrainChanged(){if(Base!=null)RefreshBuildingCosts(Base.Buildings,true);streamed?.Update(view,PlanetRenderCentre,terrainErrorPixels,true);Base?.Rebase();spacecraft?.Rebase();Physics.SyncTransforms();}

        public static void BindSurface(ComputeShader shader,PlanetDefinition d){shader.SetFloat("_PlanetRadius",d.radius);shader.SetFloat("_PlanetHeight",d.terrainHeight);shader.SetFloat("_PlanetSeed",(d.terrainSeed%1009)*.017f);shader.SetFloat("_PlanetOcean",d.oceanHeight);shader.SetFloat("_PlanetReliefBound",d.ReliefBound);d.Edits.Bind(shader);}

        static void BindSurface(Material material,PlanetDefinition d){material.SetFloat("_PlanetRadius",d.radius);material.SetFloat("_PlanetHeight",d.terrainHeight);material.SetFloat("_PlanetSeed",(d.terrainSeed%1009)*.017f);material.SetFloat("_PlanetOcean",d.oceanHeight);material.SetFloat("_PlanetReliefBound",d.ReliefBound);d.Edits.Bind(material);}

        void Update()

        {

            if(!Ready)return;if(!ResidentActive){
                if(!dormantResourcesReleased&&Time.realtimeSinceStartupAsDouble-dormantSince>=System.Math.Max(1,SystemRuntime?.Civilisation?.Settings.dormantResourceReleaseSeconds??15)){settlementRenderer?.ReleaseDormantResources();playerConstruction?.ReleaseViewResources();dormantResourcesReleased=true;}
                return;
            }UpdateIndustry();PreparePlayerBuilding();UpdatePersonalGameplay();UpdateOrbitals();UpdateDaylightPreview();lighting?.Update();var keyboard=LocalPlayerDead?null:Keyboard.current;

            if(keyboard!=null&&Application.isFocused&&!PersonalMenuOpen&&!(hud?.TextFocused??false)){if(keyboard.numpadPlusKey.wasPressedThisFrame)ChangeSimulationSpeed(1);if(keyboard.numpadMinusKey.wasPressedThisFrame)ChangeSimulationSpeed(-1);}
            if(keyboard!=null&&keyboard.f2Key.wasPressedThisFrame)showTerrainDebug=!showTerrainDebug;

            if((cbt!=null||streamed!=null)&&keyboard!=null&&keyboard.f3Key.wasPressedThisFrame){showTerrainTriangles=!showTerrainTriangles;if(showTerrainTriangles)terrainMaterial.EnableKeyword("PLANET_CBT_DEBUG");else terrainMaterial.DisableKeyword("PLANET_CBT_DEBUG");}

            if(sessionMode==PlanetGameMode.AiTest&&keyboard!=null&&!IsPiloting&&!(hud?.TextFocused??false)){if(keyboard.digit1Key.wasPressedThisFrame)FocusFaction(0);if(keyboard.digit2Key.wasPressedThisFrame)FocusFaction(1);}

            if(keyboard!=null&&keyboard.f8Key.wasPressedThisFrame&&!(hud?.TextFocused??false))DeployVehiclePrototypes();
            if(keyboard!=null&&keyboard.hKey.wasPressedThisFrame&&!IndustrialDriving&&!PersonalMenuOpen&&!(hud?.TextFocused??false))SetShipControl(!IsPiloting);

            if(!LocalPlayerDead&&keyboard!=null&&keyboard.nKey.wasPressedThisFrame&&Mouse.current!=null&&!IsPiloting)TryTestNukeAtCursor(Mouse.current.position.ReadValue());

            bool interact=!PersonalMenuOpen&&keyboard!=null&&keyboard.fKey.wasPressedThisFrame;bool rts=keyboard!=null&&keyboard.tabKey.wasPressedThisFrame;

            UpdateCitizenFollow();if(modes.Mode==PlayerControlMode.RTS){bool input=rig.InputEnabled;rig.InputEnabled=input&&!(hud?.ModalOpen??false)&&!(hud?.TextFocused??false);rig.Step(Mouse.current!=null&&(hud?.Blocks(Mouse.current.position.ReadValue())??false));rig.InputEnabled=input;}Orbitals?.Tick();spacecraft?.StepUnpiloted(Time.deltaTime);if(IsPiloting&&!LocalPlayerDead)spacecraft.Step(Time.deltaTime);else if(IsFirstPerson){spacecraft?.Rebase();respawnView?.RebaseShipContacts();Base?.Rebase();playerConstruction?.RebaseColliders();Physics.SyncTransforms();if(AutomaticPlayerStep&&!LocalPlayerDead)player.Step(Time.deltaTime,PersonalMenuOpen||PlayerBlueprintRadialOpen?(Vector2?)Vector2.zero:null,PersonalMenuOpen||PlayerBlueprintRadialOpen?(Vector2?)Vector2.zero:null);Orbitals?.Tick();}if(groundVehicles!=null)spacecraft?.Rebase();groundVehicles?.Step(VehicleMotor.LocalStepDuration(Time.deltaTime));SyncVehicleInteractionState();spacecraft?.Rebase();Base?.Rebase();Orbitals?.Tick();if(IsFirstPerson&&spacecraft!=null){Physics.SyncTransforms();shipInteraction=spacecraft.FindInteraction(view.transform.position,view.transform.forward);}if(rts)SetFirstPerson(false);else if(interact)Interact();encounter?.UpdatePresentation();if(!LocalPlayerDead)spacecraft?.StepWeapons(SimulationDelta);var mouse=Mouse.current;

            respawnView?.Tick();RenderGroundLoot();UpdatePlayerBuilding();
            bool placing=Base!=null&&(Base.Placing!=null||Base.RoadPlacing);

            if(modes.Mode==PlayerControlMode.RTS)SelectionInput(mouse,keyboard);

            FormationInput(mouse,placing);

            SimulationBeginForValidation?.Invoke();

            // The selected RTS world must keep executing orders when its camera
            // zooms above the flight activation altitude. Other worlds still sleep.
            SimulationActive=modes.Mode==PlayerControlMode.RTS||SystemRuntime==null||SystemRuntime.Settings.ShouldSimulate(definition,ObserverPosition,SimulationActive);
            if(SimulationActive&&navigation!=null){if(legacyCommands)navigation.Relax(0,8);else navigation.RelaxPending(8);}

            if(!IsSurvival){
            movement.SetBuffer(moveKernel,"_PlanetField",navigation.Global);movement.SetVector("_PlanetGoal",CubeSphere.Encode(goal));movement.SetFloat("_PlanetSpeed",unitSpeed);movement.SetFloat("_PlanetDt",BattleSettings.Tick);

            }
            uint previousTick=SimulationTicks;accumulator=Paused||!SimulationActive?0:Mathf.Min(accumulator+SimulationDelta,BattleSettings.Tick*4*Mathf.Max(1,SimulationSpeed));while(accumulator>=BattleSettings.Tick){encounter?.BeforeTick();if(encounter!=null)encounter.MoveUnits();else if(unitCount>0)movement.Dispatch(moveKernel,(unitCount+127)/128,1,1);encounter?.AfterTick();if(!(SystemRuntime?.Civilisation?.CatchingUp(definition.bodyId)??false))Base?.Tick(BattleSettings.Tick);accumulator-=BattleSettings.Tick;SimulationTicks++;}SimulationEndForValidation?.Invoke();Base?.Rebase();resourceDecals?.Update();hud?.Tick();if(hud!=null){if(headquartersHud==null)headquartersHud=new PlanetHeadquartersHud(this);headquartersHud.Tick();groundVehicles?.TickMarkers();}

            spacecraft?.RenderTrajectory();spacecraft?.RenderWeapons();spacecraft?.RenderLanding();flightHudRenderer?.SetActive(IsPiloting);var vp=GL.GetGPUProjectionMatrix(view.projectionMatrix,true)*view.worldToCameraMatrix;var centre=origin.RenderPosition(definition.position);var bounds=new Bounds(centre,Vector3.one*(definition.radius+definition.ReliefBound)*2);

            terrainMaterial.SetFloat("_PlanetCameraAboveOcean",RenderToBody(view.transform.position).Magnitude>definition.radius+(double)definition.oceanHeight+.1?1:0);BindRenderingPrecision(terrainMaterial);PrepareTerrainPrecision();terrainMaterial.SetFloat("_PlanetPrecisionEnabled",definition.PrecisionMode==PlanetPrecisionMode.CameraRelative?0:1);terrainMaterial.SetVectorArray("_PlanetPrecisionUv",terrainPrecisionUv);terrainMaterial.SetVectorArray("_PlanetPrecisionBase",terrainPrecisionBase);
            BindSurface(terrainMaterial,definition);terrainMaterial.SetMatrix("_PlanetVP",vp);terrainMaterial.SetVector("_PlanetCenter",centre);

            int res=rig.Distance<definition.radius?128:rig.Distance<definition.radius*3?64:32;terrainMaterial.SetInteger("_PlanetMeshRes",res);terrainMaterial.SetInteger("_OceanOnly",0);

            // Explicit per-draw bindings keep procedural geometry independent of HDRP's material batching.

            for(int blockIndex=0;blockIndex<2;blockIndex++){var block=blockIndex==0?landProperties:oceanProperties;block.SetFloat("_PlanetPrecisionEnabled",definition.PrecisionMode==PlanetPrecisionMode.CameraRelative?0:1);block.SetVectorArray("_PlanetPrecisionUv",terrainPrecisionUv);block.SetVectorArray("_PlanetPrecisionBase",terrainPrecisionBase);block.SetMatrix("_PlanetVP",vp);block.SetVector("_PlanetCenter",centre);block.SetFloat("_PlanetRadius",definition.radius);block.SetFloat("_PlanetHeight",definition.terrainHeight);block.SetFloat("_PlanetOcean",definition.oceanHeight);block.SetFloat("_PlanetSeed",(definition.terrainSeed%1009)*.017f);block.SetInteger("_PlanetMeshRes",res);if(streamed!=null)streamed.Bind(block);if(cbt!=null){block.SetBuffer("_CbtTriangles",cbt.Triangles);block.SetInteger("_CbtGrid",cbt.Grid);}}

            landProperties.SetInteger("_OceanOnly",0);



            renderResolution=res;

            var detailFocus=streamed!=null||cbt!=null?TerrainDetailFocus:null;
            if(streamed!=null){streamed.BufferLayers=terrainLodBufferLayers;streamed.MergeThreshold=1-terrainLodHysteresis;streamed.Update(view,centre,terrainErrorPixels,detailFocus:detailFocus);}

            else if(cbt!=null){cbt.Update(view,centre,terrainErrorPixels,refine:previousTick!=SimulationTicks||(detailFocus.HasValue&&modes.Mode!=PlayerControlMode.RTS),detailFocus:detailFocus);if(showTerrainDebug)cbt.RequestTelemetry();}

            oceanProperties.SetInteger("_OceanOnly",1);

            groundVehicles?.Render();if(catalogRenderer!=null)catalogRenderer.Render(view,centre,unitCount);else if(unitCount>0){BindSurface(unitMaterial,definition);BindInstancePrecision(unitMaterial);unitMaterial.SetMatrix("_PlanetVP",vp);unitMaterial.SetVector("_PlanetCenter",centre);Graphics.RenderMeshIndirect(new RenderParams(unitMaterial){camera=view,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},tank,arguments);}

            scaledRenderer.Render(view,origin,distantBodies,definition,SystemRuntime?.DistantTerrain);
            SystemRuntime?.UpdateSky();settlementRenderer?.Update();

        }

        internal void RenderTerrain(CommandBuffer cmd){

            SystemRuntime?.DrawSky(cmd);

            if(terrainMaterial==null)return;int pass=terrainMaterial.FindPass("PlanetLighting");

            if(streamed!=null){cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,streamed.DrawArguments,0,landProperties);cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,streamed.DrawArguments,0,oceanProperties);}

            else if(cbt!=null){cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,cbt.DrawArguments,0,landProperties);cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,cbt.DrawArguments,0,oceanProperties);}

            else{cmd.DrawProcedural(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,36*renderResolution*renderResolution,1,landProperties);cmd.DrawProcedural(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,36*renderResolution*renderResolution,1,oceanProperties);}

            settlementRenderer?.Draw(cmd);encounter?.DrawGunfire(cmd);
        }

        internal void RenderWeather(UnityEngine.Rendering.HighDefinition.CustomPassContext ctx){if(weatherRenderer!=null)weatherRenderer.Draw(ctx,scaledRenderer);else scaledRenderer?.Draw(ctx.cmd);}
        internal bool TryWeatherCabin(out Vector3 eye,out Quaternion worldToCabin){
            eye=Vector3.zero;worldToCabin=Quaternion.identity;if(spacecraft==null)return false;if(IsPiloting&&PilotCameraSettings!=null&&!PilotCameraSettings.maskWeatherInsideCabin)return false;
            eye=spacecraft.RenderToLocal(view.transform.position);worldToCabin=Quaternion.Inverse(spacecraft.Rotation);return spacecraft.PressurizedAt(eye);
        }

        void OnGUI()

        {if(!ResidentActive)return;

            if(Prebuilding){DrawPrebuildProgress();return;}DrawFormationPreview();
            if(!Ready)return;

            playerConstruction?.DrawHud();
            if(IsDriving){groundVehicles?.DrawHud();}
            else if(IsPiloting){GUI.Label(new Rect(12,12,1100,80),$"CAPITAL SHIP · {spacecraft.SpeedReadout}\nW/S thrust · A/D strafe · Space land/take off · R/C climb/descend · V flight assist · mouse pitch/yaw · Q/E roll · Shift boost · Hold K {PlanetSpacecraft.CruiseSpeedReadout} (space) · J dock/undock · G landing gear · B brake · L orbit attitude lock · Left mouse fire rocket · F leave pilot seat");}

            else if(modes.Mode==PlayerControlMode.FirstPerson){GUI.Label(new Rect(Screen.width*.5f-4,Screen.height*.5f-10,20,20),"+");GUI.Label(new Rect(12,12,900,60),$"Health {player.Environment.Health:F0}   Oxygen {player.Environment.Oxygen:F0}%   Inventory: repair tool, suit oxygen\nWASD walk • mouse look • Space jump • F interact • I inventory");}

            else if(hud==null) GUI.Label(new Rect(12,12,1300,80),"PLANET DEVELOPMENT • WASD / middle drag pan • wheel zoom • Q/E rotate • F first person\nClick / drag select • Shift add • Ctrl remove • right-click move • X stop • Esc clear • F2 terrain statistics • F3 wireframe\n"+(commands?.Status??"Legacy all-tank commands")+" • Production combat, construction, fog and vehicles are pending migration.");

            Orbitals?.DrawHud();if(InteractionPrompt.Length>0)GUI.Label(new Rect(Screen.width*.5f-200,Screen.height*.65f,600,40),InteractionPrompt);
            if(selecting&&Mouse.current!=null){var end=Mouse.current.position.ReadValue();var rect=Rect.MinMaxRect(Mathf.Min(selectionStart.x,end.x),Screen.height-Mathf.Max(selectionStart.y,end.y),Mathf.Max(selectionStart.x,end.x),Screen.height-Mathf.Min(selectionStart.y,end.y));GUI.Box(rect,GUIContent.none);}

            if(showTerrainDebug)GUI.Label(new Rect(12,90,1100,45),streamed!=null?$"Streamed terrain • {streamed.ResidentPatches} visible / {streamed.GpuLeafCount} leaf patches • level {streamed.DeepestLevel} • {streamed.BufferBytes/1024f:F0} KiB buffers • {(streamed.BudgetLimited?"budget limited":"within detail target")}\nRadius {definition.radius:F0} m • navigation cells {navigation?.CellCount??0} • tick {SimulationTicks}":cbt!=null?$"CBT {(cbt.Converged?"settled":"refining")} • active {cbt.ActiveLeaves} / visible {cbt.VisibleLeaves} / depth-limited {cbt.SaturatedLeaves} • {cbt.BufferBytes/1048576f:F2} MiB terrain buffers\nNavigation cells {navigation?.CellCount??0} • tick {SimulationTicks} • origin sectors {origin.Origin.sectorX},{origin.Origin.sectorY},{origin.Origin.sectorZ}":"Uniform terrain baseline • adaptiveTerrain enables CBT");

        }

        internal UniversePosition ObserverPosition=>origin.Origin.Add(new Double3(view.transform.position));
        internal void FollowCelestialFrame(Double3 observerOffset){origin.Anchor(definition.position.Add(observerOffset));spacecraft?.FollowCelestialFrame();}
        internal void InitializeNow(){var steps=InitializeSteps();while(steps.MoveNext()){} }
        internal System.Collections.IEnumerator InitializeOverFrames(){
            int step=0;var steps=InitializeSteps();while(true){var timer=System.Diagnostics.Stopwatch.StartNew();bool more=steps.MoveNext();timer.Stop();SystemRuntime.RecordPreparationStep(timer.Elapsed.TotalMilliseconds,step++);HideResidentChildren();if(!more)break;yield return null;}
        }
        readonly System.Collections.Generic.Dictionary<GameObject,bool> dormantPresentation=new System.Collections.Generic.Dictionary<GameObject,bool>();
        void HideResidentChildren(){
            for(int i=0;i<transform.childCount;i++){
                var child=transform.GetChild(i).gameObject;if(child.GetComponent<PlanetDevelopment>()!=null)continue;
                if(!ResidentActive){if(!dormantPresentation.ContainsKey(child))dormantPresentation.Add(child,child.activeSelf);child.SetActive(false);}
                else if(dormantPresentation.TryGetValue(child,out bool visible))child.SetActive(visible);
            }
            if(ResidentActive)dormantPresentation.Clear();
        }
        double dormantSince;bool dormantResourcesReleased;
        internal void SetResidentActive(bool value){
            if(ResidentActive==value)return;ResidentActive=value;accumulator=0;dormantSince=Time.realtimeSinceStartupAsDouble;dormantResourcesReleased=false;
            if(!value){spacecraft?.ReleaseReentryView();StopDaylightPreview();selecting=false;formationDragging=false;weatherRenderer?.ReleaseView();flightHudRenderer?.SetActive(false);spacecraft?.SetActive(false);lighting?.Dispose();lighting=null;}
            HideResidentChildren();
            if(value&&Ready){Base?.FlushConstructionCosts();lighting=new PlanetLighting(view,transform,rig.FocusDirection);if(SystemRuntime!=null){lighting.Sun.transform.rotation=SystemRuntime.SunRotation;lighting.Update();}}
        }

        void OnDestroy(){shipyard?.Dispose();DisposeInventoryUi();DisposeRespawn();industry?.Dispose();DisposeProductionProbe();DisposePersonalGameplay();groundVehicles?.Dispose();weatherRenderer?.Dispose();Orbitals?.Dispose();settlementRenderer?.Dispose();headquartersHud?.Dispose();spacecraft?.Dispose();terrainRenderer?.Dispose();flightHudRenderer?.Dispose();lighting?.Dispose();encounter?.Dispose();resourceDecals?.Dispose();placementProbe?.Dispose();hud?.Dispose();Base?.Dispose();catalogRenderer?.Dispose();archetypes?.Dispose();traits?.Dispose();buildingFootprints?.Dispose();commands?.Dispose();player?.Dispose();streamed?.Dispose();cbt?.Dispose();scaledRenderer?.Dispose();navigation?.Dispose();costs?.Dispose();units?.Dispose();arguments?.Dispose();biomeColours?.Dispose();ArmyLifetime.Destroy(movement);TerrainSurfaceLibrary.Unbind(terrainMaterial);ArmyLifetime.Destroy(terrainMaterial);ArmyLifetime.Destroy(unitMaterial);ArmyLifetime.Destroy(tank);ArmyLifetime.Destroy(validationDistantBody);if(ownsDefinition)ArmyLifetime.Destroy(definition);if(view!=null)ArmyLifetime.Destroy(view.gameObject);}

    }

}





