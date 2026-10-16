using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    /// <summary>Seven shared mesh batches, GPU culled. No GameObject or Transform per landscape object.</summary>
    public sealed class SceneryRenderer : IDisposable
    {
        sealed class Batch
        {
            public Mesh mesh;public Material material;public GraphicsBuffer instances,visible,args;public int count;
        }
        readonly Batch[] batches=new Batch[7];
        readonly ComputeShader shader;
        readonly CommandBuffer cmd=new CommandBuffer{name="Biome scenery GPU visibility"};
        readonly int kernel;
        readonly float size;
        readonly Battlefield field;readonly int ground;int terrainRevision;
        readonly Vector4[] planes=new Vector4[6];
        public SceneryRenderer(Battlefield field)
        {
            this.field=field;terrainRevision=field.TerrainRevision;size=field.Size;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/Scenery"));kernel=shader.FindKernel("Cull");ground=shader.FindKernel("Ground");
            for(int i=0;i<batches.Length;i++)
            {
                var b=batches[i]=new Batch{mesh=ArmyMeshes.Scenery(i),count=field.Map.Scenery[i].Count};
                b.material=new Material(Resources.Load<Shader>("MassiveArmy/Scenery")){name="Biome scenery "+i,enableInstancing=true};
                b.instances=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.Max(1,b.count),32);
                if(b.count>0)b.instances.SetData(field.Map.Scenery[i]);
                b.visible=new GraphicsBuffer(GraphicsBuffer.Target.Append,Mathf.Max(1,b.count),4);
                b.args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size);
                b.args.SetData(new[]{new GraphicsBuffer.IndirectDrawIndexedArgs{indexCountPerInstance=b.mesh.GetIndexCount(0)}});
                b.material.SetBuffer("_Instances",b.instances);b.material.SetBuffer("_Visible",b.visible);
            }
        }
        public void Render(Camera camera,Matrix4x4 vp,bool shadows=true)
        {
            var frustum=GeometryUtility.CalculateFrustumPlanes(camera);
            for(int i=0;i<6;i++)planes[i]=new Vector4(frustum[i].normal.x,frustum[i].normal.y,frustum[i].normal.z,frustum[i].distance);
            cmd.Clear();cmd.SetComputeFloatParam(shader,"_WorldSize",size);cmd.SetComputeVectorParam(shader,"_RenderFocus",WorldTopology.ViewFocus(camera));cmd.BeginSample(GpuSamples.Get("Army.ForestVisibility"));cmd.SetComputeVectorArrayParam(shader,"_Planes",planes);
            foreach(var b in batches)if(b.count>0)
            {
                cmd.SetBufferCounterValue(b.visible,0);cmd.SetComputeIntParam(shader,"_Count",b.count);
                if(terrainRevision!=field.TerrainRevision){cmd.SetComputeBufferParam(shader,ground,"_GroundInstances",b.instances);cmd.SetComputeTextureParam(shader,ground,"_Terrain",field.Terrain);cmd.DispatchCompute(shader,ground,(b.count+127)/128,1,1);}
                cmd.SetComputeBufferParam(shader,kernel,"_Instances",b.instances);cmd.SetComputeBufferParam(shader,kernel,"_Visible",b.visible);
                cmd.DispatchCompute(shader,kernel,(b.count+127)/128,1,1);cmd.CopyCounterValue(b.visible,b.args,4);
            }
            cmd.EndSample(GpuSamples.Get("Army.ForestVisibility"));Graphics.ExecuteCommandBuffer(cmd);
            terrainRevision=field.TerrainRevision;
            foreach(var b in batches)if(b.count>0) {
                b.material.SetMatrix("_BattleVP",vp);b.material.SetFloat("_WorldSize",size);b.material.SetVector("_RenderFocus",WorldTopology.ViewFocus(camera));
                Graphics.RenderMeshIndirect(new RenderParams(b.material){camera=camera,worldBounds=new Bounds(camera.transform.position,new Vector3(size*3,6000,size*3)),shadowCastingMode=shadows&&camera.orthographic&&camera.orthographicSize<=300?ShadowCastingMode.On:ShadowCastingMode.Off},b.mesh,b.args);
            }
        }
        public void Dispose() { cmd.Dispose();ArmyLifetime.Destroy(shader);foreach(var b in batches) { b.instances.Dispose();b.visible.Dispose();b.args.Dispose();ArmyLifetime.Destroy(b.material);ArmyLifetime.Destroy(b.mesh); } }
    }
}
