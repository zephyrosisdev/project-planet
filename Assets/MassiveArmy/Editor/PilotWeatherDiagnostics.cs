using System.IO;
using System.Reflection;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PilotWeatherDiagnostics {
        const string Folder="TestResults/Planetary/PilotWeatherTrace",Request=Folder+"/request.txt";
        static PlanetWeatherRenderer Renderer(){foreach(var world in Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None))if(world.Ready&&world.View!=null&&world.View.enabled)return (PlanetWeatherRenderer)typeof(PlanetDevelopment).GetField("weatherRenderer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);return null;}
        [MenuItem("Synthetic Front/Vehicles/Camera settings/Spaceship pilot")]
        public static void SelectPilot(){SelectCamera("SpaceshipPilot");}
        [MenuItem("Synthetic Front/Vehicles/Camera settings/Quad bike")]
        public static void SelectQuad(){SelectCamera("SF-QB1");}
        [MenuItem("Synthetic Front/Vehicles/Camera settings/Jeep")]
        public static void SelectJeep(){SelectCamera("SF-LUV1");}
        static void SelectCamera(string name){var settings=Resources.Load<VehicleCameraSettings>("MassiveArmy/VehicleCameras/"+name);if(settings==null){Debug.LogWarning("Camera profile not imported: "+name);return;}Selection.activeObject=settings;EditorGUIUtility.PingObject(settings);EditorApplication.ExecuteMenuItem("Window/General/Inspector");}
        [MenuItem("Synthetic Front/Diagnostics/Pilot weather/Start recording")]
        public static void Start(){var renderer=Renderer();if(renderer==null){Debug.LogWarning("Start a planetary session first.");return;}renderer.ClearTrace();renderer.TraceEnabled=true;Debug.Log("Recording the last 128 weather renders. Pause at the glitch, then Save recording; paused repaints are retained too.");}
        [MenuItem("Synthetic Front/Diagnostics/Pilot weather/Save recording")]
        public static void Save(){var renderer=Renderer();if(renderer==null)return;Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/frames.json",renderer.TraceJson());renderer.TraceEnabled=false;Debug.Log("Saved pilot weather trace to "+Folder+"/frames.json; recording stopped.");}
        [MenuItem("Synthetic Front/Diagnostics/Pilot weather/Stop recording")]
        public static void Stop(){var renderer=Renderer();if(renderer!=null)renderer.TraceEnabled=false;}
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;string action=File.ReadAllText(Request).Trim();File.Delete(Request);if(action=="start")Start();else if(action=="save")Save();else if(action=="stop")Stop();else if(action=="select-pilot")SelectPilot();}
    }
}
