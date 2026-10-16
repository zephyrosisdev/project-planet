using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        readonly Dictionary<string,Label> uiLabels=new Dictionary<string,Label>();
        readonly Dictionary<string,VectorImage> uiIcons=new Dictionary<string,VectorImage>();
        readonly Dictionary<string,Texture2D> uiPortraits=new Dictionary<string,Texture2D>();
        readonly List<Action> progressUpdates=new List<Action>();
        VisualElement uiRoot,menuUi,loadingUi,setupUi,hudUi,modalUi,constructionUi,contextUi,scoutUi,developerUi,tooltipUi;
        RtsUiDrawing worldDrawing,mapDrawing;
        Image minimapImage,minimapFogImage,atlasImage;
        string uiScreen="",contextKey="",buildKey="",modalKey="",setupKey="";
        double nextUi,nextProgress;
        ListView constructionList;
        sealed class QueueUiRow
        {
            public VisualElement card,track,fill;public Image portrait;public Label title,time;public RtsBuilding owner,pressedOwner;public QueueOrder order,pressedOrder;public bool pressed;public Action update;
        }
        readonly List<QueueUiRow> queueRows=new List<QueueUiRow>();
        readonly Dictionary<int,Button> categoryButtons=new Dictionary<int,Button>();
        Color32[] fogPixels;
        RtsUiArt artwork;Texture2D uiGlow;VisualTreeAsset catalogRowTemplate,resourceCellTemplate;
        bool selectionPanelClosed;RtsBuilding closedBuilding;

        internal void BuildToolkitUi(VisualElement root)
        {
            catalogRowTemplate=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/CatalogRow");resourceCellTemplate=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/ResourceCell");
            uiRoot=root.Q("front");queueRows.Clear();constructionList=null;sharedConstruction=null;categoryButtons.Clear();uiLabels.Clear();progressUpdates.Clear();uiIcons.Clear();uiPortraits.Clear();artwork=Resources.Load<RtsUiArt>("MassiveArmy/UI/FrontArt");uiGlow=Resources.Load<Texture2D>("MassiveArmy/UI/ControlHighlight");
            menuUi=uiRoot.Q("menu");loadingUi=uiRoot.Q("loading");setupUi=uiRoot.Q("setup");hudUi=uiRoot.Q("hud");modalUi=uiRoot.Q("modal");
            constructionUi=uiRoot.Q("construction");contextUi=uiRoot.Q("context");scoutUi=uiRoot.Q("scout");developerUi=uiRoot.Q("developer");tooltipUi=uiRoot.Q("tooltip");
            foreach(var element in new[]{menuUi,loadingUi,setupUi,modalUi,constructionUi,contextUi,scoutUi,developerUi,uiRoot.Q("top"),uiRoot.Q("bottom"),uiRoot.Q("quick-actions")})ui.Block(element);
            worldDrawing=new RtsUiDrawing();worldDrawing.style.position=Position.Absolute;worldDrawing.StretchToParentSize();uiRoot.Q("overlay").Add(worldDrawing);
            MakeMenu();MakeLoading();MakeHud();uiScreen=contextKey=buildKey=modalKey=setupKey="";nextUi=nextProgress=0;
        }
        static void Visible(VisualElement e,bool visible){if(e!=null)e.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;}
        static VisualElement Box(VisualElement parent,string classes="")
        {
            var e=new VisualElement();foreach(string c in classes.Split(' '))if(c.Length>0)e.AddToClassList(c);parent.Add(e);if(e.ClassListContains("panel"))e.Insert(0,new RtsUiFrame());return e;
        }
        Label L(VisualElement parent,string text,string classes="body",string key=null)
        {
            var e=new Label(text);foreach(string c in classes.Split(' '))if(c.Length>0)e.AddToClassList(c);e.pickingMode=PickingMode.Ignore;parent.Add(e);
            if(key!=null)uiLabels[key]=e;return e;
        }
        void Value(string key,string text){if(uiLabels.TryGetValue(key,out var e)&&e.text!=text)e.text=text;}
        VectorImage Icon(string id)
        {
            if(!uiIcons.TryGetValue(id,out var icon)){icon=id.StartsWith("tech-")?artwork?.Find("technology",id.Substring(5))?.icon:Resources.Load<VectorImage>("MassiveArmy/UI/Icons/"+id);uiIcons[id]=icon;}return icon;
        }
        Image I(VisualElement parent,string id,string classes="resource-icon")
        {
            var image=new Image{vectorImage=Icon(id),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.AddToClassList(classes);parent.Add(image);return image;
        }
        Image Portrait(VisualElement parent,string id,string classes="portrait")
        {
            if(!uiPortraits.TryGetValue(id,out var texture)){int separator=id.IndexOf('-');texture=artwork?.Find(id.Substring(0,separator),id.Substring(separator+1))?.portrait;uiPortraits[id]=texture;}
            var image=new Image{image=texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.AddToClassList(classes);parent.Add(image);return image;
        }
        Button B(VisualElement parent,string text,Action action,string icon=null,string classes="")
        {
            var b=new Button(()=>{action();nextUi=0;}){name=text,userData=action};b.AddToClassList("action");foreach(string c in classes.Split(' '))if(c.Length>0)b.AddToClassList(c);
            var glow=new Image{image=uiGlow,pickingMode=PickingMode.Ignore};glow.AddToClassList("control-glow");b.Add(glow);if(icon!=null)I(b,icon,"action-icon");L(b,text,"card-name");parent.Add(b);return b;
        }
        void OnApplicationFocus(bool focus){if(!focus)CancelGestures();}
        void CancelGestures(){if(editingRoute&&Base!=null)Base.Notice="Patrol cancelled";middleCameraArmed=formationPreviewDirty=false;dragging=forming=false;attackArmed=attackUnitArmed=false;wallDragging=editingRoute=false;patrolPoints.Clear();Battle?.CancelPreview();pointer?.Clear();}
        void SetSettingsOpen(bool open)
        {
            if(open==settingsOpen)return;
            if(open){pausedBeforeSettings=Paused;Paused=true;CancelGestures();}else Paused=pausedBeforeSettings;
            settingsOpen=open;
        }
        void ReturnMenu(){CancelGestures();SetSettingsOpen(false);State="menu";rig.InputEnabled=false;DisposeBattle();}
        void MakeMenu()
        {
            menuUi.Clear();L(menuUi,"SYNTHETIC FRONT","heading");var layout=Box(menuUi,"menu-layout");var main=Box(layout,"menu-main");
            L(main,"SYNTHETIC\nFRONT","title");L(main,"Massive armies. One continuous battlefield.","heading");
            L(main,"Base building RTS · GPU combined arms · CYAN vs RED","body");L(main,"BATTLE CAPACITY","subheading");
            var scales=Box(main,"row");for(int i=0;i<Scales.Length;i++){int index=i;var b=B(scales,new[]{"10K","50K","100K","250K","500K","1M"}[i],()=>{chosenScale=index;foreach(var child in scales.Children())child.EnableInClassList("active",scales.IndexOf(child)==index);},null,"scale-choice");b.EnableInClassList("active",i==chosenScale);}
            L(main,"","muted","menu-world");var start=Box(main,"row");B(start,"BASE BUILDING RTS",()=>StartBaseBattle(Scales[chosenScale]),"construct","primary grow");B(start,"SWARM SURVIVAL",()=>StartSwarmBattle(Scales[chosenScale]),"defence","grow");B(start,"AI TEST / SPECTATE",()=>StartTestBattle(Scales[chosenScale]),"research","grow");
            var more=Box(main,"row");B(more,"DEPLOY STRESS ARMIES",()=>{settings.capacity=Scales[chosenScale];StartBattle(Scales[chosenScale]);},"units","grow");B(more,"WORLD SETUP",()=>worldSetupOpen=true,"map","grow");B(more,"SETTINGS",()=>SetSettingsOpen(true),"settings");
            L(main,"Select: click / drag · Shift adds · Ctrl removes\nOrder: right-click / right-drag formation\nCamera: WASD / arrows · middle drag · wheel · Q / E","muted");
            var aiProfiles=Box(main,"row");foreach(int team in new[]{0,1}){int t=team;var choice=new DropdownField(t==0?"Test Cyan AI":"Test Red AI",Enum.GetNames(typeof(StrategicProfile)).ToList(),t==0?settings.aiCyanProfile:settings.aiRedProfile);choice.RegisterValueChangedCallback(e=>{if(t==0)settings.aiCyanProfile=choice.index;else settings.aiRedProfile=choice.index;});aiProfiles.Add(choice);}
            L(main,"Swarm: 10 minutes to prepare · 30-second warning · doubling waves","muted");L(main,"","danger-text body","menu-error");
            var art=Box(layout,"menu-art");Portrait(art,"building-command","hero");L(art,"COMMAND THE FRONT","heading");L(art,"","muted","menu-memory");
        }
        void MakeLoading()
        {
            loadingUi.Clear();L(loadingUi,"SYNTHETIC FRONT","title");Portrait(loadingUi,"building-command","hero");L(loadingUi,"","heading","load-phase");L(loadingUi,"","body","load-info");B(loadingUi,"CANCEL LOADING",CancelLoading,"cancel");
        }
        void MakeHud()
        {
            tooltipUi.Clear();tooltipUi.Insert(0,new RtsUiFrame());L(tooltipUi,"","heading","tooltip-title");L(tooltipUi,"","body","tooltip-body");
            var layout=new RtsHudLayout(uiRoot,id=>{
                switch(id){
                    case "map":ShowBiomeMap();break;case "pause-game":Paused=!Paused;break;case "menu":ReturnMenu();break;
                    case "construct":buildOpen=!buildOpen;break;case "tech":CancelGestures();techOpen=!techOpen;break;
                    case "supply-toggle":settings.logistics=!settings.logistics;break;case "context-reopen":closedBuilding=null;selectionPanelClosed=false;break;
                    case "move":attackArmed=attackUnitArmed=false;break;case "attack-move":attackArmed=true;attackUnitArmed=false;break;case "attack":attackUnitArmed=true;attackArmed=false;break;
                    case "stop":Battle?.IssueOrder(View,Vector2.zero,ArmyOrder.Stop,Vector2.zero);break;
                }nextUi=0;
            });
            foreach(var pair in layout.Labels)uiLabels[pair.Key]=pair.Value;
            var minimap=uiRoot.Q("minimap");minimap.Clear();minimapImage=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};minimapImage.AddToClassList("map-image");minimap.Add(minimapImage);minimapFogImage=new Image{pickingMode=PickingMode.Ignore};minimapFogImage.AddToClassList("map-image");minimap.Add(minimapFogImage);mapDrawing=new RtsUiDrawing();mapDrawing.StretchToParentSize();minimap.Add(mapDrawing);
            minimap.RegisterCallback<PointerDownEvent>(e=>{if(Battle==null||Telemetry.Benchmarking)return;NavigateUiMap(minimap,e.localPosition,false);e.StopPropagation();});
            MakeScout();MakeDeveloper();
        }
        void MakeScout()
        {
            scoutUi.Clear();scoutUi.Insert(0,new RtsUiFrame());L(scoutUi,"RECON & SUPPORT","heading");var row=Box(scoutUi,"row");B(row,"LOAD",()=>Battle.TransportOrder(true),"transport","grow");B(row,"UNLOAD",()=>Battle.TransportOrder(false),"transport","grow");
            B(scoutUi,"CARRIER FIGHTER",()=>{var wallet=Base.Factions[0].wallet;var unit=Base.Catalog.Units["fighter"];if(!Base.Factions[0].HasTech("carrier"))Base.Notice="Research Carrier Operations first";else if(wallet.Spend(unit.cost)){bool queued=Battle.LaunchCarrierWing(Base.Catalog.TypeIndex("fighter"),ok=>{if(!ok)wallet.Credit(unit.cost);});if(!queued)wallet.Credit(unit.cost);}else Base.Notice="Insufficient resources";},"aircraft");
            var pattern=new DropdownField("Search",new List<string>{"Spiral","Grid","Sector","Perimeter","Route","Auto Explore","Observe","Follow Army"},searchPattern-1);pattern.RegisterValueChangedCallback(e=>searchPattern=pattern.index+1);scoutUi.Add(pattern);
            B(scoutUi,"SCOUT ORDER",()=>{if(searchPattern==5){patrolPoints.Clear();editingRoute=true;Base.Notice="Click at least two patrol waypoints";}else Battle.ScoutOrder((SearchPattern)searchPattern,new Vector2(rig.Focus.x,rig.Focus.z),new Vector2(220,220));},"target");
        }
        void MakeDeveloper()
        {
            developerUi.Clear();developerUi.Insert(0,new RtsUiFrame());L(developerUi,"GPU LAB / F1","heading");L(developerUi,"","body","developer-stats");var scroll=new ScrollView();developerUi.Add(scroll);foreach(string marker in BattleTelemetry.Markers)L(scroll,"","muted","marker-"+marker);
            B(developerUi,"BENCHMARK 25 s",()=>{if(!Telemetry.Benchmarking)BeginBenchmark();},"research");B(developerUi,"SUBSYSTEM SWITCHES",()=>SetSettingsOpen(true),"settings");L(developerUi,"","muted","benchmark-report");
        }
        internal void RefreshToolkitUi()
        {
            if(uiRoot==null)return;uiRoot.EnableInClassList("compact",uiRoot.contentRect.width<1750);
            double now=Time.realtimeSinceStartupAsDouble;
            if(now>=nextProgress){nextProgress=now+1.0/30;foreach(var update in progressUpdates)update();DrawWorldUi();}
            if(now<nextUi)return;nextUi=now+.1;
            string screen=State=="menu"?(worldSetupOpen?"setup":"menu"):State;
            if(screen!=uiScreen){ReleaseQueueRows();uiScreen=screen;contextKey=buildKey=setupKey="";}
            Visible(menuUi,screen=="menu");Visible(loadingUi,screen=="loading");Visible(setupUi,screen=="setup");Visible(hudUi,screen=="battle");
            Value("menu-world",$"WRAPPING WORLD {settings.world.size:N0} × {settings.world.size:N0} m · capacity {Scales[chosenScale]:N0}");Value("menu-memory",MemoryBudget.Report(settings));Value("menu-error",error);
            Value("load-phase",Loading);Value("load-info",$"{settings.activeUnits:N0} units · seed {settings.world.seed}");
            if(screen=="setup")RefreshSetup();
            if(State=="battle"&&Battle!=null)RefreshHud();else Visible(tooltipUi,false);
            RefreshModal();
        }
        void RefreshHud()
        {
            var s=Battle.Statistics;Value("faction",$"CYAN {(s==null?"…":s[0].ToString("N0"))}"+(Base==null?$" · RED {(s==null?"…":s[1].ToString("N0"))}":""));Value("fps",$"{Telemetry.Fps:0} FPS · TICK {Battle.Tick}"+(Paused?" · PAUSED":""));Value("selected",settings.spectatorMode?"SPECTATOR � READ ONLY":$"SELECTED {(s==null?"…":s[2].ToString("N0"))}");
            uiRoot.Q<Button>("pause-game").Q<Label>().text=Paused?"RESUME":"PAUSE";uiRoot.Q<Button>("supply-toggle").Q<Label>().text=settings.logistics?"SUPPLY ON":"SUPPLY OFF";
            bool baseMode=Base!=null;Visible(uiRoot.Q("quick-actions"),baseMode&&!settings.spectatorMode);Visible(uiRoot.Q("commands"),!settings.spectatorMode);Visible(uiRoot.Q("grid-status"),baseMode);
            if(baseMode){var f=Base.Factions[settings.spectatorMode?observedTeam:0];for(int i=0;i<5;i++)Value("resource-"+i,$"{f.wallet.Stock[i]:0} / {f.wallet.Capacity[i]:0}");int contacts=0;if(Battle.RadarContacts!=null)for(int i=0;i<GpuBattle.FogResolution*GpuBattle.FogResolution;i++)if(Battle.RadarContacts[i]>0)contacts++;
                Value("power",(settings.spectatorMode?(observedTeam==0?"CYAN AI � ":"RED AI � "):"")+$"POWER {f.generation:0}/{f.demand:0} MW · {(f.efficiency<1?"LOW POWER "+f.efficiency.ToString("P0"):"GRID STABLE")} · BATTERY {f.battery:0}/{f.energyCapacity:0} · T{f.tier} · CONTACTS {contacts}");
                if(SelectedBuilding!=null&&SelectedBuilding.removed)SelectedBuilding=null;
                if(settings.spectatorMode)RefreshObserver();else RefreshConstruction();RefreshBuilding();Value("territory",$"TERRITORY {Base.SectorOwner.Count(t=>t==0)}/16");
            }else{for(int i=0;i<5;i++)Value("resource-"+i,"—");Visible(constructionUi,false);Visible(contextUi,false);Visible(scoutUi,false);}
            var footprint=placing!=null?BuildingGrid.Dimensions(placing,placementRotation):Vector2Int.zero;
            Value("notice",placing!=null?placing.name+" · "+footprint.x+"×"+footprint.y+" cells ("+footprint.x*8+"×"+footprint.y*8+" m) · "+placement.reason:Base?.Notice??Battle.Notice);Visible(uiLabels["notice"],uiLabels["notice"].text.Length>0);
            Value("placement",editingRoute?"Click patrol waypoints · right-click to begin · Esc cancels":placing!=null?"8 m grid · Wheel rotates · Left-click builds · Shift repeats · Drag walls · Esc cancels":attackArmed||attackUnitArmed?"Click terrain or target to issue attack order":"");Visible(uiLabels["placement"],uiLabels["placement"].text.Length>0);
            Value("swarm",(Base!=null?$"ZOMBIES · {Battle.ZombieAlive:N0} roaming / attacking"+(Battle.AmbientZombieRemaining>0?$" · {Battle.AmbientZombieRemaining:N0} arriving":""):"")+(Swarm==null?"":"\n"+Swarm.Status+"\n"+Swarm.Notice));Visible(uiLabels["swarm"],Base!=null);Value("outcome",victory);Visible(uiLabels["outcome"],victory.Length>0);
            Visible(developerUi,ShowDeveloper);if(ShowDeveloper){Visible(contextUi,false);Visible(scoutUi,false);}if(ShowDeveloper){Value("developer-stats",$"CPU {BattleTelemetry.TimeLabel(Telemetry.CpuMs)} · GPU {BattleTelemetry.TimeLabel(Telemetry.GpuMs)}\nSlots {settings.activeUnits:N0}/{settings.capacity:N0}\nVisible {(s==null?0:s[8]+s[9]+s[10]):N0}\nLOD {(s==null?0:s[8]):N0}/{(s==null?0:s[9]):N0}/{(s==null?0:s[10]):N0}\nShells {(s==null?0:s[4]):N0} · Max cell {(s==null?0:s[5])}\nBuffers/maps {MemoryBudget.Bytes(settings)/1048576.0:F1} MiB (estimate)");foreach(string marker in BattleTelemetry.Markers)Value("marker-"+marker,marker.Substring(5)+"  "+BattleTelemetry.TimeLabel(Telemetry.GpuMilliseconds(marker)));Value("benchmark-report",Telemetry.LastReport);}
            RefreshMinimap();RefreshResourceTooltip();
        }
        string AvailabilityKey()=>Base.Factions[0].tier+":"+string.Join("",Base.Catalog.technologies.Select(t=>Base.Factions[0].HasTech(t.id)?"1":"0"));
        RtsConstructionPanel sharedConstruction;
        void RefreshConstruction()
        {
            Visible(constructionUi,buildOpen);if(!buildOpen)return;
            if(sharedConstruction==null){sharedConstruction=new RtsConstructionPanel(constructionUi,artwork,catalogRowTemplate,uiGlow,BeginPlacement,index=>buildCategory=index,()=>buildOpen=false);constructionList=sharedConstruction.List;foreach(var pair in sharedConstruction.Categories)categoryButtons[pair.Key]=pair.Value;}
            sharedConstruction.Refresh(Base.Catalog,Base.Factions[0],buildCategory);
        }
        bool ProgressVisible(VisualElement element){if(element.panel==null||contextUi.resolvedStyle.display==DisplayStyle.None||hudUi.resolvedStyle.display==DisplayStyle.None)return false;var scroll=element.GetFirstAncestorOfType<ScrollView>();return scroll==null?element.worldBound.Overlaps(contextUi.worldBound):element.worldBound.Overlaps(scroll.contentViewport.worldBound);}
        void AddProgress(VisualElement parent,Func<float> value)
        {
            var track=Box(parent,"progress");var fill=Box(track,"progress-fill");progressUpdates.Add(()=>{if(ProgressVisible(track))fill.style.width=Length.Percent(Mathf.Clamp01(value())*100);});
        }
        void RefreshBuilding()
        {
            var b=SelectedBuilding;selectionPanelClosed=b!=null&&ReferenceEquals(b,closedBuilding);Visible(uiRoot.Q("context-reopen"),selectionPanelClosed);Visible(contextUi,b!=null&&!selectionPanelClosed);Visible(scoutUi,b==null&&!settings.spectatorMode);if(b==null){if(contextKey.Length>0)ReleaseQueueRows();contextKey="";return;}if(selectionPanelClosed)return;
            string key=b.index+":"+b.team+":"+b.level+":"+b.Complete+":"+b.Upgrading+":"+b.paused+":"+b.repeat+":"+b.repairing+":"+b.powered+":"+b.holdFire+":"+b.priority+":"+AvailabilityKey()+":"+string.Join(",",b.queue.Select(q=>q.id+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(q)));
            if(key!=contextKey){contextKey=key;selectionPanelClosed=false;
                // Retain this building's scroll view across queue/status updates.
                var retainedScroll=contextUi.Q<ScrollView>("building-scroll");
                if(retainedScroll!=null&&!ReferenceEquals(retainedScroll.userData,b))retainedScroll=null;
                if(retainedScroll!=null){retainedScroll.RemoveFromHierarchy();retainedScroll.Clear();}
                contextUi.Clear();contextUi.Insert(0,new RtsUiFrame());progressUpdates.Clear();
                var titleRow=Box(contextUi,"row");L(titleRow,b.definition.name+" · MK "+b.level,"heading grow");B(titleRow,"×",()=>{selectionPanelClosed=true;closedBuilding=b;},null,"close");
                var info=Box(contextUi,"row");Portrait(info,"building-"+b.definition.id,"portrait-large");L(info,"","body","building-stats");
                if(!b.Complete){L(contextUi,"CONSTRUCTING","subheading");AddProgress(contextUi,()=>b.construction/b.definition.time);}if(b.Upgrading){L(contextUi,"UPGRADING","subheading");AddProgress(contextUi,()=>b.UpgradeFraction);}
                if(settings.spectatorMode){L(contextUi,(b.team==0?"CYAN":"RED")+" AI � READ ONLY","subheading");var readOnly=new ScrollView();contextUi.Add(readOnly);foreach(var q in b.queue){L(readOnly,(q.research?"RESEARCH � ":"PRODUCTION � ")+q.id,"card-name");AddProgress(readOnly,()=>q.Fraction);}Value("building-stats",$"HP {b.health:0}/{b.MaxHealth:0}\nARMOUR {b.Armour:0}\nPOWER {b.definition.consumption:0} MW\nEFFICIENCY {b.Efficiency:P0}");return;}
                if(b.team!=0){L(contextUi,"HOSTILE STRUCTURE","danger-text");return;}
                Action<Action> owned=action=>{if(Base!=null&&!b.removed&&b.team==0)action();};
                var row=Box(contextUi,"row");B(row,b.paused?"RESUME":"PAUSE",()=>owned(()=>b.paused=!b.paused),"pause","grow");B(row,b.repeat?"REPEAT ON":"REPEAT OFF",()=>owned(()=>b.repeat=!b.repeat),"repeat","grow");
                row=Box(contextUi,"row");B(row,"UPGRADE",()=>owned(()=>Base.Upgrade(b)),"upgrade","grow");B(row,b.repairing?"REPAIR ON":"REPAIR",()=>owned(()=>b.repairing=!b.repairing),"repair","grow");L(contextUi,"Upgrade: "+Base.UpgradeCost(b),"muted");
                row=Box(contextUi,"row");B(row,b.powered?"POWER DOWN":"POWER UP",()=>owned(()=>b.powered=!b.powered),"power","grow");B(row,"SELL 45%",()=>owned(()=>{Base.Sell(b);SelectedBuilding=null;}),"sell","danger grow");if(b.Upgrading)B(contextUi,"CANCEL UPGRADE",()=>owned(()=>Base.CancelUpgrade(b)),"cancel");
                if(b.definition.weaponDamage>0){B(contextUi,b.holdFire?"HOLD FIRE":"AUTOMATIC FIRE",()=>owned(()=>b.holdFire=!b.holdFire),"attack");var priority=new EnumField("Target priority",b.priority);priority.RegisterValueChangedCallback(e=>owned(()=>b.priority=(TargetPriority)e.newValue));contextUi.Add(priority);}
                L(contextUi,"","muted","rally");var scroll=retainedScroll??new ScrollView{name="building-scroll",userData=b};contextUi.Add(scroll);if(b.definition.category==BuildingCategory.Research)B(scroll,"OPEN RESEARCH GRAPH",()=>{CancelGestures();techOpen=true;},"research");
                foreach(string id in b.definition.production){var unit=Base.Catalog.Units[id];var card=B(scroll,"",()=>owned(()=>Base.EnqueueUnit(b,id)),null,"catalog-card");card.name="produce-"+id;Portrait(card,"unit-"+id);var details=Box(card,"grow");L(details,unit.name,"card-name");L(details,unit.cost+" · "+unit.time+"s","card-meta");L(scroll,"","card-lock","produce-reason-"+id);}
                L(scroll,"QUEUE · "+b.Bays+" PRODUCTION BAY(S)","subheading");BindQueueRows(scroll,b);
                if(b.queue.Count>0||b.repeatWaiting.Count>0)B(scroll,"CLEAR QUEUE",()=>owned(()=>{b.repeat=false;b.repeatWaiting.Clear();for(int i=b.queue.Count-1;i>=0;i--)Base.Cancel(b,i);}),"cancel");
            }
            Value("building-stats",$"HP {b.health:0}/{b.MaxHealth:0}\nARMOUR {b.Armour:0}\nPOWER {b.definition.consumption:0} MW\nEFFICIENCY {b.Efficiency:P0}");Value("rally","RALLY · "+(b.rallyUnit>=0?"friendly unit / group":"terrain")+" · right-click to change"+(b.node>=0?$"\n{Base.Nodes[b.node].resource}: {b.definition.extraction*Base.Nodes[b.node].rateMultiplier*b.level*b.Efficiency*Base.Modifier(Base.Factions[b.team],"extraction"):0.0}/s · {Base.Nodes[b.node].remaining:0} remaining":""));
            foreach(string id in b.definition.production){bool available=Base.CanProduce(b,id,out string reason);contextUi.Q<Button>("produce-"+id)?.SetEnabled(available);Value("produce-reason-"+id,available?"":reason);}
        }
        void ReleaseQueueRows()
        {
            foreach(var row in queueRows){row.owner=null;row.order=null;row.pressed=false;row.pressedOwner=null;row.pressedOrder=null;row.card.RemoveFromHierarchy();}
        }
        void QueueAction(QueueUiRow row,int delta)
        {
            // Retain the pressed order if production/reordering rebinds this slot before release.
            var owner=row.pressed?row.pressedOwner:row.owner;var order=row.pressed?row.pressedOrder:row.order;row.pressed=false;
            if(settings.spectatorMode||Base==null||owner==null||order==null||owner.removed||owner.team!=0||!ReferenceEquals(SelectedBuilding,owner)||!Base.Buildings.Contains(owner))return;
            int index=owner.queue.IndexOf(order);if(index<0)return;if(delta==0)Base.Cancel(owner,index);else Base.Reorder(owner,index,index+delta);
        }
        QueueUiRow MakeQueueRow()
        {
            var row=new QueueUiRow{card=new VisualElement()};row.card.AddToClassList("queue-card");
            row.portrait=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};row.portrait.AddToClassList("portrait");row.card.Add(row.portrait);
            var details=Box(row.card,"grow");row.title=L(details,"","card-name");row.track=Box(details,"progress");row.fill=Box(row.track,"progress-fill");row.time=L(details,"","card-meta");var controls=Box(details,"row");
            foreach(var action in new[]{(-1,"↑"),(1,"↓"),(0,"CANCEL")}){
                int delta=action.Item1;var button=B(controls,action.Item2,()=>QueueAction(row,delta),delta==0?"cancel":null);
                button.RegisterCallback<PointerDownEvent>(e=>{if(e.button==0){row.pressed=true;row.pressedOwner=row.owner;row.pressedOrder=row.order;}},TrickleDown.TrickleDown);
                button.RegisterCallback<PointerUpEvent>(e=>button.schedule.Execute(()=>row.pressed=false));
                button.RegisterCallback<NavigationSubmitEvent>(e=>row.pressed=false,TrickleDown.TrickleDown);
            }
            row.update=()=>{var q=row.order;if(q==null||!ProgressVisible(row.track))return;row.fill.style.width=Length.Percent(Mathf.Clamp01(q.Fraction)*100);row.time.text=$"{Mathf.Max(0,q.duration-q.progress):0}s · {q.Fraction:P0}";};return row;
        }
        void BindQueueRows(VisualElement parent,RtsBuilding owner)
        {
            // Existing queues are capped at 64 orders; pooled rows represent orders, never army units.
            for(int i=0;i<owner.queue.Count;i++){
                if(i==queueRows.Count)queueRows.Add(MakeQueueRow());var row=queueRows[i];var q=owner.queue[i];row.owner=owner;row.order=q;parent.Add(row.card);
                row.portrait.image=artwork.Find(q.research?"building":"unit",q.research?Base.Catalog.Technologies[q.id].requiredBuilding:q.id)?.portrait;
                row.title.text=(i+1)+". "+(q.research?Base.Catalog.Technologies[q.id].name:Base.Catalog.Units[q.id].name);row.time.text=$"{Mathf.Max(0,q.duration-q.progress):0}s · {q.Fraction:P0}";row.fill.style.width=Length.Percent(Mathf.Clamp01(q.Fraction)*100);progressUpdates.Add(row.update);
            }
            for(int i=owner.queue.Count;i<queueRows.Count;i++){var row=queueRows[i];row.card.RemoveFromHierarchy();row.owner=null;row.order=null;}
        }
        void NavigateUiMap(VisualElement area,Vector2 local,bool close)
        {
            Vector2 uv=(local-area.contentRect.position)/area.contentRect.size;rig.FocusSector(new Vector2((uv.x-.5f)*field.Size,(.5f-uv.y)*field.Size));
            if(close){biomeOpen=false;nextUi=0;}
        }
        void RefreshMinimap()
        {
            minimapImage.image=field.Minimap;minimapFogImage.image=Base!=null&&!settings.spectatorMode?fogMap:null;if(Time.realtimeSinceStartupAsDouble<nextFogMap)return;nextFogMap=Time.realtimeSinceStartupAsDouble+.25;using var fogTiming=UiFogTelemetry.MinimapScope();
            mapDrawing.Lines.Clear();if(Base!=null&&Battle.PlayerVisibility!=null){int r=GpuBattle.FogResolution;if(fogMap==null){fogMap=new Texture2D(r,r,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};fogPixels=new Color32[r*r];minimapFogImage.image=settings.spectatorMode?null:fogMap;}for(int i=0;i<fogPixels.Length;i++){byte state=Battle.PlayerVisibility[i];fogPixels[i]=new Color32(0,8,15,settings.spectatorMode?(byte)0:state==0?(byte)245:state==1?(byte)160:(byte)0);}fogMap.SetPixels32(fogPixels);fogMap.Apply();
                Vector2 size=mapDrawing.contentRect.size;
                if(Battle.RadarContacts!=null)for(int i=0;i<r*r;i++)if(Battle.RadarContacts[i]>0){Vector2 p=new Vector2(i%r/(float)r*size.x,(1-i/r/(float)r)*size.y);if(settings.spectatorMode){for(int team=0;team<3;team++)if((Battle.RadarContacts[i]&(1u<<team))!=0){Vector2 offset=Vector2.up*team*2;mapDrawing.Lines.Add((p+offset,p+offset+Vector2.right*3,team==0?Cyan:team==1?Red:Color.green));}}else mapDrawing.Lines.Add((p,p+Vector2.right*3,Battle.RadarContacts[i]==1?Color.yellow:Red));}
                foreach(var b in Base.Buildings)if(!b.removed&&(b.team==0||Battle.IsVisible(b.position))){Vector2 uv=b.position/field.Size+Vector2.one*.5f,p=new Vector2(uv.x*size.x,(1-uv.y)*size.y);mapDrawing.Lines.Add((p,p+Vector2.right*4,b.team==0?Cyan:Red));}
            }else if(Battle.Statistics!=null){var s=Battle.Statistics;Vector2 size=mapDrawing.contentRect.size;for(int i=0;i<16;i++){float blue=s[48+i],red=s[64+i];if(blue+red==0)continue;Vector2 p=new Vector2((i%4+.5f)*size.x/4,(3-i/4+.5f)*size.y/4);mapDrawing.Lines.Add((p-Vector2.right*8,p+Vector2.right*8,new Color(red/(blue+red),blue/(blue+red)*.8f,blue/(blue+red))));}}
            mapDrawing.MarkDirtyRepaint();
        }
        void RefreshResourceTooltip()
        {
            Visible(tooltipUi,false);if(uiValidationTooltip&&Base!=null){var sample=Base.Nodes[0];Visible(tooltipUi,true);Value("tooltip-title",sample.kind.ToString());Value("tooltip-body",$"{sample.resource} · quality {sample.quality:P0}\n{sample.remaining:N0}/{sample.capacity:N0} remaining");tooltipUi.style.left=500;tooltipUi.style.top=350;return;}if(Base==null||Mouse.current==null||placing!=null||techOpen)return;Vector2 mouse=Mouse.current.position.ReadValue();if(OverHud(mouse)||!rig.GroundPoint(mouse,out var p))return;
            var node=Base.Nodes.FirstOrDefault(n=>field.Distance(n.position,p)<n.radius+5&&Battle.IsVisible(n.position,0,true));if(node==null)return;Visible(tooltipUi,true);Value("tooltip-title",node.kind.ToString());Value("tooltip-body",$"{node.resource} · quality {node.quality:P0}\n{node.remaining:N0}/{node.capacity:N0} remaining");var point=RuntimePanelUtils.ScreenToPanel(uiRoot.panel,new Vector2(mouse.x,Screen.height-mouse.y));tooltipUi.style.left=Mathf.Clamp(point.x+18,0,uiRoot.contentRect.width-360);tooltipUi.style.top=Mathf.Clamp(point.y+18,0,uiRoot.contentRect.height-150);
        }
        void DrawWorldUi()
        {
            worldDrawing.Lines.Clear();if(State!="battle"||Battle==null){worldDrawing.MarkDirtyRepaint();return;}
            if(dragging){Vector2 a=PanelPoint(new Vector2(dragRect.xMin,dragRect.yMin)),b=PanelPoint(new Vector2(dragRect.xMax,dragRect.yMax));AddRect(worldDrawing,Rect.MinMaxRect(a.x,b.y,b.x,a.y),Cyan);}
            if(forming){int count=Battle.PreviewCount>0?Battle.PreviewCount:(int)(Battle.Statistics?[2]??0);float spacing=Base!=null?Battle.PreviewSpacing:formation.Spacing;int cols=Mathf.Clamp(Mathf.FloorToInt(formation.Width/spacing)+1,1,Mathf.Max(1,count)),rows=Mathf.Max(formation.Rows(count),Battle.PreviewRows);Vector2 forward=new Vector2(-formation.Right.y,formation.Right.x),right=formation.Right*cols*spacing*.5f,front=formation.Front+forward*spacing*.5f,back=formation.Front-forward*(Mathf.Max(1,rows)-.5f)*spacing;var corners=new[]{front-right,front+right,back+right,back-right};for(int i=0;i<4;i++)FormationEdge(corners[i],corners[(i+1)%4],formation.Fits(field.Size,count)?Cyan:Red);}
            if(editingRoute)for(int i=1;i<patrolPoints.Count;i++)FormationEdge(patrolPoints[i-1],patrolPoints[i],Cyan);worldDrawing.MarkDirtyRepaint();
        }
        Vector2 PanelPoint(Vector2 screen)=>RuntimePanelUtils.ScreenToPanel(uiRoot.panel,new Vector2(screen.x,Screen.height-screen.y));
        static void AddRect(RtsUiDrawing drawing,Rect r,Color colour){var c=new[]{r.min,new Vector2(r.xMax,r.yMin),r.max,new Vector2(r.xMin,r.yMax)};for(int i=0;i<4;i++)drawing.Lines.Add((c[i],c[(i+1)%4],colour));}
        void FormationEdge(Vector2 a,Vector2 b,Color colour){Vector3 image=rig.Nearest(new Vector3(a.x,field.Height(a)+2,a.y));Vector2 d=field.Delta(a,b);var p=View.WorldToScreenPoint(image);var q=View.WorldToScreenPoint(new Vector3(image.x+d.x,field.Height(b)+2,image.z+d.y));if(p.z>0&&q.z>0)worldDrawing.Lines.Add((PanelPoint(p),PanelPoint(q),colour));}
    }
}

