using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed class PlanetArrivalGravityValidation:MonoBehaviour {
        [Serializable] sealed class Report {public string status="PASS",error="";public int runtimeErrors;public double expectedGravity,heldWarpPull,releasedWarpPull,warpExitRelativeSpeed,hoverDrift,frameMedianMs,frameP95Ms,simulationSecondsPerSecond;public long allocatedBytes;public List<string> checks=new List<string>(),failures=new List<string>();}
        readonly Report report=new Report();PlanetDevelopment world;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-arrival-gravity-check")>=0)new GameObject("Unassisted interplanetary arrival validation").AddComponent<PlanetArrivalGravityValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.requestedCapacity=4096;world.ambientZombies=0;world.sessionMode=PlanetGameMode.AiTest;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=Log;}
        void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        void Check(bool value,string text){if(value)report.checks.Add(text);else{report.status="FAIL";report.failures.Add(text);}}
        IEnumerator Start(){var run=Run();while(true){object next;try{if(!run.MoveNext())break;next=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();break;}yield return next;}Check(report.runtimeErrors==0,"No runtime/shader errors");File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        static double Dot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        IEnumerator Run(){double deadline=Time.realtimeSinceStartupAsDouble+120;while(!world.Ready&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;if(!world.Ready)throw new TimeoutException("Home initialization");world.Paused=true;var runtime=world.SystemRuntime;var home=world;var body=runtime.Bodies[1];world.SetShipControl(true);var ship=world.ShipForValidation;var state=ship.Pose;
            double shell=body.radius+Math.Max(body.ReliefBound+6000,body.hasAtmosphere?body.atmosphereHeight:0);state.position=body.position.Add(new Double3(0,0,-shell-100000)).RelativeTo(world.definition.position);state.linearVelocity=new Double3();state.rotation=Quaternion.identity;ship.ResetForSystemValidation(state);
            Check(!ship.FlightAssistEnabled,"Departure and warp use unassisted flight");ship.Integrate(.05f,Vector3.zero,Vector3.zero,false,false,true);var intercepted=ship.Pose;
            Check(!ship.CruiseActive,"Warp stops at foreign planet boundary");report.warpExitRelativeSpeed=(ship.Pose.linearVelocity+world.definition.velocity-body.velocity).Magnitude;Check(report.warpExitRelativeSpeed<1,"Warp exit preserves normal speed relative to the destination, rather than departure orbital motion");
            var radial=ship.Position.RelativeTo(body.position);var inward=radial*(-1/radial.Magnitude);report.expectedGravity=body.gravitationalParameter/(radial.Magnitude*radial.Magnitude);var before=ship.Pose.linearVelocity;
            for(int i=0;i<20;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero,false,false,true);
            report.heldWarpPull=Dot(ship.Pose.linearVelocity-before,inward);Check(report.heldWarpPull>report.expectedGravity*.5,"Gravity resumes after interception even if K remains held");
            ship.ResetForSystemValidation(intercepted);before=ship.Pose.linearVelocity;for(int i=0;i<20;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero);report.releasedWarpPull=Dot(ship.Pose.linearVelocity-before,inward);Check(report.releasedWarpPull>report.expectedGravity*.5,"Foreign planet attracts unassisted ship with K released before terrain handoff");
            deadline=Time.realtimeSinceStartupAsDouble+120;while(!runtime.IsPreparedForValidation(1)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;if(!runtime.IsPreparedForValidation(1))throw new TimeoutException("Destination preparation");
            world.Paused=false;deadline=Time.realtimeSinceStartupAsDouble+5;while(runtime.ActiveWorld==world&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Check(runtime.ActiveWorld!=home,"Actual unassisted approach automatically hands off to the second planet");world=runtime.ActiveWorld;world.Paused=true;ship=world.ShipForValidation;Check(!ship.FlightAssistEnabled,"Handoff preserves unassisted mode");
            before=ship.Pose.linearVelocity;radial=ship.Pose.position;inward=radial*(-1/radial.Magnitude);for(int i=0;i<20;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero);Check(Dot(ship.Pose.linearVelocity-before,inward)>report.expectedGravity*.4,"Destination gravity continues after automatic handoff");
            // Hover near the other planet outside its full-detail activation range.
            // This exercises reference tracking before terrain handoff, not just after it.
            var target=runtime.Bodies[0];state=ship.Pose;state.position=target.position.Add(new Double3(0,0,-target.radius-runtime.Settings.DetailAltitude(target)-50000)).RelativeTo(world.definition.position);state.linearVelocity=target.velocity-world.definition.velocity;ship.ResetForSystemValidation(state);ship.SetFlightAssistance(true);var offset=ship.Position.RelativeTo(target.position);world.Paused=false;
            double start=runtime.Galaxy.Time,wall=Time.realtimeSinceStartupAsDouble;var frames=new List<double>();deadline=wall+30;while(runtime.Galaxy.Time-start<2&&Time.realtimeSinceStartupAsDouble<deadline){yield return null;frames.Add(Time.unscaledDeltaTime*1000);}
            report.hoverDrift=(runtime.ActiveWorld.ShipForValidation.Position.RelativeTo(target.position)-offset).Magnitude;Check(runtime.ActiveWorld==world,"Hover test remains outside foreign planet detail activation range");Check(report.hoverDrift<10,"Assisted hover follows the nearby foreign planet before terrain handoff");
            frames.Sort();if(frames.Count>0){report.frameMedianMs=frames[frames.Count/2];report.frameP95Ms=frames[(int)((frames.Count-1)*.95)];}report.simulationSecondsPerSecond=(runtime.Galaxy.Time-start)/(Time.realtimeSinceStartupAsDouble-wall);report.allocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        }
    }
}
