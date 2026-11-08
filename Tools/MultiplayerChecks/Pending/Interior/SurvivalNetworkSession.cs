using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using UnityEngine;

namespace MassiveArmy.Networking {
    [DefaultExecutionOrder(-200)]
    public sealed partial class SurvivalNetworkSession:MonoBehaviour {
        const string InputMessage="SF.Input.1",ActionMessage="SF.Action.1",StateMessage="SF.State.1",WelcomeMessage="SF.Welcome.1",ReplyMessage="SF.Reply.1",LeaveMessage="SF.Leave.1",LeaveAckMessage="SF.LeaveAck.1";
        public static SurvivalNetworkSession Active {get;private set;}
        internal event Action<SurvivalActionReply> ActionReplied;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
        internal void RepeatProbeAction(SurvivalAction action){if(Probe!=null&&Connected)Send(ActionMessage,NetworkManager.ServerClientId,action);}
        internal SurvivalSessionProbe Probe;
#if UNITY_EDITOR || UNITY_SERVER
        internal SurvivalLoadProbe.Bot LoadBot;internal static bool DiagnosticMultipleSessions;
#endif
        internal string ProbeJoinCode=>services?.JoinCode;internal string ProbeWorldDefinition=>welcome?.definition;
        internal string ProbeLobbyId=>services?.LobbyId;
        internal HashSet<ulong> ProbeConnections=>new HashSet<ulong>(peers.Keys);
        internal long ProbeServerTick=>clock.Tick;
#endif
        internal SurvivalChat Chat=>chat;
        internal bool HeadlessProbeClient;PlanetDefinition headlessDefinition;
        internal bool ClientSimulationReady=>HeadlessProbeClient?headlessDefinition!=null:clientWorld!=null&&clientWorld.Ready;
        internal PlanetDefinition ClientSimulationDefinition=>HeadlessProbeClient?headlessDefinition:clientWorld?.definition;
        public bool IsServer=>manager!=null&&manager.IsServer;
        public bool Connected=>manager!=null&&manager.IsConnectedClient;
        public bool Online=>!IsServer&&welcome!=null;
        public string Status {get;private set;}="Disconnected";
        public SurvivalServerConfig Config {get;private set;}
        public long ActorId=>welcome?.actor??0;
        public static bool ChatInputCaptured=>Active!=null&&Active.chat!=null&&Active.chat.BlockGameplay;
        public static readonly KeyBindings Bindings=new KeyBindings();
        NetworkManager manager;UnityTransport transport;SurvivalServices services;SurvivalIdentityVerifier verifier;Task disconnectTask;
        readonly SurvivalNetworkMetrics metrics=new SurvivalNetworkMetrics();
        IndustrialAuthority authority;SurvivalWelcome welcome;SurvivalSnapshot latest;SurvivalMotion predicted;
        readonly SurvivalTickClock clock=new SurvivalTickClock();readonly Dictionary<ulong,Peer> peers=new Dictionary<ulong,Peer>();
        readonly Dictionary<ulong,string> admitted=new Dictionary<ulong,string>();readonly HashSet<string> admitting=new HashSet<string>();
        readonly Dictionary<ulong,double> retiringConnections=new Dictionary<ulong,double>();TaskCompletionSource<bool> leaveAcknowledged;
        readonly HashSet<ulong> pendingAdmissions=new HashSet<ulong>();
        readonly Dictionary<int,PlanetSurface> surfaces=new Dictionary<int,PlanetSurface>();
        readonly SurvivalInterestIndex interest=new SurvivalInterestIndex();readonly Dictionary<long,SurvivalEntity> entities=new Dictionary<long,SurvivalEntity>();
        readonly Dictionary<long,SurvivalEntity> visible=new Dictionary<long,SurvivalEntity>();
        readonly List<SurvivalInput> unacknowledged=new List<SurvivalInput>();
        readonly Dictionary<uint,SurvivalMotion> predictionHistory=new Dictionary<uint,SurvivalMotion>();
        SurvivalChat chat;PlanetDevelopment clientWorld;BattleApp app;double lastUpdate,snapshotElapsed,saveElapsed;float clientAccumulator;
        uint inputSequence;long actionSequence;bool closing,starting;int lifecycle;double connectedAt;
        internal SurvivalSnapshot Latest=>latest;internal IReadOnlyDictionary<long,SurvivalEntity> Visible=>visible;
        internal SurvivalMotion Predicted=>predicted;internal PlanetDevelopment ClientWorld=>clientWorld;
        internal sealed class Peer {
            public SurvivalPlayerRecord record;public SurvivalMotion motion;public SurvivalInputQueue input=new SurvivalInputQueue();
            public Queue<SurvivalAction> actions=new Queue<SurvivalAction>();public long newestAction;public int actionsThisWindow;public long actionWindow;
            public SurvivalInterestDelivery delivery=new SurvivalInterestDelivery();public double tokens=65536;public HashSet<long> visible=new HashSet<long>();
            public Dictionary<long,long> nextProxyTick=new Dictionary<long,long>();public long privateShip,privateShipRevision=-1;
            internal LargeSnapshot large;internal long nextLarge,cancelTransfer;internal string privateSignature;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot(){
#if !UNITY_SERVER
            Bindings.LoadFrom(PlayerPrefs.GetInt);
#endif
#if SURVIVAL_GRAPHICS_PROBE
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-report")<0){Debug.LogError("This survival graphics fixture requires the session probe; it is not a release or army benchmark build.");Application.Quit(2);return;}
#endif
#if UNITY_EDITOR || UNITY_SERVER
            var bootArgs=Environment.GetCommandLineArgs();int fixtureIndex=Array.IndexOf(bootArgs,"-survival-factory-seed");if(fixtureIndex>=0){bool ok=fixtureIndex+1<bootArgs.Length&&SeedFactoryFixture(bootArgs[fixtureIndex+1]);Application.Quit(ok?0:1);return;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-simulation-checks")>=0){SurvivalSimulationChecks.Run();Application.Quit(SurvivalSimulationChecks.LastRunPassed?0:1);return;}
#endif
#if UNITY_SERVER
            if(SurvivalLoadProbe.Install())return;
            if(!SurvivalSessionProbe.HeadlessRequested){Ensure();SurvivalSessionProbe.Install();Active.StartDedicated();return;}
#endif
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
            SurvivalSessionProbe.Install();
#endif
        }
        public static SurvivalNetworkSession Ensure(){if(Active!=null)return Active;return new GameObject("Survival networking").AddComponent<SurvivalNetworkSession>();}
        void Awake(){
#if UNITY_EDITOR || UNITY_SERVER
            if(DiagnosticMultipleSessions){if(Active==null)Active=this;DontDestroyOnLoad(gameObject);return;}
#endif
            if(Active!=null&&Active!=this){Destroy(gameObject);return;}Active=this;DontDestroyOnLoad(gameObject);
        }
        void Setup(bool server=false){
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
            SurvivalNetworkConditions.Configure();
#endif
            if(manager!=null)throw new InvalidOperationException("A network connection already exists");
            services=new SurvivalServices(
#if UNITY_EDITOR || UNITY_SERVER
                LoadBot?.registry
#else
                null
#endif
            );verifier=new SurvivalIdentityVerifier();
            var network=new GameObject("Survival transport");DontDestroyOnLoad(network);transport=network.AddComponent<UnityTransport>();
            // Keep transport alive until NGO's OnDestroy has finished. On one
            // shared GameObject Unity can destroy transport first after a timeout.
            var netcode=new GameObject("Survival netcode");DontDestroyOnLoad(netcode);manager=netcode.AddComponent<NetworkManager>();
            // Bounded shared packet queues must accommodate simultaneous joins and snapshot bursts.
            transport.MaxPacketQueueSize=server?8192:2048;
            manager.NetworkConfig=new NetworkConfig{NetworkTransport=transport,EnableSceneManagement=false,ConnectionApproval=true,TickRate=30,ClientConnectionBufferTimeout=20};
            manager.ConnectionApprovalCallback=Approve;manager.OnClientConnectedCallback+=OnConnected;manager.OnClientDisconnectCallback+=OnDisconnected;
        }
        void RegisterMessages(){
            manager.CustomMessagingManager.RegisterNamedMessageHandler(LeaveMessage,(sender,reader)=>{
                if(!IsServer||!peers.TryGetValue(sender,out var peer))return;
                try{var leave=Read<SurvivalLeave>(reader,64);if(leave?.version!=1)return;RetirePeer(sender,peer);Send(LeaveAckMessage,sender,new SurvivalLeave());}
                catch(Exception e){Debug.LogWarning("Session leave failed: "+e.GetType().Name);}
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(LeaveAckMessage,(sender,reader)=>{
                if(IsServer||sender!=NetworkManager.ServerClientId)return;
                try{if(Read<SurvivalLeave>(reader,64)?.version==1)leaveAcknowledged?.TrySetResult(true);}catch(Exception){/* A malformed acknowledgement cannot release the wait. */}
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage,(sender,reader)=>{
                if(!IsServer||!peers.TryGetValue(sender,out var peer))return;metrics.Received(reader.Length);
                try{var input=Read<SurvivalInput>(reader,1024);peer.input.Enqueue(input,clock.Tick,Config.tickRate,peer.motion.epoch);}catch(Exception){manager.DisconnectClient(sender,"Malformed input");}
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(ActionMessage,(sender,reader)=>{
                if(!IsServer||!peers.TryGetValue(sender,out var peer))return;metrics.Received(reader.Length);
                try{var action=Read<SurvivalAction>(reader,16384);long window=clock.Tick/Config.tickRate;if(peer.actionWindow!=window){peer.actionWindow=window;peer.actionsThisWindow=0;}
                    if(action==null||action.version!=1||action.epoch!=peer.motion.epoch||action.tick>clock.Tick+Config.tickRate*2L||action.sequence<=peer.newestAction||action.sequence-peer.newestAction>4096||peer.actions.Count>=32||++peer.actionsThisWindow>30)return;
                    peer.newestAction=action.sequence;peer.actions.Enqueue(action);
                }catch(Exception){manager.DisconnectClient(sender,"Malformed action");}
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(WelcomeMessage,(sender,reader)=>{
                if(IsServer||sender!=NetworkManager.ServerClientId)return;
                try{welcome=Read<SurvivalWelcome>(reader,32768);if(welcome.protocol!=SurvivalServerConfig.Protocol)throw new InvalidDataException("Incompatible protocol");
                    Config=welcome.config;Config.Validate();Status="Loading world";
                    if(HeadlessProbeClient){if(headlessDefinition!=null)Destroy(headlessDefinition);headlessDefinition=ScriptableObject.CreateInstance<PlanetDefinition>();JsonUtility.FromJsonOverwrite(welcome.definition,headlessDefinition);}
                    else app.StartNetworkSurvival(welcome.definition,this);
                }catch(Exception e){Status="World load failed: "+e.Message;_ = Disconnect();}
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage,(sender,reader)=>{
                if(IsServer||sender!=NetworkManager.ServerClientId)return;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
                Probe?.WireSnapshotBytes(reader.Length-reader.Position);
#endif
                try{Receive(Read<SurvivalSnapshot>(reader,65536));}catch(Exception e){Status="Snapshot rejected: "+e.Message;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
                    Probe?.SnapshotFailure(e.Message);
#if UNITY_EDITOR || UNITY_SERVER
                    LoadBot?.Failure(e.Message);
#endif
#endif
                }
            });
            manager.CustomMessagingManager.RegisterNamedMessageHandler(ReplyMessage,(sender,reader)=>{
                if(IsServer||sender!=NetworkManager.ServerClientId)return;var reply=Read<SurvivalActionReply>(reader,4096);Status=reply.message;ActionReplied?.Invoke(reply);
            });
        }
        static T Read<T>(FastBufferReader reader,int limit){reader.ReadValueSafe(out int length);if(length<0||length>limit||length>reader.Length-reader.Position)throw new InvalidDataException("Packet length");var bytes=new byte[length];reader.ReadBytesSafe(ref bytes,length);return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(bytes));}
        int Send<T>(string name,ulong target,T value,NetworkDelivery delivery=NetworkDelivery.ReliableFragmentedSequenced){
            return SendPayload(name,target,Encoding.UTF8.GetBytes(JsonUtility.ToJson(value)),delivery);
        }
        int SendPayload(string name,ulong target,byte[] bytes,NetworkDelivery delivery=NetworkDelivery.ReliableFragmentedSequenced){
            if(bytes.Length>65536)throw new InvalidDataException("Snapshot exceeds packet limit");
            using(var writer=new FastBufferWriter(bytes.Length+4,Allocator.Temp)){writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);manager.CustomMessagingManager.SendNamedMessage(name,target,writer,delivery);}metrics.Sent(bytes.Length+4);return bytes.Length+4;
        }
        async void Approve(NetworkManager.ConnectionApprovalRequest request,NetworkManager.ConnectionApprovalResponse response){
            response.Pending=true;response.CreatePlayerObject=false;int generation=lifecycle;string identity=null;
            try{
                if(!IsServer||peers.Count+admitted.Count+pendingAdmissions.Count>=Config.maxPlayers||request.Payload==null||request.Payload.Length>10000)throw new InvalidOperationException("Server full or invalid admission");
                pendingAdmissions.Add(request.ClientNetworkId);
                var admission=JsonUtility.FromJson<SurvivalAdmission>(Encoding.UTF8.GetString(request.Payload));
                if(admission==null||admission.protocol!=SurvivalServerConfig.Protocol||admission.content!=SurvivalServerConfig.Content)throw new InvalidOperationException("Incompatible game version");
                identity=await verifier.Verify(admission.token,Application.cloudProjectId,Config.environment);
                if(generation!=lifecycle||closing||manager==null||!manager.IsListening||!pendingAdmissions.Contains(request.ClientNetworkId))throw new InvalidOperationException("Connection cancelled");
                if(peers.Count+admitted.Count>=Config.maxPlayers)throw new InvalidOperationException("Server full");
                if(admitting.Contains(identity)||admitted.ContainsValue(identity))throw new InvalidOperationException("Character is already connected");
                foreach(var peer in peers.Values)if(peer.record.identity==identity)throw new InvalidOperationException("Character is already connected");
                admitting.Add(identity);var spawn=Spawn();string name=string.IsNullOrWhiteSpace(admission.name)?"Survivor":admission.name.Trim();if(name.Length>32)name=name.Substring(0,32);
                authority.RegisterSurvivor(identity,name,spawn,Config.worldId,Config.instanceId,Config.seed);admitted.Add(request.ClientNetworkId,identity);
                response.Approved=true;
            }catch(Exception e){response.Approved=false;response.Reason=e.Message;Debug.LogWarning("Survival admission rejected: "+e.Message);}
            finally{pendingAdmissions.Remove(request.ClientNetworkId);if(identity!=null)admitting.Remove(identity);response.Pending=false;}
        }
        FramePose Spawn(){if(spawnPose.HasValue)return spawnPose.Value;var surface=surfaces[1];var direction=PlanetStartingRegion.Select(surface.Definition,new Vector3(1,.12f,1.3f).normalized);var p=surface.GetSurfacePositionPrecise(direction,.05);spawnPose=new FramePose{body=1,x=p.x,y=p.y,z=p.z};return spawnPose.Value;}
        SurvivalServerConfig PublicConfig(){var config=JsonUtility.FromJson<SurvivalServerConfig>(JsonUtility.ToJson(Config));config.savePath="";return config;}
        void OnConnected(ulong id){
            if(!IsServer){connectedAt=Time.realtimeSinceStartupAsDouble;Status="Waiting for world";return;}
            if(!admitted.TryGetValue(id,out var identity)){manager.DisconnectClient(id,"Admission missing");return;}admitted.Remove(id);
            var record=authority.World.multiplayer.players.Find(p=>p.identity==identity);if(!RestoreDriverExit(record)){manager.DisconnectClient(id,"Your vehicle exit is blocked; clear the surrounding area before reconnecting");return;}record.controlledEntity=0;var actor=authority.World.Actor(record.actor);
            var forward=SurvivalMovement.Tangent(Vector3.forward,PlanetPrecisionPolicy.Direction(new Double3(actor.pose.x,actor.pose.y,actor.pose.z)));
            var peer=new Peer{record=record,newestAction=record.lastActionSequence,motion=new SurvivalMotion{actor=record.actor,name=record.name,pose=actor.pose,pitch=record.pitch,verticalSpeed=record.verticalSpeed,forwardX=forward.x,forwardY=forward.y,forwardZ=forward.z,grounded=true}};
            EnsurePlayerShip(record);peer.motion.pose=actor.pose;if(!RestorePassengerBerth(record)){manager.DisconnectClient(id,"Passenger berths are full; retry after a berth is free");return;}if(record.attachedShip!=0){var aboard=authority.World.multiplayer.ships.Find(s=>s.entity==record.attachedShip);if(aboard!=null){if(!RestoreInterior(peer,aboard)){manager.DisconnectClient(id,"No clear space aboard your ship; retry after the area is clear");return;}}else {record.attachedShip=0;record.interiorWalking=false;}}
            peers.Add(id,peer);connectedActors.Add(record.actor);actor.pose=ActorPose(peer.motion);var definition=surfaces[actor.pose.body].Definition;
            Send(WelcomeMessage,id,new SurvivalWelcome{actor=record.actor,config=PublicConfig(),definition=JsonUtility.ToJson(definition)});_ = services.Population(peers.Count);
            Debug.Log("Survivor connected; active="+peers.Count);
        }
        void OnDisconnected(ulong id){
            if(!IsServer)leaveAcknowledged?.TrySetResult(false);
            retiringConnections.Remove(id);
            admitted.Remove(id);pendingAdmissions.Remove(id);if(closing){peers.Remove(id);return;}
            if(IsServer){if(peers.TryGetValue(id,out var peer)){peer.input.Clear();ReleaseDriver(peer);SurvivalShipAuthority.Release(authority.World,peer.record);connectedActors.Remove(peer.record.actor);peers.Remove(id);authority.Save();}_ = services.Population(peers.Count);}
            else if(!closing){Status="Disconnected: "+manager.DisconnectReason;_ = Disconnect(true);}
        }
        void RetirePeer(ulong id,Peer peer){
            // Reliable application acknowledgement precedes local transport disposal.
            // Old connection IDs lose command/control authority immediately.
            peer.input.Clear();peer.actions.Clear();ReleaseDriver(peer);SurvivalShipAuthority.Release(authority.World,peer.record);connectedActors.Remove(peer.record.actor);peers.Remove(id);authority.Save();
            retiringConnections[id]=Time.realtimeSinceStartupAsDouble+1;_ = services?.Population(peers.Count);Debug.Log("Survivor left; active="+peers.Count);
        }
        async Task LeaveGracefully(){
            if(manager==null||manager.IsServer||!manager.IsConnectedClient||welcome==null)return;
            leaveAcknowledged=new TaskCompletionSource<bool>();
            try{
                Send(LeaveMessage,NetworkManager.ServerClientId,new SurvivalLeave());double until=Time.realtimeSinceStartupAsDouble+3;
                while(!leaveAcknowledged.Task.IsCompleted&&manager!=null&&manager.IsConnectedClient&&Time.realtimeSinceStartupAsDouble<until)await Task.Yield();
                // Relay still counts the retiring transport, even after its character
                // authority is gone. Wait for the server close before joining again.
                if(leaveAcknowledged.Task.IsCompleted&&leaveAcknowledged.Task.Result)while(manager!=null&&manager.IsConnectedClient&&Time.realtimeSinceStartupAsDouble<until+2)await Task.Yield();
            }
            catch(Exception e){Debug.LogWarning("Graceful leave unavailable: "+e.GetType().Name);}
            finally{leaveAcknowledged=null;}
        }
        public async Task Connect(BattleApp owner,string code,string lobbyId=null,string authenticationProfile="survival-client",string displayName=null){
            if(closing||starting||manager!=null)return;starting=true;closing=false;app=owner;int generation=++lifecycle;
            try{
                Setup();Status="Signing in";await services.Initialize("production",authenticationProfile);if(generation!=lifecycle)return;
                Status="Connecting through Relay";await services.Join(transport,code,lobbyId);if(generation!=lifecycle)return;
                manager.NetworkConfig.ConnectionData=Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SurvivalAdmission{token=services.AccessToken,name=displayName??PlayerPrefs.GetString("Survival.DisplayName","Survivor")}));
                if(!manager.StartClient())throw new InvalidOperationException("Transport could not start");RegisterMessages();connectedAt=Time.realtimeSinceStartupAsDouble;
            }catch(Exception e){Status="Connection failed: "+e.Message;await Disconnect(false);}finally{starting=false;}
        }
#if UNITY_SERVER || UNITY_EDITOR
        internal static PlanetDefinition ServerDefinition(int seed){var template=Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");var definition=Instantiate(template);definition.bodyId=1;definition.terrainSeed=seed;definition.radius=PlanetDefinition.DefaultRadius;definition.terrainHeight=1800;definition.oceanHeight=-35;definition.atmosphereHeight=Mathf.Max(10000,definition.atmosphereHeight);definition.gravitationalParameter=9.81*definition.radius*definition.radius;return definition;}
#endif
#if UNITY_SERVER
        async void StartDedicated(){
            try{
                Application.runInBackground=true;Application.targetFrameRate=60;
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-server-config");string configPath=index>=0&&index+1<args.Length?args[index+1]:Path.Combine(AppContext.BaseDirectory,"server.json");
                Config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(configPath));Config.Validate();
                if(string.IsNullOrEmpty(Config.savePath))Config.savePath=Path.Combine(Application.persistentDataPath,"Servers",Config.worldId,Config.instanceId,"industry.xml");
                Config.savePath=Path.GetFullPath(Config.savePath);var saved=IndustrialAuthority.Load(Config.savePath);
                if(saved.multiplayer!=null&&(saved.multiplayer.worldId!=Config.worldId||saved.multiplayer.instanceId!=Config.instanceId||saved.multiplayer.worldSeed!=Config.seed||saved.multiplayer.contentVersion!=1))throw new InvalidOperationException("Server config does not match the saved world, instance, seed or content version");
                saved.multiplayer??=new SurvivalSessionState{worldId=Config.worldId,instanceId=Config.instanceId,worldSeed=Config.seed};authority=new IndustrialAuthority(saved,Config.savePath);authority.JournalTiming=(serialize,flush)=>{metrics.Record(SurvivalNetworkMetrics.Phase.JournalSerialize,serialize);metrics.Record(SurvivalNetworkMetrics.Phase.JournalFlush,flush);};authority.CheckpointTiming=milliseconds=>metrics.Record(SurvivalNetworkMetrics.Phase.Checkpoint,milliseconds);authority.World.InventoryPoseResolver=ResolveNetworkInventoryPose;ResetDriverLeases();ResetPilotLeases();clock.Restore(saved.multiplayer.serverTick);authority.Save();
                InitializeUniverse();
                SeedResources(Spawn());Setup(true);manager.NetworkConfig.TickRate=(uint)Config.tickRate;
                await services.Initialize(Config.environment,Config.AuthenticationProfile());await verifier.Prepare();await services.Allocate(transport,Config);
                if(!manager.StartServer())throw new InvalidOperationException("Server transport could not start");RegisterMessages();
                // Offset publication from the 10 Hz industrial journal/step at the default rates.
                snapshotElapsed=.5/Config.snapshotRate;lastUpdate=Time.realtimeSinceStartupAsDouble;metrics.Reset();
                // Bind Relay before join-code/Lobby retries so its allocation stays alive and
                // discovery never advertises a transport that has not started.
                await SurvivalSessionProbe.DelayBeforeJoinCode(this);
                await services.Advertise(Config,peers.Count);Status="Server ready · Relay "+services.JoinCode;Debug.Log(Status);
            }catch(Exception e){Status="Dedicated server startup failed: "+e.Message;Debug.LogError("Dedicated server startup failed: "+e);try{await Disconnect(false);}finally{Application.Quit(1);}}
        }
#endif
        void Update(){
            services?.Tick();
            if(manager==null)return;
            if(IsServer&&retiringConnections.Count>0){double now=Time.realtimeSinceStartupAsDouble;foreach(var pair in new List<KeyValuePair<ulong,double>>(retiringConnections))if(now>=pair.Value){retiringConnections.Remove(pair.Key);manager.DisconnectClient(pair.Key,"Session left");}}
            if(IsServer){double now=Time.realtimeSinceStartupAsDouble;double elapsed=now-lastUpdate;lastUpdate=now;clock.Advance(elapsed,Config.tickRate,()=>{long started=metrics.Begin();ServerTick();metrics.End(started,peers.Count,clock.Tick);});if(metricsReportDue){metricsReportDue=false;metrics.Write(Config.savePath+".network-metrics.json",peers.Count);}}
            else {if(!closing&&connectedAt>0&&welcome==null&&Time.realtimeSinceStartupAsDouble-connectedAt>30){Status="Connection timed out";_ = Disconnect(true);}ClientTick();}
        }
        bool metricsReportDue;
        void ServerTick(){
            float dt=1f/Config.tickRate;long stage=metrics.Begin();
            StepUniverse(dt);
            UpdateServerContacts();
            foreach(var pair in peers){var peer=pair.Value;var actor=authority.World.Actor(peer.record.actor);var input=peer.input.Next();
                if(actor.health>0&&surfaces.TryGetValue(actor.pose.body,out var surface)){if(peer.record.attachedShip!=0)DriveServerShip(peer,input,dt);else if(peer.record.controlledEntity!=0)DriveServer(peer,input,dt);else StepWalking(peer.motion,input,surface,dt,actor.dose>=50?4:actor.dose>=25?6:Config.walkSpeed,true);}
                peer.motion.sequence=input.sequence;peer.motion.tick=clock.Tick;actor.pose=ActorPose(peer.motion);actor.collisionRadius=peer.motion.vehicle!=null?2.8:.35;peer.record.verticalSpeed=peer.motion.verticalSpeed;peer.record.pitch=peer.motion.pitch;peer.record.acknowledgedInput=peer.motion.sequence;
                peer.record.hunger=Math.Max(0,peer.record.hunger-dt*.02);peer.record.thirst=Math.Max(0,peer.record.thirst-dt*.035);
                if(peer.record.hunger<=0||peer.record.thirst<=0)actor.health=Math.Max(0,actor.health-dt*.5);
                var body=surfaces[actor.pose.body].Definition;double altitude=new Double3(actor.pose.x,actor.pose.y,actor.pose.z).Magnitude-body.radius;
                if(!body.BreathableAt((float)altitude)&&!ShipPressurized(peer)){peer.record.oxygen=Math.Max(0,peer.record.oxygen-dt*.5);if(peer.record.oxygen<=0)actor.health=Math.Max(0,actor.health-dt*5);}
                if(actor.health<=0){DropDeadSurvivor(peer.record);ReleaseDriver(peer);SurvivalShipAuthority.Release(authority.World,peer.record);}
                int n=0;while(peer.actions.Count>0&&n++<4)ApplyAction(pair.Key,peer,peer.actions.Dequeue());peer.tokens=Math.Min(Config.packetBytesPerSecond,peer.tokens+Config.packetBytesPerSecond*dt);
            }
            metrics.Measure(SurvivalNetworkMetrics.Phase.Players,stage);stage=metrics.Begin();
            authority.World.multiplayer??=new SurvivalSessionState{worldId=Config.worldId,instanceId=Config.instanceId,worldSeed=Config.seed};authority.World.multiplayer.serverTick++;
            StepUnoccupiedVehicles(dt);StepServerShips(dt);
            metrics.Measure(SurvivalNetworkMetrics.Phase.Craft,stage);
            industryElapsed+=dt;if(industryElapsed>=.1){stage=metrics.Begin();authority.Step();metrics.Measure(SurvivalNetworkMetrics.Phase.Industry,stage);industryElapsed-=.1;}
            stage=metrics.Begin();ProcessNetworkDeaths();metrics.Measure(SurvivalNetworkMetrics.Phase.Death,stage);
            snapshotElapsed+=dt;if(snapshotElapsed>=1.0/Config.snapshotRate){snapshotElapsed-=1.0/Config.snapshotRate;stage=metrics.Begin();Publish();metrics.Measure(SurvivalNetworkMetrics.Phase.Snapshot,stage);}
            saveElapsed+=dt;if(saveElapsed>=30){authority.Save();metricsReportDue=true;saveElapsed=0;}
        }
        double industryElapsed;
        void ApplyAction(ulong client,Peer peer,SurvivalAction action){
            var actor=authority.World.Actor(peer.record.actor);string error=null;
            try{
                error=authority.ExecuteSurvivalAction(actor.id,action.sequence,()=>{if(actor.health<=0&&action.kind!="respawn")return "Respawn before performing gameplay actions";string actionError=null;switch(action.kind){
                    case "industrial":
                        actionError=ExecuteNetworkIndustrial(peer,action.command);break;
                    case "building":actionError=ExecuteBuilding(actor.id,action.building);break;
                    case "ship-invite":actionError=SurvivalShipAuthority.Invite(authority.World,actor.id,action.entity,action.count,true);break;
                    case "ship-revoke":actionError=SurvivalShipAuthority.Invite(authority.World,actor.id,action.entity,action.count,false);break;
                    case "board-passenger":actionError=BoardPassenger(peer,action.entity);break;
                    case "ship-door":actionError=ToggleShipDoor(peer,action.entity,action.slot);break;
                    case "enter-pilot":actionError=EnterPilot(peer,action.entity);break;
                    case "leave-pilot":actionError=LeavePilot(peer);break;
                    case "exit-ship":actionError=ExitShip(peer);break;
                    case "enter-driver":actionError=EnterDriver(peer,action.entity);break;
                    case "exit-driver":actionError=ExitDriver(peer);break;
                    case "use":actionError=UseItem(peer,action.item);break;
                    case "shoot":actionError=FireWeapon(peer,action.sequence);break;
                    case "inventory-move":actionError=authority.MoveInventory(actor.id,action.from,action.to,action.count);break;
                    case "slot":if(action.slot<0||action.slot>=10)actionError="Invalid slot";else authority.SelectInventorySlot(actor.id,action.slot);break;
                    case "respawn-place":actionError=PlaceNetworkRespawn(peer,action);break;
                    case "respawn-pack":actionError=ManageNetworkRespawn(peer,action,false);break;
                    case "respawn-rename":actionError=ManageNetworkRespawn(peer,action,true);break;
                    case "respawn":actionError=RespawnNetworkPlayer(peer,action.entity);break;
                    default:actionError="Unknown action";break;
                }
                return actionError;});
            }catch(Exception e){error="Rejected: "+e.GetType().Name;}
            Send(ReplyMessage,client,new SurvivalActionReply{sequence=action.sequence,tick=clock.Tick,accepted=error==null,message=error??"Applied"});
        }
        static readonly HashSet<string> AllowedIndustrial=new HashSet<string>{"place","pack","pile","craft","cancel-craft","dig","deposit-item","withdraw-item","withdraw-input","fill-water","drain-water","protection","treat","recipe","toggle","drain-energy","recycle-legacy","recover-tailings","haul-stop","power","water","belt","remove-power","remove-water","remove-belt","disconnect","ship-place","ship-remove","ship-wire","ship-unwire"};
        string ExecuteNetworkIndustrial(Peer peer,IndustrialCommand command){
            var actor=authority.World.Actor(peer.record.actor);if(actor==null||actor.health<=0)return "Respawn before performing gameplay actions";
            if(command==null||command.route?.Count>128||!AllowedIndustrial.Contains(command.kind??""))return "Action is not available through this network adapter";
            string error=ValidatePlacement(actor,command)??ValidateFactoryConnection(actor,command)??ValidateShipConstruction(actor,command);if(error!=null)return error;
            if(command.kind=="dig"&&peer.record.nextGatherTick>clock.Tick)return "Gathering is cooling down";
            if(command.kind=="dig")peer.record.nextGatherTick=clock.Tick+(long)Math.Ceiling(.4*Config.tickRate);
            command.sequence=actor.lastSequence+1;var result=authority.Execute(actor.id,command);if(!result.accepted)return result.message;
            if(ShipConstruction(command.kind))RefreshShipConstruction(actor.id);return null;
        }
        void Publish()=>Publish((client,packet,payload)=>SendPayload(StateMessage,client,payload));
        void Publish(Func<ulong,SurvivalSnapshot,byte[],int> send){
            var rejected=new List<ulong>();
            interest.Clear();entities.Clear();
            foreach(var peer in peers.Values){var actor=authority.World.Actor(peer.record.actor);var proxy=SurvivalPlayerProxy.From(peer.motion,actor.pose);AddEntity(actor.id*8+1,"player",actor.pose,JsonUtility.ToJson(proxy));entities[actor.id*8+1].fingerprintData=proxy.Fingerprint();var presentation=entities[actor.id*8+1];if(proxy.TryParentPose(out var local)){local.body=actor.pose.body;presentation.fingerprintPose=local;presentation.requiredParent=proxy.parent*8+(proxy.parentKind=="ship"?5:2);}}
            foreach(var m in authority.World.machines)AddEntity(m.id*8+2,"machine",m.pose,"");
            foreach(var p in authority.World.piles)AddEntity(p.id*8+3,"pile",p.pose,JsonUtility.ToJson(p));
            foreach(var d in authority.World.deposits)AddEntity(d.id*8+4,"deposit",d.pose,JsonUtility.ToJson(d));
            foreach(var point in authority.World.respawnPoints)if(point.pose.frame==0)AddEntity(point.id*8+7,"respawn-point",point.pose,point.definition);
            foreach(var drop in authority.World.droppedItems){var pose=ResolveNetworkInventoryPose(drop.pose)??drop.resolvedPose;AddEntity(drop.id*8,"loot",pose,"");}
            foreach(var ship in authority.World.multiplayer.ships){var state=ship.Copy();state.physics="";state.invitedActors.Clear();AddEntity(ship.entity*8+5,"ship",ship.pose,JsonUtility.ToJson(state));entities[ship.entity*8+6]=new SurvivalEntity{id=ship.entity*8+6,kind="ship-layout",pose=new FramePose{body=ship.pose.body,frame=ship.entity},data=ShipLayout(ship)};}
            PublishFactoryLinks();PublishBuildings();
            foreach(var pair in peers){var peer=pair.Value;var actor=authority.World.Actor(peer.record.actor);
                double speed=peer.motion.ship==null?0:new Double3(peer.motion.ship.velocityX,peer.motion.ship.velocityY,peer.motion.ship.velocityZ).Magnitude;
                double radius=(peer.motion.ship!=null?Config.shipRadius:Config.surfaceRadius)+Math.Min(20000,speed);
                var nearby=new HashSet<long>();interest.Query(actor.pose,radius,nearby);
                var retained=new HashSet<long>();interest.Query(actor.pose,radius+Config.hysteresis,retained);foreach(long id in peer.visible)if(retained.Contains(id))nearby.Add(id);
                var detailedShips=new HashSet<long>();interest.Query(actor.pose,Config.shipRadius+Math.Min(20000,speed),detailedShips,true);
                var retainedDetails=new HashSet<long>();interest.Query(actor.pose,Config.shipRadius+Math.Min(20000,speed)+Config.hysteresis,retainedDetails,true);
                foreach(long id in retainedDetails)if(peer.visible.Contains(id+1))detailedShips.Add(id);
                foreach(long id in detailedShips){nearby.Add(id);nearby.Add(id+1);}
                interest.Query(actor.pose,Config.proxyRadius,nearby,true);nearby.Remove(actor.id*8+1);
                var retainedProxies=new HashSet<long>();interest.Query(actor.pose,Config.proxyRadius+Config.hysteresis,retainedProxies,true);foreach(long id in retainedProxies)if(peer.visible.Contains(id))nearby.Add(id);
                if(peer.motion.ship!=null){nearby.Add(peer.motion.ship.entity*8+5);nearby.Add(peer.motion.ship.entity*8+6);detailedShips.Add(peer.motion.ship.entity*8+5);}
                foreach(long id in new List<long>(nearby))if(entities.TryGetValue(id,out var child)&&child.requiredParent!=0&&entities.TryGetValue(child.requiredParent,out var parent)){nearby.Add(parent.id);if(parent.kind=="ship"){nearby.Add(parent.id+1);detailedShips.Add(parent.id);}}
                AddFactoryEndpoints(nearby);
                RetainLarge(peer,nearby);
                var assembly=peer.motion.ship==null?null:authority.World.Ship(peer.motion.ship.owner);
                bool sendLayout=assembly!=null&&(peer.privateShip!=peer.motion.ship.entity||peer.privateShipRevision!=assembly.revision);
                var packetMotion=peer.motion;if(packetMotion.ship!=null){packetMotion=packetMotion.Copy();packetMotion.ship.invitedActors.Clear();if(packetMotion.ship.owner!=actor.id)packetMotion.ship.physics="";}
                var ownShip=authority.World.multiplayer.ships.Find(s=>s.owner==actor.id);
                var packet=new SurvivalSnapshot{tick=clock.Tick,motion=packetMotion,self=actor,vitals=peer.record,pilotedShip=sendLayout&&peer.large==null?(assembly.owner==actor.id?assembly:JsonUtility.FromJson<ShipAssembly>(ShipLayout(peer.motion.ship))):null,ownedShip=ownShip?.entity??0,shipInvites=ShipInvites(ownShip),incomingInvitations=IncomingInvitations(actor.id)};
                packet.industryTick=authority.World.tick;foreach(var point in authority.World.respawnPoints)if(point.owner==actor.id)packet.respawnPoints.Add(point);
                peer.delivery.RetainPending(nearby);
                var candidates=new List<SurvivalInterestDelivery.Candidate>();var changes=new Dictionary<long,SurvivalEntity>();
                foreach(long id in nearby){var entity=entities[id];string data=entity.data,kind=entity.kind;
                    if(entity.kind=="machine"){
                        var machine=authority.World.Machine(id/8);var copy=JsonUtility.FromJson<IndustrialMachine>(JsonUtility.ToJson(machine));
                        if(machine.owner!=actor.id||actor.pose.Distance(machine.pose)>12){copy.input=new Inventory();copy.output=new Inventory();copy.reserved.Clear();copy.frontTailings=null;copy.constructionPaid.Clear();}
                        data=JsonUtility.ToJson(copy);
                    }else if(entity.kind=="loot"){
                        var drop=authority.World.Dropped(id/8);data=JsonUtility.ToJson(new DroppedInventory{id=drop.id,pose=entity.pose,resolvedPose=entity.pose,inventory=actor.health>0&&actor.pose.Distance(entity.pose)<=12?drop.inventory:new Inventory()});
                    }else if(entity.kind=="ship"&&!detailedShips.Contains(id)){
                        if(peer.nextProxyTick.TryGetValue(id,out long due)&&clock.Tick<due)continue;
                        var ship=authority.World.multiplayer.ships.Find(x=>x.entity==id/8);kind="ship-proxy";data=JsonUtility.ToJson(new ShipProxy{pose=ship.pose,rotationX=ship.rotationX,rotationY=ship.rotationY,rotationZ=ship.rotationZ,rotationW=ship.rotationW});
                    }
                    var signature=new SurvivalInterestDelivery.Fingerprint{kind=kind,data=entity.fingerprintData??data,pose=entity.fingerprintPose??(entity.kind=="player"?SurvivalPlayerProxy.FingerprintPose(entity.pose):entity.pose)};
                    if(peer.delivery.IsCurrent(id,signature))continue;
                    var change=kind==entity.kind&&data==entity.data?entity:new SurvivalEntity{id=id,kind=kind,pose=entity.pose,data=data,revision=clock.Tick};
                    change.revision=clock.Tick;int cost=SurvivalSnapshotEncoding.Entity(change).Length+1;
                    int priority=kind=="ship-layout"?1:kind=="ship-proxy"?3:2;
                    var candidate=peer.delivery.Stage(id,signature,clock.Tick,priority,actor.pose.Distance(entity.pose),cost);
                    if(candidate!=null){candidates.Add(candidate);changes.Add(id,change);}
                }
                int maximum=(int)Math.Min(60000,Config.packetBytesPerSecond);
                if(candidates.Exists(candidate=>candidate.bytes>SurvivalChunkSender.MaximumBytes)){rejected.Add(pair.Key);continue;}
                if(peer.large==null){
                    int basis=Encoding.UTF8.GetByteCount(JsonUtility.ToJson(packet))+4;
                    var ordered=SurvivalInterestDelivery.Select(new List<SurvivalInterestDelivery.Candidate>(candidates),clock.Tick,Config.tickRate,SurvivalChunkSender.MaximumBytes);
                    string signature=basis>maximum?PrivateSignature(packet):null;bool includePrivate=signature==null||signature!=peer.privateSignature;
                    int coreBasis=basis>maximum?Encoding.UTF8.GetByteCount(JsonUtility.ToJson(LeanSnapshot(packet)))+4:basis;
                    int inlineLimit=Math.Max(768,Math.Min(maximum-coreBasis,Config.packetBytesPerSecond/Config.snapshotRate));
                    var oversized=ordered.Find(candidate=>coreBasis+candidate.bytes>maximum||candidate.bytes>inlineLimit);
                    if(basis>maximum&&(includePrivate||packet.pilotedShip!=null)||oversized!=null){if(!BeginLarge(peer,packet,oversized,oversized==null?null:changes[oversized.id],maximum,includePrivate,signature)){rejected.Add(pair.Key);continue;}}
                    else if(basis>maximum)packet=LeanSnapshot(packet);
                }
                if(peer.large!=null)packet=StreamingEnvelope(peer,packet);
                packet.cancelTransfer=peer.cancelTransfer;
                int estimated=Encoding.UTF8.GetByteCount(JsonUtility.ToJson(packet))+4;
                if(estimated>maximum){rejected.Add(pair.Key);continue;}
                foreach(long removed in peer.delivery.Removals(nearby)){
                    int cost=removed.ToString(System.Globalization.CultureInfo.InvariantCulture).Length+1;
                    if(estimated+cost>Math.Min(maximum,peer.tokens))break;packet.removed.Add(removed);estimated+=cost;
                }
                int available=(int)Math.Min(maximum,peer.tokens)-estimated;
                if(peer.large?.target!=null)candidates.RemoveAll(candidate=>candidate.id==peer.large.target.id);
                int reserved=peer.large==null?0:512;
                if(reserved>maximum-estimated){rejected.Add(pair.Key);continue;}
                int smallest=int.MaxValue;foreach(var candidate in candidates)if(candidate.bytes<=maximum-estimated-reserved)smallest=Math.Min(smallest,candidate.bytes);
                int advance=reserved+(smallest==int.MaxValue?0:smallest);
                // Accumulate enough budget for useful data. Sending only the core
                // each time tokens cross its size can otherwise starve all deltas.
                if((peer.large!=null||candidates.Count>0)&&available<advance)continue;
                var selected=SurvivalInterestDelivery.Select(candidates,clock.Tick,Config.tickRate,available-reserved);
                foreach(var candidate in selected)packet.changed.Add(changes[candidate.id]);
                var fragment=FitChunk(peer,packet,(int)Math.Min(maximum,peer.tokens));
                // Check the actual envelope before committing any subscription/removal state.
                byte[] payload=SurvivalSnapshotEncoding.Encode(packet);int packetBytes=payload.Length+4;
                if(packetBytes<=Math.Min(maximum,peer.tokens)){
                    peer.tokens-=send(pair.Key,packet,payload);peer.delivery.Commit(selected);peer.delivery.CommitRemovals(packet.removed);
                    CommitLarge(peer,fragment);
                    peer.cancelTransfer=0;
                    if(packet.privateDetails)peer.privateSignature=null;
                    foreach(long removed in packet.removed)peer.nextProxyTick.Remove(removed);
                    foreach(var change in packet.changed)if(change.kind=="ship-proxy")peer.nextProxyTick[change.id]=clock.Tick+Config.tickRate;
                    peer.visible=nearby;if(packet.pilotedShip!=null){peer.privateShip=peer.motion.ship.entity;peer.privateShipRevision=assembly.revision;}
                }
            }
            foreach(ulong client in rejected){Debug.LogError("Client state exceeds bounded snapshot/core limits: "+client);if(manager!=null)manager.DisconnectClient(client,"State exceeds configured snapshot limits; contact the server operator");}
        }
        void AddEntity(long id,string kind,FramePose pose,string data,double extent=0){entities.Add(id,new SurvivalEntity{id=id,kind=kind,pose=pose,data=data});interest.Add(new SurvivalInterestIndex.Entry{id=id,pose=pose,ship=kind=="ship",extent=extent});}
        void Receive(SurvivalSnapshot snapshot){
            if(closing||snapshot==null||welcome==null||snapshot.self==null||snapshot.self.id!=ActorId||snapshot.motion==null||snapshot.motion.actor!=ActorId||latest!=null&&snapshot.tick<latest.tick)return;
            int normalized=snapshot.NormalizeReferences();
            largeReceiver.Cancel(snapshot.cancelTransfer);
            MergePrivate(snapshot);
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
            if(Probe!=null)Probe.EmptyReferencesNormalized(normalized);
#endif
            ReceiveMachineStates(snapshot);ReceiveShipStates(snapshot);ReceivePlayers(snapshot);ReceiveFactoryLinks(snapshot);ReceiveBuildings(snapshot);ReceiveContacts(snapshot);
            bool wasDead=latest?.self!=null&&latest.self.health<=0;latest=snapshot;if(wasDead!=(snapshot.self.health<=0))UpdateNetworkCursor();actionSequence=Math.Max(actionSequence,snapshot.vitals.lastActionSequence);foreach(long id in snapshot.removed)visible.Remove(id);foreach(var entity in snapshot.changed)visible[entity.id]=entity;
            bool reset=predicted!=null&&predicted.epoch!=snapshot.motion.epoch;
            if(reset){unacknowledged.Clear();predictionHistory.Clear();pendingLook=Vector2.zero;pendingJump=pendingAssist=pendingGear=false;}
            unacknowledged.RemoveAll(i=>i.sequence<=snapshot.motion.sequence);
            bool snap=reset||predicted==null||predicted.pose.body!=snapshot.motion.pose.body||predicted.pose.frame!=snapshot.motion.pose.frame;
            double error=SurvivalPrediction.MatchingError(predictionHistory,snapshot.motion);
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
            Probe?.Snapshot(snapshot,error,reset||error>=CorrectionDistance(snapshot.motion),predictionHistory,inputSequence);
#endif
            #if UNITY_EDITOR || UNITY_SERVER
            LoadBot?.Snapshot(snapshot,error,reset||error>=CorrectionDistance(snapshot.motion));
#endif
            predicted=snapshot.motion.Copy();if(ClientSimulationReady){if(latest.self.health>0)SurvivalPrediction.Replay(predicted,unacknowledged,predictionHistory,1f/Config.tickRate,PredictMotion);clientWorld?.PresentNetworkObserver(predicted,snap||error>=CorrectionDistance(snapshot.motion));}
            var keys=new List<uint>(predictionHistory.Keys);foreach(uint key in keys)if(key<=snapshot.motion.sequence)predictionHistory.Remove(key);
            Status="Connected";
            if(snapshot.chunk!=null)ReceiveLarge(snapshot.chunk);
        }
        internal void BindWorld(PlanetDevelopment world){clientWorld=world;chat=gameObject.AddComponent<SurvivalChat>();chat.Bind(this);}
        void ClientTick(){
            if(closing||!Online||!ClientSimulationReady||predicted==null)return;
            if(!HeadlessProbeClient){GameplayInput();PlacementInput();}var keys=UnityEngine.InputSystem.Keyboard.current;var mouse=UnityEngine.InputSystem.Mouse.current;
            bool blocked=HeadlessProbeClient||ChatInputCaptured||ClientMenuOpen||!Application.isFocused||clientWorld.PersonalMenuOpen;
            if(blocked){pendingLook=Vector2.zero;pendingJump=pendingAssist=pendingGear=false;}
            if(!blocked&&mouse!=null)pendingLook+=mouse.delta.ReadValue()*.12f;
            if(!blocked&&keys?.spaceKey.wasPressedThisFrame==true)pendingJump=true;
            if(!blocked&&keys!=null&&predicted.ship!=null&&!predicted.interior){pendingAssist|=keys[Bindings.pilotAssist].wasPressedThisFrame;pendingGear|=keys.gKey.wasPressedThisFrame;}
            clientAccumulator=Mathf.Min(clientAccumulator+Time.unscaledDeltaTime,.25f);float dt=1f/Config.tickRate;
            while(clientAccumulator>=dt){clientAccumulator-=dt;
                var input=new SurvivalInput{sequence=++inputSequence,epoch=predicted.epoch,brake=blocked||(predicted.ship!=null?keys?.bKey.isPressed==true:predicted.vehicle!=null&&keys?.spaceKey.isPressed==true),boost=!blocked&&keys?.leftShiftKey.isPressed==true,z=blocked||keys==null?0:(keys.rKey.isPressed?1:0)-(keys.cKey.isPressed?1:0),roll=blocked||keys==null?0:(keys.qKey.isPressed?1:0)-(keys.eKey.isPressed?1:0),toggleAssist=pendingAssist,toggleGear=pendingGear,x=blocked||keys==null?0:(keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),y=blocked||keys==null?0:(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0),lookX=Mathf.Clamp(pendingLook.x,-180,180),lookY=Mathf.Clamp(pendingLook.y,-180,180),jump=pendingJump};pendingLook=Vector2.zero;pendingJump=pendingAssist=pendingGear=false;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
                if(Probe!=null)input=Probe.Controls(input);
#if UNITY_EDITOR || UNITY_SERVER
                if(LoadBot!=null)input=LoadBot.Controls(input);
#endif
#endif
                if(unacknowledged.Count>=Config.tickRate*2){Status="Waiting for server";break;}unacknowledged.Add(input);
                if(latest.self.health>0)PredictMotion(predicted,input,dt);predictionHistory[input.sequence]=predicted.Copy();
                Send(InputMessage,NetworkManager.ServerClientId,input,NetworkDelivery.UnreliableSequenced);
            }
            clientWorld?.PresentNetworkObserver(predicted,false);RenderEntities();
        }
        Vector2 pendingLook;bool pendingJump,pendingAssist,pendingGear;
        internal void Submit(SurvivalAction action){if(closing||!Connected||!Online)return;action.sequence=++actionSequence;action.tick=predicted?.tick??latest?.tick??0;action.epoch=predicted?.epoch??latest?.motion.epoch??0;action.inputSequence=inputSequence;Send(ActionMessage,NetworkManager.ServerClientId,action);}
        public Task Disconnect(bool returnToMenu=true){if(disconnectTask!=null&&!disconnectTask.IsCompleted)return disconnectTask;return disconnectTask=DisconnectCore(returnToMenu);}
        async Task DisconnectCore(bool returnToMenu){
            if(closing)return;closing=true;++lifecycle;var previousChat=chat;chat=null;var chatCleanup=previousChat?.StopAsync();
            Exception failure=null;
            try{
                await LeaveGracefully();welcome=null;
                if(authority!=null){
                    try{foreach(var peer in peers.Values){peer.input.Clear();ReleaseDriver(peer);SurvivalShipAuthority.Release(authority.World,peer.record);}authority.Save();}
                    catch(Exception e){failure=e;}
                    finally{try{authority.Dispose();}catch(Exception e){failure??=e;}authority=null;}
                }
                var previousManager=manager;manager=null;var previousTransport=transport;transport=null;
                if(previousManager!=null){
                    previousManager.OnClientConnectedCallback-=OnConnected;previousManager.OnClientDisconnectCallback-=OnDisconnected;previousManager.ConnectionApprovalCallback-=Approve;
                    try{previousManager.Shutdown(true);double until=Time.realtimeSinceStartupAsDouble+2;while(previousManager!=null&&previousManager.IsListening&&Time.realtimeSinceStartupAsDouble<until)await Task.Yield();}
                    catch(Exception e){failure??=e;}
                    finally{
                        if(previousManager!=null){Destroy(previousManager.gameObject);while(previousManager!=null)await Task.Yield();}
                    }
                    // Let the old manager clear its singleton before a reconnect creates another.
                    await Task.Yield();
                }
                if(previousTransport!=null)Destroy(previousTransport.gameObject);
                var previousServices=services;services=null;if(previousServices!=null)try{await previousServices.Leave();}catch(Exception e){failure??=e;}
                if(chatCleanup!=null)await chatCleanup;if(previousChat!=null)Destroy(previousChat);
                DisposeUniverse();peers.Clear();connectedActors.Clear();admitted.Clear();retiringConnections.Clear();visible.Clear();ClearLargeSnapshots();ClearEntityViews();DisposeVehicleSimulations();DisposeShipSimulations();unacknowledged.Clear();predictionHistory.Clear();predicted=null;latest=null;inputSequence=0;actionSequence=0;connectedAt=0;clientAccumulator=0;inventoryOpen=industryOpen=networkMenu=false;
                if(returnToMenu&&app!=null)app.ReturnFromNetwork();clientWorld=null;if(headlessDefinition!=null)Destroy(headlessDefinition);headlessDefinition=null;
            }finally{closing=false;}
            if(failure!=null)throw new InvalidOperationException("Session cleanup failed",failure);
        }
        void OnApplicationFocus(bool focus){if(!focus){pendingLook=Vector2.zero;pendingJump=pendingAssist=pendingGear=false;chat?.Mute();}}
        void OnApplicationQuit(){closing=true;chat?.Mute();if(authority!=null)foreach(var peer in peers.Values){ReleaseDriver(peer);SurvivalShipAuthority.Release(authority.World,peer.record);}DisposeVehicleSimulations();DisposeShipSimulations();authority?.Save();authority?.Dispose();_ = services?.Leave();}
        void OnDestroy(){if(Active==this)Active=null;}
    }
}
