using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        Vector2 gridValidationScreen;
        public void ValidateGridPlacementInput(Action<bool,string> check,Vector2 site)
        {
            BeginPlacement(Base.Catalog.Buildings["gate"]);int original=placementRotation;
            ApplyPlacementWheel(1,true,true);check(placementRotation==original,"HUD scroll cannot rotate placement");
            ApplyPlacementWheel(1,false,false);check(placementRotation==original,"Focus loss suppresses placement wheel");
            ApplyPlacementWheel(.5f,false,true);check(placementRotation==original,"Fractional wheel input accumulates");
            ApplyPlacementWheel(.5f,false,true);check(placementRotation==1,"Wheel rotates preview one quarter turn");
            ApplyPlacementWheel(3,false,true);check(placementRotation==0,"Multiple wheel transitions wrap correctly");
            ApplyPlacementWheel(-1,false,true);check(placementRotation==3,"Reverse wheel rotation wraps correctly");
            check(placementWheelConsumed,"Wheel remains consumed if placement commits in the same frame");
            bool visible=false;
            for(int y=0;y<16&&!visible;y++)for(int x=0;x<16&&!visible;x++){
                var screen=new Vector2(Screen.width*(.3f+x*.025f),Screen.height*(.3f+y*.025f));
                if(OverHud(screen)||!rig.GroundPoint(screen,out var point)||!Base.ValidatePlacement(placing,0,point,true,rotation:placementRotation).valid)continue;
                gridValidationScreen=screen;visible=true;
            }
            check(visible,"Preview fixture is inside actual world UI geometry");
            float zoom=View.orthographicSize;UpdatePlacement(gridValidationScreen);check(View.orthographicSize==zoom,"Placement rotation preserves camera zoom");
            check(ghostPoint==Base.SnapPlacement(placing,ghostPoint,placementRotation),"Rotated preview uses its snapped footprint");
            ValidatePlacementGridBounds(check);
            UpdatePlacement(gridValidationScreen);ValidatePlacementGridBounds(check);
            CancelPlacement();BeginPlacement(Base.Catalog.Buildings["gate"]);UpdatePlacement(gridValidationScreen);ValidatePlacementGridBounds(check);
            wallDragging=true;CancelGestures();check(!wallDragging,"Cancelling gameplay gestures cancels wall drag");
        }
        void ValidatePlacementGridBounds(Action<bool,string> check)
        {
            check(placementGridReady&&placementGridMesh.vertexCount==2500,"Construction grid initializes all line vertices");
            var bounds=placementGrid.GetComponent<Renderer>().bounds;
            bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
            check(Finite(bounds.center.x)&&Finite(bounds.center.y)&&Finite(bounds.center.z)&&Finite(bounds.size.x)&&Finite(bounds.size.y)&&Finite(bounds.size.z)&&bounds.size.x<256&&bounds.size.z<256&&Mathf.Abs(bounds.center.x)<field.Size*2&&Mathf.Abs(bounds.center.z)<field.Size*2,"Construction grid keeps finite local and wrapped world bounds");
        }
        public void CaptureGridPlacementPreview(string path)
        {
            UpdatePlacement(gridValidationScreen);
            // PNG captures need the display transfer, rather than raw linear HDR values.
            var target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();
            var pixels=new Texture2D(2560,1440,TextureFormat.RGB24,false);var active=RenderTexture.active;
            try{
                RenderPipeline.SubmitRenderRequest(View,new RenderPipeline.StandardRequest{destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,2560,1440),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
            }finally{RenderTexture.active=active;target.Release();Destroy(target);Destroy(pixels);}
        }
    }
    // Explicit opt-in only. No work or allocations during normal play.
    public sealed class GridPlayerValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status,error,unity,gpu,timestamp;public string[] checks;}
        readonly List<string> checks=new List<string>();
        BattleApp app;
        string folder;
        int runtimeErrors;
        string firstRuntimeError="";
        void RuntimeLog(string message,string stack,LogType type)
        {
            if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert)return;
            runtimeErrors++;if(firstRuntimeError.Length==0)firstRuntimeError=message;
        }
        void OnDestroy()=>Application.logMessageReceived-=RuntimeLog;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-grid-validation")>=0)new GameObject("Grid player validation").AddComponent<GridPlayerValidation>();}
        static string Arg(string name,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);checks.Add(text);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=RuntimeLog;
            folder=Arg("-benchmark-output",Application.persistentDataPath);Directory.CreateDirectory(folder);app=FindAnyObjectByType<BattleApp>();
            app.settings.world.seed=app.settings.seed=73129;app.settings.world.size=int.Parse(Arg("-benchmark-world-size","4096"));app.settings.enemyAI=false;
            app.StartFlatValidationBattle(256);double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            yield return new WaitForSecondsRealtime(1);
            string error="";
            try{
                Check(app.State=="battle","Standalone building match loads");app.Paused=true;app.Base.EnemyEnabled=false;
                GridPlacementChecks.Geometry(app.Base.Catalog,Check);
                var gate=GridPlacementChecks.World(app.Base,Check,0);GridPlacementChecks.World(app.Base,Check,1);
                app.SelectBuildingForUiBenchmark(gate);app.View.GetComponent<RtsCamera>().FocusBase(gate.position);
                Check(app.Base.TryFindPlacement("gate",0,out var site),"Standalone preview foundation found");app.ValidateGridPlacementInput(Check,site);
            }catch(Exception e){error=e.ToString();Debug.LogException(e);}
            yield return null;yield return null;
            if(error.Length==0)app.CaptureGridPlacementPreview(Path.GetFullPath(Path.Combine(folder,"placement.png")));yield return new WaitForSecondsRealtime(.5f);
            if(error.Length==0&&runtimeErrors>0)error="Runtime logged "+runtimeErrors+" errors: "+firstRuntimeError;
            if(error.Length==0)checks.Add("No runtime error, exception or invalid render bounds logged");
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,timestamp=DateTime.UtcNow.ToString("O"),checks=checks.ToArray()},true));Application.Quit(error.Length==0?0:1);
        }
    }
}
