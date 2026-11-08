#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    // Explicit CLI diagnostics use the production authentication/transport/session path.
    // This does not supply a server-side admission bypass or privileged gameplay commands.
    internal sealed partial class SurvivalSessionProbe:MonoBehaviour {
        [Serializable] sealed class Report {
            public string status="STARTING",scope="Standalone authenticated session probe; not full survival, Vivox, capacity, frame-time or army acceptance",error="",profile,relayCode;
            public bool dedicated,headlessClient,canonicalDefinitionMatches,remotePlayerSeen,reconnected,cleanupRequested;public long actor,firstTick,lastTick;public int snapshots,matchingPredictionSamples,hardCorrections;
            public double movementMetres,movementMaximumMetres,maxMatchingPredictionError;public bool separatedRegions;public int body;
            public uint lastAcknowledgedInput,lastSentInput;public int historyEntries,samplesWithHistory,epochMismatches,tickMismatches,frameMismatches,nonFiniteHistorySamples,emptyReferencesNormalized;
            public bool chatRequired,textBefore,voiceBefore,mutedBefore,peerTextBefore,ownTextAcknowledgedBefore,textAfter,voiceAfter,mutedAfter,peerTextAfter,ownTextAcknowledgedAfter,gameplayConnectedOnChatFailure;
            public string chatStatus;public int holdSeconds;
            public bool factoryRequired,factoryReplication,factoryPrivacy,factoryReplaySafe,factoryReconnect,factoryRestartRestored,factoryRemoved,factoryConserved;
            public int factoryAccepted,factoryOwnershipRejections;public string factoryPhase;
            public bool factoryRendered,factoryRenderCleared;
            public bool listingRecoveryRequired,listingRestored,listingConnectionsPreserved,listingMetadataMatches;public string originalLobby,replacementLobby;public double listingRecoverySeconds;
            public bool joinCodeDelayChecked;public double joinCodeDelaySeconds;public long joinCodeDelayTicks;
            public int snapshotFragments,largeSnapshots,partialSnapshots,maximumSnapshotBytes,largestPrivateRespawnCount;
            public bool buildingCreated,buildingRestored,buildingRecovered,buildingConserved,buildingPrivacy,buildingReconnect,buildingRendered,buildingRenderCleared;public int buildingAccepted,buildingRejections,buildingStepNow;
            public bool shipyardCreated,shipyardRestored,shipyardRecovered,shipyardConserved,shipyardPrivacy;public int shipyardAccepted,shipyardRejections;
            public bool transportSimulationActive;public int simulatedDelayMs,simulatedJitterMs;public float simulatedLossPercent;
            public bool interiorRequired,interiorBoarded,interiorWalked,interiorHatchBlocked,interiorPilot,interiorPilotDenied,interiorParentMoved,interiorReconnect,interiorCanonical;public int interiorStepNow;
            public bool combatRequired,combatKilled,combatRevived,combatRecovered,combatReplay,combatReconnect;public int combatShots,combatStepNow;
            public int snapshotFailures,visibleMachines,factoryStepNow,shipStepNow;public string lastSnapshotError,lastSessionStatus;
        }
        Report report=new Report();string path,profile,code;SurvivalNetworkSession session;BattleApp app;
        [Serializable] sealed class CanonicalDefinition {public int bodyId,terrainSeed;public float radius,terrainHeight,oceanHeight;public double gravitationalParameter;public bool stampTerrain;}
        double deadline,phaseStarted;bool connecting,finishing,readyWritten;int phase;FramePose startPose,lastPose;
        SurvivalChat observedChat;double nextChatHello,chatWaitStarted,chatReadySince;bool peerHello,ownHelloAcknowledged;
        double nextProgress;
        static string Argument(string name){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        internal static bool HeadlessRequested=>!string.IsNullOrWhiteSpace(Argument("-survival-probe-report"))&&Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-headless")>=0;
        internal static void Install(){
            string path=Argument("-survival-probe-report");if(string.IsNullOrWhiteSpace(path))return;
            var go=new GameObject("Survival session probe");DontDestroyOnLoad(go);var probe=go.AddComponent<SurvivalSessionProbe>();probe.path=Path.GetFullPath(path);
            probe.session=SurvivalNetworkSession.Active;
            probe.report.headlessClient=HeadlessRequested;
            if(HeadlessRequested)probe.report.scope="Headless authenticated production transport/prediction checks; no rendering, UI, Vivox, full survival or capacity acceptance";
#if UNITY_SERVER
            probe.report.dedicated=!HeadlessRequested;
            if(probe.report.dedicated&&probe.session!=null)probe.session.Probe=probe;
#endif
        }
        void Start(){
            Application.runInBackground=true;deadline=Time.realtimeSinceStartupAsDouble+180;
            string extraWait=Argument("-survival-probe-extra-wait");if(extraWait!=null){if(!int.TryParse(extraWait,out int extra)||extra<0||extra>600||!Path.GetFullPath(path).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){_ = Finish(false,"Invalid isolated diagnostic wait extension");return;}deadline+=extra;}
            string hold=Argument("-survival-probe-hold-seconds");if(hold!=null){if(!int.TryParse(hold,out report.holdSeconds)||report.holdSeconds<0||report.holdSeconds>90){_ = Finish(false,"Probe hold must be 0 to 90 seconds");return;}deadline+=report.holdSeconds;}
#if SURVIVAL_GRAPHICS_PROBE
            report.scope="Graphical survival diagnostic fixture; offline army kernels omitted. Not release, UI/Vivox, full survival, capacity, rendered frame-time or army acceptance.";
#endif
            if(report.dedicated){StartListingRecovery();return;}
            report.separatedRegions=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-separated")>=0;
            report.chatRequired=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-chat")>=0;
            if(report.chatRequired)report.scope+=" Includes Vivox text delivery, positional channel membership and muted capture before/after reconnect. Audible voice and physical keyboard input remain unverified.";
            if(report.chatRequired&&report.headlessClient){_ = Finish(false,"Chat probe requires graphics-enabled clients");return;}
            if(!StartFactoryProbe()||!StartInteriorProbe()||!StartCombatProbe())return;
            profile=Argument("-survival-probe-profile");code=Argument("-survival-probe-code");report.profile=profile;
            if(string.IsNullOrWhiteSpace(profile)||!profile.StartsWith("probe_",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(code))_ = Finish(false,"Probe needs a disposable probe_ profile and Relay code");
        }
        void Update(){
            if(finishing)return;
            if(Time.realtimeSinceStartupAsDouble>=nextProgress&&!report.dedicated){nextProgress=Time.realtimeSinceStartupAsDouble+5;report.visibleMachines=session==null?0:FactoryMachines().Count;report.factoryStepNow=factoryStep;report.shipStepNow=shipStep;report.buildingStepNow=buildingStep;report.lastSessionStatus=session?.Status;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path+".progress",JsonUtility.ToJson(report,true));}
            if(Time.realtimeSinceStartupAsDouble>deadline){_ = Finish(false,"Session probe timed out: "+(session?.Status??"no session"));return;}
            if(report.dedicated){
                session=SurvivalNetworkSession.Active;
                if(!readyWritten&&session!=null&&session.IsServer&&session.Status.StartsWith("Server ready",StringComparison.Ordinal)){readyWritten=true;report.status="READY";report.relayCode=session.ProbeJoinCode;Write();}
                UpdateListingRecovery();
                if(File.Exists(path+".stop")){bool success=readyWritten&&(!report.listingRecoveryRequired||report.listingRestored);_ = Finish(success,success?null:"Stopped before readiness/listing recovery");}
                return;
            }
            if(!connecting){if(!report.headlessClient)app=FindAnyObjectByType<BattleApp>();if(report.headlessClient||app?.View!=null){connecting=true;_ = Connect();}return;}
            if(phase!=3&&session!=null&&(session.Status.StartsWith("Disconnected:",StringComparison.Ordinal)||session.Status=="Connection timed out")){_ = Finish(false,session.Status);return;}
            if(session?.Latest?.self==null||!session.ClientSimulationReady)return;
            if(!report.canonicalDefinitionMatches){
                var supplied=JsonUtility.FromJson<CanonicalDefinition>(session.ProbeWorldDefinition);var actual=session.ClientSimulationDefinition;
                if(supplied==null||supplied.bodyId!=actual.bodyId||supplied.terrainSeed!=actual.terrainSeed||supplied.radius!=actual.radius||supplied.terrainHeight!=actual.terrainHeight||supplied.oceanHeight!=actual.oceanHeight||supplied.stampTerrain!=actual.stampTerrain||Math.Abs(supplied.gravitationalParameter-actual.gravitationalParameter)>Math.Max(1,Math.Abs(supplied.gravitationalParameter))*1e-12){_ = Finish(false,"Client initialization changed the server's canonical planet definition");return;}report.canonicalDefinitionMatches=true;
            }
            var snapshot=session.Latest;report.body=snapshot.self.pose.body;if(report.separatedRegions&&report.remotePlayerSeen){_ = Finish(false,"A player from a different planet leaked into local interest");return;}if(report.actor==0){report.actor=snapshot.self.id;report.firstTick=snapshot.tick;phaseStarted=Time.realtimeSinceStartupAsDouble;}
            if(report.chatRequired)ObserveChat();
            foreach(var entity in session.Visible.Values)if(entity.kind=="player")report.remotePlayerSeen=true;
            if(report.combatRequired){UpdateCombatProbe();return;}
            if(report.interiorRequired){UpdateInteriorProbe();return;}
            if(report.factoryRequired){UpdateFactoryProbe();return;}
            if(phase==0){if(!report.remotePlayerSeen&&!report.separatedRegions)return;phase=1;phaseStarted=Time.realtimeSinceStartupAsDouble;startPose=snapshot.self.pose;report.firstTick=snapshot.tick;}
            if(phase==1&&Time.realtimeSinceStartupAsDouble-phaseStarted>=4){phase=2;phaseStarted=Time.realtimeSinceStartupAsDouble;}
            if(phase==2&&Time.realtimeSinceStartupAsDouble-phaseStarted>=3+report.holdSeconds){
                report.movementMetres=startPose.Distance(snapshot.self.pose);report.lastTick=snapshot.tick;report.movementMaximumMetres=session.Config.walkSpeed*(report.lastTick-report.firstTick)/(double)session.Config.tickRate+session.Config.walkSnap;
                if(report.movementMetres<3||report.movementMetres>report.movementMaximumMetres){_ = Finish(false,"Authoritative walking distance outside the expected fixed-clock bound");return;}
                if(report.matchingPredictionSamples<10){_ = Finish(false,"Too few acknowledged prediction samples");return;}
                if(report.chatRequired&&!CheckChat(false))return;
                lastPose=snapshot.self.pose;phase=3;_ = Reconnect();
            }
            if(phase==4){
                if(snapshot.self.id!=report.actor||lastPose.Distance(snapshot.self.pose)>session.Config.walkSnap){_ = Finish(false,"Reconnect changed the character or its saved position");return;}
                report.reconnected=true;if(report.chatRequired&&!CheckChat(true))return;_ = Finish(true,null);
            }
        }
        async Task Connect(){
            try{session=SurvivalNetworkSession.Ensure();session.Probe=this;session.HeadlessProbeClient=report.headlessClient;await session.Connect(app,code,null,profile,"Probe "+profile);if(session.Status.StartsWith("Connection failed",StringComparison.Ordinal))_ = Finish(false,session.Status);}
            catch(Exception e){_ = Finish(false,e.Message);}
        }
        async Task Reconnect(){
            try{await session.Disconnect(true);peerHello=ownHelloAcknowledged=false;chatWaitStarted=chatReadySince=nextChatHello=0;await session.Connect(app,code,null,profile,"Probe "+profile);phase=4;if(session.Status.StartsWith("Connection failed",StringComparison.Ordinal))_ = Finish(false,session.Status);}
            catch(Exception e){_ = Finish(false,e.Message);}
        }
        internal SurvivalInput Controls(SurvivalInput input){
            if(report.combatRequired){input.x=input.y=0;return input;}
            input.x=0;input.y=!report.factoryRequired&&phase==1?1:0;input.z=input.roll=input.lookX=input.lookY=0;input.jump=input.boost=input.toggleAssist=input.toggleGear=false;input.brake=false;return report.interiorRequired?InteriorControls(input):report.factoryRequired?FactoryControls(input):input;
        }
        internal void EmptyReferencesNormalized(int count){report.emptyReferencesNormalized+=count;}
        string ChatStage=>phase>=3?"after":"before";
        string ChatPrefix=>"sf-probe/"+session.Config.worldId+"/";
        void ObserveChat(){
            var current=session.Chat;
            if(!ReferenceEquals(current,observedChat)){if(!ReferenceEquals(observedChat,null))observedChat.TextReceived-=ChatMessage;observedChat=current;if(current!=null)current.TextReceived+=ChatMessage;}
            if(current==null)return;report.chatStatus=current.Status;
            if(current.TextReady&&Time.realtimeSinceStartupAsDouble>=nextChatHello){nextChatHello=Time.realtimeSinceStartupAsDouble+1;_ = current.SendText(ChatPrefix+report.actor+"/"+ChatStage+"/hello");}
        }
        void ChatMessage(string sender,string message){
            if(finishing||observedChat==null||!message.StartsWith(ChatPrefix,StringComparison.Ordinal))return;
            string[] fields=message.Substring(ChatPrefix.Length).Split('/');if(fields.Length!=3||!long.TryParse(fields[0],out long actor)||actor==report.actor||fields[1]!=ChatStage)return;
            if(fields[2]=="hello"){peerHello=true;_ = observedChat.SendText(ChatPrefix+report.actor+"/"+ChatStage+"/ack-"+actor);}
            else if(fields[2]=="ack-"+report.actor)ownHelloAcknowledged=true;
        }
        bool CheckChat(bool after){
            var current=session.Chat;bool text=current?.TextReady==true,voice=current?.VoiceReady==true,muted=current?.InputMuted==true;
            if(after){report.textAfter=text;report.voiceAfter=voice;report.mutedAfter=muted;report.peerTextAfter=peerHello;report.ownTextAcknowledgedAfter=ownHelloAcknowledged;}
            else{report.textBefore=text;report.voiceBefore=voice;report.mutedBefore=muted;report.peerTextBefore=peerHello;report.ownTextAcknowledgedBefore=ownHelloAcknowledged;}
            if(text&&voice&&muted&&peerHello&&ownHelloAcknowledged){if(chatReadySince==0)chatReadySince=Time.realtimeSinceStartupAsDouble;return Time.realtimeSinceStartupAsDouble-chatReadySince>=2;}
            chatReadySince=0;
            if(chatWaitStarted==0)chatWaitStarted=Time.realtimeSinceStartupAsDouble;
            if(Time.realtimeSinceStartupAsDouble-chatWaitStarted>35){report.gameplayConnectedOnChatFailure=session.Connected&&session.Online;_ = Finish(false,"Vivox text/positional channel gate failed: "+(current?.Status??"no chat instance"));}
            return false;
        }
        internal void Snapshot(SurvivalSnapshot snapshot,double predictionError,bool hardCorrection,Dictionary<uint,SurvivalMotion> history,uint newestInput){
            if(snapshot.chunk!=null)report.snapshotFragments++;if(!snapshot.privateDetails)report.partialSnapshots++;
            report.transportSimulationActive=SurvivalNetworkConditions.Active;report.simulatedDelayMs=SurvivalNetworkConditions.DelayMs;report.simulatedJitterMs=SurvivalNetworkConditions.JitterMs;report.simulatedLossPercent=SurvivalNetworkConditions.LossPercent;
            report.snapshots++;report.lastTick=snapshot.tick;
            report.lastAcknowledgedInput=snapshot.motion.sequence;report.lastSentInput=newestInput;report.historyEntries=history.Count;
            if(history.TryGetValue(snapshot.motion.sequence,out var sample)){report.samplesWithHistory++;if(sample.epoch!=snapshot.motion.epoch)report.epochMismatches++;if(sample.tick!=snapshot.motion.tick)report.tickMismatches++;if(sample.pose.body!=snapshot.motion.pose.body||sample.pose.frame!=snapshot.motion.pose.frame)report.frameMismatches++;if(!SurvivalServerConfig.Finite(sample.pose.x)||!SurvivalServerConfig.Finite(sample.pose.y)||!SurvivalServerConfig.Finite(sample.pose.z))report.nonFiniteHistorySamples++;}
            if(!double.IsInfinity(predictionError)&&!double.IsNaN(predictionError)){report.matchingPredictionSamples++;report.maxMatchingPredictionError=Math.Max(report.maxMatchingPredictionError,predictionError);if(hardCorrection)report.hardCorrections++;}
        }
        internal void WireSnapshotBytes(int bytes){report.maximumSnapshotBytes=Math.Max(report.maximumSnapshotBytes,bytes);}
        internal void SnapshotFailure(string error){report.snapshotFailures++;report.lastSnapshotError=error;if(report.snapshotFailures==1)Debug.LogWarning("Diagnostic snapshot rejection: "+error);}
        internal void LargeSnapshotApplied(SurvivalSnapshot snapshot){report.largeSnapshots++;if(snapshot.privateDetails)report.largestPrivateRespawnCount=Math.Max(report.largestPrivateRespawnCount,snapshot.respawnPoints.Count);}
        async Task Finish(bool success,string error){
            if(finishing)return;finishing=true;if(report.factoryRequired&&factoryStartPose.HasValue&&session?.Latest?.self!=null)report.movementMetres=factoryStartPose.Value.Distance(session.Latest.self.pose);StopFactoryProbe();StopInteriorProbe();StopCombatProbe();report.error=error??"";report.status=success?"PASS":"FAIL";
            if(!ReferenceEquals(observedChat,null))observedChat.TextReceived-=ChatMessage;
            try{if(session!=null){report.cleanupRequested=true;await session.Disconnect(false);}}
            catch(Exception e){report.status="FAIL";report.error+=" Cleanup: "+e.Message;}
            Write();Debug.Log("Survival session probe: "+report.status+" "+report.error);Application.Quit(report.status=="PASS"?0:1);
        }
        void Write(){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(report,true));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        void OnApplicationQuit(){if(!finishing&&!string.IsNullOrEmpty(path)){report.status="FAIL";report.error="Process exited before probe completion: "+(session?.Status??"no session");Write();}}
    }
}

#endif
