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
        Job[] jobs;Summary[] summaries;GraphicsBuffer nodes,railNodes,jobBuffer,work,seen,parents,queue,distances,summary,poses;
        int roadRevision=-1,railRevision=-1,nodeCapacity,vehicleCapacity;uint serial=1;bool readbackPending,disposed;uint nextReadbackTick;
        public int Count=>vehicles.Count;public GraphicsBuffer Poses=>poses;
        public long SearchStorageBytes=>(long)nodeCapacity*vehicleCapacity*16;
        public SettlementCargoNavigation(PlanetDevelopment world){this.world=world;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementRoad"));kernel=shader.FindKernel("Navigate");shader.SetFloat("_RoadLaneOffset",world.Base.Grid.RoadLaneOffset);}
        static RtsBuilding Find(PlanetRtsBase local,long id)=>local.FindBuilding(id,true);
        static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,Math.Max(1,count),stride);
        // 2D storage passes avoid the DX11 65,535-group limit as the road graph grows.
        internal static void DispatchStorage(ComputeShader shader,int kernel,int count){int groups=(count+127)/128,width=Math.Min(1024,groups);shader.SetInt("_StorageWidth",width*128);shader.Dispatch(kernel,width,(groups+width-1)/width,1);}
        void Allocate(int roads,int carts){
            var oldSeen=seen;var oldParents=parents;var oldQueue=queue;var oldDistances=distances;var oldWork=work;var oldPoses=poses;int oldStride=nodeCapacity,oldSlots=vehicleCapacity;
            nodes?.Dispose();railNodes?.Dispose();jobBuffer?.Dispose();summary?.Dispose();nodeCapacity=Mathf.NextPowerOfTwo(Math.Max(1,roads));vehicleCapacity=Mathf.NextPowerOfTwo(Math.Max(1,carts));
            nodes=Buffer(nodeCapacity,32);railNodes=Buffer(nodeCapacity,32);jobBuffer=Buffer(vehicleCapacity,64);work=Buffer(vehicleCapacity,48);seen=Buffer(nodeCapacity*vehicleCapacity,4);parents=Buffer(nodeCapacity*vehicleCapacity,4);queue=Buffer(nodeCapacity*vehicleCapacity,4);distances=Buffer(nodeCapacity*vehicleCapacity,4);summary=Buffer(vehicleCapacity,16);poses=Buffer(vehicleCapacity,80);
            var previousJobs=jobs;var previousSummaries=summaries;jobs=new Job[vehicleCapacity];summaries=new Summary[vehicleCapacity];if(previousJobs!=null){Array.Copy(previousJobs,jobs,previousJobs.Length);Array.Copy(previousSummaries,summaries,previousSummaries.Length);}roadRevision=-1;
            shader.SetBuffer(kernel,"_RoadNodes",nodes);shader.SetBuffer(kernel,"_RailNodes",railNodes);shader.SetBuffer(kernel,"_RoadJobs",jobBuffer);shader.SetBuffer(kernel,"_RoadWork",work);shader.SetBuffer(kernel,"_RoadSeen",seen);shader.SetBuffer(kernel,"_RoadParents",parents);shader.SetBuffer(kernel,"_RoadQueue",queue);shader.SetBuffer(kernel,"_RoadDistances",distances);shader.SetBuffer(kernel,"_RoadSummary",summary);shader.SetBuffer(kernel,"_CargoPoses",poses);
            shader.SetInt("_RoadStride",nodeCapacity);shader.SetInt("_SearchSlots",vehicleCapacity);int clear=shader.FindKernel("ClearSearch");shader.SetBuffer(clear,"_RoadSeen",seen);shader.SetBuffer(clear,"_RoadWork",work);DispatchStorage(shader,clear,nodeCapacity*vehicleCapacity);
            if(oldSeen!=null){int copy=shader.FindKernel("CopySearch");shader.SetInt("_PreviousStride",oldStride);shader.SetInt("_PreviousSlots",oldSlots);
                shader.SetBuffer(copy,"_PreviousSeen",oldSeen);shader.SetBuffer(copy,"_PreviousParents",oldParents);shader.SetBuffer(copy,"_PreviousQueue",oldQueue);shader.SetBuffer(copy,"_PreviousDistances",oldDistances);shader.SetBuffer(copy,"_PreviousWork",oldWork);shader.SetBuffer(copy,"_PreviousPoses",oldPoses);
                shader.SetBuffer(copy,"_RoadSeen",seen);shader.SetBuffer(copy,"_RoadParents",parents);shader.SetBuffer(copy,"_RoadQueue",queue);shader.SetBuffer(copy,"_RoadDistances",distances);shader.SetBuffer(copy,"_RoadWork",work);shader.SetBuffer(copy,"_CargoPoses",poses);DispatchStorage(shader,copy,oldStride*oldSlots);
                oldSeen.Dispose();oldParents.Dispose();oldQueue.Dispose();oldDistances.Dispose();oldWork.Dispose();oldPoses.Dispose();
            }
        }
        public void Step(float seconds,bool synchronousEvents=false){
            if(disposed||!world.SimulationActive)return;var local=world.Base;if(local==null)return;
            bool dirty=false;foreach(var colony in local.Settlements)if(colony!=null)foreach(var vehicle in colony.cargoVehicles)if(!slots.ContainsKey(vehicle.id)){slots.Add(vehicle.id,vehicles.Count);vehicles.Add(vehicle);dirty=true;}
            if(vehicles.Count==0)return;
            if(poses==null||Math.Max(local.Roads.Tiles.Count,local.Rails.Tiles.Count)>nodeCapacity||vehicles.Count>vehicleCapacity){Allocate(Math.Max(local.Roads.Tiles.Count,local.Rails.Tiles.Count),vehicles.Count);dirty=true;}
            bool topology=roadRevision!=local.Roads.Revision||railRevision!=local.Rails.Revision;if(topology){if(local.Roads.Nodes.Length>0)nodes.SetData(local.Roads.Nodes);if(local.Rails.Nodes.Length>0)railNodes.SetData(local.Rails.Nodes);railRevision=local.Rails.Revision;roadRevision=local.Roads.Revision;dirty=true;}
            for(int slot=0;slot<vehicles.Count;slot++){
                var vehicle=vehicles[slot];if(jobs[slot].version!=0&&vehicle.navigationVersion==jobs[slot].version)continue;
                var home=Find(local,vehicle.warehouse);var from=vehicle.phase==CargoVehiclePhase.Delivering?Find(local,vehicle.source):vehicle.phase==CargoVehiclePhase.Returning?Find(local,vehicle.destination):home;
                var to=vehicle.phase==CargoVehiclePhase.Collecting?Find(local,vehicle.source):vehicle.phase==CargoVehiclePhase.Delivering?Find(local,vehicle.destination):home;
                if(vehicle.freight&&vehicle.phase!=CargoVehiclePhase.Idle&&vehicle.stops.Count==4){from=Find(local,vehicle.stops[vehicle.leg]);to=Find(local,vehicle.stops[vehicle.leg+1]);if(vehicle.shuttleReturning){var swap=from;from=to;to=swap;}}
                if(from==null||to==null){from=to=home;}if(home==null)continue;
                bool rail=vehicle.train&&vehicle.leg==1;var network=rail?local.Rails:local.Roads;int a=network.Node(network.Port(from)),b=network.Node(network.Port(to));
                Vector3 start=a>=0?(Vector3)network.Nodes[a].position:network.Port(from)*(world.definition.radius+1),goal=b>=0?(Vector3)network.Nodes[b].position:network.Port(to)*(world.definition.radius+1);
                if(vehicle.funeral&&vehicle.phase!=CargoVehiclePhase.Idle){int pickup=NearestRoad(local,vehicle.funeralPickup,home.team);if(vehicle.phase==CargoVehiclePhase.Collecting)b=pickup;else a=pickup;start=a>=0?(Vector3)network.Nodes[a].position:start;goal=b>=0?(Vector3)network.Nodes[b].position:goal;}
                bool longHaul=vehicle.freight&&vehicle.leg==1;bool flying=vehicle.aerial&&(!vehicle.freight||longHaul),sailing=vehicle.seaborne&&longHaul;
                if(flying){var surface=new PlanetSurface(world.definition);start=surface.GetSurfacePositionPrecise(local.Roads.Port(from),60).Float;goal=surface.GetSurfacePositionPrecise(local.Roads.Port(to),60).Float;}
                if(sailing){float radius=world.definition.radius+world.definition.oceanHeight+1;start=world.SystemRuntime.Civilisation.Logistics.Berth(local,from)*radius;goal=world.SystemRuntime.Civilisation.Logistics.Berth(local,to)*radius;}
                vehicle.navigationVersion=++serial;vehicle.gpuArrived=false;
                jobs[slot]=new Job{start=new Vector4(start.x,start.y,start.z,vehicle.freight?1+.2f*(Math.Clamp(vehicle.tier,1,3)-1):1),goal=goal,version=vehicle.navigationVersion,startNode=unchecked((uint)a),goalNode=unchecked((uint)b),flags=(flying?2u:sailing?8u:rail?16u:0u)|(vehicle.phase==CargoVehiclePhase.Idle?4u:0u),parameters=new Vector4(vehicle.freight?(float)world.SystemRuntime.Civilisation.Logistics.LegSpeed(vehicle):vehicle.aerial?world.SystemRuntime.Civilisation.Settings.airCargoSpeed:world.SystemRuntime.Civilisation.Settings.groundCargoSpeed,from.team,world.definition.radius,vehicle.funeral?43:rail?13:sailing?22:flying&&vehicle.freight?17:flying?18:11)};summaries[slot]=default;dirty=true;
            }
            // Arrival events already contain the final pose/status. Until a new mission or
            // topology revision, an arrived/idle fleet needs no additional GPU dispatch/readback.
            if(!dirty){bool pending=false;foreach(var vehicle in vehicles)if(vehicle.navigationVersion==0||!vehicle.gpuArrived){pending=true;break;}if(!pending)return;}
            if(dirty)jobBuffer.SetData(jobs);
            shader.SetInt("_RoadRevision",local.Roads.Revision*397^local.Rails.Revision);shader.SetInt("_RailCount",local.Rails.Tiles.Count);shader.SetInt("_RoadCount",local.Roads.Tiles.Count);shader.SetInt("_RoadStride",nodeCapacity);shader.SetInt("_CargoCount",vehicles.Count);shader.SetFloat("_CargoDelta",Mathf.Max(0,seconds));shader.Dispatch(kernel,(vehicles.Count+31)/32,1,1);
            if(synchronousEvents){var data=new Summary[vehicles.Count];summary.GetData(data,0,0,vehicles.Count);Receive(data);}
            // Each summary carries its own mission version. A different cart starting a job must not discard this whole batch.
            else if(!readbackPending&&unchecked((int)(world.SimulationTicks-nextReadbackTick))>=0){readbackPending=true;nextReadbackTick=world.SimulationTicks+(uint)Math.Max(1,Mathf.RoundToInt(.1f/BattleSettings.Tick));AsyncGPUReadback.Request(summary,vehicles.Count*16,0,request=>{readbackPending=false;if(disposed||request.hasError)return;var data=request.GetData<Summary>();for(int i=0;i<data.Length&&i<summaries.Length;i++)summaries[i]=data[i];Apply();});}
        }
        void Receive(Summary[] data){Array.Copy(data,summaries,data.Length);Apply();}
        void Apply(){for(int i=0;i<vehicles.Count;i++){var value=summaries[i];var vehicle=vehicles[i];if(value.version!=vehicle.navigationVersion)continue;vehicle.navigationStatus=value.status;vehicle.gpuArrived=value.arrived!=0;vehicle.gpuRouteMetres=value.distance;if(value.distance>0&&vehicle.phase!=CargoVehiclePhase.Idle){if(vehicle.freight){vehicle.legArrival=Math.Max(vehicle.legArrival,vehicle.legDeparture+value.distance/world.SystemRuntime.Civilisation.Logistics.LegSpeed(vehicle)+world.SystemRuntime.Civilisation.Settings.cargoHandlingSeconds);continue;}vehicle.routeMetres=value.distance;var settings=world.SystemRuntime.Civilisation.Settings;vehicle.arrival=Math.Max(vehicle.arrival,vehicle.departure+value.distance/(vehicle.aerial?settings.airCargoSpeed:settings.groundCargoSpeed)+settings.cargoHandlingSeconds);}}}
        internal void ValidateStorageGrowth(){if(poses!=null)Allocate(nodeCapacity*2,vehicleCapacity);}
        public bool Arrived(CargoVehicleState vehicle)=>vehicle.navigationVersion!=0&&vehicle.gpuArrived;
        void Release(){nodes?.Dispose();railNodes?.Dispose();jobBuffer?.Dispose();work?.Dispose();seen?.Dispose();parents?.Dispose();queue?.Dispose();distances?.Dispose();summary?.Dispose();poses?.Dispose();}
        public void Dispose(){disposed=true;pickResult?.Dispose();Release();UnityEngine.Object.Destroy(shader);}
    }
}
