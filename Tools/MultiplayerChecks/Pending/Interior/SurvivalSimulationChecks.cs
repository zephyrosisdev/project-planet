#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public static class SurvivalSimulationChecks {
#if UNITY_EDITOR
        const string Request="TestResults/Multiplayer/run-simulation-checks.request";
        static SurvivalSimulationChecks(){if(File.Exists(Request))UnityEditor.EditorApplication.delayCall+=RunRequested;}
        static void RunRequested(){if(!File.Exists(Request))return;File.Delete(Request);Run();}
#endif
        public static bool LastRunPassed {get;private set;}
        [Serializable] sealed class Report {public string status,scope=Application.isEditor?"Unity Editor simulation checks; not standalone, transport, capacity or performance acceptance":"Standalone headless simulation checks; not transport, capacity, frame-time or army acceptance",error;public int checks;}
        [Serializable] sealed class MetricsTimingWire {public string name;public int samples;public double medianMs,p95Ms,maximumMs,totalMs;}
        [Serializable] sealed class MetricsWire {public long window,firstServerTick,lastServerTick;public int samples,minimumPlayers,maximumPlayers;public double meanPlayers;public MetricsTimingWire[] phases;}
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Massive Army/Validation/Survival Network Simulation")]
#endif
        public static void Run(){
            var report=new Report();PlanetDefinition definition=null;PlanetSpacecraft first=null,second=null;SurvivalVehicleSimulation vehicle=null,replayVehicle=null;
            void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks++;}
            try{
#if UNITY_EDITOR
                if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run these isolated checks outside Play Mode");
#endif
                int cameras=Resources.FindObjectsOfTypeAll<Camera>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,objects=Resources.FindObjectsOfTypeAll<GameObject>().Length;
                definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=10000;definition.terrainHeight=0;definition.oceanHeight=-35;definition.stampTerrain=false;definition.gravitationalParameter=9.81*10000*10000;
                var assembly=ShipModules.Starter(1);first=new PlanetSpacecraft(definition,assembly,(a,b,q)=>true);
                var initial=new PlanetShipSimulationSnapshot{pose=new SpacePose{position=new Double3(0,10008,0),rotation=Quaternion.identity},gear=true,landed=true,assist=true,heldClearance=88};
                first.RestoreSimulation(initial);const float dt=1f/30;
                first.StepSimulation(dt,Vector3.zero,Vector3.zero,false,false,false,false,true);
                for(int i=0;i<10;i++)first.StepSimulation(dt,Vector3.zero,Vector3.zero,false,false,false,false,false);
                var checkpoint=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(JsonUtility.ToJson(first.CaptureSimulation()));
                Check(checkpoint.autoFlight&&!checkpoint.landed,"Simulation-only takeoff starts the existing landing-assist path");
                for(int i=0;i<120;i++)first.StepSimulation(dt,Vector3.zero,Vector3.zero,false,false,false,false,false);
                var expected=first.CaptureSimulation();Check(!expected.landed&&!expected.autoFlight&&expected.pose.position.Magnitude>10060,"Takeoff completes above terrain");
                second=new PlanetSpacecraft(definition,ShipModules.Starter(1),(a,b,q)=>true);second.RestoreSimulation(checkpoint);
                for(int i=0;i<120;i++)second.StepSimulation(dt,Vector3.zero,Vector3.zero,false,false,false,false,false);
                var replay=second.CaptureSimulation();Check((expected.pose.position-replay.pose.position).Magnitude<1e-7&&(expected.pose.linearVelocity-replay.pose.linearVelocity).Magnitude<1e-7&&Quaternion.Angle(expected.pose.rotation,replay.pose.rotation)<.001f,"Mid-takeoff snapshot replay reproduces pose and velocity");
                var freefall=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(JsonUtility.ToJson(expected));freefall.assist=false;freefall.pose.linearVelocity=default;second.RestoreSimulation(freefall);
                for(int i=0;i<60;i++)second.StepSimulation(dt,Vector3.zero,Vector3.zero,false,false,false,false,false);
                Check(second.CaptureSimulation().pose.linearVelocity.y<-18,"Unassisted network flight uses planetary gravity");
                var surface=new PlanetSurface(definition);vehicle=new SurvivalVehicleSimulation("truck",surface);replayVehicle=new SurvivalVehicleSimulation("truck",surface);
                var state=new SurvivalVehicleState{entity=100,pose=new FramePose{body=1,y=10000.025},maximumSpeed=10,fueled=true};
                for(int i=0;i<30;i++)vehicle.Step(state,new SurvivalInput{y=1,x=.3f},dt,Array.Empty<IndustrialMachine>());
                var saved=state.Copy();for(int i=0;i<60;i++)vehicle.Step(state,new SurvivalInput{y=1,x=.3f},dt,Array.Empty<IndustrialMachine>());
                for(int i=0;i<60;i++)replayVehicle.Step(saved,new SurvivalInput{y=1,x=.3f},dt,Array.Empty<IndustrialMachine>());
                Check(state.pose.Distance(saved.pose)<1e-7&&Math.Abs(state.velocityX-saved.velocityX)<1e-7&&Math.Abs(state.yawVelocity-saved.yawVelocity)<1e-7,"Vehicle replay restores motor internals as well as the transform");
                Check(new Double3(state.velocityX,state.velocityY,state.velocityZ).Magnitude<=10.1,"Truck movement remains inside its configured speed limit");
                for(int i=0;i<90;i++)vehicle.Step(state,new SurvivalInput{brake=true},dt,Array.Empty<IndustrialMachine>());
                Check(new Double3(state.velocityX,state.velocityY,state.velocityZ).Magnitude<.1,"Neutral/brake input stops an unoccupied ground vehicle");
                bool poleWalking=true;foreach(var axis in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back}){var p=new Double3(axis)*10000.05;var motion=new SurvivalMotion{pose=new FramePose{body=1,x=p.x,y=p.y,z=p.z},grounded=true,forwardZ=1};var before=motion.pose;SurvivalMovement.Step(motion,new SurvivalInput{sequence=1,y=1},surface,1f/30,7);poleWalking&=motion.pose.Distance(before)>.2&&Math.Abs(Vector3.Dot(SurvivalMovement.Forward(motion),PlanetPrecisionPolicy.Direction(new Double3(motion.pose.x,motion.pose.y,motion.pose.z))))<.001;}
                Check(poleWalking,"Canonical walking has a valid tangent and moves at every spherical axis, including the forward/back poles");
                var lowRate=new SurvivalMotion{pose=new FramePose{body=1,y=10000},grounded=true,forwardZ=1};SurvivalMovement.Step(lowRate,new SurvivalInput{sequence=1,y=1},surface,.1f,7);Check(lowRate.pose.z>.69&&lowRate.pose.z<.71,"The supported 10 Hz configuration advances walking with a server-owned fixed duration");
                var corrected=new SurvivalMotion{epoch=7,pose=new FramePose{body=1,x=100,y=10000},grounded=true};var pending=new[]{new SurvivalInput{sequence=40,epoch=7,y=1},new SurvivalInput{sequence=41,epoch=7,y=1}};
                var expectedWalking=corrected.Copy();SurvivalMovement.Step(expectedWalking,pending[0],surface,dt,7);var firstWalking=expectedWalking.Copy();SurvivalMovement.Step(expectedWalking,pending[1],surface,dt,7);
                var history=new Dictionary<uint,SurvivalMotion>{{40,new SurvivalMotion{epoch=7,pose=new FramePose{body=1,y=10000}}}};
                SurvivalPrediction.Replay(corrected,pending,history,dt,(motion,input,duration)=>SurvivalMovement.Step(motion,input,surface,duration,7));
                Check(SurvivalPrediction.MatchingError(history,firstWalking)<1e-7&&corrected.pose.Distance(expectedWalking.pose)<1e-7,"A later acknowledgement compares against replayed history after an earlier correction, without another false hard snap");
                corrected.pose.x+=20;Check(history[41].pose.Distance(expectedWalking.pose)<1e-7&&double.IsNaN(SurvivalPrediction.MatchingError(history,new SurvivalMotion{sequence=42,epoch=7})),"Prediction samples remain independent and a missing acknowledged sample cannot trigger a distance snap");
                Check(double.IsNaN(SurvivalPrediction.MatchingError(history,new SurvivalMotion{sequence=41,epoch=7,tick=history[41].tick+1,pose=history[41].pose})),"A repeated held-input acknowledgement at a different server tick cannot produce a false distance snap");
                var privateMotion=new SurvivalMotion{actor=12,name="Crew",epoch=9,sequence=42,ship=new SurvivalShipState{entity=31,physics="private physics",invitedActors=new List<long>{55}}};var feet=new FramePose{body=1,x=25,y=10001,z=40};var proxy=SurvivalPlayerProxy.From(privateMotion,feet);string publicJson=JsonUtility.ToJson(proxy);
                Check(proxy.pose.Distance(feet)==0&&proxy.parent==31&&proxy.parentKind=="ship"&&!publicJson.Contains("physics")&&!publicJson.Contains("invitedActors")&&!publicJson.Contains("sequence")&&!publicJson.Contains("epoch"),"Remote player proxies carry canonical presentation and parent state without pilot physics, invitations or private command state");
                proxy=SurvivalPlayerProxy.From(new SurvivalMotion{actor=12,name="Ground",forwardZ=1},feet);
                var stablePose=SurvivalPlayerProxy.FingerprintPose(proxy.pose);string stableFingerprint=proxy.Fingerprint();var exactPose=proxy.pose;proxy.pose.x+=1e-11;proxy.serverTick++;
                Check(proxy.Fingerprint()==stableFingerprint&&proxy.pose.x!=exactPose.x,"Submillimetre public dirty checks ignore radial floating-point noise without changing the authoritative pose");
                proxy.pose.x+=.01;Check(proxy.Fingerprint()!=stableFingerprint||SurvivalPlayerProxy.FingerprintPose(proxy.pose).Distance(stablePose)>.009,"Centimetre player movement still produces a public delta");proxy.pose=exactPose;proxy.parent++;
                Check(proxy.Fingerprint()!=stableFingerprint,"Player frame-parent changes always produce a public delta");
                var walkingPacket=JsonUtility.FromJson<SurvivalSnapshot>(JsonUtility.ToJson(new SurvivalSnapshot{motion=new SurvivalMotion{actor=12,sequence=42,pose=feet},self=new IndustrialActor{id=12}}));walkingPacket.NormalizeReferences();
                Check(walkingPacket.motion.sequence==42&&walkingPacket.motion.pose.body==1&&walkingPacket.motion.ship==null&&walkingPacket.motion.vehicle==null&&walkingPacket.pilotedShip==null,"A walking snapshot survives Unity JSON without phantom craft states that change the prediction frame");
                var interestDelivery=new SurvivalInterestDelivery();var pendingEntities=new List<SurvivalInterestDelivery.Candidate>();var wireEntities=new Dictionary<long,SurvivalEntity>();
                var boundedPacket=new SurvivalSnapshot{tick=987};int envelope=System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(boundedPacket))+4;
                for(int i=1;i<=8;i++){var entity=new SurvivalEntity{id=i,revision=987,kind="fixture",pose=new FramePose{body=3,frame=900,x=1e12+i},data="quoted \"text\" "+new string('\u754c',100)};wireEntities.Add(i,entity);string json=JsonUtility.ToJson(entity);pendingEntities.Add(interestDelivery.Stage(i,json,987,2,i,System.Text.Encoding.UTF8.GetByteCount(json)+1));}
                int wireBudget=envelope+1500;var selected=SurvivalInterestDelivery.Select(pendingEntities,987,30,wireBudget-envelope);foreach(var entity in selected)boundedPacket.changed.Add(wireEntities[entity.id]);string boundedJson=JsonUtility.ToJson(boundedPacket);
                Check(selected.Count>0&&selected.Count<8&&System.Text.Encoding.UTF8.GetByteCount(boundedJson)+4<=wireBudget,"AOI selection respects the actual Unity JSON UTF-8 envelope with escaped and multibyte data");
                Check(System.Text.Encoding.UTF8.GetString(SurvivalSnapshotEncoding.Encode(boundedPacket))==boundedJson,"Cached snapshot encoding preserves exact Unity JSON bytes including escaped Unicode entities");
                boundedPacket.removed.Add(123);boundedPacket.cancelTransfer=45;boundedPacket.changed.Add(new SurvivalEntity{id=90,kind="quoted",data="\"changed\":[] 🛰️ 中文 \\\"nested\\\""});
                string encodedFirst=System.Text.Encoding.UTF8.GetString(SurvivalSnapshotEncoding.Encode(boundedPacket));
                Check(encodedFirst==JsonUtility.ToJson(boundedPacket)&&boundedPacket.changed.Count==selected.Count+1,"Cached snapshot encoding preserves removals, metadata, nested marker strings and the caller's entity list");
                boundedPacket.changed.RemoveAt(boundedPacket.changed.Count-1);boundedPacket.removed.Clear();boundedPacket.cancelTransfer=0;
                Check(System.Text.Encoding.UTF8.GetString(SurvivalSnapshotEncoding.Encode(new SurvivalSnapshot()))==JsonUtility.ToJson(new SurvivalSnapshot()),"Empty snapshots retain the canonical Unity wire format");
                var wirePacket=JsonUtility.FromJson<SurvivalSnapshot>(boundedJson);var firstEntity=wirePacket.changed[0];
                Check(firstEntity.version==1&&firstEntity.revision==987&&firstEntity.pose.body==3&&firstEntity.pose.frame==900&&firstEntity.pose.x==1e12+firstEntity.id,"Entity version, server revision and canonical parent-frame coordinates survive Unity serialization");
                interestDelivery.Commit(selected);var exit=interestDelivery.Removals(new HashSet<long>());var deferredExit=JsonUtility.FromJson<SurvivalSnapshot>(JsonUtility.ToJson(new SurvivalSnapshot{tick=988,removed=exit}));
                Check(deferredExit.removed.Count==selected.Count&&interestDelivery.Removals(new HashSet<long>()).Count==selected.Count,"Serializing a deferred removal does not clear delivered subscriptions before send commit");
                Check(cameras==Resources.FindObjectsOfTypeAll<Camera>().Length&&meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&materials==Resources.FindObjectsOfTypeAll<Material>().Length&&objects==Resources.FindObjectsOfTypeAll<GameObject>().Length,"Headless ship and vehicle simulation allocate no camera, mesh, material or GameObject");
                var diagnostic=new SurvivalNetworkMetrics();diagnostic.Record(SurvivalNetworkMetrics.Phase.JournalSerialize,1);diagnostic.Record(SurvivalNetworkMetrics.Phase.JournalSerialize,2);diagnostic.Record(SurvivalNetworkMetrics.Phase.JournalSerialize,3);diagnostic.Record(SurvivalNetworkMetrics.Phase.Snapshot,4);diagnostic.End(diagnostic.Begin(),8);diagnostic.End(diagnostic.Begin(),8);
                Directory.CreateDirectory("TestResults/Multiplayer");string metricsPath="TestResults/Multiplayer/metrics-fixture.json";diagnostic.Write(metricsPath,8);var metricWire=JsonUtility.FromJson<MetricsWire>(File.ReadAllText(metricsPath));
                var journalWire=Array.Find(metricWire.phases,p=>p.name=="JournalSerialize");
                Check(metricWire.samples==2&&metricWire.minimumPlayers==8&&metricWire.maximumPlayers==8&&metricWire.meanPlayers==8,"Diagnostic windows preserve sampled occupied-player bounds");
                Check(journalWire.samples==3&&journalWire.medianMs==2&&journalWire.p95Ms==3&&journalWire.maximumMs==3&&journalWire.totalMs==6,"Stage timing reports preserve counts, percentiles and totals");
                diagnostic.End(diagnostic.Begin(),4);diagnostic.Write(metricsPath,4);var priorWire=JsonUtility.FromJson<MetricsWire>(File.ReadAllText("TestResults/Multiplayer/metrics-fixture.window-1.json"));metricWire=JsonUtility.FromJson<MetricsWire>(File.ReadAllText(metricsPath));
                Check(priorWire.window==1&&priorWire.minimumPlayers==8&&metricWire.window==2&&metricWire.samples==1&&metricWire.minimumPlayers==4,"Bounded historical metric windows survive reset and later player departures");
                var numbered=new SurvivalNetworkMetrics();numbered.End(numbered.Begin(),8,401);numbered.End(numbered.Begin(),8,402);numbered.Write("TestResults/Multiplayer/numbered-metrics.json",8);var numberedWire=JsonUtility.FromJson<MetricsWire>(File.ReadAllText("TestResults/Multiplayer/numbered-metrics.json"));Check(numberedWire.firstServerTick==401&&numberedWire.lastServerTick==402,"Metric windows preserve exact server tick bounds to exclude rally and admission from measured load");
                SurvivalNetworkSession.RunInterestChecks(Check);
                SurvivalNetworkSession.RunFactoryChecks(Check);
                SurvivalServiceChecks.Run(Check);
                SurvivalBindingChecks.Run(Check);
                SurvivalNetworkSession.RunLargeSnapshotChecks(Check);
                SurvivalNetworkSession.RunShipyardChecks(Check);
                SurvivalNetworkSession.RunBuildingChecks(Check);
                SurvivalNetworkSession.RunLeaveChecks(Check);
                SurvivalUniverseChecks.Run(Check);
                SurvivalNetworkSession.RunCombatChecks(Check);
                SurvivalNetworkSession.RunShipWalkingChecks(Check);
                var loadBot=new SurvivalLoadProbe.Bot{report=new SurvivalLoadProbe.BotReport{index=2},count=8};var neutral=loadBot.Controls(new SurvivalInput{sequence=1,epoch=1});
                Check(neutral.Valid&&neutral.y==0&&neutral.brake&&!loadBot.headingSent,"Scripted load clients remain stationary until the common occupied measurement window");loadBot.windowStart=1;var moving=loadBot.Controls(new SurvivalInput{sequence=2,epoch=1});
                Check(moving.Valid&&moving.y==1&&!moving.brake&&moving.lookX==90&&loadBot.headingSent,"Scripted load begins a reproducible bounded heading/input timeline after admission settles");
                Check(SurvivalLoadProbe.CanRetryInitial(0,0,1,"Connection timed out")&&!SurvivalLoadProbe.CanRetryInitial(1,0,1,"Connection timed out")&&!SurvivalLoadProbe.CanRetryInitial(0,1,1,"Disconnected: lost")&&!SurvivalLoadProbe.CanRetryInitial(0,0,3,"Connection timed out"),"Initial connection retry is bounded and cannot hide a disconnect after gameplay or during measurement");
                report.status="PASS";
            }catch(Exception error){report.status="FAIL";report.error=error.ToString();Debug.LogError("Survival simulation validation: "+error.Message);}
            finally{first?.Dispose();second?.Dispose();vehicle?.Dispose();replayVehicle?.Dispose();if(definition!=null)UnityEngine.Object.DestroyImmediate(definition);Directory.CreateDirectory("TestResults/Multiplayer");File.WriteAllText("TestResults/Multiplayer/unity-simulation-checks.json",JsonUtility.ToJson(report,true));}
            LastRunPassed=report.status=="PASS";
            Debug.Log("Survival network simulation checks: "+report.status+" ("+report.checks+")");
        }
    }
}


#endif
