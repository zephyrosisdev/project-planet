#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        bool capturedWorld,capturedPilot,capturingPresentation;
        void ObservePresentationCapture(bool pilot){
            if(capturingPresentation||pilot&&capturedPilot||!pilot&&capturedWorld)return;
            if(pilot)capturedPilot=true;else capturedWorld=true;
            StartCoroutine(CapturePresentation(pilot));
        }
        IEnumerator CapturePresentation(bool pilot,string suffix=null){
            capturingPresentation=true;var world=session.ClientWorld;var craft=pilot?world.ShipForValidation:null;
            var ui=new RenderTexture(960,540,0,RenderTextureFormat.ARGB32);ui.Create();
            var pilotUi=pilot?new RenderTexture(960,540,0,RenderTextureFormat.ARGB32):null;if(pilotUi!=null)pilotUi.Create();
            var scene=new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf);scene.Create();
            var composite=new RenderTexture(960,540,0,RenderTextureFormat.ARGB32);composite.Create();
            var final=new RenderTexture(960,540,0,RenderTextureFormat.ARGB32);final.Create();
            var material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUiComposite"));
            var camera=world.View;var previousTarget=camera.targetTexture;bool previousEnabled=camera.enabled;
            try{
                session.SetNetworkToolkitTarget(ui);if(pilotUi!=null)craft.SetPilotRenderTarget(pilotUi);
                for(int i=0;i<8;i++)yield return null;
                if(camera==null)yield break;
                camera.enabled=false;camera.targetTexture=scene;
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=scene});
                material.SetTexture("_Ui",ui);Graphics.Blit(scene,composite,material);
                if(pilotUi!=null){material.SetTexture("_Ui",pilotUi);Graphics.Blit(composite,final,material);}else Graphics.Blit(composite,final);
                var previous=RenderTexture.active;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
                try{RenderTexture.active=final;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(path+(suffix??(pilot?".pilot.png":".world.png")),image.EncodeToPNG());}
                finally{RenderTexture.active=previous;Destroy(image);}
            }finally{
                session.SetNetworkToolkitTarget(null);craft?.SetPilotRenderTarget(null);
                if(camera!=null){camera.targetTexture=previousTarget;camera.enabled=previousEnabled;}
                foreach(var target in new[]{ui,pilotUi,scene,composite,final})if(target!=null){target.Release();Destroy(target);}Destroy(material);capturingPresentation=false;
            }
        }
    }
    public sealed partial class SurvivalNetworkSession {
        internal void SetNetworkToolkitTarget(RenderTexture target){if(inventoryToolkit?.Root!=null)inventoryToolkit.SetRenderTarget(target);else networkToolkit?.SetRenderTarget(target);}
    }
}
#endif
