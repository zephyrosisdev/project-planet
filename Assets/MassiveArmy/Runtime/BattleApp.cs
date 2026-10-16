using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MassiveArmy
{
    public sealed partial class BattleApp : MonoBehaviour
    {
        public BattleSettings settings=new BattleSettings();
        public GpuBattle Battle {get;private set;}
        public Camera View {get;private set;}
        public bool Paused;
        public bool ShowDeveloper=true;
        public string State {get;private set;}="menu";
        public string Loading {get;private set;}="";
        public BattleTelemetry Telemetry {get;}=new BattleTelemetry();
        Battlefield field;
        ArmyRenderer armyRenderer;
        RtsCamera rig;BattleRenderQuality renderQuality;
        GameObject world;
        Coroutine activeLoad;System.Threading.CancellationTokenSource loadCancellation;
        GameObject presentation;
        WaterSurface ocean;
        VolumeProfile profile;
        Light sun;
        BattleLightingRig lightingRig;bool ownedProfile,lastShadows;
        BattleVisualSettings visualSettings;
        VolumetricFogOfWar volumetricFog;
        bool dragging,attackArmed,attackUnitArmed,settingsOpen;
        bool forming,formationPreviewDirty,biomeOpen,mapTerrain;
        string mapExport="";
        Vector2 formationStart,formationScreenStart;
        FormationLayout formation;
        Vector2 dragStart;
        Rect dragRect;
        int chosenScale=1;
        string error="",victory="";
        RtsUiController ui; RtsUiInput pointer; bool pausedBeforeSettings,middleCameraArmed;
        static readonly Color Cyan=new Color(.1f,.85f,1),Red=new Color(1,.15f,.36f),Ink=new Color(.018f,.04f,.065f,.96f);
        static readonly int[] Scales={10000,50000,100000,250000,500000,1000000};

        void OnEnable()
        {

            if(View!=null)return;
            Application.runInBackground=true;Application.targetFrameRate=120;
            QualitySettings.vSyncCount=PlayerPrefs.GetInt("Army.VSync",0);
            settings.shadows=PlayerPrefs.GetInt("Army.Shadows",1)!=0;
            settings.world??=new WorldGenerationSettings();settings.seed=PlayerPrefs.GetInt("Army.Seed",73129);settings.world.seed=settings.seed;
            presentation=new GameObject("Battle presentation");presentation.transform.SetParent(transform,false);
            var cameraObject=new GameObject("RTS camera");cameraObject.transform.SetParent(presentation.transform,false);
            View=cameraObject.AddComponent<Camera>();View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.012f,.025f,.05f);
            cameraObject.tag="MainCamera";var hd=cameraObject.AddComponent<HDAdditionalCameraData>();hd.antialiasing=HDAdditionalCameraData.AntialiasingMode.None;
            hd.clearColorMode=HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=View.backgroundColor;
            rig=cameraObject.AddComponent<RtsCamera>();rig.InputEnabled=false;rig.Sensitivity=PlayerPrefs.GetFloat("Army.Camera",1);
            BuildVolume();renderQuality=presentation.AddComponent<BattleRenderQuality>();renderQuality.Initialize(View,PlayerPrefs.GetFloat("Army.RenderScale",1));
            ui=new RtsUiController(this);ui.Attach();pointer=new RtsUiInput(HandlePointer,CancelGestures);
        }
        void BuildVolume()
        {
            visualSettings=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");
            lightingRig=FindAnyObjectByType<BattleLightingRig>();
            if(lightingRig!=null&&lightingRig.atmosphere!=null&&lightingRig.sun!=null){
                profile=lightingRig.atmosphere.sharedProfile;sun=lightingRig.sun;ownedProfile=false;
                lightingRig.Configure(View,settings.shadows);lastShadows=settings.shadows;return;
            }
            var go=new GameObject("Battle atmosphere");go.transform.SetParent(presentation.transform,false);
            var volume=go.AddComponent<Volume>();volume.isGlobal=true;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;ownedProfile=true;
            var water=profile.Add<WaterRendering>();water.enable.Override(true);water.ambientProbeDimmer.Override(1);
            var lightObject=new GameObject("Battle sun");lightObject.transform.SetParent(presentation.transform,false);lightObject.transform.rotation=Quaternion.Euler(48,-35,0);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;
            sun=light;visualSettings=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");
            if(visualSettings==null)throw new InvalidOperationException("Missing battle visual settings asset.");
            presentation.AddComponent<BattleAtmosphere>().Initialize(visualSettings,profile,sun);
            var shadows=profile.Add<HDShadowSettings>();shadows.maxShadowDistance.Override(2600);
            shadows.cascadeShadowSplit0.Override(.45f);shadows.cascadeShadowSplit1.Override(.62f);shadows.cascadeShadowSplit2.Override(.8f);
            var sunData=sun.GetComponent<HDAdditionalLightData>();sunData.SetShadowResolution(2048);sunData.normalBias=.1f;
            lightingRig=go.AddComponent<BattleLightingRig>();lightingRig.atmosphere=volume;lightingRig.sun=sun;
            lightingRig.Configure(View,settings.shadows);lastShadows=settings.shadows;
        }
        void LateUpdate(){if(lightingRig!=null&&lastShadows!=settings.shadows){lightingRig.Configure(View,settings.shadows);lastShadows=settings.shadows;}}
        public void StartBattle(int count)
        {
            if(State=="loading")return;
            settings.baseBuilding=false;settings.swarmMode=false;settings.spectatorMode=false;settings.activeUnits=count;settings.capacity=Mathf.Max(count,settings.capacity);settings.Validate();
            settings.ScaleWorld();
            activeLoad=StartCoroutine(LoadBattle());
        }
        IEnumerator LoadBattle()
        {
#if UNITY_EDITOR
            var startupClock=System.Diagnostics.Stopwatch.StartNew();
            var startupTimings=new System.Text.StringBuilder();
            long phaseStarted;
#endif
            loadCancellation?.Dispose();loadCancellation=new System.Threading.CancellationTokenSource();var token=loadCancellation.Token;
            State="loading";Loading="Generating terrain and traversal maps";error="";victory="";Paused=false;balanceOutcomeSaved=false;LastBalanceReport="";
            DisposeBattle();yield return null;
            Exception failure=null;
            if(settings.preview==null||settings.preview.InputKey!=settings.world.Key){
                var options=settings.world.Copy();var catalog=RtsCatalog.Load();var generation=System.Threading.Tasks.Task.Run(()=>WorldSetupResult.Generate(options,token,catalog),token);
                while(!generation.IsCompleted)yield return null;
                if(generation.IsCanceled){State="menu";yield break;}
                if(generation.IsFaulted){Fail(generation.Exception.GetBaseException());yield break;}
                settings.preview=generation.Result;
            }
#if UNITY_EDITOR
            phaseStarted=startupClock.ElapsedMilliseconds;
#endif
            try{TransportProgression.Validate(settings.preview,RtsCatalog.Load());}catch(Exception e){failure=e;}
            if(failure!=null){Fail(failure);yield break;}
            try { world=new GameObject("Battle world");world.transform.SetParent(transform,false);field=new Battlefield(settings,world.transform); }
            catch(Exception e) { failure=e; }
            if(failure!=null) { Fail(failure);yield break; }
#if UNITY_EDITOR
            startupTimings.AppendLine($"Terrain and traversal: {startupClock.ElapsedMilliseconds-phaseStarted} ms");
#endif
            yield return null;Loading="Allocating GPU buffers and shared navigation";
#if UNITY_EDITOR
            phaseStarted=startupClock.ElapsedMilliseconds;
#endif
            try { Battle=new GpuBattle(settings,field,settings.baseBuilding?RtsCatalog.Load().units.Select(u=>u.Gpu).ToArray():null); }
            catch(Exception e) { failure=e; }
            if(failure!=null) { Fail(failure);yield break; }
#if UNITY_EDITOR
            startupTimings.AppendLine($"GPU buffers and navigation: {startupClock.ElapsedMilliseconds-phaseStarted} ms");
#endif
            yield return null;Loading="Creating indirect tank tiers, directional atlas and forest";
#if UNITY_EDITOR
            phaseStarted=startupClock.ElapsedMilliseconds;
#endif
            try
            {
                armyRenderer=new ArmyRenderer(Battle);rig.Field=field;rig.Spectator=settings.spectatorMode;rig.ResetOverview();rig.InputEnabled=true;
                ocean=new GameObject("HDRP ocean • visual only").AddComponent<WaterSurface>();ocean.transform.SetParent(world.transform,false);ocean.gameObject.SetActive(settings.world.oceanCoverage>0);
                ocean.surfaceType=WaterSurfaceType.OceanSeaLake;ocean.geometryType=WaterGeometryType.Infinite;
                ocean.largeWindSpeed=12;ocean.repetitionSize=field.Size/Mathf.Max(1,Mathf.Round(field.Size/500));
                // HDRP's secondary swell ratio and fixed 10 m ripple tile do not divide
                // our supported world widths. Keep the periodic primary band aligned
                // to world axes; inland water has its own periodic detail normals.
                ocean.largeOrientationValue=0;ocean.largeBand1Multiplier=0;ocean.ripples=false;
                ocean.refractionColor=new Color(.035f,.16f,.24f);ocean.scatteringColor=new Color(.015f,.095f,.15f);
                ocean.ambientScattering=.3f;
                if(settings.baseBuilding&&!settings.spectatorMode){volumetricFog=world.AddComponent<VolumetricFogOfWar>();volumetricFog.Initialize(Battle,View,sun,visualSettings);}
            }
            catch(Exception e) { failure=e; }
            if(failure!=null) { Fail(failure);yield break; }
#if UNITY_EDITOR
            startupTimings.AppendLine($"Render assets: {startupClock.ElapsedMilliseconds-phaseStarted} ms");
            phaseStarted=startupClock.ElapsedMilliseconds;
#endif
            try { if(settings.baseBuilding){Base=new RtsWorld(Battle,world.transform,RtsCatalog.Load());if(settings.swarmMode)Swarm=new SwarmMode(Base);SelectedBuilding=Base.Buildings[0];buildOpen=true;ShowDeveloper=false;rig.FocusBase(Base.BasePosition(0));} }
            catch(Exception e) { failure=e; }
            if(failure!=null){Fail(failure);yield break;}
#if UNITY_EDITOR
            startupTimings.AppendLine($"Bases and starting units: {startupClock.ElapsedMilliseconds-phaseStarted} ms");
#endif
            yield return null;Loading="GPU pipeline online";State="battle";
#if UNITY_EDITOR
            startupTimings.AppendLine($"Total battle load: {startupClock.ElapsedMilliseconds} ms ({DateTime.UtcNow:O})");
            System.IO.Directory.CreateDirectory("TestResults");
            System.IO.File.WriteAllText("TestResults/startup-timing.txt",startupTimings.ToString());
#endif
        }
        public void CancelLoading()
        {
            loadCancellation?.Cancel();if(activeLoad!=null)StopCoroutine(activeLoad);activeLoad=null;
            DisposeBattle();State="menu";Loading="Generation cancelled";
        }
        void Fail(Exception e) { Debug.LogException(e);error=e.Message;State="menu";DisposeBattle(); }
        void Update()
        {
            if(planetSession!=null){pointer?.Clear();return;}
            Telemetry.Update(Battle);
            PollWorldPreview();ui?.Tick();RotatePlacement();pointer?.Drain(State=="battle"&&!settingsOpen&&!biomeOpen&&!techOpen&&!Telemetry.Benchmarking&&Application.isFocused);RefreshFormationPreview();
            if(State!="battle" || Battle==null){if(Keyboard.current?.escapeKey.wasPressedThisFrame==true&&settingsOpen)SetSettingsOpen(false);return;}
            var k=Keyboard.current;
            if(k!=null && Application.isFocused && !Telemetry.Benchmarking)
            {
                bool escape=k.escapeKey.wasPressedThisFrame;
                if(escape)HandleEscape();
                if(!escape&&!(ui?.TextFocused??false))
                {
                    if(k.mKey.wasPressedThisFrame&&!settingsOpen&&!techOpen) { if(biomeOpen)biomeOpen=false;else ShowBiomeMap(); }
                    if(k.f1Key.wasPressedThisFrame&&(!settings.baseBuilding||settings.spectatorMode)&&!settingsOpen&&!biomeOpen&&!techOpen)ShowDeveloper=!ShowDeveloper;
                    if(settings.baseBuilding&&!settings.spectatorMode&&!settingsOpen&&!biomeOpen){if(techOpen){if(k[baseHotkeys.technology].wasPressedThisFrame)techOpen=false;}else BaseHotkeys(k);}
                    if(k.spaceKey.wasPressedThisFrame && !settingsOpen&&!biomeOpen&&!techOpen)Paused=!Paused;
                    if(k.nKey.wasPressedThisFrame&&Mouse.current!=null)TryTestNukeAtCursor(Mouse.current.position.ReadValue());
                }
            }
            rig.InputEnabled=!settingsOpen && !biomeOpen && !techOpen && !Telemetry.Benchmarking && !forming && !(ui?.TextFocused??false);
            rig.PointerInputEnabled=placing==null&&!placementWheelConsumed&&(Mouse.current==null||!OverHud(Mouse.current.position.ReadValue()));rig.PanInputEnabled=middleCameraArmed;
            rig.UpdateForFrame();
            if(Base!=null&&!Paused){float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);Base.Update(dt);if(Base.Outcome.Length==0)Swarm?.Update(dt);if(Base.Outcome.Length>0){victory=Base.Outcome;Paused=true;if(settings.spectatorMode&&!balanceOutcomeSaved){balanceOutcomeSaved=true;ExportBalanceSnapshot();}}}
            Battle.Update(View,Time.unscaledDeltaTime,armyRenderer.IndexCounts,Paused);
            UpdatePlacement();
            Base?.Rebase(View);field.UpdateLod(View,settings.terrain);ocean.gameObject.SetActive(settings.terrain&&settings.world.oceanCoverage>0);
            armyRenderer.Render(View);
            var stats=Battle.Statistics;
            if(Base==null && stats!=null && victory.Length==0 && Battle.Tick>30)
            {
                if(stats[0]==0)victory="DEFEAT · CYAN ARMY DESTROYED";
                else if(stats[1]==0)victory="VICTORY · RED ARMY DESTROYED";
                if(victory.Length>0)Paused=true;
            }
        }
        void HandleEscape()
        {
            if(placing!=null||techOpen||editingRoute){CancelPlacement();techOpen=false;CancelGestures();}
            else if(biomeOpen)biomeOpen=false;
            else SetSettingsOpen(!settingsOpen);
        }
        bool OverHud(Vector2 p)=>settingsOpen||biomeOpen||techOpen||(ui?.Blocks(p)??true);
        internal bool TryTestNukeAtCursor(Vector2 screen)
        {
            if(State!="battle"||Battle==null||Telemetry.Benchmarking||(ui?.TextFocused??false)||OverHud(screen)||!rig.GroundPoint(screen,out var point))return false;
            Battle.RequestTestNuke(point);return true;
        }
        void RefreshFormationPreview(){if(!formationPreviewDirty)return;formationPreviewDirty=false;if(forming&&Battle!=null)Battle.PreviewFormation(View,formation);}
        void HandlePointer(RtsPointerInput input)
        {
            Vector2 mouse=new Vector2(input.mousePosition.x,Screen.height-input.mousePosition.y);
            if(input.button==2){if(input.type==PointerPhase.Down)middleCameraArmed=!OverHud(mouse);if(input.type==PointerPhase.Up)middleCameraArmed=false;return;}
            if(OverHud(mouse)){if(input.type==PointerPhase.Up){dragging=forming=wallDragging=false;Battle.CancelPreview();}return;}
            if(settings.spectatorMode){if(input.type==PointerPhase.Down&&input.button==0){SelectedBuilding=Base?.Pick(View,mouse);closedBuilding=null;selectionPanelClosed=false;if(SelectedBuilding!=null)observedTeam=SelectedBuilding.team;}return;}
            if(placing!=null)UpdatePlacement(mouse);
            if(BasePointer(input,mouse))return;
            if(input.type==PointerPhase.Down && input.button==0 && !OverHud(mouse) && !forming)
            {
                if(attackArmed || attackUnitArmed)
                {
                    var building=Base?.Pick(View,mouse);
                    if(building!=null&&building.team!=0)Battle.AttackStructure(View,building.index,building.position,building.definition.radius);
                    else if(rig.GroundPoint(mouse,out var p))SendOrder(p,attackUnitArmed?ArmyOrder.Attack:ArmyOrder.AttackMove,new Vector2(mouse.x/Screen.width,mouse.y/Screen.height));
                    attackArmed=attackUnitArmed=false;
                }
                else { SelectedBuilding=null;dragging=true;dragStart=mouse; }
            }
            if(dragging)
            {
                Vector2 min=Vector2.Min(dragStart,mouse),max=Vector2.Max(dragStart,mouse);dragRect=Rect.MinMaxRect(min.x,min.y,max.x,max.y);
                if(input.type==PointerPhase.Up && input.button==0)
                {
                    dragging=false;var operation=input.control?SelectionOperation.Remove:
                        input.shift?SelectionOperation.Add:SelectionOperation.Replace;
                    bool click=Vector2.Distance(dragStart,mouse)<5;
                    if(click)dragRect=new Rect(mouse.x,mouse.y,0,0);
                    Battle.Select(View,new Rect(dragRect.x/Screen.width,dragRect.y/Screen.height,dragRect.width/Screen.width,dragRect.height/Screen.height),operation,click);
                }
            }
            if(input.type==PointerPhase.Down && input.button==1 && !OverHud(mouse))
            {
                dragging=false;attackArmed=attackUnitArmed=false;
                if(rig.GroundPoint(mouse,out formationStart))
                {
                    forming=true;formationScreenStart=mouse;
                    formationBuilding=Base?.Pick(View,mouse);if(formationBuilding!=null&&formationBuilding.team==0)formationBuilding=null;
                    formation=FormationLayout.Compact(formationStart,new Vector2(View.transform.right.x,View.transform.right.z),(int)(Battle.Statistics?[2]??1));
                    formationPreviewDirty=true;
                }
            }
            if(forming)
            {
                if(input.type==PointerPhase.Move && rig.GroundPoint(mouse,out var end) && Vector2.Distance(mouse,formationScreenStart)>5)
                { Vector2 delta=field.Delta(formationStart,end);formation=new FormationLayout(field.Canonical(formationStart+delta*.5f),delta,delta.magnitude);formationPreviewDirty=true; }
                if(input.type==PointerPhase.Up && input.button==1)
                {
                    forming=false;
                    if(!OverHud(mouse)){
                        if(formationBuilding!=null&&!formationBuilding.removed&&Vector2.Distance(mouse,formationScreenStart)<5)Battle.AttackStructure(View,formationBuilding.index,formationBuilding.position,formationBuilding.definition.radius);
                        else SendOrder(formation.Front,ArmyOrder.Move,mouse,formation);
                    }
                    else Battle.CancelPreview();
                    formationBuilding=null;
                }
            }
        }
#if UNITY_EDITOR
        public void PreviewFormationGestureForValidation()
        {
            Paused=true;
            if(Base!=null){
                CancelPlacement();SelectedBuilding=null;buildOpen=techOpen=false;
                Battle.Select(View,new Rect(0,0,1,1),SelectionOperation.Replace,false);
                Vector2 centre=Base.BasePosition(0);rig.FocusBase(centre+Vector2.right*60);
                Vector2 a=centre+new Vector2(60,-40),b=centre+new Vector2(140,20);
                var screenA=View.WorldToScreenPoint(new Vector3(a.x,field.Height(a),a.y));var screenB=View.WorldToScreenPoint(new Vector3(b.x,field.Height(b),b.y));
                HandlePointer(new RtsPointerInput{type=PointerPhase.Down,button=1,mousePosition=new Vector2(screenA.x,Screen.height-screenA.y)});
                HandlePointer(new RtsPointerInput{type=PointerPhase.Move,button=1,mousePosition=new Vector2(screenB.x,Screen.height-screenB.y)});
                RefreshFormationPreview();System.IO.File.WriteAllText("TestResults/rts-formation-input.txt",$"Right mouse drag: forming={forming}, preview={Battle.PreviewActive}, units={Battle.PreviewCount}, width={formation.Width:0.00}, right={formation.Right}");
                return;
            }
            Battle.Select(View,new Rect(.30f,.36f,.10f,.18f),SelectionOperation.Replace,false);
            var start=View.WorldToScreenPoint(new Vector3(60,field.Height(new Vector2(60,0)),0));
            var end=View.WorldToScreenPoint(new Vector3(160,field.Height(new Vector2(160,55)),55));
            HandlePointer(new RtsPointerInput{type=PointerPhase.Down,button=1,mousePosition=new Vector2(start.x,Screen.height-start.y)});
            HandlePointer(new RtsPointerInput{type=PointerPhase.Move,button=1,mousePosition=new Vector2(end.x,Screen.height-end.y)});
        }
        public void CommitFormationGestureForValidation()
        {
            var point=View.WorldToScreenPoint(new Vector3(formation.Front.x,field.Height(formation.Front),formation.Front.y));
            HandlePointer(new RtsPointerInput{type=PointerPhase.Up,button=1,mousePosition=new Vector2(point.x,Screen.height-point.y)});
            Battle.ReadUnitSample(0,8,units=>{
                var selected=units.Where(u=>(u.flags&3)==3&&(u.packed&255)==0).ToArray();
                bool valid=selected.Length>0&&selected.All(u=>(u.flags&16)!=0)&&selected.Select(u=>u.destination).Distinct().Count()==selected.Length;
                System.IO.File.AppendAllText("TestResults/rts-formation-input.txt",$"\nRight mouse release: forming={forming}, valid={valid}, selected={selected.Length}, notice={Battle.Notice}");
            });
        }
        public void BuildingAttackGestureForValidation()
        {
            var enemy=Base.Buildings.First(b=>b.team!=0&&!b.removed);
            SelectedBuilding=null;buildOpen=techOpen=false;Paused=true;Battle.Select(View,new Rect(0,0,1,1),SelectionOperation.Replace,false);
            rig.FocusBase(enemy.position);
            int x=WorldTopology.Cell(Mathf.FloorToInt((enemy.position.x/field.Size+.5f)*GpuBattle.FogResolution),GpuBattle.FogResolution),y=WorldTopology.Cell(Mathf.FloorToInt((enemy.position.y/field.Size+.5f)*GpuBattle.FogResolution),GpuBattle.FogResolution);
            int cell=x+y*GpuBattle.FogResolution;byte old=Battle.PlayerVisibility[cell];Battle.PlayerVisibility[cell]=2;
            var screen=View.WorldToScreenPoint(new Vector3(enemy.position.x,field.Height(enemy.position)+5,enemy.position.y));
            var mouse=new Vector2(screen.x,Screen.height-screen.y);
            HandlePointer(new RtsPointerInput{type=PointerPhase.Down,button=1,mousePosition=mouse});HandlePointer(new RtsPointerInput{type=PointerPhase.Up,button=1,mousePosition=mouse});
            Battle.PlayerVisibility[cell]=old;rig.FocusBase(Base.BasePosition(0)+Vector2.right*60);
            Battle.ReadUnitSample(0,8,units=>{
                var selected=units.Where(u=>(u.flags&3)==3&&(u.packed&255)==0).ToArray();
                bool valid=selected.Length>0&&selected.All(u=>u.target==(0x80000000u|(uint)enemy.index)&&(u.flags&65536)!=0);
                System.IO.File.AppendAllText("TestResults/rts-formation-input.txt",$"\nRight click hostile building: valid={valid}, selected={selected.Length}, target={enemy.index}, notice={Battle.Notice}");
            });
        }
#endif
        public void ShowBiomeMap()
        {
            if(field==null)return;CancelGestures();biomeOpen=true;forming=dragging=false;attackArmed=attackUnitArmed=false;Battle.CancelPreview();
        }
        public void BeginBenchmark(bool resetCamera=true) { Paused=false;forming=dragging=biomeOpen=false;Battle.CancelPreview();if(resetCamera)rig.ResetOverview();Telemetry.Begin(View); }
        void DisposeBattle()
        {
            if(planetSession!=null){ArmyLifetime.Destroy(planetSession.gameObject);planetSession=null;if(View!=null)View.enabled=true;if(ui?.Root!=null)ui.Root.style.display=UnityEngine.UIElements.DisplayStyle.Flex;}
            progressUpdates.Clear();contextKey=buildKey="";closedBuilding=null;selectionPanelClosed=false;contextUi?.Clear();if(constructionList!=null)constructionList.itemsSource=null;
            if(volumetricFog!=null)volumetricFog.Detach();volumetricFog=null;
            CancelPlacement();ArmyLifetime.Destroy(fogMap);fogMap=null;Swarm=null;Base?.Dispose();Base=null;SelectedBuilding=null;buildOpen=techOpen=false;
            forming=dragging=biomeOpen=false;mapExport="";
            if(Battle!=null && Telemetry.Benchmarking)Telemetry.Finish(Battle);
            armyRenderer?.Dispose();armyRenderer=null;Battle?.Dispose();Battle=null;
            if(rig!=null)rig.Field=null;field?.Dispose();field=null;if(world!=null)ArmyLifetime.Destroy(world);world=null;
        }
        void OnDisable()
        {
            loadCancellation?.Cancel();loadCancellation?.Dispose();loadCancellation=null;if(activeLoad!=null)StopCoroutine(activeLoad);activeLoad=null;
            pointer?.Dispose();pointer=null;ui?.Dispose();ui=null;
            CancelWorldPreview();
            DisposeBattle();if(profile!=null&&ownedProfile)ArmyLifetime.Destroy(profile);profile=null;lightingRig=null;
            if(presentation!=null)ArmyLifetime.Destroy(presentation);presentation=null;View=null;rig=null;
            State="menu";
        }
        void OnDestroy() => OnDisable();
    }
}
