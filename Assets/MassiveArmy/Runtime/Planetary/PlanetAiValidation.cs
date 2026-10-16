using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-210)]
    public sealed class PlanetAiValidation : MonoBehaviour
    {
        [Serializable]sealed class Report
        {
            public string status="PASS",error="",gpu,memoryScope="Unity tracked allocation/reservation; not total VRAM",measurement="Simulation wall time includes explicit army SRV dependency, completion dispatch and verified tick readback; rendering is fenced separately";
            public int width=2560,height=1440,zombies=4096,units,centredSpawns,uniqueSpawns,subcellBinsX,subcellBinsY,lateMoving,lateAlive,lateEngaged,lateQueued,queuedZombies,resumedZombies,distinctAiObjectives;
            public double medianMs,p95Ms,simulationMedianMs,simulationP95Ms,simulationHz;public long allocatedBytes,reservedBytes;
            public List<string> checks=new List<string>();
        }
        [Serializable]sealed class ProbeRecords{public UnitState[] units;}
        readonly List<int> queuedZombieIds=new List<int>();float[] buildingHealth;Vector2[] blockedPositions;bool unblockedChecked;
        readonly HashSet<Vector2> objectives=new HashSet<Vector2>();
        readonly Report report=new Report();readonly List<double> frames=new List<double>(),simulation=new List<double>();
        PlanetSimulationFence simulationFence;PlanetDevelopment world;RenderTexture target;RenderPipeline.StandardRequest request;string output;bool baseline,started,measured,roamingChecked,hearingChecked;Vector2 objectiveBeforeRetask,isolatedSpawn,roamerBefore;int isolatedId;double start,previous;uint firstTick,lastTick;readonly Stopwatch timer=new Stopwatch();UnitState[] probe;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();if(!Application.isEditor&&Array.IndexOf(args,"-planet-ai-validation")>=0)new GameObject("Planet AI regression").AddComponent<PlanetAiValidation>();}
        void Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i<0?Application.persistentDataPath:args[i+1];Directory.CreateDirectory(output);baseline=Array.IndexOf(args,"-planet-ai-baseline")>=0;world=FindAnyObjectByType<PlanetDevelopment>();world.currentRtsContent=true;world.sessionMode=PlanetGameMode.AiTest;world.ambientZombies=0;world.requestedCapacity=8192;report.gpu=SystemInfo.graphicsDeviceName;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
        void Check(bool valid,string message){if(valid)report.checks.Add(message);else if(!baseline)throw new InvalidOperationException(message);else report.checks.Add("BASELINE FAIL: "+message);}
        void LateUpdate(){try{
            if(world?.EncounterForValidation==null)return;
            if(!started){started=true;world.Paused=true;int before=world.unitCount;Check(world.EncounterForValidation.Spawn(report.zombies,2,31,17)==report.zombies,"Requested zombie count is preserved");var records=new UnitState[report.zombies];world.UnitsForValidation.GetData(records,0,before,records.Length);var unique=new HashSet<string>();var x=new HashSet<int>();var y=new HashSet<int>();var surface=new PlanetSurface(world.definition);
                foreach(var u in records){var d=CubeSphere.Decode(u.position);var cell=CubeSphere.Cell(world.definition.bodyId,d,world.navigationResolution);if(CubeSphere.ArcDistance(d,CubeSphere.Direction(cell,world.navigationResolution),world.definition.radius)<.001f)report.centredSpawns++;unique.Add(u.position.x.ToString("F6")+":"+u.position.y.ToString("F6"));float fx=(u.position.x-Mathf.Floor(u.position.x))*world.navigationResolution;float fy=u.position.y*world.navigationResolution;x.Add(Mathf.Clamp((int)((fx-Mathf.Floor(fx))*16),0,15));y.Add(Mathf.Clamp((int)((fy-Mathf.Floor(fy))*16),0,15));if(surface.SampleHeight(d)<world.definition.oceanHeight)throw new InvalidOperationException("Spawn is underwater");}
                isolatedSpawn=records[0].position;isolatedId=before;report.uniqueSpawns=unique.Count;report.subcellBinsX=x.Count;report.subcellBinsY=y.Count;Check(report.centredSpawns<report.zombies/100,"Zombies are not snapped into navigation-cell rows");Check(unique.Count>report.zombies*.99f,"At least 99% of spawned surface positions are distinct");Check(x.Count>=14&&y.Count>=14,"Spawn jitter covers the cell in both dimensions");
                // Common, deterministic initial positions for the before/after simulation measurements.
                probe=new UnitState[world.unitCount];world.UnitsForValidation.GetData(probe);for(int n=0;n<probe.Length;n++){probe[n].health=1000000000;probe[n].cooldown=1000000;if(n>=before){var direction=world.Base.Offset(new Vector2(-200+(n%64)*6,-250+(n/64)*6));if(surface.SampleHeight(direction)>=world.definition.oceanHeight)probe[n].position=CubeSphere.Encode(direction);else probe[n].position=CubeSphere.Encode(world.Rig.FocusDirection);probe[n].previous=probe[n].position;}}
                world.UnitsForValidation.SetData(probe);foreach(var b in world.Base.Buildings)b.health=1000000000;
                simulationFence=new PlanetSimulationFence(world.UnitsForValidation);world.SimulationBeginForValidation=()=>timer.Restart();world.SimulationEndForValidation=()=>{if(world.SimulationTicks!=lastTick)simulationFence.Wait(world.SimulationTicks,world.unitCount);timer.Stop();if(!measured&&Time.realtimeSinceStartupAsDouble-start>=5&&world.SimulationTicks!=lastTick)simulation.Add(timer.Elapsed.TotalMilliseconds);};
                target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;world.Rig.InputEnabled=false;request=new RenderPipeline.StandardRequest{destination=target};world.Paused=false;start=previous=Time.realtimeSinceStartupAsDouble;firstTick=world.SimulationTicks;report.units=world.unitCount;
            }
            RenderPipeline.SubmitRenderRequest(world.View,request);var completion=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);completion.WaitForCompletion();double now=Time.realtimeSinceStartupAsDouble,elapsed=now-start;if(!measured&&elapsed>=5)frames.Add((now-previous)*1000);previous=now;lastTick=world.SimulationTicks;report.allocatedBytes=Math.Max(report.allocatedBytes,Profiler.GetTotalAllocatedMemoryLong());report.reservedBytes=Math.Max(report.reservedBytes,Profiler.GetTotalReservedMemoryLong());
            objectives.Add(world.NavigationForValidation.GoalForValidation(15));
            if(elapsed<35)return;
            if(!measured){
                measured=true;world.UnitsForValidation.GetData(probe);report.lateAlive=probe.Count(u=>(u.flags&1)!=0);report.lateMoving=probe.Count(u=>(u.flags&1)!=0&&u.velocity.sqrMagnitude>.25f);report.simulationHz=(world.SimulationTicks-firstTick)/elapsed;report.medianMs=Percentile(frames,.5);report.p95Ms=Percentile(frames,.95);report.simulationMedianMs=Percentile(simulation,.5);report.simulationP95Ms=Percentile(simulation,.95);Check(!world.Paused&&world.SimulationTicks-firstTick>900,"Simulation continues beyond repeated AI order intervals");
                if(!baseline){
                    var engagedRecords=new bool[probe.Length];
                    for(int record=0;record<probe.Length;record++){var u=probe[record];if((u.flags&1)==0||u.velocity.sqrMagnitude>.25f||u.target==uint.MaxValue)continue;int type=(int)(u.packed>>8&255);float range=type==31?ZombieDefinition.Archetype.attackRange:world.Base.Catalog.units[type].range;var d=CubeSphere.Decode(u.position);bool engaged=false;
                        if((u.target&0x80000000u)!=0){int b=(int)(u.target&0x7fffffffu);if(b<world.Base.Buildings.Count){var structure=world.Base.Buildings[b];float radius=Mathf.Sqrt(structure.definition.footprintWidth*structure.definition.footprintWidth+structure.definition.footprintDepth*structure.definition.footprintDepth)*4;engaged=!structure.removed&&CubeSphere.ArcDistance(d,CubeSphere.Decode(structure.position),world.definition.radius)<=range+radius+.1f;}}
                        else if(u.target<probe.Length)engaged=(probe[u.target].flags&1)!=0&&CubeSphere.ArcDistance(d,CubeSphere.Decode(probe[u.target].position),world.definition.radius)<=range+.1f;
                        if(engaged){report.lateEngaged++;engagedRecords[record]=true;}}
                    CountBlockedQueues(engagedRecords);
                    File.WriteAllText(Path.Combine(output,"idle-probes.json"),JsonUtility.ToJson(new ProbeRecords{units=probe}));
                    Check(report.lateMoving+report.lateEngaged+report.lateQueued>report.zombies*.9f,"At least 90% are moving, in firing range, or in a physically blocked queue connected to a fighter after 35 seconds");
                    objectiveBeforeRetask=world.NavigationForValidation.GoalForValidation(15);blockedPositions=probe.Select(u=>u.position).ToArray();buildingHealth=world.Base.Buildings.Select(b=>b.health).ToArray();
                    // Remove the obstruction rather than accepting permanent 'valid queue' idling.
                    var release=(UnitState[])probe.Clone();for(int n=0;n<release.Length;n++)if((release[n].flags&262144u)==0)release[n].flags=0;
                    foreach(var b in world.Base.Buildings)b.health=0;world.UnitsForValidation.SetData(release);
                }
            }
            if(!baseline&&elapsed>=44&&!unblockedChecked){
                var resumed=new UnitState[probe.Length];world.UnitsForValidation.GetData(resumed);foreach(int i in queuedZombieIds)if((resumed[i].flags&1)!=0&&CubeSphere.ArcDistance(CubeSphere.Decode(blockedPositions[i]),CubeSphere.Decode(resumed[i].position),world.definition.radius)>2)report.resumedZombies++;
                Check(queuedZombieIds.Count==0||report.resumedZombies>=queuedZombieIds.Count*.9f,"At least 90% of blocked zombies resume movement after combat obstructions are removed");
                for(int n=0;n<resumed.Length;n++){if((probe[n].packed&255)==0)resumed[n]=probe[n];else resumed[n].flags=0;}for(int n=0;n<buildingHealth.Length;n++)world.Base.Buildings[n].health=buildingHealth[n];
                resumed[isolatedId]=new UnitState{position=isolatedSpawn,previous=isolatedSpawn,destination=isolatedSpawn,packed=2u|(31u<<8),health=1000000000,flags=1u|262144u|524288u,target=uint.MaxValue,group=17};roamerBefore=isolatedSpawn;world.UnitsForValidation.SetData(resumed);unblockedChecked=true;
            }
            if(!baseline&&elapsed>=46&&!roamingChecked){var unit=new UnitState[1];world.UnitsForValidation.GetData(unit,0,isolatedId,1);Check(CubeSphere.ArcDistance(CubeSphere.Decode(roamerBefore),CubeSphere.Decode(unit[0].position),world.definition.radius)>3,"An isolated ambient zombie continues roaming without an army target");roamerBefore=unit[0].position;var c=CubeSphere.Cell(world.definition.bodyId,CubeSphere.Decode(roamerBefore),world.navigationResolution);var neighbour=CubeSphere.Neighbour(c,1,0,world.navigationResolution);world.EncounterForValidation.EmitNoiseForValidation(CubeSphere.Direction(neighbour,world.navigationResolution));roamingChecked=true;}

            if(!baseline&&elapsed>=48&&!hearingChecked){Check(world.EncounterForValidation.HeardCellForValidation(CubeSphere.Decode(roamerBefore))>0,"Gunfire hearing propagates through the six-face cell map");var dead=new UnitState[1];world.UnitsForValidation.GetData(dead,0,isolatedId,1);dead[0].flags=0;world.UnitsForValidation.SetData(dead,0,isolatedId,1);hearingChecked=true;}
            if(!baseline&&elapsed<58)return;
            report.distinctAiObjectives=objectives.Count;Check(objectives.Count>=3,"AI chooses new objectives across multiple order intervals");
            if(!baseline)Check(world.NavigationForValidation.GoalForValidation(15)!=objectiveBeforeRetask,"AI resumes patrol after all enemy targets disappear");
            var command=world.Base.Buildings.First(b=>b.definition.id=="command");world.Base.Damage(command,(uint)Mathf.CeilToInt(command.health));Check(!world.Paused,"AI-test simulation keeps running after command-centre destruction");
            Finish();
        }catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);Finish();}}
        // Bounded fixture analysis only. A queue must have a stopped blocker ahead,
        // within hull spacing, and connect to an actual fighter; a stale target alone is insufficient.
        void CountBlockedQueues(bool[] engaged){
            int count=probe.Length;var positions=new Vector3[count];var radii=new float[count];var followers=new List<int>[count];var connected=(bool[])engaged.Clone();var queue=new Queue<int>();
            for(int i=0;i<count;i++){positions[i]=CubeSphere.Decode(probe[i].position)*world.definition.radius;int type=(int)(probe[i].packed>>8&255);radii[i]=type==31?ZombieDefinition.Archetype.radius:world.Base.Catalog.units[type].radius;if(engaged[i])queue.Enqueue(i);}
            for(int i=0;i<count;i++){var u=probe[i];if((u.flags&1)==0||u.velocity.sqrMagnitude>.25f||engaged[i])continue;Vector3 goal=CubeSphere.Decode(u.destination)*world.definition.radius;
                if(u.target<count)goal=positions[u.target];else if(u.target!=uint.MaxValue&&(u.target&0x80000000u)!=0){int b=(int)(u.target&0x7fffffffu);if(b<world.Base.Buildings.Count)goal=CubeSphere.Decode(world.Base.Buildings[b].position)*world.definition.radius;}
                var heading=(goal-positions[i]).normalized;if(heading.sqrMagnitude<.5f)continue;
                for(int j=0;j<count;j++){if(i==j||(probe[j].flags&1)==0||probe[j].velocity.sqrMagnitude>.25f||(u.packed&255)!=(probe[j].packed&255))continue;var delta=positions[j]-positions[i];float distance=delta.magnitude;if(distance>radii[i]+radii[j]+.9f||distance<1e-5f||Vector3.Dot(delta/distance,heading)<.4f)continue;if(followers[j]==null)followers[j]=new List<int>();followers[j].Add(i);}
            }
            while(queue.Count>0){int leader=queue.Dequeue();if(followers[leader]==null)continue;foreach(int follower in followers[leader])if(!connected[follower]){connected[follower]=true;queue.Enqueue(follower);}}
            for(int i=0;i<count;i++)if(connected[i]&&!engaged[i]){report.lateQueued++;if((probe[i].flags&262144u)!=0)queuedZombieIds.Add(i);}report.queuedZombies=queuedZombieIds.Count;
        }
        static double Percentile(List<double> values,double q){if(values.Count==0)return 0;var sorted=values.OrderBy(x=>x).ToArray();return sorted[(int)((sorted.Length-1)*q)];}
        void Finish(){File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(output,"frames.csv"),string.Join("\n",frames.Select(x=>x.ToString("F6",System.Globalization.CultureInfo.InvariantCulture))));File.WriteAllText(Path.Combine(output,"simulation.csv"),string.Join("\n",simulation.Select(x=>x.ToString("F6",System.Globalization.CultureInfo.InvariantCulture))));enabled=false;Application.Quit(report.status=="PASS"?0:1);}
        void OnDestroy(){simulationFence?.Dispose();if(world!=null){world.SimulationBeginForValidation=null;world.SimulationEndForValidation=null;}if(target!=null){target.Release();Destroy(target);}}
    }
}
