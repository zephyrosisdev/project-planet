using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class SpawnGpuValidation
    {
        static IEnumerator routine;
        static GameObject root;
        static Battlefield field;
        static GpuBattle battle;
        [Serializable] sealed class Report { public string status,error,timestamp,gpu;public int units;public float worldSize;public uint invalid,overlaps,checkedUnits,gridErrors,gridMembers; }
        static Report report;
        public static void Start()
        {
            if(routine!=null)throw new InvalidOperationException("Spawn validation already running");
            report=new Report{status="PASS",timestamp=DateTime.UtcNow.ToString("O"),gpu=SystemInfo.graphicsDeviceName};
            routine=Run();EditorApplication.update+=Advance;
        }
        static IEnumerator Run()
        {
            root=new GameObject("Million-unit spawn validation");
            var settings=new BattleSettings{activeUnits=1000000,capacity=1000000,seed=332385012,combat=false,targeting=false,enemyAI=false,projectileCapacity=256};settings.ScaleWorld();
            field=new Battlefield(settings,root.transform);battle=new GpuBattle(settings,field);
            report.units=settings.activeUnits;report.worldSize=settings.worldSize;
            bool done=false;uint[] stats=null;
            battle.ValidateSpawns(s=>{stats=s;done=true;});while(!done)yield return null;
            if(stats==null)throw new InvalidOperationException("Spawn readback failed");
            report.invalid=stats[104];report.overlaps=stats[105];report.checkedUnits=stats[106];
            if(report.invalid!=0||report.overlaps!=0||report.checkedUnits!=1000000)throw new InvalidOperationException("GPU deployment contains blocked or insufficiently spaced spawns");
            done=false;battle.ValidateSpatialGrid(s=>{stats=s;done=true;});while(!done)yield return null;
            if(stats==null)throw new InvalidOperationException("Grid readback failed");
            report.gridErrors=stats[100];report.gridMembers=stats[101];
            if(report.gridErrors!=0||report.gridMembers!=1000000)throw new InvalidOperationException("Million-unit membership mismatch");
        }
        static void Advance()
        {
            try { if(routine.MoveNext())return; }
            catch(Exception e) { report.status="FAIL";report.error=e.ToString();Debug.LogException(e); }
            EditorApplication.update-=Advance;routine=null;battle?.Dispose();field?.Dispose();battle=null;field=null;
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            File.WriteAllText("TestResults/million-spawn-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Million-unit GPU spawn validation: "+report.status);
        }
    }
}
