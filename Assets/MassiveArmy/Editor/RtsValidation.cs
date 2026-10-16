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
    public static class RtsValidation
    {
        static IEnumerator routine;
        static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
        static GameObject root;
        static Battlefield field;
        static GpuBattle battle;
        static RtsWorld world;
        static Camera camera;
        static string error;
        static double deadline;
        static readonly List<string> checks=new List<string>();
        static readonly uint[] draws={3,3,6,6};
        [Serializable] sealed class Report {public string status,error,unity,gpu,timestamp;public string[] checks;public int buildings,produced;}
        [MenuItem("Massive Army/Validate RTS Foundation")]
        public static void Start(){if(routine!=null)return;checks.Clear();steps.Clear();error="";routine=Run();steps.Push(routine);deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Advance;AssemblyReloadEvents.beforeAssemblyReload+=BeforeReload;}
        static void BeforeReload(){error="Validation interrupted by script reload";Finish();}
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(routine==null){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Advance()
        {
            try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("RTS validation timed out");if(steps.Count>0){var top=steps.Peek();if(top.MoveNext()){if(top.Current is IEnumerator child)steps.Push(child);return;}steps.Pop();if(steps.Count>0)return;}}
            catch(Exception e){error=e.ToString();Debug.LogException(e);}Finish();
        }
        static void Finish()
        {
            EditorApplication.update-=Advance;AssemblyReloadEvents.beforeAssemblyReload-=BeforeReload;routine=null;
            File.WriteAllText("TestResults/rts-validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,timestamp=DateTime.UtcNow.ToString("O"),checks=checks.ToArray(),buildings=world?.Buildings.Count??0,produced=battle?.ProducedUnits??0},true));
            world?.Dispose();battle?.Dispose();field?.Dispose();world=null;battle=null;field=null;if(root!=null)UnityEngine.Object.DestroyImmediate(root);Debug.Log("RTS validation "+(error.Length==0?"PASS":"FAIL"));
        }
        static void Require(bool result,string description){if(!result)throw new InvalidOperationException(description);checks.Add(description);File.WriteAllLines("TestResults/rts-validation-progress.txt",checks);}
        static void Step(float dt=.25f){world.Update(dt);battle.AdvanceRtsValidation();battle.Update(camera,BattleSettings.Tick,draws,false);}
        static IEnumerator AdvanceSeconds(float seconds){for(float t=0;t<seconds;t+=.25f){Step();yield return null;}}
        static RtsBuilding Build(string id)
        {
            Require(world.TryFindPlacement(id,0,out var p),"Placement found for "+id);var result=world.Construct(id,0,p,true);Require(result!=null,"Paid construction accepted for "+id);return result;
        }
        static IEnumerator Run()
        {
            var catalog=RtsCatalog.Load();Require(catalog.buildings.Length>=39&&catalog.units.Length>=31&&catalog.technologies.Length>=20,"Catalogue contains complete building, combined arms and technology rosters");
            Require(System.Runtime.InteropServices.Marshal.SizeOf<GpuStructure>()==64&&System.Runtime.InteropServices.Marshal.SizeOf<UnitTraits>()==64,"Structure and sensor GPU layouts match 64 byte HLSL records");
            root=new GameObject("RTS validation fixture"){hideFlags=HideFlags.DontSave};camera=new GameObject("Validation camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,1700,-1000);camera.transform.LookAt(Vector3.zero);
            var settings=new BattleSettings{ambientZombies=0,baseBuilding=true,activeUnits=32,capacity=512,worldSize=2048,navigationResolution=128,spatialResolution=64,projectileCapacity=256,enemyAI=false};
            field=new Battlefield(settings,root.transform);battle=new GpuBattle(settings,field,catalog.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,catalog){EnemyEnabled=false};
            for(int j=0;j<20;j++){Step();yield return null;}
            bool read=false;uint[] stats=null;battle.RequestStatistics(s=>{stats=s;read=true;});while(!read)yield return null;
            Require(stats!=null&&stats[0]==4&&stats[1]==4,"Initial expeditionary forces exist only in GPU unit buffers");
            var f=world.Factions[0];var command=world.Buildings[0];
            Require(!world.ValidatePlacement(catalog.Buildings["mine"],0,command.position,true).valid,"Placement rejects overlap and missing resource nodes");
            Require(!world.ValidatePlacement(catalog.Buildings["fusion"],0,command.position+new Vector2(100,0),true).valid,"Placement enforces technology locks");
            var wind=Build("wind");var mine=Build("mine");var lab=Build("lab");var barracks=Build("barracks");var factory=Build("factory");
            yield return AdvanceSeconds(25);Require(wind.Complete&&mine.Complete&&factory.Complete,"Construction completes real structures");
            float before=f.wallet.Stock.materials;yield return AdvanceSeconds(8);Require(f.wallet.Stock.materials>before&&world.Nodes[mine.node].remaining<world.Nodes[mine.node].capacity,"Extractor deposits income and depletes deterministic finite node");
            Require(f.generation>0&&f.demand>0,"Generation capacity and power demand are tracked separately");
            wind.powered=false;yield return AdvanceSeconds(1);Require(f.efficiency<1,"Generator power down slows the economy and production");wind.powered=true;
            Require(world.EnqueueUnit(barracks,"infantry")&&world.EnqueueUnit(barracks,"engineer"),"Production queue accepts multiple paid orders");world.Reorder(barracks,1,0);Require(barracks.queue[0].id=="engineer","Production queue reorders");
            before=f.wallet.Stock.materials;world.Cancel(barracks,0);Require(barracks.queue.Count==1&&f.wallet.Stock.materials>before,"Cancellation refunds unfinished production");
            int produced=battle.ProducedUnits;barracks.rally=field.TraversableDestination(barracks.position+new Vector2(100,0));yield return AdvanceSeconds(40);
            double spawnDeadline=EditorApplication.timeSinceStartup+8;
            while(barracks.queue.Count>0&&EditorApplication.timeSinceStartup<spawnDeadline){Step(.05f);yield return null;}
            Require(battle.ProducedUnits>produced&&barracks.queue.Count==0,"Completed production is acknowledged by GPU spawn kernel (produced "+battle.ProducedUnits+", queue "+barracks.queue.Count+", committing "+barracks.awaitingSpawn.Count+")");
            Require(!world.CanProduce(factory,"tank",out _),"Factory starts with tank technology locked");
            f.wallet.Stock=new ResourceCost{materials=2800,fuel=1500,rareElements=800,research=1500};Require(world.Upgrade(command),"Command Centre upgrade spends resources");yield return AdvanceSeconds(40);Require(f.tier==2,"Command upgrade raises maximum technology era");
            Require(world.EnqueueResearch(lab,"mechanized"),"Research queue accepts affordable prerequisite graph node");yield return AdvanceSeconds(80);Require(f.HasTech("mechanized"),"Timed research unlocks actual technology");
            Require(world.Upgrade(factory),"Factory upgrade accepted after command progression");yield return AdvanceSeconds(55);Require(factory.level==2&&factory.Bays==2&&world.CanProduce(factory,"tank",out _),"Factory upgrade adds parallel bays and unlocks tanks");
            // Test late domains with explicit fixture unlocks; the earlier test proves real research gating.
            f.tier=5;command.level=5;foreach(var tech in catalog.technologies)f.technologies.Add(tech.id);f.wallet.Capacity=new ResourceCost{materials=100000,fuel=100000,rareElements=100000,isotopes=100000,research=100000};f.wallet.Stock=f.wallet.Capacity;
            bool aircraft=false;Vector2 ap=command.position+new Vector2(100,0);Require(battle.RequestSpawn(catalog.TypeIndex("fighter"),0,ap,ap+new Vector2(150,0),ok=>aircraft=ok),"Aircraft uses GPU spawn command");
            Vector2 water=Vector2.zero;bool found=false;for(int i=0;i<field.Map.Height.Length;i++)if(field.Deployment.Costs[4*field.Map.Height.Length+i]<DeploymentMap.Blocked*.5f){water=field.Map.Position(i);found=true;break;}Require(found,"Procedural map provides navigable water");bool ship=false;battle.RequestSpawn(catalog.TypeIndex("patrol_boat"),0,water,water,ok=>ship=ok);
            spawnDeadline=EditorApplication.timeSinceStartup+8;
            while((!aircraft||!ship)&&EditorApplication.timeSinceStartup<spawnDeadline){Step(.05f);yield return null;}
            Require(aircraft&&ship,"Air and naval archetypes enter the same GPU simulation (air "+aircraft+", naval "+ship+", produced "+battle.ProducedUnits+")");
            Require(battle.PlayerVisibility!=null&&battle.PlayerVisibility.Any(v=>v==2)&&battle.PlayerVisibility.Any(v=>v==0),"GPU fog retains distinct visible and unknown cells");
            var wall=Build("wall");yield return AdvanceSeconds(5);world.Sell(wall);Require(wall.removed,"Decommission removes structure and releases its local navigation footprint");
            world.EnemyEnabled=true;settings.enemyAI=true;yield return AdvanceSeconds(120);Require(world.Buildings.Count(b=>!b.removed&&b.team==1)>2,"Enemy uses paid construction to build an economy and power");
            read=false;battle.RequestStatistics(s=>{stats=s;read=true;});while(!read)yield return null;Require(stats!=null&&stats[0]>=5,"GPU production preserves live army counts");
            Require(!UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t=>t.GetComponent<Rigidbody>()!=null||t.GetComponent<UnityEngine.AI.NavMeshAgent>()!=null),"No per-unit rigidbodies or NavMeshAgents are created");
            var largeSettings=new BattleSettings{world=new WorldGenerationSettings{size=16384},ambientZombies=0,baseBuilding=true,activeUnits=32,capacity=64,projectileCapacity=64,enemyAI=false};largeSettings.ScaleWorld();
            using(var largeField=new Battlefield(largeSettings,root.transform))
            using(var largeBattle=new GpuBattle(largeSettings,largeField,catalog.units.Select(u=>u.Gpu).ToArray()))
            using(var largeWorld=new RtsWorld(largeBattle,root.transform,catalog)){
                Require(largeField.Size>=10000&&largeWorld.Buildings.Count==2&&largeWorld.Nodes.Count>=16,"Million-scale terrain supports modest starting bases and resource placement across sea sectors");
                Require(largeWorld.Nodes.All(n=>largeWorld.Nodes.All(other=>other==n||largeField.Distance(n.position,other.position)>24)),"Large-map resource deposits retain separate usable wrapped footprints");
            }
        }
    }
}
