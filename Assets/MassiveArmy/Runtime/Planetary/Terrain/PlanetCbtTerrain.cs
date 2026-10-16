using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    /// <summary>Bounded implicit CBT with compatible longest-edge bisection. No CPU terrain traversal.</summary>
    public sealed class PlanetCbtTerrain : IDisposable
    {
        public readonly int MaximumDepth,Grid,TreeWords,VertexCount,RequestWords;
        public GraphicsBuffer States {get;}
        public GraphicsBuffer Counts {get;}
        public GraphicsBuffer Triangles {get;}
        public GraphicsBuffer DrawArguments {get;}
        public GraphicsBuffer Statistics {get;}
        readonly GraphicsBuffer splits,merges,workArguments;
        readonly ComputeShader shader;
        readonly CommandBuffer commands=new CommandBuffer{name="Planet CBT update and visibility"};
        readonly int initialize,clear,mergeCandidates,applyMerges,fold,work,request,propagate,applySplits,emit,draw,resetVisible;
        readonly Vector4[] planes=new Vector4[6];
        readonly Plane[] frustum=new Plane[6];
        readonly PlanetDefinition definition;int lastTerrainRevision=-1;
        uint[] telemetry=new uint[8];bool disposed,reading,initialized,settled,hasConfiguration,lastNoCull;double nextReadback;
        int revision,lastSeed,lastPixelHeight;
        float lastRadius,lastHeight,lastOcean,lastError;
        Vector3 lastCamera;Quaternion lastRotation;Matrix4x4 lastProjection;
        public bool Converged=>settled;
        public uint ActiveLeaves=>telemetry[0];public uint VisibleLeaves=>telemetry[1];public uint SaturatedLeaves=>telemetry[5];
        public long BufferBytes=>(long)12*TreeWords*8+(long)RequestWords*8+(long)12*(1<<MaximumDepth)*8+8*4+7*4;
        public PlanetCbtTerrain(PlanetDefinition definition,int maximumDepth=14)
        {
            if(maximumDepth<2||maximumDepth>14||(maximumDepth&1)!=0)throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            this.definition=definition;MaximumDepth=maximumDepth;Grid=1<<(maximumDepth/2);TreeWords=1<<(maximumDepth+1);VertexCount=6*(Grid+1)*(Grid+1);RequestWords=maximumDepth*VertexCount;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetCbt"));
            initialize=shader.FindKernel("Initialize");clear=shader.FindKernel("ClearRequests");mergeCandidates=shader.FindKernel("MergeCandidates");applyMerges=shader.FindKernel("ApplyMerges");fold=shader.FindKernel("FoldCounts");work=shader.FindKernel("BuildWork");request=shader.FindKernel("RequestSplits");propagate=shader.FindKernel("PropagateConformity");applySplits=shader.FindKernel("ApplySplits");emit=shader.FindKernel("EmitVisible");draw=shader.FindKernel("BuildDraw");
            resetVisible=shader.FindKernel("ResetVisibleStats");
            States=Buffer(12*TreeWords);Counts=Buffer(12*TreeWords);splits=Buffer(RequestWords);merges=Buffer(RequestWords);Statistics=Buffer(8);
            workArguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Structured,3,4);
            DrawArguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Structured,4,4);
            Triangles=new GraphicsBuffer(GraphicsBuffer.Target.Append,12*(1<<MaximumDepth),8);
            foreach(int kernel in new[]{initialize,clear,mergeCandidates,applyMerges,fold,work,request,propagate,applySplits,emit,draw,resetVisible}){
                shader.SetBuffer(kernel,"_CbtStates",States);shader.SetBuffer(kernel,"_CbtCounts",Counts);shader.SetBuffer(kernel,"_SplitRequests",splits);shader.SetBuffer(kernel,"_MergeRequests",merges);shader.SetBuffer(kernel,"_CbtStats",Statistics);
                shader.SetBuffer(kernel,"_CbtWorkArgs",workArguments);shader.SetBuffer(kernel,"_CbtDrawArgs",DrawArguments);shader.SetBuffer(kernel,"_CbtVisible",Triangles);
            }
            shader.SetInt("_CbtTreeWords",TreeWords);shader.SetInt("_CbtGrid",Grid);shader.SetInt("_CbtMaxDepth",MaximumDepth);shader.SetInt("_CbtVertexCount",VertexCount);shader.SetInt("_CbtRequestWords",RequestWords);
            shader.Dispatch(initialize,(12*TreeWords+127)/128,1,1);
        }
        static GraphicsBuffer Buffer(int n)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,n,4);
        void Fold()
        {
            for(int depth=MaximumDepth;depth>=0;depth--){commands.SetComputeIntParam(shader,"_CbtDepth",depth);commands.DispatchCompute(shader,fold,(12*(1<<depth)+127)/128,1,1);}
            commands.DispatchCompute(shader,work,1,1,1);
        }
        public void Update(Camera camera,Vector3 planetRenderCentre,float errorPixels=2,bool disableCulling=false,bool refine=true)
        {
            var position=camera.transform.position-planetRenderCentre;var projection=camera.projectionMatrix;
            if(!hasConfiguration||(position-lastCamera).sqrMagnitude>1e-8f||Quaternion.Angle(camera.transform.rotation,lastRotation)>.005f||!projection.Equals(lastProjection)||lastPixelHeight!=camera.pixelHeight||lastRadius!=definition.radius||lastHeight!=definition.ReliefBound||lastTerrainRevision!=definition.Edits.Revision||lastOcean!=definition.oceanHeight||lastSeed!=definition.terrainSeed||lastError!=errorPixels||lastNoCull!=disableCulling){
                hasConfiguration=true;revision++;settled=false;lastCamera=position;lastRotation=camera.transform.rotation;lastProjection=projection;lastPixelHeight=camera.pixelHeight;lastRadius=definition.radius;lastHeight=definition.ReliefBound;lastTerrainRevision=definition.Edits.Revision;lastOcean=definition.oceanHeight;lastSeed=definition.terrainSeed;lastError=errorPixels;lastNoCull=disableCulling;
            }
            PlanetDevelopment.BindSurface(shader,definition);shader.SetVector("_CbtCamera",camera.transform.position-planetRenderCentre);shader.SetFloat("_CbtProjection",camera.pixelHeight/(2*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)));shader.SetFloat("_CbtErrorPixels",Mathf.Max(.1f,errorPixels));shader.SetInt("_CbtDisableCulling",disableCulling?1:0);
            GeometryUtility.CalculateFrustumPlanes(camera,frustum);
            for(int i=0;i<6;i++)planes[i]=new Vector4(frustum[i].normal.x,frustum[i].normal.y,frustum[i].normal.z,frustum[i].distance+Vector3.Dot(frustum[i].normal,planetRenderCentre));
            shader.SetVectorArray("_CbtPlanes",planes);
            commands.Clear();commands.BeginSample(GpuSamples.Get("Planet.CBT.Update"));
            if((refine&&!settled)||!initialized){
            commands.BeginSample(GpuSamples.Get("Planet.CBT.Refinement"));
            commands.DispatchCompute(shader,clear,(RequestWords+127)/128,1,1);commands.DispatchCompute(shader,mergeCandidates,(12*TreeWords+127)/128,1,1);commands.DispatchCompute(shader,applyMerges,(12*TreeWords+127)/128,1,1);
            Fold();commands.DispatchCompute(shader,request,workArguments,0);
            for(int i=0;i<MaximumDepth;i++)commands.DispatchCompute(shader,propagate,workArguments,0);
            // Ascending depths also apply requests to newly created children in this update.
            for(int depth=0;depth<MaximumDepth;depth++){commands.SetComputeIntParam(shader,"_CbtDepth",depth);commands.DispatchCompute(shader,applySplits,(12*(1<<depth)+127)/128,1,1);}
            Fold();commands.EndSample(GpuSamples.Get("Planet.CBT.Refinement"));initialized=true;}
            commands.BeginSample(GpuSamples.Get("Planet.CBT.Culling"));commands.DispatchCompute(shader,resetVisible,1,1,1);commands.SetBufferCounterValue(Triangles,0);commands.DispatchCompute(shader,emit,workArguments,0);commands.CopyCounterValue(Triangles,DrawArguments,0);commands.DispatchCompute(shader,draw,1,1,1);commands.EndSample(GpuSamples.Get("Planet.CBT.Culling"));
            commands.EndSample(GpuSamples.Get("Planet.CBT.Update"));Graphics.ExecuteCommandBuffer(commands);
            RequestTelemetry();
        }
        public void Bind(Material material){material.EnableKeyword("PLANET_CBT");material.SetInteger("_CbtGrid",Grid);material.SetBuffer("_CbtTriangles",Triangles);}
        public void RequestTelemetry()
        {
            if(disposed||reading||Time.realtimeSinceStartupAsDouble<nextReadback)return;reading=true;nextReadback=Time.realtimeSinceStartupAsDouble+.25;
            int capturedRevision=revision;
            AsyncGPUReadback.Request(Statistics,r=>{if(disposed)return;reading=false;if(r.hasError)return;telemetry=r.GetData<uint>().ToArray();if(capturedRevision==revision&&telemetry[2]==0&&telemetry[3]==0&&telemetry[6]==0)settled=true;});
        }
        public void Dispose(){disposed=true;commands.Dispose();States.Dispose();Counts.Dispose();splits.Dispose();merges.Dispose();workArguments.Dispose();DrawArguments.Dispose();Statistics.Dispose();Triangles.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
