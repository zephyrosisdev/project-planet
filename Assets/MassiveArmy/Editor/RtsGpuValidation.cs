using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class RtsGpuValidation
    {
        static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
        static readonly List<string> checks=new List<string>();
        static GameObject root;
        static Battlefield field;
        static GpuBattle battle;
        static RtsCatalog catalog;
        static Camera camera;
        static string error;
        static double deadline;
        static readonly uint[] draws={3,3,6,6};
        static UnitState[] sample;
        static UnitOperations[] operations;
        static ProjectileState[] missiles;
        static ProjectileState[] projectiles;
        static bool pending;
        [Serializable] sealed class Report {public string status,error,unity,gpu,timestamp;public string[] checks;}
        [MenuItem("Massive Army/Validate RTS Combined Arms GPU")]
        public static void Start()
        {
            if(steps.Count>0)return;checks.Clear();error="";steps.Push(Run());deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Advance;
            AssemblyReloadEvents.beforeAssemblyReload+=BeforeReload;
            File.WriteAllText("TestResults/rts-gpu-progress.txt","Starting combined arms validation");
        }
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(steps.Count==0){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Advance()
        {
            try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Combined arms validation timed out");var top=steps.Peek();if(top.MoveNext()){if(top.Current is IEnumerator child)steps.Push(child);return;}steps.Pop();if(steps.Count>0)return;}
            catch(Exception e){error=e.ToString();Debug.LogException(e);}Finish();
        }
        static void BeforeReload(){error="Validation interrupted by script reload";Finish();}
        static void Finish()
        {
            steps.Clear();EditorApplication.update-=Advance;AssemblyReloadEvents.beforeAssemblyReload-=BeforeReload;
            File.WriteAllText("TestResults/rts-gpu-validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,timestamp=DateTime.UtcNow.ToString("O"),checks=checks.ToArray()},true));
            battle?.Dispose();field?.Dispose();if(root!=null)UnityEngine.Object.DestroyImmediate(root);battle=null;field=null;root=null;Debug.Log("RTS combined arms GPU validation "+(error.Length==0?"PASS":"FAIL"));
        }
        static void Require(bool ok,string text){if(!ok)throw new InvalidOperationException(text);checks.Add(text);File.WriteAllLines("TestResults/rts-gpu-progress.txt",checks);}
        static void Tick(){battle.AdvanceRtsValidation();battle.Update(camera,BattleSettings.Tick,draws,false);}
        static IEnumerator Ticks(int count){uint end=battle.RtsSteps+(uint)count;while(battle.RtsSteps<end){Tick();yield return null;}yield return null;}
        static IEnumerator ReadUnits(){pending=true;battle.ReadUnitSample(0,64,r=>{sample=r;pending=false;});while(pending)yield return null;Require(sample!=null,"Bounded unit validation readback succeeds");}
        static IEnumerator ReadOperations(){pending=true;battle.ReadValidationOperations(64,r=>{operations=r;pending=false;});while(pending)yield return null;Require(operations!=null,"Aircraft and transport state readback succeeds");}
        static IEnumerator ReadMissiles(){pending=true;battle.ReadValidationMissiles(r=>{missiles=r;pending=false;});while(pending)yield return null;}
        static IEnumerator ReadProjectiles(){pending=true;UnityEngine.Rendering.AsyncGPUReadback.Request(battle.Projectiles,r=>{projectiles=r.hasError?null:r.GetData<ProjectileState>().ToArray();pending=false;});while(pending)yield return null;Require(projectiles!=null,"Gunfire pool readback succeeds");}
        static IEnumerator CheckRenderedGunfire()
        {
            var target=new RenderTexture(1024,640,24);target.Create();
            var image=new Texture2D(1024,640,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try {
                camera.aspect=1.6f;
                camera.enabled=false;
                // Isolate the tracer from terrain/scenery appearing as editor imports settle.
                // A changing background invalidates a raw framebuffer subtraction.
                battle.Settings.trees=false;
                foreach(var background in root.GetComponentsInChildren<Renderer>())background.enabled=false;
                var hd=camera.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                hd.clearColorMode=UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;hd.backgroundColorHDR=Color.black;
                using(var renderer=new ArmyRenderer(battle)) {
                    battle.Update(camera,0,renderer.IndexCounts,true);renderer.Render(camera);
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
                    RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1024,640),0,0);image.Apply();var withShots=image.GetPixels32();
                    File.WriteAllBytes("TestResults/rts-gunfire.png",image.EncodeToPNG());
                    RenderTexture.active=previous;
                    // Indirect submissions survive until the end of this editor frame.
                    // Capture the disabled reference on a fresh frame so it cannot include these shots.
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    yield return null;
                    battle.Settings.vfx=false;battle.Update(camera,0,renderer.IndexCounts,true);renderer.Render(camera);
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
                    RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1024,640),0,0);image.Apply();var withoutShots=image.GetPixels32();
                    File.WriteAllBytes("TestResults/rts-no-gunfire.png",image.EncodeToPNG());
                    uint[] disabledArgs=null;battle.ReadDrawArguments(r=>disabledArgs=r);while(disabledArgs==null)yield return null;
                    Require(disabledArgs[16]==0,"Disabled gunfire has zero indirect instances");
                    int changed=0;for(int i=0;i<withShots.Length;i++)if(withShots[i].g>withoutShots[i].g+15||withShots[i].b>withoutShots[i].b+15)changed++;
                    Require(changed>8,"Actual HDRP framebuffer contains visible cyan gunfire ("+changed+" pixels)");
                    battle.Settings.vfx=true;battle.Update(camera,0,renderer.IndexCounts,true);
                }
            } finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static UnitState Unit(string id,int team,Vector2 p,bool selected=false) => new UnitState{position=p,previous=p,destination=p,health=(uint)catalog.Units[id].health,packed=(uint)(team|(catalog.TypeIndex(id)<<8)),flags=1u|(selected?2u:0),target=uint.MaxValue,group=uint.MaxValue};
        static void Fixture(params UnitState[] units)
        {
            var data=new UnitState[64];Array.Copy(units,data,units.Length);battle.SetValidationUnits(data);
            var ops=new UnitOperations[64];for(int i=0;i<units.Length;i++){var u=catalog.units[(units[i].packed>>8)&255];ops[i]=new UnitOperations{state=2,fuel=u.fuel,ammo=u.ammunition,home=units[i].position,mission=units[i].position,homeBuilding=uint.MaxValue,carrier=uint.MaxValue,capacity=(uint)u.transportCapacity};}battle.SetValidationOperations(ops);
            battle.UploadStructures(new GpuStructure[512],0);battle.SetValidationMissiles(new ProjectileState[32]);
            battle.Projectiles.SetData(new ProjectileState[battle.Settings.projectileCapacity]);
        }
        static IEnumerator Run()
        {
            catalog=RtsCatalog.Load();root=new GameObject("RTS combined arms GPU fixture"){hideFlags=HideFlags.DontSave};camera=new GameObject("GPU fixture camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,1600,-800);camera.transform.LookAt(Vector3.zero);
            var settings=new BattleSettings{ambientZombies=0,baseBuilding=true,activeUnits=32,capacity=64,navigationResolution=128,spatialResolution=64,worldSize=2048,projectileCapacity=256,enemyAI=false,movement=false,combat=false,targeting=false};
            field=new Battlefield(settings,root.transform);battle=new GpuBattle(settings,field,catalog.units.Select(u=>u.Gpu).ToArray());battle.ConfigureRoster(catalog.units);
            Vector2 p=Vector2.zero;bool found=false;float cell=field.Size/settings.spatialResolution;
            for(int i=0;i<field.Map.Height.Length;i++){Vector2 q=field.Map.Position(i);q=new Vector2(Mathf.Floor(q.x/cell)*cell+cell*.5f,Mathf.Floor(q.y/cell)*cell+cell*.5f);if(field.Deployment.Safe(q)&&field.Deployment.Safe(q+Vector2.one*5)){p=q;found=true;break;}}
            Require(found,"Combined arms fixtures use navigable dry terrain");
            camera.farClipPlane=10000;camera.transform.position=new Vector3(p.x+22,field.Height(p)+100,p.y-80);camera.transform.LookAt(new Vector3(p.x+22,field.Height(p),p.y));
            foreach(bool visuals in new[]{true,false}) {
                settings.vfx=visuals;
                var shooter=Unit("infantry",0,p);shooter.target=1;shooter.turret=shooter.facing=Mathf.PI*.5f;
                Fixture(shooter,Unit("heavy_infantry",1,p+Vector2.right*45));yield return Ticks(2);
                settings.combat=true;Tick();yield return ReadProjectiles();
                Require(projectiles.Any(s=>s.alive!=0&&s.owner==uint.MaxValue)==visuals,"Direct-damage infantry gunfire respects the VFX toggle (enabled="+visuals+")");
                if(visuals) {
                    uint[] args=null;battle.ReadDrawArguments(r=>args=r);while(args==null)yield return null;
                    Require(args[16]>0,"Infantry gunfire reaches the indirect projectile draw");
                    yield return CheckRenderedGunfire();
                }
                yield return ReadUnits();uint expected=(uint)catalog.Units["heavy_infantry"].health-(uint)(catalog.Units["infantry"].damage-catalog.Units["heavy_infantry"].armour*.5f);
                Require(sample[1].health==expected,"Gunfire visuals preserve direct damage and armour (enabled="+visuals+")");
                settings.combat=false;yield return Ticks(20);yield return ReadProjectiles();yield return ReadUnits();
                Require(projectiles.All(s=>s.alive==0)&&sample[1].health==expected,"Tracers expire without dealing damage again (enabled="+visuals+")");
            }
            settings.vfx=true;
            var turning=Unit("infantry",0,p);turning.target=1;Fixture(turning,Unit("heavy_infantry",1,p+Vector2.right*45));settings.combat=true;yield return Ticks(20);yield return ReadUnits();
            Require(Mathf.Abs(Mathf.DeltaAngle(sample[0].facing*Mathf.Rad2Deg,90))<5,"Infantry turns its body toward the enemy it is shooting");settings.combat=false;
            Fixture(Unit("apc",0,p,true),Unit("infantry",0,p+Vector2.right*20,true));yield return Ticks(3);battle.ControlGroup(1,true,false);battle.TransportOrder(true);yield return ReadUnits();
            Require((sample[1].flags&32768)!=0,"APC loads nearby infantry entirely on the GPU");yield return ReadOperations();Require(operations[0].payload==1,"Transport manifest counts loaded passengers");
            battle.ControlGroup(1,false,false);yield return ReadUnits();Require((sample[1].flags&2)==0,"Recalling a control group keeps carried passengers out of selection");
            battle.TransportOrder(false);yield return ReadUnits();Require((sample[1].flags&32768)==0&&Vector2.Distance(sample[1].position,p)<55,"Unload restores infantry at a navigable position");
            Fixture(Unit("infantry",0,p));bool transportSpawned=false;
            battle.RequestSpawn(catalog.TypeIndex("apc"),0,p,p,ok=>transportSpawned=ok);yield return Ticks(8);yield return ReadOperations();
            Require(transportSpawned&&operations.Any(o=>o.capacity==catalog.Units["apc"].transportCapacity),"GPU production uses the transport capacity authored in the catalogue");
            Fixture(Unit("fighter",0,p));var airOps=new UnitOperations[64];airOps[0]=new UnitOperations{state=3,fuel=1,ammo=0,home=p,mission=p+Vector2.right*200,homeBuilding=0,carrier=uint.MaxValue};battle.SetValidationOperations(airOps);
            var buildings=new GpuStructure[512];buildings[0]=new GpuStructure{position=p,health=4000,team=0,flags=5,vision=100};battle.UploadStructures(buildings,1);
            yield return Ticks(4);yield return ReadOperations();Require(operations[0].state==4,"Aircraft returns to a powered base and enters rearming");
            yield return Ticks(90);yield return ReadOperations();Require(operations[0].ammo>0&&operations[0].fuel>1&&operations[0].state!=4,"Airbase replenishes aircraft and returns it to service");
            Fixture(Unit("infantry",0,p),Unit("fighter",1,p+Vector2.right*250));
            buildings[0]=new GpuStructure{position=p,health=4000,team=0,flags=1,vision=50,detection=450,range=420,damage=250,targetMask=8,reload=.5f};battle.UploadStructures(buildings,1);yield return Ticks(2);yield return ReadUnits();
            Require(sample[1].health<catalog.Units["fighter"].health,"Networked SAM engages radar detected hostile aircraft");
            int fogX=Mathf.Clamp((int)(((p.x+250)/field.Size+.5f)*GpuBattle.FogResolution),0,GpuBattle.FogResolution-1),fogY=Mathf.Clamp((int)((p.y/field.Size+.5f)*GpuBattle.FogResolution),0,GpuBattle.FogResolution-1);
            Require(battle.RadarContacts!=null&&battle.RadarContacts[fogX+fogY*GpuBattle.FogResolution]==1,"Radar contacts remain generic outside visual range");
            Fixture(Unit("scout",0,p,true));yield return Ticks(3);battle.ScoutOrder(SearchPattern.AutoExplore,p,new Vector2(220,220));yield return ReadUnits();Vector2 explore=sample[0].destination;
            Require(Vector2.Distance(explore,p)>20&&(sample[0].flags&4096)!=0,"Auto Explore assigns a real unexplored GPU destination");
            battle.ScoutOrder(SearchPattern.Grid,p,new Vector2(220,220));yield return ReadUnits();Require(Vector2.Distance(sample[0].destination,explore)>10,"Grid search issues a different systematic route");
            Vector2 firstWaypoint=field.TraversableDestination(p+Vector2.right*100),secondWaypoint=field.TraversableDestination(p+Vector2.up*150);
            Require(Vector2.Distance(firstWaypoint,secondWaypoint)>20,"Patrol fixture has distinct navigable waypoints");
            Require(battle.ScoutRoute(new[]{firstWaypoint,secondWaypoint}),"Waypoint patrol command stores a bounded shared GPU route");yield return ReadUnits();
            Require(Vector2.Distance(sample[0].destination,firstWaypoint)<field.Size/field.Resolution*1.5f,"Scout patrol begins at the first authored waypoint (expected "+firstWaypoint+", actual "+sample[0].destination+", flags "+sample[0].flags+")");
            sample[0].position=sample[0].destination;battle.SetValidationUnits(sample);yield return Ticks(1);yield return ReadUnits();Require(Vector2.Distance(sample[0].destination,secondWaypoint)<field.Size/field.Resolution*1.5f,"Scout advances to the next authored patrol waypoint");
            Fixture(Unit("infantry",1,p));buildings[0]=new GpuStructure{position=p,health=4000,team=1,flags=17,vision=100,detection=600,range=600};battle.UploadStructures(buildings,1);
            var pool=new ProjectileState[32];pool[0]=new ProjectileState{position=new Vector3(p.x+30,100,p.y),velocity=new Vector3(p.x,field.Height(p),p.y),lifetime=5,damage=2000,team=0,alive=1,target=120};battle.SetValidationMissiles(pool);
            yield return Ticks(4);yield return ReadMissiles();Require(missiles!=null&&missiles[0].alive==0,"Powered radar linked missile defence intercepts a strategic missile");yield return ReadUnits();Require(sample[0].health>0,"Intercepted strategic missile causes no impact damage");
            buildings[0].flags=1;battle.UploadStructures(buildings,1);pool[0].lifetime=.1f;pool[0].alive=1;battle.SetValidationMissiles(pool);yield return Ticks(8);yield return ReadUnits();Require(sample[0].health==0,"Unintercepted strategic missile applies area damage through GPU unit health");
            bool spawned=false;battle.RequestSpawn(catalog.TypeIndex("infantry"),0,p,p,ok=>spawned=ok);yield return Ticks(8);yield return ReadUnits();Require(spawned&&sample.Any(u=>u.health>0&&(u.packed&255)==0),"Production recycles dead GPU slots safely");
            Fixture(Unit("tank",0,p));float damage=0;battle.OnStructureDamage=a=>damage+=a[0];buildings[0]=new GpuStructure{position=p+Vector2.right*30,health=4000,team=1,flags=1,armour=20};battle.UploadStructures(buildings,1);yield return Ticks(16);Require(damage>0,"Ground forces damage hostile building infrastructure");float unshielded=damage;
            Fixture(Unit("tank",0,p));damage=0;buildings[1]=new GpuStructure{position=p+Vector2.right*40,health=4000,team=1,flags=1,shield=180};battle.UploadStructures(buildings,2);yield return Ticks(16);Require(damage>0&&damage<unshielded,"Shield reduces damage without making structures invulnerable");
            Fixture(Unit("infantry",0,p,true),Unit("scout",0,p+Vector2.right*12,true));battle.ControlGroup(3,true,false);battle.Select(camera,new Rect(-1,-1,0,0),SelectionOperation.Replace,false);battle.ControlGroup(3,false,false);yield return ReadUnits();Require((sample[0].flags&2)!=0&&(sample[1].flags&2)!=0,"GPU control groups restore the selected army");
            settings.movement=true;battle.IssueCombinedOrder(camera,p+Vector2.right*90,ArmyOrder.Move,Vector2.zero);yield return ReadUnits();Require(sample[0].group!=sample[1].group&&sample[0].destination!=p,"Mixed movement classes receive separate GPU formation routes");
            yield return FormationAndStructureOrders(p);
        }
        static IEnumerator FormationAndStructureOrders(Vector2 p)
        {
            battle.Settings.movement=false;
            Fixture(Unit("infantry",0,p,true),Unit("scout",0,p+Vector2.right*10,true),Unit("tank",0,p+Vector2.right*20,true),Unit("fighter",0,p+Vector2.up*10,true));
            var layout=new FormationLayout(p+Vector2.right*65,Vector2.up,40);
            battle.PreviewFormation(camera,layout);
            Require(battle.PreviewActive&&battle.PreviewCount==4,"Mixed infantry, scout, tank and aircraft formation previews include every selected unit");
            Vector4[] ghosts=null;UnityEngine.Rendering.AsyncGPUReadback.Request(battle.PreviewSlots,4*16,0,r=>ghosts=r.hasError?null:r.GetData<Vector4>().ToArray());while(ghosts==null)yield return null;
            Require(ghosts.All(g=>g.z==0)&&ghosts.Select(g=>new Vector2(g.x,g.y)).Distinct().Count()==4,"Mixed movement formation ghosts occupy distinct traversable slots: "+string.Join(", ",ghosts.Select(g=>g.ToString("F2"))));
            Require(battle.IssueCombinedOrder(camera,layout.Front,ArmyOrder.Move,Vector2.zero,layout),"Dragged width and facing are accepted for all selected movement classes");yield return ReadUnits();
            for(int i=0;i<4;i++)Require(Vector2.Distance(sample[i].destination,new Vector2(ghosts[i].x,ghosts[i].y))<.1f,"Committed mixed formation matches ghost slot "+i);
            Require(sample.Take(4).Select(u=>u.group).Distinct().Count()==4,"Mixed formation preserves separate navigation domains");
            Fixture(Unit("infantry",0,p,true),Unit("infantry",0,p+Vector2.right*8,true),Unit("scout",0,p+Vector2.right*16,true),Unit("scout",0,p+Vector2.right*24,true));
            var wide=new FormationLayout(p+Vector2.right*60,Vector2.up,30);
            Require(battle.IssueCombinedOrder(camera,wide.Front,ArmyOrder.Move,Vector2.zero,wide),"Wide mixed squad formation accepts a dragged front");yield return ReadUnits();
            Require(Vector2.Dot((sample[0].destination-sample[1].destination).normalized,Vector2.up)<-.99f&&Vector2.Distance(sample[0].destination,sample[1].destination)>5,"Squad front follows the dragged orientation");
            var narrow=new FormationLayout(wide.Front,Vector2.up,0);Require(battle.IssueCombinedOrder(camera,narrow.Front,ArmyOrder.Move,Vector2.zero,narrow),"Narrow mixed squad formation can retarget existing route groups");yield return ReadUnits();
            Require(Mathf.Abs(sample[0].destination.y-sample[1].destination.y)<.1f&&Vector2.Distance(sample[0].destination,sample[1].destination)>5,"Narrow drag creates rear rows instead of discarding its width");
            Fixture(Unit("scout",0,p));float damage=0;battle.OnStructureDamage=a=>damage+=a[0];var buildings=new GpuStructure[512];
            buildings[0]=new GpuStructure{position=p+Vector2.right*40,radius=10,health=4000,team=1,flags=1,armour=20};battle.UploadStructures(buildings,1);
            bool visibleShot=false;for(int i=0;i<12;i++){Tick();yield return ReadProjectiles();visibleShot|=projectiles.Any(s=>s.alive!=0&&s.owner==uint.MaxValue);}yield return ReadUnits();
            Require(damage>0&&visibleShot,"Scout automatically damages a nearby enemy building and emits a visible shot");
            Require(Mathf.Abs(Mathf.DeltaAngle(sample[0].facing*Mathf.Rad2Deg,90))<5,"Scout body and turret face the building it attacks");
            Vector2 start=p;bool found=false;
            foreach(var q in field.Map.Height.Select((h,i)=>field.Map.Position(i))) {
                bool safe=true;
                for(int step=0;step<=10&&safe;step++)foreach(var offset in new[]{new Vector2(-3,-3),new Vector2(3,-3),new Vector2(-3,3),new Vector2(3,3)})
                    if(field.Deployment.Costs[field.Map.Height.Length+field.Map.Index(q+Vector2.right*(step*16)+offset)]>=DeploymentMap.Blocked*.5f){safe=false;break;}
                if(safe&&field.Deployment.Safe(q)){start=q;found=true;break;}
            }
            Require(found,"Building attack fixture has a navigable approach outside weapon range");
            Fixture(Unit("scout",0,start,true));damage=0;buildings[0].position=start+Vector2.right*160;buildings[1]=buildings[0];buildings[1].position=start+Vector2.right*145+Vector2.up*8;battle.UploadStructures(buildings,2);
            float[] damageByBuilding=new float[2];battle.OnStructureDamage=a=>{damage+=a[0];damageByBuilding[0]+=a[0];damageByBuilding[1]+=a[1];};
            battle.Settings.movement=true;battle.Settings.targeting=true;battle.Settings.combat=true;
            Require(battle.AttackStructure(camera,0,buildings[0].position,buildings[0].radius),"Enemy building click produces an accepted GPU attack order");yield return ReadUnits();
            Require(sample[0].target==0x80000000u&&(sample[0].flags&65536)!=0,"Attack order retains the exact clicked building beyond weapon range");
            float initial=Vector2.Distance(sample[0].position,buildings[0].position);for(int i=0;i<600&&damage==0;i++){Tick();if(i%4==0)yield return null;}
            yield return ReadUnits();
            Require(damage>0&&Vector2.Distance(sample[0].position,buildings[0].position)<initial-30,"Scout moves into range and attacks the requested building (start "+start+", position "+sample[0].position+", destination "+sample[0].destination+", flags "+sample[0].flags+", cooldown "+sample[0].cooldown+", damage "+damage+")");
            Require(damageByBuilding[1]==0,"Explicit building attack does not switch to another nearby structure");
            buildings[0].health=0;battle.UploadStructures(buildings,2);yield return Ticks(4);yield return ReadUnits();Require(sample[0].target!=0x80000000u,"Destroyed building releases the attack target safely");
            battle.Settings.combat=false;battle.Settings.targeting=false;
            Require(battle.IssueCombinedOrder(camera,start,ArmyOrder.Move,Vector2.zero),"A move command overrides the previous building attack");yield return ReadUnits();Require((sample[0].flags&65536)==0,"New movement clears the explicit building attack flag");
        }
    }
}
