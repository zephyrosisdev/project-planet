using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools {
    public static class PlanetAirBuild {
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){const string request="TestResults/Planetary/Atmosphere/request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying||!File.Exists(request))return;File.Delete(request);try{Build();}catch(Exception e){Debug.LogException(e);File.WriteAllText("TestResults/Planetary/Atmosphere/build-error.txt",e.ToString());}}
        public static void Build(){
            const string baseline="Assets/MassiveArmy/Resources/MassiveArmy/PlanetWeatherLegacyValidation.shader",baselineDensity="Assets/MassiveArmy/Resources/MassiveArmy/PlanetWeatherLegacyDensity.hlsl",scenePath="Assets/MassiveArmy/Editor/AtmosphereValidation.unity";
            const BuildTarget target=BuildTarget.StandaloneWindows64;bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);
            try{
                File.WriteAllText(baselineDensity,File.ReadAllText("Tools/Fixtures/PlanetWeatherPreRayleighDensity.hlsl.txt"));AssetDatabase.ImportAsset(baselineDensity,ImportAssetOptions.ForceSynchronousImport);
                File.WriteAllText(baseline,File.ReadAllText("Tools/Fixtures/PlanetWeatherPreRayleigh.shader.txt").Replace("Shader \"MassiveArmy/Planet weather\"","Shader \"MassiveArmy/Planet weather baseline validation\"").Replace("\"PlanetWeatherDensity.hlsl\"","\"PlanetWeatherLegacyDensity.hlsl\""));AssetDatabase.ImportAsset(baseline,ImportAssetOptions.ForceSynchronousImport);
                var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);EditorSceneManager.SaveScene(scene,scenePath);EditorSceneManager.CloseScene(scene,true);UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{GraphicsDeviceType.Direct3D11});
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},locationPathName="TestResults/Planetary/Atmosphere/Build/Atmosphere.exe",target=target,options=BuildOptions.None});
                File.WriteAllText("TestResults/Planetary/Atmosphere/build.txt",report.summary.result+"; "+report.summary.totalErrors+" errors");if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Atmosphere validation build failed");
            }finally{PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);AssetDatabase.DeleteAsset(baseline);AssetDatabase.DeleteAsset(baselineDensity);AssetDatabase.DeleteAsset(scenePath);}
        }
    }
}
