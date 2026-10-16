using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    /// <summary>The CPU owns buffer lifetimes and compact commands. All authoritative units live here on the GPU.</summary>
    public sealed partial class GpuBattle : IDisposable
    {
        public readonly BattleSettings Settings;
        public readonly Battlefield Field;
        public readonly GpuNavigation Navigation;
        public GraphicsBuffer Units => current;
        public GraphicsBuffer TypeBuffer=>types;
        public GraphicsBuffer Projectiles { get; }
        public GraphicsBuffer Visible { get; }
        public GraphicsBuffer VisibleProjectiles { get; }
        public GraphicsBuffer Arguments { get; }
        public GraphicsBuffer PreviewArguments {get;}
        public GraphicsBuffer PreviewSlots {get;}
        public bool PreviewActive {get;private set;}
        public uint[] Statistics { get; private set; }
        public uint Tick { get; private set; }
        public bool ReadbackFailed { get; private set; }
        public string Notice { get; private set; } = "";
        public GraphicsBuffer CellCounts => counts;
        public GraphicsBuffer CellOffsets => offsets;
        public GraphicsBuffer ScatteredIndices => indices;
        readonly ComputeShader shader;
        readonly Dictionary<string, int> kernels = new Dictionary<string, int>();
        readonly GraphicsBuffer types, damage, counts, offsets, cursors, indices, pick, statistics, visibleCounts,routeProgress;
        readonly GpuPrefixScan scan;
        readonly GpuCrowd crowd;
        readonly GpuPrefixScan contactScan,selectionScan;
        readonly GraphicsBuffer contactCounts,contactOffsets,contactCursor,contactIndices,selectedFlags,selectedRanks,combinedRanks;
        readonly GraphicsBuffer formationSlots,formationState,orderOccupancy;
        readonly uint[] occupancy=new uint[BattleSettings.Groups*2+7];
        int orderStructureTarget=-1;
        bool combinedFormation;
        readonly Vector4[] formationOrigins=new Vector4[5];
        readonly uint[] placementState=new uint[4];
        uint previewSequence;
        bool previewReadback;
        public int PreviewRows {get;private set;}
        public int PreviewCount {get;private set;}
        readonly int contactResolution;
        readonly CommandBuffer cmd = new CommandBuffer { name = "Massive army pipeline" };
        GraphicsBuffer current, next;
        bool disposed, readbackPending;
        double nextReadback, nextAI;
        float accumulator;
        public float RenderInterpolation {get;private set;}=1;
        static readonly string[] Names = { "InitializeUnits", "InitializeProjectiles", "ClearGrid", "CountCells", "ScatterUnits", "MoveUnits", "ClearPick", "FindPick", "SelectUnits", "ApplyOrders", "AcquireTargets", "FireWeapons", "MoveProjectiles", "ApplyDamage", "ClearStatistics", "UpdateStatistics", "ClearVisibility", "ClassifyVisibility", "ClassifyProjectiles", "BuildIndirectArgs", "ValidateGrid", "ValidateMembership" };

        public GpuBattle(BattleSettings settings, Battlefield field, UnitArchetype[] archetypes = null)
        {
            Settings = settings; Field = field;
            if(settings.baseBuilding)settings.activeUnits=settings.capacity;
            settings.worldSize=field.Size;settings.navigationResolution=field.Resolution;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing) throw new NotSupportedException("Compute shaders and GPU instancing are required.");
            if (System.Runtime.InteropServices.Marshal.SizeOf<UnitState>() != UnitState.Stride) throw new InvalidOperationException("Unit layout mismatch");
            shader = UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Battle"));
            if(settings.baseBuilding)shader.EnableKeyword("BUILDING_GRID");
            if(settings.baseBuilding&&(settings.ambientZombies>0||settings.swarmMode))shader.EnableKeyword("SWARM_MODE");
            foreach (string name in Names) kernels[name] = shader.FindKernel(name);
            foreach(string name in new[]{"MixedFormationSlots","ClearContactGrid","CountContactCells","ScatterContactUnits","ResolveContacts","SelectedFlags","FinalizeFormation","PreviewFormationSlots","ValidateSpawns","ClearOrderOccupancy","CountOrderOccupancy","ClearFormationSlots","FlagFormationSlots","ScatterFormationSlots","AdvanceFormationSlots","FinishFormationSlots","ApplyStrategicSlots","AssignFactionOrders","AssignClearanceOrders","CommitFormation"})kernels[name]=shader.FindKernel(name);
            current = Buffer(settings.capacity, UnitState.Stride, "Authoritative units A"); next = Buffer(settings.capacity, UnitState.Stride, "Authoritative units B");
            archetypes = archetypes ?? new[] { UnitArchetype.Tank };
            if(settings.ZombiesEnabled){ZombieType=archetypes.Length;var expanded=new UnitArchetype[archetypes.Length+1];Array.Copy(archetypes,expanded,archetypes.Length);expanded[ZombieType]=ZombieDefinition.Archetype;archetypes=expanded;}
            types = Buffer(archetypes.Length, UnitArchetype.Stride, "Archetypes"); types.SetData(archetypes);
            damage = Buffer(settings.capacity, 4, "Atomic fixed-point damage");
            routeProgress=Buffer(settings.capacity,8,"Best route potential and stalled time");
            int cells = settings.spatialResolution * settings.spatialResolution;
            counts = Buffer(cells, 4, "Cell counts"); offsets = Buffer(cells, 4, "Exclusive cell offsets"); cursors = Buffer(cells, 4, "Scatter cursors");
            indices = Buffer(settings.capacity, 4, "Spatial unit indices");
            Projectiles = Buffer(settings.projectileCapacity, ProjectileState.Stride, "Gameplay shells");
            Visible = Buffer(settings.capacity * 3, 4, "LOD visible indices");
            VisibleProjectiles = Buffer(settings.projectileCapacity, 4, "Visible shells");
            visibleCounts = Buffer(5, 4, "Visibility counts");
            // IndirectArguments exposes a typed R32_UINT UAV on D3D11. Shader writes argument words.
            Arguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 4, GraphicsBuffer.IndirectDrawIndexedArgs.size) { name = "GPU draw arguments" };
            Arguments.SetData(new GraphicsBuffer.IndirectDrawIndexedArgs[4]);
            pick = Buffer(1, 4, "GPU screen pick"); statistics = Buffer(128, 4, "Statistics and validation"); statistics.SetData(new uint[128]);
            scan = new GpuPrefixScan(cells); Navigation = new GpuNavigation(settings, field);crowd=new GpuCrowd(field,settings.ZombiesEnabled);
            contactResolution=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(settings.worldSize/4)),64,1024);
            int contactCells=contactResolution*contactResolution;
            contactCounts=Buffer(contactCells,4,"Fine contact counts");contactOffsets=Buffer(contactCells,4,"Fine contact offsets");contactCursor=Buffer(contactCells,4,"Fine contact cursors");
            contactIndices=Buffer(settings.capacity,4,"Fine contact indices");contactScan=new GpuPrefixScan(contactCells);
            selectedFlags=Buffer(settings.capacity,4,"Selected live flags");selectedRanks=Buffer(settings.capacity,4,"Unique formation ranks",GraphicsBuffer.Target.CopySource);selectionScan=new GpuPrefixScan(settings.capacity);
            combinedRanks=Buffer(settings.capacity,4,"Shared mixed formation ranks",GraphicsBuffer.Target.CopyDestination);
            selectedFlags.SetData(new uint[settings.capacity]);
            formationSlots=Buffer(settings.capacity,8,"Unique traversable formation slots");formationState=Buffer(4,4,"Formation slot counts and rows");
            orderOccupancy=Buffer(occupancy.Length,4,"Exact live and selected route membership");
            PreviewArguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size);
            PreviewArguments.SetData(new GraphicsBuffer.IndirectDrawIndexedArgs[1]);
            PreviewSlots=Buffer(4096,16,"Formation ghost slots");
            InitializeRts(archetypes.Length);InitializeZombieNoise();InitializeCombatVisuals();
            Parameters(null);
            using(var deployment=Buffer(settings.activeUnits,8,"Temporary connected deployment slots")) {
                deployment.SetData(settings.baseBuilding?new Vector2[settings.activeUnits]:field.TakeDeployment(settings.activeUnits));
                B(kernels["InitializeUnits"],"_SpawnSlots",deployment);
                Dispatch("InitializeUnits", settings.activeUnits);
                Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
            }
            Dispatch("InitializeProjectiles", settings.projectileCapacity);
            BuildGrid();
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
        }
        static GraphicsBuffer Buffer(int n, int stride, string name, GraphicsBuffer.Target copyTarget = 0) => new GraphicsBuffer(GraphicsBuffer.Target.Structured | copyTarget, n, stride) { name = name };
        void B(int kernel, string name, GraphicsBuffer b) => cmd.SetComputeBufferParam(shader, kernel, name, b);
        void Terrain(int kernel) { cmd.SetComputeTextureParam(shader,kernel,"_Terrain",Field.Terrain);cmd.SetComputeTextureParam(shader,kernel,"_WaterMap",Field.WaterMap); }
        void Bind(string name, int k)
        {
            if(Settings.baseBuilding)B(k,"_StructureCells",Field.StructureCells);
            switch (name)
            {
                case "InitializeUnits": B(k,"_Units",current); B(k,"_Next",next); B(k,"_Damage",damage); B(k,"_RouteProgress",routeProgress); B(k,"_Groups",Navigation.Groups); B(k,"_Types",types); B(k,"_Costs",Field.Costs); break;
                case "InitializeProjectiles": B(k,"_Projectiles",Projectiles); break;
                case "ClearGrid": B(k,"_CellCounts",counts); B(k,"_CellCursor",cursors); break;
                case "CountCells": B(k,"_Units",current); B(k,"_CellCounts",counts); break;
                case "ScatterUnits": B(k,"_Units",current); B(k,"_CellCursor",cursors); B(k,"_CellOffsets",offsets); B(k,"_Indices",indices); break;
                case "MoveUnits":
                    if(noiseA!=null){B(k,"_SwarmField",swarmPublished??crowd.Grid);B(k,"_NoiseField",noiseA);}
                    B(k,"_Read",current); B(k,"_Next",next); B(k,"_Types",types); B(k,"_Groups",Navigation.Groups); B(k,"_Costs",Field.Costs);
                    B(k,"_RouteProgress",routeProgress);
                    B(k,"_CrowdGrid",crowd.Grid);B(k,"_CoarseField",Navigation.Coarse); B(k,"_GlobalField",Navigation.Global);Contact(k); Terrain(k); break;
                case "ClearContactGrid": B(k,"_ContactCounts",contactCounts);B(k,"_ContactCursor",contactCursor);break;
                case "CountContactCells": B(k,"_Units",current);B(k,"_ContactCounts",contactCounts);break;
                case "ScatterContactUnits": B(k,"_Units",current);B(k,"_ContactCounts",contactCounts);B(k,"_ContactOffsets",contactOffsets);B(k,"_ContactCursor",contactCursor);B(k,"_ContactIndices",contactIndices);break;
                case "ResolveContacts": B(k,"_Read",current);B(k,"_Next",next);B(k,"_Types",types);B(k,"_Costs",Field.Costs);Contact(k);break;
                case "SelectedFlags": B(k,"_Units",current);B(k,"_SelectedFlags",selectedFlags);break;
                case "ClearOrderOccupancy": B(k,"_OrderOccupancy",orderOccupancy);break;
                case "CountOrderOccupancy": B(k,"_Units",current);B(k,"_OrderOccupancy",orderOccupancy);break;
                case "FinalizeFormation": B(k,"_Groups",Navigation.Groups);B(k,"_SelectedFlags",selectedFlags);B(k,"_SelectedRanks",selectedRanks);B(k,"_Costs",Field.Costs);B(k,"_PreviewArgs",PreviewArguments);break;
                case "CommitFormation": B(k,"_Groups",Navigation.Groups);B(k,"_Costs",Field.Costs);break;
                case "ClearFormationSlots": B(k,"_FormationState",formationState);B(k,"_Groups",Navigation.Groups);break;
                case "FlagFormationSlots": B(k,"_Groups",Navigation.Groups);B(k,"_Costs",Field.Costs);B(k,"_SelectedFlags",selectedFlags);break;
                case "ScatterFormationSlots": B(k,"_Groups",Navigation.Groups);B(k,"_SelectedFlags",selectedFlags);B(k,"_SelectedRanks",selectedRanks);B(k,"_FormationState",formationState);B(k,"_FormationSlots",formationSlots);break;
                case "AdvanceFormationSlots": B(k,"_SelectedFlags",selectedFlags);B(k,"_SelectedRanks",selectedRanks);B(k,"_FormationState",formationState);break;
                case "FinishFormationSlots": B(k,"_Groups",Navigation.Groups);B(k,"_Costs",Field.Costs);B(k,"_FormationState",formationState);break;
                case "PreviewFormationSlots": B(k,"_Groups",Navigation.Groups);B(k,"_FormationState",formationState);B(k,"_FormationSlots",formationSlots);B(k,"_PreviewSlots",PreviewSlots);B(k,"_SelectedFlags",selectedFlags);break;
                case "MixedFormationSlots": B(k,"_Units",current);B(k,"_Types",types);B(k,"_CombinedRanks",combinedRanks);B(k,"_Groups",Navigation.Groups);B(k,"_Costs",Field.Costs);B(k,"_FormationSlots",formationSlots);B(k,"_FormationState",formationState);B(k,"_SelectedFlags",selectedFlags);break;
                case "ClearPick": B(k,"_Pick",pick); break;
                case "FindPick": case "SelectUnits": B(k,"_Units",current); B(k,"_Pick",pick); Terrain(k); break;
                case "ApplyOrders": B(k,"_Units",current); B(k,"_Pick",pick); B(k,"_Groups",Navigation.Groups);B(k,"_SelectedRanks",selectedRanks);B(k,"_CombinedRanks",combinedRanks);B(k,"_FormationSlots",formationSlots); break;
                case "AssignClearanceOrders":
                case "AssignFactionOrders": B(k,"_Costs",Field.Costs);B(k,"_Units",current);B(k,"_Types",types);B(k,"_Groups",Navigation.Groups);Terrain(k);break;
                case "ApplyStrategicSlots": B(k,"_Units",current);B(k,"_Groups",Navigation.Groups);B(k,"_FormationSlots",formationSlots);break;
                case "AcquireTargets": B(k,"_Units",current); B(k,"_Types",types); B(k,"_CellCounts",counts); B(k,"_CellOffsets",offsets); B(k,"_Indices",indices); break;
                case "FireWeapons": BindZombieNoise(k);B(k,"_Units",current); B(k,"_Types",types); B(k,"_Projectiles",Projectiles); B(k,"_Damage",damage); Terrain(k); break;
                case "MoveProjectiles": BindCombatVisuals(shader,k);BindZombieNoise(k);B(k,"_Read",current); B(k,"_Types",types); B(k,"_Projectiles",Projectiles); B(k,"_Damage",damage); B(k,"_ProjectileCellCounts",counts); B(k,"_ProjectileCellOffsets",offsets); B(k,"_ProjectileIndices",indices); Terrain(k); break;
                case "ApplyDamage": BindCombatVisuals(shader,k);B(k,"_Units",current); B(k,"_Damage",damage);break;
                case "ClearStatistics": B(k,"_Statistics",statistics); break;
                case "UpdateStatistics": B(k,"_Units",current); B(k,"_Groups",Navigation.Groups); B(k,"_Projectiles",Projectiles); B(k,"_CellCounts",counts); B(k,"_Statistics",statistics); break;
                case "ClearVisibility": B(k,"_VisibleCounts",visibleCounts); break;
                case "ClassifyVisibility": B(k,"_Units",current); B(k,"_Types",types); B(k,"_Visible",Visible); B(k,"_VisibleCounts",visibleCounts); Terrain(k); break;
                case "ClassifyProjectiles": B(k,"_Projectiles",Projectiles); B(k,"_VisibleProjectiles",VisibleProjectiles); B(k,"_VisibleCounts",visibleCounts); break;
                case "BuildIndirectArgs": B(k,"_Args",Arguments); B(k,"_VisibleCounts",visibleCounts); B(k,"_Statistics",statistics); break;
                case "ValidateGrid": B(k,"_Units",current); B(k,"_CellCounts",counts); B(k,"_CellCursor",cursors); B(k,"_CellOffsets",offsets); B(k,"_Indices",indices); B(k,"_Statistics",statistics); B(k,"_Damage",damage); break;
                case "ValidateMembership": B(k,"_Units",current); B(k,"_Damage",damage); B(k,"_Statistics",statistics); break;
                case "ValidateSpawns": B(k,"_Units",current);B(k,"_Costs",Field.Costs);B(k,"_Statistics",statistics);Contact(k);break;
            }
        }
        void Dispatch(string name, int elements)
        {
            int k = kernels[name]; Bind(name, k); cmd.DispatchCompute(shader, k, (elements + 127) / 128, 1, 1);
        }
        void Single(string name) { int k = kernels[name]; Bind(name, k); cmd.DispatchCompute(shader, k, 1, 1, 1); }
        void Contact(int k) { B(k,"_ContactCounts",contactCounts);B(k,"_ContactOffsets",contactOffsets);B(k,"_ContactIndices",contactIndices); }
        void Parameters(Camera camera)
        {
            CombatVisualParameters(shader,camera);
            CombatVisualParameters(visualPresentationShader,camera);
            cmd.SetComputeIntParam(shader,"_CrowdRes",crowd.Resolution);cmd.SetComputeIntParam(shader,"_UseCrowd",Settings.crowdPressure&&Settings.spatialGrid?1:0);
            cmd.SetComputeIntParam(shader,"_ContactRes",contactResolution);
            cmd.SetComputeIntParam(shader,"_Active",Settings.activeUnits); cmd.SetComputeIntParam(shader,"_Capacity",Settings.capacity);
            cmd.SetComputeIntParam(shader,"_ProjectileCapacity",Settings.projectileCapacity); cmd.SetComputeIntParam(shader,"_GridRes",Settings.spatialResolution);
            cmd.SetComputeIntParam(shader,"_NavRes",Settings.navigationResolution); cmd.SetComputeIntParam(shader,"_Seed",Settings.seed);
            cmd.SetComputeIntParam(shader,"_Tick",(int)Tick); cmd.SetComputeIntParam(shader,"_TargetSlices",Settings.targetSlices);
            cmd.SetComputeIntParam(shader,"_NeighbourBudget",Settings.neighbourBudget); cmd.SetComputeIntParam(shader,"_TargetBudget",Settings.targetBudget);
            cmd.SetComputeIntParam(shader,"_UseNavigation",Settings.navigation?1:0); cmd.SetComputeIntParam(shader,"_UseGrid",Settings.spatialGrid?1:0);
            cmd.SetComputeIntParam(shader,"_UseLod",Settings.simulationLod?1:0); cmd.SetComputeIntParam(shader,"_UseProjectiles",Settings.projectiles?1:0);
            cmd.SetComputeIntParam(shader,"_UseMeshes",Settings.meshes?1:0); cmd.SetComputeIntParam(shader,"_UseImpostors",Settings.impostors?1:0);
            cmd.SetComputeIntParam(shader,"_UseVfx",Settings.vfx?1:0);
            cmd.SetComputeVectorArrayParam(shader,"_TeamModifiers",TeamModifiers);
            cmd.SetComputeIntParam(shader,"_Logistics",Settings.logistics?1:0);
            cmd.SetComputeIntParam(shader,"_BaseMode",Settings.baseBuilding?1:0);cmd.SetComputeIntParam(shader,"_Spectator",Settings.spectatorMode?1:0);cmd.SetComputeIntParam(shader,"_BaseEnemyAI",Settings.baseBuilding&&Settings.enemyAI?1:0);
            if(Settings.baseBuilding)cmd.SetComputeIntParam(shader,"_StructureRes",Field.StructureResolution);
            cmd.SetComputeIntParam(shader,"_FogRes",FogResolution);cmd.SetComputeIntParam(shader,"_FilterMovement",orderMovementFilter);
            cmd.SetComputeIntParam(shader,"_BuildingCount",structureCount);cmd.SetComputeIntParam(shader,"_StructureTarget",orderStructureTarget);
            cmd.SetComputeIntParam(shader,"_CombinedFormation",combinedFormation?1:0);cmd.SetComputeVectorArrayParam(shader,"_FormationOrigins",formationOrigins);
            cmd.SetComputeFloatParam(shader,"_WorldSize",Settings.worldSize); cmd.SetComputeFloatParam(shader,"_Delta",BattleSettings.Tick);
            if (camera != null)
            {
                cmd.SetComputeFloatParam(shader,"_ProjectedUnitPixels",camera.orthographic?5*camera.pixelHeight/(2*camera.orthographicSize):-1);
                cmd.SetComputeVectorParam(shader,"_CameraPosition",camera.transform.position);
                cmd.SetComputeVectorParam(shader,"_RenderFocus",WorldTopology.ViewFocus(camera));
                cmd.SetComputeMatrixParam(shader,"_ViewProjection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,false)*camera.worldToCameraMatrix);
                cmd.SetComputeVectorParam(shader,"_ScreenSize",new Vector4(camera.pixelWidth,camera.pixelHeight,0,0));
                var planes = GeometryUtility.CalculateFrustumPlanes(camera); var data = new Vector4[6];
                for (int i=0;i<6;i++) data[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance);
                cmd.SetComputeVectorArrayParam(shader,"_Planes",data);
            }
        }
        void BuildGrid()
        {
            cmd.BeginSample(GpuSamples.Get("Army.SpatialGrid"));
            Dispatch("ClearGrid",Settings.spatialResolution*Settings.spatialResolution);
            Dispatch("CountCells",Settings.activeUnits); scan.Record(cmd,counts,offsets); Dispatch("ScatterUnits",Settings.activeUnits);
            cmd.EndSample(GpuSamples.Get("Army.SpatialGrid"));
        }
        void Simulate(Camera camera)
        {
            Tick++; Parameters(camera);
            RecordZombieNoise();
            if (Settings.navigation && Tick % 2 == 0) Navigation.Record(cmd);
            if (Settings.spatialGrid) BuildGrid();
            if(Settings.spatialGrid && Settings.movement) { BuildContactGrid();if(Settings.crowdPressure)crowd.Record(cmd,current,Settings.activeUnits,types,Settings.baseBuilding); }
            if (Settings.movement)
            {
                cmd.BeginSample(GpuSamples.Get("Army.MovementAvoidance")); Dispatch("MoveUnits",Settings.activeUnits); cmd.EndSample(GpuSamples.Get("Army.MovementAvoidance"));
                (current,next)=(next,current);
                if(Settings.spatialGrid)
                {
                    BuildContactGrid(); // Contacts must index predicted positions, not the previous movement snapshot.
                    cmd.BeginSample(GpuSamples.Get("Army.ContactResolution"));
                    for(int pass=0;pass<3;pass++) { Dispatch("ResolveContacts",Settings.activeUnits);(current,next)=(next,current); }
                    cmd.EndSample(GpuSamples.Get("Army.ContactResolution"));
                }
            }
            if (Settings.targeting && Settings.spatialGrid)
            { cmd.BeginSample(GpuSamples.Get("Army.TargetAcquisition")); Dispatch("AcquireTargets",Settings.activeUnits); cmd.EndSample(GpuSamples.Get("Army.TargetAcquisition")); }
            if (Settings.combat)
            { cmd.BeginSample(GpuSamples.Get("Army.Combat")); Dispatch("FireWeapons",Settings.activeUnits); cmd.EndSample(GpuSamples.Get("Army.Combat")); }
            // Existing shells finish even if firing gameplay shells is disabled.
            cmd.BeginSample(GpuSamples.Get("Army.Projectiles")); Dispatch("MoveProjectiles",Settings.projectileCapacity); cmd.EndSample(GpuSamples.Get("Army.Projectiles"));
            Dispatch("ApplyDamage",Settings.activeUnits);
        }
        public void Update(Camera camera, float delta, uint[] indexCounts, bool paused)
        {
            if (disposed) return;
            Field.ApplyQueuedCraters(Navigation);
            cmd.Clear(); Parameters(camera);
            if (!paused)
            {
                RecordFactionAdvances();
                accumulator = Mathf.Min(accumulator+delta, BattleSettings.Tick*3);
                for (int i=0;accumulator>=BattleSettings.Tick && i<3;i++) { accumulator-=BattleSettings.Tick; Simulate(camera); }
                if (Settings.enemyAI && !Settings.baseBuilding && Time.realtimeSinceStartupAsDouble > nextAI)
                {
                    nextAI=Time.realtimeSinceStartupAsDouble+8;
                    // Strategic AI operates on one army and the 4x4 sector reduction, independent of unit count.
                    Vector2 objective=Settings.preview?.Starts[0]??Field.StrategicSites[0]; uint largest=0;
                    if (Statistics!=null) for(int i=0;i<16;i++) if(Statistics[48+i]>largest) { largest=Statistics[48+i]; objective=Field.StrategicSites[i]; }
                    objective=Field.TraversableDestination(objective);
                    if(Field.Distance(objective,Navigation.GetOrder(1).destination)>Settings.worldSize/Settings.navigationResolution) {
                        TryStrategicOrder(camera,objective);
                    }
                    // Existing red army follows group data; update only its destinations on the GPU.
                    // Movement reads the current group's flow; formations remain stable around the objective.
                }
            }
            bool readback = !readbackPending && Time.realtimeSinceStartupAsDouble >= nextReadback;
            if (readback)
            {
                cmd.BeginSample(GpuSamples.Get("Army.Statistics")); Single("ClearStatistics");
                Dispatch("UpdateStatistics",Mathf.Max(Settings.activeUnits,Mathf.Max(Settings.projectileCapacity,Settings.spatialResolution*Settings.spatialResolution)));
                cmd.EndSample(GpuSamples.Get("Army.Statistics"));
            }
            RenderInterpolation=paused?1:Mathf.Clamp01(accumulator/BattleSettings.Tick);
            RecordRts(camera,paused);
            RecordTestNuke(camera);
            cmd.BeginSample(GpuSamples.Get("Army.Visibility")); Single("ClearVisibility"); Dispatch("ClassifyVisibility",Settings.activeUnits); Dispatch("ClassifyProjectiles",Settings.projectileCapacity);
            for(int i=0;i<4;i++) cmd.SetComputeIntParam(shader,"_IndexCount"+i,(int)indexCounts[i]);
            Single("BuildIndirectArgs"); cmd.EndSample(GpuSamples.Get("Army.Visibility"));
            RecordCombatVisualReadback(paused);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            if (readback)
            {
                Navigation.RequestConvergence();
                readbackPending=true; nextReadback=Time.realtimeSinceStartupAsDouble+.2;
                AsyncGPUReadback.Request(statistics, request => {
                    if(disposed) return; readbackPending=false;
                    if(request.hasError) { ReadbackFailed=true; return; }
                    Statistics=request.GetData<uint>().ToArray();
                });
            }
        }
        bool TryStrategicOrder(Camera camera,Vector2 objective)
        {
            Navigation.StageStrategicOrder(cmd,1,objective,1,ArmyOrder.AttackMove);
            cmd.SetComputeIntParam(shader,"_CommandGroup",BattleSettings.PreviewGroup);BuildFormationSlots();
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();formationState.GetData(placementState);Parameters(camera);
            if(placementState[0]!=placementState[2])return false;
            Navigation.SetOrder(cmd,1,objective,1,ArmyOrder.AttackMove);
            cmd.SetComputeIntParam(shader,"_CommandGroup",1);Single("CommitFormation");Navigation.Reset(cmd,1);Dispatch("ApplyStrategicSlots",Settings.activeUnits);
            return true;
        }
        public void Select(Camera camera, Rect normalized, SelectionOperation operation, bool click)
        {
            if(!Settings.selection) return;
            cmd.Clear(); Parameters(camera);
            cmd.SetComputeVectorParam(shader,"_SelectionRect",new Vector4(normalized.xMin,normalized.yMin,normalized.xMax,normalized.yMax));
            cmd.SetComputeIntParam(shader,"_SelectionOperation",(int)operation); cmd.SetComputeIntParam(shader,"_ClickSelection",click?1:0);
            cmd.SetComputeIntParam(shader,"_PickTeam",0); Single("ClearPick"); if(click) Dispatch("FindPick",Settings.activeUnits);
            Dispatch("SelectUnits",Settings.activeUnits); Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            nextReadback=0;
        }
        void BuildContactGrid()
        {
            cmd.BeginSample(GpuSamples.Get("Army.ContactGrid"));Dispatch("ClearContactGrid",contactResolution*contactResolution);
            Dispatch("CountContactCells",Settings.activeUnits);contactScan.Record(cmd,contactCounts,contactOffsets);Dispatch("ScatterContactUnits",Settings.activeUnits);
            cmd.EndSample(GpuSamples.Get("Army.ContactGrid"));
        }
        void RankSelection() { Dispatch("SelectedFlags",Settings.activeUnits);selectionScan.Record(cmd,selectedFlags,selectedRanks); }
        void BuildFormationSlots()
        {
            Single("ClearFormationSlots");
            // Four bounded lattice batches extend the rear rows around blocked cells, never merge slots.
            for(int batch=0;batch<4;batch++) {
                cmd.SetComputeIntParam(shader,"_SlotBatch",batch);Dispatch("FlagFormationSlots",Settings.activeUnits);
                selectionScan.Record(cmd,selectedFlags,selectedRanks);Dispatch("ScatterFormationSlots",Settings.activeUnits);Single("AdvanceFormationSlots");
            }
            Single("FinishFormationSlots");
        }
        public void PreviewFormation(Camera camera,FormationLayout layout)
        {
            if(Settings.baseBuilding){PreviewCombinedFormation(camera,layout);return;}
            cmd.Clear();Parameters(camera);RankSelection();previewSequence++;
            Navigation.UploadFormation(cmd,BattleSettings.PreviewGroup,layout,0,ArmyOrder.Move);
            cmd.SetComputeIntParam(shader,"_CommandGroup",BattleSettings.PreviewGroup);Single("FinalizeFormation");BuildFormationSlots();Dispatch("PreviewFormationSlots",4096);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();PreviewActive=true;
            RequestPreviewMetadata();
        }
        void RequestPreviewMetadata()
        {
            if(previewReadback || !PreviewActive || disposed)return;
            previewReadback=true;uint sequence=previewSequence;
            AsyncGPUReadback.Request(formationState,r=> {
                if(disposed)return;previewReadback=false;if(!PreviewActive)return;
                // A drag may update again before this tiny readback completes. Always fetch the latest box.
                if(sequence!=previewSequence) { RequestPreviewMetadata();return; }
                if(r.hasError)return;var state=r.GetData<uint>();PreviewRows=(int)state[1];PreviewCount=(int)state[2];
            });
        }
        public void CancelPreview() { PreviewActive=false;PreviewRows=PreviewCount=0;previewSequence++; }
        public bool IssueOrder(Camera camera, Vector2 destination, ArmyOrder order, Vector2 mouse,FormationLayout? formation=null)
        {
            if(Settings.spectatorMode)return false;
            CancelPreview();cmd.Clear();Parameters(camera);if(!combinedFormation)RankSelection();Single("ClearOrderOccupancy");Dispatch("CountOrderOccupancy",Settings.activeUnits);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();orderOccupancy.GetData(occupancy);
            int selected=(int)occupancy[BattleSettings.Groups*2];
            if(selected==0) { Notice="Select units before issuing a formation order.";return false; }
            int group = order==ArmyOrder.Stop ? 0 : Navigation.AllocatePlayerGroup(occupancy);
            if(group<0) { Notice="All 16 route groups have other tanks moving. Select a whole group to retarget it, or stop a group first."; return false; }
            Notice=""; destination=MovementDestination(destination,orderMovementFilter<0?2:orderMovementFilter);
            cmd.Clear(); Parameters(camera);
            cmd.SetComputeIntParam(shader,"_Order",(int)order); cmd.SetComputeIntParam(shader,"_CommandGroup",group);
            if(order!=ArmyOrder.Stop)
            {
                var layout=formation??FormationLayout.Compact(destination,new Vector2(camera.transform.right.x,camera.transform.right.z),selected);
                if(!layout.Fits(Settings.worldSize,selected)) { cmd.Clear();Notice="Formation exceeds the battlefield. Drag a wider front or select fewer tanks.";return false; }
                if(combinedFormation){
                    Parameters(camera);cmd.SetComputeIntParam(shader,"_Order",(int)order);cmd.SetComputeIntParam(shader,"_CommandGroup",group);
                    Navigation.UploadFormation(cmd,group,layout,0,order,orderMovementFilter);Single("CommitFormation");Navigation.Reset(cmd,group);
                }else{
                // Validate into the isolated preview group before touching a live group's field.
                Navigation.UploadFormation(cmd,BattleSettings.PreviewGroup,layout,0,order,orderMovementFilter<0?2:orderMovementFilter);
                cmd.SetComputeIntParam(shader,"_CommandGroup",BattleSettings.PreviewGroup);Single("FinalizeFormation");BuildFormationSlots();
                Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();formationState.GetData(placementState);
                if(placementState[0]!=(uint)selected) { Notice="Not enough clear formation slots here. Move or widen the formation.";return false; }
                Parameters(camera);cmd.SetComputeIntParam(shader,"_Order",(int)order);cmd.SetComputeIntParam(shader,"_CommandGroup",group);
                Navigation.UploadFormation(cmd,group,layout,0,order,orderMovementFilter<0?2:orderMovementFilter);Single("CommitFormation");Navigation.Reset(cmd,group);RankSelection();
                }
            }
            Single("ClearPick");
            if(order==ArmyOrder.Attack)
            {
                cmd.SetComputeIntParam(shader,"_PickTeam",1);
                cmd.SetComputeVectorParam(shader,"_SelectionRect",new Vector4(mouse.x,mouse.y,mouse.x,mouse.y));
                Dispatch("FindPick",Settings.activeUnits);
            }
            Dispatch("ApplyOrders",Settings.activeUnits); Graphics.ExecuteCommandBuffer(cmd); cmd.Clear(); nextReadback=0;PreviewActive=false;
            return true;
        }
        public void ValidateSpatialGrid(Action<uint[]> callback)
        {
            cmd.Clear(); Parameters(null); BuildGrid(); Single("ClearStatistics");
            Dispatch("ValidateGrid",Settings.spatialResolution*Settings.spatialResolution);
            Dispatch("ValidateMembership",Settings.activeUnits);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            AsyncGPUReadback.Request(statistics,request=> { if(disposed) return; callback(request.hasError?null:request.GetData<uint>().ToArray()); });
        }
        public void ValidateSpawns(Action<uint[]> callback)
        {
            Parameters(null);Dispatch("ClearStatistics",128);BuildContactGrid();Dispatch("ValidateSpawns",Settings.activeUnits);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
            AsyncGPUReadback.Request(statistics,r=>callback(r.hasError?null:r.GetData<uint>().ToArray()));
        }
        public void RequestStatistics(Action<uint[]> callback)
        {
            cmd.Clear(); Parameters(null);Single("ClearStatistics");
            Dispatch("UpdateStatistics",Mathf.Max(Settings.activeUnits,Mathf.Max(Settings.projectileCapacity,Settings.spatialResolution*Settings.spatialResolution)));
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
            AsyncGPUReadback.Request(statistics,request=> { if(!disposed) callback(request.hasError?null:request.GetData<uint>().ToArray()); });
        }
        public void ReadUnitSample(int start, int count, Action<UnitState[]> callback)
        {
            if(count<1 || count>256 || start<0 || start+count>Settings.activeUnits) throw new ArgumentOutOfRangeException(nameof(count));
            AsyncGPUReadback.Request(current,count*UnitState.Stride,start*UnitState.Stride,request=> { if(disposed) return; callback(request.hasError?null:request.GetData<UnitState>().ToArray()); });
        }
        public void ReadDrawArguments(Action<uint[]> callback)
        {
            AsyncGPUReadback.Request(Arguments,request=> { if(!disposed)callback(request.hasError?null:request.GetData<uint>().ToArray()); });
        }
#if UNITY_EDITOR
        public bool SetValidationStrategicOrder(Camera camera,Vector2 objective)
        {
            cmd.Clear();Parameters(camera);bool accepted=TryStrategicOrder(camera,objective);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();return accepted;
        }
        public void SetValidationUnits(UnitState[] fixture)
        {
            if(fixture.Length>256 || fixture.Length!=Settings.activeUnits)throw new ArgumentException("Validation fixtures must cover at most 256 slots.");
            current.SetData(fixture);next.SetData(fixture);
            if(Settings.baseBuilding)ResetValidationFreeSlots(fixture);
            cmd.Clear();Parameters(null);BuildGrid();Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
#endif
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            DisposeCombatVisuals();DisposeRts();cmd.Dispose(); Navigation.Dispose();crowd.Dispose(); scan.Dispose();contactScan.Dispose();selectionScan.Dispose();
            foreach(var buffer in new[]{contactCounts,contactOffsets,contactCursor,contactIndices,selectedFlags,selectedRanks,combinedRanks,PreviewArguments,PreviewSlots,formationSlots,formationState,orderOccupancy})buffer.Dispose();
            foreach(var b in new[]{current,next,types,damage,counts,offsets,cursors,indices,pick,statistics,visibleCounts,Projectiles,Visible,VisibleProjectiles,Arguments,routeProgress}) b.Dispose();
            ArmyLifetime.Destroy(shader);
        }
    }
}
