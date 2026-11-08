#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace MassiveArmy.Networking {
    internal static class SurvivalBindingChecks {
        internal static void Run(Action<bool,string> check){
            var keys=new SurvivalNetworkSession.KeyBindings();
            check(keys.pushToTalk==Key.V&&keys.pilotAssist==Key.U,"Survival bindings default to V voice and U pilot assist");
            check(keys.TrySet(Key.Y,Key.X,out _)&&keys.pushToTalk==Key.Y&&keys.pilotAssist==Key.X,"Both survival actions accept available alternate keys");
            check(!keys.TrySet(Key.Z,Key.Z,out _)&&keys.pushToTalk==Key.Y&&keys.pilotAssist==Key.X,"Conflicting bindings are rejected atomically without changing either action");
            check(!keys.TrySet(Key.Enter,Key.F4,out _)&&!keys.TrySet(Key.Escape,Key.None,out _),"Chat, menu, interaction and invalid keys cannot replace survival bindings");
            keys.LoadFrom((name,fallback)=>9999);
            check(keys.pushToTalk==Key.V&&keys.pilotAssist==Key.U,"Malformed saved key values restore safe defaults");
            keys.LoadFrom((name,fallback)=>(int)Key.V);
            check(keys.pushToTalk==Key.V&&keys.pilotAssist==Key.U,"A conflicting saved key pair restores safe defaults");
            var saved=new Dictionary<string,int>();bool flushed=false;keys.TrySet(Key.F6,Key.F7,out _);keys.SaveTo((name,value)=>saved[name]=value,()=>flushed=true);
            var restored=new SurvivalNetworkSession.KeyBindings();restored.LoadFrom((name,fallback)=>saved.TryGetValue(name,out int value)?value:fallback);
            check(flushed&&saved.Count==2&&restored.pushToTalk==Key.F6&&restored.pilotAssist==Key.F7,"Saved survival keys round-trip through the production preference adapter without modifying real preferences in tests");
        }
    }
}
#endif
