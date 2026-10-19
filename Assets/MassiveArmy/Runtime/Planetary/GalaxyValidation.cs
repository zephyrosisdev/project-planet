using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-230)] public sealed class GalaxyValidation:MonoBehaviour
    {
        [Serializable]sealed class Report {public string status="PASS",error="",predictionStatus="";public List<string> checks=new List<string>();public int systems,stars,planets,nodes,runtimeErrors;public double orbitalReturnMs;public double integrationDifference,predictionMaxSliceMs,primaryShare;public long allocatedBytes,reservedBytes;}
        readonly Report report=new Report();PlanetDevelopment world;string output;RenderTexture target,hudTarget,hudComposite;Material hudMaterial;bool captureHud;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-galaxy-validation")>=0)new GameObject("Galaxy integration validation").AddComponent<GalaxyValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();Time.captureDeltaTime=1f/60;world.Paused=true;world.ambientZombies=32;world.requestedCapacity=4096;world.currentRtsContent=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=OnLog;}
        void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)report.runtimeErrors++;}
        void OnDestroy(){Time.captureDeltaTime=0;Application.logMessageReceived-=OnLog;if(target!=null){target.Release();Destroy(target);}if(hudTarget!=null){hudTarget.Release();Destroy(hudTarget);}if(hudComposite!=null){hudComposite.Release();Destroy(hudComposite);}if(hudMaterial!=null)Destroy(hudMaterial);}
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks.Add(message);}
        void Start(){StartCoroutine(Guard());}
        IEnumerator Guard(){var steps=Run();while(true){object current;try{if(!steps.MoveNext())break;current=steps.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);break;}yield return current;}report.allocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.reservedBytes=Profiler.GetTotalReservedMemoryLong();File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        IEnumerator Run(){
            while(!world.Ready)yield return null;world.Paused=true;world.Rig.InputEnabled=false;
            var runtime=world.SystemRuntime;var galaxy=runtime.Galaxy;if(PlanetOrbitalStress.Enabled)PlanetOrbitalStress.Seed(world);Check(galaxy!=null,"Default planet entry creates a generated galaxy");
            report.systems=galaxy.SystemNodes.Length;report.nodes=galaxy.Nodes.Length;report.planets=runtime.Bodies.Length;
            foreach(var n in galaxy.Nodes)if(n.kind==CelestialKind.Star)report.stars++;
            Check(report.systems==48&&report.planets==192&&report.stars>48,"Configured 48 systems and 192 planets, including binary stars");
            Check(world.definition.radius==120000,"Home planet retains its 120 km radius");
            Check(runtime.ResidentWorldCount==1,"Only the home planet creates a local GPU simulation");
            bool untouched=true;for(int i=1;i<runtime.Bodies.Length;i++)untouched&=runtime.Bodies[i].edits==null&&runtime.Bodies[i].stampField==null;
            Check(untouched,"All distant gameplay definitions remain free of terrain edits, stamp fields and armies");
            var clone=Instantiate(world.definition);var repeat=new GalaxyModel(galaxy.Settings,runtime.Settings,clone,out var generated,galaxy.HomeSunDirection);
            for(int i=0;i<galaxy.Nodes.Length;i++)Check((galaxy.Current[i].position-repeat.Current[i].position).Magnitude<1e-7&&galaxy.Nodes[i].mu==repeat.Nodes[i].mu,"Seed reproduces celestial body "+i);
            for(int i=0;i<generated.Length;i++)Destroy(generated[i]);
            var states=new CelestialState[galaxy.Nodes.Length];var before=new CelestialState[states.Length];var after=new CelestialState[states.Length];
            galaxy.Evaluate(-.01,before);galaxy.Evaluate(.01,after);double velocityError=0;
            for(int i=1;i<states.Length;i++)velocityError=Math.Max(velocityError,((after[i].position-before[i].position)*50-galaxy.Current[i].velocity).Magnitude);
            Check(velocityError<.01,"Analytical ephemeris velocities match position derivatives ("+velocityError.ToString("F6")+" m/s)");
            int bary=galaxy.SystemNodes[0],starA=bary+1,starB=bary+2;var a=galaxy.Nodes[starA];var b=galaxy.Nodes[starB];
            var centre=((galaxy.Current[starA].position-galaxy.Current[bary].position)*a.mu+(galaxy.Current[starB].position-galaxy.Current[bary].position)*b.mu)*(1/(a.mu+b.mu));
            Check(centre.Magnitude<1e-5,"Binary star positions preserve their mass-weighted barycentre");
            double separation=(galaxy.Current[starA].position-galaxy.Current[starB].position).Magnitude;
            galaxy.Evaluate(10000,states);
            Check(Math.Abs((states[starA].position-states[starB].position).Magnitude-separation)<1e-5,"Binary separation survives orbital evolution");
            Check((states[bary].position-galaxy.Current[bary].position).Magnitude>100000,"Star system actually orbits the galactic black hole");
            int home=galaxy.NodeFor(world.definition);
            Check((states[home].position-states[bary].position-(galaxy.Current[home].position-galaxy.Current[bary].position)).Magnitude>10000,"Planet actually orbits its stellar barycentre");
            Check(galaxy.Nodes[home].semiMajor>separation*10,"Circumbinary planet is outside the binary pair");
            var binaryPoint=galaxy.Current[bary].position+Double3.Rotate(a.plane,new Double3(0,separation*1.5,0));
            Double3 direct=new Double3();for(int i=0;i<galaxy.Nodes.Length;i++){var node=galaxy.Nodes[i];if(node.mu<=0)continue;var d=galaxy.Current[i].position-binaryPoint;double r=Math.Max(node.radius,d.Magnitude);direct+=d*(node.mu/(r*r*r));}
            var summed=galaxy.Acceleration(binaryPoint,galaxy.Current);
            Check((direct-summed).Magnitude/Math.Max(1,direct.Magnitude)<.0001,"Gravity includes both stars, every local planet, black hole and distant system monopoles");
            var p=binaryPoint-galaxy.Current[home].position;var relative=galaxy.RelativeAcceleration(p,home,0);
            Check((relative-(summed-galaxy.Current[home].acceleration)).Magnitude<1e-7,"Accelerating planet frame subtracts its analytical frame acceleration");
            Double3 velocity=Double3.Rotate(a.plane,new Double3(100,20,80))-galaxy.Current[home].velocity+galaxy.Current[bary].velocity;
            var coarseP=p;var coarseV=velocity;var fineP=p;var fineV=velocity;
            for(int i=0;i<400;i++)galaxy.Integrate(ref coarseP,ref coarseV,home,i*.05,.05,new Double3());
            for(int i=0;i<4000;i++)galaxy.Integrate(ref fineP,ref fineV,home,i*.005,.005,new Double3());
            report.integrationDifference=(coarseP-fineP).Magnitude;Check(report.integrationDifference<.02,"Multi-body Verlet integration at the game's maximum 50 ms step agrees with a 5 ms reference ("+report.integrationDifference.ToString("F6")+" m)");
            world.SetShipControl(true);var ship=world.ShipForValidation;var pose=ship.Pose;
            pose.position=p;pose.linearVelocity=velocity;pose.rotation=Quaternion.LookRotation((galaxy.Current[bary].position-binaryPoint).Float.normalized,a.plane*Vector3.forward);ship.ResetForSystemValidation(pose);
            for(int i=0;i<600;i++){yield return null;var prediction=ship.PredictionForValidation;if(prediction!=null&&prediction.Count>2)break;}
            var path=ship.PredictionForValidation;Check(path!=null&&path.Count>2,"Incremental predictor publishes a numerical multi-body path");
            report.predictionMaxSliceMs=path.MaxSliceMs;report.predictionStatus=path.Status;report.primaryShare=path.PrimaryShare;
            Check(galaxy.Nodes[path.Primary].kind==CelestialKind.Star&&galaxy.Nodes[path.Secondary].kind==CelestialKind.Star,"Binary forecast identifies both stellar influences");
            Check(path.FrameNode==bary,"Binary trajectory uses the shared barycentre as its moving display frame");
            Check(!path.Status.StartsWith("PREDICTED BOUND"),"Comparable binary influences never receive a two-body stable-orbit claim");
            Check(path.Horizon>0&&path.Horizon<=galaxy.Settings.predictionHorizon,"Forecast explicitly bounds its prediction window");
            double arcDeadline=Time.realtimeSinceStartupAsDouble+5;while(ship.TrajectoryPointsForValidation<2&&Time.realtimeSinceStartupAsDouble<arcDeadline)yield return null;
            Check(ship.TrajectoryPointsForValidation>1,"The numerical prediction feeds the 3D HUD arc and route markers");
            var frame=ship.TrajectoryFrameForValidation;var oldCenter=galaxy.Current[frame].position;var oldAnchor=ship.TrajectoryAnchorForValidation;
            world.Paused=false;for(int i=0;i<8;i++)yield return null;world.Paused=true;
            var frameTravel=galaxy.Current[frame].position-oldCenter;var arcTravel=ship.TrajectoryAnchorForValidation.RelativeTo(oldAnchor);
            Check((arcTravel-frameTravel).Magnitude<1000,"3D trajectory remains attached to its moving reference frame between forecast uploads");
            Check(world.View.transform.position.magnitude<1e-6,"Moving-body flight keeps the camera at render origin");
            hudTarget=new RenderTexture(1440,900,0,RenderTextureFormat.ARGB32);hudTarget.Create();ship.SetPilotRenderTarget(hudTarget);captureHud=true;for(int i=0;i<8;i++)yield return null;Capture("binary-stars",false);ship.SetPilotRenderTarget(null);captureHud=false;
            var sky=runtime.SkyForValidation;int refreshes=sky.RefreshCount;Check(sky.StarCount==report.stars+1,"Sky renderer represents the generated stars and black hole exactly once");
            var backdrop=sky.BackdropForValidation;
            Check(backdrop.StarCount==30000,"Thirty thousand decorative stars do not expand the gravity or travel catalogue");
            Check(backdrop.Ready&&backdrop.BakeCount>0,"Progressive volumetric nebula cache completes successfully");
            Check(backdrop.TextureBytes==2097152,"Two nebula caches use a bounded 2 MiB of GPU texels");
            galaxy.Settings.backgroundStars=0;galaxy.Settings.nebulaBrightness=0;yield return null;Capture("sky-control",false);
            galaxy.Settings.backgroundStars=30000;yield return null;Capture("sky-stars",false);
            galaxy.Settings.nebulaBrightness=.8f;yield return null;Capture("sky-nebula",false);
            Check(PixelDifference("sky-control","sky-stars")>500,"Additional stars produce visible pixels in the actual HDRP camera render");
            Check(PixelDifference("sky-stars","sky-nebula")>5000,"Cached nebula clouds produce an extended visible sky overlay");
            galaxy.Settings.backgroundStars=100000;galaxy.Settings.nebulaBrightness=0;yield return null;Capture("sky-100000-stars",false);
            Check(backdrop.StarCount==100000&&PixelDifference("sky-stars","sky-100000-stars")>1000,"The 100,000-star setting produces additional rendered stars without expanding the celestial catalogue");
            galaxy.Settings.backgroundStars=30000;galaxy.Settings.nebulaBrightness=.8f;yield return null;
            int bakes=backdrop.BakeCount;
            for(int i=0;i<30;i++)yield return null;
            Check(backdrop.BakeCount==bakes,"Stationary observers reuse the volumetric nebula cache");
            Check(!backdrop.NeedsBake(world.ObserverPosition.Add(new Double3(1e9,0,0)),Time.realtimeSinceStartupAsDouble),"Nebula travel refresh obeys its minimum interval");
            Check(backdrop.NeedsBake(world.ObserverPosition.Add(new Double3(1e9,0,0)),Time.realtimeSinceStartupAsDouble+61),"Substantial travel schedules a new galaxy-space cloud integration");
            Check(sky.RefreshCount==refreshes,"A stationary observer does not rebuild the distant star catalogue every frame");
            Check(!sky.NeedsRefresh(world.ObserverPosition.Add(new Double3(1e8,0,0)),Time.realtimeSinceStartupAsDouble),"Large travel still obeys the sky refresh minimum interval");
            Check(sky.NeedsRefresh(world.ObserverPosition.Add(new Double3(1e8,0,0)),Time.realtimeSinceStartupAsDouble+61),"Travel and time trigger a distant sky refresh");
            Check(runtime.DistantTerrain.Count<=galaxy.Settings.terrainPreviewBudget,"Distant terrain cache stays inside its configured texture budget");
            // Switch to a planet in another star system and retain the original army, inventory and canonical ship state.
            int destination=galaxy.Settings.planetsPerSystem;runtime.PreparePlanet(destination);
            double deadline=Time.realtimeSinceStartupAsDouble+90;
            while(!runtime.IsPreparedForValidation(destination)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Check(runtime.IsPreparedForValidation(destination),"A planet in another star system prepares incrementally");
            var dest=runtime.Bodies[destination];pose.position=dest.position.Add(new Double3(dest.radius+30000,0,0)).RelativeTo(world.definition.position);pose.linearVelocity=dest.velocity-world.definition.velocity;ship.ResetForSystemValidation(pose);
            var position=world.definition.position.Add(ship.Pose.position);var inertial=world.definition.velocity+ship.Pose.linearVelocity;uint tick=world.SimulationTicks;
            runtime.EnterPlanet(destination);var next=runtime.ActiveWorld;next.Paused=true;
            Check(next.definition.bodyId==dest.bodyId,"Seamless handoff can enter a different star system");
            Check(position.RelativeTo(next.definition.position.Add(next.ShipForValidation.Pose.position)).Magnitude<1e-5,"Interstellar handoff preserves canonical position");
            Check((inertial-next.definition.velocity-next.ShipForValidation.Pose.linearVelocity).Magnitude<1e-7,"Interstellar handoff preserves inertial velocity");
            for(int i=0;i<8;i++)yield return null;
            Check(world.SimulationTicks==tick,"The departed star system stops all army simulation");
            Check(next.View.transform.position.magnitude<1e-6,"Camera remains at render origin after interstellar handoff");
            Capture("new-system",true);
            if(PlanetOrbitalStress.Enabled){
                var asset=runtime.Civilisation.State.orbitals[0];var station=world.Orbitals.Find(asset.id);
                Check(world.Orbitals.ViewCount==32&&!station.Root.activeInHierarchy,"Departed orbital views and collision are inactive in the distant system");
                var orbit=asset.Pose(runtime.Civilisation.RenderTime);var back=next.ShipForValidation.Pose;back.position=world.definition.position.Add(orbit.position+Double3.Rotate(orbit.rotation,new Double3(0,0,-250))).RelativeTo(next.definition.position);back.linearVelocity=world.definition.velocity+orbit.linearVelocity-next.definition.velocity;back.rotation=orbit.rotation;next.ShipForValidation.ResetForSystemValidation(back);
                var watch=Stopwatch.StartNew();runtime.EnterPlanet(0);watch.Stop();report.orbitalReturnMs=watch.Elapsed.TotalMilliseconds;
                Check(runtime.ActiveWorld==world&&station.Root.activeInHierarchy&&world.Orbitals.Find(asset.id)==station,"Returning reuses the original orbital identities and station views");
                Check((station.Root.transform.position-world.RenderBodyPosition(asset.Pose(runtime.Civilisation.RenderTime).position)).magnitude<.005,"Returned station geometry and collision rebase before the first rendered frame");
                Check(world.View.transform.position==Vector3.zero&&!next.ResidentActive,"Orbital return retains a zero-origin camera and sleeps the departed planet");
            }
            Check(report.runtimeErrors==0,"Galaxy flight, sky, predictions, previews and cross-system activation emit no runtime errors");
        }
        int PixelDifference(string first,string second){
            var a=new Texture2D(2,2);var b=new Texture2D(2,2);a.LoadImage(File.ReadAllBytes(Path.Combine(output,first+".png")));b.LoadImage(File.ReadAllBytes(Path.Combine(output,second+".png")));
            var x=a.GetPixels32();var y=b.GetPixels32();int changed=0;for(int i=0;i<x.Length;i++)if(Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b)>6)changed++;
            Destroy(a);Destroy(b);return changed;
        }
        void Capture(string name,bool any){var active=world.SystemRuntime.ActiveWorld;
            if(target==null){target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGBHalf);target.Create();}
            active.View.enabled=false;active.View.targetTexture=target;RenderPipeline.SubmitRenderRequest(active.View,new RenderPipeline.StandardRequest{destination=target});
            if(captureHud){hudComposite=new RenderTexture(1440,900,0,RenderTextureFormat.ARGB32);hudComposite.Create();hudMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUiComposite"));hudMaterial.SetTexture("_Ui",hudTarget);Graphics.Blit(target,hudComposite,hudMaterial);}
            var previous=RenderTexture.active;RenderTexture.active=captureHud?hudComposite:target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());int lit=0;foreach(var c in image.GetPixels32())if(c.r>80||c.g>80||c.b>80)lit++;Destroy(image);RenderTexture.active=previous;Check(lit>100,name+" has visible celestial/scene pixels ("+lit+")");
        }
    }
}
