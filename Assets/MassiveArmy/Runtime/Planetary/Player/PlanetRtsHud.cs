using System;

using System.Collections.Generic;

using System.Linq;

using UnityEngine;

using UnityEngine.UIElements;

namespace MassiveArmy.Planetary

{

    /// <summary>Planetary Front HUD with interactive navigation and read-only spectator gameplay controls.</summary>

    public sealed partial class PlanetRtsHud : IDisposable

    {

        readonly PlanetDevelopment fixture;readonly PlanetRtsBase world;readonly RtsUiController ui;

        readonly RtsUiArt art;readonly VisualTreeAsset cardTemplate,resourceTemplate;

        readonly List<Label> resources=new List<Label>();VisualElement front,context;Label selection,notice,power,queueStatus;

        RtsConstructionPanel sharedConstruction;int buildCategory;bool buildOpen;Button constructionTab;float drawerLeft;int revision=-1;double nextRefresh;

        RtsHudLayout layout;bool technologyOpen;
        readonly Dictionary<string,Button> technologyCards=new Dictionary<string,Button>();

        Texture2D map;RtsUiDrawing mapDrawing;VisualElement mapHost;

        public VisualElement Root=>ui.Root;

        public bool Blocks(Vector2 point)=>ui.Blocks(point);

        public bool ModalOpen=>technologyOpen||civilisationOpen;
        bool Observer=>fixture.sessionMode==PlanetGameMode.AiTest;

        public bool TextFocused=>ui.TextFocused;

        public void SetRenderTarget(RenderTexture target)=>ui.SetRenderTarget(target);

        public PlanetRtsHud(PlanetDevelopment fixture,PlanetRtsBase world)

        {

            this.fixture=fixture;this.world=world;art=Resources.Load<RtsUiArt>("MassiveArmy/UI/FrontArt");cardTemplate=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/CatalogRow");resourceTemplate=Resources.Load<VisualTreeAsset>("MassiveArmy/UI/ResourceCell");

            ui=new RtsUiController(fixture.transform,Build,Refresh,()=>!fixture.IsFirstPerson&&!fixture.IsPiloting);ui.Attach();

        }

        Label Label(VisualElement parent,string text,string style="body"){var label=new Label(text);label.AddToClassList(style);parent.Add(label);return label;}

        Button Button(VisualElement parent,string text,Action action,string name=null){var button=new Button(action){text=text,name=name,userData=action};button.AddToClassList("action");parent.Add(button);return button;}

        void Build(VisualElement root)

        {

            front=root.Q("front");front.AddToClassList("planet-hud");foreach(string id in new[]{"menu","loading","setup","modal","tooltip","developer","scout","overlay"})front.Q(id).style.display=DisplayStyle.None;

            front.Q("hud").style.display=DisplayStyle.Flex;resources.Clear();revision=-1;

            foreach(string id in new[]{"top","bottom","construction","context","quick-actions"})ui.Block(front.Q(id));

            layout=new RtsHudLayout(front,Dispatch);layout.Value("selection-help","SHIFT add · CTRL remove\nRight-click orders");layout.Value("camera-help-text","WASD / MIDDLE DRAG\nWHEEL ZOOM\nF: WALK / INTERACT · TAB: RTS · H: SHIP · N: NUKE\nAI TEST: 1 / 2 VIEW FACTION");resources.Clear();for(int i=0;i<5;i++)resources.Add(layout.Labels["resource-"+i]);

            power=layout.Labels["power"];selection=layout.Labels["selected"];notice=layout.Labels["notice"];
            layout.Labels["swarm"].style.display=DisplayStyle.None;

            foreach(string id in new[]{"attack","load","unload","carrier","scout","supply-toggle","benchmark"}){layout.Actions[id].SetEnabled(false);layout.Actions[id].tooltip="This gameplay subsystem is still being migrated to planetary navigation.";}

            front.Q("scout").style.display=DisplayStyle.None;front.Q("developer").style.display=DisplayStyle.None;

            ui.Block(front.Q("scout"));ui.Block(front.Q("modal"));

            Label(front.Q("camera-help"),"F: first person • R: rotate blueprint","muted");

            var construction=front.Q("construction");sharedConstruction=new RtsConstructionPanel(construction,art,cardTemplate,Resources.Load<Texture2D>("MassiveArmy/UI/ControlHighlight"),world.BeginPlacement,index=>ShowBuildingCategory((BuildingCategory)index),()=>SetConstructionOpen(false),readOnly:Observer);sharedConstruction.Refresh(world.Catalog,world.Faction,buildCategory);SetConstructionOpen(buildOpen);

            if(fixture.sessionMode==PlanetGameMode.AiTest){front.Q("commands").SetEnabled(false);}

            // The planetary HUD keeps only a standalone map from the shared bottom strip.
            front.Q("bottom").style.display=DisplayStyle.None;
            front.Q("quick-actions").Clear();front.Q("quick-actions").style.display=DisplayStyle.None;
            constructionTab=Button(front.Q("hud"),"B\nU\nI\nL\nD\n>",()=>SetConstructionOpen(!buildOpen),"construction-tab");
            constructionTab.tooltip="Open / close construction [B]";
            constructionTab.AddToClassList("construction-tab");ui.Block(constructionTab);
            drawerLeft=buildOpen?40:-400;UpdateConstructionDrawer(true);SetConstructionOpen(buildOpen);
            MakeMap();BuildResourceHover();context=front.Q("context");Refresh();
            if(world.Civilisation!=null){var toolbar=new VisualElement{name="settlement-toolbar"};toolbar.AddToClassList("settlement-toolbar");front.Q("hud").Add(toolbar);ui.Block(toolbar);Button(toolbar,"ORBITALS",OpenOrbitals,"orbitals-open");Button(toolbar,"RESIDENTS",OpenResidents,"residents-open");Button(toolbar,"RESOURCES",OpenResources,"resources-open");Button(toolbar,"SETTLEMENTS",ToggleCivilisation,"civilisation-open");BuildDistrictToggle(toolbar);Button(toolbar,"RESEARCH",()=>Dispatch("tech"),"tech");Button(toolbar,"ROADS",()=>world.BeginRoadPlacement(),"civilisation-roads").SetEnabled(fixture.sessionMode!=PlanetGameMode.AiTest);}

        }

        void Dispatch(string id)

        {
            if(Observer&&(id=="move"||id=="attack-move"||id=="stop"))return;

            switch(id){

                case "move":fixture.AttackMoveArmed=false;break;

                case "attack-move":fixture.AttackMoveArmed=true;world.Notice="Click terrain to attack-move with the selected army";break;

                case "stop":fixture.AttackMoveArmed=false;fixture.Commands.Stop();break;

                case "pause-game":fixture.Paused=!fixture.Paused;break;

                case "menu":fixture.ReturnToMenu();break;

                case "construct":SetConstructionOpen(!buildOpen);break;

                case "tech":civilisationOpen=false;if(Observer&&world.Selected!=null)inspectedFaction=world.Selected.team;technologyOpen=!technologyOpen;ShowTechnology();break;

                case "map":fixture.Rig.Distance=fixture.definition.radius*2;break;

                case "context-reopen":Context();break;

                case "settings":world.Notice="Return to the main menu to change rendering settings.";break;

            }

        }

        void ShowTechnology()

        {

            var modal=front.Q("modal");modal.Clear();technologyCards.Clear();modal.style.display=technologyOpen?DisplayStyle.Flex:DisplayStyle.None;if(!technologyOpen)return;

            var panel=new VisualElement();panel.AddToClassList("panel");panel.style.width=1100;modal.Add(panel);Label(panel,"TECHNOLOGY DEPENDENCY GRAPH","heading");Button(panel,"CLOSE",()=>{technologyOpen=false;ShowTechnology();});
            if(Observer){var factions=new VisualElement();factions.AddToClassList("row");panel.Add(factions);for(int i=0;i<world.Factions.Length;i++){int team=i;Button(factions,team==0?"CYAN":"RED",()=>{inspectedFaction=team;ShowTechnology();},"technology-faction-"+team);}Label(panel,"SPECTATOR · VIEW ONLY · "+(inspectedFaction==0?"CYAN":"RED"),"muted").name="technology-observer";}
            var faction=world.Factions[Observer?inspectedFaction:0];

            var scroll=new ScrollView(ScrollViewMode.VerticalAndHorizontal);scroll.style.height=530;panel.Add(scroll);var canvas=new VisualElement();canvas.AddToClassList("tech-canvas");scroll.Add(canvas);var drawing=new RtsUiDrawing();drawing.StretchToParentSize();canvas.Add(drawing);

            var positions=new Dictionary<string,Rect>();int column=0,maxRows=0;foreach(int tier in world.Catalog.technologies.Select(t=>t.tier).Distinct().OrderBy(t=>t)){int row=0;foreach(var tech in world.Catalog.technologies.Where(t=>t.tier==tier))positions[tech.id]=new Rect(10+column*260,44+row++*102,230,78);column++;maxRows=Math.Max(maxRows,row);}canvas.style.width=Math.Max(1040,column*260);canvas.style.height=Math.Max(850,44+maxRows*102);

            foreach(var tech in world.Catalog.technologies){var t=tech;var card=Button(canvas,"",()=>{if(Observer)return;var lab=world.Buildings.FirstOrDefault(b=>b.team==0&&b.Complete&&b.definition.id==t.requiredBuilding&&b.level>=t.requiredBuildingLevel);if(lab==null)world.Notice="Requires "+t.requiredBuilding+" MK "+t.requiredBuildingLevel;else if(!world.Research(lab,t.id))world.Notice="Research requirements are not met";else ShowTechnology();},"tech-"+t.id);card.AddToClassList("tech-node");var rect=positions[t.id];card.style.left=rect.x;card.style.top=rect.y;

                technologyCards.Add(t.id,card);foreach(string dep in t.prerequisites){var a=positions[dep];var from=new Vector2(a.xMax,a.center.y);var to=new Vector2(rect.xMin,rect.center.y);drawing.Lines.Add((from,to,faction.HasTech(dep)?Color.cyan:Color.gray));}}
            RefreshTechnology();

        }
        void RefreshTechnology(){
            if(!technologyOpen)return;var faction=world.Factions[Observer?inspectedFaction:0];
            foreach(var tech in world.Catalog.technologies){if(!technologyCards.TryGetValue(tech.id,out var card))continue;bool complete=faction.HasTech(tech.id),pending=faction.researching.Contains(tech.id);string text=tech.name+"\n"+(complete?"COMPLETE":pending?"IN PROGRESS":tech.cost.ToString());if(card.text!=text)card.text=text;card.EnableInClassList("complete",complete);card.EnableInClassList("pending",pending);card.SetEnabled(!Observer&&!complete&&!pending);}
        }

        // One projection is shared by the texture, markers and pointer navigation. UV y increases southwards.
        internal static Vector2 MapUv(Vector3 direction){direction.Normalize();return new Vector2(Mathf.Atan2(direction.x,direction.z)/(2*Mathf.PI)+.5f,.5f-Mathf.Asin(Mathf.Clamp(direction.y,-1,1))/Mathf.PI);}
        internal static Vector3 MapDirection(Vector2 uv){float longitude=(uv.x-.5f)*2*Mathf.PI,latitude=(.5f-uv.y)*Mathf.PI,parallel=Mathf.Cos(latitude);return new Vector3(Mathf.Sin(longitude)*parallel,Mathf.Sin(latitude),Mathf.Cos(longitude)*parallel);}

        void MakeMap()

        {

            if(map==null||map.width!=320){ArmyLifetime.Destroy(map);const int width=320,height=160;map=new Texture2D(width,height,TextureFormat.RGB24,false){name="Planet longitude-latitude overview",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};var pixels=new Color[width*height];var surface=new PlanetSurface(fixture.definition);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++){var direction=MapDirection(new Vector2((x+.5f)/width,1-(y+.5f)/height));pixels[x+y*width]=Biomes.Colours[(int)surface.SampleBiome(direction)];}map.SetPixels(pixels);map.Apply(false,true);}

            mapHost=front.Q("minimap");front.Q("hud").Add(mapHost);ui.Block(mapHost);mapHost.Clear();var image=new Image{image=map,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};image.StretchToParentSize();mapHost.Add(image);mapDrawing=new RtsUiDrawing{name="minimap-camera"};mapDrawing.StretchToParentSize();mapHost.Add(mapDrawing);

            var north=new Label("N ^"){pickingMode=PickingMode.Ignore};north.style.position=Position.Absolute;north.style.left=4;north.style.top=2;north.style.fontSize=12;north.style.color=Color.white;north.style.backgroundColor=new Color(0,.02f,.04f,.75f);mapHost.Add(north);
            mapHost.tooltip="World map - north up - yellow arrow: camera heading - yellow outline: camera view - click to focus";
            mapHost.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;var p=(Vector2)e.localPosition-mapHost.contentRect.position;fixture.Rig.SetFocusDirection(MapDirection(new Vector2(Mathf.Clamp01(p.x/mapHost.contentRect.width),Mathf.Clamp01(p.y/mapHost.contentRect.height))));e.StopPropagation();});

        }

        void RefreshMap()

        {

            if(mapDrawing==null||mapHost.contentRect.width<1)return;mapDrawing.Lines.Clear();

            Vector2 Point(Vector3 direction){var uv=MapUv(direction);return new Vector2(uv.x*mapHost.contentRect.width,uv.y*mapHost.contentRect.height);}
            void Line(Vector2 a,Vector2 b,Color colour){
                float width=mapHost.contentRect.width;
                // Unwrap across the date line, then draw its copy at the opposite edge.
                if(b.x-a.x>width*.5f)b.x-=width;else if(b.x-a.x<-width*.5f)b.x+=width;
                mapDrawing.Lines.Add((a,b,colour));
                if(a.x<0||b.x<0)mapDrawing.Lines.Add((a+Vector2.right*width,b+Vector2.right*width,colour));
                else if(a.x>width||b.x>width)mapDrawing.Lines.Add((a-Vector2.right*width,b-Vector2.right*width,colour));
            }

            void Mark(Vector2 p,Color color,float r){mapDrawing.Lines.Add((p+Vector2.left*r,p+Vector2.right*r,color));mapDrawing.Lines.Add((p+Vector2.up*r,p+Vector2.down*r,color));}

            foreach(var building in world.Buildings)Mark(Point(CubeSphere.Decode(building.position)),Color.cyan,2);
            var rig=fixture.Rig;var focus=rig.FocusDirection.normalized;var centre=Point(focus);Mark(centre,Color.yellow,4);
            // A fixed-size heading arrow stays legible when a street-level view is smaller than one atlas pixel.
            var heading=Vector3.ProjectOnPlane(rig.transform.forward,focus).normalized;
            if(heading.sqrMagnitude<.1f)heading=Vector3.ProjectOnPlane(rig.transform.up,focus).normalized;
            var delta=Point((focus+heading*.002f).normalized)-centre;
            float mapWidth=mapHost.contentRect.width;if(delta.x>mapWidth*.5f)delta.x-=mapWidth;else if(delta.x<-mapWidth*.5f)delta.x+=mapWidth;
            var arrow=delta.normalized;
            var tip=centre+arrow*18;var side=new Vector2(-arrow.y,arrow.x);
            Line(centre,tip,Color.yellow);Line(tip,tip-arrow*6+side*4,Color.yellow);Line(tip,tip-arrow*6-side*4,Color.yellow);
            // Trace the viewport against a reference sphere, without terrain queries or unit readback.
            var camera=rig.GetComponent<Camera>();var origin=fixture.ObserverPosition.RelativeTo(fixture.definition.position).Float;
            float radius=fixture.definition.radius+Mathf.Max(fixture.definition.oceanHeight,rig.Surface.SampleHeight(focus));
            bool previousHit=false;Vector2 previous=default;
            for(int i=0;i<=64;i++){
                float edge=(i%64)/16f;Vector2 uv=edge<1?new Vector2(edge,0):edge<2?new Vector2(1,edge-1):edge<3?new Vector2(3-edge,1):new Vector2(0,4-edge);
                var direction=camera.ViewportPointToRay(new Vector3(uv.x,uv.y,0)).direction;
                double b=(double)origin.x*direction.x+(double)origin.y*direction.y+(double)origin.z*direction.z,c=(double)origin.x*origin.x+(double)origin.y*origin.y+(double)origin.z*origin.z-(double)radius*radius,discriminant=b*b-c;
                bool hit=discriminant>=0&&-b-System.Math.Sqrt(System.Math.Max(0,discriminant))>=0;
                if(hit){var point=(origin+direction*(float)(-b-System.Math.Sqrt(discriminant))).normalized;var pixel=Point(point);
                    if(previousHit)Line(previous,pixel,Color.yellow);previous=pixel;}
                previousHit=hit;
            }
            mapDrawing.MarkDirtyRepaint();

        }

        Button Card(VisualElement host,string kind,string id,string name,string cost,Action action,string controlName)

        {

            var card=cardTemplate.CloneTree();host.Add(card);var button=card.Q<Button>("card");button.name=controlName;button.clicked+=action;button.userData=action;

            card.Q<Label>("name").text=name;card.Q<Label>("cost").text=cost;card.Q<Image>("portrait").image=art?.Find(kind,id)?.portrait;return button;

        }

        void Context()

        {

            context.Clear();queueStatus=null;var building=world.Selected;context.style.display=building==null?DisplayStyle.None:DisplayStyle.Flex;layout.Actions["context-reopen"].SetEnabled(building!=null);if(building==null)return;

            Label(context,building.definition.name.ToUpperInvariant(),"heading").name="planet-building-name";Label(context,building.definition.description,"body");if(world.Civilisation!=null){Label(context,"DISTRICT · "+building.definition.district,"muted");HouseholdContext(context,building);if(building.roadSuspended)Label(context,"Road access blocked · suspended while another route is planned","muted");}

            var portrait=new Image{image=art?.Find("building",building.definition.id)?.portrait,scaleMode=ScaleMode.ScaleToFit};portrait.AddToClassList("portrait-large");context.Add(portrait);

            queueStatus=Label(context,"","muted");queueStatus.name="planet-building-status";if(building.awaitingProjectMaterials)queueStatus.text="REBUILD PROJECT: waiting for delivered construction supplies";
            if(building.team!=0||fixture.sessionMode==PlanetGameMode.AiTest){Label(context,"AI faction "+(building.team+1)+" · autonomous base","muted");return;}
            if(building.definition.id=="command"){
                var autoPilot=Button(context,world.AutoPilotEnabled?"AUTO PILOT: ON":"AUTO PILOT: OFF",()=>world.SetAutoPilot(!world.AutoPilotEnabled),"planet-auto-pilot");
                autoPilot.SetEnabled(building.Complete&&fixture.StrategyEnabled);
                autoPilot.tooltip="Let the AI build and supply your base, research technology, train units and repair buildings using your resources. You can still issue orders. Switching off stops new AI orders; existing work continues.";
            }

            var row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,"UPGRADE",()=>world.Upgrade(building),"planet-upgrade");Button(row,building.paused?"RESUME":"PAUSE",()=>{building.paused=!building.paused;revision=-1;},"planet-pause");

            row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,building.repeat?"REPEAT ON":"REPEAT OFF",()=>{building.repeat=!building.repeat;world.NotifyChanged();});Button(row,building.repairing?"REPAIR ON":"REPAIR",()=>{building.repairing=!building.repairing;world.NotifyChanged();});

            row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,building.powered?"POWER DOWN":"POWER UP",()=>{building.powered=!building.powered;world.NotifyChanged();});Button(row,"SELL 45%",()=>world.Sell(building));if(building.Upgrading)Button(context,"CANCEL UPGRADE",()=>world.CancelUpgrade(building));

            LaunchControls(context,building);
            if(building.definition.id=="gate")Button(context,building.gateOpen?"CLOSE GATE":"OPEN GATE",()=>world.SetGate(building,!building.gateOpen),"gate-toggle");
            if(building.definition.id=="silo")Button(context,"LAUNCH NUKE · 100F / 25I",()=>{fixture.NuclearTargetArmed=true;world.Notice="Click a target within silo range · right-click cancels";},"planet-launch-nuke");

            var scroll=new ScrollView();scroll.style.flexGrow=1;context.Add(scroll);
            if(world.Civilisation!=null){
                Button(scroll,"CONNECT TO WAREHOUSE",()=>world.ConnectSelectedToWarehouse(),"civilisation-connect");
                foreach(var recipe in world.Civilisation.Catalogue.recipes)if(recipe.building==building.definition.id){var selectedRecipe=recipe;Button(scroll,"PRODUCE "+recipe.name,()=>{world.Civilisation.SetRecipe(fixture.definition.bodyId,building,selectedRecipe.id,0);world.Notice="Recipe selected: "+selectedRecipe.name;},"recipe-"+recipe.id);}
                if(building.definition.id=="light_depot"||building.definition.id=="helipad")Button(scroll,building.definition.id=="helipad"?"MANUFACTURE AIR CARGO":"MANUFACTURE CARGO TRUCK",()=>{bool ok=world.Civilisation.Logistics.Order(world,building.team,building.definition.id=="helipad");world.Notice=ok?"Cargo vehicle ordered":"Requires connected production and construction materials";},"civilisation-hauler");
            }

            foreach(string id in building.definition.production){var d=world.Catalog.Units[id];var button=Card(scroll,"unit",id,d.name,d.cost+" • "+d.time+"s",()=>{if(!world.QueueUnit(building,d.id))world.Notice="Training unavailable: check construction, technology, level or resources";},"train-"+id);

                button.SetEnabled(building.Complete&&world.Faction.HasTech(d.requiredTech)&&building.level>=d.producerLevel);if(!world.Faction.HasTech(d.requiredTech))button.tooltip="Requires "+d.requiredTech;}

            foreach(var technology in world.Catalog.technologies.Where(t=>t.requiredBuilding==building.definition.id)){var d=technology;var button=Card(scroll,"technology",d.id,d.name,d.cost+" • "+d.time+"s",()=>{if(!world.Research(building,d.id))world.Notice="Research requirements are not met";},"research-"+d.id);

                button.SetEnabled(!world.Faction.HasTech(d.id)&&!world.Faction.researching.Contains(d.id)&&building.Complete&&building.level>=d.requiredBuildingLevel&&d.prerequisites.All(world.Faction.HasTech));}

            if(building.queue.Count>0)Label(scroll,"PRODUCTION QUEUE","subheading");for(int i=0;i<building.queue.Count;i++){int at=i;var order=building.queue[i];Button(scroll,(order.research?"RESEARCH ":"TRAIN ")+order.id+" • CANCEL",()=>world.CancelQueue(building,at),"cancel-queue-"+i);}

        }

        void Refresh()

        {

            if(front==null)return;front.Q("hud").style.display=fixture.IsFirstPerson||fixture.IsPiloting?DisplayStyle.None:DisplayStyle.Flex;
            RefreshCivilisation();

            if(Time.realtimeSinceStartupAsDouble<nextRefresh&&revision==world.Revision)return;nextRefresh=Time.realtimeSinceStartupAsDouble+.1;
            RefreshTechnology();RefreshResourceHover();

            for(int i=0;i<resources.Count;i++)resources[i].text=$"{world.Faction.wallet.Stock[i]:0} / {world.Faction.wallet.Capacity[i]:0}";

            layout.Value("fps",$"{1/Mathf.Max(Time.smoothDeltaTime,.0001f):0} FPS · TICK {fixture.SimulationTicks} · {fixture.SimulationSpeedLabel}"+(fixture.Paused?" · PAUSED":""));layout.Value("faction","PLANET COMMAND");layout.Value("outcome",world.Outcome);

            selection.text=$"{fixture.Commands.SelectedCount} SELECTED • {fixture.unitCount} ACTIVE\n"+fixture.Commands.Status;notice.text=world.Notice;notice.tooltip=world.Notice;power.text=$"POWER {world.Faction.generation:0} / {world.Faction.demand:0} • EFFICIENCY {world.Faction.efficiency:P0}";

            if(revision!=world.Revision){revision=world.Revision;Context();sharedConstruction?.Refresh(world.Catalog,world.Faction,buildCategory);}

            foreach(string id in new[]{"notice","placement","outcome"})layout.Labels[id].style.display=string.IsNullOrEmpty(layout.Labels[id].text)?DisplayStyle.None:DisplayStyle.Flex;

            RefreshMap();

            var b=world.Selected;if(b!=null&&queueStatus!=null)queueStatus.text=b.Complete?$"LEVEL {b.level} • HEALTH {b.health:0} / {b.MaxHealth:0}\n"+(world.Civilisation==null?"":$"WORKERS {b.WorkersAssigned}/{b.definition.workers*b.level} · STAFFING {b.WorkforceEfficiency:P0}\n")+(b.queue.Count==0?"Queue empty":$"{b.queue[0].id}: {b.queue[0].Fraction:P0}"):b.awaitingProjectMaterials?"REBUILD PROJECT: waiting for delivered construction supplies":$"CONSTRUCTION {b.construction/b.definition.time:P0}";

        }

        void SetConstructionOpen(bool open){
            buildOpen=open;var drawer=front.Q("construction");drawer.style.display=DisplayStyle.Flex;drawer.EnableInClassList("drawer-open",open);if(open)drawer.SetEnabled(true);
            if(constructionTab!=null){constructionTab.text="B\nU\nI\nL\nD\n"+(open?"<":">");constructionTab.EnableInClassList("active",open);}
        }
        void UpdateConstructionDrawer(bool immediate=false){
            var drawer=front?.Q("construction");if(drawer==null)return;float width=drawer.resolvedStyle.width;if(float.IsNaN(width)||width<1)width=365;
            float target=buildOpen?40:-width-8;drawerLeft=immediate?target:Mathf.MoveTowards(drawerLeft,target,Time.unscaledDeltaTime*1800);
            if(drawer.style.left.value.value!=drawerLeft)drawer.style.left=drawerLeft;
            if(!buildOpen&&drawerLeft==target)drawer.SetEnabled(false);
        }
        public void ShowBuildingCategory(BuildingCategory category){SetConstructionOpen(true);buildCategory=(int)category;sharedConstruction?.Refresh(world.Catalog,world.Faction,buildCategory);}

        public void Tick(){ui.Tick();UpdateConstructionDrawer();var k=UnityEngine.InputSystem.Keyboard.current;if(k==null||TextFocused||fixture.IsFirstPerson||fixture.IsPiloting)return;if(k.escapeKey.wasPressedThisFrame){if(technologyOpen){technologyOpen=false;ShowTechnology();}else if(civilisationOpen)ToggleCivilisation();else if(buildOpen)Dispatch("construct");return;}if(k.bKey.wasPressedThisFrame)Dispatch("construct");if(k.tKey.wasPressedThisFrame)Dispatch("tech");if(k.spaceKey.wasPressedThisFrame)Dispatch("pause-game");if(k.mKey.wasPressedThisFrame)Dispatch("map");}

        public void Dispose(){ui.Dispose();ArmyLifetime.Destroy(map);}

    }

}


