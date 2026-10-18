using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.Rendering;

namespace MassiveArmy.Planetary

{

    public enum PlanetGameMode { Rts, Swarm, AiTest }

    /// <summary>Opt-in migration fixture; does not modify the flat RTS entry scene.</summary>

    public sealed class PlanetDevelopment : MonoBehaviour

    {

        public bool deformTerrainOnNuke=true;
        public PlanetGameMode sessionMode;public int requestedCapacity,ambientZombies=256,terrainSeed=73129;

        internal PlanetPathfindingAdapter NavigationForValidation=>navigation;

        internal System.Action SimulationBeginForValidation,SimulationEndForValidation;

        public bool Paused,AttackMoveArmed,NuclearTargetArmed;public System.Action returnToMenu;

        public string ModeStatus=>sessionMode.ToString()+" Ã‚Â· Radius "+(definition!=null?(definition.radius/1000).ToString("0.#"):"6")+" km Ã‚Â· "+(encounter?.Status??"Planetary base operations");

        PlanetEncounter encounter;PlanetLighting lighting;PlanetSpacecraft spacecraft;PlanetHeadquartersHud headquartersHud;
        internal PlanetHeadquartersHud HeadquartersForValidation=>headquartersHud;

        public bool StrategyEnabled {get;private set;}
        internal Vector3 OpposingHome {get;private set;}

        public void FocusFaction(int team){if(Base==null||team<0||team>1)return;rig.SetFocusDirection(Base.Homes[team]);rig.Distance=300;}

        internal PlanetSpacecraft ShipForValidation=>spacecraft;
        internal PlanetFirstPersonController PlayerForValidation=>player;int shipInteraction=-1;internal bool AutomaticPlayerStep=true;
        public string InteractionPrompt=>IsPiloting?"F - LEAVE PILOT SEAT":IsFirstPerson&&spacecraft!=null?spacecraft.InteractionPrompt(shipInteraction):"";

        public bool IsPiloting=>modes!=null&&modes.Mode==PlayerControlMode.Spacecraft;

        internal PlanetLighting LightingForValidation=>lighting;

        internal PlanetEncounter EncounterForValidation=>encounter;

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

        [Min(0)]public float configuredRadius=PlanetDefinition.DefaultRadius;

        public PlanetStreamedTerrain StreamedTerrain=>streamed;

        PlanetStreamedTerrain streamed;PlanetTerrainRenderer terrainRenderer;PlanetFlightHudRenderer flightHudRenderer;int renderResolution;

        public bool currentRtsContent=true;

        [Tooltip("GPU pressure and predictive traffic steering; measured costs are in TestResults/Planetary/Crowd/README.md.")]public bool restoredCrowd=true;

        [Range(.25f,8)]public float terrainErrorPixels=2;

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

        internal GraphicsBuffer UnitsForValidation=>units;

        GraphicsBuffer placementProbe;readonly uint[] occupied=new uint[1];

        public bool IsFootprintClear(Vector3 direction,float radius,bool air=false)

        {

            if(unitCount==0)return true;if(placementProbe==null)return false;occupied[0]=0;placementProbe.SetData(occupied);int probe=movement.FindKernel("CheckClearance");movement.SetVector("_PlacementDirection",direction);movement.SetFloat("_PlacementRadius",radius);movement.SetInt("_PlacementAir",air?1:0);movement.Dispatch(probe,(unitCount+127)/128,1,1);placementProbe.GetData(occupied);return occupied[0]==0;

        }

        internal Vector3 InterestDirection=>IsPiloting?spacecraft.Direction:IsFirstPerson?player.Direction:rig.FocusDirection;

        internal float InterestRadius=>IsFirstPerson?600:Mathf.Clamp(rig.Distance*1.5f,600,1200);

        public bool IsFirstPerson=>modes!=null&&modes.Mode==PlayerControlMode.FirstPerson;

        public PlanetArmyCommands Commands=>commands;

        public Vector3 PlanetRenderCentre=>origin.RenderPosition(definition.position);
        public Vector3 RenderBodyPosition(Double3 bodyPosition)=>origin.RenderPosition(definition.position.Add(bodyPosition));
        public Double3 RenderToBody(Vector3 renderPosition)=>origin.RenderToUniverse(renderPosition).RelativeTo(definition.position);

        public bool LaunchNuke(Vector3 direction){NuclearTargetArmed=false;bool launched=encounter!=null&&encounter.LaunchNuke(Base.Selected,direction);Base.Notice=launched?"NUCLEAR MISSILE LAUNCHED Ã‚Â· 12 seconds to impact":"Launch unavailable: check range, fuel, isotopes, technology and silo cooldown";return launched;}

        public bool OrderSelected(Vector3 direction,bool attackMove=false)

        {

            if(sessionMode==PlanetGameMode.AiTest)return false;
            var cell=CubeSphere.Cell(definition.bodyId,direction,navigationResolution);

            bool land=surface.SampleHeight(navigation.Regional!=null?direction:CubeSphere.Direction(cell,navigationResolution))>=definition.oceanHeight;

            return commands!=null&&commands.Move(navigation,direction,m=>m==3||(m==4?!land:land),attackMove||AttackMoveArmed);

        }

        public bool TrySpawnUnit(int type,Vector3 direction,int team=0)

        {

            if(Base==null||unitCount>=capacity)return false;var d=Base.Catalog.units[type];bool land=surface.SampleHeight(navigation.Regional!=null?direction:CubeSphere.Direction(CubeSphere.Cell(definition.bodyId,direction,navigationResolution),navigationResolution))>=definition.oceanHeight;

            if(d.movement!=MovementClass.Air&&(d.movement==MovementClass.Water?land:!land))return false;

            if(d.movement!=MovementClass.Air){var cellDirection=navigation.Regional!=null?direction:CubeSphere.Direction(CubeSphere.Cell(definition.bodyId,direction,navigationResolution),navigationResolution);foreach(var b in Base.Buildings){float radius=Mathf.Sqrt(b.definition.footprintWidth*b.definition.footprintWidth+b.definition.footprintDepth*b.definition.footprintDepth)*4+2+(navigation.Regional!=null?d.radius:definition.radius/navigationResolution);if(!b.removed&&CubeSphere.ArcDistance(cellDirection,CubeSphere.Decode(b.position),definition.radius)<radius)return false;}}

            if(!IsFootprintClear(direction,d.radius,d.movement==MovementClass.Air))return false;

            int spawn=movement.FindKernel("SpawnCatalog");movement.SetBuffer(spawn,"_PlanetUnits",units);movement.SetBuffer(spawn,"_Archetypes",archetypes);movement.SetInt("_PlanetSpawnSlot",unitCount);movement.SetInt("_PlanetSpawnType",type);movement.SetInt("_PlanetSpawnTeam",team);movement.SetInt("_PlanetSpawnGroup",team==1?16:sessionMode==PlanetGameMode.AiTest?15:0);movement.SetVector("_PlanetStart",CubeSphere.Encode(direction));movement.Dispatch(spawn,1,1,1);

            unitCount++;movement.SetInt("_PlanetUnitCount",unitCount);commands.SetCount(unitCount);return true;

        }

        internal void SetArmyCount(int count){unitCount=count;movement.SetInt("_PlanetUnitCount",count);commands.SetCount(count);}

        public void RefreshBuildingCosts(System.Collections.Generic.List<RtsBuilding> buildings,bool asynchronous=false)

        {

            int cells=navigation.CellCount;int generate=movement.FindKernel("GenerateCosts");movement.Dispatch(generate,(cells+127)/128,1,1);

            var footprints=new Vector4[buildings.Count];for(int i=0;i<buildings.Count;i++){var b=buildings[i];var d=CubeSphere.Decode(b.position);footprints[i]=new Vector4(d.x,d.y,d.z,b.removed||b.definition.id=="gate"&&b.gateOpen?0:Mathf.Sqrt(b.definition.footprintWidth*b.definition.footprintWidth+b.definition.footprintDepth*b.definition.footprintDepth)*4+2);}

            buildingFootprints.SetData(footprints);int stamp=movement.FindKernel("StampBuildings");movement.SetBuffer(stamp,"_PlanetBuildings",buildingFootprints);movement.SetBuffer(stamp,"_PlanetCosts",costs);movement.SetInt("_PlanetBuildingCount",buildings.Count);movement.Dispatch(stamp,(cells+127)/128,1,1);navigation.SetRegionalBuildings(buildingFootprints,buildings.Count);navigation.Regional?.Bind(movement,moveKernel);if(asynchronous)navigation.InvalidateAsync();else navigation.Invalidate();

        }

        public PlanetCbtTerrain Cbt=>cbt;

        public Camera View=>view;

        public PlanetRtsCamera Rig=>rig;

        public uint SimulationTicks {get;private set;}

        bool initialized;internal bool DeferredInitialization;
        public bool Ready=>initialized;

        void Start(){if(!DeferredInitialization)InitializeNow();}
        System.Collections.IEnumerator InitializeSteps()
        {
            if(Ready)yield break;
            var launchArgs=System.Environment.GetCommandLineArgs();
            if(System.Array.IndexOf(launchArgs,"-planet-deform-terrain")>=0)deformTerrainOnNuke=true;
            if(System.Array.IndexOf(launchArgs,"-planet-no-deform-terrain")>=0)deformTerrainOnNuke=false;

            StrategyEnabled=true;foreach(var arg in launchArgs)if((arg.EndsWith("-validation")&&arg!="-planet-gameplay-validation"&&arg!="-planet-menu-validation"&&arg!="-planet-ship-validation"&&arg!="-planet-landing-validation"&&arg!="-planet-hq-terrain-validation"&&arg!="-planet-system-validation"&&arg!="-galaxy-validation"&&arg!="-civilisation-validation"&&arg!="-planet-economy-ai-validation"&&arg!="-planet-household-validation")||arg=="-planet-benchmark"||arg=="-planet-fixture")StrategyEnabled=false;

            if(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0)configuredRadius=0;

            if(System.Array.IndexOf(launchArgs,"-planet-restored-crowd")>=0)restoredCrowd=true;

            if(System.Array.IndexOf(launchArgs,"-planet-legacy-steering")>=0)restoredCrowd=false;

            if(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0)currentRtsContent=false;

            if(System.Array.IndexOf(launchArgs,"-planet-rts")>=0)currentRtsContent=true;

            if(System.Array.IndexOf(launchArgs,"-planet-visual-validation")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-rts-validation")>=0)ambientZombies=0;

            if(System.Array.IndexOf(launchArgs,"-planet-crowd-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.Rts;ambientZombies=0;requestedCapacity=8192;configuredRadius=2000;streamedTerrain=true;}

            if(System.Array.IndexOf(launchArgs,"-planet-ai-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.AiTest;ambientZombies=0;requestedCapacity=8192;configuredRadius=0;}

            if(System.Array.IndexOf(launchArgs,"-planet-interest-validation")>=0){currentRtsContent=true;sessionMode=PlanetGameMode.AiTest;ambientZombies=0;requestedCapacity=1048576;configuredRadius=2000;streamedTerrain=true;}

            oceanProperties=new MaterialPropertyBlock();landProperties=new MaterialPropertyBlock();

            // Compact validation fixtures deliberately use their original generated definition.
            if(configuredRadius==0&&(System.Array.IndexOf(launchArgs,"-planet-fixture")>=0||System.Array.IndexOf(launchArgs,"-planet-benchmark")>=0||System.Array.IndexOf(launchArgs,"-planet-smoke")>=0||System.Array.IndexOf(launchArgs,"-planet-ai-validation")>=0))definition=null;
            if(definition!=null&&!ownsDefinition)ConfigureDefinition(definition);
            if(definition==null){definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.oceanHeight=-8;definition.terrainSeed=terrainSeed;ownsDefinition=true;}

            if(System.Array.IndexOf(launchArgs,"-planet-interest-validation")>=0||System.Array.IndexOf(launchArgs,"-planet-crowd-validation")>=0){definition.terrainHeight=0;definition.oceanHeight=-8;}

            if(System.Array.IndexOf(launchArgs,"-planet-legacy-terrain")>=0)definition.stampTerrain=false;
            if(System.Array.IndexOf(launchArgs,"-planet-stamp-terrain")>=0)definition.stampTerrain=true;
            int radiusArg=System.Array.IndexOf(launchArgs,"-planet-radius");

            if(radiusArg>=0&&radiusArg+1<launchArgs.Length&&float.TryParse(launchArgs[radiusArg+1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float requestedRadius))configuredRadius=Mathf.Clamp(requestedRadius,100,PlanetDefinition.TargetRadius);

            if(configuredRadius>0){if(!ownsDefinition){definition=Instantiate(definition);ownsDefinition=true;}definition.radius=configuredRadius;definition.gravitationalParameter=9.81*definition.radius*definition.radius;navigationResolution=Mathf.Clamp(Mathf.CeilToInt(definition.radius/15.625f),32,128);}

            var startingHome=new Vector3(1,.12f,1.3f).normalized;
            if(configuredRadius>=PlanetDefinition.RegionalNavigationRadius&&System.Array.IndexOf(launchArgs,"-planet-interest-validation")<0&&System.Array.IndexOf(launchArgs,"-planet-crowd-validation")<0){if(SystemRuntime==null){definition.terrainHeight=definition.radius>=20000?1800:240;definition.oceanHeight=-35;}if(definition.radius>=20000)definition.atmosphereHeight=Mathf.Max(10000,definition.atmosphereHeight);var home=startingHome;
                if(definition.radius>=20000){
                    if(DeferredInitialization){bool found=false;foreach(var candidate in PlanetStartingRegion.Candidates(definition,startingHome)){if(PlanetStartingRegion.ConnectedPlain(definition,candidate)){home=candidate;found=true;break;}yield return null;}if(!found)throw new System.InvalidOperationException("No connected dry starting region found");}
                    else home=PlanetStartingRegion.Select(definition,startingHome);
                }
                startingHome=home;var right=Vector3.Cross(Vector3.up,home).normalized;var enemy=(home+right*(4500/definition.radius)).normalized;
                if(StrategyEnabled&&sessionMode==PlanetGameMode.AiTest&&(SystemRuntime?.Civilisation==null||SystemRuntime.Civilisation.IsHome(definition.bodyId))){bool found=false;int work=0;
                    foreach(var candidate in PlanetStartingRegion.OppositeCandidates(definition,home)){if(PlanetStartingRegion.ExpansionPlain(definition,candidate)){enemy=candidate;found=true;break;}if(DeferredInitialization&&++work%16==0)yield return null;}
                    if(!found)throw new System.InvalidOperationException("No dry mainland found opposite the first AI base");OpposingHome=enemy;
                }
                definition.Edits.PrepareStartingAreas(home,enemy);}
            if(System.Array.IndexOf(launchArgs,"-planet-interest-relief")>=0){definition.terrainHeight=1800;definition.oceanHeight=-35;var right=Vector3.Cross(Vector3.up,startingHome).normalized;definition.Edits.PrepareStartingAreas(startingHome,(startingHome+right*(4500/definition.radius)).normalized);}
            yield return null;
            surface=new PlanetSurface(definition);origin=new FloatingOrigin(definition.position);

            view=new GameObject("Planet RTS camera").AddComponent<Camera>();view.transform.SetParent(transform);view.backgroundColor=new Color(.004f,.007f,.015f);view.clearFlags=CameraClearFlags.SolidColor;

            rig=view.gameObject.AddComponent<PlanetRtsCamera>();rig.FocusDirection=startingHome;rig.Initialize(surface,origin);if(ResidentActive)lighting=new PlanetLighting(view,transform,rig.FocusDirection);

            player=new PlanetFirstPersonController(surface,origin,view,transform){CaptureCursor=!Application.isBatchMode};modes=new PlayerControlModeManager();

            modes.Bind(PlayerControlMode.RTS,_=>{});modes.Bind(PlayerControlMode.FirstPerson,enabled=>player.SetActive(enabled));

            yield return null;
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

            biomeColours=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Biomes.Colours.Length,16);biomeColours.SetData(Biomes.Colours);

            terrainMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTerrain"));terrainMaterial.SetBuffer("_BiomeColours",biomeColours);

            if(System.Array.IndexOf(launchArgs,"-planet-flat-shading")<0){Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary").Bind(terrainMaterial);terrainMaterial.EnableKeyword("PLANET_SURFACE_TEXTURES");}

            yield return null;
            if(definition.radius>=PlanetDefinition.RegionalNavigationRadius)streamedTerrain=true;

            var args=System.Environment.GetCommandLineArgs();if(System.Array.IndexOf(args,"-planet-streamed")>=0)streamedTerrain=true;if(System.Array.IndexOf(args,"-planet-cbt")>=0){adaptiveTerrain=true;streamedTerrain=false;}if(System.Array.IndexOf(args,"-planet-uniform-terrain")>=0)adaptiveTerrain=false;

            if(streamedTerrain){streamed=new PlanetStreamedTerrain(definition);streamed.Bind(terrainMaterial);streamed.Update(view,origin.RenderPosition(definition.position),terrainErrorPixels);}

            else if(adaptiveTerrain){cbt=new PlanetCbtTerrain(definition);cbt.Bind(terrainMaterial);for(int i=0;i<14;i++)cbt.Update(view,origin.RenderPosition(definition.position),terrainErrorPixels);}

            if(cbt!=null&&System.Array.IndexOf(args,"-planet-terrain-debug")>=0){showTerrainTriangles=true;terrainMaterial.EnableKeyword("PLANET_CBT_DEBUG");}

            terrainRenderer=new PlanetTerrainRenderer(this);flightHudRenderer=new PlanetFlightHudRenderer(this);terrainMaterial.SetColor("_TintOverride",Color.clear);scaledRenderer=new ScaledSpaceRenderer(biomeColours);

            if(System.Array.IndexOf(args,"-planet-system-baseline")>=0){validationDistantBody=ScriptableObject.CreateInstance<PlanetDefinition>();validationDistantBody.bodyId=2;validationDistantBody.radius=10000000;validationDistantBody.position=definition.position.Add(new Double3(-40000000,10000000,-20000000));distantBodies=new[]{validationDistantBody};}
            if(SystemRuntime==null&&(distantBodies==null||distantBodies.Length==0)&&System.Array.IndexOf(args,"-planet-single-body")<0){
                systemDefinition=systemDefinition!=null?systemDefinition:Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");
                if(systemDefinition!=null){SystemRuntime=gameObject.AddComponent<PlanetSystemRuntime>();SystemRuntime.Initialize(this,systemDefinition);}
            }
            if(distantBodies==null)distantBodies=System.Array.Empty<PlanetDefinition>();

            yield return null;
            unitMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUnits")){enableInstancing=true};unitMaterial.SetBuffer("_PlanetUnits",units);

            if(currentRtsContent){

                var catalog=RtsCatalog.Load();var art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");for(int i=0;i<catalog.units.Length;i++)if(art.unitIds[i]!=catalog.units[i].id)throw new System.InvalidOperationException("RTS catalogue and model bank type order disagree");

                var gpuTypes=new UnitArchetype[catalog.units.Length+1];var gpuTraits=new UnitTraits[catalog.units.Length+1];for(int i=0;i<catalog.units.Length;i++){gpuTypes[i]=catalog.units[i].Gpu;gpuTraits[i]=catalog.units[i].Traits;}

                gpuTypes[catalog.units.Length]=ZombieDefinition.Archetype;gpuTraits[catalog.units.Length]=ZombieDefinition.Traits;

                archetypes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,gpuTypes.Length,64);archetypes.SetData(gpuTypes);traits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,gpuTraits.Length,UnitTraits.Stride);traits.SetData(gpuTraits);buildingFootprints=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128,16);

                movement.EnableKeyword("PLANET_RTS_CONTENT");movement.SetBuffer(moveKernel,"_Archetypes",archetypes);movement.SetBuffer(moveKernel,"_Traits",traits);int configure=movement.FindKernel("ConfigureCatalog");movement.SetBuffer(configure,"_PlanetUnits",units);movement.SetBuffer(configure,"_Archetypes",archetypes);movement.Dispatch(configure,Mathf.Max(1,(unitCount+127)/128),1,1);commands.BindCatalog(archetypes,traits);

                catalogRenderer=new PlanetCatalogRenderer(definition,units,traits,capacity);

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

            initialized=true;
        }

        public bool Interact(){if(!IsPiloting&&!IsFirstPerson&&(hud?.TextFocused==true||hud?.ModalOpen==true))return false;if(IsPiloting){spacecraft.Rebase();Physics.SyncTransforms();if(!spacecraft.CanExitSeat(player.Collision,out var local)){Base.Notice="Pilot exit is obstructed";return false;}modes.Switch(PlayerControlMode.FirstPerson);player.EnterShip(spacecraft,local,Vector3.forward);spacecraft.Rebase();Base?.Rebase();Physics.SyncTransforms();return true;}if(!IsFirstPerson){SetFirstPerson(true);return true;}if(spacecraft==null)return false;Physics.SyncTransforms();int action=spacecraft.FindInteraction(view.transform.position,view.transform.forward);if(action<0)return false;if(action<spacecraft.DoorCount)return spacecraft.ToggleDoor(action);SetShipControl(true);return true;}
        public void SetShipControl(bool enabled){if(spacecraft==null)return;if(enabled){player.SetActive(false);modes.Switch(PlayerControlMode.Spacecraft);}else{rig.SetFocusDirection(spacecraft.Direction);modes.Switch(PlayerControlMode.RTS);rig.RefreshPose();}spacecraft.Rebase();Base?.Rebase();Physics.SyncTransforms();}

        internal void ApplyShipRocketImpact(Vector3 direction)=>encounter?.RocketImpact(direction);
        internal bool TryTestNukeAtCursor(Vector2 screen){if(hud?.ModalOpen==true||hud?.TextFocused==true||hud?.Blocks(screen)==true||!rig.GroundPoint(screen,out var direction))return false;encounter?.Detonate(direction,craterRadius:PlanetEncounter.TestNukeCraterRadius);return encounter!=null;}

        public void SetFirstPerson(bool enabled)

        {

            if(!Ready||!enabled&&!IsFirstPerson&&!IsPiloting)return;
            if(IsPiloting&&!enabled){SetShipControl(false);return;}
            selecting=false;
            if(enabled){Base?.CancelPlacement();if(player.AttachedToShip){modes.Switch(PlayerControlMode.FirstPerson);player.Resume();}else{player.Enter(rig.FocusDirection,view.transform.forward);modes.Switch(PlayerControlMode.FirstPerson);}}
            else{rig.SetFocusDirection(player.Direction);modes.Switch(PlayerControlMode.RTS);rig.RefreshPose();}
            spacecraft?.Rebase();Base?.Rebase();Physics.SyncTransforms();

        }

        // Spectator selection deliberately never enters unit selection, orders or placement.
        internal bool InspectAiBuilding(Vector2 position){
            if(sessionMode!=PlanetGameMode.AiTest||Base==null||hud?.ModalOpen==true||hud?.TextFocused==true||hud?.Blocks(position)==true)return false;
            bool picked=Base.Pick(position);if(!picked)Base.ClearSelection();return picked;
        }

        void SelectionInput(Mouse mouse,Keyboard keyboard)

        {

            if(hud?.ModalOpen==true||hud?.TextFocused==true||mouse==null||!rig.InputEnabled||!Application.isFocused)return;

            Vector2 position=mouse.position.ReadValue();

            bool blocked=hud!=null&&hud.Blocks(position);

            if(sessionMode==PlanetGameMode.AiTest){
                selecting=false;
                if(mouse.leftButton.wasPressedThisFrame&&!blocked)InspectAiBuilding(position);
                if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)Base?.ClearSelection();
                return;
            }
            if(commands==null||SystemRuntime?.Civilisation?.CatchingUp(definition.bodyId)==true)return;

            if(NuclearTargetArmed){if(mouse.rightButton.wasPressedThisFrame||keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)NuclearTargetArmed=false;else if(mouse.leftButton.wasPressedThisFrame&&!blocked&&rig.GroundPoint(position,out var impact))LaunchNuke(impact);return;}

            if(Base?.RoadPlacing==true){selecting=false;if(mouse.rightButton.wasPressedThisFrame||keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){Base.CancelRoadPlacement();return;}if(!blocked&&mouse.leftButton.wasPressedThisFrame&&rig.GroundPoint(position,out var roadPoint))Base.RoadClick(roadPoint);return;}
            if(Base!=null&&Base.Placing!=null){selecting=false;if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){Base.CancelPlacement();return;}if(keyboard!=null&&keyboard.rKey.wasPressedThisFrame)Base.Rotate();if(mouse.rightButton.wasPressedThisFrame){Base.CancelPlacement();return;}if(!blocked&&rig.GroundPoint(position,out var ground)){Base.Preview(ground);if(mouse.leftButton.wasPressedThisFrame)Base.Construct(ground);}return;}

            var centre=origin.RenderPosition(definition.position);

            var matrix=view.projectionMatrix*view.worldToCameraMatrix*Matrix4x4.Translate(centre);

            int operation=keyboard!=null&&(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)?2:keyboard!=null&&(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed)?1:0;

            if(mouse.leftButton.wasPressedThisFrame&&!blocked&&position.y<Screen.height-85){if(Base!=null&&Base.Pick(position)){commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));selecting=false;return;}selectionStart=position;selecting=true;}

            if(selecting&&mouse.leftButton.wasReleasedThisFrame){commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),selectionStart,position,operation,(position-selectionStart).sqrMagnitude<25);if(commands.SelectedCount>0)Base?.ClearSelection();selecting=false;}

            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){selecting=false;commands.Select(matrix,view.transform.position-centre,new Vector2(Screen.width,Screen.height),new Vector2(-100,-100),new Vector2(-100,-100));}

            if(keyboard!=null&&keyboard.xKey.wasPressedThisFrame)commands.Stop();

        }

        internal void TerrainChanged(){RefreshBuildingCosts(Base.Buildings,true);streamed?.Update(view,PlanetRenderCentre,terrainErrorPixels,true);Base.Rebase();spacecraft?.Rebase();Physics.SyncTransforms();}

        public static void BindSurface(ComputeShader shader,PlanetDefinition d){shader.SetFloat("_PlanetRadius",d.radius);shader.SetFloat("_PlanetHeight",d.terrainHeight);shader.SetFloat("_PlanetSeed",(d.terrainSeed%1009)*.017f);shader.SetFloat("_PlanetOcean",d.oceanHeight);shader.SetFloat("_PlanetReliefBound",d.ReliefBound);d.Edits.Bind(shader);}

        static void BindSurface(Material material,PlanetDefinition d){material.SetFloat("_PlanetRadius",d.radius);material.SetFloat("_PlanetHeight",d.terrainHeight);material.SetFloat("_PlanetSeed",(d.terrainSeed%1009)*.017f);material.SetFloat("_PlanetOcean",d.oceanHeight);material.SetFloat("_PlanetReliefBound",d.ReliefBound);d.Edits.Bind(material);}

        void Update()

        {

            if(!Ready||!ResidentActive)return;lighting?.Update();var keyboard=Keyboard.current;

            if(keyboard!=null&&keyboard.f2Key.wasPressedThisFrame)showTerrainDebug=!showTerrainDebug;

            if((cbt!=null||streamed!=null)&&keyboard!=null&&keyboard.f3Key.wasPressedThisFrame){showTerrainTriangles=!showTerrainTriangles;if(showTerrainTriangles)terrainMaterial.EnableKeyword("PLANET_CBT_DEBUG");else terrainMaterial.DisableKeyword("PLANET_CBT_DEBUG");}

            if(sessionMode==PlanetGameMode.AiTest&&keyboard!=null&&!IsPiloting&&!(hud?.TextFocused??false)){if(keyboard.digit1Key.wasPressedThisFrame)FocusFaction(0);if(keyboard.digit2Key.wasPressedThisFrame)FocusFaction(1);}

            if(keyboard!=null&&keyboard.hKey.wasPressedThisFrame&&!(hud?.TextFocused??false))SetShipControl(!IsPiloting);

            if(keyboard!=null&&keyboard.nKey.wasPressedThisFrame&&Mouse.current!=null&&!IsPiloting)TryTestNukeAtCursor(Mouse.current.position.ReadValue());

            bool interact=keyboard!=null&&keyboard.fKey.wasPressedThisFrame;bool rts=keyboard!=null&&keyboard.tabKey.wasPressedThisFrame;

            if(modes.Mode==PlayerControlMode.RTS){bool input=rig.InputEnabled;rig.InputEnabled=input&&!(hud?.ModalOpen??false)&&!(hud?.TextFocused??false);rig.Step();rig.InputEnabled=input;}spacecraft?.StepUnpiloted(Time.deltaTime);if(IsPiloting)spacecraft.Step(Time.deltaTime);else if(IsFirstPerson){spacecraft?.Rebase();Base?.Rebase();Physics.SyncTransforms();if(AutomaticPlayerStep)player.Step(Time.deltaTime);}spacecraft?.Rebase();Base?.Rebase();if(IsFirstPerson&&spacecraft!=null){Physics.SyncTransforms();shipInteraction=spacecraft.FindInteraction(view.transform.position,view.transform.forward);}if(rts)SetFirstPerson(false);else if(interact)Interact();encounter?.UpdatePresentation();spacecraft?.StepWeapons(Time.deltaTime);var mouse=Mouse.current;

            bool placing=Base!=null&&(Base.Placing!=null||Base.RoadPlacing);

            if(modes.Mode==PlayerControlMode.RTS)SelectionInput(mouse,keyboard);

            if(!NuclearTargetArmed&&!placing&&modes.Mode==PlayerControlMode.RTS&&rig.InputEnabled&&Application.isFocused&&mouse!=null&&mouse.rightButton.wasPressedThisFrame&&!(hud?.Blocks(mouse.position.ReadValue())??false)&&rig.GroundPoint(mouse.position.ReadValue(),out var target)){

                var cell=CubeSphere.Cell(definition.bodyId,target,navigationResolution);

                if(commands!=null)OrderSelected(target);else if(surface.SampleHeight(CubeSphere.Direction(cell,navigationResolution))>=definition.oceanHeight){goal=target;navigation.SetGoal(0,goal);}

            }

            SimulationBeginForValidation?.Invoke();

            SimulationActive=SystemRuntime==null||SystemRuntime.Settings.ShouldSimulate(definition,ObserverPosition,SimulationActive);
            if(SimulationActive){if(legacyCommands)navigation.Relax(0,8);else navigation.RelaxPending(8);}

            movement.SetBuffer(moveKernel,"_PlanetField",navigation.Global);movement.SetVector("_PlanetGoal",CubeSphere.Encode(goal));movement.SetFloat("_PlanetSpeed",unitSpeed);movement.SetFloat("_PlanetDt",BattleSettings.Tick);

            uint previousTick=SimulationTicks;accumulator=Paused||!SimulationActive?0:Mathf.Min(accumulator+Time.deltaTime,BattleSettings.Tick*4);while(accumulator>=BattleSettings.Tick){encounter?.BeforeTick();if(encounter!=null)encounter.MoveUnits();else movement.Dispatch(moveKernel,(unitCount+127)/128,1,1);encounter?.AfterTick();if(!(SystemRuntime?.Civilisation?.CatchingUp(definition.bodyId)??false))Base?.Tick(BattleSettings.Tick);accumulator-=BattleSettings.Tick;SimulationTicks++;}SimulationEndForValidation?.Invoke();Base?.Rebase();resourceDecals?.Update();hud?.Tick();if(hud!=null){if(headquartersHud==null)headquartersHud=new PlanetHeadquartersHud(this);headquartersHud.Tick();}

            spacecraft?.RenderTrajectory();spacecraft?.RenderWeapons();spacecraft?.RenderLanding();flightHudRenderer?.SetActive(IsPiloting);var vp=GL.GetGPUProjectionMatrix(view.projectionMatrix,true)*view.worldToCameraMatrix;var centre=origin.RenderPosition(definition.position);var bounds=new Bounds(centre,Vector3.one*(definition.radius+definition.ReliefBound)*2);

            BindSurface(terrainMaterial,definition);terrainMaterial.SetMatrix("_PlanetVP",vp);terrainMaterial.SetVector("_PlanetCenter",centre);

            int res=rig.Distance<definition.radius?128:rig.Distance<definition.radius*3?64:32;terrainMaterial.SetInteger("_PlanetMeshRes",res);terrainMaterial.SetInteger("_OceanOnly",0);

            // Explicit per-draw bindings keep procedural geometry independent of HDRP's material batching.

            for(int blockIndex=0;blockIndex<2;blockIndex++){var block=blockIndex==0?landProperties:oceanProperties;block.SetMatrix("_PlanetVP",vp);block.SetVector("_PlanetCenter",centre);block.SetFloat("_PlanetRadius",definition.radius);block.SetFloat("_PlanetHeight",definition.terrainHeight);block.SetFloat("_PlanetOcean",definition.oceanHeight);block.SetFloat("_PlanetSeed",(definition.terrainSeed%1009)*.017f);block.SetInteger("_PlanetMeshRes",res);if(streamed!=null)streamed.Bind(block);if(cbt!=null){block.SetBuffer("_CbtTriangles",cbt.Triangles);block.SetInteger("_CbtGrid",cbt.Grid);}}

            landProperties.SetInteger("_OceanOnly",0);



            renderResolution=res;

            if(streamed!=null)streamed.Update(view,centre,terrainErrorPixels);

            else if(cbt!=null){cbt.Update(view,centre,terrainErrorPixels,refine:previousTick!=SimulationTicks);if(showTerrainDebug)cbt.RequestTelemetry();}

            oceanProperties.SetInteger("_OceanOnly",1);

            if(catalogRenderer!=null)catalogRenderer.Render(view,centre,unitCount);else{BindSurface(unitMaterial,definition);unitMaterial.SetMatrix("_PlanetVP",vp);unitMaterial.SetVector("_PlanetCenter",centre);Graphics.RenderMeshIndirect(new RenderParams(unitMaterial){camera=view,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},tank,arguments);}

            scaledRenderer.Render(view,origin,distantBodies,definition,SystemRuntime?.DistantTerrain);
            SystemRuntime?.UpdateSky();settlementRenderer?.Update();

        }

        internal void RenderTerrain(CommandBuffer cmd){

            SystemRuntime?.DrawSky(cmd);

            if(terrainMaterial==null)return;int pass=terrainMaterial.FindPass("PlanetLighting");

            if(streamed!=null){cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,streamed.DrawArguments,0,landProperties);cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,streamed.DrawArguments,0,oceanProperties);}

            else if(cbt!=null){cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,cbt.DrawArguments,0,landProperties);cmd.DrawProceduralIndirect(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,cbt.DrawArguments,0,oceanProperties);}

            else{cmd.DrawProcedural(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,36*renderResolution*renderResolution,1,landProperties);cmd.DrawProcedural(Matrix4x4.identity,terrainMaterial,pass,MeshTopology.Triangles,36*renderResolution*renderResolution,1,oceanProperties);}

            settlementRenderer?.Draw(cmd);scaledRenderer?.Draw(cmd);
        }

        void OnGUI()

        {if(!ResidentActive)return;

            if(!Ready)return;

            if(IsPiloting){GUI.Label(new Rect(12,12,1100,80),$"CAPITAL SHIP Ã‚Â· {spacecraft.Speed:F0} m/s\nW/S thrust Ã‚Â· A/D strafe Ã‚Â· Space land/take off Ã‚Â· R/C climb/descend Ã‚Â· V flight assist Ã‚Â· mouse pitch/yaw Ã‚Â· Q/E roll Ã‚Â· Shift boost Ã‚Â· G landing gear Ã‚Â· B brake Ã‚Â· L orbit attitude lock Ã‚Â· Left mouse fire rocket Ã‚Â· F leave seat Ã‚Â· Tab/H return to RTS");}

            else if(modes.Mode==PlayerControlMode.FirstPerson){GUI.Label(new Rect(Screen.width*.5f-4,Screen.height*.5f-10,20,20),"+");GUI.Label(new Rect(12,12,900,60),$"Health {player.Environment.Health:F0}   Oxygen {player.Environment.Oxygen:F0}%   Inventory: repair tool, suit oxygen\nWASD walk Ã¢â‚¬Â¢ mouse look Ã¢â‚¬Â¢ Space jump Ã¢â‚¬Â¢ F interact Ã¢â‚¬Â¢ Tab return to RTS");}

            else if(hud==null) GUI.Label(new Rect(12,12,1300,80),"PLANET DEVELOPMENT Ã¢â‚¬Â¢ WASD / middle drag pan Ã¢â‚¬Â¢ wheel zoom Ã¢â‚¬Â¢ Q/E rotate Ã¢â‚¬Â¢ F first person\nClick / drag select Ã¢â‚¬Â¢ Shift add Ã¢â‚¬Â¢ Ctrl remove Ã¢â‚¬Â¢ right-click move Ã¢â‚¬Â¢ X stop Ã¢â‚¬Â¢ Esc clear Ã¢â‚¬Â¢ F2 terrain statistics Ã¢â‚¬Â¢ F3 wireframe\n"+(commands?.Status??"Legacy all-tank commands")+" Ã¢â‚¬Â¢ Production combat, construction, fog and vehicles are pending migration.");

            if(InteractionPrompt.Length>0)GUI.Label(new Rect(Screen.width*.5f-200,Screen.height*.65f,600,40),InteractionPrompt);
            if(selecting&&Mouse.current!=null){var end=Mouse.current.position.ReadValue();var rect=Rect.MinMaxRect(Mathf.Min(selectionStart.x,end.x),Screen.height-Mathf.Max(selectionStart.y,end.y),Mathf.Max(selectionStart.x,end.x),Screen.height-Mathf.Min(selectionStart.y,end.y));GUI.Box(rect,GUIContent.none);}

            if(showTerrainDebug)GUI.Label(new Rect(12,90,1100,45),streamed!=null?$"Streamed terrain Ã¢â‚¬Â¢ {streamed.ResidentPatches} visible / {streamed.GpuLeafCount} leaf patches Ã¢â‚¬Â¢ level {streamed.DeepestLevel} Ã¢â‚¬Â¢ {streamed.BufferBytes/1024f:F0} KiB buffers Ã¢â‚¬Â¢ {(streamed.BudgetLimited?"budget limited":"within detail target")}\nRadius {definition.radius:F0} m Ã¢â‚¬Â¢ navigation cells {navigation.CellCount} Ã¢â‚¬Â¢ tick {SimulationTicks}":cbt!=null?$"CBT {(cbt.Converged?"settled":"refining")} Ã¢â‚¬Â¢ active {cbt.ActiveLeaves} / visible {cbt.VisibleLeaves} / depth-limited {cbt.SaturatedLeaves} Ã¢â‚¬Â¢ {cbt.BufferBytes/1048576f:F2} MiB terrain buffers\nNavigation cells {navigation.CellCount} Ã¢â‚¬Â¢ tick {SimulationTicks} Ã¢â‚¬Â¢ origin sectors {origin.Origin.sectorX},{origin.Origin.sectorY},{origin.Origin.sectorZ}":"Uniform terrain baseline Ã¢â‚¬Â¢ adaptiveTerrain enables CBT");

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
        internal void SetResidentActive(bool value){
            if(ResidentActive==value)return;ResidentActive=value;accumulator=0;
            if(!value){flightHudRenderer?.SetActive(false);spacecraft?.SetActive(false);lighting?.Dispose();lighting=null;}
            HideResidentChildren();
            if(value&&Ready){lighting=new PlanetLighting(view,transform,rig.FocusDirection);if(SystemRuntime!=null){lighting.Sun.transform.rotation=SystemRuntime.SunRotation;lighting.Update();}}
        }

        void OnDestroy(){settlementRenderer?.Dispose();headquartersHud?.Dispose();spacecraft?.Dispose();terrainRenderer?.Dispose();flightHudRenderer?.Dispose();lighting?.Dispose();encounter?.Dispose();resourceDecals?.Dispose();placementProbe?.Dispose();hud?.Dispose();Base?.Dispose();catalogRenderer?.Dispose();archetypes?.Dispose();traits?.Dispose();buildingFootprints?.Dispose();commands?.Dispose();player?.Dispose();streamed?.Dispose();cbt?.Dispose();scaledRenderer?.Dispose();navigation?.Dispose();costs?.Dispose();units?.Dispose();arguments?.Dispose();biomeColours?.Dispose();ArmyLifetime.Destroy(movement);TerrainSurfaceLibrary.Unbind(terrainMaterial);ArmyLifetime.Destroy(terrainMaterial);ArmyLifetime.Destroy(unitMaterial);ArmyLifetime.Destroy(tank);ArmyLifetime.Destroy(validationDistantBody);if(ownsDefinition)ArmyLifetime.Destroy(definition);if(view!=null)ArmyLifetime.Destroy(view.gameObject);}

    }

}





