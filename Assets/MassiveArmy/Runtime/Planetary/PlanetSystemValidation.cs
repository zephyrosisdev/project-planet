using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-225)]public sealed class PlanetSystemValidation:MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public double firstVisitMs,returnVisitMs,longestPreparationStepMs;public long initialAllocatedBytes,visitedAllocatedBytes,visitedReservedBytes;public double[] preparationSteps;public List<string> checks=new List<string>();}
        readonly Report report=new Report();int runtimeErrors;PlanetDevelopment home;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-system-validation")>=0)new GameObject("Planet system validation").AddComponent<PlanetSystemValidation>();}
        void OnLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)runtimeErrors++;}
        void OnDestroy(){Application.logMessageReceived-=OnLog;}
        void Awake(){Application.logMessageReceived+=OnLog;var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);home=FindAnyObjectByType<PlanetDevelopment>();home.ambientZombies=32;home.requestedCapacity=4096;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);}
        IEnumerator Start(){var run=Run();while(true){object current;try{if(!run.MoveNext())break;current=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);break;}yield return current;}File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void Capture(PlanetDevelopment world,string name){
            var camera=world.View;bool enabled=camera.enabled;var previous=camera.targetTexture;var target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf);target.Create();camera.enabled=false;camera.targetTexture=target;
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();int visible=0;var pixels=image.GetPixels32();for(int y=250;y<750;y++)for(int x=400;x<1200;x++){var c=pixels[x+y*1600];if(c.r+c.g+c.b>30)visible++;}Check(visible>2000,name+" shows visible planetary terrain in the central flight view ("+visible+" pixels)");File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());RenderTexture.active=old;Destroy(image);camera.targetTexture=previous;camera.enabled=enabled;target.Release();Destroy(target);
        }
        IEnumerator Run(){
            while(!home.Ready||home.ShipForValidation==null)yield return null;
            report.initialAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var system=home.SystemRuntime;Check(system!=null&&system.Bodies.Length==4,"Default session generates four planets");
            Check(system.ResidentWorldCount==1,"Only the home planet allocates a local simulation at startup");
            var repeat=system.Settings.Generate(home.definition);
            for(int i=1;i<repeat.Length;i++){
                var body=system.Bodies[i];Check(body.terrainSeed==repeat[i].terrainSeed&&body.radius==repeat[i].radius&&body.position.RelativeTo(repeat[i].position).Magnitude==0,"Deterministic body "+i);
                Check(body.edits==null&&body.stampField==null,"Unvisited body "+i+" has no terrain/edit cache");
                for(int j=0;j<i;j++)Check(body.position.RelativeTo(system.Bodies[j].position).Magnitude>body.radius+system.Bodies[j].radius+2*system.Settings.activationAltitude,"Planet activation regions do not intersect "+i+"/"+j);
                Destroy(repeat[i]);
            }
            home.PlayerForValidation.Inventory.Add("transit-test",3);home.PlayerForValidation.Environment.Health=72;home.PlayerForValidation.Environment.Oxygen=81;
            var settings=system.Settings;var boundary=home.definition.position.Add(new Double3(home.definition.radius+settings.activationAltitude+settings.sleepHysteresis*.5,0,0));
            Check(settings.ShouldSimulate(home.definition,boundary,true)&&!settings.ShouldSimulate(home.definition,boundary,false),"Distance hysteresis prevents boundary thrashing");
            home.SetShipControl(true);var ship=home.ShipForValidation;var state=ship.Pose;
            var bodyPreview=system.Bodies[1];var preview=system.DistantTerrain.Get(bodyPreview);var expectedHeight=new float[6];float worst=0;
            for(int face=0;face<6;face++){
                const int x=47,y=91;var read=AsyncGPUReadback.Request(preview.Surface,0,x,1,y,1,face,1);read.WaitForCompletion();Check(!read.hasError,"Distant face "+face+" height cache is readable");
                var d=CubeSphere.Direction((CubeFace)face,new Vector2((x-.5f)/PlanetDistantTerrain.Resolution*2-1,(y-.5f)/PlanetDistantTerrain.Resolution*2-1));expectedHeight[face]=PlanetTerrainEdits.BaseHeight(preview.Definition,d);worst=Mathf.Max(worst,Mathf.Abs(read.GetData<Vector4>()[0].w-expectedHeight[face]));
            }
            Check(worst<.2f,"Low-detail planet height is baked from the canonical CPU/GPU terrain field (error "+worst.ToString("F4")+" m)");
            var isolatedA=Instantiate(home.definition);var isolatedB=Instantiate(bodyPreview);isolatedA.bodyId=1001;isolatedB.bodyId=1002;
            var sampleDirection=CubeSphere.Direction((CubeFace)0,new Vector2((47-.5f)/PlanetDistantTerrain.Resolution*2-1,(91-.5f)/PlanetDistantTerrain.Resolution*2-1));
            using(var isolated=new PlanetDistantTerrain()){
                isolatedA.Edits.AddCrater(-sampleDirection,120);isolatedB.Edits.AddCrater(-sampleDirection,120);isolated.Get(isolatedA);isolated.Get(isolatedB);
                isolatedA.Edits.AddCrater(sampleDirection,240);isolatedB.Edits.AddCrater(Vector3.up,240);
                var refreshed=isolated.Get(isolatedA);var read=AsyncGPUReadback.Request(refreshed.Surface,0,47,1,91,1,0,1);read.WaitForCompletion();
                Check(!read.hasError&&Mathf.Abs(read.GetData<Vector4>()[0].w-new PlanetSurface(isolatedA).SampleHeight(sampleDirection))<.2f,"Interleaved terrain edits on separate planets cannot contaminate a preview rebake");
            }
            Destroy(isolatedA);Destroy(isolatedB);
            Check(bodyPreview.edits==null&&bodyPreview.stampField==null&&system.ResidentWorldCount==1,"Visible distant terrain creates only visual metadata, with no dormant simulation allocation");
            var farPose=state;var outward=(-home.LightingForValidation.Sun.transform.forward+bodyPreview.position.RelativeTo(home.definition.position).Float.normalized*.4f).normalized;farPose.position=bodyPreview.position.Add(new Double3(outward)*(bodyPreview.radius+500000)).RelativeTo(home.definition.position);farPose.rotation=Quaternion.LookRotation(-outward,Vector3.up);farPose.linearVelocity=new Double3();home.Paused=true;ship.ResetForSystemValidation(farPose);yield return null;Capture(home,"planet-from-500km");home.Paused=false;
            state.position=new Double3(home.definition.radius+settings.activationAltitude+settings.sleepHysteresis+10000,0,0);state.linearVelocity=new Double3();ship.ResetForSystemValidation(state);
            yield return null;yield return null;uint ticks=home.SimulationTicks;yield return new WaitForSeconds(.3f);
            Check(!home.SimulationActive&&home.SimulationTicks==ticks,"Leaving the simulation interest range freezes army and base ticks");
            state.position=new Double3(home.definition.radius+10000,0,0);ship.ResetForSystemValidation(state);yield return new WaitForSeconds(.3f);
            Check(home.SimulationActive&&home.SimulationTicks>ticks,"Returning to range resumes simulation without catch-up ticks");
            home.definition.Edits.AddCrater(Vector3.down,120);int craters=home.definition.Edits.Count;
            var destination=system.Bodies[1];var universe=destination.position.Add(new Double3(outward)*(destination.radius+50000)+new Double3(.125,.375,.625));
            state.position=universe.RelativeTo(home.definition.position);state.linearVelocity=new Double3(12,3,7);state.rotation=Quaternion.LookRotation(-outward,Vector3.up);ship.ResetForSystemValidation(state);
            var parking=state;parking.position=new Double3(home.definition.radius+10000,0,0);ship.ResetForSystemValidation(parking);
            system.PreparePlanet(1);float deadline=Time.realtimeSinceStartup+30;
            while(system.Bodies[1]==destination){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Planet preparation did not finish");yield return null;}
            report.longestPreparationStepMs=system.LongestPreparationStepMs;report.preparationSteps=system.PreparationStepMaxMs;
            // Restore the exact pose after preparation frames before testing coordinate conversion.
            home.Paused=true;ship.ResetForSystemValidation(state);yield return null;Capture(home,"planet-before-handoff");home.Paused=false;
            var timer=System.Diagnostics.Stopwatch.StartNew();system.EnterPlanet(1);timer.Stop();report.firstVisitMs=timer.Elapsed.TotalMilliseconds;var other=system.ActiveWorld;
            Check(other.PlayerForValidation.Inventory.Items.ContainsKey("transit-test")&&other.PlayerForValidation.Environment.Health==72&&other.PlayerForValidation.Environment.Oxygen==81,"Player inventory, health and oxygen transfer with the ship");
            for(int face=0;face<6;face++){const int x=47,y=91;var d=CubeSphere.Direction((CubeFace)face,new Vector2((x-.5f)/PlanetDistantTerrain.Resolution*2-1,(y-.5f)/PlanetDistantTerrain.Resolution*2-1));Check(Mathf.Abs(PlanetTerrainEdits.BaseHeight(other.definition,d)-expectedHeight[face])<.2f,"Activation preserves distant terrain identity on face "+face);}
            Check(other!=home&&other.Ready&&other.IsPiloting&&system.ResidentWorldCount==2,"Approached planet creates its local RTS world and transfers pilot control");
            Check(other.definition.position.Add(other.ShipForValidation.Pose.position).RelativeTo(universe).Magnitude<.00001,"Ship universe position survives the body-frame change at sub-millimetre precision");
            Check((other.ShipForValidation.Pose.linearVelocity-new Double3(12,3,7)).Magnitude<.00001,"Ship inertial velocity survives the body-frame change");
            Check(other.ShipForValidation.VisualForValidation.transform.TransformPoint(PlanetSpacecraft.PilotEye).magnitude<.02f,"Ship hull and pilot camera agree in the destination render frame immediately after handoff");
            Check(Quaternion.Angle(other.LightingForValidation.Sun.transform.rotation,system.SunRotation)<.001f,"Sun direction remains fixed across planet handoff");
            Check(other.View.transform.position==Vector3.zero&&!home.View.gameObject.activeInHierarchy,"Only the destination camera is active, at world zero");
            other.Paused=true;yield return null;Capture(other,"planet-after-handoff");other.Paused=false;
            ticks=home.SimulationTicks;var record=new UnitState[1];home.UnitsForValidation.GetData(record,0,0,1);var saved=record[0];
            yield return new WaitForSeconds(.5f);home.UnitsForValidation.GetData(record,0,0,1);
            Check(home.SimulationTicks==ticks&&record[0].position==saved.position&&record[0].health==saved.health,"Dormant planet retains GPU unit state with no simulation dispatch");
            Check(home.definition.Edits.Count==craters,"Dormancy retains crater edits");
            var nextState=other.ShipForValidation.Pose;var back=home.definition.position.Add(new Double3(home.definition.radius+50000,.25,.5));nextState.position=back.RelativeTo(other.definition.position);nextState.linearVelocity=new Double3(8,9,10);other.ShipForValidation.ResetForSystemValidation(nextState);
            timer.Restart();system.EnterPlanet(0);timer.Stop();report.returnVisitMs=timer.Elapsed.TotalMilliseconds;Check(system.ActiveWorld==home&&system.ResidentWorldCount==2&&home.definition.Edits.Count==craters,"Return reuses the original army, buildings and edited terrain without regenerating");
            Check(home.definition.position.Add(home.ShipForValidation.Pose.position).RelativeTo(back).Magnitude<.00001&&home.View.transform.position==Vector3.zero,"Return flight preserves precision and the zero-origin camera");
            yield return new WaitForSeconds(.2f);Check(home.SimulationTicks>ticks&&!other.ResidentActive,"Only the returned planet resumes ticking");
            state=home.ShipForValidation.Pose;universe=other.definition.position.Add(new Double3(other.definition.radius+50000,0,0));state.position=universe.RelativeTo(home.definition.position);state.linearVelocity=new Double3();home.ShipForValidation.ResetForSystemValidation(state);
            yield return null;yield return null;Check(system.ActiveWorld==other&&system.ResidentWorldCount==2,"Approaching a visited planet automatically transfers context without a manual travel command");
            for(int i=2;i<system.Bodies.Length;i++){
                var active=system.ActiveWorld;var body=system.Bodies[i];int bodyId=body.bodyId;var arrival=active.ShipForValidation.Pose;
                arrival.position=body.position.Add(new Double3(body.radius+50000,0,0)).RelativeTo(active.definition.position);arrival.linearVelocity=new Double3();active.ShipForValidation.ResetForSystemValidation(arrival);
                deadline=Time.realtimeSinceStartup+30;while(system.ActiveWorld==active){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("Automatic arrival failed for planet "+i);yield return null;}
                Check(system.ActiveWorld.definition.bodyId==bodyId&&system.ActiveWorld.Ready,"Generated planet "+i+" prepares and activates through automatic approach");
            }
            report.longestPreparationStepMs=system.LongestPreparationStepMs;report.preparationSteps=system.PreparationStepMaxMs;
            report.visitedAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();report.visitedReservedBytes=UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            DontDestroyOnLoad(gameObject);home.ReturnToMenu();yield return null;yield return new WaitForSeconds(.3f);
            Check(runtimeErrors==0,"Generation, automatic travel and teardown produce no runtime errors or exceptions");
            Check(FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None).Length==0&&FindAnyObjectByType<PlanetSystemRuntime>()==null,"Returning to the menu releases all visited planet contexts and the system controller");
        }
    }
}
