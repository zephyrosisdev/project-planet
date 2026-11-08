#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using Unity.Services.Core;
using UnityEngine;

namespace MassiveArmy.Networking {
    // Multiple independent authenticated clients, each running the production
    // session/prediction path. This avoids launching 100 copies of the engine.
    // It is scripted gameplay load, never a rendered/full-gameplay capacity pass.
    internal sealed partial class SurvivalLoadProbe:MonoBehaviour {
        [Serializable] internal sealed class BotReport {
            public int index,snapshots,matchedSamples,hardCorrections,accepted,rejected,snapshotFailures,visibleEntities,waterBefore,rationBefore,connectionAttempts=1;
            public long actor,firstTick,lastTick,windowFirstTick;public double maximumPredictionError;public string status,error,lastConnectionError;
        }
        [Serializable] sealed class Report {
            public string status="STARTING",error="",layout,scope="Multiple independently authenticated production Relay/NGO clients in one headless process, using walking prediction and two authoritative consumable transactions each. No render/UI/voice, ship/machinery activity, army/fog or full gameplay capacity acceptance.";
            public int requested,ready,rallied,holdSeconds,combinedClientFrameSamples,totalRequested,workerIndex,workerCount=1;public double measuredSeconds,combinedClientFrameMedianMs,combinedClientFrameP95Ms,measurementStartRadiusMetres,measurementEndRadiusMetres,rallySeconds;public string clientFrameScope="Headless combined-client engine loop including every scripted client; not an individual rendered gameplay frame time";public long peakWorkingSetBytes,peakPrivateBytes,measurementFirstServerTick,measurementLastServerTick;public List<BotReport> bots=new List<BotReport>();
        }
        internal sealed class Bot {
            internal IUnityServices registry;internal SurvivalNetworkSession session;internal BotReport report;
            internal bool dispersed,headingSent,waterSent,rationSent,retrying;internal int count;internal long waterSequence,rationSequence;
            internal double windowStart;internal FramePose? rallyGoal;internal string profile;
            internal SurvivalInput Controls(SurvivalInput input){input.brake=windowStart==0;input.y=windowStart==0?0:1;input.x=input.lookX=0;
                if(windowStart==0){
                    if(rallyGoal.HasValue&&session?.Predicted!=null){var pose=session.Predicted;var position=new Double3(pose.pose.x,pose.pose.y,pose.pose.z);if(pose.pose.Distance(rallyGoal.Value)>1){var up=PlanetPrecisionPolicy.Direction(position);var desired=Vector3.ProjectOnPlane((SurvivalVehicleSimulation.Position(rallyGoal.Value)-position).Float,up).normalized;var forward=Vector3.ProjectOnPlane(SurvivalMovement.Forward(pose),up).normalized;var right=Vector3.Cross(up,forward).normalized;input.x=Mathf.Clamp(Vector3.Dot(desired,right),-1,1);input.y=Mathf.Clamp(Vector3.Dot(desired,forward),-1,1);input.brake=false;}}
                    return input;
                }
                if(!headingSent){input.lookX=Mathf.DeltaAngle(0,report.index*360f/count);headingSent=true;}else input.lookX=dispersed?0:1.2f;return input;}
            internal void Failure(string error){report.snapshotFailures++;report.error="Snapshot rejected: "+error;}
            internal void Snapshot(SurvivalSnapshot snapshot,double error,bool correction){report.snapshots++;report.actor=snapshot.self.id;if(report.firstTick==0)report.firstTick=snapshot.tick;report.lastTick=snapshot.tick;if(!double.IsNaN(error)&&!double.IsInfinity(error)){report.matchedSamples++;report.maximumPredictionError=Math.Max(report.maximumPredictionError,error);}if(correction)report.hardCorrections++;}
            internal void Reply(SurvivalActionReply reply){if(reply.sequence!=waterSequence&&reply.sequence!=rationSequence)return;if(reply.accepted)report.accepted++;else{report.rejected++;report.error="Consumable command rejected: "+reply.message;}}
            internal void Tick(double elapsed){
                report.status=session.Status;report.visibleEntities=session.Visible.Count;
                if(!waterSent&&elapsed>=5+report.index*.05){report.waterBefore=(int)session.Latest.self.inventory.Count("water");report.rationBefore=(int)session.Latest.self.inventory.Count("ration");var action=new SurvivalAction{kind="use",item="water"};session.Submit(action);waterSequence=action.sequence;waterSent=true;}
                if(!rationSent&&elapsed>=20+report.index*.05){var action=new SurvivalAction{kind="use",item="ration"};session.Submit(action);rationSequence=action.sequence;rationSent=true;}
            }
            internal bool Conserved=>waterSent&&rationSent&&report.accepted==2&&report.rejected==0&&session.Latest.self.inventory.Count("water")==report.waterBefore-1&&session.Latest.self.inventory.Count("ration")==report.rationBefore-1;
        }
        readonly Report report=new Report();readonly List<Bot> bots=new List<Bot>();readonly List<double> frames=new List<double>(4096);string path,code,run;double deadline,windowStart,occupiedAt,nextProgress,rallyStarted;bool launching,finishing,rallyAssigned;FramePose hub;
        internal static bool CanRetryInitial(double windowStart,int snapshots,int attempts,string status)=>windowStart==0&&snapshots==0&&attempts<3&&(status=="Connection timed out"||status.StartsWith("Disconnected:",StringComparison.Ordinal)||status.StartsWith("Connection failed",StringComparison.Ordinal));
        static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        internal static bool Install(){
            string destination=Argument("-survival-load-report");if(destination==null)return false;
            try{
                var resolved=Path.GetFullPath(destination);var allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;
                if(!resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Load probes must use a disposable Live report path");
                if(!int.TryParse(Argument("-survival-load-count"),out int count)||count<2||count>100)throw new ArgumentException("Load count must be 2 to 100");
                if(!int.TryParse(Argument("-survival-load-seconds"),out int seconds)||seconds<30||seconds>300)throw new ArgumentException("Load hold must be 30 to 300 seconds");
                string layout=Argument("-survival-load-layout")??"crowded",code=Argument("-survival-load-code");if(layout!="crowded"&&layout!="dispersed"||string.IsNullOrWhiteSpace(code)||code.Length>16)throw new ArgumentException("Invalid load layout or Relay code");
                SurvivalNetworkSession.DiagnosticMultipleSessions=true;var go=new GameObject("Authenticated survival load fixture");DontDestroyOnLoad(go);var probe=go.AddComponent<SurvivalLoadProbe>();probe.path=resolved;probe.code=code;probe.report.requested=count;probe.report.holdSeconds=seconds;probe.report.layout=layout;probe.run=Guid.NewGuid().ToString("N").Substring(0,8);probe.ConfigureWorkers(count);return true;
            }catch(Exception e){Debug.LogError("Load probe arguments: "+e.Message);Application.Quit(2);return true;}
        }
        async void Start(){
            Application.runInBackground=true;Application.targetFrameRate=60;deadline=Time.realtimeSinceStartupAsDouble+600+report.holdSeconds;launching=true;
            try{
                if(report.workerCount>1)await Task.Delay(report.workerIndex*300);
                for(int i=0;i<report.requested;i++){
                    if(finishing)return;var bot=new Bot{report=new BotReport{index=report.workerIndex*report.requested+i},profile="probe_load_"+run+"_"+i,count=report.totalRequested,dispersed=report.layout=="dispersed",registry=UnityServices.CreateServices("survival-load-"+run+"-"+i)};
                    var go=new GameObject("Independent survival client "+i);bot.session=go.AddComponent<SurvivalNetworkSession>();bot.session.HeadlessProbeClient=true;bot.session.LoadBot=bot;bot.session.ActionReplied+=bot.Reply;bots.Add(bot);report.bots.Add(bot.report);
                    await bot.session.Connect(null,code,null,bot.profile,"Load "+i);
                    // Bound admission/API bursts; no synchronized 100-client sign-in flood.
                    await Task.Delay(300*report.workerCount);
                }
            }catch(Exception e){await Finish(false,e.Message);}finally{launching=false;}
        }
        void Update(){
            if(finishing)return;double now=Time.realtimeSinceStartupAsDouble;if(now>deadline){_ = Finish(false,"Load clients did not reach/complete the occupied measurement window");return;}
            report.ready=0;var identities=new HashSet<long>();foreach(var bot in bots){
                if(bot.report.error!=null){_ = Finish(false,bot.report.error);return;}
                bot.report.status=bot.retrying?"Retrying initial connection":bot.session.Status;
                if(bot.retrying)continue;
                if(CanRetryInitial(windowStart,bot.report.snapshots,bot.report.connectionAttempts,bot.session.Status)){bot.retrying=true;_ = RetryInitial(bot);continue;}
                if(bot.session.Status.StartsWith("Disconnected:",StringComparison.Ordinal)||bot.session.Status.StartsWith("Connection failed",StringComparison.Ordinal)||bot.session.Status=="Connection timed out"){_ = Finish(false,bot.session.Status);return;}
                if(bot.session.PrivateStateReady&&bot.session.Latest!=null&&bot.report.snapshots>10){report.ready++;if(!identities.Add(bot.session.ActorId)){_ = Finish(false,"Independent clients resolved to the same authenticated character");return;}}
            }
            if(!launching&&report.ready==report.requested){
                bool settled;
                if(report.workerCount>1){try{settled=CoordinateWorkers(now);}catch(Exception e){_ = Finish(false,e.Message);return;}}
                else{
                    if(!rallyAssigned){try{AssignRally();}catch(Exception e){_ = Finish(false,e.Message);return;}rallyStarted=now;rallyAssigned=true;report.status="RALLYING";}
                    report.rallied=0;foreach(var bot in bots)if(bot.session.Latest.self.pose.Distance(bot.rallyGoal.Value)<=2)report.rallied++;
                    if(windowStart==0&&report.rallied!=report.requested){occupiedAt=0;report.status="RALLYING";Progress(now);return;}
                    if(occupiedAt==0)occupiedAt=now;settled=now-occupiedAt>=10;if(!settled)report.status="SETTLING";
                }
                if(settled){
                    if(windowStart==0){windowStart=now;report.rallySeconds=now-rallyStarted;report.status="MEASURING";foreach(var bot in bots){bot.windowStart=now;bot.report.windowFirstTick=bot.report.lastTick;report.measurementFirstServerTick=Math.Max(report.measurementFirstServerTick,bot.report.lastTick);report.measurementStartRadiusMetres=Math.Max(report.measurementStartRadiusMetres,bot.session.Latest.self.pose.Distance(hub));}}
                    report.measuredSeconds=now-windowStart;if(frames.Count<4096)frames.Add(Time.unscaledDeltaTime*1000.0);foreach(var bot in bots)bot.Tick(report.measuredSeconds);
                    if(report.measuredSeconds>=report.holdSeconds){foreach(var bot in bots)if(!bot.Conserved||bot.report.matchedSamples<30||bot.report.snapshotFailures!=0){_ = Finish(false,"A load client failed command conservation or prediction/delivery checks");return;}_ = Finish(true,null);return;}
                }
            }
            Progress(now);
        }
        void Progress(double now){if(now>=nextProgress){nextProgress=now+5;Write(path+".progress");}}
        void AssignRally(FramePose? common=null){
            hub=common??bots[0].session.Latest.self.pose;var origin=SurvivalVehicleSimulation.Position(hub);var up=PlanetPrecisionPolicy.Direction(origin);var east=Vector3.Cross(Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);var surface=new PlanetSurface(bots[0].session.ClientSimulationDefinition);
            int columns=Mathf.CeilToInt(Mathf.Sqrt(report.totalRequested)),rows=Mathf.CeilToInt(report.totalRequested/(float)columns);
            foreach(var bot in bots){if(bot.session.Latest.self.pose.body!=hub.body)throw new InvalidOperationException("The scripted load rally requires one common starting body");double x=(bot.report.index%columns-(columns-1)*.5)*2,z=(bot.report.index/columns-(rows-1)*.5)*2;var point=surface.ProjectToSurface(origin+new Double3(east)*x+new Double3(north)*z,.05);bot.rallyGoal=new FramePose{body=hub.body,x=point.x,y=point.y,z=point.z};}
        }
        async Task RetryInitial(Bot bot){
            bot.report.lastConnectionError=bot.session.Status;bot.report.connectionAttempts++;
            try{await bot.session.Disconnect(false);await Task.Delay((bot.report.connectionAttempts-1)*2000);if(!finishing)await bot.session.Connect(null,code,null,bot.profile,"Load "+bot.report.index);}
            catch(Exception e){bot.report.error="Initial connection retry failed: "+e.Message;}finally{bot.retrying=false;}
        }
        void Write(string destination){var sorted=frames.ToArray();Array.Sort(sorted);report.combinedClientFrameSamples=sorted.Length;if(sorted.Length>0){report.combinedClientFrameMedianMs=sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*.5)-1)];report.combinedClientFrameP95Ms=sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*.95)-1)];}try{using(var process=System.Diagnostics.Process.GetCurrentProcess()){report.peakWorkingSetBytes=Math.Max(report.peakWorkingSetBytes,process.PeakWorkingSet64);report.peakPrivateBytes=Math.Max(report.peakPrivateBytes,process.PrivateMemorySize64);}}catch(Exception){/* Unsupported process counters remain zero. */}Directory.CreateDirectory(Path.GetDirectoryName(destination));File.WriteAllText(destination,JsonUtility.ToJson(report,true));}
        async Task Finish(bool success,string error){
            if(finishing)return;finishing=true;report.status=success?"PASS":"FAIL";report.error=error??"";
            report.measurementLastServerTick=long.MaxValue;foreach(var bot in bots){report.measurementLastServerTick=Math.Min(report.measurementLastServerTick,bot.report.lastTick);if(bot.session.Latest?.self!=null&&rallyAssigned)report.measurementEndRadiusMetres=Math.Max(report.measurementEndRadiusMetres,bot.session.Latest.self.pose.Distance(hub));}if(bots.Count==0)report.measurementLastServerTick=0;
            var cleanup=new List<Task>();foreach(var bot in bots){bot.session.ActionReplied-=bot.Reply;cleanup.Add(CloseBot(bot));}await Task.WhenAll(cleanup);
            Write(path);Debug.Log("Authenticated scripted survival load: "+report.status+"; clients="+report.requested);Application.Quit(report.status=="PASS"?0:1);
        }
        async Task CloseBot(Bot bot){try{await bot.session.Disconnect(false);Destroy(bot.session.gameObject);}catch(Exception e){report.status="FAIL";report.error="Load client cleanup: "+e.Message;}}
    }
}
#endif
