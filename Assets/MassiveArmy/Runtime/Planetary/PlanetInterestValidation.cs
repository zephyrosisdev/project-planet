using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-215)]
    public sealed class PlanetInterestValidation : MonoBehaviour
    {
        [Serializable]sealed class Report
        {
            public string status="PASS",error="",gpu,memoryScope="Unity tracked memory; not total VRAM",measurement="1440p explicit HDRP render fence; simulation wall time includes explicit army SRV dependency, completion dispatch and verified tick readback";
            public bool baseline,stampedTerrain;public int terrainCraters;public int zombies,units,activeZombies,dormantZombies,width=2560,height=1440;
            public Vector2 initialProbe;public float initialProbeTravel;public uint probeId;public Vector2 probeBefore,probeAfter,probeDestination,probeVelocity;public uint probeTicks;
            public double medianMs,p95Ms,simulationMedianMs,simulationP95Ms,simulationHz;public long allocatedBytes,reservedBytes;
            public double[] frameSamplesMs,simulationSamplesMs;
            public List<string> checks=new List<string>();
        }
        readonly Report report=new Report();readonly List<double> frames=new List<double>(),simulation=new List<double>();readonly Stopwatch timer=new Stopwatch();
        PlanetSimulationFence simulationFence;PlanetDevelopment world;RenderTexture target;RenderPipeline.StandardRequest request;string output;bool started,measured;double start,previous,phaseStart,readinessDeadline;uint firstTick,lastTick,dormantId;int phase;UnitState dormantBefore,actorBefore,combatZombie;Vector3 home;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-interest-validation")>=0)new GameObject("Planet area of interest validation").AddComponent<PlanetInterestValidation>();}
        string Arg(string name,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        void Start(){readinessDeadline=Time.realtimeSinceStartupAsDouble+120;output=Arg("-planet-output",Application.persistentDataPath);Directory.CreateDirectory(output);report.zombies=int.Parse(Arg("-planet-interest-count","50000"));report.baseline=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-no-interest")>=0;report.gpu=SystemInfo.graphicsDeviceName;world=FindAnyObjectByType<PlanetDevelopment>();Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);}
        UnitState ReadUnit(uint index){var data=new UnitState[1];world.UnitsForValidation.GetData(data,0,(int)index,1);return data[0];}
        uint ReadActivity(uint index){var data=new uint[1];world.EncounterForValidation.ActivityForValidation.GetData(data,0,(int)index,1);return data[0];}
        void LateUpdate(){try{
            if(world?.EncounterForValidation==null){if(Time.realtimeSinceStartupAsDouble>readinessDeadline)throw new TimeoutException("Planet stress session failed to initialize; inspect player.log");return;}
            if(!started){started=true;report.stampedTerrain=world.definition.stampTerrain;world.Paused=true;home=world.Rig.FocusDirection;Check(world.EncounterForValidation.Spawn(report.zombies,2,31,17)==report.zombies,"Requested zombie population fits without reducing the count");world.EncounterForValidation.PrepareInterestForValidation();report.initialProbe=ReadUnit(65).position;if(Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-interest-crater")>=0){world.definition.Edits.AddCrater(-home,Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-interest-large-crater")>=0?PlanetEncounter.TestNukeCraterRadius:120);report.terrainCraters=world.definition.Edits.Count;}foreach(var b in world.Base.Buildings)b.health=1000000000;
                target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;world.Rig.InputEnabled=false;request=new RenderPipeline.StandardRequest{destination=target};
                simulationFence=new PlanetSimulationFence(world.UnitsForValidation);world.SimulationBeginForValidation=()=>timer.Restart();world.SimulationEndForValidation=()=>{if(world.SimulationTicks!=lastTick)simulationFence.Wait(world.SimulationTicks,world.unitCount);timer.Stop();if(!measured&&Time.realtimeSinceStartupAsDouble-start>=5&&world.SimulationTicks!=lastTick)simulation.Add(timer.Elapsed.TotalMilliseconds);};
                world.Paused=false;start=previous=Time.realtimeSinceStartupAsDouble;firstTick=world.SimulationTicks;report.units=world.unitCount;
            }
            RenderPipeline.SubmitRenderRequest(world.View,request);var completion=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);completion.WaitForCompletion();double now=Time.realtimeSinceStartupAsDouble,elapsed=now-start;
            if(!measured&&elapsed>=5)frames.Add((now-previous)*1000);previous=now;lastTick=world.SimulationTicks;report.allocatedBytes=Math.Max(report.allocatedBytes,Profiler.GetTotalAllocatedMemoryLong());report.reservedBytes=Math.Max(report.reservedBytes,Profiler.GetTotalReservedMemoryLong());
            if(elapsed<20)return;
            if(!measured){measured=true;report.frameSamplesMs=frames.ToArray();report.simulationSamplesMs=simulation.ToArray();report.medianMs=Percentile(frames,.5);report.p95Ms=Percentile(frames,.95);report.simulationMedianMs=Percentile(simulation,.5);report.simulationP95Ms=Percentile(simulation,.95);report.simulationHz=(world.SimulationTicks-firstTick)/elapsed;
                Check(world.EncounterForValidation.ZombieCountForValidation()==report.zombies,"Every zombie retains a live GPU record after the timed run");report.initialProbeTravel=CubeSphere.ArcDistance(CubeSphere.Decode(report.initialProbe),CubeSphere.Decode(ReadUnit(65).position),world.definition.radius);Check(report.initialProbeTravel>.1f,"A distant zombie advances throughout the timed run");
                if(report.baseline){Finish();return;}
                var counts=new uint[6];world.EncounterForValidation.ActiveDispatchForValidation.GetData(counts);report.activeZombies=(int)counts[5];report.dormantZombies=report.zombies-report.activeZombies;
                Check(report.activeZombies>0&&report.activeZombies<report.zombies*.7,"At least 30% of the population skips detailed local steering/combat while nearby zombies remain active");
                dormantId=world.EncounterForValidation.FindDormantForValidation();Check(dormantId!=uint.MaxValue,"A persistent dormant slot is available for transition checks");dormantBefore=ReadUnit(dormantId);var soundOrigin=CubeSphere.Decode(dormantBefore.position);var soundTangent=Vector3.Cross(Mathf.Abs(soundOrigin.y)>.95f?Vector3.right:Vector3.up,soundOrigin).normalized;var epicentre=(soundOrigin+soundTangent*(380/world.definition.radius)).normalized;world.EncounterForValidation.EmitExplosionNoiseForValidation(epicentre,600);var heard=ReadUnit(dormantId);Check(CubeSphere.ArcDistance(CubeSphere.Decode(heard.destination),epicentre,world.definition.radius)<.2f,"Dormant zombie receives the exact nearby explosion epicentre without a coarse cell-centre substitution");Check(ReadActivity(dormantId)==0,"Explosion attraction preserves staggered dormant simulation outside the area of interest");report.probeId=dormantId;report.probeBefore=dormantBefore.position;report.probeTicks=world.SimulationTicks;phaseStart=now;
            }
            if(phase==0&&now-phaseStart>=2.5){var unit=ReadUnit(dormantId);report.probeAfter=unit.position;report.probeDestination=unit.destination;report.probeVelocity=unit.velocity;report.probeTicks=world.SimulationTicks-report.probeTicks;Check(unit.health==dormantBefore.health&&(unit.flags&1)!=0,"Dormancy preserves zombie identity, health and life state");Check(CubeSphere.ArcDistance(CubeSphere.Decode(unit.position),CubeSphere.Decode(dormantBefore.position),world.definition.radius)>.1f,"Dormant zombie continues staggered coarse movement");world.Rig.SetFocusDirection(CubeSphere.Decode(unit.position));phase=1;phaseStart=now;}
            else if(phase==1&&now-phaseStart>=.3){Check(ReadActivity(dormantId)==1,"Moving the observer to a distant zombie restores full simulation");actorBefore=ReadUnit(0);var actor=actorBefore;actor.position=ReadUnit(dormantId).position;actor.previous=actor.position;world.UnitsForValidation.SetData(new[]{actor},0,0,1);world.Rig.SetFocusDirection(home);phase=2;phaseStart=now;}
            else if(phase==2&&now-phaseStart>=.3){Check(ReadActivity(dormantId)==1,"Offscreen player forces keep nearby zombie combat active");
                combatZombie=ReadUnit(dormantId);var victim=combatZombie;victim.health=1;victim.target=uint.MaxValue;world.UnitsForValidation.SetData(new[]{victim},0,(int)dormantId,1);
                var actor=ReadUnit(0);actor.position=victim.position;actor.previous=actor.position;actor.packed=0;actor.flags=5;actor.cooldown=0;actor.target=uint.MaxValue;world.UnitsForValidation.SetData(new[]{actor},0,0,1);phase=3;phaseStart=now;}
            else if(phase==3&&now-phaseStart>=.5){var victim=ReadUnit(dormantId);Check((victim.flags&1)==0&&victim.health==0,"Detailed steering and combat execute for active zombies around offscreen troops");world.UnitsForValidation.SetData(new[]{combatZombie},0,(int)dormantId,1);world.UnitsForValidation.SetData(new[]{actorBefore},0,0,1);phase=4;phaseStart=now;}
            else if(phase==4&&now-phaseStart>=.3){Check(ReadActivity(dormantId)==0,"Leaving a region returns zombies to coarse simulation without despawning them");Check(world.EncounterForValidation.ZombieCountForValidation()==report.zombies,"Interest transitions preserve the complete requested population");Finish();}
        }catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);Finish();}}
        static double Percentile(List<double> data,double q){if(data.Count==0)return 0;var values=data.ToArray();Array.Sort(values);return values[(int)((values.Length-1)*q)];}
        void Finish(){File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));File.WriteAllText(Path.Combine(output,"frames.csv"),string.Join("\n",frames.ConvertAll(x=>x.ToString("F6",CultureInfo.InvariantCulture))));File.WriteAllText(Path.Combine(output,"simulation.csv"),string.Join("\n",simulation.ConvertAll(x=>x.ToString("F6",CultureInfo.InvariantCulture))));enabled=false;Application.Quit(report.status=="PASS"?0:1);}
        void OnDestroy(){simulationFence?.Dispose();if(world!=null){world.SimulationBeginForValidation=null;world.SimulationEndForValidation=null;}if(target!=null){target.Release();Destroy(target);}}
    }
}
