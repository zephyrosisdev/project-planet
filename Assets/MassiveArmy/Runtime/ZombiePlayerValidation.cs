using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy
{
    // Opt-in presented-player fixture. Aggregate and bounded sample readbacks only.
    public sealed class ZombiePlayerValidation:MonoBehaviour
    {
        [Serializable]sealed class Report{public string status,error,gpu;public string[] checks;public uint regularAlive,swarmAlive;}
        [Serializable]sealed class WaveSample{public Vector2[] before,after,velocity,destination;public uint[] flags,targets;public int moving;public float meanTravel;}
        readonly List<string> checks=new List<string>();string failure="";BattleApp app;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-zombie-validation")>=0)new GameObject("Zombie player fixture").AddComponent<ZombiePlayerValidation>();}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||text.Contains("Skipping dispatch"))failure=text;}
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
        static string Arg(string key,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;app=FindAnyObjectByType<BattleApp>();string folder=Arg("-benchmark-output",Application.persistentDataPath);Directory.CreateDirectory(folder);
            uint regularAlive=0,swarmAlive=0;
            foreach(bool swarm in new[]{false,true}){
                app.settings.ambientZombies=100000;app.settings.enemyAI=false;app.settings.world.seed=app.settings.seed=73129;app.settings.world.size=4096;
                if(swarm)app.StartFlatValidationBattle(50000, swarm:true);else app.StartFlatValidationBattle(50000);
                double deadline=Time.realtimeSinceStartupAsDouble+150;
                while((app.State=="loading"||app.State=="battle"&&(app.Battle.AmbientZombieRemaining>0||app.Battle.ZombieAlive<100000))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                try{Check(app.State=="battle","Starts "+(swarm?"swarm":"regular")+" match");Check(app.Battle.ZombieAlive==100000&&app.Battle.AmbientZombieRemaining==0,"Exactly 100K ambient zombies spawn in "+(swarm?"swarm":"regular")+" mode");Check(app.Battle.SwarmAlive==0,"Ambient population does not make a wave active");Check(app.settings.capacity==150000,"50K match reserves another 100K slots without reducing player capacity");}catch(Exception e){failure=e.ToString();break;}
                if(!swarm){regularAlive=app.Battle.ZombieAlive;bool sampled=false;UnitState[] glimpse=null;app.Battle.ReadUnitSample(8,1,data=>{glimpse=data;sampled=true;});while(!sampled)yield return null;
                    if(glimpse!=null){Vector2 p=glimpse[0].position;app.Battle.RequestSpawn(app.Base.Catalog.TypeIndex("scout"),0,p+new Vector2(80,0),p,null);app.View.GetComponent<RtsCamera>().FocusBase(p);double capture=Time.realtimeSinceStartupAsDouble+1;while(Time.realtimeSinceStartupAsDouble<capture)yield return null;Capture(Path.Combine(folder,"ambient-zombies.png"));yield return null;}
                    continue;}
                foreach(var building in app.Base.Buildings)building.health=1e9f;app.Swarm.Schedule.Advance(630);deadline=Time.realtimeSinceStartupAsDouble+90;
                while((app.Swarm.Schedule.Remaining>0||app.Battle.SwarmPending||app.Battle.SwarmAlive!=1000)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                try{Check(app.Battle.SwarmAlive==1000&&app.Swarm.Schedule.Remaining==0,"First wave adds 1000 zombies on top of the ambient population");}catch(Exception e){failure=e.ToString();break;}
                swarmAlive=app.Battle.SwarmAlive;bool done=false;UnitState[] before=null,after=null;int start=100004;
                app.Battle.ReadUnitSample(start,64,data=>{before=data;done=true;});while(!done)yield return null;
                uint tick=app.Battle.Tick;while(app.Battle.Tick<tick+240&&app.State=="battle")yield return null;
                done=false;app.Battle.ReadUnitSample(start,64,data=>{after=data;done=true;});while(!done)yield return null;
                try{
                    Check(before!=null&&after!=null,"Bounded wave sample reads back successfully");int moving=0;float travel=0;
                    for(int i=0;i<before.Length;i++)if((before[i].flags&131073)==131073&&(after[i].flags&1)!=0){float d=app.Battle.Field.Distance(before[i].position,after[i].position);if(d>25)moving++;travel+=d;}
                    var sample=new WaveSample{before=new Vector2[64],after=new Vector2[64],velocity=new Vector2[64],destination=new Vector2[64],flags=new uint[64],targets=new uint[64],moving=moving,meanTravel=travel/64};for(int i=0;i<64;i++){sample.before[i]=before[i].position;sample.after[i]=after[i].position;sample.velocity[i]=after[i].velocity;sample.destination[i]=after[i].destination;sample.flags[i]=after[i].flags;sample.targets[i]=after[i].target;}File.WriteAllText(Path.Combine(folder,"wave-sample.json"),JsonUtility.ToJson(sample,true));
                    Check(moving>=48,"At least 75% of the sampled wave stampedes over 25m in eight simulation seconds");Check(travel/64>40,"Wave front advances together rather than trickling stationary ranged attackers");
                }catch(Exception e){failure=e.ToString();break;}
            }
            File.WriteAllText(Path.Combine(folder,"player-validation.json"),JsonUtility.ToJson(new Report{status=failure.Length==0?"PASS":"FAIL",error=failure,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray(),regularAlive=regularAlive,swarmAlive=swarmAlive},true));Application.logMessageReceived-=Log;Application.Quit(failure.Length==0?0:1);
        }
        void Capture(string path)
        {
            var target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32);target.Create();var request=new RenderPipeline.StandardRequest{destination=target};
            RenderPipeline.SubmitRenderRequest(app.View,request);RenderPipeline.SubmitRenderRequest(app.View,request);var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("Zombie capture render fence failed");
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var active=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=active;File.WriteAllBytes(path,pixels.EncodeToPNG());Destroy(pixels);target.Release();Destroy(target);
        }
    }
}
