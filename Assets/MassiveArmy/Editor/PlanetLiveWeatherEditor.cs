using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class PlanetLiveWeatherEditor
    {
        static PlanetDefinition Active(){
            var system=Object.FindFirstObjectByType<PlanetSystemRuntime>();
            if(system?.ActiveWorld?.definition!=null)return system.ActiveWorld.definition;
            foreach(var world in Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None))if(world.Ready&&world.View.enabled)return world.definition;
            return null;
        }
        [MenuItem("Synthetic Front/Planets/Use natural daylight atmosphere")]
        public static void NaturalDaylight(){var body=Active()??Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");if(body==null)return;Undo.RecordObject(body,"Natural daylight atmosphere");body.weather??=new PlanetWeather();body.weather.physicalAtmosphere=true;body.weather.rayleighStrength=1;body.weather.aerosolStrength=1;body.weather.haze=.45f;body.weather.cloudColour=Color.white;body.weather.atmosphereColour=new Color(.18f,.4f,.85f);body.weather.neutralSunlight=true;EditorUtility.SetDirty(body);Selection.activeObject=body;Debug.Log("Natural daylight: custom Rayleigh/Mie atmosphere, white primary sun and clouds. Atmosphere tint only applies in legacy haze mode.");}
        [MenuItem("Synthetic Front/Planets/Edit active planet weather")]
        public static void SelectWeather(){var body=Active();if(body==null){Debug.LogWarning("Start a planetary session before editing live weather.");return;}Selection.activeObject=body;EditorGUIUtility.PingObject(body);}
        [MenuItem("Synthetic Front/Planets/Apply saved large-planet cloud preset")]
        public static void ApplyLargePlanetPreset(){
            var body=Active();var preset=Resources.Load<TextAsset>("MassiveArmy/Planets/LargePlanetClouds");
            if(body==null||preset==null){Debug.LogWarning("Start a planetary session before applying the saved cloud preset.");return;}
            Undo.RecordObject(body,"Apply large-planet cloud preset");JsonUtility.FromJsonOverwrite(preset.text,body.weather);EditorUtility.SetDirty(body);Selection.activeObject=body;
        }
        public static void SavePresetAsDefault(){
            var body=AssetDatabase.LoadAssetAtPath<PlanetDefinition>("Assets/MassiveArmy/Resources/MassiveArmy/Planets/DefaultPlanet.asset");
            var preset=Resources.Load<TextAsset>("MassiveArmy/Planets/LargePlanetClouds");
            JsonUtility.FromJsonOverwrite(preset.text,body.weather);EditorUtility.SetDirty(body);AssetDatabase.SaveAssetIfDirty(body);
        }
        [MenuItem("Synthetic Front/Planets/Copy active weather settings")]
        public static void CopyWeather(){var body=Active();if(body==null){Debug.LogWarning("Start a planetary session first.");return;}EditorGUIUtility.systemCopyBuffer=JsonUtility.ToJson(body.weather,true);Debug.Log("Active planet weather settings copied. Paste them into the chat before stopping Play mode.");}
    }
}

