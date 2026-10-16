using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    /// <summary>Sparse visual quadtree. Only visible patch descriptors are resident on the GPU;
    /// vertices and surface layers are evaluated on demand by the existing terrain shader.
    /// This owns no simulation state and eviction cannot remove armies or buildings.</summary>
    public sealed class PlanetStreamedTerrain : IDisposable
    {
        public const int Grid=16,MaximumLevel=20;
        public readonly int PatchBudget;
        public struct Patch
        {
            public int face,level,x,y;
            public float Span=>2f/(1<<level);
            public Vector4 Descriptor=>new Vector4(-1+x*Span,-1+y*Span,Span,face);
        }
        readonly PlanetDefinition definition;int lastTerrainRevision=-1;
        readonly List<Patch> leaves;
        struct Candidate { public Patch patch;public float score; }
        readonly List<Candidate> queue;
        readonly Vector4[] descriptors;
        readonly GraphicsBuffer patches,queueBuffer;
        public GraphicsBuffer DrawArguments {get;}
        public GraphicsBuffer Statistics {get;}
        readonly ComputeShader shader;readonly int generate;
        readonly CommandBuffer generationCommands=new CommandBuffer{name="Planet sparse terrain residency"};
        readonly Vector4[] gpuPlanes=new Vector4[6];bool reading,disposed;double nextReadback;
        uint[] telemetry=new uint[4];
        readonly Plane[] planes=new Plane[6];
        Vector3 observer,lastPosition;Quaternion lastRotation;Matrix4x4 lastProjection;
        float projection,error,lastRadius,lastHeight,lastError,lastOcean;int lastPixelHeight;bool hasView;
        public int ResidentPatches {get;private set;}
        public int LeafCount=>leaves.Count;
        public int DeepestLevel {get;private set;}
        public bool BudgetLimited {get;private set;}
        public int Revision {get;private set;}
        public long BufferBytes=>(long)PatchBudget*48+32;
        public int VerticesPerPatch=>(Grid*Grid+4*Grid)*6;
        public IReadOnlyList<Patch> Leaves=>leaves;
        public PlanetStreamedTerrain(PlanetDefinition definition,int patchBudget=384)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            if(patchBudget<6||patchBudget>4096)throw new ArgumentOutOfRangeException(nameof(patchBudget));
            this.definition=definition;PatchBudget=patchBudget;leaves=new List<Patch>(patchBudget);
            queue=new List<Candidate>(patchBudget);descriptors=new Vector4[patchBudget];patches=new GraphicsBuffer(GraphicsBuffer.Target.Structured,patchBudget,16);
            queueBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,patchBudget,32);Statistics=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4);
            DrawArguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Structured,4,4);
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetStreaming"));generate=shader.FindKernel("Generate");
            shader.SetBuffer(generate,"_Queue",queueBuffer);shader.SetBuffer(generate,"_Patches",patches);shader.SetBuffer(generate,"_Stats",Statistics);shader.SetBuffer(generate,"_DrawArgs",DrawArguments);
            shader.SetInt("_Budget",patchBudget);shader.SetInt("_Vertices",VerticesPerPatch);
        }
        public void Bind(Material material)
        {
            material.DisableKeyword("PLANET_CBT");material.EnableKeyword("PLANET_STREAMED");
            material.SetBuffer("_PlanetPatches",patches);material.SetInteger("_PlanetPatchGrid",Grid);
        }
        public void Bind(MaterialPropertyBlock block){block.SetBuffer("_PlanetPatches",patches);block.SetInteger("_PlanetPatchGrid",Grid);}
        // Conservative sphere enclosing the patch on land and ocean, including its skirts.
        void Bound(Patch p,out Vector3 centre,out float bound)
        {
            var uv=p.Descriptor;var axis=CubeSphere.Direction((CubeFace)p.face,new Vector2(uv.x+uv.z*.5f,uv.y+uv.z*.5f));
            float minimum=1;
            for(int y=0;y<2;y++)for(int x=0;x<2;x++)minimum=Mathf.Min(minimum,Vector3.Dot(axis,CubeSphere.Direction((CubeFace)p.face,new Vector2(uv.x+uv.z*x,uv.y+uv.z*y))));
            centre=axis*definition.radius;bound=definition.radius*Mathf.Sqrt(Mathf.Max(0,2-2*minimum))+Mathf.Abs(definition.ReliefBound)*3+Mathf.Abs(definition.oceanHeight);
        }
        bool Visible(Patch p,out float distance)
        {
            Bound(p,out var centre,out float bound);distance=Mathf.Max(1,Vector3.Distance(observer,centre)-bound);
            foreach(var plane in planes)if(plane.GetDistanceToPoint(centre)<-bound)return false;
            float rmin=Mathf.Max(1,definition.radius-Mathf.Abs(definition.ReliefBound)),length=observer.magnitude;
            if(length>rmin){float horizon=rmin*rmin/length;
                // The tangent plane at the conservative inner sphere cannot occlude a patch above it.
                if(Vector3.Dot(centre,observer/length)+bound<horizon-Mathf.Abs(definition.ReliefBound)*2)return false;}
            return true;
        }
        float Score(Patch p)
        {
            if(p.level>=MaximumLevel||!Visible(p,out float distance))return 0;
            float segment=definition.radius*p.Span/Grid;
            // Curvature plus the current analytic relief Hessian estimate; texture frequency does not require geometry subdivision.
            Bound(p,out var center,out float bound);float curvature=Mathf.Max((.125f+80*definition.ReliefBound/Mathf.Max(1,definition.radius))/Mathf.Max(1,definition.radius),Mathf.Max(PlanetTerrainEdits.ReliefCurvature(definition),definition.Edits.FeatureCurvature(center,bound)));return segment*segment*curvature*projection/distance/error;
        }
        public void UpdateReferenceForValidation(Camera camera,Vector3 planetRenderCentre,float errorPixels=2,bool force=false)
        {
            observer=camera.transform.position-planetRenderCentre;projection=camera.pixelHeight/(2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f));error=Mathf.Max(.25f,errorPixels);
            if(!force&&hasView&&(lastPosition-observer).sqrMagnitude<.01f&&Quaternion.Angle(lastRotation,camera.transform.rotation)<.02f&&lastProjection.Equals(camera.projectionMatrix)&&lastPixelHeight==camera.pixelHeight&&lastRadius==definition.radius&&lastHeight==definition.ReliefBound&&lastTerrainRevision==definition.Edits.Revision&&lastOcean==definition.oceanHeight&&lastError==error)return;
            hasView=true;lastPosition=observer;lastRotation=camera.transform.rotation;lastProjection=camera.projectionMatrix;lastPixelHeight=camera.pixelHeight;lastRadius=definition.radius;lastHeight=definition.ReliefBound;lastTerrainRevision=definition.Edits.Revision;lastOcean=definition.oceanHeight;lastError=error;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            for(int i=0;i<planes.Length;i++){var p=planes[i];p.distance+=Vector3.Dot(p.normal,planetRenderCentre);planes[i]=p;}
            leaves.Clear();queue.Clear();for(int face=0;face<6;face++)Push(new Patch{face=face});
            BudgetLimited=false;DeepestLevel=0;
            // A bounded max heap keeps camera updates O(budget log budget).
            while(queue.Count>0){var candidate=Pop();
                if(candidate.score<=1){leaves.Add(candidate.patch);continue;}
                if(leaves.Count+queue.Count+4>PatchBudget){BudgetLimited=true;leaves.Add(candidate.patch);continue;}
                var parent=candidate.patch;
                for(int y=0;y<2;y++)for(int x=0;x<2;x++)Push(new Patch{face=parent.face,level=parent.level+1,x=parent.x*2+x,y=parent.y*2+y});
            }
            ResidentPatches=0;
            foreach(var p in leaves){DeepestLevel=Mathf.Max(DeepestLevel,p.level);if(Visible(p,out _))descriptors[ResidentPatches++]=p.Descriptor;}
            if(ResidentPatches>0)patches.SetData(descriptors,0,0,ResidentPatches);Revision++;
        }
        public void Update(Camera camera,Vector3 planetRenderCentre,float errorPixels=2,bool force=false)
        {
            observer=camera.transform.position-planetRenderCentre;error=Mathf.Max(.25f,errorPixels);
            if(!force&&hasView&&(lastPosition-observer).sqrMagnitude<.01f&&Quaternion.Angle(lastRotation,camera.transform.rotation)<.02f&&lastProjection.Equals(camera.projectionMatrix)&&lastPixelHeight==camera.pixelHeight&&lastRadius==definition.radius&&lastHeight==definition.ReliefBound&&lastTerrainRevision==definition.Edits.Revision&&lastOcean==definition.oceanHeight&&lastError==error){ReadTelemetry();return;}
            hasView=true;lastPosition=observer;lastRotation=camera.transform.rotation;lastProjection=camera.projectionMatrix;lastPixelHeight=camera.pixelHeight;lastRadius=definition.radius;lastHeight=definition.ReliefBound;lastTerrainRevision=definition.Edits.Revision;lastOcean=definition.oceanHeight;lastError=error;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            for(int i=0;i<6;i++)gpuPlanes[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance+Vector3.Dot(planes[i].normal,planetRenderCentre));
            PlanetDevelopment.BindSurface(shader,definition);shader.SetVectorArray("_Planes",gpuPlanes);shader.SetVector("_Observer",observer);shader.SetFloat("_Projection",camera.pixelHeight/(2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)));shader.SetFloat("_Error",error);
            generationCommands.Clear();generationCommands.BeginSample(GpuSamples.Get("Planet.Streaming.Generate"));generationCommands.DispatchCompute(shader,generate,1,1,1);generationCommands.EndSample(GpuSamples.Get("Planet.Streaming.Generate"));Graphics.ExecuteCommandBuffer(generationCommands);Revision++;ReadTelemetry();
        }
        void ReadTelemetry(){if(disposed||reading||Time.realtimeSinceStartupAsDouble<nextReadback)return;reading=true;nextReadback=Time.realtimeSinceStartupAsDouble+.25;
            AsyncGPUReadback.Request(Statistics,result=>{if(disposed)return;reading=false;if(result.hasError)return;telemetry=result.GetData<uint>().ToArray();ApplyTelemetry();});}
        void ApplyTelemetry(){ResidentPatches=(int)telemetry[0];DeepestLevel=(int)telemetry[2];BudgetLimited=telemetry[3]!=0;}
        public int GpuLeafCount=>(int)telemetry[1];
        public Vector4[] ReadPatchesForValidation(){Statistics.GetData(telemetry);ApplyTelemetry();var values=new Vector4[ResidentPatches];if(values.Length>0)patches.GetData(values);return values;}
        void Push(Patch patch)
        {
            var value=new Candidate{patch=patch,score=Score(patch)};int index=queue.Count;queue.Add(value);
            while(index>0){int parent=(index-1)/2;if(queue[parent].score>=value.score)break;queue[index]=queue[parent];index=parent;}queue[index]=value;
        }
        Candidate Pop()
        {
            var result=queue[0];int count=queue.Count-1;var last=queue[count];queue.RemoveAt(count);if(count==0)return result;int index=0;
            while(index*2+1<count){int child=index*2+1;if(child+1<count&&queue[child+1].score>queue[child].score)child++;if(queue[child].score<=last.score)break;queue[index]=queue[child];index=child;}queue[index]=last;return result;
        }
        public void Dispose(){disposed=true;generationCommands.Dispose();patches.Dispose();queueBuffer.Dispose();Statistics.Dispose();DrawArguments.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
