using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class SwarmValidation
    {
        static IEnumerator routine;static string error="";static double deadline;static GameObject root;static GpuBattle battle;static Battlefield field;static RtsWorld world;static Camera view;
        static readonly List<string> checks=new List<string>();static readonly uint[] draws={3,3,6,6};
        [Serializable]sealed class Report{public string status,error,gpu;public string[] checks;}
        [MenuItem("Massive Army/Validate Swarm Survival")]
        public static void Start(){if(routine!=null)return;if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before validation.");checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Advance;AssemblyReloadEvents.beforeAssemblyReload+=Interrupt;}
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(routine==null){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Interrupt(){error="Interrupted by assembly reload";Finish();}
        static void Advance(){try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Swarm validation timed out");if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();Debug.LogException(e);}Finish();}
        static void Finish(){EditorApplication.update-=Advance;AssemblyReloadEvents.beforeAssemblyReload-=Interrupt;routine=null;Cleanup();Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/swarm-validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,gpu=SystemInfo.graphicsDeviceName,checks=checks.ToArray()},true));Debug.Log("Swarm validation "+(error.Length==0?"PASS":"FAIL"));}
        static void Cleanup(){world?.Dispose();world=null;battle?.Dispose();battle=null;field?.Dispose();field=null;if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        static void CostsForAllTeams(float[] costs){for(int team=0;team<field.CostTeams;team++)field.Costs.SetData(costs,0,team*costs.Length,costs.Length);}
        static void Step(bool paused=false){battle.AdvanceRtsValidation();battle.AdvanceSwarmValidation();battle.Update(view,BattleSettings.Tick,draws,paused);EditorApplication.QueuePlayerLoopUpdate();}
        static void Fixture(int capacity)
        {
            root=new GameObject("Swarm validation fixture"){hideFlags=HideFlags.DontSave};view=new GameObject("Fixture camera").AddComponent<Camera>();view.transform.SetParent(root.transform);view.enabled=false;view.orthographic=true;view.orthographicSize=240;view.transform.SetPositionAndRotation(new Vector3(0,500,-300),Quaternion.Euler(55,0,0));view.farClipPlane=7000;
            var cfg=new WorldGenerationSettings{size=2048,oceanCoverage=0,riverAbundance=0,lakeAbundance=0,reliefHeight=0,mountainCoverage=0,forestCoverage=0,roadDensity=0,rockAbundance=0};
            var settings=new BattleSettings{world=cfg,activeUnits=32,capacity=capacity,projectileCapacity=256,ambientZombies=0,baseBuilding=true,swarmMode=true,enemyAI=false,simulationLod=false,crowdPressure=false,targeting=false,combat=false};settings.ScaleWorld();
            var map=new WorldMap(256,2048,cfg.seed,cfg);for(int i=0;i<map.Height.Length;i++){map.Height[i]=12;map.Slope[i]=0;}
            settings.preview=WorldSetupResult.FromMap(map,new DeploymentMap(map));field=new Battlefield(settings,root.transform);var cat=RtsCatalog.Load();battle=new GpuBattle(settings,field,cat.units.Select(u=>u.Gpu).ToArray());battle.ConfigureRoster(cat.units);
        }
        static IEnumerator Run()
        {
            var clock=new SwarmSchedule();clock.Advance(599);Check(!clock.Warning&&!clock.Active,"No attackers during the ten-minute preparation period");clock.Advance(1);Check(clock.Warning&&clock.Countdown==30,"Warning starts at ten minutes with exactly thirty seconds remaining");clock.Advance(29);Check(!clock.Active,"No attackers before the warning countdown finishes");clock.Advance(0);Check(clock.Countdown==1,"A paused clock does not advance");clock.Advance(1);Check(clock.Active&&clock.Remaining==1000,"The first wave requests exactly one thousand attackers");clock.ConfirmSpawn(800);Check(!clock.TryClear(0,false),"Empty GPU slots do not clear a wave with unspawned reinforcements");clock.ConfirmSpawn(200);Check(!clock.TryClear(1,false)&&!clock.TryClear(0,true),"Survival waits for all attackers and pending GPU acknowledgements");Check(clock.TryClear(0,false)&&clock.WaveSize==2000&&clock.Countdown==630,"Survival doubles the next wave and resets preparation");clock.Advance(630);clock.ConfirmSpawn(2000);clock.TryClear(0,false);Check(clock.WaveSize==4000,"Wave sizes progress from 1000 to 2000 to 4000");
            for(int i=0;i<53;i++){clock.Advance(630);clock.ConfirmSpawn(clock.Remaining);clock.TryClear(0,false);}
            Check(clock.Exhausted&&clock.WaveSize>ulong.MaxValue/2,"Wave doubling never silently overflows the supported counter");
            Fixture(2048);
            var components=(int[])field.Deployment.Components.Clone();int component=components[field.Map.Index(Vector2.zero)];
            for(int i=0;i<components.Length;i++){Vector2 p=field.Map.Position(i);field.Deployment.Components[i]=Mathf.Abs(p.x)<=650&&Mathf.Abs(p.y)<=650?component:-1;}
            var islandEntries=SwarmMode.SpawnSites(field,Vector2.zero);Check(islandEntries.Length>=4&&islandEntries.All(p=>Mathf.Abs(p.x)<=650&&Mathf.Abs(p.y)<=650),"Island maps use reachable inland perimeter entries when canonical edges are inaccessible");
            for(int i=0;i<components.Length;i++){Vector2 p=field.Map.Position(i);field.Deployment.Components[i]=p.magnitude<100?component:-1;}
            bool rejected=false;try{SwarmMode.SpawnSites(field,Vector2.zero);}catch(InvalidOperationException){rejected=true;}Check(rejected,"An island without distant invasion sites is rejected instead of silently resizing the world");Array.Copy(components,field.Deployment.Components,components.Length);
            world=new RtsWorld(battle,root.transform,RtsCatalog.Load());var mode=new SwarmMode(world);
            Check(world.Buildings.Count==1&&world.Buildings[0].team==0&&!world.EnemyEnabled,"Swarm starts with one player base and no opposing economy");Check(battle.SwarmEntryCount>=4,"Several reachable perimeter entry sites are available");
            for(int i=0;i<100;i++){Step();yield return null;}
            // Keep light vehicles unreachable for this wave. Failed procedural draws
            // must recycle their slots and continue drawing a reachable combat type.
            var restricted=(float[])field.Deployment.Costs.Clone();int cells=field.Resolution*field.Resolution;
            for(int i=0;i<cells;i++)restricted[cells+i]=DeploymentMap.Blocked;CostsForAllTeams(restricted);battle.RefreshNavigation(new Bounds(Vector3.zero,Vector3.one*2048));
            mode.Schedule.Advance(630);mode.Update(0);
            for(int i=0;i<200&&(mode.Schedule.Remaining>0||battle.SwarmPending);i++){mode.Update(0);Step();yield return null;}
            Check(mode.Schedule.Remaining==0&&battle.SwarmAlive==1000,$"GPU spawning acknowledges exactly 1000 enemies without duplicating simulation identities (remaining {mode.Schedule.Remaining}, alive {battle.SwarmAlive}, pending {battle.SwarmPending})");
            Check(mode.Schedule.Remaining==0,"Unreachable movement-class draws are reclaimed and rerolled until the full wave arrives");
            bool done=false;uint[] stats=null;battle.RequestStatistics(data=>{stats=data;done=true;});while(!done)yield return null;Check(stats!=null&&stats[0]==4&&stats[112]==1000&&stats[1]==0,"Player starter units and swarm enemy counts remain independent");
            done=false;UnitState[] sample=null;battle.ReadUnitSample(4,64,data=>{sample=data;done=true;});while(!done)yield return null;
            Check(sample!=null&&sample.All(u=>(u.flags&131073)==131073&&(u.packed&255)==2&&Mathf.Abs(u.position.x)<1024&&Mathf.Abs(u.position.y)<1024),"A bounded sample contains canonical GPU swarm identities");Check(sample.All(u=>((u.packed>>8)&255)==battle.ZombieType),"A wave contains only the dedicated melee zombie archetype");
            var kill=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassiveArmy/Editor/SwarmValidation.compute");battle.DamageSwarmForValidation(kill);
            for(int i=0;i<200&&mode.Schedule.Wave==1;i++){Step();yield return null;mode.Update(0);}Check(mode.Schedule.Wave==2&&mode.Schedule.WaveSize==2000,$"GPU deaths release slots and survival advances the live mode (alive {battle.SwarmAlive}, pending {battle.SwarmPending})");
            mode.Schedule.Advance(630);mode.Update(0);for(int i=0;i<200;i++){Step();yield return null;mode.Update(0);if(mode.Schedule.Remaining==160&&battle.SwarmAlive==1840&&!battle.SwarmPending)break;}
            Check(mode.Schedule.Remaining==160&&battle.SwarmAlive==1840,"Oversized waves preserve the remaining attackers and reserve space for player production");
            battle.DamageSwarmForValidation(kill);for(int i=0;i<150;i++){Step();yield return null;mode.Update(0);if(mode.Schedule.Remaining==0&&battle.SwarmAlive==160&&!battle.SwarmPending)break;}Check(mode.Schedule.Remaining==0&&battle.SwarmAlive==160,$"Unspawned attackers arrive after previous attackers free GPU slots (remaining {mode.Schedule.Remaining}, alive {battle.SwarmAlive}, wave {mode.Schedule.Wave})");
            Cleanup();Fixture(64);battle.Settings.combat=true;battle.Settings.targeting=true;var structures=new[]{new GpuStructure{position=new Vector2(-1000,0),radius=8,health=100000,team=0,vision=200},new GpuStructure{position=Vector2.zero,radius=8,health=100000,team=0,vision=200}};
            var costs=(float[])field.Deployment.Costs.Clone();int total=field.Resolution*field.Resolution;
            for(int m=0;m<3;m++)for(int y=112;y<=144;y++)foreach(int x in new[]{0,255})costs[m*total+x+y*256]=DeploymentMap.Blocked;CostsForAllTeams(costs);
            battle.UploadStructures(structures,2);battle.ConfigureSwarm(new[]{(uint)RtsCatalog.Load().TypeIndex("infantry")},new[]{new Vector2(1000,0),new Vector2(900,100)});uint damage=0;battle.OnStructureDamage=data=>damage+=data[0];
            bool acknowledged=false;battle.RequestSwarm(16,99,count=>{Check(count==16,"Bounded seam attack fixture spawns all requested units");acknowledged=true;});
            for(int i=0;i<25;i++){Step(true);yield return null;}Check(!acknowledged,"Pausing prevents queued swarm spawning");
            for(int i=0;i<1800;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,16,data=>{sample=data;done=true;});while(!done)yield return null;
            Check(acknowledged&&sample.All(u=>u.target==0x80000000u),"Swarm units choose the closest reachable building across the world seam");Check(damage>0,"Swarm attackers travel to and damage player buildings across the seam");
            Check(sample.All(u=>costs[field.Map.Index(u.position)]<DeploymentMap.Blocked*.5f),"Swarm steering and contact resolution preserve a blocked seam barrier");
            structures[0].health=0;battle.UploadStructures(structures,2);for(int i=0;i<180;i++){Step();yield return null;}
            done=false;battle.ReadUnitSample(0,16,data=>{sample=data;done=true;});while(!done)yield return null;Check(sample.All(u=>u.target==0x80000001u),"Destroying a target rebuilds shared routes and retargets surviving attackers");
        }
    }
}
