using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    // Session orchestration reads aggregate counters only. Slots, targets, damage and recycling stay on GPU.
    public sealed class PlanetEncounter : IDisposable
    {
        readonly PlanetCombatDecals decals;readonly GraphicsBuffer roamOrigins;readonly PlanetNuclearPresentation nuclear;
        public int NukesDetonated {get;private set;}
        struct Strike{public Vector3 direction;public float radius,damage;public uint tick;}
        readonly List<Strike> strikes=new List<Strike>(32);readonly Dictionary<int,uint> siloCooldown=new Dictionary<int,uint>();
        public bool LaunchNuke(RtsBuilding source,Vector3 direction){if(source==null||source.removed||source.definition.id!="silo"||!source.Complete||source.paused||!source.powered||strikes.Count>=32||!world.Base.Factions[source.team].HasTech("nuclear"))return false;if(siloCooldown.TryGetValue(source.index,out uint ready)&&world.SimulationTicks<ready)return false;if(CubeSphere.ArcDistance(CubeSphere.Decode(source.position),direction,world.definition.radius)>source.definition.weaponRange)return false;var cost=new ResourceCost{fuel=100,isotopes=25};if(!world.Base.Factions[source.team].wallet.Spend(cost))return false;strikes.Add(new Strike{direction=direction,radius=source.definition.explosionRadius,damage=source.definition.weaponDamage,tick=world.SimulationTicks+360});siloCooldown[source.index]=world.SimulationTicks+(uint)Mathf.CeilToInt(source.definition.reload/BattleSettings.Tick);return true;}
        readonly GraphicsBuffer crowdMass,crowdGrid,pressureA,pressureB,crowdDispatch,crowdWork,contactHeads,contactNext;readonly int contactResolution;CommandBuffer crowdCommands;
        internal bool CrowdEnabled {get;}
        internal void UseTiledCrowdForValidation(bool tiled){bool enabled=tiled&&resolution%16==0;shader.SetInt("_CrowdTiled",enabled?1:0);RecordCrowdCommands(enabled);}
        internal GraphicsBuffer PressureForValidation=>pressureA;internal GraphicsBuffer MassForValidation=>crowdMass;
        internal void ResetCrowdForValidation(){crowdGrid.SetData(new Vector4[navigation.CellCount]);pressureA.SetData(new float[navigation.CellCount]);pressureB.SetData(new float[navigation.CellCount]);}
        internal GraphicsBuffer SteeringForValidation=>steering;internal GraphicsBuffer CrowdForValidation=>crowdGrid;
        readonly PlanetDevelopment world;readonly PlanetPathfindingAdapter navigation;readonly ComputeShader shader,movement;
        readonly GraphicsBuffer snapshot,heads,next,damage,free,counters,sites,steering,eligibility,presence,objectives,noise,hearing;
        readonly GraphicsBuffer interestSources,interestRegions,activity,activeIndices,activeDispatch,lastSimulationTick;readonly int interestResolution;bool useInterest;
        public bool InterestManagement=true;public uint ActiveZombies {get;private set;}public uint DormantZombies=>useInterest?zombies>ActiveZombies?zombies-ActiveZombies:0:0;
        internal GraphicsBuffer RoamOriginsForValidation=>roamOrigins;
        internal GraphicsBuffer ActivityForValidation=>activity;internal GraphicsBuffer ActiveDispatchForValidation=>activeDispatch;
        GraphicsBuffer structures,structureDamage;Vector4[] structureData=new Vector4[128*3];uint[] clearStructureDamage=new uint[128];
        readonly uint[] reset=new uint[8],values=new uint[8];readonly int capacity,resolution,headCount;readonly List<Vector2> entries=new List<Vector2>();
        public readonly SwarmSchedule Schedule=new SwarmSchedule();
        readonly uint[] orderGoals={uint.MaxValue,uint.MaxValue},emptyGoals={uint.MaxValue,uint.MaxValue};bool objectivePending;
        bool disposed,pending;int waveAge;double nextRead;uint nextOrderTick;uint zombies,waveAlive;int patrol;int ambientRemaining;uint serial;
        internal void PrepareInterestForValidation(){shader.SetInt("_Count",world.unitCount);Dispatch("PrepareInterestValidation",world.unitCount);}
        internal uint FindDormantForValidation(){activeDispatch.SetData(new[]{uint.MaxValue},0,4,1);Dispatch("FindDormantValidation",world.unitCount);var data=new uint[1];activeDispatch.GetData(data,0,4,1);return data[0];}
        internal uint ZombieCountForValidation(){counters.SetData(new uint[4],0,4,4);Dispatch(useInterest?"StatisticsBatched":"Statistics",world.unitCount);var data=new uint[8];counters.GetData(data);return data[6];}
        internal void EmitNoiseForValidation(Vector3 direction){int cell=CubeSphere.Cell(world.definition.bodyId,direction,resolution).Index(resolution);noise.SetData(new[]{world.SimulationTicks+1},0,cell,1);}
        internal uint HeardCellForValidation(Vector3 direction){var data=new uint[1];int cell=CubeSphere.Cell(world.definition.bodyId,direction,resolution).Index(resolution);hearing.GetData(data,0,cell,1);return data[0];}
        internal PlanetNuclearScorch ScorchForValidation=>decals.ScorchForValidation;
        internal uint DecalCountForValidation=>decals.CountForValidation();
        internal uint WaveAliveForValidation=>waveAlive;
        public string Status=>(useInterest?$"LOCAL {ActiveZombies:N0} · DISTANT {DormantZombies:N0} · ":"")+(world.sessionMode==PlanetGameMode.Swarm?$"ZOMBIES {zombies:N0} · WAVE {Schedule.Wave} · "+(Schedule.Active?$"{Schedule.Remaining:N0} arriving":$"{Schedule.Countdown:0}s to wave"): $"ZOMBIES {zombies:N0}");
        public PlanetEncounter(PlanetDevelopment world,GraphicsBuffer units,GraphicsBuffer types,GraphicsBuffer traits,PlanetPathfindingAdapter navigation,int capacity,ComputeShader movement,int ambient,Deployment deployment=null)
        {
            CrowdEnabled=world.restoredCrowd&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-legacy-steering")<0;this.world=world;this.navigation=navigation;this.capacity=capacity;this.movement=movement;ambientRemaining=ambient;resolution=world.navigationResolution;roamOrigins=Buffer(capacity,8);
            interestResolution=Mathf.Clamp(Mathf.CeilToInt(2*world.definition.radius/1000),8,512);InterestManagement=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-no-interest")<0;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetEncounter"));if(CrowdEnabled){shader.EnableKeyword("PLANET_CROWD");movement.EnableKeyword("PLANET_CROWD");}else{shader.DisableKeyword("PLANET_CROWD");movement.DisableKeyword("PLANET_CROWD");}if(navigation.Regional!=null)shader.EnableKeyword("PLANET_REGIONAL_NAV");PlanetDevelopment.BindSurface(shader,world.definition);shader.SetInt("_Res",resolution);shader.SetInt("_Capacity",capacity);
            bool sparse=world.definition.radius>=PlanetDefinition.RegionalNavigationRadius&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-dense-spatial")<0;headCount=sparse?262144:navigation.CellCount;if(sparse)shader.EnableKeyword("PLANET_SPARSE_SPATIAL");shader.SetInt("_HeadCount",headCount);shader.SetInt("_SpatialRes",sparse?Mathf.CeilToInt(2*world.definition.radius/128):resolution);
            contactResolution=Mathf.Clamp(Mathf.CeilToInt(2*world.definition.radius/24),32,512);contactHeads=Buffer(6*contactResolution*contactResolution,4);contactNext=Buffer(capacity,4);shader.SetInt("_ContactRes",contactResolution);float maxContactRadius=ZombieDefinition.Archetype.radius;foreach(var type in world.Base.Catalog.units)if((int)type.movement<3)maxContactRadius=Mathf.Max(maxContactRadius,type.radius);shader.SetFloat("_MaxContactRadius",maxContactRadius);
            crowdDispatch=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments,4,4);crowdWork=Buffer(12*((resolution+15)/16)*((resolution+15)/16),4);shader.SetInt("_CrowdTiled",resolution%16==0?1:0);crowdMass=Buffer(navigation.CellCount,4);crowdGrid=Buffer(navigation.CellCount,16);pressureA=Buffer(navigation.CellCount,4);pressureB=Buffer(navigation.CellCount,4);crowdGrid.SetData(new Vector4[navigation.CellCount]);pressureA.SetData(new float[navigation.CellCount]);pressureB.SetData(new float[navigation.CellCount]);
            snapshot=Buffer(capacity,64);heads=Buffer(headCount,4);next=Buffer(capacity*(sparse?3:1),4);damage=Buffer(capacity,4);free=Buffer(capacity,4);counters=Buffer(8,4);steering=Buffer(capacity,16);structures=Buffer(128*3,16);structureDamage=Buffer(128,4);structureDamage.SetData(clearStructureDamage);
            interestSources=Buffer(6*interestResolution*interestResolution,4);interestRegions=Buffer(6*interestResolution*interestResolution,4);activity=Buffer(capacity,4);activeIndices=Buffer(capacity,4);lastSimulationTick=Buffer(capacity,4);activeDispatch=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments,6,4);activeDispatch.SetData(new uint[6]);
            if(deployment==null){deployment=new Deployment();var prepare=deployment.Prepare(world.definition,world.Rig.FocusDirection,resolution);while(prepare.MoveNext()){} }
            entries.AddRange(deployment.entries);var eligible=deployment.eligible;var home=world.Rig.FocusDirection;
            eligibility=Buffer(eligible.Length,4);eligibility.SetData(eligible);presence=Buffer(eligible.Length,4);presence.SetData(new uint[eligible.Length]);noise=Buffer(eligible.Length,4);noise.SetData(new uint[eligible.Length]);hearing=Buffer(eligible.Length,4);hearing.SetData(new uint[eligible.Length]);objectives=Buffer(2,4);
            shader.SetFloat("_Refuge",Mathf.Min(world.StrategyEnabled?1500:350,world.definition.radius*.8f));
            var refuges=new Vector4[2];for(int team=0;team<2;team++){var h=world.Base.Homes[team];refuges[team]=new Vector4(h.x,h.y,h.z,Mathf.Min(1500,world.definition.radius*.3f));}
            shader.SetInt("_SeparatedFactions",world.StrategyEnabled&&world.sessionMode!=PlanetGameMode.Swarm&&Vector3.Dot(world.Base.Homes[0],world.Base.Homes[1])<-.8f?1:0);
            shader.SetVectorArray("_StartingRefuges",refuges);movement.SetVectorArray("_StartingRefuges",refuges);shader.SetInt("_StartingRefugeCount",world.StrategyEnabled?2:0);movement.SetInt("_StartingRefugeCount",world.StrategyEnabled?2:0);shader.SetInt("_StartingRefugeActive",1);shader.SetVector("_Home",home);shader.SetInt("_SpawnSeed",world.definition.terrainSeed);
            sites=Buffer(entries.Count,8);sites.SetData(entries);shader.SetInt("_SiteCount",entries.Count);
            foreach(string name in new[]{"ClearCrowd","SplatCrowd","BuildCrowd","RelaxCrowd","FinishCrowd","Snapshot","ClearCells","BuildCells","Steer","Fight","ApplyDamage","FindFree","Spawn","Statistics","StatisticsBatched","Defences","Objectives","Hearing","InitializeInterest","ClearInterest","MarkInterest","MarkObservers","ExpandInterest","ClassifyInterest","BuildActiveDispatch","PrepareInterestValidation","FindDormantValidation","StrategicBlast","StrategicStamp"}){int k=shader.FindKernel(name);if(!shader.IsSupported(k))throw new InvalidOperationException("Unsupported planetary simulation kernel: "+name);shader.SetBuffer(k,"_RoamOrigins",roamOrigins);shader.SetBuffer(k,"_RoamOriginsRead",roamOrigins);shader.SetBuffer(k,"_ContactHeads",contactHeads);shader.SetBuffer(k,"_ContactNext",contactNext);shader.SetBuffer(k,"_ContactHeadsRead",contactHeads);shader.SetBuffer(k,"_ContactNextRead",contactNext);shader.SetBuffer(k,"_PresenceRead",presence);shader.SetBuffer(k,"_CrowdDispatch",crowdDispatch);shader.SetBuffer(k,"_CrowdDispatchRead",crowdDispatch);shader.SetBuffer(k,"_CrowdWork",crowdWork);shader.SetBuffer(k,"_CrowdWorkRead",crowdWork);shader.SetBuffer(k,"_CrowdMass",crowdMass);shader.SetBuffer(k,"_CrowdGrid",crowdGrid);shader.SetBuffer(k,"_CrowdRead",crowdGrid);shader.SetBuffer(k,"_PressureRead",pressureA);shader.SetBuffer(k,"_PressureOther",pressureB);shader.SetBuffer(k,"_PressureWrite",pressureB);shader.SetBuffer(k,"_Groups",navigation.Groups);shader.SetBuffer(k,"_SnapshotRead",snapshot);shader.SetBuffer(k,"_UnitRead",units);shader.SetBuffer(k,"_ActiveRead",activeIndices);shader.SetBuffer(k,"_ActivityRead",activity);shader.SetBuffer(k,"_HeadsRead",heads);shader.SetBuffer(k,"_NextRead",next);shader.SetBuffer(k,"_NoiseRead",noise);shader.SetBuffer(k,"_HearingRead",hearing);shader.SetBuffer(k,"_LastSimulationTick",lastSimulationTick);shader.SetBuffer(k,"_InterestSources",interestSources);shader.SetBuffer(k,"_InterestRegions",interestRegions);shader.SetBuffer(k,"_UnitActivity",activity);shader.SetBuffer(k,"_ActiveIndices",activeIndices);shader.SetBuffer(k,"_ActiveDispatch",activeDispatch);shader.SetBuffer(k,"_ActiveCounts",activeDispatch);shader.SetBuffer(k,"_Hearing",hearing);shader.SetBuffer(k,"_NavCosts",navigation.Costs);shader.SetBuffer(k,"_Presence",presence);shader.SetBuffer(k,"_Objectives",objectives);shader.SetBuffer(k,"_Eligibility",eligibility);shader.SetBuffer(k,"_Noise",noise);shader.SetBuffer(k,"_Units",units);shader.SetBuffer(k,"_Snapshot",snapshot);shader.SetBuffer(k,"_Heads",heads);shader.SetBuffer(k,"_Next",next);shader.SetBuffer(k,"_Damage",damage);shader.SetBuffer(k,"_Free",free);shader.SetBuffer(k,"_Counters",counters);shader.SetBuffer(k,"_Sites",sites);shader.SetBuffer(k,"_Types",types);shader.SetBuffer(k,"_Traits",traits);shader.SetBuffer(k,"_Steering",steering);shader.SetBuffer(k,"_Structures",structures);shader.SetBuffer(k,"_StructureDamage",structureDamage);}
            navigation.Regional?.Bind(shader,shader.FindKernel("Steer"));
            shader.SetFloat("_SpawnLocalRadius",world.definition.radius>=PlanetDefinition.RegionalNavigationRadius&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-interest-validation")<0?2000:0);
            decals=new PlanetCombatDecals(world.TerrainMaterial,shader);nuclear=new PlanetNuclearPresentation(world);RecordCrowdCommands(resolution%16==0);
            shader.SetInt("_UseInterest",0);movement.SetInt("_PlanetInterestEnabled",0);movement.EnableKeyword("PLANET_ENCOUNTERS");int move=movement.FindKernel("Move");movement.SetBuffer(move,"_RoamOriginsRead",roamOrigins);movement.SetBuffer(move,"_PlanetSteering",steering);movement.SetBuffer(move,"_PlanetActivity",activity);movement.SetBuffer(move,"_PlanetLastSimulationTick",lastSimulationTick);world.Commands.PlayerGroupCount=15;
            foreach(string name in new[]{"MoveActive","MoveDormant"}){int k=movement.FindKernel(name);movement.SetBuffer(k,"_RoamOriginsRead",roamOrigins);movement.SetBuffer(k,"_PlanetUnits",units);movement.SetBuffer(k,"_PlanetCosts",navigation.Costs);movement.SetBuffer(k,"_PlanetField",navigation.Global);movement.SetBuffer(k,"_Groups",navigation.Groups);movement.SetBuffer(k,"_Archetypes",types);movement.SetBuffer(k,"_Traits",traits);movement.SetBuffer(k,"_PlanetSteering",steering);movement.SetBuffer(k,"_PlanetActivity",activity);movement.SetBuffer(k,"_PlanetLastSimulationTick",lastSimulationTick);movement.SetBuffer(k,"_PlanetActiveIndices",activeIndices);movement.SetBuffer(k,"_PlanetActiveCounts",activeDispatch);navigation.Regional?.Bind(movement,k);}
            navigation.SetGoal(17,home,MovementClass.Infantry);navigation.SetGoal(16,home,MovementClass.Infantry);
            if(!world.DeferredInitialization){var initial=SeedInitialForces(ambient);while(initial.MoveNext()){} }

        }
        internal System.Collections.IEnumerator SeedInitialForces(int ambient){if(world.SystemRuntime?.Civilisation!=null&&!world.SystemRuntime.Civilisation.IsHome(world.definition.bodyId))yield break;
            if(world.sessionMode!=PlanetGameMode.Swarm&&(world.StrategyEnabled||ambient>0||world.sessionMode==PlanetGameMode.AiTest)){
                if(world.StrategyEnabled){for(int i=0;i<32;i++){
                    // Keep the staging area clear of warehouse/housing footprints. A failed
                    // GPU clearance query must not silently discard a starting soldier.
                    bool placed=false;for(int attempt=0;attempt<32&&!placed;attempt++){
                        int slot=i+attempt;var direction=world.Base.TeamOffset(1,new Vector2((slot%8-3.5f)*10,100+slot/8*12));
                        placed=world.TrySpawnUnit(i%2==0?0:3,direction,1);
                    }
                    if(!placed)Debug.LogWarning("Starting garrison has no clear deployment position for soldier "+i);
                    yield return null;
                }}
                else Spawn(32,1,0,16);
            }
        }
        // Immutable terrain deployment metadata. This is navigation-cell data, never a CPU army mirror.
        public sealed class Deployment
        {
            internal readonly List<Vector2> entries=new List<Vector2>();internal uint[] eligible;
            public System.Collections.IEnumerator Prepare(PlanetDefinition definition,Vector3 home,int resolution){
                int count=6*resolution*resolution;var surface=new PlanetSurface(definition);var visited=new bool[count];var queue=new Queue<PlanetNavCoordinate>();
                int Index(PlanetNavCoordinate c)=>(int)c.face*resolution*resolution+c.y*resolution+c.x;
                var start=CubeSphere.Cell(definition.bodyId,home,resolution);queue.Enqueue(start);visited[Index(start)]=true;
                var steps=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};int work=0;
                while(queue.Count>0){
                    var cell=queue.Dequeue();var d=CubeSphere.Direction(cell,resolution);if(CubeSphere.ArcDistance(home,d,definition.radius)>Mathf.Min(350,definition.radius*.8f))entries.Add(CubeSphere.Encode(d));
                    foreach(var step in steps){var neighbour=CubeSphere.Neighbour(cell,step.x,step.y,resolution);int i=Index(neighbour);if(visited[i])continue;visited[i]=true;if(surface.SampleHeight(CubeSphere.Direction(neighbour,resolution))>=definition.oceanHeight)queue.Enqueue(neighbour);}
                    if(++work%256==0)yield return null;
                }
                if(entries.Count==0)throw new InvalidOperationException("Planet has no reachable land outside the starting refuge for zombies.");
                eligible=new uint[count];for(int i=0;i<count;i++){
                    var c=new PlanetNavCoordinate(definition.bodyId,(CubeFace)(i/(resolution*resolution)),i%resolution,i/resolution%resolution);var d=CubeSphere.Direction(c,resolution);
                    if(surface.SampleHeight(d)>=definition.oceanHeight){eligible[i]=visited[i]?4u:0u;if(CubeSphere.ArcDistance(home,d,definition.radius)>Mathf.Min(350,definition.radius*.8f))eligible[i]|=visited[i]?3u:1u;}
                    if(i%256==255)yield return null;
                }
            }
        }
        // The flat GpuCrowd records its Jacobi passes into a command buffer too.
        // All bindings here are lifetime-stable; gameplay submits one cached sequence per tick.
        void RecordCrowdCommands(bool tiled){
            if(crowdCommands==null)crowdCommands=new CommandBuffer{name="Planet crowd pressure"};else crowdCommands.Clear();
            var cmd=crowdCommands;cmd.BeginSample(GpuSamples.Get("Planet.CrowdPressure"));
            cmd.SetComputeIntParam(shader,"_Res",resolution);cmd.SetComputeFloatParam(shader,"_PlanetRadius",world.definition.radius);cmd.SetComputeIntParam(shader,"_CrowdTiled",tiled?1:0);
            int relax=shader.FindKernel("RelaxCrowd"),finish=shader.FindKernel("FinishCrowd");
            foreach(int kernel in new[]{relax,finish}){cmd.SetComputeBufferParam(shader,kernel,"_NavCosts",navigation.Costs);cmd.SetComputeBufferParam(shader,kernel,"_CrowdDispatchRead",crowdDispatch);cmd.SetComputeBufferParam(shader,kernel,"_CrowdWorkRead",crowdWork);}
            cmd.SetComputeBufferParam(shader,relax,"_CrowdRead",crowdGrid);
            for(int i=0;i<8;i++){cmd.SetComputeBufferParam(shader,relax,"_PressureRead",i%2==0?pressureA:pressureB);cmd.SetComputeBufferParam(shader,relax,"_PressureWrite",i%2==0?pressureB:pressureA);cmd.DispatchCompute(shader,relax,crowdDispatch,0);}
            cmd.SetComputeBufferParam(shader,finish,"_PressureRead",pressureA);cmd.SetComputeBufferParam(shader,finish,"_CrowdGrid",crowdGrid);cmd.DispatchCompute(shader,finish,crowdDispatch,0);
            cmd.EndSample(GpuSamples.Get("Planet.CrowdPressure"));
        }
        int Index(PlanetNavCoordinate c)=>(int)c.face*resolution*resolution+c.y*resolution+c.x;
        static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
        void Dispatch(string name,int count){shader.Dispatch(shader.FindKernel(name),Mathf.Max(1,(count+127)/128),1,1);}
        public uint Spawn(int requested,int team,int type,int group,bool wave=false)
        {
            reset[0]=(uint)world.unitCount;counters.SetData(reset);shader.SetInt("_Count",world.unitCount);Dispatch("FindFree",world.unitCount);shader.SetInt("_SpawnCount",requested);shader.SetInt("_SpawnWave",wave?1:0);shader.SetInt("_SpawnTeam",team);shader.SetInt("_SpawnType",type);shader.SetInt("_SpawnGroup",group);shader.SetInt("_Serial",(int)serial);Dispatch("Spawn",requested);serial+=(uint)requested;counters.GetData(values);world.SetArmyCount((int)values[0]);if(team==2)zombies+=values[2];return values[2];
        }
        public void WakeDefenceOrders(){nextOrderTick=Math.Min(nextOrderTick,world.SimulationTicks);}
        public void BeforeTick()
        {
            for(int strike=strikes.Count-1;strike>=0;strike--)if(world.SimulationTicks>=strikes[strike].tick){var hit=strikes[strike];strikes.RemoveAt(strike);Detonate(hit.direction,hit.radius,hit.damage);}
            shader.SetInt("_StartingRefugeActive",world.StrategyEnabled&&world.SimulationTicks<5400?1:0);movement.SetInt("_StartingRefugeActive",world.StrategyEnabled&&world.SimulationTicks<5400?1:0);
            if(ambientRemaining>0){uint added=Spawn(Math.Min(256,ambientRemaining),2,31,17);ambientRemaining-=(int)added;}
            if(world.sessionMode==PlanetGameMode.Swarm){Schedule.Advance(BattleSettings.Tick);if(Schedule.Active)waveAge++;else waveAge=0;if(Schedule.Active&&Schedule.Remaining>0){uint added=Spawn((int)Math.Min(256UL,Schedule.Remaining),2,31,17,true);Schedule.ConfirmSpawn(added);}if(waveAge>45)Schedule.TryClear(waveAlive,pending);}
            shader.SetInt("_AiTest",world.sessionMode==PlanetGameMode.AiTest?1:0);
            if(world.SimulationTicks>=nextOrderTick){nextOrderTick=world.SimulationTicks+240;patrol++;if(!objectivePending){objectives.SetData(emptyGoals);Dispatch("Objectives",navigation.CellCount);objectivePending=true;AsyncGPUReadback.Request(objectives,result=>{if(disposed)return;objectivePending=false;if(result.hasError)return;var data=result.GetData<uint>();orderGoals[0]=data[0];orderGoals[1]=data[1];});}var goals=orderGoals;
                for(int team=0;team<2;team++){bool defending=world.Base.TryProtectionTarget(team,out var protectionTarget);if(team==0&&world.sessionMode!=PlanetGameMode.AiTest&&!defending)continue;Vector3 target;if(defending)target=protectionTarget;else if(goals[team]!=uint.MaxValue){int i=(int)(goals[team]&131071u);target=CubeSphere.Direction(new PlanetNavCoordinate(world.definition.bodyId,(CubeFace)(i/(resolution*resolution)),i%resolution,i/resolution%resolution),resolution);}else if(world.StrategyEnabled){var enemy=world.Base.Buildings.Find(b=>b.team!=team&&!b.removed&&b.definition.id=="command");target=world.SimulationTicks<1350?world.Base.Homes[team]:enemy!=null?CubeSphere.Decode(enemy.position):world.Base.Homes[1-team];}else target=CubeSphere.Decode(entries[(patrol*811+team*271)%entries.Count]);// A remote rival is a strategic destination, not a walkable local ground order.
                    if(!defending&&world.StrategyEnabled&&world.sessionMode!=PlanetGameMode.Swarm&&Vector3.Dot(world.Base.Homes[0],world.Base.Homes[1])<-.8f&&CubeSphere.ArcDistance(world.Base.Homes[team],target,world.definition.radius)>5000){
                        float angle=((world.SimulationTicks>1350?world.SimulationTicks-1350:0)/3600)*2.3999632f+team*Mathf.PI*.5f;
                        target=world.SimulationTicks<1350?world.Base.Homes[team]:world.Base.TeamOffset(team,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*850);
                    }
                    navigation.SetGoalQueued(team==0?15:16,target,MovementClass.Infantry);}
                var building=world.Base.Buildings.Find(b=>!b.removed&&b.definition.id=="command")??world.Base.Buildings.Find(b=>!b.removed);navigation.SetGoalQueued(17,building!=null?CubeSphere.Decode(building.position):CubeSphere.Decode(entries[patrol*811%entries.Count]),MovementClass.Infantry);
            }
            int structureCount=world.Base.Buildings.Count;EnsureStructures(structureCount);shader.SetInt("_StructureCount",structureCount);
            for(int i=0;i<structureCount;i++){var b=world.Base.Buildings[i];var d=CubeSphere.Decode(b.position);structureData[i*3]=new Vector4(d.x,d.y,d.z,Mathf.Sqrt(b.definition.footprintWidth*b.definition.footprintWidth+b.definition.footprintDepth*b.definition.footprintDepth)*4);structureData[i*3+1]=new Vector4(b.removed?0:b.health,b.Armour,b.team,b.Complete&&b.powered&&!b.paused?b.definition.id=="silo"?0:b.definition.weaponRange:0);structureData[i*3+2]=new Vector4(b.definition.weaponDamage,0,0,0);}
            structures.SetData(structureData);
            shader.SetInt("_Count",world.unitCount);shader.SetFloat("_Dt",BattleSettings.Tick);shader.SetInt("_Tick",(int)world.SimulationTicks);bool wasInterested=useInterest;useInterest=InterestManagement&&zombies>=8192&&world.definition.radius*2>world.InterestRadius;
            if(wasInterested!=useInterest){shader.SetInt("_UseInterest",useInterest?1:0);movement.SetInt("_PlanetInterestEnabled",useInterest?1:0);}if(useInterest)movement.SetInt("_PlanetInterestTick",(int)world.SimulationTicks);
            if(useInterest){if(!wasInterested)Dispatch("InitializeInterest",world.unitCount);shader.SetInt("_InterestRes",interestResolution);shader.SetVector("_InterestObserver",world.InterestDirection);shader.SetFloat("_InterestRadius",world.InterestRadius);Dispatch("ClearInterest",6*interestResolution*interestResolution);Dispatch("MarkInterest",world.unitCount);Dispatch("MarkObservers",structureCount);Dispatch("ExpandInterest",6*interestResolution*interestResolution);Dispatch("ClassifyInterest",world.unitCount);Dispatch("BuildActiveDispatch",1);}
            Dispatch("Snapshot",world.unitCount);Dispatch("ClearCells",CrowdEnabled?Math.Max(headCount,6*contactResolution*contactResolution):headCount);Dispatch("BuildCells",world.unitCount);if(world.SimulationTicks%5==0)Dispatch("Hearing",navigation.CellCount);
            if(CrowdEnabled){
                Dispatch("BuildCrowd",navigation.CellCount);
                Graphics.ExecuteCommandBuffer(crowdCommands);
            }
            navigation.Regional?.Bind(shader,shader.FindKernel("Steer"));shader.SetBuffer(shader.FindKernel("Steer"),"_NavField",navigation.Global);DispatchActive("Steer");
        }
        public void MoveUnits(){
            if(useInterest&&navigation.Regional!=null){int active=movement.FindKernel("MoveActive"),dormant=movement.FindKernel("MoveDormant");movement.DispatchIndirect(active,activeDispatch,0);movement.Dispatch(dormant,(world.unitCount+4095)/4096,1,1);}
            else movement.Dispatch(movement.FindKernel("Move"),(world.unitCount+127)/128,1,1);
        }
        void DispatchActive(string name){if(useInterest)shader.DispatchIndirect(shader.FindKernel(name),activeDispatch,0);else Dispatch(name,world.unitCount);}
        public void AfterTick()
        {
            DispatchActive("Fight");Dispatch("Defences",world.Base.Buildings.Count);Dispatch("ApplyDamage",world.unitCount);
            if(pending||Time.realtimeSinceStartupAsDouble<nextRead)return;nextRead=Time.realtimeSinceStartupAsDouble+.5;
            // Clear only the statistics tail; spawning counters occupy the first three words.
            counters.SetData(new uint[4],0,4,4);Dispatch(useInterest?"StatisticsBatched":"Statistics",world.unitCount);pending=true;
            AsyncGPUReadback.Request(structureDamage,result=>{if(disposed||result.hasError)return;var damage=result.GetData<uint>();for(int i=0;i<Math.Min(damage.Length,world.Base.Buildings.Count);i++)if(damage[i]>0)world.Base.Damage(world.Base.Buildings[i],damage[i]);});structureDamage.SetData(clearStructureDamage);
            if(useInterest)AsyncGPUReadback.Request(activeDispatch,result=>{if(!disposed&&!result.hasError)ActiveZombies=result.GetData<uint>()[5];});
            AsyncGPUReadback.Request(counters,result=>{if(disposed)return;pending=false;if(!result.hasError){var statistics=result.GetData<uint>();zombies=statistics[6];waveAlive=statistics[7];}});
        }
        void EnsureStructures(int count){
            if(count<=structureDamage.count)return;int oldCount=structureDamage.count,capacity=Mathf.NextPowerOfTwo(count);var old=structureDamage;
            structures.Dispose();structures=Buffer(capacity*3,16);structureDamage=Buffer(capacity,4);structureData=new Vector4[capacity*3];clearStructureDamage=new uint[capacity];
            int copy=shader.FindKernel("GrowStructureDamage");shader.SetInt("_OldStructureCapacity",oldCount);shader.SetInt("_NewStructureCapacity",capacity);shader.SetBuffer(copy,"_PreviousStructureDamage",old);shader.SetBuffer(copy,"_StructureDamage",structureDamage);shader.Dispatch(copy,(capacity+127)/128,1,1);
            foreach(string name in new[]{"Steer","Fight","Defences","MarkObservers"}){int kernel=shader.FindKernel(name);shader.SetBuffer(kernel,"_Structures",structures);shader.SetBuffer(kernel,"_StructureDamage",structureDamage);}old.Dispose();
        }
        public void UpdatePresentation()=>nuclear.Update();
        public const float TestNukeCraterRadius=360,RocketBlastRadius=24;
        public int RocketImpacts {get;private set;}
        void ExplosionDamage(Vector3 direction,float radius,float damage,float hearingRadius){
            direction.Normalize();shader.SetInt("_Count",world.unitCount);Dispatch("Snapshot",world.unitCount);
            shader.SetVector("_StrategicBlast",new Vector4(direction.x,direction.y,direction.z,damage));shader.SetFloat("_StrategicRadius",radius);shader.SetFloat("_StrategicHearingRadius",hearingRadius);Dispatch("StrategicBlast",world.unitCount);Dispatch("ApplyDamage",world.unitCount);
            foreach(var b in world.Base.Buildings){float distance=CubeSphere.ArcDistance(direction,CubeSphere.Decode(b.position),world.definition.radius);if(!b.removed&&distance<radius)world.Base.Damage(b,(uint)(damage*(1-distance/radius*.75f)));}
        }
        internal void EmitExplosionNoiseForValidation(Vector3 direction,float range)=>ExplosionDamage(direction,0,0,range);
        public void RocketImpact(Vector3 direction){ExplosionDamage(direction,RocketBlastRadius,450,600);RocketImpacts++;}
        public void Detonate(Vector3 direction,float radius=120,float damage=5000,float craterRadius=-1){
            direction.Normalize();ExplosionDamage(direction,radius,damage,Mathf.Max(2000,radius*8));float crater=craterRadius>0?craterRadius:radius;
            decals.StampNuke(direction,crater,world.definition.radius);
            if(world.deformTerrainOnNuke){if(world.definition.Edits.AddCrater(direction,crater))world.TerrainChanged();else world.Base.Notice="Persistent crater budget reached (32); blast damage still applied";}
            nuclear.Play(direction,Mathf.Max(radius,crater));NukesDetonated++;
        }
        public void Dispose(){disposed=true;nuclear.Dispose();roamOrigins.Dispose();crowdCommands?.Dispose();decals.Dispose();movement.DisableKeyword("PLANET_ENCOUNTERS");movement.DisableKeyword("PLANET_CROWD");foreach(var buffer in new[]{contactHeads,contactNext,crowdDispatch,crowdWork,crowdMass,crowdGrid,pressureA,pressureB,snapshot,heads,next,damage,free,counters,sites,steering,eligibility,presence,objectives,noise,hearing,interestSources,interestRegions,activity,activeIndices,activeDispatch,lastSimulationTick})buffer.Dispose();ArmyLifetime.Destroy(shader);}
    }
}

