using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        public sealed class KeyBindings {
            internal const string TalkPreference="Survival.PushToTalk",AssistPreference="Survival.PilotAssist";
            // These keys avoid chat/menu, movement, interaction and flight controls.
            internal static readonly Key[] Choices={Key.V,Key.U,Key.T,Key.Y,Key.X,Key.Z,Key.F5,Key.F6,Key.F7,Key.F8,Key.F9,Key.F10,Key.F11,Key.F12};
            internal static readonly string[] Labels=Array.ConvertAll(Choices,key=>key.ToString());
            public Key pushToTalk {get;private set;}=Key.V;
            public Key pilotAssist {get;private set;}=Key.U;
            public bool TrySet(Key talk,Key assist,out string error){
                error=null;
                if(Array.IndexOf(Choices,talk)<0||Array.IndexOf(Choices,assist)<0)error="Choose an available survival key.";
                else if(talk==assist)error="Voice and pilot assist need different keys.";
                if(error!=null)return false;pushToTalk=talk;pilotAssist=assist;return true;
            }
            internal void LoadFrom(Func<string,int,int> read){
                if(!TrySet((Key)read(TalkPreference,(int)Key.V),(Key)read(AssistPreference,(int)Key.U),out _))TrySet(Key.V,Key.U,out _);
            }
            internal void SaveTo(Action<string,int> write,Action flush){write(TalkPreference,(int)pushToTalk);write(AssistPreference,(int)pilotAssist);flush();}
            public void Save()=>SaveTo(PlayerPrefs.SetInt,PlayerPrefs.Save);
        }
        string bindingNotice="";
        void SetSurvivalKeys(Key talk,Key assist){if(Bindings.TrySet(talk,assist,out bindingNotice)){Bindings.Save();bindingNotice="Saved survival keys.";}}
        void SurvivalBindingsGUI(){
            GUILayout.Label("Push-to-talk");int talk=Array.IndexOf(KeyBindings.Choices,Bindings.pushToTalk);
            int selected=GUILayout.SelectionGrid(talk,KeyBindings.Labels,7);if(selected!=talk&&selected>=0)SetSurvivalKeys(KeyBindings.Choices[selected],Bindings.pilotAssist);
            GUILayout.Label("Pilot assist (single player and multiplayer)");int assist=Array.IndexOf(KeyBindings.Choices,Bindings.pilotAssist);
            selected=GUILayout.SelectionGrid(assist,KeyBindings.Labels,7);if(selected!=assist&&selected>=0)SetSurvivalKeys(Bindings.pushToTalk,KeyBindings.Choices[selected]);
            if(GUILayout.Button("RESET VOICE V / ASSIST U"))SetSurvivalKeys(Key.V,Key.U);
            if(!string.IsNullOrEmpty(bindingNotice))GUILayout.Label(bindingNotice);
        }
    }
}
