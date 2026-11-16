#if UNITY_EDITOR || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal sealed class SurvivalOfflinePresetProbe:MonoBehaviour {
        [Serializable] sealed class Report {public string status="STARTING",error="",savePath,presetId,fingerprint;public double gravity;public float radius,atmosphereHeight,cloudAltitude,scale;public int bodies;public bool journalled,returnedToMenu;}
        string path;double deadline,readyAt;int closedFrames;BattleApp app;PlanetDevelopment world;Report report=new Report();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-survival-offline-preset-probe");if(at<0||at+1>=args.Length)return;var go=new GameObject("Offline shared preset probe");DontDestroyOnLoad(go);var probe=go.AddComponent<SurvivalOfflinePresetProbe>();probe.path=Path.GetFullPath(args[at+1]);probe.deadline=Time.realtimeSinceStartupAsDouble+120;}
        void Update(){try{
            if(Time.realtimeSinceStartupAsDouble>deadline)throw new InvalidOperationException("Offline preset startup timed out");
            if(report.returnedToMenu){if(++closedFrames<3)return;var saved=IndustrialAuthority.Load(report.savePath);report.journalled=saved.universe!=null&&SurvivalWorldPresetData.Parse(saved.universe.presetJson).Fingerprint()==report.fingerprint;if(!report.journalled)throw new InvalidOperationException("Offline universe preset was not saved");Finish(true,null);return;}
            if(app==null){app=UnityEngine.Object.FindFirstObjectByType<BattleApp>();if(app==null)return;app.StartSurvivalGame();return;}
            if(world==null){foreach(var candidate in UnityEngine.Object.FindObjectsByType<PlanetDevelopment>())if(candidate.worldPreset!=null){world=candidate;break;}if(world==null)return;}
            if(!world.Ready||world.IndustrialAuthority?.World.universe==null)return;
            if(readyAt==0){readyAt=Time.realtimeSinceStartupAsDouble;return;}if(Time.realtimeSinceStartupAsDouble-readyAt<2)return;
            var data=SurvivalWorldPresetData.Parse(world.IndustrialAuthority.World.universe.presetJson);report.presetId=data.presetId;report.fingerprint=data.Fingerprint();report.radius=world.definition.radius;report.gravity=world.definition.gravitationalParameter/(world.definition.radius*(double)world.definition.radius);report.atmosphereHeight=world.definition.atmosphereHeight;report.cloudAltitude=world.definition.weather.cloudAltitude;report.scale=world.systemSizeMultiplier;report.bodies=world.SystemRuntime.Bodies.Length;report.savePath=world.OfflineIndustryPath;world.IndustrialAuthority.Save();
            world.ReturnToMenu();report.returnedToMenu=app.State=="menu";if(!report.returnedToMenu)throw new InvalidOperationException("Offline session did not return to the menu");
        }catch(Exception e){Finish(false,e.ToString());}}
        void Finish(bool success,string error){enabled=false;report.status=success?"PASS":"FAIL";report.error=error;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));Application.Quit(success?0:1);}
    }
}
#endif
