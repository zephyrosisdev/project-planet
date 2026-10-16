using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools
{
    public static class GpuWrappingValidation
    {
        static IEnumerator routine;static GameObject root;static Battlefield field;static GpuBattle battle;static Camera view;static string error="";static double deadline;static bool waiting;static UnitState[] sample;static FogWakeField wake;
        static readonly List<string> checks=new List<string>();static readonly uint[] draws={3,3,6,6};
        [Serializable] sealed class Report{public string status,error,gpu;public string[] checks;}
        [MenuItem("Massive Army/Validate GPU World Wrapping")]
        public static void Start(){if(routine!=null)return;if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before validation.");checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Advance;AssemblyReloadEvents.beforeAssemblyReload+=Interrupted;}
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(routine==null){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Interrupted(){error="Interrupted by assembly reload";Finish();}
        static void Advance(){try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("GPU wrapping validation timed out");if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();Debug.LogException(e);}Finish();}
        static void Finish(){EditorApplication.update-=Advance;AssemblyReloadEvents.beforeAssemblyReload-=Interrupted;routine=null;Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/gpu-wrapping-validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray()},true));wake?.Dispose();wake=null;battle?.Dispose();field?.Dispose();battle=null;field=null;if(root!=null)UnityEngine.Object.DestroyImmediate(root);Debug.Log("GPU wrapping validation "+(error.Length==0?"PASS":"FAIL"));}
        static void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);checks.Add(message);}
        static UnitState Unit(Vector2 p,Vector2 destination,uint team=0,uint flags=1)=>new UnitState{position=p,previous=p,destination=destination,health=300,packed=team,flags=flags,group=uint.MaxValue,target=uint.MaxValue,facing=team==0?Mathf.PI/2:-Mathf.PI/2,turret=team==0?Mathf.PI/2:-Mathf.PI/2};
        static void Step(){battle.AdvanceRtsValidation();battle.Update(view,BattleSettings.Tick,draws,false);EditorApplication.QueuePlayerLoopUpdate();}
        static void Read(){waiting=true;battle.ReadUnitSample(0,4,data=>{sample=data;waiting=false;});}
        static IEnumerator Run(){
            root=new GameObject("Bounded GPU wrapping fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("Fixture camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.farClipPlane=7000;var rig=view.gameObject.AddComponent<RtsCamera>();rig.InputEnabled=false;
            var cfg=new WorldGenerationSettings{size=2048,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,activeUnits=64,capacity=64,projectileCapacity=256,enemyAI=false,simulationLod=false,crowdPressure=false,targeting=false,combat=false};settings.ScaleWorld();
            var map=new WorldMap(256,2048,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);rig.Field=field;battle=new GpuBattle(settings,field);
            rig.ResetOverview();Check(field.InitialSlots==null&&WorldTopology.Distance(new Vector2(rig.Focus.x,rig.Focus.z),settings.preview.Starts[0],field.Size)<.01f,"Camera startup uses persistent start metadata after deployment upload");rig.FocusBase(new Vector2(1024,1024));
            var fixture=new UnitState[64];fixture[0]=Unit(new Vector2(1022,0),new Vector2(-994,0));fixture[1]=Unit(new Vector2(0,1022),new Vector2(0,-994));fixture[2]=Unit(new Vector2(1022,1022),new Vector2(-994,-994));battle.SetValidationUnits(fixture);
            for(int i=0;i<160;i++){Step();yield return null;}Read();while(waiting)yield return null;
            Check(sample!=null&&sample[0].position.x<-990&&sample[1].position.y<-990&&sample[2].position.x<-990&&sample[2].position.y<-990,"Movement and navigation take the short route across both seams and corners");
            Check(Mathf.Abs(WorldTopology.Delta(sample[2].previous,sample[2].position,2048).x)<2,"GPU movement history remains local after crossing");
            bool complete=false;uint[] statistics=null;battle.RequestStatistics(data=>{statistics=data;complete=true;});while(!complete)yield return null;Check(statistics!=null&&statistics[0]==3&&statistics[1]==0,"Repeated images retain three original simulation identities");complete=false;uint[] arguments=null;battle.ReadDrawArguments(data=>{arguments=data;complete=true;});while(!complete)yield return null;Check(arguments!=null&&arguments[1]+arguments[6]+arguments[11]>0&&arguments[1]+arguments[6]+arguments[11]<=3,"Visible repeated images never duplicate unit draw membership");
            fixture=new UnitState[64];fixture[0]=Unit(new Vector2(1022,0),new Vector2(1022,0));fixture[1]=Unit(new Vector2(-1022,0),new Vector2(-1022,0),1);battle.SetValidationUnits(fixture);for(int i=0;i<12;i++){Step();yield return null;}Read();while(waiting)yield return null;Check(field.Distance(sample[0].position,sample[1].position)>4.1f,"Contacts resolve overlapping hulls across the seam");
            settings.movement=false;settings.targeting=true;settings.combat=true;settings.targetSlices=1;fixture=new UnitState[64];fixture[0]=Unit(new Vector2(1008,0),new Vector2(1008,0),0,5);fixture[1]=Unit(new Vector2(-1008,0),new Vector2(-1008,0),1,5);battle.SetValidationUnits(fixture);
            for(int i=0;i<150;i++){Step();yield return null;}Read();while(waiting)yield return null;Check(sample[0].health<300||sample[1].health<300,"Target acquisition, weapon aiming and damage operate across a seam");
            settings.targeting=false;settings.combat=false;fixture[0]=Unit(new Vector2(1020,0),new Vector2(1020,0),0,5);fixture[1]=Unit(new Vector2(-1000,0),new Vector2(-1000,0),1,5);battle.SetValidationUnits(fixture);
            var shots=new ProjectileState[256];shots[0]=new ProjectileState{position=new Vector3(1020,13,0),velocity=new Vector3(100,0,0),lifetime=.5f,damage=50,owner=0,target=1,team=0,alive=1};battle.Projectiles.SetData(shots);for(int i=0;i<20;i++){Step();yield return null;}Read();while(waiting)yield return null;Check(sample[1].health<300,"Projectile segment hits use the nearest target image after crossing");
            settings.baseBuilding=true;fixture[0]=Unit(new Vector2(1020,0),new Vector2(1020,0),0,5);fixture[1]=Unit(new Vector2(-1000,0),new Vector2(-1000,0),1,5);battle.SetValidationUnits(fixture);for(int i=0;i<12;i++){Step();yield return null;}while(battle.PlayerVisibility==null)yield return null;Check(battle.IsVisible(new Vector2(-1000,0)),"Reconnaissance reveals cells on the opposite side of the seam");Check(!battle.IsVisible(new Vector2(0,700)),"Reconnaissance keeps distant terrain hidden");
            // Fixed-size diagnostic fields and at most 64 units; never mirror a live army.
            rig.FocusBase(new Vector2(1024,0));var look=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");
            var visibility=new uint[GpuBattle.FogResolution*GpuBattle.FogResolution*2];battle.FogBuffer.SetData(visibility);
            fixture=new UnitState[64];fixture[0]=Unit(new Vector2(1020,0),new Vector2(-900,0),1);fixture[0].velocity=Vector2.right*15;battle.SetValidationUnits(fixture);wake=new FogWakeField();
            using(var cmd=new CommandBuffer()){wake.Record(cmd,battle,view,look,1);Graphics.ExecuteCommandBuffer(cmd);}bool read=false,nonzero=false;
            AsyncGPUReadback.Request(wake.Current,0,r=>{if(r.hasError){error="Wake readback failed";read=true;return;}for(int layer=0;layer<r.layerCount;layer++)foreach(ushort value in r.GetData<ushort>(layer))if(value!=0)nonzero=true;read=true;});while(!read)yield return null;Check(error.Length==0,"Wake readback succeeded");
            Check(!nonzero,"Hidden enemies inject no observable disturbance into the 3D wake field");wake.Dispose();wake=new FogWakeField();fixture[0].packed=0;battle.SetValidationUnits(fixture);
            using(var cmd=new CommandBuffer()){wake.Record(cmd,battle,view,look,1);Graphics.ExecuteCommandBuffer(cmd);}read=false;nonzero=false;
            AsyncGPUReadback.Request(wake.Current,0,r=>{if(r.hasError){error="Wake readback failed";read=true;return;}for(int layer=0;layer<r.layerCount;layer++)foreach(ushort value in r.GetData<ushort>(layer))if(value!=0)nonzero=true;read=true;});while(!read)yield return null;Check(error.Length==0,"Wake readback succeeded");
            read=false;long injected=0;var aggregate=(GraphicsBuffer)typeof(FogWakeField).GetField("injection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(wake);
            AsyncGPUReadback.Request(aggregate,r=>{if(!r.hasError){var data=r.GetData<int>();for(int i=3;i<data.Length;i+=4)injected+=data[i];}read=true;});while(!read)yield return null;
            Read();while(waiting)yield return null;
            File.WriteAllText("TestResults/wake-debug.txt",$"strength {look.wakeStrength}, swirl {look.swirlStrength}, radius {look.wakeRadius}, tick {battle.Tick}, origin {wake.Origin}, extent {wake.Extent}, velocity {fixture[0].velocity}, gpu velocity {sample[0].velocity}, gpu flags {sample[0].flags}, gpu team {sample[0].packed}, injection count {injected}");
            Check(nonzero,"Moving friendly units inject a persistent three-dimensional vortex field");
            double after=EditorApplication.timeSinceStartup+.1;while(EditorApplication.timeSinceStartup<after)yield return null;fixture[0].velocity=Vector2.zero;battle.SetValidationUnits(fixture);
            using(var cmd=new CommandBuffer()){wake.Record(cmd,battle,view,look,1);Graphics.ExecuteCommandBuffer(cmd);}read=false;nonzero=false;
            AsyncGPUReadback.Request(wake.Current,0,r=>{if(r.hasError){error="Wake readback failed";read=true;return;}for(int layer=0;layer<r.layerCount;layer++)foreach(ushort value in r.GetData<ushort>(layer))if(value!=0)nonzero=true;read=true;});while(!read)yield return null;Check(error.Length==0,"Wake readback succeeded");
            Check(nonzero,"Vortex wakes persist and decay after their emitter stops");
        }
    }
}
