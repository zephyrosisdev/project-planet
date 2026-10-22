using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public sealed class ModularBuildingWindow:EditorWindow {
        public const string Root="Assets/MassiveArmy/Art/ModularBuildings";
        [SerializeField]ModularBuildingProfile profile;UnityEditor.Editor inspector;Vector2 scroll;string message="";
        [MenuItem("Massive Army/Art/Modular buildings")]
        public static void Open(){var window=GetWindow<ModularBuildingWindow>("Modular Buildings");window.minSize=new Vector2(760,620);if(Selection.activeObject is ModularBuildingProfile p)window.Select(p);else if(window.profile==null)window.Select(AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(Root+"/Profiles/civil_housing.asset"));window.Show();window.Focus();}
        void OnDisable(){if(inspector!=null)DestroyImmediate(inspector);inspector=null;}
        void Select(ModularBuildingProfile p){profile=p;if(inspector!=null)DestroyImmediate(inspector);inspector=p!=null?UnityEditor.Editor.CreateEditor(p):null;}
        void Run(Action action){try{action();message="Done. Enter Play mode; F opens nearby building doors.";}catch(Exception e){message=e.Message;Debug.LogException(e);}}
        void OnGUI(){
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)){
                using(new EditorGUI.DisabledScope(EditorApplication.isPlaying)){
                    if(GUILayout.Button("Create / sync profiles",EditorStyles.toolbarButton))Run(CreateProfiles);
                    if(GUILayout.Button("Bake enabled profiles & use",EditorStyles.toolbarButton))Run(()=>BakeAll(true));
                    using(new EditorGUI.DisabledScope(profile==null))if(GUILayout.Button("Bake selected & use",EditorStyles.toolbarButton))Run(()=>BakeAll(false,profile));
                    if(GUILayout.Button("Use original art",EditorStyles.toolbarButton))Run(()=>RtsModelPackBaker.Use(null));
                }
            }
            var next=(ModularBuildingProfile)EditorGUILayout.ObjectField("Generation profile",profile,typeof(ModularBuildingProfile),false);if(next!=profile)Select(next);
            EditorGUILayout.HelpBox("Shared baked module meshes: detailed exterior / flat facade / 12-triangle box. Interiors and collision stream only for the nearest four buildings. Open doors and transparent windows expose connected rooms through portal visibility. Kit modules and grid additions are combined at bake time.",MessageType.Info);
            if(message.Length>0)EditorGUILayout.HelpBox(message,MessageType.Info);
            scroll=EditorGUILayout.BeginScrollView(scroll);if(profile!=null){if(inspector==null)inspector=UnityEditor.Editor.CreateEditor(profile);inspector.OnInspectorGUI();}else EditorGUILayout.HelpBox("Create profiles to populate one editable asset per building ID. Choose a profile from the project, or use the object field above. Kit assets accept shared-atlas meshes for walls, doors, windows, floors, stairs, garden walls and pillars.",MessageType.Info);EditorGUILayout.EndScrollView();
        }
        static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        [MenuItem("Massive Army/Art/Create modular building profiles")]
        public static void CreateProfiles(){
            Folder(Root+"/Profiles");var kit=AssetDatabase.LoadAssetAtPath<ModularBuildingKit>(Root+"/DefaultKit.asset");if(kit==null){kit=CreateInstance<ModularBuildingKit>();kit.name="Modular concrete, timber and glass kit";AssetDatabase.CreateAsset(kit,Root+"/DefaultKit.asset");var opaque=new Material(Resources.Load<Shader>("MassiveArmy/ModularStructure")){name="Shared modular vertex-colour atlas"};var glass=new Material(Resources.Load<Shader>("MassiveArmy/ModularGlass")){name="Shared transparent panes"};AssetDatabase.CreateAsset(opaque,Root+"/Opaque.mat");AssetDatabase.CreateAsset(glass,Root+"/Glass.mat");kit.material=opaque;kit.glassMaterial=glass;
                kit.modules=new[]{M("wall-1m",BuildingModuleKind.Wall,new Vector3(1,3.2f,.2f)),M("wall-2m",BuildingModuleKind.Wall,new Vector3(2,3.2f,.2f)),M("wall-4m",BuildingModuleKind.Wall,new Vector3(4,3.2f,.2f)),M("door-1m",BuildingModuleKind.Door,new Vector3(1,2.3f,.09f)),M("door-wide",BuildingModuleKind.Door,new Vector3(2.4f,2.3f,.09f)),M("window-1m",BuildingModuleKind.Window,new Vector3(1,1.3f,.025f)),M("window-2m",BuildingModuleKind.Window,new Vector3(2,1.3f,.025f)),M("window-3m",BuildingModuleKind.Window,new Vector3(3,1.3f,.025f)),M("floor",BuildingModuleKind.Floor,new Vector3(2,.12f,2)),M("stairs",BuildingModuleKind.Stairs,new Vector3(1.8f,3.2f,4.8f)),M("garden-wall",BuildingModuleKind.GardenWall,new Vector3(2,1.2f,.2f)),M("pillar",BuildingModuleKind.Pillar,new Vector3(.2f,3.2f,.2f))};EditorUtility.SetDirty(kit);}
            foreach(var d in RtsModelPackBaker.Catalogue().buildings){string path=Root+"/Profiles/"+d.id+".asset";if(AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(path)!=null)continue;var p=CreateInstance<ModularBuildingProfile>();p.name=d.name+" generation";p.buildingId=d.id;p.kit=kit;
                bool home=d.housingCapacity>0;bool factory=d.category==BuildingCategory.Production||d.id.Contains("factory")||d.id.Contains("processing");bool warehouse=d.id.Contains("warehouse")||d.id.Contains("depot");p.use=home?ModularUse.Home:warehouse?ModularUse.Warehouse:factory?ModularUse.Workshop:d.category==BuildingCategory.Core||d.category==BuildingCategory.Research?ModularUse.Office:ModularUse.Utility;
                p.enabledForGeneration=!d.id.StartsWith("civil_farm_")&&d.id!="civil_road"&&d.id!="civil_rail"&&d.id!="wall"&&d.id!="gate"&&d.id!="civil_fence"&&d.category!=BuildingCategory.Defence&&d.category!=BuildingCategory.Power&&d.id!="silo"&&d.id!="civil_launch_pad";
                p.width=Mathf.Min(home?14:factory?34:24,d.footprintWidth*12*.8f);p.width=Mathf.Max(9,p.width);p.depth=Mathf.Max(12,Mathf.Min(home?16:factory?34:24,d.footprintDepth*12*.8f));p.hallWidth=3.6f;p.floors=home||p.use==ModularUse.Office?2:1;p.floorHeight=factory||warehouse?4.2f:3.2f;p.doorWidth=warehouse?2.4f:1.2f;p.pitchedRoof=home;p.gardenWalls=home;p.gardenSize=new Vector2(Mathf.Max(p.width+2,d.footprintWidth*12),Mathf.Max(p.depth+2,d.footprintDepth*12));AssetDatabase.CreateAsset(p,path);
            }AssetDatabase.SaveAssets();Open();var first=AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(Root+"/Profiles/civil_housing.asset");Selection.activeObject=first;GetWindow<ModularBuildingWindow>().Select(first);
        }
        static BuildingModule M(string id,BuildingModuleKind kind,Vector3 size)=>new BuildingModule{id=id,kind=kind,size=size};
        public static void BakeAll(bool all,ModularBuildingProfile selected=null,ModularBuildingProfile[] subset=null){
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before baking.");var profiles=subset??(all?AssetDatabase.FindAssets("t:ModularBuildingProfile",new[]{Root+"/Profiles"}).Select(g=>AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(p=>p.enabledForGeneration).ToArray():new[]{selected});
            if(profiles.Length==0||profiles.Any(p=>p==null))throw new InvalidOperationException("Create or select a generation profile first.");
            var catalogue=Instantiate(RtsModelArt.Load());catalogue.name="Procedural modular buildings";var entries=catalogue.buildings.ToDictionary(x=>x.id,x=>x);var generated=new System.Collections.Generic.List<ModularBuildingBaker.Result>();string folder=null;
            try{
                var definitions=RtsModelPackBaker.Catalogue();foreach(var p in profiles){if(!definitions.Buildings.TryGetValue(p.buildingId,out var d))throw new InvalidOperationException("Unknown building ID "+p.buildingId);var result=ModularBuildingBaker.Bake(p);generated.Add(result);var l=result.layout;
                    if(l.bounds.size.x>d.footprintWidth*14||l.bounds.size.z>d.footprintDepth*14)throw new InvalidOperationException(p.buildingId+": generated modules exceed the reserved footprint; reduce the building or garden size.");entries[p.buildingId]=new RtsModelArt.Building{id=p.buildingId,near=l.exterior,far=l.flat,distant=l.proxy,customLods=true,modular=l,material=l.material};}
                folder=AssetDatabase.GenerateUniqueAssetPath(Root+"/Baked");AssetDatabase.CreateFolder(Root,Path.GetFileName(folder));AssetDatabase.StartAssetEditing();try{foreach(var result in generated){AssetDatabase.CreateAsset(result.layout,folder+"/"+result.layout.buildingId+".asset");foreach(var asset in result.assets)if(asset!=result.layout)AssetDatabase.AddObjectToAsset(asset,result.layout);}catalogue.buildings=entries.Values.ToArray();AssetDatabase.CreateAsset(catalogue,folder+"/Catalogue.asset");}finally{AssetDatabase.StopAssetEditing();}
                foreach(var result in generated)foreach(var asset in result.assets)EditorUtility.SetDirty(asset);EditorUtility.SetDirty(catalogue);for(int i=0;i<profiles.Length;i++){profiles[i].lastBake=generated[i].layout;EditorUtility.SetDirty(profiles[i]);}AssetDatabase.SaveAssets();RtsModelPackBaker.Use(catalogue);
            }catch{if(folder!=null)AssetDatabase.DeleteAsset(folder);if(!AssetDatabase.Contains(catalogue))DestroyImmediate(catalogue);throw;}finally{foreach(var result in generated)result.Dispose();}
        }
    }
}

namespace MassiveArmy.EditorTools {
    public static class CivicSecurityArt {
        [MenuItem("Massive Army/Art/Create police and graveyard models")]
        public static void Build(){ModularBuildingWindow.CreateProfiles();foreach(string id in new[]{"civil_police_hq","civil_graveyard"}){var p=AssetDatabase.LoadAssetAtPath<ModularBuildingProfile>(ModularBuildingWindow.Root+"/Profiles/"+id+".asset");bool grave=id=="civil_graveyard";p.use=ModularUse.Office;p.floors=grave?1:2;p.width=grave?12:18;p.depth=grave?14:20;p.gardenWalls=grave;p.gardenSize=new Vector2(44,44);p.gardenHeight=.9f;p.pitchedRoof=grave;p.exterior=grave?new Color(.6f,.58f,.53f):new Color(.45f,.55f,.64f);p.rooms=new[]{new ModularRoomSettings{name=grave?"Chapel":"Dispatch"},new ModularRoomSettings{name=grave?"Mortuary":"Armoury"},new ModularRoomSettings{name=grave?"Records":"Briefing room"},new ModularRoomSettings{name=grave?"Staff room":"Custody"}};
                if(grave){if(!p.kit.modules.Any(m=>m.id=="grave-marker")){var modules=p.kit.modules.ToList();modules.Add(new BuildingModule{id="grave-marker",kind=BuildingModuleKind.Pillar,size=new Vector3(.65f,.9f,.18f),colour=new Color(.5f,.51f,.49f)});p.kit.modules=modules.ToArray();EditorUtility.SetDirty(p.kit);}p.moduleGrid=.5f;var markers=new List<BuildingModulePlacement>();for(int side=-1;side<=1;side+=2)for(int x=0;x<3;x++)for(int z=0;z<5;z++)markers.Add(new BuildingModulePlacement{module="grave-marker",cell=new Vector3Int(side*(20+x*8),1,-28+z*12),room=-1});p.additions=markers.ToArray();}
                EditorUtility.SetDirty(p);ModularBuildingWindow.BakeAll(false,p);
            }AssetDatabase.SaveAssets();
        }
    }
}
