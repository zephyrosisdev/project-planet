using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    // Full population audit stays on the GPU; only four aggregate integers return.
    public static class ZombieRefugeValidation
    {
        static IEnumerator routine;static GameObject root;static Battlefield field;static GpuBattle battle;static RtsWorld world;static string error="";static double deadline;
        static readonly List<string> checks=new List<string>();
        [Serializable]sealed class Report{public string status,error,gpu;public string[] checks;}
        public static void Batch(){checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+300;Application.logMessageReceived+=Log;EditorApplication.update+=Advance;}
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||text.Contains("Skipping dispatch"))error=text;}
        static void Cleanup(){world?.Dispose();world=null;battle?.Dispose();battle=null;field?.Dispose();field=null;if(root)UnityEngine.Object.DestroyImmediate(root);}
        static void Advance(){try{if(error.Length>0)throw new Exception(error);if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException();if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();}EditorApplication.update-=Advance;Application.logMessageReceived-=Log;Cleanup();Directory.CreateDirectory("TestResults/ZombieRefuge");File.WriteAllText("TestResults/ZombieRefuge/validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray()},true));EditorApplication.Exit(error.Length==0?0:1);}
        static IEnumerator Run(){
            for(int mode=0;mode<4;mode++){
                root=new GameObject("Starting zombie refuge fixture"){hideFlags=HideFlags.DontSave};var view=new GameObject("Audit camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.farClipPlane=8192;
                int size=mode==3?2048:4096;
                var cfg=new WorldGenerationSettings{size=size,seed=73129};
                var settings=new BattleSettings{world=cfg,seed=73129,baseBuilding=true,swarmMode=mode==1,spectatorMode=mode==2,enemyAI=false,ambientZombies=100000,activeUnits=150000,capacity=150000,combat=false,targeting=false,crowdPressure=false};settings.ScaleWorld();
                var map=new WorldMap(settings.navigationResolution,size,cfg.seed,cfg);settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);
                var cat=RtsCatalog.Load();battle=new GpuBattle(settings,field,cat.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,cat);
                while(battle.AmbientZombieRemaining>0||battle.SwarmPending){battle.AdvanceSwarmValidation();battle.AdvanceRtsValidation();battle.Update(view,BattleSettings.Tick,new uint[]{3,3,6,6},false);EditorApplication.QueuePlayerLoopUpdate();yield return null;}
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassiveArmy/Editor/ZombieValidation.compute");int k=shader.FindKernel("RefugeAudit");float radius=ZombieDefinition.StartingRefugeRadius(size);
                using(var result=new UnityEngine.GraphicsBuffer(UnityEngine.GraphicsBuffer.Target.Structured,4,4)){
                    result.SetData(new uint[]{0,0,uint.MaxValue,uint.MaxValue});var a=world.BasePosition(0);var b=world.BasePosition(1);
                    shader.SetFloat("_WorldSize",size);shader.SetFloat("_RefugeRadius",radius);shader.SetInt("_HomeCount",mode==1?1:2);shader.SetInt("_AuditCapacity",settings.capacity);shader.SetVector("_Homes",new Vector4(a.x,a.y,b.x,b.y));shader.SetBuffer(k,"_AuditUnits",battle.Units);shader.SetBuffer(k,"_RefugeResults",result);shader.Dispatch(k,(settings.capacity+127)/128,1,1);
                    var data=new uint[4];result.GetData(data);
                    if(data[0]!=100000||data[1]!=0)throw new Exception("Refuge audit failed for mode "+mode+": "+string.Join(",",data));
                    checks.Add("Mode "+mode+", "+size+"m world: all 100000 zombies outside "+radius+"m; nearest Cyan "+data[2]/1000f+"m"+(mode==1?"":" / Red "+data[3]/1000f+"m"));
                }
                Cleanup();yield return null;
            }
        }
    }
}
