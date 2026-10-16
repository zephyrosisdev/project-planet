using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public struct GpuStructure
    {
        public Vector2 position;
        public float radius,health,armour;
        public uint team;
        public float range,damage;
        public uint flags;
        public float vision,detection,shield;
        public uint level,targetMask;
        public float reload,repair;
        public const int Stride=64;
    }
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    struct GpuSpawn
    {
        public Vector2 position,destination;
        public uint type,team;
        public uint homeBuilding;
        public uint rallyTarget;
    }
    public sealed partial class GpuBattle
    {
        public const int MaxBuildings=512,FogResolution=128;
        public GraphicsBuffer TraitsBuffer => rtsTraits;
        public GraphicsBuffer FogBuffer => fog;
        public GraphicsBuffer MissileBuffer => missiles;
        public GraphicsBuffer StructureAimBuffer => structureAim;
        public void ResetStructureAim(int index){structureAim.SetData(new Vector4[1],0,index,1);}
        public byte[] PlayerVisibility {get;private set;}
        public uint[] SensorData {get;private set;}
        public uint[] RadarContacts {get;private set;}
        public Vector4[] TeamModifiers={Vector4.one,Vector4.one};
        public bool LogisticsEnabled {get=>Settings.logistics;set=>Settings.logistics=value;}
        public Action<uint[]> OnStructureDamage;
        public Action<uint[]> OnSectorControl;
        ComputeShader rtsShader;
        int missileKernel,testNukeKernel;
        GraphicsBuffer rtsTraits,structures,structureDamage,structureTimers,structureAim,freeSlots,freeCount,spawns,spawnResults,missions,fog,radar,sensors,controlGroups,sectors,contacts,support,operations,missiles,intercepts,routePoints,routeCounts,routeReferences;
        uint[] routeUsers=new uint[64];
        readonly double[] routeBooked=new double[64];
        readonly List<GpuSpawn> pendingSpawns=new List<GpuSpawn>();
        readonly List<Action<bool>> spawnCallbacks=new List<Action<bool>>();
        bool spawnPending,rtsPending;
        uint rtsTick;
        int orderMovementFilter=-1;
        double nextRts;
        int structureCount;
        bool testNukePending;Vector2 testNukePoint;
        public int TestNukesDetonated {get;private set;}
        public Vector2 LastTestNuke {get;private set;}
        public void RequestTestNuke(Vector2 point){testNukePoint=Field.Canonical(point);testNukePending=true;}
        void RecordTestNuke(Camera camera)
        {
            if(!testNukePending)return;testNukePending=false;LastTestNuke=testNukePoint;TestNukesDetonated++;
            BuildGrid();cmd.SetBufferData(structureDamage,new uint[MaxBuildings]);CombatVisualParameters(rtsShader,camera);cmd.SetComputeVectorParam(rtsShader,"_TestNuke",new Vector4(testNukePoint.x,testNukePoint.y,5000,120));RtsDispatch("TestNuke",1);Dispatch("ApplyDamage",Settings.activeUnits);
            if(Settings.baseBuilding)cmd.RequestAsyncReadback(structureDamage,r=>{if(!disposed&&!r.hasError)OnStructureDamage?.Invoke(r.GetData<uint>().ToArray());});
        }
        public int ProducedUnits {get;private set;}
        public uint RtsSteps => rtsTick;
        void InitializeRts(int typeCount)
        {
            weaponNoise=Buffer(typeCount,4,"Catalogue weapon explosive profiles");weaponNoise.SetData(new float[typeCount]);
            rtsShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Rts"));
            missileKernel=rtsShader.FindKernel("MissileOperations");testNukeKernel=rtsShader.FindKernel("TestNuke");
            rtsTraits=Buffer(typeCount,UnitTraits.Stride,"Unit sensors armour and logistics");
            var fallback=new UnitTraits[typeCount];for(int i=0;i<typeCount;i++)fallback[i]=new UnitTraits {vision=100,detection=100,signature=1,targetMask=31,scale=Vector4.one};rtsTraits.SetData(fallback);
            structures=Buffer(MaxBuildings,GpuStructure.Stride,"CPU structures");structures.SetData(new GpuStructure[MaxBuildings]);
            structureDamage=Buffer(MaxBuildings,4,"GPU structure damage");structureDamage.SetData(new uint[MaxBuildings]);
            structureTimers=Buffer(MaxBuildings,4,"Structure weapon timers");structureTimers.SetData(new float[MaxBuildings]);
            structureAim=Buffer(MaxBuildings,16,"GPU building weapon aim");structureAim.SetData(new Vector4[MaxBuildings]);
            freeSlots=Buffer(Settings.capacity,4,"GPU vacant unit slots");freeCount=Buffer(1,4,"GPU vacant slot count");freeCount.SetData(new[]{(uint)Settings.capacity});
            spawns=Buffer(64,32,"Production spawn commands");spawnResults=Buffer(64,4,"Production acknowledgements");
            missions=Buffer(Settings.capacity,32,"GPU scouting missions");
            routePoints=Buffer(1024,8,"Shared patrol waypoint commands");routePoints.SetData(new Vector2[1024]);
            routeCounts=Buffer(64,4,"Patrol route lengths");routeCounts.SetData(new uint[64]);
            routeReferences=Buffer(64,4,"Active patrol route reduction");routeReferences.SetData(new uint[64]);
            operations=Buffer(Settings.capacity,48,"GPU aircraft operations and transport manifests");
            missiles=Buffer(32,48,"Strategic ballistic missile pool");missiles.SetData(new ProjectileState[32]);
            intercepts=Buffer(MaxBuildings,4,"Bounded missile interception claims");
            fog=Buffer(FogResolution*FogResolution*2,4,"Persistent explored and visible cells");fog.SetData(new uint[FogResolution*FogResolution*2]);
            radar=Buffer(FogResolution*FogResolution*2,4,"Radar detection cells");radar.SetData(new uint[FogResolution*FogResolution*2]);
            sensors=Buffer(MaxBuildings*4,4,"Building contacts and threats");sensors.SetData(new uint[MaxBuildings*4]);
            controlGroups=Buffer(Settings.capacity,4,"GPU control group bit masks");
            sectors=Buffer(32,4,"Territory presence reduction");
            contacts=Buffer(FogResolution*FogResolution*2,4,"Reduced visual and radar contacts");
            support=Buffer(Settings.spatialResolution*Settings.spatialResolution*2,4,"GPU repair and supply influence");
            int k=rtsShader.FindKernel("InitializeRts");rtsShader.SetBuffer(k,"_FreeSlots",freeSlots);rtsShader.SetBuffer(k,"_Missions",missions);rtsShader.SetBuffer(k,"_ControlGroups",controlGroups);rtsShader.SetInt("_Capacity",Settings.capacity);rtsShader.Dispatch(k,(Settings.capacity+127)/128,1,1);
            foreach(var name in new[]{"MoveUnits","ResolveContacts","FindPick","SelectUnits","AcquireTargets","FireWeapons","MoveProjectiles","ClassifyVisibility","UpdateStatistics"}) {
                int kernel=kernels[name];shader.SetBuffer(kernel,"_Traits",rtsTraits);
            }
            foreach(var name in new[]{"SelectedFlags","CountOrderOccupancy","ApplyOrders","UpdateStatistics"})shader.SetBuffer(kernels[name],"_Types",types);
            foreach(var name in new[]{"MoveUnits","AcquireTargets","FireWeapons"})shader.SetBuffer(kernels[name],"_Structures",structures);
            foreach(var pair in new[]{("_CellCounts",counts),("_CellOffsets",offsets),("_Indices",indices),("_ShotCounts",counts),("_ShotOffsets",offsets),("_ShotIndices",indices)})shader.SetBuffer(kernels["FireWeapons"],pair.Item1,pair.Item2);
            foreach(var name in new[]{"FindPick","AcquireTargets","ClassifyVisibility","FireWeapons","ClassifyProjectiles"}) { shader.SetBuffer(kernels[name],"_Fog",fog);shader.SetBuffer(kernels[name],"_Radar",radar); }
            shader.SetBuffer(kernels["FireWeapons"],"_Missiles",missiles);
            foreach(var name in new[]{"MoveUnits","FireWeapons"}){shader.SetBuffer(kernels[name],"_Operations",operations);shader.SetBuffer(kernels[name],"_SupportInfluence",support);}
            shader.SetBuffer(kernels["ApplyDamage"],"_ControlGroups",controlGroups);shader.SetBuffer(kernels["ApplyDamage"],"_FreeSlots",freeSlots);shader.SetBuffer(kernels["ApplyDamage"],"_FreeCount",freeCount);
        }
        public void ConfigureRoster(UnitDefinition[] roster)
        {
            var data=new UnitTraits[roster.Length+(Settings.ZombiesEnabled?1:0)];var blasts=new float[data.Length];for(int i=0;i<roster.Length;i++){data[i]=roster[i].Traits;blasts[i]=Mathf.Max(roster[i].explosionRadius,roster[i].splash);}if(Settings.ZombiesEnabled)data[roster.Length]=ZombieDefinition.Traits;rtsTraits.SetData(data);weaponNoise.SetData(blasts);
        }
        public void UploadStructures(GpuStructure[] data,int count)
        {
            SwarmTargetsChanged(data,count);
            structureCount=Mathf.Clamp(count,0,MaxBuildings);structures.SetData(data,0,0,Mathf.Max(1,structureCount));
        }
        public bool RequestSpawn(int type,int team,Vector2 position,Vector2 rally,Action<bool> callback,int homeBuilding=-1,int rallyTarget=-1)
        {
            if(!Settings.baseBuilding || pendingSpawns.Count>=64 || type<0)return false;
            pendingSpawns.Add(new GpuSpawn {position=position,destination=rally,type=(uint)type,team=(uint)team,homeBuilding=unchecked((uint)homeBuilding),rallyTarget=unchecked((uint)rallyTarget)});spawnCallbacks.Add(callback);return true;
        }
        void RtsBind(int k)
        {
            if(k==missileKernel||k==testNukeKernel)BindCombatVisuals(rtsShader,k);
            cmd.SetComputeBufferParam(rtsShader,k,"_StructureAim",structureAim);
            cmd.SetComputeIntParam(rtsShader,"_Spectator",Settings.spectatorMode?1:0);
            BindZombieNoiseRts(k);
            cmd.SetComputeBufferParam(rtsShader,k,"_CombatFog",fog);cmd.SetComputeBufferParam(rtsShader,k,"_CombatRadar",radar);cmd.SetComputeBufferParam(rtsShader,k,"_CombatUnits",current);
            cmd.SetComputeBufferParam(rtsShader,k,"_WeaponNoise",weaponNoise);
            cmd.SetComputeBufferParam(rtsShader,k,"_StructureCells",Field.StructureCells);
            cmd.SetComputeIntParam(rtsShader,"_StructureRes",Field.StructureResolution);
            cmd.SetComputeBufferParam(rtsShader,k,"_Projectiles",Projectiles);
            cmd.SetComputeIntParam(rtsShader,"_ProjectileCapacity",Settings.projectileCapacity);
            cmd.SetComputeIntParam(rtsShader,"_UseVfx",Settings.vfx?1:0);
            foreach(var pair in new[]{("_Units",current),("_Next",next),("_Types",types),("_Traits",rtsTraits),("_Structures",structures),("_StructureDamage",structureDamage),("_StructureTimers",structureTimers),("_FreeSlots",freeSlots),("_FreeCount",freeCount),("_Spawns",spawns),("_SpawnResults",spawnResults),("_Missions",missions),("_RoutePoints",routePoints),("_RouteCounts",routeCounts),("_RouteReferences",routeReferences),("_Operations",operations),("_Missiles",missiles),("_Intercepts",intercepts),("_Fog",fog),("_Radar",radar),("_Sensors",sensors),("_ControlGroups",controlGroups),("_SectorCounts",sectors),("_Contacts",contacts),("_Support",support),("_Damage",damage),("_RouteProgress",routeProgress),("_Costs",Field.Costs),("_CellCounts",counts),("_CellOffsets",offsets),("_Indices",indices)})
                cmd.SetComputeBufferParam(rtsShader,k,pair.Item1,pair.Item2);
            cmd.SetComputeTextureParam(rtsShader,k,"_Terrain",Field.Terrain);
            cmd.SetComputeIntParam(rtsShader,"_Active",Settings.activeUnits);cmd.SetComputeIntParam(rtsShader,"_Capacity",Settings.capacity);
            cmd.SetComputeIntParam(rtsShader,"_BuildingCount",structureCount);cmd.SetComputeIntParam(rtsShader,"_FogRes",FogResolution);
            cmd.SetComputeIntParam(rtsShader,"_GridRes",Settings.spatialResolution);cmd.SetComputeIntParam(rtsShader,"_NavRes",Field.Resolution);
            cmd.SetComputeFloatParam(rtsShader,"_WorldSize",Field.Size);cmd.SetComputeFloatParam(rtsShader,"_Step",.25f);
            cmd.SetComputeVectorArrayParam(rtsShader,"_TeamModifiers",TeamModifiers);
            cmd.SetComputeIntParam(rtsShader,"_RtsTick",(int)rtsTick);cmd.SetComputeIntParam(rtsShader,"_Logistics",Settings.logistics?1:0);
        }
        void RtsDispatch(string name,int count) { int k=rtsShader.FindKernel(name);RtsBind(k);cmd.DispatchCompute(rtsShader,k,(Mathf.Max(1,count)+127)/128,1,1); }
        void RecordRts(Camera camera,bool paused)
        {
            if(!Settings.baseBuilding || paused)return;
            CombatVisualParameters(rtsShader,camera);
            if(!spawnPending && pendingSpawns.Count>0)
            {
                var batch=pendingSpawns.ToArray();var callbacks=spawnCallbacks.ToArray();pendingSpawns.Clear();spawnCallbacks.Clear();spawnPending=true;
                cmd.SetBufferData(spawns,batch);cmd.SetComputeIntParam(rtsShader,"_SpawnCount",batch.Length);RtsDispatch("SpawnUnits",1);RtsDispatch("InitializeSpawnOperations",batch.Length);
                cmd.RequestAsyncReadback(spawnResults,r=> { if(disposed)return;spawnPending=false;var data=r.hasError?default:r.GetData<uint>();
                    for(int i=0;i<callbacks.Length;i++) { bool ok=!r.hasError&&data[i]!=0;if(ok)ProducedUnits++;callbacks[i]?.Invoke(ok); } });
            }
            RecordSwarm(paused);
            if(rtsPending || Time.realtimeSinceStartupAsDouble<nextRts)return;
            nextRts=Time.realtimeSinceStartupAsDouble+.25;rtsTick++;rtsPending=true;
            if(UiFogTelemetry.Enabled)cmd.BeginSample(UiFogTelemetry.Intel);
            RtsDispatch("ResetIntel",Mathf.Max(FogResolution*FogResolution*2,Settings.spatialResolution*Settings.spatialResolution*2));
            RtsDispatch("UnitIntel",Settings.activeUnits);RtsDispatch("BuildingIntel",structureCount);if(UiFogTelemetry.Enabled)cmd.EndSample(UiFogTelemetry.Intel);
            RtsDispatch("ResetRouteReferences",64);RtsDispatch("AircraftOperations",Settings.activeUnits);RtsDispatch("TransportPassengers",Settings.activeUnits);if(UiFogTelemetry.Enabled)cmd.BeginSample(UiFogTelemetry.Intel);RtsDispatch("CollectContacts",Settings.activeUnits);if(UiFogTelemetry.Enabled)cmd.EndSample(UiFogTelemetry.Intel);RtsDispatch("ScoutMissions",Settings.activeUnits);RtsDispatch("StructureWeapons",structureCount);
            RtsDispatch("UnitStructureCombat",Settings.activeUnits);RtsDispatch("SupportUnits",Settings.activeUnits);RtsDispatch("MissileOperations",32);
            RecordAiAreaQueries();
            cmd.RequestAsyncReadback(structureDamage,r=> { if(!disposed&&!r.hasError)OnStructureDamage?.Invoke(r.GetData<uint>().ToArray()); });
            cmd.RequestAsyncReadback(routeReferences,r=>{if(!disposed&&!r.hasError)routeUsers=r.GetData<uint>().ToArray();});
            cmd.RequestAsyncReadback(contacts,r=> {if(!disposed&&!r.hasError)RadarContacts=r.GetData<uint>().ToArray();});
            cmd.RequestAsyncReadback(sensors,r=> { if(!disposed&&!r.hasError)SensorData=r.GetData<uint>().ToArray(); });
            cmd.RequestAsyncReadback(sectors,r=> { if(!disposed&&!r.hasError)OnSectorControl?.Invoke(r.GetData<uint>().ToArray()); });
            cmd.RequestAsyncReadback(fog,r=> {
                if(disposed)return;rtsPending=false;if(r.hasError)return;using var fogTiming=UiFogTelemetry.ReadbackScope();var data=r.GetData<uint>();
                PlayerVisibility=PlayerVisibility??new byte[FogResolution*FogResolution*2];for(int i=0;i<data.Length;i++)PlayerVisibility[i]=(byte)data[i];
            });
        }
        public bool IsVisible(Vector2 p,int team=0,bool explored=false)
        {
            return Settings.spectatorMode||IsKnown(p,team,explored);
        }
        public bool IsKnown(Vector2 p,int team=0,bool explored=false)
        {
            if(!Settings.baseBuilding)return true;if(PlayerVisibility==null)return false;
            int x=WorldTopology.Cell(Mathf.FloorToInt((p.x/Field.Size+.5f)*FogResolution),FogResolution),y=WorldTopology.Cell(Mathf.FloorToInt((p.y/Field.Size+.5f)*FogResolution),FogResolution);
            return PlayerVisibility[team*FogResolution*FogResolution+x+y*FogResolution]>=(explored?1:2);
        }
        public void ScoutOrder(SearchPattern pattern,Vector2 origin,Vector2 extent,int routeSlot=-1)
        {
            cmd.Clear();cmd.SetComputeIntParam(rtsShader,"_RouteSlot",routeSlot);cmd.SetComputeIntParam(rtsShader,"_Pattern",(int)pattern);cmd.SetComputeVectorParam(rtsShader,"_MissionOrigin",new Vector4(origin.x,origin.y,extent.x,extent.y));
            RtsDispatch("AssignScout",Settings.activeUnits);Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
        public bool ScoutRoute(IReadOnlyList<Vector2> points)
        {
            if(points.Count<2||points.Count>16)return false;
            double now=Time.realtimeSinceStartupAsDouble;int slot;
            for(slot=0;slot<64;slot++)if(routeUsers[slot]==0&&routeBooked[slot]<=now)break;
            if(slot==64){Notice="All 64 patrol routes are in use";return false;}
            var data=new Vector2[16];for(int i=0;i<points.Count;i++)data[i]=points[i];
            routePoints.SetData(data,0,slot*16,16);routeCounts.SetData(new[]{(uint)points.Count},0,slot,1);routeBooked[slot]=now+1;
            ScoutOrder(SearchPattern.Route,points[0],Vector2.zero,slot);return true;
        }
        public void PickRallyUnit(Camera camera,Vector2 mouse,Action<int> callback)
        {
            cmd.Clear();Parameters(camera);cmd.SetComputeIntParam(shader,"_PickTeam",0);
            cmd.SetComputeVectorParam(shader,"_SelectionRect",new Vector4(mouse.x/Screen.width,mouse.y/Screen.height,0,0));
            Single("ClearPick");Dispatch("FindPick",Settings.activeUnits);
            cmd.RequestAsyncReadback(pick,r=>{if(!disposed)callback(r.hasError||r.GetData<uint>()[0]==uint.MaxValue?-1:(int)(r.GetData<uint>()[0]&0xfffff));});
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
        Vector2 MovementDestination(Vector2 p,int movement)
        {
            p=Field.Canonical(p);
            if(movement==3)return p;
            int n=Field.Resolution;float cell=Field.Size/n;
            float clearance=movement==0?.4f:movement==4?0:2.2f;
            var offsets=new[]{Vector2.zero,new Vector2(clearance,clearance),new Vector2(-clearance,clearance),new Vector2(clearance,-clearance),new Vector2(-clearance,-clearance)};
            for(int ring=0;ring<n;ring++)for(int k=0;k<Mathf.Max(1,ring*8);k++) {
                float a=k*Mathf.PI*2/Mathf.Max(1,ring*8);Vector2 q=p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*ring*cell;
                bool safe=true;
                foreach(var offset in offsets)
                    if(Field.MovementCost(movement,q+offset)>=DeploymentMap.Blocked*.5f){safe=false;break;}
                if(safe)return Field.Canonical(q);
            }return p;
        }
        int[] SelectedComposition(Camera camera,out float spacing)
        {
            orderMovementFilter=-1;cmd.Clear();Parameters(camera);Single("ClearOrderOccupancy");Dispatch("CountOrderOccupancy",Settings.activeUnits);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();orderOccupancy.GetData(occupancy);
            var counts=new int[5];for(int m=0;m<5;m++)counts[m]=(int)occupancy[BattleSettings.Groups*2+1+m];
            float radius=BitConverter.Int32BitsToSingle(unchecked((int)occupancy[BattleSettings.Groups*2+6]));
            spacing=Mathf.Max(BattleSettings.FormationSpacing,radius*2+1);return counts;
        }
        bool PrepareCombinedFormation(Camera camera,FormationLayout layout,int[] counts,ArmyOrder order)
        {
            for(int m=0;m<5;m++){
                Vector2 delta=counts[m]>0?Field.Delta(layout.Front,MovementDestination(layout.Front,m)):Vector2.zero;
                formationOrigins[m]=new Vector4(delta.x,delta.y,0,0);
            }
            orderMovementFilter=-1;cmd.Clear();Parameters(camera);RankSelection();cmd.CopyBuffer(selectedRanks,combinedRanks);
            Navigation.UploadFormation(cmd,BattleSettings.PreviewGroup,layout,0,order);
            cmd.SetComputeIntParam(shader,"_CommandGroup",BattleSettings.PreviewGroup);Single("FinalizeFormation");Single("ClearFormationSlots");
            Dispatch("MixedFormationSlots",Settings.activeUnits);Single("FinishFormationSlots");Dispatch("PreviewFormationSlots",4096);
            Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();formationState.GetData(placementState);
            PreviewCount=(int)placementState[2];PreviewRows=(int)placementState[1];PreviewSpacing=layout.Spacing;
            return placementState[0]==placementState[2];
        }
        void PreviewCombinedFormation(Camera camera,FormationLayout requested)
        {
            var counts=SelectedComposition(camera,out float spacing);int total=0;foreach(int count in counts)total+=count;
            if(total==0){CancelPreview();return;}
            var layout=new FormationLayout(requested.Front,requested.Right,requested.Width,spacing);combinedFormation=true;previewSequence++;
            try{PrepareCombinedFormation(camera,layout,counts,ArmyOrder.Move);PreviewActive=true;}finally{combinedFormation=false;}
        }
        public float PreviewSpacing {get;private set;}=BattleSettings.FormationSpacing;
        public bool IssueCombinedOrder(Camera camera,Vector2 destination,ArmyOrder order,Vector2 mouse,FormationLayout? formation=null,int buildingTarget=-1)
        {
            if(Settings.spectatorMode)return false;
            var counts=SelectedComposition(camera,out float spacing);int total=0;foreach(int count in counts)total+=count;
            if(total==0){Notice="Select units before issuing an order.";CancelPreview();return false;}
            var requested=formation??FormationLayout.Compact(destination,new Vector2(camera.transform.right.x,camera.transform.right.z),total,spacing);
            var layout=new FormationLayout(requested.Front,requested.Right,requested.Width,spacing);bool accepted=true;string failure="";
            orderStructureTarget=buildingTarget;combinedFormation=true;
            try {
                if(order!=ArmyOrder.Stop&&!PrepareCombinedFormation(camera,layout,counts,order)){Notice="Not enough clear formation slots here. Move or widen the formation.";CancelPreview();return false;}
                for(int m=0;m<5;m++)if(counts[m]>0){orderMovementFilter=m;
                    if(!IssueOrder(camera,layout.Front,order,mouse,layout)){accepted=false;failure=Notice;}
                }
            } finally {orderMovementFilter=-1;orderStructureTarget=-1;combinedFormation=false;}
            Notice=accepted?"":failure;return accepted;
        }
        public bool AttackStructure(Camera camera,int index,Vector2 position,float radius)
        {
            if(!Settings.baseBuilding||index<0||index>=structureCount)return false;
            Vector2 from=new Vector2(camera.transform.position.x,camera.transform.position.z);
            Vector2 side=Field.Delta(position,from);if(side.sqrMagnitude<1)side=Vector2.down;
            Vector2 approach=position+side.normalized*(radius+12);
            return IssueCombinedOrder(camera,approach,ArmyOrder.AttackMove,Vector2.zero,null,index);
        }
        public bool LaunchCarrierWing(int type,Action<bool> callback) => RequestSpawn(type,0,Vector2.zero,Vector2.zero,callback,-2);
        public void TransportOrder(bool load)
        {
            cmd.Clear();cmd.SetComputeIntParam(rtsShader,"_LoadTransport",load?1:0);RtsDispatch("LoadUnload",Settings.activeUnits);Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
        public void ControlGroup(int number,bool assign,bool additive)
        {
            cmd.Clear();cmd.SetComputeIntParam(rtsShader,"_GroupBit",1<<number);cmd.SetComputeIntParam(rtsShader,"_AssignGroup",assign?1:0);cmd.SetComputeIntParam(rtsShader,"_AddGroup",additive?1:0);
            RtsDispatch("ControlGroup",Settings.activeUnits);Graphics.ExecuteCommandBuffer(cmd);cmd.Clear();
        }
        public void RefreshNavigation(Bounds changed)
        {
            if(swarmA!=null)swarmDirty=true;
            using(var update=new CommandBuffer {name="Invalidate routes after local building obstacle change"}) {
                Navigation.InvalidateLocal(update,changed);
                Graphics.ExecuteCommandBuffer(update);
            }
        }
        void DisposeRts()
        {
            DisposeAiAreaQueries();
            DisposeSwarm();DisposeZombieNoise();
            foreach(var b in new[]{rtsTraits,structures,structureDamage,structureTimers,structureAim,freeSlots,freeCount,spawns,spawnResults,missions,fog,radar,sensors,controlGroups,sectors,contacts,support,operations,missiles,intercepts,routePoints,routeCounts,routeReferences})b?.Dispose();
            ArmyLifetime.Destroy(rtsShader);
        }
#if UNITY_EDITOR
        public void AdvanceRtsValidation() { nextRts=0; }
        public void SetValidationOperations(UnitOperations[] data) { if(data.Length>256||data.Length>Settings.capacity)throw new ArgumentOutOfRangeException(nameof(data));operations.SetData(data); }
        public void ReadValidationOperations(int count,Action<UnitOperations[]> callback) => AsyncGPUReadback.Request(operations,count*UnitOperations.Stride,0,r=>{if(!disposed)callback(r.hasError?null:r.GetData<UnitOperations>().ToArray());});
        public void ReadValidationMissiles(Action<ProjectileState[]> callback) => AsyncGPUReadback.Request(missiles,r=>{if(!disposed)callback(r.hasError?null:r.GetData<ProjectileState>().ToArray());});
        public void SetValidationMissiles(ProjectileState[] data) { if(data.Length!=32)throw new ArgumentException("Cover the bounded missile pool");missiles.SetData(data); }
        public void ResetValidationFreeSlots(UnitState[] fixture)
        {
            var slots=new List<uint>();for(int i=fixture.Length-1;i>=0;i--)if((fixture[i].flags&1)==0)slots.Add((uint)i);
            if(slots.Count>0)freeSlots.SetData(slots.ToArray());freeCount.SetData(new[]{(uint)slots.Count});
        }
#endif
    }
}
