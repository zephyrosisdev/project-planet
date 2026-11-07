using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEditor.Build;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class SettlementMemoryBuild {
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){const string request="TestResults/MemoryBounds/build.request";if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(request))return;File.Delete(request);try{Build();}catch(Exception e){File.WriteAllText("TestResults/MemoryBounds/build.txt",e.ToString());}}
        [MenuItem("Synthetic Front/Diagnostics/Capture settlement memory")]
        public static void Capture(){var world=UnityEngine.Object.FindAnyObjectByType<MassiveArmy.Planetary.PlanetDevelopment>();if(world?.Base?.Civilisation==null)throw new InvalidOperationException("Start a settlement before capturing memory");world.Base.Civilisation.WriteMemoryUsage("TestResults/MemoryBounds/live-memory.json");}
        internal static bool Active;
        static void Build(){
            const string scene="Assets/Scenes/MemoryNavigationValidation.unity";var temporary=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);EditorSceneManager.SaveScene(temporary,scene);EditorSceneManager.CloseScene(temporary,true);
            var target=BuildTarget.StandaloneWindows64;bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);Active=true;
            try {PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName="TestResults/MemoryBounds/Build/MemoryNavigation.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            File.WriteAllText("TestResults/MemoryBounds/build.txt",report.summary.result+"; errors="+report.summary.totalErrors);
            }finally{Active=false;PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);}
        }
    }
    // Only this camera-free diagnostic player strips unrelated game shaders.
    // Ordinary game builds never enter this branch.
    public sealed class SettlementMemoryShaderFilter:IPreprocessShaders,IPreprocessComputeShaders {
        public int callbackOrder=>int.MaxValue;
        public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> data){if(SettlementMemoryBuild.Active&&AssetDatabase.GetAssetPath(shader).StartsWith("Assets/MassiveArmy/",StringComparison.Ordinal)&&shader.name!="MassiveArmy/Settlement fields"&&shader.name!="MassiveArmy/Settlement citizens"&&shader.name!="MassiveArmy/Settlement land use")data.Clear();}
        public void OnProcessComputeShader(ComputeShader shader,string kernel,IList<ShaderCompilerData> data){if(SettlementMemoryBuild.Active&&AssetDatabase.GetAssetPath(shader).StartsWith("Assets/MassiveArmy/",StringComparison.Ordinal)&&shader.name!="SettlementRoad"&&shader.name!="MemoryBaselineRoad"&&shader.name!="SettlementCitizens")data.Clear();}
    }
}

