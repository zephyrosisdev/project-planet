using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class CombatValidation
    {
        const string Request="SyntheticFront.ValidateCombatPresentation";
        [InitializeOnLoadMethod] static void Install(){EditorApplication.playModeStateChanged-=Played;EditorApplication.playModeStateChanged+=Played;}
        [MenuItem("Massive Army/Validate Combat Presentation")]
        public static void Start(){SessionState.SetBool(Request,true);EditorApplication.isPlaying=true;}
        static void Played(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Request,false))return;SessionState.SetBool(Request,false);
            var window=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));window.Show();window.Focus();UnityEngine.Object.FindAnyObjectByType<BattleApp>().StartCoroutine(Run());
        }
        static IEnumerator Run()
        {
            var app=UnityEngine.Object.FindAnyObjectByType<BattleApp>();yield return null;app.StartFlatValidationBattle();while(app.State=="loading")yield return null;
            if(app.Base==null)throw new InvalidOperationException("Combat presentation could not load");app.Paused=false;yield return new WaitForSecondsRealtime(2);app.Paused=true;
            yield return app.ValidateCombatPresentation("TestResults/CombatPresentation");EditorApplication.isPlaying=false;
        }
    }
}
