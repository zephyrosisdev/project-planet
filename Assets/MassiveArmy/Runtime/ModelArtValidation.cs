using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MassiveArmy
{
    // Standalone acceptance fixture. All units are created through the normal GPU spawn API.
    public sealed class ModelArtValidation : MonoBehaviour
    {
        [Serializable] sealed class Report { public string status,error,gpu;public int buildings,units,spawned;public string[] checks; }
        readonly List<string> checks=new List<string>();
        int errors,spawned;string failure="";
        static string Arg(string key,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-art-validation")>=0)new GameObject("Model art acceptance").AddComponent<ModelArtValidation>();}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;failure=message;}}
        void OnDestroy(){Application.logMessageReceived-=Log;}
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);checks.Add(message);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;
            string folder=Arg("-benchmark-output",Application.persistentDataPath);Directory.CreateDirectory(folder);
            var app=FindAnyObjectByType<BattleApp>();app.settings.enemyAI=false;app.settings.world.seed=app.settings.seed=73129;app.settings.world.size=4096;
            app.StartFlatValidationBattle(256);double deadline=Time.realtimeSinceStartupAsDouble+120;
            while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            string error="";var art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");
            try{
                Check(app.State=="battle","Textured base battle starts");Check(art!=null&&art.buildings.Length==49&&art.unitIds.Length==31,"Complete 49 building / 31 unit catalogue");
                foreach(var d in app.Base.Catalog.buildings){var b=art.Find(d.id);Check(b!=null&&b.near.vertexCount>0&&b.far.vertexCount>0&&b.material.shader.isSupported,"Shell meshes and supported shader: "+d.id);
                    Check(b.near.bounds.size.x<=d.footprintWidth*8+.01f&&b.near.bounds.size.z<=d.footprintDepth*8+.01f,"Reserved foundation: "+d.id);
                    foreach(string map in new[]{"_Albedo","_Metallic","_Normal"})Check(b.material.GetTexture(map)!=null,"UV texture "+map+": "+d.id);}
                Check(art.albedo.depth==31&&art.metallic.depth==31&&art.normal.depth==31&&art.impostors.depth==248,"Complete GPU texture arrays and directional impostors");
                Check(art.Find("wall").walls.Length==16&&art.Find("gate").gateLeaf!=null,"Wall connections and separate moving gate shell");
                for(int i=0;i<31;i++){int type=i;Vector2 p=app.Base.BasePosition(0)+new Vector2((i%8-3)*9,35+(i/8)*10);
                    Check(art.unitIds[i]==app.Base.Catalog.units[i].id,"GPU catalogue order: "+art.unitIds[i]);
                    Check(app.Battle.RequestSpawn(type,0,p,p,ok=>{if(ok)spawned++;else{errors++;failure="GPU spawn rejected type "+type;}}),"GPU spawn submitted: "+art.unitIds[i]);}
                app.View.GetComponent<RtsCamera>().FocusBase(app.Base.BasePosition(0));
            }catch(Exception e){error=e.ToString();}
            deadline=Time.realtimeSinceStartupAsDouble+10;
            while(error.Length==0&&spawned<31&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            yield return new WaitForSecondsRealtime(2);
            if(error.Length==0){if(spawned!=31)error="Expected 31 GPU spawn completions, received "+spawned;else checks.Add("All 31 catalogue types spawned through GPU API");}
            // A hidden validation window has no dependable presented backbuffer. The
            // existing HDRP render-request path captures the real camera and GPU draws.
            app.CaptureGridPlacementPreview(Path.GetFullPath(Path.Combine(folder,"all-unit-types.png")));yield return new WaitForSecondsRealtime(.5f);
            if(error.Length==0&&errors>0)error="Runtime errors: "+errors+" "+failure;
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,buildings=art?.buildings.Length??0,units=art?.unitIds.Length??0,spawned=spawned,checks=checks.ToArray()},true));Application.Quit(error.Length==0?0:1);
        }
    }
}
