using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    /// <summary>Projected flight instruments retain their colour after the world's exposure and tonemapping.</summary>
    public sealed class PlanetFlightHudRenderer:IDisposable
    {
        readonly CustomPassVolume volume;
        [Serializable]sealed class FlightPass:CustomPass
        {
            public PlanetDevelopment owner;
            protected override void Execute(CustomPassContext ctx){
                if(owner==null||!owner.Ready||!owner.ResidentActive||!owner.IsPiloting||ctx.hdCamera.camera!=owner.View)return;
                CoreUtils.SetRenderTarget(ctx.cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);
                owner.ShipForValidation.DrawFlightInstruments(ctx.cmd);
            }
        }
        public PlanetFlightHudRenderer(PlanetDevelopment owner){volume=owner.gameObject.AddComponent<CustomPassVolume>();volume.enabled=false;volume.isGlobal=true;volume.injectionPoint=CustomPassInjectionPoint.AfterPostProcess;volume.customPasses.Add(new FlightPass{owner=owner,name="Projected flight instruments",targetColorBuffer=CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None});}
        public void SetActive(bool active){if(volume!=null&&volume.enabled!=active)volume.enabled=active;}
        public void Dispose(){if(volume!=null)volume.enabled=false;ArmyLifetime.Destroy(volume);}
    }
}
