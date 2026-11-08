using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassiveArmy.EditorTools {
    public static class SurvivalBuild {
        internal static bool DedicatedBuildActive {get;private set;}
        internal static bool GraphicsProbeBuildActive {get;private set;}
        [MenuItem("Massive Army/Build/Windows Client")]
        public static void Client()=>Build(false);
        [MenuItem("Massive Army/Build/Windows Dedicated Server")]
        public static void Server()=>Build(true);
        public static void GraphicsProbeClient()=>Build(false,true);
        static void Build(bool server,bool graphicsProbe=false){
            const BuildTarget target=BuildTarget.StandaloneWindows64;
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,target))throw new InvalidOperationException("Install Windows Build Support for this Unity editor");
            string variations=Path.Combine(EditorApplication.applicationContentsPath,"PlaybackEngines/WindowsStandaloneSupport/Variations");
            if(server&&(!Directory.Exists(variations)||Directory.GetDirectories(variations,"win64_server*mono").Length==0))throw new InvalidOperationException("Install Windows Dedicated Server Build Support for Unity "+Application.unityVersion);
            string output=graphicsProbe?"TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe":server?"Builds/WindowsServer/SyntheticFrontServer.exe":"Builds/WindowsClient/SyntheticFront.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // An empty server scene prevents any offline/render bootstrap from running.
            const string serverScene="Assets/MassiveArmy/Generated/SurvivalServer.unity";
            if(server){
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
                Directory.CreateDirectory("Assets/MassiveArmy/Generated");
                var previous=SceneManager.GetActiveScene();
                if(!Application.isBatchMode&&string.IsNullOrEmpty(previous.path))throw new InvalidOperationException("Save the untitled scene before building a dedicated server.");
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene,serverScene);
                if(!Application.isBatchMode){EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);}AssetDatabase.Refresh();
            }
            BuildReport report;DedicatedBuildActive=server;GraphicsProbeBuildActive=graphicsProbe;
            try{report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=new[]{server?serverScene:"Assets/Scenes/MassiveBattle.unity"},locationPathName=output,target=target,
                subtarget=(int)(server?StandaloneBuildSubtarget.Server:StandaloneBuildSubtarget.Player),options=BuildOptions.None,
                extraScriptingDefines=graphicsProbe?new[]{"SURVIVAL_GRAPHICS_PROBE"}:Array.Empty<string>()
            });}finally{DedicatedBuildActive=false;GraphicsProbeBuildActive=false;}
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Build failed: "+report.summary.result+"; errors="+report.summary.totalErrors);
            string probeMarker=Path.Combine(Path.GetDirectoryName(output),"session-probe.version");
            if(server)File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"load-probe.version"),"3");
            if(server||graphicsProbe)File.WriteAllText(probeMarker,"11");else if(File.Exists(probeMarker))File.Delete(probeMarker);
            if(graphicsProbe)File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"graphics-probe-only"),"Survival diagnostic fixture only. Offline army/RTS compute kernels omitted. Not a release, RTS, capacity or performance acceptance build. See SurvivalServerShaderFilter.ProbeArmyShaders for the exact exclusion list.");
            if(server){string config=Path.Combine(Path.GetDirectoryName(output),"server.json");if(!File.Exists(config))File.Copy("Server/server.example.json",config);}
            Debug.Log("Built "+Path.GetFullPath(output));
        }
    }
    // Normal client/RTS/army builds retain all variants. The separately marked
    // survival fixture omits unreachable offline army/RTS compute shaders.
    public sealed class SurvivalServerShaderFilter:IPreprocessShaders,IPreprocessComputeShaders {
        internal static readonly HashSet<string> ProbeArmyShaders=new HashSet<string>{"Battle","Rts","Navigation","SwarmNavigation","PlanetDevelopment","PlanetEncounter","PlanetCommands","PlanetGoal","PlanetRegionalCosts","PlanetVehicles","ZombieNoise","AiAreaQuery","CombatPresentation","Crowd","BenchmarkSetup"};
        public int callbackOrder=>int.MaxValue;
        public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> data){if(SurvivalBuild.DedicatedBuildActive)data.Clear();}
        public void OnProcessComputeShader(ComputeShader shader,string kernel,IList<ShaderCompilerData> data){
            if(SurvivalBuild.DedicatedBuildActive){data.Clear();return;}
            if(SurvivalBuild.GraphicsProbeBuildActive){string path=AssetDatabase.GetAssetPath(shader);if(path.StartsWith("Assets/MassiveArmy/Resources/MassiveArmy/",StringComparison.Ordinal)&&ProbeArmyShaders.Contains(Path.GetFileNameWithoutExtension(path)))data.Clear();}
        }
    }
}
