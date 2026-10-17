using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>One viewed planet. Unvisited bodies are definitions; visited worlds retain GPU state while frozen.</summary>
    [DefaultExecutionOrder(-100)] public sealed class PlanetSystemRuntime:MonoBehaviour
    {
        public PlanetSystemDefinition Settings {get;private set;}
        public PlanetDefinition[] Bodies {get;private set;}
        public GalaxyModel Galaxy {get;private set;}
        public CivilisationRuntime Civilisation {get;private set;}
        GalaxySkyRenderer sky;
        internal GalaxySkyRenderer SkyForValidation=>sky;
        internal bool IsPreparedForValidation(int index)=>worlds[index]!=null&&worlds[index].Ready;
        public PlanetDevelopment ActiveWorld {get;private set;}
        public Quaternion SunRotation {get;private set;}
        PlanetDevelopment[] worlds;bool[] failed;PlanetDevelopment root;
        public string PreparationError {get;private set;}="";
        public readonly PlanetDistantTerrain DistantTerrain=new PlanetDistantTerrain();
        public int ResidentWorldCount {get;private set;}
        public double LongestPreparationStepMs {get;private set;}
        public readonly double[] PreparationStepMaxMs=new double[2048];
        internal void RecordPreparationStep(double ms,int step){LongestPreparationStepMs=Math.Max(LongestPreparationStepMs,ms);if(step<PreparationStepMaxMs.Length)PreparationStepMaxMs[step]=Math.Max(PreparationStepMaxMs[step],ms);}
        PlanetDevelopment CreateLocalWorld(int index){
            var host=new GameObject(Bodies[index].displayName+" local simulation");host.transform.SetParent(root.transform,false);
            var next=host.AddComponent<PlanetDevelopment>();next.DeferredInitialization=true;next.ResidentActive=false;next.SystemRuntime=this;next.systemDefinition=Settings;
            next.ConfigureDefinition(Bodies[index]);next.configuredRadius=Bodies[index].radius;next.terrainSeed=Bodies[index].terrainSeed;
            next.currentRtsContent=root.currentRtsContent;next.requestedCapacity=root.requestedCapacity;
            next.ambientZombies=root.ambientZombies;next.sessionMode=root.sessionMode;next.restoredCrowd=root.restoredCrowd;
            next.deformTerrainOnNuke=root.deformTerrainOnNuke;next.returnToMenu=root.ReturnToMenu;
            worlds[index]=next;ResidentWorldCount++;return next;
        }
        internal void PreparePlanet(int index){if(worlds[index]!=null||failed[index])return;var next=CreateLocalWorld(index);StartCoroutine(Prepare(index,next));}
        System.Collections.IEnumerator Prepare(int index,PlanetDevelopment next){
            var steps=next.InitializeOverFrames();while(true){
                bool more=false;Exception error=null;try{more=steps.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){PreparationError=Bodies[index].displayName+": "+error.Message;Debug.LogException(error);failed[index]=true;worlds[index]=null;ResidentWorldCount--;ArmyLifetime.Destroy(next.gameObject);yield break;}
                if(!more)break;yield return steps.Current;
            }
            ArmyLifetime.Destroy(Bodies[index]);Bodies[index]=next.definition;RefreshDistantBodies();
        }
        public void Initialize(PlanetDevelopment home,PlanetSystemDefinition settings){
            root=ActiveWorld=home;SunRotation=home.LightingForValidation.Sun.transform.rotation;Settings=settings;
            if(settings.galaxy!=null&&settings.galaxy.enabledInGame&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-legacy-system")<0){Galaxy=new GalaxyModel(settings.galaxy,settings,home.definition,out var planets,-home.LightingForValidation.Sun.transform.forward);Bodies=planets;sky=new GalaxySkyRenderer(Galaxy);DistantTerrain.MaximumCount=settings.galaxy.terrainPreviewBudget;}
            else Bodies=settings.Generate(home.definition);
            if(Galaxy!=null){var config=Resources.Load<CivilisationSettings>("MassiveArmy/Civilisation/DefaultCivilisation");if(config!=null&&config.enabledInGame)Civilisation=new CivilisationRuntime(this,config);}
            worlds=new PlanetDevelopment[Bodies.Length];failed=new bool[Bodies.Length];worlds[0]=home;ResidentWorldCount=1;RefreshDistantBodies();
        }
        void RefreshDistantBodies(){for(int i=0;i<worlds.Length;i++)if(worlds[i]!=null){var far=new PlanetDefinition[Bodies.Length-1];int k=0;for(int j=0;j<Bodies.Length;j++)if(j!=i)far[k++]=Bodies[j];worlds[i].distantBodies=far;}}
        void Update(){
            if(Galaxy==null||ActiveWorld==null||!ActiveWorld.Ready)return;
            var observer=ActiveWorld.ObserverPosition;var offset=observer.RelativeTo(ActiveWorld.definition.position);
            if(!ActiveWorld.Paused){double step=Math.Min(Time.deltaTime,.05);Galaxy.Publish(Galaxy.Time+step,Bodies);Civilisation?.Advance(step);}
            for(int i=0;i<worlds.Length;i++)if(worlds[i]!=null&&worlds[i].definition!=Bodies[i]){worlds[i].definition.position=Bodies[i].position;worlds[i].definition.velocity=Bodies[i].velocity;}
            ActiveWorld.FollowCelestialFrame(offset);
            UpdateStarlight();
        }
        internal void UpdateSky(){if(Galaxy!=null&&ActiveWorld!=null)sky.Update(ActiveWorld.View,ActiveWorld.ObserverPosition,ActiveWorld.definition);}
        internal void DrawSky(UnityEngine.Rendering.CommandBuffer cmd){sky?.Draw(cmd);}
        internal void UpdateStarlight(){
            if(Galaxy==null||ActiveWorld?.LightingForValidation==null)return;
            var observer=ActiveWorld.ObserverPosition.RelativeTo(Galaxy.Origin);int closest=-1,second=-1;double best=0,next=0;
            for(int i=0;i<Galaxy.Nodes.Length;i++)if(Galaxy.Nodes[i].kind==CelestialKind.Star){var d=Galaxy.Current[i].position-observer;double power=Galaxy.Nodes[i].mu/Math.Max(1,d.Magnitude*d.Magnitude);if(power>best){next=best;second=closest;best=power;closest=i;}else if(power>next){next=power;second=i;}}
            if(closest<0)return;var sun=ActiveWorld.LightingForValidation.Sun;
            var direction=(observer-Galaxy.Current[closest].position).Float.normalized;
            // Keep sub-pixel stellar motion from dirtying native shadow/culling state every frame.
            if((sun.transform.forward-direction).sqrMagnitude>2.74156e-7f)sun.transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
            var colour=Color.Lerp(Color.white,Galaxy.Nodes[closest].colour,.25f);if(sun.color!=colour)sun.color=colour;
            SunRotation=sun.transform.rotation;
            if(second>=0)ActiveWorld.LightingForValidation.SetCompanion((Galaxy.Current[second].position-observer).Float.normalized,Galaxy.Nodes[second].colour,Galaxy.Settings.secondaryStarlight?(float)(next/Math.Max(1e-30,best)):0);
        }
        void LateUpdate(){
            if(ActiveWorld==null||!ActiveWorld.Ready||ActiveWorld.Paused||!ActiveWorld.IsPiloting)return;
            var observer=ActiveWorld.ObserverPosition;double best=observer.RelativeTo(ActiveWorld.definition.position).Magnitude-ActiveWorld.definition.radius;int target=-1;
            for(int i=0;i<Bodies.Length;i++)if(worlds[i]!=ActiveWorld){double altitude=observer.RelativeTo(Bodies[i].position).Magnitude-Bodies[i].radius;
                if(worlds[i]==null&&altitude<Settings.activationAltitude+Settings.preparationDistance)PreparePlanet(i);
                if(worlds[i]!=null&&worlds[i].Ready&&altitude<best-Settings.sleepHysteresis&&Settings.ShouldSimulate(Bodies[i],observer,false)){best=altitude;target=i;}}
            if(target>=0)EnterPlanet(target);
        }
        // Only at a sphere-of-interest crossing, never per unit. Canonical sector/double poses survive handoff.
        internal void EnterPlanet(int index){
            if(index<0||index>=Bodies.Length)throw new ArgumentOutOfRangeException(nameof(index));
            if(worlds[index]==ActiveWorld)return;
            var previous=ActiveWorld;var ship=previous.ShipForValidation;
            if(ship==null||!previous.IsPiloting)throw new InvalidOperationException("Enter a pilot seat before travelling between planets");
            var pose=ship.Pose;var position=previous.definition.position.Add(pose.position);
            var velocity=pose.linearVelocity+previous.definition.velocity;bool assist=ship.FlightAssistEnabled,gear=ship.GearDeployed;
            var next=worlds[index];if(next==null||!next.Ready)throw new InvalidOperationException("Destination planet is still preparing");
            previous.SetResidentActive(false);
            try{
                next.SetResidentActive(true);
                pose.position=position.RelativeTo(next.definition.position);pose.linearVelocity=velocity-next.definition.velocity;pose.frameId=0;
                next.PlayerForValidation.CopySessionFrom(previous.PlayerForValidation);next.ShipForValidation.CopyDoorsFrom(ship);
                next.SetShipControl(true);next.ShipForValidation.Arrive(pose,assist,gear);next.ShipForValidation.SetActive(true);next.Base?.Rebase();Physics.SyncTransforms();
                ActiveWorld=next;
                Civilisation?.ObserveArrival(next.definition.bodyId,0);
            }catch{
                next.SetResidentActive(false);ActiveWorld=previous;
                previous.SetResidentActive(true);ship.SetActive(true);throw;
            }
        }
        void OnGUI(){
            if(ActiveWorld==null||!ActiveWorld.Ready||!ActiveWorld.IsPiloting)return;
            GUI.color=new Color(.5f,1,.72f);var observer=ActiveWorld.ObserverPosition;
            GUI.Label(new Rect(12,100,280,25),ActiveWorld.definition.displayName+" / "+(ActiveWorld.SimulationActive?"LOCAL SIMULATION":"SURFACE ASLEEP"));
            int row=0;int system=Galaxy!=null?Galaxy.Nodes[Galaxy.NodeFor(ActiveWorld.definition)].system:-1;
            for(int i=0;i<Bodies.Length;i++){
                var body=Bodies[i];if(worlds[i]==ActiveWorld)continue;
                if(Civilisation!=null&&ActiveWorld.sessionMode!=PlanetGameMode.AiTest&&!Civilisation.Known(0,body.bodyId))continue;
                if(Galaxy!=null&&Galaxy.Nodes[Galaxy.NodeFor(body)].system!=system)continue;
                var offset=body.position.RelativeTo(observer);double distance=Math.Max(0,offset.Magnitude-body.radius);
                GUI.Label(new Rect(12,128+row++*22,280,24),body.displayName+" / "+(distance/1000).ToString("0.0")+" km / "+(worlds[i]==null?"UNVISITED":!worlds[i].Ready?"PREPARING":"ASLEEP"));
                var point=ActiveWorld.View.WorldToScreenPoint(ActiveWorld.View.transform.position+offset.Float.normalized*1000);
                if(point.z>0&&point.x>15&&point.x<Screen.width-180&&point.y>100&&point.y<Screen.height-100){GUI.Label(new Rect(point.x-6,Screen.height-point.y-9,20,22),"+");GUI.Label(new Rect(point.x+12,Screen.height-point.y-9,180,40),body.displayName+"\n"+(distance/1000).ToString("0.0")+" km");}
            }
            if(Galaxy!=null){GUI.Label(new Rect(12,130+row++*22,280,24),Civilisation!=null&&ActiveWorld.sessionMode!=PlanetGameMode.AiTest?"Astronomical survey incomplete":Galaxy.SystemNodes.Length+" star systems · "+Bodies.Length+" planets");
                int shown=0;for(int i=0;i<Galaxy.Nodes.Length&&shown<5;i++){var body=Galaxy.Nodes[i];if(body.kind!=CelestialKind.Star||body.system!=system)continue;var offset=Galaxy.Origin.Add(Galaxy.Current[i].position).RelativeTo(observer);GUI.Label(new Rect(12,130+row++*22,280,24),body.name+" · "+body.stellarClass+" · "+(offset.Magnitude/1000).ToString("0")+" km");shown++;}}
            if(PreparationError.Length>0)GUI.Label(new Rect(12,310,390,70),PreparationError);
            GUI.color=Color.white;
        }
        void OnDestroy(){sky?.Dispose();DistantTerrain.Dispose();if(Bodies==null)return;for(int i=1;i<Bodies.Length;i++)if(worlds[i]==null||Bodies[i]!=worlds[i].definition)ArmyLifetime.Destroy(Bodies[i]);}
    }
}
