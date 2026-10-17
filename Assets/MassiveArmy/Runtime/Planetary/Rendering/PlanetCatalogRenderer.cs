using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    public sealed class PlanetCatalogRenderer : IDisposable
    {
        readonly ArtUnitRenderer art;
        readonly Mesh[] meshes;readonly Material[] materials;
        readonly ComputeShader cull;readonly GraphicsBuffer visible,args;readonly int kernel;
        readonly GraphicsBuffer.IndirectDrawIndexedArgs[] reset=new GraphicsBuffer.IndirectDrawIndexedArgs[3];readonly Plane[] planes=new Plane[6];readonly Vector4[] frustum=new Vector4[6];
        readonly PlanetDefinition definition;
        public PlanetCatalogRenderer(PlanetDefinition definition,GraphicsBuffer units,GraphicsBuffer traits,int capacity)
        {
            this.definition=definition;art=new ArtUnitRenderer(true);meshes=new[]{art.NearMesh,art.FarMesh,ArmyMeshes.Quad()};
            visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,capacity*3,4);args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,3,GraphicsBuffer.IndirectDrawIndexedArgs.size);
            cull=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetArtCull"));PlanetDevelopment.BindSurface(cull,definition);kernel=cull.FindKernel("Cull");cull.SetInt("_ConservativeCull",1);
            cull.SetBuffer(kernel,"_PlanetUnits",units);cull.SetBuffer(kernel,"_Traits",traits);cull.SetBuffer(kernel,"_Visible",visible);cull.SetBuffer(kernel,"_Arguments",args);cull.SetInt("_Capacity",capacity);
            materials=new Material[3];for(int i=0;i<3;i++){reset[i].indexCountPerInstance=meshes[i].GetIndexCount(0);materials[i]=new Material(Resources.Load<Shader>("MassiveArmy/PlanetArtUnits")){enableInstancing=true};art.Bind(materials[i],i);materials[i].SetBuffer("_PlanetUnits",units);materials[i].SetBuffer("_Traits",traits);materials[i].SetBuffer("_Visible",visible);materials[i].SetInt("_Mode",i);materials[i].SetInt("_Capacity",capacity);Bind(materials[i]);}
        }
        void Bind(Material material){material.SetFloat("_PlanetRadius",definition.radius);material.SetFloat("_PlanetHeight",definition.terrainHeight);material.SetFloat("_PlanetSeed",(definition.terrainSeed%1009)*.017f);material.SetFloat("_PlanetOcean",definition.oceanHeight);definition.Edits.Bind(material);}
        internal void BindArt(Material material)=>art.Bind(material,0);
        public void Render(Camera camera,Vector3 centre,int count)
        {
            if(count<=0)return;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);for(int i=0;i<6;i++)frustum[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance+Vector3.Dot(planes[i].normal,centre));
            args.SetData(reset);cull.SetVectorArray("_Frustum",frustum);cull.SetVector("_CameraLocal",camera.transform.position-centre);cull.SetFloat("_ProjectionScale",camera.pixelHeight/(2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)));cull.SetInt("_Count",count);cull.Dispatch(kernel,(count+127)/128,1,1);
            var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;var bounds=new Bounds(centre,Vector3.one*(definition.radius+definition.ReliefBound+1000)*2);
            for(int i=0;i<3;i++){var material=materials[i];material.SetMatrix("_PlanetVP",vp);material.SetVector("_PlanetCenter",centre);material.SetVector("_CameraPosition",camera.transform.position);material.SetVector("_CameraRight",camera.transform.right);material.SetVector("_CameraUp",camera.transform.up);material.SetFloat("_TimeSeconds",Time.time);
                Graphics.RenderMeshIndirect(new RenderParams(material){camera=camera,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},meshes[i],args,1,i);}
        }
        public void Dispose(){art.Dispose();ArmyLifetime.Destroy(meshes[2]);foreach(var m in materials)ArmyLifetime.Destroy(m);ArmyLifetime.Destroy(cull);visible.Dispose();args.Dispose();}
    }
}

