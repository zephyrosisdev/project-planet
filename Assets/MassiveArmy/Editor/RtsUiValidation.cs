using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using MouseButton=UnityEngine.InputSystem.LowLevel.MouseButton;

namespace MassiveArmy.EditorTools
{
    public static class RtsUiValidation
    {
        const string RuntimeRequest="SyntheticFront.ValidateRuntimeUi",GalleryRequest="SyntheticFront.ValidateEditorGallery";
        [InitializeOnLoadMethod] static void Install(){EditorApplication.playModeStateChanged-=Played;EditorApplication.playModeStateChanged+=Played;}
        [MenuItem("Massive Army/UI/Validate Presented Editor UI")]
        public static void StartRuntime(){SessionState.SetBool(RuntimeRequest,true);if(EditorApplication.isPlaying)Played(PlayModeStateChange.EnteredPlayMode);else EditorApplication.isPlaying=true;}
        [MenuItem("Massive Army/UI/Validate Camera and Menu Scroll")]
        public static void StartInteractionFixes(){SessionState.SetBool("SyntheticFront.ValidateCameraMenu",true);UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MassiveBattle.unity");StartRuntime();}
        [MenuItem("Massive Army/UI/Validate All Editor Screens")]
        public static void StartGallery(){Directory.CreateDirectory("TestResults/UiRedesign/editor-gallery");File.Delete("TestResults/UiRedesign/editor-gallery/gallery.txt");File.Delete("TestResults/UiRedesign/editor-gallery/error.txt");SessionState.SetBool(GalleryRequest,true);StartRuntime();}
        static void Played(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(RuntimeRequest,false))return;SessionState.SetBool(RuntimeRequest,false);
            var gameView=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(gameView);window.Show();window.Focus();
            var app=UnityEngine.Object.FindAnyObjectByType<BattleApp>();
            if(SessionState.GetBool(GalleryRequest,false)){SessionState.SetBool(GalleryRequest,false);var harness=new GameObject("Editor UI screen validation").AddComponent<UiGalleryHarness>();harness.outputOverride="TestResults/UiRedesign/editor-gallery";app.StartCoroutine(WaitForEditorGallery());}
            else if(SessionState.GetBool("SyntheticFront.ValidateCameraMenu",false)){SessionState.SetBool("SyntheticFront.ValidateCameraMenu",false);app.StartCoroutine(ValidateInteractionFixes());}
            else app.StartCoroutine(ValidateRuntime());
        }
        static IEnumerator ValidateInteractionFixes()
        {
            var app=UnityEngine.Object.FindAnyObjectByType<BattleApp>();yield return new WaitForSecondsRealtime(1);app.StartFlatValidationBattle();
            while(app.State=="loading")yield return null;
            const string folder="TestResults/UiInteractionFixes";
            yield return app.ValidateCameraAndMenuScroll(folder);yield return app.ValidateUiTextEntry(folder);yield return app.ValidateQueuePointerRebind(folder);
            bool pass=File.ReadAllText(folder+"/camera-menu-scroll.json").Contains("\"PASS\"")&&File.ReadAllText(folder+"/text-focus.json").Contains("\"PASS\"")&&File.ReadAllText(folder+"/queue-pointer.json").Contains("\"PASS\"");
            Debug.Log("Camera and menu scroll validation: "+(pass?"PASS":"FAIL"));
            EditorApplication.isPlaying=false;if(Application.isBatchMode)EditorApplication.Exit(pass?0:1);
        }
        static IEnumerator ValidateRuntime()
        {
            var app=UnityEngine.Object.FindAnyObjectByType<BattleApp>();yield return new WaitForSecondsRealtime(1);app.StartFlatValidationBattle();
            while(app.State=="loading")yield return null;app.Paused=false;yield return new WaitForSecondsRealtime(2);app.Paused=true;app.ShowUiValidationScreen("battle");yield return new WaitForSecondsRealtime(1);
            const string folder="TestResults/UiRedesign/editor";Directory.CreateDirectory(folder);app.ValidateUiInteractions(folder);yield return app.ValidateUiTextEntry(folder);yield return app.ValidateUiFormationCommands(folder);yield return app.ValidateUiGestures(folder);yield return app.ValidateQueuePointerRebind(folder);
            File.WriteAllText(folder+"/diagnostics.txt",app.ToolkitDiagnostics);ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder+"/battle.png"));yield return new WaitForSecondsRealtime(1);EditorApplication.isPlaying=false;
        }
        static IEnumerator WaitForEditorGallery()
        {
            const string folder="TestResults/UiRedesign/editor-gallery";double deadline=Time.realtimeSinceStartupAsDouble+240;
            while(!File.Exists(folder+"/gallery.txt")&&!File.Exists(folder+"/error.txt")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            var failures=new List<string>();
            if(!File.Exists(folder+"/gallery.txt"))failures.Add("Screen walkthrough did not complete");
            foreach(string file in new[]{"interactions.json","gestures.json","text-focus.json","formation.json","queue-pointer.json","geometry-battle.json"})if(!File.Exists(folder+"/"+file)||!File.ReadAllText(folder+"/"+file).Contains("\"PASS\""))failures.Add(file);
            int screens=Directory.GetFiles(folder,"*.png").Length;if(screens!=24)failures.Add("Expected all 24 screen captures");
            File.WriteAllText(folder+"/screen-walkthrough.json",JsonUtility.ToJson(new Report{result=failures.Count==0?"PASS":"FAIL",checks=screens,failures=failures.ToArray()},true));
            EditorApplication.isPlaying=false;Debug.Log("Editor screen walkthrough: "+(failures.Count==0?"PASS":"FAIL"));
        }
        public static void BuildPlayer(){RtsUiBuilder.Build();Run();WorldBuild.Run();}
        [Serializable] sealed class Report{public string result;public int checks;public string[] failures;}
        [MenuItem("Massive Army/UI/Validate UI and Input")]
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Run UI input validation in Edit mode.");
            var failures=new List<string>();int checks=0;
            Action<bool,string> check=(valid,name)=>{checks++;if(!valid)failures.Add(name);};
            var events=new List<RtsPointerInput>();int cancellations=0;var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();bool originalMerging=InputSystem.settings.disableRedundantEventsMerging;
            try
            {
                using(var router=new RtsUiInput(events.Add,()=>cancellations++))
                {
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(500,500)}.WithButton(MouseButton.Right));
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(600,550)}.WithButton(MouseButton.Right));
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(620,570)});
                    InputSystem.Update();router.Drain(true);
                    check(events.Select(e=>e.type).SequenceEqual(new[]{PointerPhase.Down,PointerPhase.Move,PointerPhase.Move,PointerPhase.Up}),"Same-frame formation events retain order");
                    check(events.Count==4&&events[0].button==1&&events[0].mousePosition.x==500&&events[3].mousePosition.x==620,"Event positions and buttons retained");
                    events.Clear();InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(600,600)}.WithButton(MouseButton.Left));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(600,600)});InputSystem.Update();router.Drain(true);
                    check(events.Count==2&&events[0].type==PointerPhase.Down&&events[1].type==PointerPhase.Up,"Same-frame click not lost");
                    events.Clear();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftShift,Key.LeftCtrl));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(640,640)}.WithButton(MouseButton.Left));InputSystem.Update();router.Drain(true);check(events.Count>0&&events[0].shift&&events[0].control,"Pointer record retains modifier state");
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(640,640)});InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();router.Drain(true);
                    events.Clear();InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(650,650)}.WithButton(MouseButton.Left));InputSystem.Update();router.Drain(false);check(events.Count==0,"Blocked input is discarded");
                    InputSystem.ResetDevice(mouse);router.Drain(true);check(events.Count==0,"Mouse reset discards stale transitions");check(cancellations>0,"Mouse reset cancels the owning gameplay gesture");
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(660,650)}.WithButton(MouseButton.Left));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(660,650)});InputSystem.Update();router.Drain(true);
                    check(events.Count==2&&events[0].type==PointerPhase.Down&&events[1].type==PointerPhase.Up,"First click after mouse reset retains press and release");
                }
            }
            finally{InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);}
            check(InputSystem.settings.disableRedundantEventsMerging==originalMerging,"Event merging preference restored after disposal");
            var art=Resources.Load<RtsUiArt>("MassiveArmy/UI/FrontArt");var catalog=RtsCatalog.Load();
            check(art!=null&&art.entries.Length==catalog.buildings.Length+catalog.units.Length+catalog.technologies.Length,"Art catalogue covers roster");
            foreach(var b in catalog.buildings)check(art?.Find("building",b.id)?.portrait!=null,"Building portrait "+b.id);
            foreach(var u in catalog.units)check(art?.Find("unit",u.id)?.portrait!=null,"Unit portrait "+u.id);
            foreach(var tech in catalog.technologies)check(art?.Find("technology",tech.id)?.icon!=null,"Technology icon "+tech.id);
            foreach(string font in new[]{"Rajdhani SDF","Rajdhani Bold SDF","Inter SDF","Inter Medium SDF"})check(Resources.Load<FontAsset>("MassiveArmy/UI/Fonts/"+font)!=null,"Font "+font);
            foreach(string template in new[]{"CatalogRow","ResourceCell"})check(Resources.Load<VisualTreeAsset>("MassiveArmy/UI/"+template)!=null,"Reusable UXML "+template);
            var tree=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/Front");check(tree!=null,"UXML loads");if(tree!=null){var root=tree.CloneTree();foreach(string name in new[]{"front","menu","loading","setup","hud","construction","context","modal","minimap","developer"})check(root.Q(name)!=null,"UXML element "+name);}
            foreach(string file in Directory.GetFiles("Assets/MassiveArmy/Runtime","*.cs"))check(!File.ReadAllText(file).Contains("OnGUI("),"No runtime IMGUI: "+Path.GetFileName(file));
            var report=new Report{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()};Directory.CreateDirectory("TestResults/UiRedesign");File.WriteAllText("TestResults/UiRedesign/validation.json",JsonUtility.ToJson(report,true));Debug.Log("UI validation: "+report.result+"; "+checks+" checks");if(failures.Count>0)throw new InvalidOperationException("UI validation failed: "+string.Join(", ",failures));
        }
    }
}
