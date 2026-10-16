using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MassiveArmy
{
    public sealed class UiGalleryHarness : MonoBehaviour
    {
        [NonSerialized] public string outputOverride;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)] struct WindowRect {public int left,top,right,bottom;}
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] static extern bool AdjustWindowRectEx(ref WindowRect rect,uint style,bool menu,uint extendedStyle);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
#endif
        static void SetCaptureSize(Vector2Int size)
        {
            // Resolution request has already settled before the client-area override.
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Windows normally clamps oversized windows to the monitor. This opt-in
            // gallery resizes its own client area so ultrawide captures are real pixels.
            var window=GetActiveWindow();if(window==IntPtr.Zero)return;
            var rect=new WindowRect{right=size.x,bottom=size.y};
            AdjustWindowRectEx(ref rect,(uint)GetWindowLong(window,-16),false,(uint)GetWindowLong(window,-20));
            SetWindowPos(window,IntPtr.Zero,0,0,rect.right-rect.left,rect.bottom-rect.top,0x0004);
#endif
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-gallery")>=0)new GameObject("UI gallery harness").AddComponent<UiGalleryHarness>();}
        IEnumerator Start()
        {
            var app=FindAnyObjectByType<BattleApp>();var args=Environment.GetCommandLineArgs();int output=Array.IndexOf(args,"-benchmark-output");string folder=outputOverride??(output>=0?args[output+1]:Application.persistentDataPath);Directory.CreateDirectory(folder);
            Application.targetFrameRate=60;File.WriteAllText(Path.Combine(folder,"resolutions.txt"),"");
            foreach(var screen in new[]{"menu","settings","setup"}){app.ShowUiValidationScreen(screen);yield return new WaitForSecondsRealtime(1);yield return Capture(folder,screen);}
            app.ShowUiValidationScreen("preview");double deadline=Time.realtimeSinceStartupAsDouble+180;while(!app.UiPreviewReady&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;yield return new WaitForSecondsRealtime(1);yield return Capture(folder,"world-preview");
            app.ShowUiValidationScreen("deploy");yield return null;yield return Capture(folder,"loading");while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(app.State!="battle"){File.WriteAllText(Path.Combine(folder,"error.txt"),app.State+" "+app.Loading);if(!Application.isEditor)Application.Quit(2);yield break;}
            app.Paused=false;yield return new WaitForSecondsRealtime(2);app.Paused=true;
            foreach(var screen in new[]{"battle","construction","production","production-queue","research","research-queue","research-complete","atlas","hostile","low-power","tooltip","removed","selection","outcome","defeat","developer"}){app.ShowUiValidationScreen(screen);yield return new WaitForSecondsRealtime(1);if(screen=="battle")app.ValidateUiGeometry(folder,"battle");yield return Capture(folder,screen);}
            app.ShowUiValidationScreen("swarm-deploy");while(app.State=="loading")yield return null;app.Paused=false;yield return new WaitForSecondsRealtime(2);app.Paused=true;app.ShowUiValidationScreen("swarm");yield return new WaitForSecondsRealtime(1);yield return Capture(folder,"swarm");
            app.ShowUiValidationScreen("battle");app.ValidateUiInteractions(folder);yield return app.ValidateUiTextEntry(folder);yield return app.ValidateUiFormationCommands(folder);yield return app.ValidateUiGestures(folder);yield return app.ValidateQueuePointerRebind(folder);
            if(!Application.isEditor)foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720),new Vector2Int(3440,1440),new Vector2Int(2560,1440)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(1);SetCaptureSize(size);yield return new WaitForSecondsRealtime(.5f);
                File.AppendAllText(Path.Combine(folder,"resolutions.txt"),$"Requested {size.x}×{size.y}; actual {Screen.width}×{Screen.height}\n");
                foreach(float scale in new[]{.85f,1f,1.25f}){app.SetUiValidationScale(scale);yield return new WaitForSecondsRealtime(.5f);app.ValidateUiGeometry(folder,$"layout-{size.x}-{size.y}-{scale:0.00}");yield return Capture(folder,$"layout-{size.x}-{size.y}-{scale:0.00}");}
            }
            app.SetUiValidationScale(1);app.ShowUiValidationScreen("cancel-loading");yield return new WaitForSecondsRealtime(1);yield return Capture(folder,"loading-cancelled");app.ShowUiValidationScreen("failed-loading");yield return new WaitForSecondsRealtime(1);yield return Capture(folder,"loading-failure");File.WriteAllText(Path.Combine(folder,"gallery.txt"),app.ToolkitDiagnostics);if(!Application.isEditor)Application.Quit();
        }
        static IEnumerator Capture(string folder,string screen)
        {
            FindAnyObjectByType<BattleApp>().ValidateScreenGeometry(folder,screen);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(folder,screen+".png")));yield return null;
        }
    }
    public sealed partial class BattleApp
    {
        bool uiValidationTooltip;
        public bool UiPreviewReady=>settings.preview!=null;
        public void SetUiValidationScale(float scale){ui.SetScale(scale);}
        [Serializable] sealed class InteractionReport { public string result;public int checks;public string[] failures; }
        public void ValidateScreenGeometry(string folder,string screen)
        {
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            if(screen=="settings"||screen=="setup"||screen=="world-preview"){
                var parent=screen=="settings"?modalUi:setupUi;var slider=parent.Q<Slider>();var dragger=slider?.Q(className:"unity-base-slider__dragger");Check(dragger!=null&&dragger.worldBound.width>=8&&dragger.worldBound.height>=8,"Standard slider thumb has usable size");
                if(screen=="settings"){var toggle=parent.Q<Toggle>();var mark=toggle?.Q(className:"unity-toggle__checkmark");Check(mark!=null&&mark.worldBound.width>=8&&mark.worldBound.height>=8,"Checkbox has usable size");}
            }
            if(screen=="production"||screen=="production-queue"||screen=="research-queue"||screen=="low-power"||screen=="tooltip")foreach(var row in queueRows.Where(r=>r.order!=null&&r.card.panel!=null))Check(row.card.worldBound.height>=70,"Queue row retains readable height");
            if(screen=="low-power"){Check(Base.Factions[0].demand>Base.Factions[0].generation,"Fixture creates a real power deficit");Check(SelectedBuilding.Efficiency<.99f,"Selected building displays reduced efficiency");}
            if(screen=="research"||screen=="research-complete"){var canvas=modalUi.Q(className:"tech-canvas");Check(canvas!=null&&canvas.worldBound.height>=800,"Technology graph retains scrollable height");}
            if(checks>0)File.WriteAllText(Path.Combine(folder,"screen-geometry-"+screen+".json"),JsonUtility.ToJson(new InteractionReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
        public void ValidateUiGeometry(string folder,string screen)
        {
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            Rect front=uiRoot.worldBound,top=uiRoot.Q("top").worldBound,bottom=uiRoot.Q("bottom").worldBound;
            Check(front.width>500&&front.height>500,"Full-screen root has usable dimensions");
            Check(Mathf.Abs(hudUi.worldBound.height-front.height)<1,"HUD fills root height");
            Check(top.height>=50,"Resource bar is not compressed");
            Check(constructionUi.worldBound.height>=200,"Construction panel is not compressed");
            Check(contextUi.worldBound.height>=200,"Building panel is not compressed");
            Check(bottom.y>front.y+front.height*.5f&&bottom.height>=80,"Commands occupy the bottom edge");
            Check(constructionUi.resolvedStyle.backgroundColor.a>.9f&&contextUi.resolvedStyle.backgroundColor.a>.9f,"Panel surfaces remain opaque");
            File.WriteAllText(Path.Combine(folder,"geometry-"+screen+".json"),JsonUtility.ToJson(new InteractionReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
        public IEnumerator ValidateQueuePointerRebind(string folder)
        {
            Paused=true;var producer=UiBenchmarkFixture.Prepare(Base,out _);producer.paused=true;SelectedBuilding=producer;closedBuilding=null;contextKey="";nextUi=0;RefreshToolkitUi();yield return null;
            var row=queueRows[0];var button=row.card.Q<Button>("CANCEL");button.GetFirstAncestorOfType<ScrollView>().ScrollTo(button);yield return new WaitForSecondsRealtime(.2f);
            var original=row.order;Vector2 panelPoint=button.worldBound.center;Vector2 pixel=new Vector2(panelPoint.x*Screen.width/uiRoot.worldBound.width,Screen.height-panelPoint.y*Screen.height/uiRoot.worldBound.height);
            InputSystem.QueueStateEvent(Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState{position=pixel}.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));yield return new WaitForSecondsRealtime(.1f);bool pressed=row.pressed;
            Base.Reorder(producer,0,1);nextUi=0;RefreshToolkitUi();yield return new WaitForSecondsRealtime(.1f);var rebound=row.order;
            InputSystem.QueueStateEvent(Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState{position=pixel});yield return new WaitForSecondsRealtime(.2f);
            bool safe=pressed&&!ReferenceEquals(original,rebound)&&producer.queue.Contains(rebound);
            File.WriteAllText(Path.Combine(folder,"queue-pointer.json"),$"{{\"result\":\"{(safe?"PASS":"FAIL")}\",\"physicalPressRecorded\":{pressed.ToString().ToLowerInvariant()},\"reboundOrderSurvivesRelease\":{producer.queue.Contains(rebound).ToString().ToLowerInvariant()}}}");
            SelectedBuilding=Base.Buildings.First(b=>b.team==0&&!b.removed&&b.definition.id=="command");contextKey="";nextUi=0;RefreshToolkitUi();
        }
        public IEnumerator ValidateCameraAndMenuScroll(string folder)
        {
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            void Refresh(){nextUi=0;RefreshToolkitUi();}
            CancelGestures();CancelPlacement();settingsOpen=biomeOpen=techOpen=false;Paused=true;buildOpen=true;closedBuilding=null;
            var producer=UiBenchmarkFixture.Prepare(Base,out _);producer.paused=true;SelectedBuilding=producer;Refresh();yield return null;
            var button=contextUi.Q<Button>("produce-"+producer.definition.production[0]);button.Focus();yield return null;
            Check(!ui.TextFocused,"Production button does not claim text input");
            foreach(var key in new[]{KeyCode.W,KeyCode.A,KeyCode.S,KeyCode.D,KeyCode.UpArrow,KeyCode.DownArrow,KeyCode.LeftArrow,KeyCode.RightArrow}){
                using(var e=KeyDownEvent.GetPooled('\0',key,EventModifiers.None)){button.SendEvent(e);Check(e.isPropagationStopped,"Camera key is reserved: "+key);}
            }
            foreach(var direction in new[]{Vector2.up,Vector2.down,Vector2.left,Vector2.right}){
                using(var e=NavigationMoveEvent.GetPooled(direction,EventModifiers.None)){button.SendEvent(e);Check(e.isPropagationStopped,"Directional UI navigation is blocked: "+direction);}
                Check(ReferenceEquals(ui.Root.focusController.focusedElement,button),"Camera navigation retains button focus");
            }
            Check(Application.isFocused,"Game view owns application focus for movement validation");
            foreach(var key in new[]{Key.W,Key.A,Key.S,Key.D,Key.UpArrow,Key.DownArrow,Key.LeftArrow,Key.RightArrow}){
                button.Focus();yield return null;Vector3 before=rig.Focus;
                InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(key));yield return null;yield return null;
                Check(rig.InputEnabled&&(rig.Focus-before).sqrMagnitude>.0001f,"Focused UI button permits camera movement: "+key);
                Check(ReferenceEquals(ui.Root.focusController.focusedElement,button),"Movement key does not navigate UI: "+key);
                InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());yield return null;
            }
            var scroll=contextUi.Q<ScrollView>("building-scroll");scroll.scrollOffset=new Vector2(0,150);yield return null;float offset=scroll.scrollOffset.y;
            Check(offset>0,"Production fixture is scrolled");
            int count=producer.queue.Count;((Action)button.userData)();Refresh();yield return null;yield return null;
            Check(producer.queue.Count==count+1,"Production action enqueues unit");
            Check(ReferenceEquals(scroll,contextUi.Q<ScrollView>("building-scroll"))&&Mathf.Abs(scroll.scrollOffset.y-offset)<1,"Enqueue retains production scroll offset");
            Base.Reorder(producer,0,1);Refresh();yield return null;yield return null;
            Check(Mathf.Abs(scroll.scrollOffset.y-offset)<1,"Reorder retains production scroll offset");
            Base.Cancel(producer,0);Refresh();yield return null;yield return null;
            Check(Mathf.Abs(scroll.scrollOffset.y-offset)<1,"Cancellation retains production scroll offset");
            buildCategory=Base.Catalog.buildings.GroupBy(d=>(int)d.category).OrderByDescending(g=>g.Count()).First().Key;Refresh();yield return null;yield return null;
            var constructionScroll=constructionList.Q<ScrollView>();constructionScroll.scrollOffset=new Vector2(0,94);yield return null;offset=constructionScroll.scrollOffset.y;
            Check(offset>0,"Construction fixture is scrolled");
            var definition=((IEnumerable<BuildingDefinition>)constructionList.itemsSource).Last(d=>Base.Factions[0].HasTech(d.requiredTech)&&Base.Factions[0].tier>=d.tier);
            BeginPlacement(definition);Refresh();yield return null;yield return null;
            Check(placing==definition&&Mathf.Abs(constructionScroll.scrollOffset.y-offset)<1,"Building placement retains construction scroll offset");
            buildKey="";Refresh();yield return null;yield return null;
            Check(Mathf.Abs(constructionScroll.scrollOffset.y-offset)<1,"Construction availability refresh retains scroll offset");
            CancelPlacement();SelectedBuilding=Base.Buildings.First(b=>b.team==0&&!b.removed&&b.definition.id=="command");Refresh();yield return null;yield return null;
            Check(contextUi.Q<ScrollView>("building-scroll").scrollOffset.y==0,"Selecting another building starts its menu at the top");
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"camera-menu-scroll.json"),JsonUtility.ToJson(new InteractionReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
        public IEnumerator ValidateUiTextEntry(string folder)
        {
            SetSettingsOpen(true);nextUi=0;RefreshToolkitUi();yield return null;
            var input=modalUi.Q<IntegerField>();input?.Focus();yield return null;bool focused=ui.TextFocused;
            input?.Blur();SetSettingsOpen(false);nextUi=0;RefreshToolkitUi();
            File.WriteAllText(Path.Combine(folder,"text-focus.json"),$"{{\"result\":\"{(focused?"PASS":"FAIL")}\",\"numericTextFocused\":{focused.ToString().ToLowerInvariant()}}}");
        }
        public IEnumerator ValidateUiFormationCommands(string folder)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-gallery")<0&&!Application.isEditor)throw new InvalidOperationException("Validation requires opt-in");
            CancelGestures();SelectedBuilding=null;buildOpen=false;settingsOpen=biomeOpen=techOpen=false;Paused=true;nextUi=0;RefreshToolkitUi();
            Battle.Select(View,new Rect(0,0,1,1),SelectionOperation.Replace,false);yield return null;
            Vector2 a=new Vector2(Screen.width*.45f,Screen.height*.35f),b=new Vector2(Screen.width*.55f,Screen.height*.35f);
            HandlePointer(new RtsPointerInput{type=PointerPhase.Down,button=1,mousePosition=new Vector2(a.x,Screen.height-a.y)});
            HandlePointer(new RtsPointerInput{type=PointerPhase.Move,button=1,mousePosition=new Vector2(b.x,Screen.height-b.y)});
            RefreshFormationPreview();bool preview=Battle.PreviewActive;yield return null;
            HandlePointer(new RtsPointerInput{type=PointerPhase.Up,button=1,mousePosition=new Vector2(b.x,Screen.height-b.y)});
            bool completed=false,valid=false;int selectedCount=0;
            Battle.ReadUnitSample(0,8,units=>{var selected=units.Where(u=>(u.flags&3)==3&&(u.packed&255)==0).ToArray();selectedCount=selected.Length;valid=selectedCount>0&&selected.All(u=>(u.flags&16)!=0)&&selected.Select(u=>u.destination).Distinct().Count()==selectedCount;completed=true;});
            double deadline=Time.realtimeSinceStartupAsDouble+10;while(!completed&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"formation.json"),$"{{\"result\":\"{(preview&&valid&&!forming?"PASS":"FAIL")}\",\"preview\":{preview.ToString().ToLowerInvariant()},\"selected\":{selectedCount},\"distinctGpuDestinations\":{valid.ToString().ToLowerInvariant()}}}");
            SelectedBuilding=Base.Buildings[0];buildOpen=true;nextUi=0;
        }
        public void ValidateUiInteractions(string folder)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-gallery")<0&&!Application.isEditor)throw new InvalidOperationException("Validation requires opt-in");
            var failures=new List<string>();int checks=0;Action<bool,string> check=(ok,name)=>{checks++;if(!ok)failures.Add(name);};
            Action refresh=()=>{nextUi=0;RefreshToolkitUi();};Action<Button> click=button=>{((Action)button.userData)();refresh();};
            var command=Base.Buildings.First(b=>b.team==0&&b.definition.id=="command");SelectedBuilding=command;closedBuilding=null;buildOpen=true;refresh();
            check(constructionUi.resolvedStyle.display!=DisplayStyle.None&&contextUi.resolvedStyle.display!=DisplayStyle.None,"Both side panels open");
            click(contextUi.Q<Button>("×"));check(SelectedBuilding==command&&closedBuilding==command&&buildOpen,"Closing details retains selection and construction");
            click(uiRoot.Q<Button>("context-reopen"));check(!selectionPanelClosed,"Details can reopen independently");
            click(constructionUi.Q<Button>("×"));check(!buildOpen&&SelectedBuilding==command,"Closing construction retains details");buildOpen=true;
            Paused=false;SetSettingsOpen(true);check(Paused,"Settings pauses active match");SetSettingsOpen(false);check(!Paused,"Settings restores running state");
            Paused=true;SetSettingsOpen(true);SetSettingsOpen(false);check(Paused,"Settings preserves prior pause");
            command=UiBenchmarkFixture.Prepare(Base,out var validationLab);SelectedBuilding=command;command.paused=false;command.repeat=false;command.construction=0;refresh();check(contextUi.Q<Button>("produce-"+command.definition.production[0]).enabledSelf==false,"Construction disables production");
            check(uiLabels["produce-reason-"+command.definition.production[0]].text.Length>0,"Unavailable production has readable reason");command.construction=command.definition.time;refresh();
            var wallet=Base.Factions[0].wallet;for(int i=0;i<5;i++)wallet.Stock[i]=wallet.Capacity[i];
            click(contextUi.Q<Button>("produce-"+command.definition.production[0]));check(command.queue.Count>0,"Production control enqueues existing action");
            int count=command.queue.Count;var firstOrder=command.queue[0];click(contextUi.Q<Button>("↓"));check(command.queue[1]==firstOrder,"Queue reorder control moves an existing order");click(contextUi.Query<Button>("↑").ToList()[1]);check(command.queue[0]==firstOrder,"Queue reorder control restores order");click(contextUi.Q<Button>("REPEAT OFF"));check(command.repeat,"Repeat control");click(contextUi.Q<Button>("PAUSE"));check(command.paused,"Pause production control");
            click(contextUi.Q<Button>("CANCEL"));check(command.queue.Count==count-1,"Queue cancellation control");command.repeat=command.paused=false;
            command.team=1;refresh();check(contextUi.Q<Button>("UPGRADE")==null,"Hostile building has no owned actions");command.team=0;
            command.removed=true;refresh();check(SelectedBuilding==null,"Removed building clears details");command.removed=false;
            SelectedBuilding=null;buildOpen=false;refresh();Vector2 worldPoint=new Vector2(Screen.width*.45f,Screen.height*.35f);
            check(!OverHud(worldPoint)&&Base.Pick(View,worldPoint)==null,"World gesture point lies outside actual HUD and structures");
            var down=new RtsPointerInput{type=PointerPhase.Down,button=0,mousePosition=new Vector2(worldPoint.x,Screen.height-worldPoint.y)};HandlePointer(down);check(dragging,"World press starts selection gesture");
            var release=new RtsPointerInput{type=PointerPhase.Up,button=0,mousePosition=new Vector2(Screen.width*.5f,12)};HandlePointer(release);check(!dragging,"Release across HUD cancels selection");
            down.button=1;HandlePointer(down);check(forming,"World right press starts formation");release.button=1;HandlePointer(release);check(!forming&&!Battle.PreviewActive,"Release across HUD cancels formation preview");
            HandlePointer(down);OnApplicationFocus(false);check(!forming&&!dragging&&!middleCameraArmed,"Focus loss cancels gestures");
            HandlePointer(down);SetSettingsOpen(true);check(!forming&&!dragging,"Modal opening cancels gestures");refresh();
            var text=modalUi.Q<IntegerField>();check(text!=null,"Numeric text field is present");SetSettingsOpen(false);
            techOpen=true;SelectedBuilding=Base.Buildings[0];refresh();check(techReason.text.Length>0&&!researchButton.enabledSelf,"Research prerequisites explain unavailable action");techOpen=false;
            ui.SetScale(.85f);check(Mathf.Approximately(ui.Scale,.85f),"Minimum UI scale");ui.SetScale(1.25f);check(Mathf.Approximately(ui.Scale,1.25f),"Maximum UI scale");ui.SetScale(1);
            SelectedBuilding=command;buildOpen=true;refresh();
            File.WriteAllText(Path.Combine(folder,"interactions.json"),JsonUtility.ToJson(new InteractionReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
        public IEnumerator ValidateUiGestures(string folder)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-gallery")<0&&!Application.isEditor)throw new InvalidOperationException("Validation requires opt-in");
            var failures=new List<string>();int checks=0;Action<bool,string> check=(ok,name)=>{checks++;if(!ok)failures.Add(name);};
            Action refresh=()=>{nextUi=0;RefreshToolkitUi();};Action<Button> click=button=>{if(button==null){check(false,"Required action exists");return;}((Action)button.userData)();refresh();};
            CancelGestures();CancelPlacement();SelectedBuilding=null;buildOpen=false;techOpen=biomeOpen=settingsOpen=false;Paused=true;refresh();yield return null;
            Vector2 worldPoint=new Vector2(Screen.width*.45f,Screen.height*.35f);
            RtsPointerInput Pointer(PointerPhase phase,int button,Vector2 screen,bool shift=false,bool control=false)=>new RtsPointerInput{type=phase,button=button,mousePosition=new Vector2(screen.x,Screen.height-screen.y),shift=shift,control=control};
            Vector2 ScreenPoint(Vector2 point){var p=View.WorldToScreenPoint(rig.Nearest(new Vector3(point.x,field.Height(point),point.y)));return new Vector2(p.x,p.y);}
            check(!OverHud(worldPoint),"World test point is outside actual UI");
            HandlePointer(Pointer(PointerPhase.Down,0,worldPoint));HandlePointer(Pointer(PointerPhase.Up,0,worldPoint));check(!dragging,"Same-frame world click finishes");
            HandlePointer(Pointer(PointerPhase.Down,2,new Vector2(Screen.width*.5f,Screen.height-10)));check(!middleCameraArmed,"Middle press on UI cannot pan");
            HandlePointer(Pointer(PointerPhase.Down,2,worldPoint));check(middleCameraArmed,"Middle press on world arms pan");HandlePointer(Pointer(PointerPhase.Up,2,worldPoint));check(!middleCameraArmed,"Middle release ends pan");
            editingRoute=true;patrolPoints.Clear();HandlePointer(Pointer(PointerPhase.Down,0,worldPoint));HandlePointer(Pointer(PointerPhase.Down,0,worldPoint+Vector2.right*40));check(patrolPoints.Count==2&&!dragging,"Patrol clicks create waypoints without selecting");
            OnApplicationFocus(false);check(!editingRoute&&patrolPoints.Count==0,"Focus loss cancels route gesture");
            editingRoute=true;patrolPoints.Add(Vector2.zero);SetSettingsOpen(true);check(!editingRoute&&patrolPoints.Count==0,"Modal cancels route gesture");SetSettingsOpen(false);
            var home=Base.BasePosition(0);rig.FocusBase(home);SelectedBuilding=Base.Buildings[0];refresh();yield return null;
            rig.GroundPoint(worldPoint,out var rally);HandlePointer(Pointer(PointerPhase.Down,1,worldPoint));check(field.Distance(SelectedBuilding.rally,rally)<.01f&&!forming,"Right click routes through building rally action");yield return new WaitForSecondsRealtime(.2f);
            SelectedBuilding=null;refresh();yield return null;
            var minimap=uiRoot.Q("minimap");var priorFocus=rig.Focus;
            foreach(var uv in new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1),new Vector2(-.01f,.5f),new Vector2(1.01f,.5f)}){
                NavigateUiMap(minimap,minimap.contentRect.position+uv*minimap.contentRect.size,false);Vector2 expected=field.Canonical(new Vector2((uv.x-.5f)*field.Size,(.5f-uv.y)*field.Size));check(field.Distance(new Vector2(rig.Focus.x,rig.Focus.z),expected)<.01f,"Minimap navigation wraps "+uv);
            }
            ShowBiomeMap();refresh();yield return null;var atlas=modalUi.Q(className:"map-atlas");NavigateUiMap(atlas,atlas.contentRect.position+new Vector2(atlas.contentRect.width,0),true);check(!biomeOpen&&field.Distance(new Vector2(rig.Focus.x,rig.Focus.z),field.Canonical(new Vector2(field.Size*.5f,field.Size*.5f)))<.01f,"Atlas corner closes modal and wraps seams");rig.FocusBase(home);refresh();yield return null;
            Vector2 uiPixel=new Vector2(Screen.width*.5f,Screen.height-10);float zoom=View.orthographicSize;
            InputSystem.QueueStateEvent(Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState{position=uiPixel,scroll=Vector2.up});yield return null;yield return null;
            check(!rig.PointerInputEnabled&&Mathf.Approximately(View.orthographicSize,zoom),"UI wheel does not zoom world camera");InputSystem.QueueStateEvent(Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState{position=worldPoint});yield return null;
            SetSettingsOpen(true);refresh();yield return null;var text=modalUi.Q<IntegerField>();text.Focus();yield return null;check(ui.TextFocused,"Numeric entry owns keyboard focus");bool previousBuild=buildOpen;
            InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.B,Key.Space));yield return null;yield return null;
            check(buildOpen==previousBuild&&Paused,"Text entry suppresses construction and pause shortcuts");InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());yield return null;
            InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.Escape));yield return null;yield return null;check(!settingsOpen&&Paused,"Escape closes focused settings and restores prior pause");InputSystem.QueueStateEvent(Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());yield return null;
            var faction=Base.Factions[0];faction.tier=3;faction.wallet.Stock=faction.wallet.Capacity;SelectedBuilding=Base.Buildings[0];refresh();click(contextUi.Q<Button>("REPAIR"));check(SelectedBuilding.repairing,"Repair dispatches existing action");click(contextUi.Q<Button>("POWER DOWN"));check(!SelectedBuilding.powered,"Power down control");click(contextUi.Q<Button>("POWER UP"));check(SelectedBuilding.powered,"Power up control");click(contextUi.Q<Button>("UPGRADE"));check(SelectedBuilding.Upgrading,"Upgrade starts existing paid action");click(contextUi.Q<Button>("CANCEL UPGRADE"));check(!SelectedBuilding.Upgrading,"Upgrade cancellation refunds through existing action");
            SelectedBuilding=null;buildOpen=false;refresh();yield return null;var previousVisibility=(byte[])Battle.PlayerVisibility.Clone();Array.Fill(Battle.PlayerVisibility,(byte)2);
            bool FindSite(string id,out Vector2 point){var definition=Base.Catalog.Buildings[id];for(int y=0;y<16;y++)for(int x=0;x<20;x++){var pixel=new Vector2(Screen.width*(.27f+x*.025f),Screen.height*(.24f+y*.04f));if(OverHud(pixel)||!rig.GroundPoint(pixel,out point))continue;if(Base.ValidatePlacement(definition,0,point).valid)return true;}point=default;return false;}
            try{
                check(FindSite("wind",out var site),"Valid visible foundation found for placement gesture");BeginPlacement(Base.Catalog.Buildings["wind"]);int count=Base.Buildings.Count(b=>!b.removed&&b.definition.id=="wind");HandlePointer(Pointer(PointerPhase.Down,0,ScreenPoint(site),true));check(placing!=null&&Base.Buildings.Count(b=>!b.removed&&b.definition.id=="wind")==count+1,"Shift placement constructs and retains repetition");
                check(FindSite("wind",out site),"Second foundation found");HandlePointer(Pointer(PointerPhase.Down,0,ScreenPoint(site)));check(placing==null&&Base.Buildings.Count(b=>!b.removed&&b.definition.id=="wind")==count+2,"Ordinary placement constructs and ends repetition");
                check(FindSite("wall",out site),"Wall foundation found");Vector2 end=site;bool chain=false;for(int direction=0;direction<4&&!chain;direction++){var delta=new Vector2(Mathf.Cos(direction*Mathf.PI/2),Mathf.Sin(direction*Mathf.PI/2))*8;chain=true;for(int i=1;i<=3;i++){var p=field.Canonical(site+delta*i);if(!Base.ValidatePlacement(Base.Catalog.Buildings["wall"],0,p).valid||OverHud(ScreenPoint(p))){chain=false;break;}}if(chain)end=field.Canonical(site+delta*3);}
                check(chain,"Visible multi-segment wall path found");BeginPlacement(Base.Catalog.Buildings["wall"]);HandlePointer(Pointer(PointerPhase.Down,0,ScreenPoint(site)));check(wallDragging,"Wall press starts chain");HandlePointer(Pointer(PointerPhase.Move,0,ScreenPoint(end)));HandlePointer(Pointer(PointerPhase.Up,0,ScreenPoint(end)));check(!wallDragging&&placing==null&&Base.Buildings.Count(b=>!b.removed&&b.definition.id=="wall")>=2,"Wall release constructs multiple segments");
                BeginPlacement(Base.Catalog.Buildings["wall"]);HandlePointer(Pointer(PointerPhase.Down,0,ScreenPoint(site)));HandlePointer(Pointer(PointerPhase.Up,0,uiPixel));check(!wallDragging,"Wall release over UI cancels chain");CancelPlacement();
                var seamCells=new List<int>();int gridN=BuildingGrid.Resolution(field.Size);int seamStart=BuildingGrid.CellIndex(gridN-1,gridN/2,field.Size),seamEnd=BuildingGrid.CellIndex(1,gridN/2,field.Size);var router=new WallPreviewRouter();bool routed=router.Find(seamStart,seamEnd,gridN,c=>true,seamCells,out _);var seam=seamCells.Select(c=>BuildingGrid.CellCentre(c,field.Size)).ToList();check(routed&&seam.Count==3&&seam.All(p=>p.x>=-field.Size*.5f&&p.x<field.Size*.5f)&&field.Distance(seam[0],seam[2])<17,"Wall preview follows shortest wrapping seam");
            }finally{Array.Copy(previousVisibility,Battle.PlayerVisibility,previousVisibility.Length);CancelPlacement();}
            SelectedBuilding=null;refresh();click(scoutUi.Q<Button>("LOAD"));click(scoutUi.Q<Button>("UNLOAD"));uint expectedAlive=Battle.Statistics[0]+Battle.Statistics[1];bool transportRead=false,transportSafe=false;Battle.ReadUnitSample(0,8,units=>{transportSafe=units.Count(u=>(u.flags&1)!=0)==expectedAlive&&units.Where(u=>(u.flags&1)!=0).All(u=>u.health>0&&(u.flags&32768)==0);transportRead=true;});double readDeadline=Time.realtimeSinceStartupAsDouble+10;while(!transportRead&&Time.realtimeSinceStartupAsDouble<readDeadline)yield return null;check(transportSafe,"Unselected load/unload controls preserve the GPU army");var pattern=scoutUi.Q<DropdownField>();pattern.index=4;click(scoutUi.Q<Button>("SCOUT ORDER"));check(editingRoute,"Route dropdown and scout control start patrol mode");CancelGestures();
            rig.FocusBase(home);SelectedBuilding=Base.Buildings[0];SelectedBuilding.repairing=false;Base.Notice="";buildOpen=true;refresh();Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"gestures.json"),JsonUtility.ToJson(new InteractionReport{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
        // Explicit opt-in validation fixture. Called only by UiGalleryHarness.
        public void ShowUiValidationScreen(string screen)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-ui-gallery")<0&&!Application.isEditor)throw new InvalidOperationException("UI gallery requires opt-in");
            uiValidationTooltip=false;settingsOpen=biomeOpen=techOpen=false;worldSetupOpen=false;victory="";ShowDeveloper=false;nextUi=0;
            if(screen=="settings")SetSettingsOpen(true);
            if(screen=="setup"||screen=="preview"){worldSetupOpen=true;if(screen=="preview")BeginWorldPreview();}
            if(screen=="deploy")StartFlatValidationBattle(50000);
            if(screen=="cancel-loading"){StartFlatValidationBattle(50000);CancelLoading();return;}
            if(screen=="failed-loading"){Fail(new InvalidOperationException("Validation fixture: loading failed. Generate a new world preview and try again."));return;}
            if(screen=="swarm-deploy"){StartFlatValidationBattle(50000, swarm:true);return;}
            if(Base==null)return;
            Paused=true;buildOpen=true;
            if(screen=="battle"){SelectedBuilding=Base.Buildings[0];buildCategory=0;}
            if(screen=="construction"){SelectedBuilding=Base.Buildings[0];buildCategory=3;}
            if(screen=="production")
            {
                SelectedBuilding=UiBenchmarkFixture.Prepare(Base,out var lab);
            }
            if(screen=="production-queue"){contextUi.Q<ScrollView>().scrollOffset=new Vector2(0,360);}
            if(screen=="research-complete"){var lab=Base.Buildings.First(b=>b.definition.id=="lab");var order=lab.queue.First(q=>q.research);selectedTechnology=order.id;order.progress=order.duration;lab.paused=false;lab.Efficiency=1;Base.Update(0);lab.paused=true;SelectedBuilding=lab;techOpen=true;}
            if(screen=="research-queue"){UiBenchmarkFixture.Prepare(Base,out var lab);SelectedBuilding=lab;}
            if(screen=="research"){UiBenchmarkFixture.Prepare(Base,out var lab);SelectedBuilding=lab;techOpen=true;}
            if(screen=="atlas")ShowBiomeMap();
            if(screen=="tooltip")uiValidationTooltip=true;
            if(screen=="removed"){SelectedBuilding=Base.Buildings.First(b=>b.definition.id=="barracks");Base.Sell(SelectedBuilding);}
            if(screen=="hostile")SelectedBuilding=Base.Buildings.First(b=>b.team==1);
            if(screen=="low-power"){foreach(var building in Base.Buildings.Where(b=>b.team==0&&b.definition.generation>0))building.powered=false;Base.Factions[0].battery=0;SelectedBuilding=Base.Buildings.First(b=>b.definition.id=="lab");SelectedBuilding.paused=false;Base.Update(.25f);}
            if(screen=="defeat")victory="DEFEAT · CYAN COMMAND CENTRE DESTROYED";
            if(screen=="selection"){SelectedBuilding=null;Battle.Select(View,new Rect(0,0,1,1),SelectionOperation.Replace,false);}
            if(screen=="outcome")victory="VICTORY · CYAN CONTROLS THE FRONT";
            if(screen=="swarm")Swarm.Schedule.Advance(601);
            if(screen=="developer"){ShowDeveloper=true;SelectedBuilding=null;}
        }
    }
}
