using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public sealed class RtsModelPackWindow:EditorWindow
    {
        RtsModelPack pack;SerializedObject serialized;Vector2 listScroll,detailScroll;string search="",message="";int tab,selected;bool failed;
        [MenuItem("Massive Army/Art/Model packs")]
        public static void Open(){var window=GetWindow<RtsModelPackWindow>("RTS Model Packs");window.minSize=new Vector2(840,580);}
        [MenuItem("Assets/Edit RTS model pack",true)]static bool CanEdit()=>Selection.activeObject is RtsModelPack;
        [MenuItem("Assets/Edit RTS model pack")]static void Edit(){Open();var window=GetWindow<RtsModelPackWindow>();window.Set(Selection.activeObject as RtsModelPack);}
        void Set(RtsModelPack value){pack=value;serialized=pack!=null?new SerializedObject(pack):null;selected=0;message="";}
        void Act(Action action){try{action();failed=false;message="Done. Enter Play mode to test the selected pack.";}catch(Exception e){failed=true;message=e.Message;Debug.LogException(e);}Repaint();}
        void OnGUI(){
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)){
                var next=(RtsModelPack)EditorGUILayout.ObjectField(pack,typeof(RtsModelPack),false,GUILayout.MinWidth(240));if(next!=pack)Set(next);
                if(GUILayout.Button("New pack",EditorStyles.toolbarButton,GUILayout.Width(90)))Create();
                using(new EditorGUI.DisabledScope(pack==null||EditorApplication.isPlaying)){
                    if(GUILayout.Button("Sync catalogue",EditorStyles.toolbarButton,GUILayout.Width(110)))Act(()=>{RtsModelPackBaker.Sync(pack);serialized.Update();});
                    if(GUILayout.Button("Bake & use pack",EditorStyles.toolbarButton,GUILayout.Width(120)))Act(()=>{serialized.ApplyModifiedProperties();RtsModelPackBaker.BakeAndUse(pack);});
                    using(new EditorGUI.DisabledScope(pack==null||pack.bakedCatalogue==null))if(GUILayout.Button("Use last bake",EditorStyles.toolbarButton,GUILayout.Width(100)))Act(()=>RtsModelPackBaker.Use(pack.bakedCatalogue));
                }
                using(new EditorGUI.DisabledScope(EditorApplication.isPlaying))if(GUILayout.Button("Use defaults",EditorStyles.toolbarButton,GUILayout.Width(100)))Act(()=>RtsModelPackBaker.Use(null));
            }
            var selector=Resources.Load<RtsModelSelection>("MassiveArmy/ModelArt/Selection");EditorGUILayout.LabelField("Active catalogue",selector!=null&&selector.catalogue!=null?selector.catalogue.name:"Original art (planet buildings remain cubes)");
            if(EditorApplication.isPlaying)EditorGUILayout.HelpBox("Stop Play mode before baking or switching packs. Existing GPU buffers stay intact during play.",MessageType.Info);
            if(message.Length>0)EditorGUILayout.HelpBox(message,failed?MessageType.Error:MessageType.Info);
            if(pack==null){EditorGUILayout.HelpBox("Create a pack or select an existing one. Each building and unit can keep its original art or use custom meshes/prefabs, textured cubes, or generated shapes. Bake & use pack activates it for the next Play session. The original catalogue is preserved.",MessageType.Info);return;}
            if(serialized==null)serialized=new SerializedObject(pack);serialized.Update();
            using(new EditorGUILayout.HorizontalScope()){
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(240))){
                    tab=GUILayout.Toolbar(tab,new[]{"Buildings","Units"});search=EditorGUILayout.TextField("Find",search);listScroll=EditorGUILayout.BeginScrollView(listScroll);
                    var array=serialized.FindProperty(tab==0?"buildings":"units");if(selected>=array.arraySize)selected=0;
                    for(int i=0;i<array.arraySize;i++){var entry=array.GetArrayElementAtIndex(i);string id=entry.FindPropertyRelative("id").stringValue;if(search.Length>0&&id.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                        string label=(entry.FindPropertyRelative("enabled").boolValue?"● ":"○ ")+id;if(GUILayout.Toggle(selected==i,label,"Button"))selected=i;}
                    EditorGUILayout.EndScrollView();
                }
                detailScroll=EditorGUILayout.BeginScrollView(detailScroll);
                var rows=serialized.FindProperty(tab==0?"buildings":"units");if(rows.arraySize>0){var row=rows.GetArrayElementAtIndex(selected);EditorGUILayout.LabelField(row.FindPropertyRelative("id").stringValue,EditorStyles.boldLabel);EditorGUILayout.PropertyField(row.FindPropertyRelative("enabled"),new GUIContent("Override this model"));
                    using(new EditorGUI.DisabledScope(!row.FindPropertyRelative("enabled").boolValue)){
                        EditorGUILayout.PropertyField(row.FindPropertyRelative("size"),new GUIContent("Authored size (metres)", "Before planet construction-grid scaling; flat RTS stays unscaled. All LODs fit this box, centred horizontally with their base at ground level."));
                        Lod(row.FindPropertyRelative("lod0"),tab==0?"LOD0 · near · 4,096 triangles max":"LOD0 · near · 512 triangles max");
                        Lod(row.FindPropertyRelative("lod1"),tab==0?"LOD1 · middle · 512 triangles max":"LOD1 · middle · 128 triangles max");
                        if(tab==0)Lod(row.FindPropertyRelative("lod2"),"LOD2 · distant · 128 triangles max");
                        else{EditorGUILayout.LabelField("LOD2 · eight directional impostors",EditorStyles.boldLabel);EditorGUILayout.PropertyField(row.FindPropertyRelative("impostors"),new GUIContent("Custom views (optional)"),true);EditorGUILayout.HelpBox("An empty list bakes all eight views from LOD1. Custom views: exactly eight, clockwise from the front (+Z). Strategic dots in the flat-world stress path retain the existing renderer.",MessageType.Info);}
                    }
                }
                EditorGUILayout.Space();EditorGUILayout.HelpBox("Y is up, +Z is forward. Prefab LODGroups select their matching level; child transforms and material slots are combined. Standard/HDRP base colour, metallic/smoothness and normal maps are baked to shared 512px atlases. Custom shader effects and animation controllers are not copied; skinned models use their static bind-pose mesh. Mesh import Read/Write must be enabled while baking. Roads/rails use their own network renderer.",MessageType.Info);
                if(pack.bakedAt?.Length>0)EditorGUILayout.LabelField("Last bake",pack.bakedAt);EditorGUILayout.EndScrollView();
            }serialized.ApplyModifiedProperties();
        }
        static void Lod(SerializedProperty source,string label){
            EditorGUILayout.Space(8);EditorGUILayout.LabelField(label,EditorStyles.boldLabel);EditorGUILayout.PropertyField(source.FindPropertyRelative("kind"),new GUIContent("Source"));
            var kind=(ModelSourceKind)source.FindPropertyRelative("kind").enumValueIndex;
            if(kind==ModelSourceKind.MeshOrPrefab){EditorGUILayout.PropertyField(source.FindPropertyRelative("model"),new GUIContent("Mesh / prefab / model"));var value=source.FindPropertyRelative("model").objectReferenceValue;if(value!=null&&!(value is Mesh)&&!(value is GameObject))EditorGUILayout.HelpBox("Assign a Mesh or GameObject asset.",MessageType.Error);}
            if(kind==ModelSourceKind.Procedural)EditorGUILayout.PropertyField(source.FindPropertyRelative("shape"));
            EditorGUILayout.PropertyField(source.FindPropertyRelative("rotation"),new GUIContent("Import rotation"));
            if(kind!=ModelSourceKind.Original){EditorGUILayout.PropertyField(source.FindPropertyRelative("material"));EditorGUILayout.PropertyField(source.FindPropertyRelative("albedo"),new GUIContent("Albedo override"));EditorGUILayout.PropertyField(source.FindPropertyRelative("tint"));}
        }
        void Create(){string path=EditorUtility.SaveFilePanelInProject("New model pack","Model Pack","asset","Choose a folder for this model pack.");if(path.Length==0)return;var next=CreateInstance<RtsModelPack>();next.name=System.IO.Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(next,path);RtsModelPackBaker.Sync(next);AssetDatabase.SaveAssets();Set(next);Selection.activeObject=next;}
    }
    [CustomEditor(typeof(RtsModelPack))]public sealed class RtsModelPackInspector:Editor
    {
        public override void OnInspectorGUI(){EditorGUILayout.HelpBox("Author mesh, material, cube and procedural LOD choices in the Model Packs window.",MessageType.Info);if(GUILayout.Button("Open Model Packs")){Selection.activeObject=target;EditorApplication.ExecuteMenuItem("Assets/Edit RTS model pack");}DrawDefaultInspector();}
    }
}
