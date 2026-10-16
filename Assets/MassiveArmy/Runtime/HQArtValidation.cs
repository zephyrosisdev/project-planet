using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        public void PrepareHQArtPlacement(Action<bool,string> check)
        {
            BeginPlacement(Base.Catalog.Buildings["command"]);bool found=false;
            for(int y=0;y<24&&!found;y++)for(int x=0;x<24&&!found;x++){
                var screen=new Vector2(Screen.width*(.26f+x*.02f),Screen.height*(.22f+y*.025f));
                if(OverHud(screen)||!rig.GroundPoint(screen,out var point)||!Base.ValidatePlacement(placing,0,Base.SnapPlacement(placing,point,0),rotation:0).valid)continue;
                gridValidationScreen=screen;found=true;
            }
            check(found,"HQ construction fixture finds a valid grid site");UpdatePlacement(gridValidationScreen);
            check(modelGhost&&ghost.GetComponent<MeshFilter>().sharedMesh==Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue").Find("command").near,"Construction preview uses the detailed HQ shell");
            check(ghost.transform.localScale==Vector3.one,"HQ preview preserves authored metres");
            ApplyPlacementWheel(1,false,true);UpdatePlacement(gridValidationScreen);check(placementRotation==1,"HQ preview rotates one quarter turn");
            check(ghostPoint==Base.SnapPlacement(placing,ghostPoint,placementRotation),"Rotated HQ remains snapped to its placement grid");
        }
        public void FinishHQArtPlacement()=>CancelPlacement();
    }
    // Explicit standalone fixture, absent from the normal play loop.
    public sealed class HQArtValidation : MonoBehaviour
    {
        [Serializable] sealed class Report { public string status,error,gpu;public int triangles,vertices;public string[] checks; }
        readonly List<string> checks=new List<string>();int errors;string failure="";
        static string Arg(string key,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-hq-validation")>=0)new GameObject("HQ art validation").AddComponent<HQArtValidation>();}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;failure=message;}}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);checks.Add(message);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;string folder=Arg("-benchmark-output",Application.persistentDataPath);Directory.CreateDirectory(folder);
            var app=FindAnyObjectByType<BattleApp>();app.settings.enemyAI=false;app.settings.world.seed=app.settings.seed=73129;app.settings.world.size=4096;
            app.StartFlatValidationBattle(256);double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            string error="";var art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");var hq=art?.Find("command");GameObject preview=null;
            try{
                Check(app.State=="battle","Existing base battle starts");Check(art!=null&&art.buildings.Length==49&&art.unitIds.Length==31,"Existing catalogue coverage retained");
                Check(hq!=null&&hq.near.triangles.Length/3>10000,"Detailed HQ shell loaded");
                Check(hq.near.bounds.size.x<=40.01f&&hq.near.bounds.size.z<=40.01f,"HQ remains within 5 x 5 grid footprint");
                Check(hq.material.shader.isSupported&&hq.material.shader.name=="MassiveArmy/HQ concept armour","HQ material supported in standalone");
                Check(hq.near.colors.Length==hq.near.vertexCount,"Crevice shading present on all HQ vertices");
                foreach(string map in new[]{"_Albedo","_Metallic","_Normal"})Check(hq.material.GetTexture(map)?.width==2048,"2048px trim map: "+map);
                app.View.GetComponent<RtsCamera>().enabled=false;
                // Isolate the real runtime material above the battlefield for an uncluttered comparison.
                preview=new GameObject("HQ comparison shell");preview.AddComponent<MeshFilter>().sharedMesh=hq.near;var renderer=preview.AddComponent<MeshRenderer>();renderer.sharedMaterial=hq.material;
                var properties=new MaterialPropertyBlock();properties.SetColor("_TeamColor",new Color(.02f,.7f,1));renderer.SetPropertyBlock(properties);
                preview.layer=31;preview.transform.position=Vector3.zero;
            }catch(Exception e){error=e.ToString();}
            yield return new WaitForSecondsRealtime(1);
            if(error.Length==0){
                // Independent camera: queued GPU draws belong to the gameplay
                // camera and retain its explicit VP matrix, so moving it is not
                // a valid way to isolate an art asset for a comparison capture.
                var cameraObject=new GameObject("HQ isolated HDRP camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=20f;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.cullingMask=1<<31;
                camera.transform.position=new Vector3(68.23f,64,103.32f);camera.transform.LookAt(new Vector3(6.23f,14,13.32f));
                var data=cameraObject.AddComponent<HDAdditionalCameraData>();data.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;data.backgroundColorHDR=new Color(.008f,.013f,.020f,1);data.volumeLayerMask=0;data.customRenderingSettings=true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess,false);data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ExposureControl,false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess]=true;data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ExposureControl]=true;
                var target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();var pixels=new Texture2D(2560,1440,TextureFormat.RGB24,false);var active=RenderTexture.active;
                try{RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,2560,1440),0,0);pixels.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(folder,"hq-runtime-concept-view.png")),pixels.EncodeToPNG());checks.Add("Actual isolated HDRP material screenshot captured");}
                finally{RenderTexture.active=active;target.Release();Destroy(target);Destroy(pixels);Destroy(cameraObject);}
            }
            if(preview!=null)Destroy(preview);
            app.View.GetComponent<RtsCamera>().enabled=true;app.View.GetComponent<RtsCamera>().FocusBase(app.Base.BasePosition(0));
            yield return new WaitForSecondsRealtime(1);
            if(error.Length==0){try{app.PrepareHQArtPlacement(Check);app.CaptureGridPlacementPreview(Path.GetFullPath(Path.Combine(folder,"hq-placement.png")));app.FinishHQArtPlacement();}catch(Exception e){error=e.ToString();}}
            yield return new WaitForSecondsRealtime(.5f);
            if(error.Length==0&&errors>0)error="Runtime errors: "+errors+" "+failure;
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,triangles=hq?.near.triangles.Length/3??0,vertices=hq?.near.vertexCount??0,checks=checks.ToArray()},true));Application.Quit(error.Length==0?0:1);
        }
    }
}
