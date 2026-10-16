using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MassiveArmy.EditorTools
{
    public static class BattleProject
    {
        [MenuItem("Massive Army/Prepare HDRP and Battle Scene")]
        public static void Prepare()
        {
            const string assetPath="Assets/MassiveArmy/Resources/MassiveArmy/BattleHDRP.asset";
            var asset=AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(assetPath);
            if(asset==null) { asset=ScriptableObject.CreateInstance<HDRenderPipelineAsset>();AssetDatabase.CreateAsset(asset,assetPath); }
            var options=asset.currentPlatformRenderPipelineSettings;
            options.supportWater=true;options.supportVolumetrics=false;options.supportRayTracing=false;
            options.supportSSR=false;options.supportSSAO=false;options.supportMotionVectors=false;
            options.waterSimulationResolution=WaterSimulationResolution.Low64;
            asset.currentPlatformRenderPipelineSettings=options;
            GraphicsSettings.defaultRenderPipeline=asset;
            int previousQuality=QualitySettings.GetQualityLevel();
            for(int i=0;i<QualitySettings.names.Length;i++) { QualitySettings.SetQualityLevel(i,false);QualitySettings.renderPipeline=asset; }
            QualitySettings.SetQualityLevel(previousQuality,false);
            PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.enableFrameTimingStats=true;
            PlayerSettings.runInBackground=true;
            var qualityType=typeof(PlayerSettings).Assembly.GetType("UnityEditor.LightmapEncodingQuality");
            var lightmap=typeof(PlayerSettings).GetMethod("SetLightmapEncodingQualityForPlatform",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            foreach(var target in new[]{BuildTarget.StandaloneWindows,BuildTarget.StandaloneWindows64,BuildTarget.StandaloneLinux64,BuildTarget.StandaloneOSX})
                lightmap.Invoke(null,new[]{(object)target,Enum.ToObject(qualityType,2)});
            // Unity 6.6 makes this package setup helper internal. Invoke the installed HDRP helper
            // rather than duplicating its versioned resource provisioning and migration logic.
            var globalType=typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings",true);
            var ensure=globalType.GetMethod("Ensure",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            var global=(UnityEngine.Object)ensure.Invoke(null,new object[]{true});
            EditorUtility.SetDirty(asset);EditorUtility.SetDirty(global);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Synthetic Front").AddComponent<BattleApp>();
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/MassiveBattle.unity");
            BattleLightingSetup.Run();
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/MassiveBattle.unity",true),new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity",false)};
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/setup.txt","HDRP configured; MassiveBattle is the startup scene.\nUnity "+Application.unityVersion);
            Debug.Log("Massive Army: HDRP and playable scene prepared.");
        }
        [MenuItem("Massive Army/Run GPU Validation")]
        public static void Validate() => GpuValidation.Start();

        // A bounded local editor control surface used by validation tooling; no arbitrary code execution.
        [InitializeOnLoadMethod]
        static void InstallRequestPump()
        {
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
            EditorApplication.delayCall+=CleanOrphanedValidationFixtures;
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/editor-ready.txt",DateTime.UtcNow.ToString("O"));
        }
        static double nextPoll;
        static void CleanOrphanedValidationFixtures()
        {
            if(EditorApplication.isPlaying)return;
            foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if(t!=null&&t.parent==null&&(t.name=="RTS validation fixture"||t.name=="RTS combined arms GPU fixture"))UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
        static void Deploy(int count)
        {
            var app=UnityEngine.Object.FindFirstObjectByType<BattleApp>();
            if(app!=null) { app.settings.capacity=count;app.StartBattle(count); }
        }
        [Serializable] sealed class RuntimeStatus
        {
            public bool playing;
            public string state,loading,benchmarkReport;
            public int units,capacity,transforms;
            public uint tick,blue,red,selected,visible,near,medium,far,shells;
            public float fps;
            public float worldSize;
            public double cpuMs,gpuMs;
            public bool benchmarking;
        }
        static void WriteStatus()
        {
            var app=UnityEngine.Object.FindFirstObjectByType<BattleApp>();
            var data=new RuntimeStatus{playing=EditorApplication.isPlaying,state=app!=null?app.State:"edit"};
            if(app!=null)
            {
                data.loading=app.Loading;data.fps=app.Telemetry.Fps;data.cpuMs=app.Telemetry.CpuMs;data.gpuMs=app.Telemetry.GpuMs;
                data.benchmarkReport=app.Telemetry.LastReport;data.benchmarking=app.Telemetry.Benchmarking;
                if(app.Battle!=null)
                {
                    data.units=app.Battle.Settings.activeUnits;data.capacity=app.Battle.Settings.capacity;data.tick=app.Battle.Tick;
                    data.worldSize=app.Battle.Settings.worldSize;
                    var s=app.Battle.Statistics;
                    if(s!=null) { data.blue=s[0];data.red=s[1];data.selected=s[2];data.visible=s[8]+s[9]+s[10];data.near=s[8];data.medium=s[9];data.far=s[10];data.shells=s[4]; }
                }
            }
            data.transforms=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            File.WriteAllText("TestResults/runtime-status.json",JsonUtility.ToJson(data,true));
        }
        static void Pump()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup<nextPoll)return;
            nextPoll=EditorApplication.timeSinceStartup+.5;
            try { WriteStatus(); }
            catch(IOException) { } // A diagnostic reader can briefly hold the status file; retry next poll.
            const string request="TestResults/editor-request.txt";
            if(!File.Exists(request))return;
            string action;
            try { action=File.ReadAllText(request).Trim();File.Delete(request); }
            catch(IOException) { return; } // A local request writer may still hold the file for this poll.
            try
            {
                switch(action)
                {
                    case "refresh": AssetDatabase.Refresh();break;
                    case "ui-assets": RtsUiBuilder.Build();break;
                    case "ui-validate": RtsUiValidation.Run();break;
                    case "ui-runtime-validate": RtsUiValidation.StartRuntime();break;
                    case "ui-interaction-validate": RtsUiValidation.StartInteractionFixes();break;
                    case "node-build-baseline": ResourceNodeValidation.BuildBaseline();break;
                    case "node-build-current": ResourceNodeValidation.BuildCurrent();break;
                    case "combat-build-baseline": ResourceNodeValidation.BuildCombatBaseline();break;
                    case "combat-build-current": ResourceNodeValidation.BuildCombatCurrent();break;
                    case "combat-refine-build-current": ResourceNodeValidation.BuildCombatRefinement();break;
                    case "combat-surface-build-current": ResourceNodeValidation.Build("current","TestResults/CombatSurface");break;
                    case "nuclear-build-current": ResourceNodeValidation.Build("current","TestResults/NuclearPresentation");break;
                    case "nuclear-build-baseline": ResourceNodeValidation.Build("baseline","TestResults/NuclearPresentation");break;
                    case "nuclear-art-build": CombatArtBuilder.BuildNuclearOnly();break;
                    case "combat-surface-art": CombatSurfaceBuilder.Build();break;
                    case "combat-surface-validate": CombatSurfaceValidation.Run();break;
                    case "army-lighting-validate": ArmyLightingValidation.Run();break;
                    case "combat-art-build": CombatArtBuilder.Build();break;
                    case "combat-shaders": CombatArtBuilder.ExportShaders();break;
                    case "combat-shader-validate": CombatArtBuilder.ValidateShaders();break;
                    case "combat-validate": CombatValidation.Start();break;
                    case "node-grid-validate": ResourceNodeValidation.Run();break;
                    case "node-runtime-validate": ResourceNodeValidation.StartRuntime();break;
                    case "ui-editor-gallery": RtsUiValidation.StartGallery();break;
                    case "gpu-wrapping-validate":GpuWrappingValidation.Start();break;
                    case "wrapping-world-validate": WrappingWorldValidation.Run();break;
                    case "build-windows": WorldBuild.Run();break;
                    case "prepare": Prepare();break;
                    case "validate": Validate();break;
                    case "rts-validate": RtsValidation.Start();break;
                    case "grid-validate": RtsGridValidation.Run();break;
                    case "grid-movement-validate": RtsGridMovementValidation.Start();break;
                    case "rts-gpu-validate": RtsGpuValidation.Start();break;
                    case "swarm-validate": SwarmValidation.Start();break;
                    case "zombie-validate": ZombieValidation.Start();break;
                    case "zombie-hostility-validate": ZombieValidation.StartHostility();break;
                    case "zombie-build-baseline": ResourceNodeValidation.Build("baseline","TestResults/ZombiePursuit");break;
                    case "zombie-build-current": ResourceNodeValidation.Build("current","TestResults/ZombiePursuit");break;
                    case "swarm-deploy": UnityEngine.Object.FindAnyObjectByType<BattleApp>()?.StartSwarmBattle();break;
                    case "base-deploy": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.StartBaseBattle();break;
                    case "world-validate": WorldValidation.Run();break;
                    case "terrain-art-build": TerrainArtBuilder.BuildAndValidate();break;
                    case "terrain-art-validate": TerrainArtValidation.Run();break;
                    case "terrain-live-validate": TerrainLookDev.ValidateLiveEditing();break;
                    case "terrain-lighting-validate": TerrainLightingValidation.Run();break;
                    case "battle-visual-validate": BattleVisualValidation.Run();break;
                    case "fog-on": Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").fogOfWar=true;break;
                    case "fog-off": Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").fogOfWar=false;break;
                    case "fog-overview": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.View.GetComponent<RtsCamera>().ResetOverview();break;
                    case "visual-pause": var visualApp=UnityEngine.Object.FindFirstObjectByType<BattleApp>();if(visualApp!=null)visualApp.Paused=true;break;
                    case "terrain-preview": TerrainLookDev.EnableConfiguredPreview();break;
                    case "terrain-preview-off": TerrainLookDev.DisablePreview();break;
                    case "deployment-validate": DeploymentValidation.Run();break;
                    case "spawn-validate": SpawnGpuValidation.Start();break;
                    case "crowd-validate": CrowdValidation.Start();break;
                    case "biome-map": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.ShowBiomeMap();break;
                    case "play": EditorApplication.isPlaying=true;break;
                    case "stop": EditorApplication.isPlaying=false;break;
                    case "deploy": Deploy(50000);break;
                    case "deploy100k": Deploy(100000);break;
                    case "deploy250k": Deploy(250000);break;
                    case "deploy500k": Deploy(500000);break;
                    case "deploy1m": Deploy(1000000);break;
                    case "benchmark": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.BeginBenchmark();break;
                    case "gpu-profile": UnityEngine.Profiling.Profiler.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU,true);UnityEngine.Profiling.Profiler.enabled=true;break;
                    case "ui-diagnostics":
                        var app=UnityEngine.Object.FindFirstObjectByType<BattleApp>();
                        File.WriteAllText("TestResults/ui-diagnostics.txt",app.ToolkitDiagnostics);
                        break;
                    case "formation-preview": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.PreviewFormationGestureForValidation();break;
                    case "formation-commit": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.CommitFormationGestureForValidation();break;
                    case "building-attack-gesture": UnityEngine.Object.FindFirstObjectByType<BattleApp>()?.BuildingAttackGestureForValidation();break;
                    case "capture": ScreenCapture.CaptureScreenshot(Path.GetFullPath("TestResults/battle.png"));break;
                    default: throw new InvalidOperationException("Unknown editor action: "+action);
                }
                try { File.WriteAllText("TestResults/editor-action.txt",action+" accepted "+DateTime.UtcNow.ToString("O")); }
                catch(IOException) { } // The action succeeded even if a diagnostic reader holds its acknowledgement.
            }
            catch(Exception e) { File.WriteAllText("TestResults/editor-error.txt",e.ToString());Debug.LogException(e); }
        }
    }
}
