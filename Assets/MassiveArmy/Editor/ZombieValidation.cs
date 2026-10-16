using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools
{
    public static class ZombieValidation
    {
        static IEnumerator routine;static GameObject root;static GpuBattle battle;static Battlefield field;static RtsWorld world;static Camera view;static string error="",gpuError="";static double deadline;static bool hostilityOnly;
        static readonly List<string> checks=new List<string>();static readonly uint[] draws={3,3,6,6};
        [Serializable]sealed class Report{public string status,error,gpu;public string[] checks;}
        [MenuItem("Massive Army/Validate Zombie Hearing and Hostility")]
        public static void Start(){if(routine!=null)return;hostilityOnly=false;checks.Clear();error=gpuError="";Application.logMessageReceived+=GpuLog;routine=Run();deadline=EditorApplication.timeSinceStartup+600;EditorApplication.update+=Advance;}
        public static void StartHostility(){if(routine!=null)return;Start();hostilityOnly=true;}
        static void GpuLog(string text,string stack,LogType type){if(text.Contains("Skipping dispatch")||text.Contains("Compute shader")&&text.Contains("not set")||text.Contains("Shader error"))gpuError=text;}
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(routine==null){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Advance(){try{if(gpuError.Length>0)throw new InvalidOperationException(gpuError);if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Zombie fixture timed out");if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();Debug.LogException(e);}EditorApplication.update-=Advance;Application.logMessageReceived-=GpuLog;routine=null;Cleanup();Directory.CreateDirectory("TestResults/Zombies");File.WriteAllText("TestResults/Zombies/editor-validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray()},true));}
        static void Cleanup(){world?.Dispose();world=null;battle?.Dispose();battle=null;field?.Dispose();field=null;if(root)UnityEngine.Object.DestroyImmediate(root);}
        static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);checks.Add(message);Directory.CreateDirectory("TestResults/Zombies");File.WriteAllLines("TestResults/Zombies/progress.txt",checks);}
        static void Step(bool paused=false){battle.AdvanceRtsValidation();battle.AdvanceSwarmValidation();battle.Update(view,BattleSettings.Tick,draws,paused);EditorApplication.QueuePlayerLoopUpdate();}
        static UnitState Zombie(Vector2 p,bool wave=false)=>new UnitState{position=p,previous=p,destination=p,health=160,packed=2u|((uint)battle.ZombieType<<8),flags=1u|8u|262144u|(wave?131072u:4u),target=uint.MaxValue,group=uint.MaxValue};
        static IEnumerator Run()
        {
            root=new GameObject("Zombie GPU fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("Fixture camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.orthographic=true;view.orthographicSize=300;view.transform.SetPositionAndRotation(new Vector3(0,500,-300),Quaternion.Euler(55,0,0));view.farClipPlane=7000;
            var cfg=new WorldGenerationSettings{size=2048,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,activeUnits=128,capacity=128,ambientZombies=0,baseBuilding=true,swarmMode=true,enemyAI=false,simulationLod=false,crowdPressure=false,targeting=false,combat=false,projectileCapacity=256};settings.ScaleWorld();
            var map=new WorldMap(256,2048,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);var cat=RtsCatalog.Load();battle=new GpuBattle(settings,field,cat.units.Select(u=>u.Gpu).ToArray());battle.ConfigureRoster(cat.units);
            battle.UploadStructures(new[]{new GpuStructure{position=new Vector2(0,-800),radius=8,health=100000,team=0},new GpuStructure{position=new Vector2(0,800),radius=8,health=100000,team=1}},2);
            battle.ConfigureSwarm(new[]{(uint)battle.ZombieType},new[]{new Vector2(700,0),new Vector2(700,100)});
            var units=new UnitState[128];units[0]=Zombie(new Vector2(400,0));units[1]=Zombie(new Vector2(960,0));battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);
            bool done=false;UnitState[] sample=null;
            if(!hostilityOnly){
            Check(field.CostTeams==3,"Third navigation cost bank prevents zombies using either faction's gates");
            for(int i=0;i<10;i++){Step();yield return null;}
            battle.ReadUnitSample(0,2,d=>{sample=d;done=true;});while(!done)yield return null;
            Check(sample.All(u=>(u.flags&524288)!=0&&u.velocity.magnitude>0&&u.velocity.magnitude<=2.6f),"Ambient zombies wander at walking pace outside visual and acoustic contact");
            Check(Vector2.Dot(sample[0].velocity.normalized,sample[1].velocity.normalized)<.98f,"Ambient wandering directions differ between zombies");
            settings.simulationLod=true;Vector3 focus=WorldTopology.ViewFocus(view);Vector2 distant=field.Canonical(new Vector2(focus.x,focus.z)+new Vector2(1000,1000));units[2]=Zombie(distant);battle.SetValidationUnits(units);
            for(int i=0;i<30;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(2,1,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&524288)!=0&&field.Distance(sample[0].position,distant)>1.5f&&field.Distance(sample[0].position,distant)<2.6f,"Distant idle movement LOD preserves walking speed rather than slowing simulation time");settings.simulationLod=false;
            var injection=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassiveArmy/Editor/ZombieValidation.compute");
            int gateCell=WorldTopology.Index(field.StructureResolution/2,field.StructureResolution/2,field.StructureResolution);field.StructureCells.SetData(new[]{2u},0,gateCell,1);
            using(var gateResults=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,4)){int k=injection.FindKernel("GateQueries");injection.SetInt("_StructureRes",field.StructureResolution);injection.SetFloat("_WorldSize",field.Size);injection.SetVector("_GatePoint",new Vector4(1,1,0,0));injection.SetBuffer(k,"_StructureCells",field.StructureCells);injection.SetBuffer(k,"_GateResults",gateResults);injection.Dispatch(k,1,1,1);var answers=new uint[3];gateResults.GetData(answers);Check(answers[0]==1&&answers[1]==0&&answers[2]==0,"An owner-open gate admits its owner and blocks enemy armies and zombies");}
            field.StructureCells.SetData(new[]{0u},0,gateCell,1);battle.InjectZombieExplosion(injection,Vector2.zero,40,2);
            for(int i=0;i<100;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,2,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&524288)!=0&&sample[0].velocity.magnitude<=2.6f,"A small blast does not interrupt wandering beyond its hearing radius");
            battle.InjectZombieExplosion(injection,Vector2.zero,10000,120);
            for(int i=0;i<120;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,2,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&4)==0&&sample[0].destination.sqrMagnitude<1&&sample[0].position.x<390,"A nuclear-strength blast wakes distant zombies and drives them toward its epicentre");
            uint tick=battle.Tick;Vector2 before=sample[0].position;for(int i=0;i<15;i++){Step(true);yield return null;}
            done=false;battle.ReadUnitSample(0,2,d=>{sample=d;done=true;});while(!done)yield return null;Check(battle.Tick==tick&&sample[0].position==before,"Pause freezes hearing lifetime and zombie movement");
            units=new UnitState[128];units[0]=Zombie(new Vector2(960,200));battle.SetValidationUnits(units);battle.InjectZombieExplosion(injection,new Vector2(-960,200),100,4);
            for(int i=0;i<100;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,1,d=>{sample=d;done=true;});while(!done)yield return null;
            Check(field.Distance(sample[0].destination,new Vector2(-960,200))<1&&field.Distance(sample[0].position,new Vector2(-960,200))<125,"Explosion hearing and pursuit cross a wrapping world seam");
            Check(battle.SwarmAlive==0,"Ambient zombies are excluded from wave-clear accounting");
            // Exercise the real N-key GPU blast beyond its 36-second acoustic lifetime.
            units=new UnitState[128];units[0]=Zombie(new Vector2(960,0));units[1]=Zombie(new Vector2(-960,0),true);battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);battle.RequestTestNuke(Vector2.zero);
            for(int i=0;i<1200;i+=4){for(int j=0;j<4;j++)Step();yield return null;}
            done=false;battle.ReadUnitSample(0,1,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&(4|524288))==0&&sample[0].velocity.magnitude>8&&field.Distance(sample[0].destination,Vector2.zero)<1,"Distant zombies continue sprinting toward an N-key blast after its sound expires");
            for(int i=0;i<1800;i+=4){for(int j=0;j<4;j++)Step();yield return null;}
            done=false;battle.ReadUnitSample(0,2,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&524288)!=0&&sample[0].velocity.magnitude<=2.6f,"Ambient zombies resume walking after completing an investigation");
            Check((sample[1].flags&65536)!=0&&(sample[1].flags&524288)==0,"Wave zombies resume attacking a base after investigating a blast");
            // Sound crosses an impassable offshore strip, while movement stays on land.
            var coast=(float[])field.Deployment.Costs.Clone();int total=field.Resolution*field.Resolution;
            for(int cell=0;cell<total;cell++){Vector2 p=field.Map.Position(cell);if(p.x>=600&&p.x<=760&&p.y>=200&&p.y<=400)coast[cell]=DeploymentMap.Blocked;}
            for(int team=0;team<field.CostTeams;team++)field.Costs.SetData(coast,0,team*coast.Length,coast.Length);
            units=new UnitState[128];units[0]=Zombie(new Vector2(500,300));battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);battle.InjectZombieExplosion(injection,new Vector2(650,300),20000,130);
            for(int i=0;i<110;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,1,d=>{sample=d;done=true;});while(!done)yield return null;
            Check(sample[0].destination.x>649&&sample[0].destination.x<651&&sample[0].position.x>520&&sample[0].position.x<600,"Offshore explosions are heard and pursued without walking into impassable terrain");
            for(int i=0;i<1800;i+=4){for(int j=0;j<4;j++)Step();yield return null;}
            done=false;battle.ReadUnitSample(0,1,d=>{sample=d;done=true;});while(!done)yield return null;
            Check((sample[0].flags&524288)!=0,"An unreachable offshore investigation times out and returns to wandering");
            for(int team=0;team<field.CostTeams;team++)field.Costs.SetData(field.Deployment.Costs,0,team*coast.Length,coast.Length);
            }
            // Hostility uses the same spatial targeting and combat kernels as the armies.
            var roster=new UnitArchetype[cat.units.Length+1];battle.TypeBuffer.GetData(roster);Check(roster[battle.ZombieType].damage==24&&roster[battle.ZombieType].attackRange>2,"GPU zombie archetype contains melee damage and range");
            settings.targeting=true;settings.combat=true;uint infantry=(uint)cat.TypeIndex("infantry");units=new UnitState[128];units[0]=Zombie(new Vector2(300,0));units[1]=new UnitState{position=new Vector2(301,0),previous=new Vector2(301,0),destination=new Vector2(301,0),packed=infantry<<8,health=5000,flags=1|4|2,target=uint.MaxValue,group=uint.MaxValue};units[2]=Zombie(new Vector2(-300,0));units[3]=units[1];units[3].flags&=~2u;units[3].packed|=1;units[3].position=units[3].previous=units[3].destination=new Vector2(-301,0);battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);
            // Keep both pairs alive through target slicing and aim rotation. The long
            // pursuit fixture changes tick phase, which must not decide this assertion.
            units[0].health=units[2].health=5000;battle.SetValidationUnits(units);
            for(int i=0;i<90;i++){Step();yield return null;}
            Check(battle.Settings.combat,"Combat is enabled for the hostility fixture");
            done=false;battle.ReadUnitSample(0,4,d=>{sample=d;done=true;});while(!done)yield return null;
            Check(sample[1].health<5000&&sample[3].health<5000,"Zombies attack both armies: "+string.Join("; ",sample.Select(u=>$"health={u.health} pos={u.position} target={u.target} flags={u.flags} facing={u.facing}")));
            Check(sample[0].health<5000&&sample[2].health<5000,"Both armies can damage neutral zombies");
            Vector4 clip=GL.GetGPUProjectionMatrix(view.projectionMatrix,false)*view.worldToCameraMatrix*new Vector4(sample[0].position.x,13,sample[0].position.y,1);var mouse=new Vector2(clip.x/clip.w*.5f+.5f,clip.y/clip.w*.5f+.5f);Check(battle.IssueOrder(view,sample[0].position,ArmyOrder.Attack,mouse),"Player attack order is accepted: "+battle.Notice);
            done=false;battle.ReadUnitSample(1,1,d=>{sample=d;done=true;});while(!done)yield return null;Check(sample[0].target==0,"Player attack picking includes visible hostile zombies");
            using(var art=new ArtUnitRenderer()){Check(art.NearMesh!=null&&art.FarMesh!=null,"Shared unarmed zombie geometry fits both GPU vertex banks");}
            // Building pose is emitted by the actual weapon kernel, including the
            // eight-UAV D3D11 zombie variant; no CPU scan picks a visual target.
            settings.targeting=false;settings.combat=true;units=new UnitState[128];
            units[0]=new UnitState{position=new Vector2(100,0),previous=new Vector2(100,0),destination=new Vector2(100,0),health=5000,packed=infantry<<8|1u,flags=1|4,target=uint.MaxValue,group=uint.MaxValue};
            battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);battle.FogBuffer.SetData(Enumerable.Repeat(2u,battle.FogBuffer.count).ToArray());
            battle.UploadStructures(new[]{new GpuStructure{position=Vector2.zero,radius=4,health=10000,team=0,vision=600,detection=600,range=300,damage=1,flags=1,targetMask=31,reload=.25f}},1);
            battle.ResetStructureAim(0);for(int i=0;i<20;i++){Step();yield return null;}
            var aim=new Vector4[1];battle.StructureAimBuffer.GetData(aim,0,0,1);
            Check(aim[0].w==1&&Mathf.Abs(aim[0].x-Mathf.PI*.5f)<.01f,"Building turret pose faces the GPU-selected firing target");
            units[0].position=units[0].previous=units[0].destination=new Vector2(-1000,0);battle.SetValidationUnits(units);
            battle.UploadStructures(new[]{new GpuStructure{position=new Vector2(1000,0),radius=4,health=10000,team=0,vision=600,detection=600,range=300,damage=1,flags=1,targetMask=31,reload=.25f}},1);
            for(int i=0;i<20;i++){Step();yield return null;}battle.StructureAimBuffer.GetData(aim,0,0,1);
            Check(aim[0].w==1&&Mathf.Abs(aim[0].x-Mathf.PI*.5f)<.01f&&aim[0].y< -990,"Building turret aim follows the shortest direction across a wrapping seam");
            battle.ResetStructureAim(0);battle.StructureAimBuffer.GetData(aim,0,0,1);Check(aim[0]==Vector4.zero,"Reused building slots clear stale turret pose");
            // A bounded, revealed render fixture checks the actual indirect shader,
            // independently of scouts dying before a distant ambient screenshot.
            settings.combat=false;settings.targeting=false;units=new UnitState[128];
            for(int i=0;i<12;i++)units[i]=Zombie(new Vector2((i%4-1.5f)*4,(i/4-1)*5));
            battle.SetValidationUnits(units);battle.SetValidationOperations(new UnitOperations[128]);
            battle.FogBuffer.SetData(Enumerable.Repeat(2u,battle.FogBuffer.count).ToArray());
            view.orthographicSize=12;view.aspect=16f/9;view.transform.SetPositionAndRotation(new Vector3(0,35,-24),Quaternion.Euler(43,0,0));
            using(var renderer=new ArmyRenderer(battle)){
                battle.Update(view,BattleSettings.Tick,renderer.IndexCounts,true);
                done=false;uint[] args=null;battle.ReadDrawArguments(data=>{args=data;done=true;});while(!done)yield return null;
                Check(args!=null&&args[1]+args[6]+args[11]==12,"Revealed zombies enter GPU draw lists");
                var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);target.Create();
                renderer.Render(view);RenderPipeline.SubmitRenderRequest(view,new RenderPipeline.StandardRequest{destination=target});
                var fence=AsyncGPUReadback.Request(target);fence.WaitForCompletion();Check(!fence.hasError,"Indirect zombie render completes without GPU errors");
                var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=previous;
                Directory.CreateDirectory("TestResults/Zombies");File.WriteAllBytes("TestResults/Zombies/zombie-render.png",pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
