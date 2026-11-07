using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    [CustomEditor(typeof(PlanetDefinition))]
    public sealed class PlanetDefinitionPrecisionEditor:Editor
    {
        public override void OnInspectorGUI(){
            var body=(PlanetDefinition)target;
            EditorGUILayout.HelpBox($"Automatic precision: {body.PrecisionMode}\nRadius: {body.radius/1000:0.###} km. Planet-centred float spacing: approximately {body.EstimatedFloatSpacing:0.####} m. Authoritative positions remain double precision. This does not change army or terrain budgets.",MessageType.Info);
            // Shortcuts edit the same serialized Weather fields, with normal Undo and asset persistence.
            serializedObject.Update();
            var weatherProperty=serializedObject.FindProperty("weather");
            if(body.weather!=null){
                EditorGUILayout.LabelField("Atmosphere density noise",EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(weatherProperty.FindPropertyRelative("airDensityNoise"),new GUIContent("Noise strength"));
                EditorGUILayout.PropertyField(weatherProperty.FindPropertyRelative("airDensityNoiseScale"),new GUIContent("Noise scale (frequency)"));
                EditorGUILayout.HelpBox("Lower scale = larger features; higher scale = smaller features. Strength 0 disables density noise. Atmosphere density uses one noise layer; Weather > Live cloud noise tuning > Detail Noise Octaves affects clouds only. These density controls require Has Atmosphere and Weather > Physical Atmosphere.",MessageType.Info);
                EditorGUILayout.PropertyField(weatherProperty.FindPropertyRelative("airSolarTide"),new GUIContent("Solar atmospheric tide"));
                EditorGUILayout.PropertyField(weatherProperty.FindPropertyRelative("airGravityTide"),new GUIContent("Gravitational atmospheric tide"));
                EditorGUILayout.Space();
            }
            serializedObject.ApplyModifiedProperties();
            DrawDefaultInspector();
            if(body.weather!=null){var weather=body.weather;long bytes=(long)weather.ShadowWidth*(weather.ShadowWidth/2)*weather.ShadowLayers*4;
                EditorGUILayout.HelpBox($"Cloud shadow cache: {weather.ShadowWidth} × {weather.ShadowWidth/2} × {weather.ShadowLayers} layers, {bytes/1048576.0:0.##} MiB (cache only). High resolutions also increase per-frame lighting work.",MessageType.Info);
            }
        }
    }
}
