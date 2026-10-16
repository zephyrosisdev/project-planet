using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools
{
    [CustomEditor(typeof(PlanetStampLibrary))]
    public sealed class PlanetStampLibraryEditor:UnityEditor.Editor
    {
        public override void OnInspectorGUI(){serializedObject.Update();EditorGUILayout.HelpBox("Seeded spherical terrain. Preserve definition order for saved worlds. Stop Play mode before rebaking source maps.",MessageType.Info);
            using var editing=new EditorGUI.DisabledScope(EditorApplication.isPlaying);var entries=serializedObject.FindProperty("definitions");EditorGUILayout.PropertyField(entries.FindPropertyRelative("Array.size"));
            for(int i=0;i<entries.arraySize;i++){
                var entry=entries.GetArrayElementAtIndex(i);EditorGUILayout.BeginVertical(EditorStyles.helpBox);EditorGUILayout.LabelField("Stamp "+i,EditorStyles.boldLabel);EditorGUILayout.PropertyField(entry.FindPropertyRelative("name"));
                var path=entry.FindPropertyRelative("sourceAsset");var previous=AssetDatabase.LoadAssetAtPath<Texture2D>(path.stringValue);var selected=(Texture2D)EditorGUILayout.ObjectField("Source heightmap",previous,typeof(Texture2D),false);if(selected!=previous)path.stringValue=selected!=null?AssetDatabase.GetAssetPath(selected):"";
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("heightGain"));EditorGUILayout.PropertyField(entry.FindPropertyRelative("footprint"));EditorGUILayout.PropertyField(entry.FindPropertyRelative("heightOffset"));EditorGUILayout.EndVertical();
            }
            serializedObject.ApplyModifiedProperties();using(new EditorGUI.DisabledScope(EditorApplication.isPlaying))if(GUILayout.Button("Bake source heightmaps"))PlanetStampImport.Bake();
        }
    }
}
