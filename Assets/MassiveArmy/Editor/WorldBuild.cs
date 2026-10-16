using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools
{
    public static class WorldBuild
    {
        public static void RunValidated()
        {
            WrappingWorldValidation.Run();
            if(!File.ReadAllText("TestResults/wrapping-world-validation.json").Contains("\"PASS\""))
                throw new System.InvalidOperationException("Wrapping world validation failed; see its JSON report.");
            Run();
        }
        public static void Run()
        {
            string folder=File.Exists("TestResults/build-label.txt")?File.ReadAllText("TestResults/build-label.txt").Trim():"current";
            if(folder!="baseline"&&folder!="current")throw new System.ArgumentException("Unknown build label");
            string path="TestResults/Builds/"+folder+"/SyntheticFront.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            const BuildTarget target=BuildTarget.StandaloneWindows64;
            bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);
            try{
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{GraphicsDeviceType.Direct3D11});
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/MassiveBattle.unity"},locationPathName=path,target=target,options=File.Exists("TestResults/build-profile.txt")?BuildOptions.Development:BuildOptions.None});
                File.WriteAllText("TestResults/build-result.txt",report.summary.result+" "+report.summary.totalErrors+" errors "+path);
                Debug.Log("World benchmark build: "+report.summary.result);
                if(report.summary.totalErrors>0)throw new System.InvalidOperationException("Validation build failed.");
            }finally{PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);}
        }
    }
}
