using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>One viewed planet. Unvisited bodies are definitions; visited worlds retain GPU state while frozen.</summary>
    public sealed class PlanetSystemRuntime:MonoBehaviour
    {
        public PlanetSystemDefinition Settings {get;private set;}
        public PlanetDefinition[] Bodies {get;private set;}
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
            root=ActiveWorld=home;SunRotation=home.LightingForValidation.Sun.transform.rotation;Settings=settings;Bodies=settings.Generate(home.definition);
            worlds=new PlanetDevelopment[Bodies.Length];failed=new bool[Bodies.Length];worlds[0]=home;ResidentWorldCount=1;RefreshDistantBodies();
        }
        void RefreshDistantBodies(){for(int i=0;i<worlds.Length;i++)if(worlds[i]!=null){var far=new PlanetDefinition[Bodies.Length-1];int k=0;for(int j=0;j<Bodies.Length;j++)if(j!=i)far[k++]=Bodies[j];worlds[i].distantBodies=far;}}
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
            }catch{
                next.SetResidentActive(false);ActiveWorld=previous;
                previous.SetResidentActive(true);ship.SetActive(true);throw;
            }
        }
        void OnGUI(){
            if(ActiveWorld==null||!ActiveWorld.Ready||!ActiveWorld.IsPiloting)return;
            GUI.color=new Color(.5f,1,.72f);var observer=ActiveWorld.ObserverPosition;
            GUI.Label(new Rect(Screen.width-285,100,280,25),ActiveWorld.definition.displayName+" / "+(ActiveWorld.SimulationActive?"LOCAL SIMULATION":"SURFACE ASLEEP"));
            for(int i=0;i<Bodies.Length;i++){
                var body=Bodies[i];if(worlds[i]==ActiveWorld)continue;
                var offset=body.position.RelativeTo(observer);double distance=Math.Max(0,offset.Magnitude-body.radius);
                GUI.Label(new Rect(Screen.width-285,128+i*22,280,24),body.displayName+" / "+(distance/1000).ToString("0.0")+" km / "+(worlds[i]==null?"UNVISITED":!worlds[i].Ready?"PREPARING":"ASLEEP"));
                var point=ActiveWorld.View.WorldToScreenPoint(ActiveWorld.View.transform.position+offset.Float.normalized*1000);
                if(point.z>0&&point.x>15&&point.x<Screen.width-180&&point.y>100&&point.y<Screen.height-100){GUI.Label(new Rect(point.x-6,Screen.height-point.y-9,20,22),"+");GUI.Label(new Rect(point.x+12,Screen.height-point.y-9,180,40),body.displayName+"\n"+(distance/1000).ToString("0.0")+" km");}
            }
            if(PreparationError.Length>0)GUI.Label(new Rect(Screen.width-400,245,390,70),PreparationError);
            GUI.color=Color.white;
        }
        void OnDestroy(){DistantTerrain.Dispose();if(Bodies==null)return;for(int i=1;i<Bodies.Length;i++)if(worlds[i]==null||Bodies[i]!=worlds[i].definition)ArmyLifetime.Destroy(Bodies[i]);}
    }
}
