using System;

using System.Collections.Generic;

using System.Linq;

using UnityEngine;

using UnityEngine.UIElements;

namespace MassiveArmy.Planetary

{

    /// <summary>The production Front layout, theme, portraits and panel lifecycle with a planet backend.</summary>

    public sealed partial class PlanetRtsHud : IDisposable

    {

        readonly PlanetDevelopment fixture;readonly PlanetRtsBase world;readonly RtsUiController ui;

        readonly RtsUiArt art;readonly VisualTreeAsset cardTemplate,resourceTemplate;

        readonly List<Label> resources=new List<Label>();VisualElement front,context;Label selection,notice,power,queueStatus;

        RtsConstructionPanel sharedConstruction;int buildCategory;bool buildOpen=true;int revision=-1;double nextRefresh;

        RtsHudLayout layout;bool technologyOpen;

        Texture2D map;RtsUiDrawing mapDrawing;VisualElement mapHost;

        public VisualElement Root=>ui.Root;

        public bool Blocks(Vector2 point)=>ui.Blocks(point);

        public bool ModalOpen=>technologyOpen||civilisationOpen;

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

            front=root.Q("front");foreach(string id in new[]{"menu","loading","setup","modal","tooltip","developer","scout","overlay"})front.Q(id).style.display=DisplayStyle.None;

            front.Q("hud").style.display=DisplayStyle.Flex;resources.Clear();revision=-1;

            foreach(string id in new[]{"top","bottom","construction","context","quick-actions"})ui.Block(front.Q(id));

            layout=new RtsHudLayout(front,Dispatch);layout.Value("selection-help","SHIFT add · CTRL remove\nRight-click orders");layout.Value("camera-help-text","WASD / MIDDLE DRAG\nWHEEL ZOOM\nF: WALK / INTERACT · TAB: RTS · H: SHIP · N: NUKE\nAI TEST: 1 / 2 VIEW FACTION");resources.Clear();for(int i=0;i<5;i++)resources.Add(layout.Labels["resource-"+i]);

            power=layout.Labels["power"];selection=layout.Labels["selected"];notice=layout.Labels["notice"];

            foreach(string id in new[]{"attack","load","unload","carrier","scout","supply-toggle","benchmark"}){layout.Actions[id].SetEnabled(false);layout.Actions[id].tooltip="This gameplay subsystem is still being migrated to planetary navigation.";}

            front.Q("scout").style.display=DisplayStyle.Flex;front.Q("developer").style.display=DisplayStyle.None;

            ui.Block(front.Q("scout"));ui.Block(front.Q("modal"));

            Label(front.Q("camera-help"),"F: first person • R: rotate blueprint","muted");

            var construction=front.Q("construction");sharedConstruction=new RtsConstructionPanel(construction,art,cardTemplate,Resources.Load<Texture2D>("MassiveArmy/UI/ControlHighlight"),world.BeginPlacement,index=>ShowBuildingCategory((BuildingCategory)index),()=>{buildOpen=false;construction.style.display=DisplayStyle.None;});sharedConstruction.Refresh(world.Catalog,world.Faction,buildCategory);

            if(fixture.sessionMode==PlanetGameMode.AiTest){construction.SetEnabled(false);front.Q("commands").SetEnabled(false);front.Q("quick-actions").SetEnabled(false);}

            MakeMap();context=front.Q("context");Refresh();
            if(world.Civilisation!=null){var toolbar=new VisualElement{name="settlement-toolbar"};toolbar.style.position=Position.Absolute;toolbar.style.right=12;toolbar.style.top=78;front.Q("hud").Add(toolbar);ui.Block(toolbar);Button(toolbar,"SETTLEMENTS",ToggleCivilisation,"civilisation-open");Button(toolbar,"ROADS",()=>world.BeginRoadPlacement(),"civilisation-roads").SetEnabled(fixture.sessionMode!=PlanetGameMode.AiTest);}

        }

        void Dispatch(string id)

        {

            switch(id){

                case "move":fixture.AttackMoveArmed=false;break;

                case "attack-move":fixture.AttackMoveArmed=true;world.Notice="Click terrain to attack-move with the selected army";break;

                case "stop":fixture.AttackMoveArmed=false;fixture.Commands.Stop();break;

                case "pause-game":fixture.Paused=!fixture.Paused;break;

                case "menu":fixture.ReturnToMenu();break;

                case "construct":buildOpen=!buildOpen;front.Q("construction").style.display=buildOpen?DisplayStyle.Flex:DisplayStyle.None;break;

                case "tech":technologyOpen=!technologyOpen;ShowTechnology();break;

                case "map":fixture.Rig.Distance=fixture.definition.radius*2;break;

                case "context-reopen":Context();break;

                case "settings":world.Notice="Return to the main menu to change rendering settings.";break;

            }

        }

        void ShowTechnology()

        {

            var modal=front.Q("modal");modal.Clear();modal.style.display=technologyOpen?DisplayStyle.Flex:DisplayStyle.None;if(!technologyOpen)return;

            var panel=new VisualElement();panel.AddToClassList("panel");panel.style.width=1100;modal.Add(panel);Label(panel,"TECHNOLOGY DEPENDENCY GRAPH","heading");Button(panel,"CLOSE",()=>{technologyOpen=false;ShowTechnology();});

            var scroll=new ScrollView(ScrollViewMode.VerticalAndHorizontal);scroll.style.height=530;panel.Add(scroll);var canvas=new VisualElement();canvas.AddToClassList("tech-canvas");scroll.Add(canvas);var drawing=new RtsUiDrawing();drawing.StretchToParentSize();canvas.Add(drawing);

            var positions=new Dictionary<string,Rect>();for(int tier=2;tier<=5;tier++){int row=0;foreach(var tech in world.Catalog.technologies.Where(t=>t.tier==tier))positions[tech.id]=new Rect(10+(tier-2)*260,44+row++*102,230,78);}

            foreach(var tech in world.Catalog.technologies){var t=tech;var card=Button(canvas,t.name+"\n"+(world.Faction.HasTech(t.id)?"COMPLETE":t.cost.ToString()),()=>{var lab=world.Buildings.FirstOrDefault(b=>b.Complete&&b.definition.id==t.requiredBuilding&&b.level>=t.requiredBuildingLevel);if(lab==null)world.Notice="Requires "+t.requiredBuilding+" MK "+t.requiredBuildingLevel;else if(!world.Research(lab,t.id))world.Notice="Research requirements are not met";else ShowTechnology();},"tech-"+t.id);card.AddToClassList("tech-node");var rect=positions[t.id];card.style.left=rect.x;card.style.top=rect.y;card.EnableInClassList("complete",world.Faction.HasTech(t.id));card.SetEnabled(!world.Faction.HasTech(t.id)&&!world.Faction.researching.Contains(t.id));

                foreach(string dep in t.prerequisites){var a=positions[dep];var from=new Vector2(a.xMax,a.center.y);var to=new Vector2(rect.xMin,rect.center.y);drawing.Lines.Add((from,to,world.Faction.HasTech(dep)?Color.cyan:Color.gray));}}

        }

        void MakeMap()

        {

            if(map==null){const int n=64;map=new Texture2D(n*3,n*2,TextureFormat.RGB24,false){name="Planet six-face biome overview"};var pixels=new Color[n*n*6];var surface=new PlanetSurface(fixture.definition);

                for(int y=0;y<n*2;y++)for(int x=0;x<n*3;x++){var direction=CubeSphere.Direction(new PlanetNavCoordinate(fixture.definition.bodyId,(CubeFace)(x/n+3*(y/n)),x%n,y%n),n);pixels[x+y*n*3]=Biomes.Colours[(int)surface.SampleBiome(direction)];}map.SetPixels(pixels);map.Apply(false,true);}

            mapHost=front.Q("minimap");mapHost.Clear();var image=new Image{image=map,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};image.StretchToParentSize();mapHost.Add(image);mapDrawing=new RtsUiDrawing();mapDrawing.StretchToParentSize();mapHost.Add(mapDrawing);

            mapHost.tooltip="Six cube faces • click to focus the globe camera";mapHost.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;var p=e.localPosition;float x=Mathf.Clamp01(p.x/mapHost.contentRect.width)*3,y=(1-Mathf.Clamp01(p.y/mapHost.contentRect.height))*2;int column=Mathf.Min(2,(int)x),row=Mathf.Min(1,(int)y);fixture.Rig.SetFocusDirection(CubeSphere.Direction((CubeFace)(column+row*3),new Vector2((x-column)*2-1,(y-row)*2-1)));e.StopPropagation();});

        }

        void RefreshMap()

        {

            if(mapDrawing==null||mapHost.contentRect.width<1)return;mapDrawing.Lines.Clear();

            Vector2 Point(Vector3 direction){CubeSphere.Project(direction,out var face,out var uv);int f=(int)face;return new Vector2((f%3+uv.x*.5f+.5f)/3*mapHost.contentRect.width,(1-(f/3+uv.y*.5f+.5f)/2)*mapHost.contentRect.height);}

            void Mark(Vector2 p,Color color,float r){mapDrawing.Lines.Add((p+Vector2.left*r,p+Vector2.right*r,color));mapDrawing.Lines.Add((p+Vector2.up*r,p+Vector2.down*r,color));}

            foreach(var building in world.Buildings)Mark(Point(CubeSphere.Decode(building.position)),Color.cyan,2);Mark(Point(fixture.Rig.FocusDirection),Color.yellow,4);mapDrawing.MarkDirtyRepaint();

        }

        Button Card(VisualElement host,string kind,string id,string name,string cost,Action action,string controlName)

        {

            var card=cardTemplate.CloneTree();host.Add(card);var button=card.Q<Button>("card");button.name=controlName;button.clicked+=action;button.userData=action;

            card.Q<Label>("name").text=name;card.Q<Label>("cost").text=cost;card.Q<Image>("portrait").image=art?.Find(kind,id)?.portrait;return button;

        }

        void Context()

        {

            context.Clear();queueStatus=null;var building=world.Selected;if(building==null){Label(context,"SELECT A BUILDING","heading");return;}

            Label(context,building.definition.name.ToUpperInvariant(),"heading").name="planet-building-name";Label(context,building.definition.description,"body");

            var portrait=new Image{image=art?.Find("building",building.definition.id)?.portrait,scaleMode=ScaleMode.ScaleToFit};portrait.AddToClassList("portrait-large");context.Add(portrait);

            queueStatus=Label(context,"","muted");queueStatus.name="planet-building-status";
            if(building.team!=0||fixture.sessionMode==PlanetGameMode.AiTest){Label(context,"AI faction "+(building.team+1)+" · autonomous base","muted");return;}

            var row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,"UPGRADE",()=>world.Upgrade(building),"planet-upgrade");Button(row,building.paused?"RESUME":"PAUSE",()=>{building.paused=!building.paused;revision=-1;},"planet-pause");

            row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,building.repeat?"REPEAT ON":"REPEAT OFF",()=>{building.repeat=!building.repeat;world.NotifyChanged();});Button(row,building.repairing?"REPAIR ON":"REPAIR",()=>{building.repairing=!building.repairing;world.NotifyChanged();});

            row=new VisualElement();row.AddToClassList("row");context.Add(row);Button(row,building.powered?"POWER DOWN":"POWER UP",()=>{building.powered=!building.powered;world.NotifyChanged();});Button(row,"SELL 45%",()=>world.Sell(building));if(building.Upgrading)Button(context,"CANCEL UPGRADE",()=>world.CancelUpgrade(building));

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

            for(int i=0;i<resources.Count;i++)resources[i].text=$"{world.Faction.wallet.Stock[i]:0} / {world.Faction.wallet.Capacity[i]:0}";

            layout.Value("fps",$"{1/Mathf.Max(Time.smoothDeltaTime,.0001f):0} FPS · TICK {fixture.SimulationTicks}"+(fixture.Paused?" · PAUSED":""));layout.Value("faction","PLANET COMMAND");layout.Value("swarm",fixture.ModeStatus);layout.Value("outcome",world.Outcome);

            selection.text=$"{fixture.Commands.SelectedCount} SELECTED • {fixture.unitCount} ACTIVE\n"+fixture.Commands.Status;notice.text=world.Notice;power.text=$"POWER {world.Faction.generation:0} / {world.Faction.demand:0} • EFFICIENCY {world.Faction.efficiency:P0}";

            if(revision!=world.Revision){revision=world.Revision;Context();sharedConstruction?.Refresh(world.Catalog,world.Faction,buildCategory);}

            foreach(string id in new[]{"notice","placement","outcome"})layout.Labels[id].style.display=string.IsNullOrEmpty(layout.Labels[id].text)?DisplayStyle.None:DisplayStyle.Flex;

            RefreshMap();

            var b=world.Selected;if(b!=null&&queueStatus!=null)queueStatus.text=b.Complete?$"LEVEL {b.level} • HEALTH {b.health:0} / {b.MaxHealth:0}\n"+(b.queue.Count==0?"Queue empty":$"{b.queue[0].id}: {b.queue[0].Fraction:P0}"):$"CONSTRUCTION {b.construction/b.definition.time:P0}";

        }

        public void ShowBuildingCategory(BuildingCategory category){buildOpen=true;buildCategory=(int)category;front.Q("construction").style.display=DisplayStyle.Flex;sharedConstruction?.Refresh(world.Catalog,world.Faction,buildCategory);}

        public void Tick(){ui.Tick();var k=UnityEngine.InputSystem.Keyboard.current;if(k==null||TextFocused||fixture.IsFirstPerson||fixture.IsPiloting||fixture.sessionMode==PlanetGameMode.AiTest)return;if(k.bKey.wasPressedThisFrame)Dispatch("construct");if(k.tKey.wasPressedThisFrame)Dispatch("tech");if(k.spaceKey.wasPressedThisFrame)Dispatch("pause-game");if(k.mKey.wasPressedThisFrame)Dispatch("map");}

        public void Dispose(){ui.Dispose();ArmyLifetime.Destroy(map);}

    }

}


