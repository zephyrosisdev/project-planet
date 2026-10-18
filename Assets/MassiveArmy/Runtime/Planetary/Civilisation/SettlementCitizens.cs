using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    /// <summary>Biographies contain no poses. Shared destination flow fields and live pedestrian motion live on GPU.</summary>
    public sealed class SettlementCitizens:IDisposable {
        struct Goal {public int node,team,active,pad;}
        struct Order {public uint home,target,team,model;public Vector4 parameters;}
        readonly PlanetDevelopment world;readonly ComputeShader shader;readonly int clear,build,walk,copy,resetDraw;
        readonly Dictionary<long,int> slots=new Dictionary<long,int>();readonly List<(ColonyState colony,ResidentState person)> residents=new List<(ColonyState,ResidentState)>();
        GraphicsBuffer nodes,goals,orders,parents,queue,search,walkers,poses,visible,drawArguments;
        readonly Plane[] planes=new Plane[6];readonly Vector4[] frustum=new Vector4[6];Goal[] goalData;Order[] orderData;
        int nodeCapacity,goalCapacity,capacity,roadRevision=-1,buildingCount=-1;double nextOrders;bool disposed;
        public int Count=>residents.Count;public GraphicsBuffer Poses=>poses;public GraphicsBuffer Visible=>visible;public GraphicsBuffer DrawArguments=>drawArguments;
        internal GraphicsBuffer WalkerStatesForValidation=>walkers;
        public long FlowStorageBytes=>(long)nodeCapacity*goalCapacity*8;
        public SettlementCitizens(PlanetDevelopment world){this.world=world;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementCitizens"));clear=shader.FindKernel("ClearFlows");build=shader.FindKernel("BuildFlows");walk=shader.FindKernel("Walk");copy=shader.FindKernel("CopyWalkers");resetDraw=shader.FindKernel("ResetDraw");}
        static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,Math.Max(1,count),stride);
        void AllocateGraph(int roads,int buildings){nodes?.Dispose();goals?.Dispose();parents?.Dispose();queue?.Dispose();search?.Dispose();nodeCapacity=Mathf.NextPowerOfTwo(Math.Max(1,roads));goalCapacity=Mathf.NextPowerOfTwo(Math.Max(1,buildings));nodes=Buffer(nodeCapacity,32);goals=Buffer(goalCapacity,16);parents=Buffer(nodeCapacity*goalCapacity,4);queue=Buffer(nodeCapacity*goalCapacity,4);search=Buffer(goalCapacity,16);goalData=new Goal[goalCapacity];roadRevision=-1;
            foreach(int kernel in new[]{clear,build,walk}){shader.SetBuffer(kernel,"_Parents",parents);shader.SetBuffer(kernel,"_Search",search);shader.SetBuffer(kernel,"_Queue",queue);shader.SetBuffer(kernel,"_Nodes",nodes);shader.SetBuffer(kernel,"_Goals",goals);}
        }
        void AllocatePeople(int count){var previous=walkers;int oldCapacity=capacity;orders?.Dispose();poses?.Dispose();visible?.Dispose();drawArguments?.Dispose();capacity=Mathf.NextPowerOfTwo(Math.Max(1,count));orders=Buffer(capacity,32);walkers=Buffer(capacity,32);poses=Buffer(capacity,80);visible=Buffer(capacity,4);drawArguments=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments,4,4);drawArguments.SetData(new uint[]{ArtUnitRenderer.NearTriangles*3,0,0,0});shader.SetBuffer(resetDraw,"_DrawArgs",drawArguments);shader.SetBuffer(walk,"_DrawArgs",drawArguments);shader.SetBuffer(walk,"_CitizenVisible",visible);orderData=new Order[capacity];shader.SetBuffer(walk,"_Orders",orders);shader.SetBuffer(walk,"_Walkers",walkers);shader.SetBuffer(walk,"_CargoPoses",poses);
            // Copy old GPU state on growth. A zero-sized previous copy uses this buffer but never reads it.
            shader.SetBuffer(copy,"_PreviousWalkers",previous??orders);shader.SetBuffer(copy,"_Walkers",walkers);shader.SetInt("_PreviousCount",previous==null?0:oldCapacity);shader.SetInt("_WalkerCount",capacity);shader.Dispatch(copy,(capacity+127)/128,1,1);previous?.Dispose();nextOrders=0;
        }
        public void Step(float seconds,bool validation=false){
            if(disposed||!world.SimulationActive)return;if(world.Paused&&!validation)seconds=0;var local=world.Base;var civilisation=world.SystemRuntime.Civilisation;if(civilisation.CatchingUp(world.definition.bodyId))return;
            bool added=false;foreach(var c in local.Settlements)if(c!=null)foreach(var p in c.residents)if(p.alive&&!slots.ContainsKey(p.id)&&residents.Count<civilisation.Settings.maximumCivilianAgents){slots.Add(p.id,residents.Count);residents.Add((c,p));added=true;}
            if(Count==0)return;if(nodes==null||local.Roads.Tiles.Count>nodeCapacity||local.Buildings.Count>goalCapacity)AllocateGraph(local.Roads.Tiles.Count,local.Buildings.Count);
            if(walkers==null||Count>capacity)AllocatePeople(Count);
            bool topology=roadRevision!=local.Roads.Revision||buildingCount!=local.Buildings.Count;
            shader.SetInt("_NodeCount",local.Roads.Nodes.Length);shader.SetInt("_NodeStride",nodeCapacity);shader.SetInt("_GoalCount",goalCapacity);
            if(topology){if(local.Roads.Nodes.Length>0)nodes.SetData(local.Roads.Nodes);Array.Clear(goalData,0,goalData.Length);foreach(var b in local.Buildings){int node=local.Roads.Node(local.Roads.Port(b));goalData[b.index]=new Goal{node=node,team=b.team,active=!b.removed&&node>=0?1:0};}goals.SetData(goalData);shader.Dispatch(clear,(nodeCapacity*goalCapacity+127)/128,1,1);roadRevision=local.Roads.Revision;buildingCount=local.Buildings.Count;nextOrders=0;}
            if(added||civilisation.RenderTime>=nextOrders){nextOrders=civilisation.RenderTime+1;for(int i=0;i<Count;i++){var (c,p)=residents[i];var h=CivilisationRuntime.Family(c,p.household);var home=CivilisationRuntime.SocietyBuilding(local,h?.home??0);var target=CivilisationRuntime.SocietyBuilding(local,civilisation.Destination(local,c,p,civilisation.RenderTime));int node=home==null?-1:local.Roads.Node(local.Roads.Port(home));
                    orderData[i]=new Order{home=unchecked((uint)node),target=unchecked((uint)(target?.index??-1)),team=(uint)c.faction,model=p.role==CivilianRole.Engineer?1u:p.role==CivilianRole.Gatherer?0u:p.role==CivilianRole.Worker?1u:0u,parameters=new Vector4(0,0,p.alive?1:0,p.age<18?.8f:1)};
                }orders.SetData(orderData);}
            shader.SetInt("_WalkerCount",Count);shader.SetFloat("_Delta",Mathf.Max(0,seconds));shader.SetFloat("_Speed",civilisation.Settings.civilianWalkingSpeed);shader.SetFloat("_Clock",(float)civilisation.RenderTime);shader.SetFloat("_DrawRange",civilisation.Settings.civilianDrawDistance);shader.SetVector("_CameraBody",world.View.transform.position-world.PlanetRenderCentre);GeometryUtility.CalculateFrustumPlanes(world.View,planes);for(int i=0;i<6;i++)frustum[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance+Vector3.Dot(planes[i].normal,world.PlanetRenderCentre));shader.SetVectorArray("_Frustum",frustum);shader.Dispatch(resetDraw,1,1,1);shader.Dispatch(build,(goalCapacity+31)/32,1,1);shader.Dispatch(walk,(Count+127)/128,1,1);
        }
        public void Dispose(){disposed=true;nodes?.Dispose();goals?.Dispose();orders?.Dispose();parents?.Dispose();queue?.Dispose();search?.Dispose();walkers?.Dispose();poses?.Dispose();visible?.Dispose();drawArguments?.Dispose();UnityEngine.Object.Destroy(shader);}
    }
}
