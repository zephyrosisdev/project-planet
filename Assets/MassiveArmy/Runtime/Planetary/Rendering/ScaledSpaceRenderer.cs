using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Cheap visual spheres. Physics and navigation never receive these compressed poses.</summary>
    public sealed class ScaledSpaceRenderer : IDisposable
    {
        readonly Material material;PlanetDistantTerrain ownPreviews;
        readonly List<MaterialPropertyBlock> blocks=new List<MaterialPropertyBlock>();readonly List<int> counts=new List<int>();
        public ScaledSpaceRenderer(GraphicsBuffer colours){material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetScaledBody"));material.SetBuffer("_BiomeColours",colours);material.SetInteger("_BodyDebug",Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-preview-debug")>=0?1:0);Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary").Bind(material);}
        public void Render(Camera camera,FloatingOrigin origin,IReadOnlyList<PlanetDefinition> bodies,PlanetDefinition foreground=null,PlanetDistantTerrain previews=null)
        {
            if(foreground!=null)foreground.Edits.Bind(material);
            if(previews==null)previews=ownPreviews??(ownPreviews=new PlanetDistantTerrain());
            var observer=origin.Origin.Add(new Double3(camera.transform.position));var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
            counts.Clear();
            for(int i=0;i<bodies.Count;i++){
                var body=bodies[i];if(body==null)continue;var preview=previews.Get(body);var visual=ScaledSpace.Project(body.position,observer,body.radius);
                while(blocks.Count<=counts.Count)blocks.Add(new MaterialPropertyBlock());var properties=blocks[counts.Count];
                var centre=camera.transform.position+visual.position;properties.Clear();properties.SetMatrix("_PlanetVP",vp);properties.SetVector("_ScaledCentre",centre);properties.SetFloat("_ScaledRadius",visual.radius);properties.SetVector("_CameraPosition",camera.transform.position);
                var physical=body.position.RelativeTo(observer).Float;properties.SetVector("_PhysicalBody",new Vector4(physical.x,physical.y,physical.z,body.radius));
                var occluder=foreground!=null?foreground.position.RelativeTo(observer).Float:Vector3.zero;
                properties.SetVector("_ForegroundSphere",new Vector4(occluder.x,occluder.y,occluder.z,foreground!=null?foreground.radius:0));properties.SetFloat("_PlanetRadius",foreground!=null?foreground.radius:1);properties.SetFloat("_PlanetHeight",foreground!=null?foreground.terrainHeight:0);properties.SetFloat("_PlanetOcean",foreground!=null?foreground.oceanHeight:0);properties.SetFloat("_PlanetSeed",foreground!=null?(foreground.terrainSeed%1009)*.017f:0);
                properties.SetTexture("_BodySurface",preview.Surface);properties.SetFloat("_BodyRadius",body.radius);properties.SetFloat("_BodyOcean",body.oceanHeight);properties.SetFloat("_BodySeed",(body.terrainSeed%1009)*.017f);properties.SetFloat("_ScaledFactor",visual.factor);
                float pixels=visual.radius/Mathf.Max(1,visual.position.magnitude)*camera.pixelHeight/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);int grid=pixels>300?64:pixels>60?32:16;properties.SetInteger("_BodyGrid",grid);
                counts.Add(36*grid*grid);
            }
        }
        public void Draw(CommandBuffer cmd){for(int i=0;i<counts.Count;i++)cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,counts[i],1,blocks[i]);}
        public void Dispose(){ownPreviews?.Dispose();TerrainSurfaceLibrary.Unbind(material);ArmyLifetime.Destroy(material);}
    }
}
