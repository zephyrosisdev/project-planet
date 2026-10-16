using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MassiveArmy.EditorTools
{
    public static class ResourceNodeValidation
    {
        public const string Folder="TestResults/ResourceDecals";
        public static void BuildBaseline()=>Build("baseline");
        public static void BuildCurrent()=>Build("current");
        public static void BuildCombatBaseline()=>Build("baseline","TestResults/CombatPresentation");
        public static void BuildCombatCurrent()=>Build("current","TestResults/CombatPresentation");
        public static void BuildCombatRefinement()=>Build("current","TestResults/CombatRefinement");
        [Serializable] sealed class Report { public string result;public int checks;public string[] failures; }
        [MenuItem("Massive Army/Validate Resource Node Grid")]
        public static void Run()
        {
            var catalog=RtsCatalog.Load();var failures=new List<string>();int checks=0;
            void Check(bool valid,string name){checks++;if(!valid)failures.Add(name);}
            foreach(int size in new[]{2048,4096,16384})foreach(int seed in new[]{73129,14351,99881}){
                var cfg=new WorldGenerationSettings{size=size,seed=seed};var world=WorldSetupResult.Generate(cfg,catalog:catalog);
                foreach(var node in world.Resources){
                    foreach(var extractor in catalog.buildings.Where(d=>d.terrain==TerrainRule.Node&&d.nodes.Contains(node.kind)))for(int rotation=0;rotation<4;rotation++)
                        Check(WorldTopology.Distance(node.position,BuildingGrid.Snap(extractor,node.position,size,rotation),size)<.001f,size+"/"+seed+": extractor centred over "+node.kind);
                    Check(world.Deployment.Safe(node.position)&&world.Map.Water[world.Map.Index(node.position)]==0,size+"/"+seed+": snapped node remains accessible on dry land");
                    Check(world.Resources.All(other=>other.id==node.id||WorldTopology.Distance(node.position,other.position,size)>=38),size+"/"+seed+": snapped resources retain spacing");
                }
                var repeat=WorldSetupResult.Generate(cfg,catalog:catalog);
                Check(world.Resources.Select(n=>n.position).SequenceEqual(repeat.Resources.Select(n=>n.position)),size+"/"+seed+": deterministic snapped resource generation");
            }
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind)))foreach(int size in new[]{2048,16384}){
                Vector2 p=BuildingGrid.SnapResource(kind,new Vector2(size*.5f+2,-size*.5f-3),size,catalog);
                Check(p.x>=-size*.5f&&p.x<size*.5f&&p.y>=-size*.5f&&p.y<size*.5f,"Wrapped resource centre remains canonical: "+kind);
                Check(BuildingGrid.ResourceDimensions(kind)==BuildingGrid.ResourceDimensions(kind,catalog),"Fallback footprint matches catalogue: "+kind);
            }
            Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/grid-validation.json",JsonUtility.ToJson(new Report{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
            if(failures.Count>0)throw new InvalidOperationException("Resource grid validation failed");
        }
        const string RuntimeRequest="SyntheticFront.ValidateResources";
        [InitializeOnLoadMethod] static void Install(){EditorApplication.playModeStateChanged-=Played;EditorApplication.playModeStateChanged+=Played;}
        [MenuItem("Massive Army/Validate Resource Decal Rendering")]
        public static void StartRuntime(){SessionState.SetBool(RuntimeRequest,true);EditorApplication.isPlaying=true;}
        static void Played(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(RuntimeRequest,false))return;SessionState.SetBool(RuntimeRequest,false);
            var gameView=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(gameView);window.Show();window.Focus();
            UnityEngine.Object.FindAnyObjectByType<BattleApp>().StartCoroutine(ValidateRuntime());
        }
        static IEnumerator ValidateRuntime()
        {
            var app=UnityEngine.Object.FindAnyObjectByType<BattleApp>();yield return null;app.StartFlatValidationBattle();
            while(app.State=="loading")yield return null;
            if(app.Base==null)throw new InvalidOperationException("Resource validation could not load the battle");
            app.Paused=false;yield return new WaitForSecondsRealtime(2);app.Paused=true;
            yield return app.ValidateResourceDecals(Folder);EditorApplication.isPlaying=false;
        }
        public static void Build(string label,string folder=Folder)
        {
            const BuildTarget target=BuildTarget.StandaloneWindows64;
            bool defaults=PlayerSettings.GetUseDefaultGraphicsAPIs(target);var apis=PlayerSettings.GetGraphicsAPIs(target);
            string path=folder+"/Builds/"+label+"/SyntheticFront.exe";Directory.CreateDirectory(Path.GetDirectoryName(path));
            try{
                PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);PlayerSettings.SetGraphicsAPIs(target,new[]{GraphicsDeviceType.Direct3D11});
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/MassiveBattle.unity"},locationPathName=path,target=target,options=BuildOptions.None});
                File.WriteAllText(folder+"/build-"+label+".txt",report.summary.result+"; "+report.summary.totalErrors+" errors");
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Resource decal comparison build failed");
            }finally{PlayerSettings.SetGraphicsAPIs(target,apis);PlayerSettings.SetUseDefaultGraphicsAPIs(target,defaults);}
        }
    }
}
