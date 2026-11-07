using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    // Lit procedural draws need HDRP's camera light/shadow buffers at a supported
    // injection point. This reuses the existing GPU terrain buffers and topology.
    public sealed class PlanetTerrainRenderer : IDisposable
    {
        readonly CustomPassVolume volume;
        [Serializable]sealed class TerrainPass : CustomPass
        {
            [NonSerialized] public PlanetDevelopment planet;
            protected override void Execute(CustomPassContext ctx){
                if(planet==null||!planet.Ready||ctx.hdCamera.camera!=planet.View)return;
                CoreUtils.SetRenderTarget(ctx.cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);
                planet.RenderTerrain(ctx.cmd);planet.RenderWeather(ctx);
            }
        }
        public PlanetTerrainRenderer(PlanetDevelopment owner){
            volume=owner.gameObject.AddComponent<CustomPassVolume>();volume.isGlobal=true;volume.injectionPoint=CustomPassInjectionPoint.BeforeTransparent;
            volume.customPasses.Add(new TerrainPass{planet=owner,name="Planet lit terrain",targetColorBuffer=CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None});
        }
        public void Dispose(){if(volume!=null)volume.enabled=false;ArmyLifetime.Destroy(volume);}
    }
}
