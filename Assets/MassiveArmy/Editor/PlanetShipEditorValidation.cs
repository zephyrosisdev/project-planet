using System;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace MassiveArmy.EditorTools
{
    // Exercise the loaded Editor assembly, not only the independently compiled player.
    public static class PlanetShipEditorValidation
    {
        const string RestoreKey="PlanetShipValidation.RestoreScene";
        [InitializeOnLoadMethod]static void Install(){EditorApplication.playModeStateChanged-=Restore;EditorApplication.playModeStateChanged+=Restore;}
        public static void Run(){
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before running the Editor ship validation.");
            var scene=SceneManager.GetActiveScene();if(scene.isDirty)throw new InvalidOperationException("Save the active scene before running the Editor ship validation.");
            SessionState.SetString(RestoreKey,scene.path);
            EditorSceneManager.OpenScene("Assets/Scenes/PlanetDevelopment.unity",OpenSceneMode.Single);
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText(PlanetShipValidation.EditorRequestFile,"run");EditorApplication.isPlaying=true;
        }
        static void Restore(PlayModeStateChange state){
            if(state!=PlayModeStateChange.EnteredEditMode)return;string path=SessionState.GetString(RestoreKey,"");SessionState.SetString(RestoreKey,"");
            if(path.Length>0&&File.Exists(path))EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        }
    }
}
