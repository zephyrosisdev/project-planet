using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>GPU owns cart search state, route parents and motion. CPU reads only bounded mission events.</summary>
    public sealed partial class SettlementCargoNavigation:IDisposable {
        struct Job {public Vector4 start,goal;public uint version,startNode,goalNode,flags;public Vector4 parameters;}
        struct Summary {public uint version,arrived;public float distance;public uint status;}
        readonly PlanetDevelopment world;readonly ComputeShader shader;readonly int kernel;
        readonly Dictionary<long,int> slots=new Dictionary<long,int>();readonly List<CargoVehicleState> vehicles=new List<CargoVehicleState>();
        Job[] jobs;Summary[] summaries;GraphicsBuffer nodes,railNodes,jobBuffer,work,seen,parents,queue,distances,summary,poses,routes,searchOwners,searchEpochs;
        readonly Vector4[] raidZones=new Vector4[8];
        internal const int RouteStride=2048,WorkStride=64;
        int searchSlots,bufferEpoch;
        int roadRevision=-1,railRevision=-1,nodeCapacity,vehicleCapacity;uint serial=1,dispatchSerial;bool readbackPending,disposed;uint nextReadbackTick;
        public int Count=>vehicles.Count;public GraphicsBuffer Poses=>poses;
        public long SearchStorageBytes=>seen==null?0:(long)nodeCapacity*searchSlots*16+searchSlots*8L;
        public long BufferBytes=>SearchStorageBytes+(nodes==null?0:nodeCapacity*64L)+vehicleCapacity*(64L+WorkStride+16+80+RouteStride*4L);
        public int SearchSlots=>seen==null?0:searchSlots;
        internal static int SearchSlotCount(int nodes,int vehicles,int maximum,int budgetMiB)=>Math.Max(1,Math.Min(Math.Min(Math.Max(1,maximum),Math.Max(1,vehicles)),(int)Math.Max(1,((long)Math.Max(1,budgetMiB)*1048576)/(Math.Max(1,nodes)*16L+8))));
        public SettlementCargoNavigation(PlanetDevelopment world){this.world=world;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementRoad"));kernel=shader.FindKernel("Navigate");shader.SetFloat("_RoadLaneOffset",world.Base.Grid.RoadLaneOffset);}
        static RtsBuilding Find(PlanetRtsBase local,long id)=>local.FindBuilding(id,true);
        static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,Math.Max(1,count),stride);
        // 2D storage passes avoid the DX11 65,535-group limit as the road graph grows.
        internal static void DispatchStorage(ComputeShader shader,int kernel,int count){int groups=(count+127)/128,width=Math.Min(1024,groups);shader.SetInt("_StorageWidth",width*128);shader.Dispatch(kernel,width,(groups+width-1)/width,1);}
        void Allocate(int roads,int carts){
            // Scratch searches are transient; only compact GPU routes and motion survive resizing.
            var oldWork=work;var oldPoses=poses;var oldRoutes=routes;int oldSlots=vehicleCapacity;
            nodes?.Dispose();railNodes?.Dispose();jobBuffer?.Dispose();summary?.Dispose();seen?.Dispose();parents?.Dispose();queue?.Dispose();distances?.Dispose();searchOwners?.Dispose();searchEpochs?.Dispose();
            nodeCapacity=Mathf.NextPowerOfTwo(Math.Max(1,roads));vehicleCapacity=Mathf.NextPowerOfTwo(Math.Max(1,carts));
            var settings=world.SystemRuntime.Civilisation.Settings;
            searchSlots=SearchSlotCount(nodeCapacity,vehicleCapacity,settings.maximumCargoSearches,settings.cargoSearchMemoryMiB);
            nodes=Buffer(nodeCapacity,32);railNodes=Buffer(nodeCapacity,32);jobBuffer=Buffer(vehicleCapacity,64);work=Buffer(vehicleCapacity,WorkStride);
            seen=Buffer(nodeCapacity*searchSlots,16);parents=queue=distances=null;
            searchOwners=Buffer(searchSlots,4);searchEpochs=Buffer(searchSlots,4);routes=Buffer(vehicleCapacity*RouteStride,4);summary=Buffer(vehicleCapacity,16);poses=Buffer(vehicleCapacity,80);
            var previousJobs=jobs;var previousSummaries=summaries;jobs=new Job[vehicleCapacity];summaries=new Summary[vehicleCapacity];if(previousJobs!=null){Array.Copy(previousJobs,jobs,Math.Min(previousJobs.Length,jobs.Length));Array.Copy(previousSummaries,summaries,Math.Min(previousSummaries.Length,summaries.Length));}roadRevision=-1;bufferEpoch++;
            foreach(int k in new[]{kernel,shader.FindKernel("ClearSearch"),shader.FindKernel("CopySearch")}){
                shader.SetBuffer(k,"_RoadWork",work);shader.SetBuffer(k,"_RoadSearch",seen);shader.SetBuffer(k,"_CargoPoses",poses);shader.SetBuffer(k,"_RoadRoutes",routes);shader.SetBuffer(k,"_SearchOwners",searchOwners);shader.SetBuffer(k,"_SearchEpochs",searchEpochs);
            }
            shader.SetBuffer(kernel,"_RoadNodes",nodes);shader.SetBuffer(kernel,"_RailNodes",railNodes);shader.SetBuffer(kernel,"_RoadJobs",jobBuffer);shader.SetBuffer(kernel,"_RoadSummary",summary);
            shader.SetInt("_RoadStride",nodeCapacity);shader.SetInt("_SearchSlots",searchSlots);shader.SetInt("_RouteStride",RouteStride);shader.SetInt("_CargoCount",vehicleCapacity);
            DispatchStorage(shader,shader.FindKernel("ClearSearch"),Math.Max(nodeCapacity*searchSlots,vehicleCapacity));
            if(oldWork!=null){int copy=shader.FindKernel("CopySearch");shader.SetInt("_PreviousSlots",Math.Min(oldSlots,vehicleCapacity));shader.SetInt("_RoadRevision",world.Base.Roads.Revision*397^world.Base.Rails.Revision);
                shader.SetBuffer(copy,"_PreviousWork",oldWork);shader.SetBuffer(copy,"_PreviousPoses",oldPoses);shader.SetBuffer(copy,"_PreviousRoutes",oldRoutes);DispatchStorage(shader,copy,Math.Min(oldSlots,vehicleCapacity)*RouteStride);
                oldWork.Dispose();oldPoses.Dispose();oldRoutes.Dispose();
            }
        }
        public void Step(float seconds,bool synchronousEvents=false){
            if(disposed||!world.SimulationActive)return;var local=world.Base;if(local==null)return;
            var camps=world.SystemRuntime.Civilisation.Planet(world.definition.bodyId).raiders;int raidCount=Math.Min(8,camps.Count);for(int c=0;c<raidCount;c++){var camp=camps[c];var d=CubeSphere.Decode(camp.position);raidZones[c]=new Vector4(d.x,d.y,d.z,camp.health>0&&camp.deployed?350:0);}shader.SetVectorArray("_RaiderCamps",raidZones);shader.SetInt("_RaiderCampCount",raidCount);
            int fleet=0;foreach(var c in local.Settlements)if(c!=null)fleet+=c.cargoVehicles.Count;if(fleet<vehicles.Count){Release();nodes=railNodes=jobBuffer=work=seen=parents=queue=distances=summary=poses=routes=searchOwners=searchEpochs=null;jobs=null;summaries=null;vehicleCapacity=0;vehicles.Clear();slots.Clear();bufferEpoch++;}
            bool dirty=false;foreach(var colony in local.Settlements)if(colony!=null)foreach(var vehicle in colony.cargoVehicles)if(!slots.ContainsKey(vehicle.id)){slots.Add(vehicle.id,vehicles.Count);vehicles.Add(vehicle);dirty=true;}
            if(vehicles.Count==0)return;
            var memorySettings=world.SystemRuntime.Civilisation.Settings;
            if(nodes==null||poses==null||SearchSlotCount(nodeCapacity,vehicleCapacity,memorySettings.maximumCargoSearches,memorySettings.cargoSearchMemoryMiB)!=searchSlots||Math.Max(local.Roads.Tiles.Count,local.Rails.Tiles.Count)>nodeCapacity||vehicles.Count>vehicleCapacity){Allocate(Math.Max(local.Roads.Tiles.Count,local.Rails.Tiles.Count),vehicles.Count);dirty=true;}
            bool topology=roadRevision!=local.Roads.Revision||railRevision!=local.Rails.Revision;if(topology){if(local.Roads.Tiles.Count>0)nodes.SetData(local.Roads.Nodes,0,0,local.Roads.Tiles.Count);if(local.Rails.Tiles.Count>0)railNodes.SetData(local.Rails.Nodes,0,0,local.Rails.Tiles.Count);railRevision=local.Rails.Revision;roadRevision=local.Roads.Revision;dirty=true;}
            for(int slot=0;slot<vehicles.Count;slot++){
                var vehicle=vehicles[slot];if(jobs[slot].version!=0&&vehicle.navigationVersion==jobs[slot].version)continue;
                var home=Find(local,vehicle.warehouse);var from=vehicle.phase==CargoVehiclePhase.Delivering?Find(local,vehicle.source):vehicle.phase==CargoVehiclePhase.Returning?Find(local,vehicle.destination):home;
                var to=vehicle.phase==CargoVehiclePhase.Collecting?Find(local,vehicle.source):vehicle.phase==CargoVehiclePhase.Delivering?Find(local,vehicle.destination):home;
                if(vehicle.freight&&vehicle.phase!=CargoVehiclePhase.Idle&&vehicle.stops.Count==4){from=Find(local,vehicle.stops[vehicle.leg]);to=Find(local,vehicle.stops[vehicle.leg+1]);if(vehicle.shuttleReturning){var swap=from;from=to;to=swap;}}
                if(from==null||to==null){from=to=home;}if(home==null)continue;
                bool rail=vehicle.train&&vehicle.leg==1;var network=rail?local.Rails:local.Roads;int a=network.Node(network.Port(from)),b=network.Node(network.Port(to));
                Vector3 start=a>=0?(Vector3)network.Nodes[a].position:network.Port(from)*(world.definition.radius+1),goal=b>=0?(Vector3)network.Nodes[b].position:network.Port(to)*(world.definition.radius+1);
                if(vehicle.funeral&&(vehicle.phase==CargoVehiclePhase.Collecting||vehicle.phase==CargoVehiclePhase.Delivering)){int pickup=NearestRoad(local,vehicle.funeralPickup,home.team);if(vehicle.phase==CargoVehiclePhase.Collecting)b=pickup;else a=pickup;start=a>=0?(Vector3)network.Nodes[a].position:start;goal=b>=0?(Vector3)network.Nodes[b].position:goal;}
                if(vehicle.funeral&&home.definition.id==CivilisationRuntime.FuneralDirectors&&(vehicle.phase==CargoVehiclePhase.Idle||vehicle.phase==CargoVehiclePhase.Returning)){
                    var point=local.PlotPointPrecise(home,local.Grid.CentrePrecise(local.Area(home)),.04);goal=(point+Double3.Rotate(local.Grid.Rotation(local.Area(home)),new Double3(11,.7,8))).Float;if(vehicle.phase==CargoVehiclePhase.Idle)start=goal;
                }
                bool longHaul=vehicle.freight&&vehicle.leg==1;bool flying=vehicle.aerial&&(!vehicle.freight||longHaul),sailing=vehicle.seaborne&&longHaul;
                if(flying){var surface=new PlanetSurface(world.definition);start=surface.GetSurfacePositionPrecise(local.Roads.Port(from),60).Float;goal=surface.GetSurfacePositionPrecise(local.Roads.Port(to),60).Float;}
                if(sailing){float radius=world.definition.radius+world.definition.oceanHeight+1;start=world.SystemRuntime.Civilisation.Logistics.Berth(local,from)*radius;goal=world.SystemRuntime.Civilisation.Logistics.Berth(local,to)*radius;}
                vehicle.navigationVersion=++serial;vehicle.gpuArrived=vehicle.ambient&&!vehicle.ambientVisible;
                jobs[slot]=new Job{start=new Vector4(start.x,start.y,start.z,vehicle.freight?1+.2f*(Math.Clamp(vehicle.tier,1,3)-1):1),goal=goal,version=vehicle.navigationVersion,startNode=unchecked((uint)a),goalNode=unchecked((uint)b),flags=(vehicle.ambient&&!vehicle.ambientVisible?128u:0u)|(vehicle.funeral&&home.definition.id==CivilisationRuntime.FuneralDirectors&&(vehicle.phase==CargoVehiclePhase.Returning||vehicle.phase==CargoVehiclePhase.Idle)?64u:0u)|(flying?2u:sailing?8u:rail?16u:0u)|(vehicle.phase==CargoVehiclePhase.Idle?4u:0u)|(!vehicle.funeral&&!vehicle.aerial&&!vehicle.seaborne&&vehicle.phase==CargoVehiclePhase.Delivering?32u:0u),parameters=new Vector4(vehicle.freight?(float)world.SystemRuntime.Civilisation.Logistics.LegSpeed(vehicle):vehicle.aerial?world.SystemRuntime.Civilisation.Settings.airCargoSpeed:world.SystemRuntime.Civilisation.Settings.groundCargoSpeed,local.CombatTeam(from.team),world.definition.radius,vehicle.funeral?43:rail?13:sailing?22:flying&&vehicle.freight?17:flying?18:11)};summaries[slot]=default;dirty=true;
            }
            // Arrival events already contain the final pose/status. Until a new mission or
            // topology revision, an arrived/idle fleet needs no additional GPU dispatch/readback.
            if(!dirty){bool pending=false;foreach(var vehicle in vehicles)if(vehicle.navigationVersion==0||!vehicle.gpuArrived){pending=true;break;}if(!pending)return;}
            if(dirty)jobBuffer.SetData(jobs);
            shader.SetInt("_RoadRevision",local.Roads.Revision*397^local.Rails.Revision);shader.SetInt("_RailCount",local.Rails.Tiles.Count);shader.SetInt("_RoadCount",local.Roads.Tiles.Count);shader.SetInt("_RoadStride",nodeCapacity);shader.SetInt("_CargoCount",vehicles.Count);shader.SetFloat("_CargoDelta",Mathf.Max(0,seconds));shader.SetInt("_CargoDispatch",(int)(++dispatchSerial&0x7fffffffu));shader.Dispatch(kernel,(vehicles.Count+31)/32,1,1);
            if(synchronousEvents){var data=new Summary[vehicles.Count];summary.GetData(data,0,0,vehicles.Count);Receive(data);}
            // Each summary carries its own mission version. A different cart starting a job must not discard this whole batch.
            else if(!readbackPending&&unchecked((int)(world.SimulationTicks-nextReadbackTick))>=0){int epoch=bufferEpoch;readbackPending=true;nextReadbackTick=world.SimulationTicks+(uint)Math.Max(1,Mathf.RoundToInt(.1f/BattleSettings.Tick));AsyncGPUReadback.Request(summary,vehicles.Count*16,0,request=>{readbackPending=false;if(disposed||epoch!=bufferEpoch||request.hasError)return;var data=request.GetData<Summary>();for(int i=0;i<data.Length&&i<summaries.Length;i++)summaries[i]=data[i];Apply();});}
        }
        void Receive(Summary[] data){Array.Copy(data,summaries,data.Length);Apply();}
        void Apply(){for(int i=0;i<vehicles.Count;i++){var value=summaries[i];var vehicle=vehicles[i];if(value.version!=vehicle.navigationVersion)continue;if((value.status>>8)>0)world.SystemRuntime.Civilisation.ResolveVisibleRaid(world.Base,vehicle,(int)(value.status>>8)-1);vehicle.navigationStatus=value.status&255u;vehicle.gpuArrived=value.arrived!=0;vehicle.gpuRouteMetres=value.distance;if(value.distance>0&&vehicle.phase!=CargoVehiclePhase.Idle){if(vehicle.freight){vehicle.legArrival=Math.Max(vehicle.legArrival,vehicle.legDeparture+value.distance/world.SystemRuntime.Civilisation.Logistics.LegSpeed(vehicle)+world.SystemRuntime.Civilisation.Settings.cargoHandlingSeconds);continue;}vehicle.routeMetres=value.distance;var settings=world.SystemRuntime.Civilisation.Settings;vehicle.arrival=Math.Max(vehicle.arrival,vehicle.departure+value.distance/(vehicle.aerial?settings.airCargoSpeed:settings.groundCargoSpeed)+settings.cargoHandlingSeconds);}}}
        internal void ValidateStorageGrowth(){if(poses!=null)Allocate(nodeCapacity*2,vehicleCapacity);}
        public bool Arrived(CargoVehicleState vehicle)=>vehicle.navigationVersion!=0&&vehicle.gpuArrived;
        internal void ReleaseSearchCache(){
            nodes?.Dispose();railNodes?.Dispose();seen?.Dispose();parents?.Dispose();queue?.Dispose();distances?.Dispose();searchOwners?.Dispose();searchEpochs?.Dispose();
            nodes=railNodes=seen=parents=queue=distances=searchOwners=searchEpochs=null;roadRevision=railRevision=-1;
        }
        void Release(){nodes?.Dispose();railNodes?.Dispose();jobBuffer?.Dispose();work?.Dispose();seen?.Dispose();parents?.Dispose();queue?.Dispose();distances?.Dispose();summary?.Dispose();poses?.Dispose();routes?.Dispose();searchOwners?.Dispose();searchEpochs?.Dispose();}
        public void Dispose(){disposed=true;pickResult?.Dispose();Release();UnityEngine.Object.Destroy(shader);}
    }
}
