using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy
{
    public sealed partial class BattleApp
    {
        string modalType="";
        Label techReason,techDetails,atlasDetails;
        Button researchButton;
        VisualElement techDetailHost;
        public string ToolkitDiagnostics=>ui?.Diagnostics??"UI not initialized";
        void RefreshModal()
        {
            string type=settingsOpen?"settings":biomeOpen?"atlas":techOpen&&Base!=null?"tech":"";
            Visible(modalUi,type.Length>0);
            string key=type=="tech"?type+":"+AvailabilityKey()+":"+string.Join(",",Base.Factions[0].researching):type;
            if(key!=modalKey){modalKey=key;modalType=type;modalUi.Clear();if(type.Length>0){var box=Box(modalUi,"modal panel");if(type=="settings")MakeSettings(box);else if(type=="atlas")MakeAtlas(box);else MakeTechGraph(box);}}
            if(type=="tech")RefreshTechDetails();
        }
        void ModalTitle(VisualElement parent,string title,Action close)
        {
            var row=Box(parent,"row");row.style.flexShrink=0;L(row,title,"heading grow");B(row,"CLOSE",close,"cancel");
        }
        void MakeSettings(VisualElement parent)
        {
            ModalTitle(parent,"SETTINGS",()=>SetSettingsOpen(false));var scroll=new ScrollView();parent.Add(scroll);
            var scale=new Slider("UI scale",.85f,1.25f){value=ui.Scale,showInputField=true};scale.AddToClassList("setting-row");scale.RegisterValueChangedCallback(e=>ui.SetScale(e.newValue));scroll.Add(scale);
            var render=new Slider("Graphics render scale",.5f,1){value=renderQuality.Scale,showInputField=true};render.AddToClassList("setting-row");render.RegisterValueChangedCallback(e=>renderQuality.Scale=e.newValue);scroll.Add(render);
            var camera=new Slider("Camera sensitivity",.25f,2){value=rig.Sensitivity,showInputField=true};camera.AddToClassList("setting-row");camera.RegisterValueChangedCallback(e=>rig.Sensitivity=e.newValue);scroll.Add(camera);
            var vsync=new Toggle("VSync"){value=QualitySettings.vSyncCount>0};vsync.AddToClassList("setting-row");vsync.RegisterValueChangedCallback(e=>QualitySettings.vSyncCount=e.newValue?1:0);scroll.Add(vsync);
            var shadows=new Toggle("Realtime shadows"){value=settings.shadows};shadows.AddToClassList("setting-row");shadows.RegisterValueChangedCallback(e=>settings.shadows=e.newValue);scroll.Add(shadows);
            L(scroll,"SIMULATION & STRESS ISOLATION","subheading");
            string[] names={"movement","navigation","spatialGrid","targeting","combat","projectiles","selection","terrain","trees","vfx","meshes","impostors","crowdPressure","enemyAI","simulationLod"};
            string[] captions={"Movement","Shared navigation","Spatial grid (off disables avoidance and new targeting)","Target acquisition","Weapon firing","Gameplay shells (off uses direct damage)","GPU selection","Terrain, freshwater and ocean","Biome vegetation and rocks","Combat effects and remains","3D tank tiers","Far impostors","Crowd density pressure","Enemy army strategy","Distant peaceful movement LOD"};
            for(int i=0;i<names.Length;i++){var field=typeof(BattleSettings).GetField(names[i]);var toggle=new Toggle(captions[i]){value=(bool)field.GetValue(settings)};if(settings.spectatorMode&&names[i]=="enemyAI"){toggle.SetValueWithoutNotify(true);toggle.SetEnabled(false);toggle.tooltip="Both AI controllers run in Test mode";}toggle.AddToClassList("setting-row");toggle.RegisterValueChangedCallback(e=>field.SetValue(settings,e.newValue));scroll.Add(toggle);}
            var seedInput=new IntegerField("Terrain seed (next battle)"){value=settings.seed,isDelayed=true};seedInput.AddToClassList("setting-row");seedInput.RegisterValueChangedCallback(e=>{settings.world.seed=settings.seed=e.newValue;InvalidateWorldPreview();});scroll.Add(seedInput);B(scroll,"NEW SEED (NEXT BATTLE)",()=>{settings.world.seed=settings.seed=unchecked(settings.seed*1664525+1013904223)&int.MaxValue;seedInput.SetValueWithoutNotify(settings.seed);InvalidateWorldPreview();},"repeat");
            B(parent,"SAVE & CLOSE",()=>{PlayerPrefs.SetInt("Army.Shadows",settings.shadows?1:0);PlayerPrefs.SetFloat("Army.UiScale",ui.Scale);PlayerPrefs.SetInt("Army.VSync",QualitySettings.vSyncCount);PlayerPrefs.SetFloat("Army.Camera",rig.Sensitivity);PlayerPrefs.SetFloat("Army.RenderScale",renderQuality.Scale);PlayerPrefs.SetInt("Army.Seed",settings.seed);PlayerPrefs.Save();SetSettingsOpen(false);},"confirm","primary");
        }
        void MakeAtlas(VisualElement parent)
        {
            parent.style.width=1480;ModalTitle(parent,"TACTICAL ATLAS · NORTH ↑",()=>biomeOpen=false);var row=Box(parent,"row");var map=Box(row,"map-atlas");atlasImage=new Image{image=mapTerrain?field.Minimap:field.BiomeMap,scaleMode=ScaleMode.ScaleToFit};atlasImage.StretchToParentSize();map.Add(atlasImage);
            var details=Box(row,"grow");L(details,$"SEED {field.Map.Seed}\n{field.Size:N0} × {field.Size:N0} m · {field.Resolution}² cells","heading");var legend=new ScrollView();legend.style.height=440;details.Add(legend);for(int i=0;i<Biomes.Names.Length;i++){var entry=Box(legend,"row");var colour=Box(entry);colour.style.width=18;colour.style.height=14;colour.style.marginRight=10;colour.style.backgroundColor=Biomes.Colours[i];L(entry,Biomes.Names[i],"body");}
            atlasDetails=L(details,"Hover for biome, climate and water depth.\nClick the map to focus the camera.","body");
            map.RegisterCallback<PointerMoveEvent>(e=>{Vector2 uv=(Vector2)e.localPosition/map.contentRect.size;int index=field.Map.Index(new Vector2((uv.x-.5f)*field.Size,(.5f-uv.y)*field.Size));atlasDetails.text=$"{Biomes.Names[(int)field.Map.Biome[index]]} · elevation {field.Map.Height[index]:F0} m\nTemperature {field.Map.Temperature[index]:P0} · moisture {field.Map.Moisture[index]:P0}\nWater depth {field.Map.WaterData(index).g:F1} m";});
            map.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;NavigateUiMap(map,e.localPosition,true);e.StopPropagation();});
            var buttons=Box(parent,"row");B(buttons,"TERRAIN / BIOMES",()=>{mapTerrain=!mapTerrain;atlasImage.image=mapTerrain?field.Minimap:field.BiomeMap;},"map");B(buttons,"EXPORT MAPS",()=>{try{string folder=Path.Combine(Application.persistentDataPath,"BiomeMaps");settings.preview.Export(folder);atlasDetails.text="Saved in "+folder;}catch(Exception e){atlasDetails.text=e.Message;}},"save");
        }
        void MakeTechGraph(VisualElement parent)
        {
            parent.style.width=1250;ModalTitle(parent,"TECHNOLOGY DEPENDENCY GRAPH",()=>techOpen=false);
            var scroll=new ScrollView(ScrollViewMode.VerticalAndHorizontal);scroll.style.height=530;parent.Add(scroll);var canvas=Box(scroll,"tech-canvas");var drawing=new RtsUiDrawing();drawing.StretchToParentSize();canvas.Add(drawing);
            var positions=new System.Collections.Generic.Dictionary<string,Rect>();
            for(int tier=2;tier<=5;tier++){int row=0;var title=L(canvas,"ERA "+tier,"heading");title.style.position=Position.Absolute;title.style.left=12+(tier-2)*260;title.style.top=2;foreach(var tech in Base.Catalog.technologies.Where(t=>t.tier==tier))positions[tech.id]=new Rect(10+(tier-2)*260,44+row++*102,230,78);}
            foreach(var tech in Base.Catalog.technologies)foreach(string dep in tech.prerequisites){Rect a=positions[dep],b=positions[tech.id];Vector2 from=new Vector2(a.xMax,a.center.y),to=new Vector2(b.xMin,b.center.y);if(a.x==b.x){from=new Vector2(a.xMax-15,a.yMax);to=new Vector2(b.xMax-15,b.yMin);}float elbow=(from.x+to.x)*.5f;Color colour=Base.Factions[0].HasTech(dep)?Cyan:new Color(.18f,.32f,.4f);drawing.Lines.Add((from,new Vector2(elbow,from.y),colour));drawing.Lines.Add((new Vector2(elbow,from.y),new Vector2(elbow,to.y),colour));drawing.Lines.Add((new Vector2(elbow,to.y),to,colour));}
            foreach(var tech in Base.Catalog.technologies){bool done=Base.Factions[0].HasTech(tech.id),pending=Base.Factions[0].researching.Contains(tech.id);var b=B(canvas,tech.name+"\n"+(done?"COMPLETE":pending?"RESEARCHING":tech.branch+" · "+tech.time+"s"),()=>{selectedTechnology=tech.id;RefreshTechDetails();},"tech-"+tech.id,"tech-node");Rect r=positions[tech.id];b.style.left=r.x;b.style.top=r.y;b.EnableInClassList("complete",done);b.EnableInClassList("pending",pending);b.name="tech-"+tech.id;}
            drawing.MarkDirtyRepaint();techDetailHost=Box(parent);techDetails=L(techDetailHost,"","body");techReason=L(techDetailHost,"","muted");var actions=Box(parent,"row");B(actions,"SELECT LABORATORY",()=>{var t=Base.Catalog.Technologies[selectedTechnology];SelectedBuilding=Base.Buildings.FirstOrDefault(b=>!b.removed&&b.team==0&&b.Complete&&b.definition.id==t.requiredBuilding&&b.level>=t.requiredBuildingLevel);},"research");researchButton=B(actions,"RESEARCH",()=>{if(SelectedBuilding!=null)Base.EnqueueResearch(SelectedBuilding,selectedTechnology);},"confirm","primary");
        }
        void RefreshTechDetails()
        {
            if(techDetails==null||Base==null)return;var selected=Base.Catalog.Technologies[selectedTechnology];var f=Base.Factions[0];
            string requires=selected.prerequisites.Length==0?"Command era "+selected.tier:string.Join(" + ",selected.prerequisites.Select(id=>Base.Catalog.Technologies[id].name));
            var unlocks=selected.unlockUnits.Select(id=>Base.Catalog.Units[id].name).Concat(selected.unlockBuildings.Select(id=>Base.Catalog.Buildings[id].name)).Concat(selected.unlockAbilities).Concat(selected.unlockModifiers);
            techDetails.text=selected.name+" · "+selected.cost+" · "+selected.time+"s\n"+selected.description+"\nRequires: "+requires+" · "+Base.Catalog.Buildings[selected.requiredBuilding].name+" MK "+selected.requiredBuildingLevel+"\nUnlocks: "+string.Join(" · ",unlocks);
            string reason="Select an owned laboratory";bool available=SelectedBuilding!=null&&SelectedBuilding.team==0&&Base.CanResearch(SelectedBuilding,selected,out reason);researchButton.SetEnabled(available);
            techReason.text=f.HasTech(selected.id)?"Technology complete":f.researching.Contains(selected.id)?"Research queued":reason;
            foreach(var t in Base.Catalog.technologies)modalUi.Q<Button>("tech-"+t.id)?.EnableInClassList("active",t.id==selectedTechnology);
        }
        void RefreshSetup()
        {
            string key=setupGroup+":"+settings.world.preset+":"+settings.world.climate+":"+settings.world.GetHashCode()+":"+(previewImage==null?"none":previewImage.GetEntityId().ToString());
            if(key!=setupKey){setupKey=key;MakeSetup();}
            Value("setup-message",setupMessage);Value("setup-summary",$"Navigation {settings.world.Resolution}² · {settings.world.size/(float)settings.world.Resolution:0.0} m/cell\nArmy/navigation/wake buffers: {MemoryBudget.Bytes(new BattleSettings{world=settings.world,capacity=Scales[chosenScale],activeUnits=32,baseBuilding=true})/1048576.0:0.0} MiB (estimate)");
            bool busy=previewTask!=null&&!previewTask.IsCompleted;setupUi.Q<Button>("start-base")?.SetEnabled(settings.preview!=null&&!busy);setupUi.Q<Button>("start-stress")?.SetEnabled(settings.preview!=null&&!busy);Value("preview-button",busy?"CANCEL PREVIEW":"GENERATE PREVIEW");
        }
        void MakeSetup()
        {
            setupUi.Clear();ModalTitle(setupUi,"WRAPPING WORLD SETUP",()=>worldSetupOpen=false);var cfg=settings.world;var row=Box(setupUi,"row grow");row.style.minHeight=0;row.style.alignItems=Align.Stretch;var fieldScroll=new ScrollView(ScrollViewMode.Vertical){horizontalScrollerVisibility=ScrollerVisibility.Hidden};fieldScroll.contentContainer.style.width=Length.Percent(100);fieldScroll.AddToClassList("setup-fields");row.Add(fieldScroll);var fields=fieldScroll.contentContainer;
            var layout=new EnumField("Layout",cfg.preset);layout.AddToClassList("setting-row");layout.RegisterValueChangedCallback(e=>{cfg.ApplyPreset((WorldPreset)e.newValue);InvalidateWorldPreview();});fields.Add(layout);
            var climate=new EnumField("Climate",cfg.climate);climate.AddToClassList("setting-row");climate.RegisterValueChangedCallback(e=>{cfg.ApplyClimate((RegionalClimate)e.newValue);InvalidateWorldPreview();});fields.Add(climate);
            var sizes=new System.Collections.Generic.List<string>{"2,048 m","4,096 m","8,192 m","16,384 m"};var size=new DropdownField("Size",sizes,Mathf.Max(0,Array.IndexOf(new[]{2048,4096,8192,16384},cfg.size)));size.AddToClassList("setting-row");size.RegisterValueChangedCallback(e=>{cfg.size=new[]{2048,4096,8192,16384}[size.index];InvalidateWorldPreview();});fields.Add(size);
            var seed=new IntegerField("Seed"){value=cfg.seed,isDelayed=true};seed.AddToClassList("setting-row");seed.RegisterValueChangedCallback(e=>{cfg.seed=settings.seed=e.newValue;InvalidateWorldPreview();});fields.Add(seed);B(fields,"REROLL",()=>{cfg.seed=settings.seed=unchecked(cfg.seed*1664525+1013904223)&int.MaxValue;seed.SetValueWithoutNotify(cfg.seed);InvalidateWorldPreview();},"repeat");
            var mode=new DropdownField("Game mode",new System.Collections.Generic.List<string>{"Regular RTS","Swarm survival","AI test / simulated run"},settings.spectatorMode?2:settings.swarmMode?1:0){name="setup-game-mode"};mode.AddToClassList("setting-row");fields.Add(mode);
            var modeInfo=L(fields,"","muted");
            Action refreshModeInfo=()=>modeInfo.text=settings.spectatorMode?"Both factions build, research, expand and fight each other and the zombies. You spectate with full map visibility. AI profiles use the main menu selections.":settings.swarmMode?"Player RTS with ambient zombies and escalating survival waves.":"Player RTS against the enemy AI, with ambient zombies.";
            mode.RegisterValueChangedCallback(e=>{settings.spectatorMode=mode.index==2;settings.swarmMode=mode.index==1;refreshModeInfo();});refreshModeInfo();
            var tabs=Box(fields,"wrap setup-tabs");for(int i=0;i<Groups.Length;i++){int group=i;var button=B(tabs,Groups[i],()=>setupGroup=group);button.EnableInClassList("active",setupGroup==group);}
            var scroll=new ScrollView();scroll.style.height=300;fields.Add(scroll);foreach(string name in Controls[setupGroup]){var field=typeof(WorldGenerationSettings).GetField(name);var range=field.GetCustomAttribute<RangeAttribute>();string caption=System.Text.RegularExpressions.Regex.Replace(name,"([A-Z])"," $1");var slider=new Slider(caption,range.min,range.max){value=Convert.ToSingle(field.GetValue(cfg)),showInputField=true};slider.AddToClassList("setting-row");slider.SetEnabled(!(name.StartsWith("island")&&cfg.oceanCoverage==0));slider.RegisterValueChangedCallback(e=>{float value=Mathf.Round(e.newValue);field.SetValue(cfg,field.FieldType==typeof(int)?(object)(int)value:value);cfg.preset=WorldPreset.Custom;InvalidateWorldPreview();});scroll.Add(slider);}
            L(fields,"","muted","setup-summary");L(fields,"","body","setup-message");var buttons=Box(fields,"row");var preview=B(buttons,"",()=>{if(previewTask!=null&&!previewTask.IsCompleted)previewCancellation.Cancel();else BeginWorldPreview();},"map");L(preview,"GENERATE PREVIEW","card-name","preview-button");var start=B(buttons,"START MATCH",()=>{worldSetupOpen=false;if(settings.spectatorMode)StartTestBattle(Scales[chosenScale]);else if(settings.swarmMode)StartSwarmBattle(Scales[chosenScale]);else StartBaseBattle(Scales[chosenScale]);},"confirm","primary");start.name="start-base";
            var bottom=Box(fields,"row");var stress=B(bottom,"STRESS "+Scales[chosenScale].ToString("N0"),()=>{worldSetupOpen=false;settings.capacity=Scales[chosenScale];StartBattle(Scales[chosenScale]);},"units");stress.name="start-stress";
            var mapHost=Box(row,"grow");if(previewImage!=null){var map=Box(mapHost,"setup-preview");var image=new Image{image=previewImage,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.StretchToParentSize();map.Add(image);var drawing=new RtsUiDrawing();drawing.StretchToParentSize();map.Add(drawing);map.RegisterCallback<GeometryChangedEvent>(e=>{drawing.Lines.Clear();foreach(var node in settings.preview.Resources)SetupMarker(drawing,node.position,new Color(1,.8f,.2f),4);for(int i=0;i<settings.preview.Starts.Length;i++)SetupMarker(drawing,settings.preview.Starts[i],i==0?Cyan:Red,12);drawing.MarkDirtyRepaint();});L(mapHost,$"Ocean {settings.preview.OceanPercent:0.0}% · lakes {settings.preview.LakePercent:0.0}%\n{settings.preview.LandComponents} land components · forest {settings.preview.ForestPercent:0.0}%\nRivers {settings.preview.RiverLength/1000:0.0} km · roads {settings.preview.RoadLength/1000:0.0} km","body");}
            else{Portrait(mapHost,"building-command","hero");L(mapHost,"Generate a preview to validate terrain, water, deposits and start sites.\nCyan / red: starts · gold: deposits","body");}
            var export=Box(setupUi,"row");export.style.flexShrink=0;if(presetPath.Length==0)presetPath=Path.Combine(Application.persistentDataPath,"world-preset.json");var path=new TextField("Preset file"){value=presetPath,isDelayed=true};path.AddToClassList("grow");path.RegisterValueChangedCallback(e=>presetPath=e.newValue);export.Add(path);B(export,"EXPORT",()=>{try{File.WriteAllText(presetPath,JsonUtility.ToJson(cfg,true));setupMessage="Saved "+presetPath;}catch(Exception e){setupMessage=e.Message;}},"save");B(export,"IMPORT",()=>{try{var imported=JsonUtility.FromJson<WorldGenerationSettings>(File.ReadAllText(presetPath));imported.Validate();settings.world=imported;settings.seed=imported.seed;InvalidateWorldPreview();}catch(Exception e){setupMessage=e.Message;}},"map");
        }
        void SetupMarker(RtsUiDrawing drawing,Vector2 position,Color colour,float diameter){Vector2 uv=position/settings.world.size+Vector2.one*.5f,size=drawing.contentRect.size,p=new Vector2(uv.x*size.x,(1-uv.y)*size.y);drawing.Lines.Add((p-Vector2.right*diameter*.5f,p+Vector2.right*diameter*.5f,colour));}
    }
}
