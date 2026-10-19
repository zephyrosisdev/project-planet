using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetEditorMemory {
        public static void Release(){
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before releasing build diagnostics");
            if(SessionState.GetInt("PlanetaryValidationWorkers",-1)<0)SessionState.SetInt("PlanetaryValidationWorkers",AssetDatabase.DesiredWorkerCount);
            AssetDatabase.DesiredWorkerCount=1;AssetDatabase.ForceToDesiredWorkerCount();
            var driver=typeof(Editor).Assembly.GetType("UnityEditorInternal.ProfilerDriver");var clearFrames=driver?.GetMethod("ClearAllFrames",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);if(clearFrames!=null&&clearFrames.GetParameters().Length==0)clearFrames.Invoke(null,null);
            var log=typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");log?.GetMethod("Clear",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,null);
            GC.Collect();GC.WaitForPendingFinalizers();EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            Directory.CreateDirectory("TestResults/Planetary/Orbitals");File.WriteAllText("TestResults/Planetary/Orbitals/editor-memory.txt",DateTime.Now.ToString("O")+"\nTemporary import worker count: "+AssetDatabase.DesiredWorkerCount+"\nTracked allocated: "+UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
        }
        public static void Restore(){int workers=SessionState.GetInt("PlanetaryValidationWorkers",-1);if(workers>=0){AssetDatabase.DesiredWorkerCount=workers;File.AppendAllText("TestResults/Planetary/Orbitals/editor-memory.txt",Environment.NewLine+"Restored original desired import workers: "+workers);SessionState.EraseInt("PlanetaryValidationWorkers");}}
    }
}
