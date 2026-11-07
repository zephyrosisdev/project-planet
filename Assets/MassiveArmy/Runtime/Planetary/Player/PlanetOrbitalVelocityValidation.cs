#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Object=UnityEngine.Object;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        // Exercise the production integrator without allocating interiors, UI or fleets.
        PlanetSpacecraft(PlanetDevelopment owner,PlanetSurface terrain,SpacePose initial){world=owner;surface=terrain;frames=new ReferenceFrameManager();pose=initial;shipFrame=new ReferenceFrame(1,0,FrameKind.Ship,pose);frames.Register(shipFrame);Landed=false;GearDeployed=false;FlightAssistEnabled=false;}
        internal static void ValidateOrbitalVelocityAssist(PlanetDevelopment owner,Action<bool,string> check){
            var terrain=new PlanetSurface(owner.definition);double mu=owner.definition.gravitationalParameter,r=owner.definition.radius+4000,circular=Math.Sqrt(mu/r);
            var state=new SpacePose{position=new Double3(0,r,0),rotation=Quaternion.identity,linearVelocity=new Double3(circular*1.4,30,0)};
            var ship=new PlanetSpacecraft(owner,terrain,state);ship.SetFlightAssistance(true);
            ship.Integrate(.05f,Vector3.zero,Vector3.zero,matchOrbit:true);
            check(ship.OrbitalVelocityAssistActive&&!ship.FlightAssistEnabled,"Holding orbital velocity assist selects physical coasting flight instead of hover assist");
            check((ship.pose.linearVelocity-state.linearVelocity).Magnitude<=OrbitalVelocityAssistAcceleration*.05+mu/(r*r)*.05+.001,"Velocity matching uses bounded thrust rather than an instantaneous speed set");
            check(ship.OrbitalVelocityTargetSpeed>circular-.001&&ship.OrbitalVelocityTargetSpeed<circular+.001,"Circular target uses centre distance and gravitational parameter at the current altitude");
            var released=ship.pose;ship.Integrate(.05f,Vector3.zero,Vector3.zero);
            var control=new PlanetSpacecraft(owner,terrain,released);control.Integrate(.05f,Vector3.zero,Vector3.zero);
            check(!ship.OrbitalVelocityAssistActive&&(ship.pose.linearVelocity-control.pose.linearVelocity).Magnitude<1e-10&&(ship.pose.position-control.pose.position).Magnitude<1e-10,"Releasing N immediately gives exactly the ordinary gravity/coasting result without further matching");
            ship.ResetFlightState(state);for(int i=0;i<600;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero,matchOrbit:true);
            var orbit=PlanetOrbit.Calculate(ship.pose.position,ship.pose.linearVelocity,mu);
            check(orbit.Valid&&orbit.Bound&&orbit.Eccentricity<.005,"Sustained holding converges from excess tangential speed and radial motion to a nearly circular bound orbit");
            double releaseRadius=ship.pose.position.Magnitude;for(int i=0;i<1200;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero);
            check(!ship.OrbitalVelocityAssistActive&&Math.Abs(ship.pose.position.Magnitude-releaseRadius)<releaseRadius*.01,"A matched orbit continues coasting after release without hover control or repeated correction");
            state.linearVelocity=new Double3(-circular*.5,0,0);ship.ResetFlightState(state);ship.Integrate(.05f,Vector3.zero,Vector3.zero,matchOrbit:true);
            check(ship.pose.linearVelocity.x<state.linearVelocity.x,"Retrograde tangential direction is preserved rather than reversed");
            state.linearVelocity=new Double3(0,20,0);ship.ResetFlightState(state);ship.Integrate(.05f,Vector3.zero,Vector3.zero,matchOrbit:true);
            check(ship.pose.linearVelocity.z>0&&ship.pose.linearVelocity.y<20,"Radial-only flight uses the projected hull heading and reduces radial drift");
            ship.ResetFlightState(state);ship.Integrate(.05f,Vector3.zero,Vector3.zero,brake:true,matchOrbit:true);
            check(!ship.OrbitalVelocityAssistActive&&ship.OrbitalVelocityAssistStatus.Contains("brake"),"Braking takes precedence over held orbital matching");
            state.position=new Double3(0,owner.definition.radius+50,0);ship.ResetFlightState(state);ship.SetFlightAssistance(true);ship.Integrate(.01f,Vector3.zero,Vector3.zero,matchOrbit:true);
            check(!ship.OrbitalVelocityAssistActive&&ship.FlightAssistEnabled&&ship.OrbitalVelocityAssistStatus.Contains("Climb"),"Matching below the atmosphere / terrain safety boundary reports a climb requirement and preserves hover assist");
            ship.Landed=true;ship.Integrate(.01f,Vector3.zero,Vector3.zero,matchOrbit:true);check(!ship.OrbitalVelocityAssistActive&&ship.Landed,"Holding N cannot launch a landed ship");
            state.position=new Double3(0,r,0);state.linearVelocity=new Double3(circular,0,0);ship.ResetFlightState(state);
            ship.ToggleOrbitalVelocityHold();check(ship.OrbitalVelocityHoldEnabled,"Pressing N latches orbit hold without requiring a held key");
            ship.pose.position+=new Double3(0,20,0);ship.pose.linearVelocity+=new Double3(0,1,0);
            for(int i=0;i<2400;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero);
            check(ship.OrbitalVelocityHoldEnabled&&Math.Abs(ship.pose.position.Magnitude-r)<.05,"Orbit hold returns to its captured radius after an outward position / velocity disturbance");
            ship.pose.position-=ship.pose.position*(20/ship.pose.position.Magnitude);
            for(int i=0;i<2400;i++)ship.Integrate(.05f,Vector3.zero,Vector3.zero,boost:true);
            check(Math.Abs(ship.pose.position.Magnitude-r)<.05,"Boosted orbit hold also recovers an inward disturbance without persistent falling");
            ship.ToggleOrbitalVelocityHold();var off=ship.pose;control=new PlanetSpacecraft(owner,terrain,off);ship.Integrate(.05f,Vector3.zero,Vector3.zero);control.Integrate(.05f,Vector3.zero,Vector3.zero);
            check(!ship.OrbitalVelocityHoldEnabled&&(ship.pose.position-control.pose.position).Magnitude<1e-10&&(ship.pose.linearVelocity-control.pose.linearVelocity).Magnitude<1e-10,"Pressing N again disables the controller and resumes exactly ordinary coasting");
            ship.ToggleOrbitalVelocityHold();ship.Integrate(.05f,Vector3.forward,Vector3.zero);check(!ship.OrbitalVelocityHoldEnabled,"Manual translational thrust cancels orbit hold so it does not fight the pilot");
            ship.ResetFlightState(state);double min=r,max=r,maxRadial=0;int steps=(int)Math.Ceiling(2*Math.PI*Math.Sqrt(r*r*r/mu)/.05);
            for(int i=0;i<steps;i++){ship.Integrate(.05f,Vector3.zero,Vector3.zero);double length=ship.pose.position.Magnitude;min=Math.Min(min,length);max=Math.Max(max,length);maxRadial=Math.Max(maxRadial,Math.Abs(CruiseDot(ship.pose.position,ship.pose.linearVelocity)/length));}
            check(max-min<.01&&maxRadial<.001,"A complete unpowered circular orbit has under 1 cm radial variation and under 0.001 m/s radial drift in the central-gravity integrator");
            double oldMin=r,oldMax=r;var oldP=state.position;var oldV=state.linearVelocity;
            for(int i=0;i<steps;i++){double length=oldP.Magnitude;oldV+=new Double3(-oldP.Float.normalized*(float)(mu/(length*length)))*.05;oldP+=oldV*.05;oldMin=Math.Min(oldMin,oldP.Magnitude);oldMax=Math.Max(oldMax,oldP.Magnitude);}
            check(max-min<(oldMax-oldMin)*.01,"The same full-period fixture reduces radial oscillation by over 99% against the frozen former Euler integration");
            Debug.Log($"Orbit drift comparison: previous range {oldMax-oldMin:F6} m; current range {max-min:F6} m; current peak radial {maxRadial:F8} m/s");
            ValidateOrbitHoldPrediction(owner,check);
        }
        static void ValidateOrbitHoldPrediction(PlanetDevelopment owner,Action<bool,string> check){
            var settings=ScriptableObject.CreateInstance<GalaxyDefinition>();settings.starSystems=2;settings.planetsPerSystem=1;settings.predictionHorizon=60;
            var local=ScriptableObject.CreateInstance<PlanetSystemDefinition>();var home=Object.Instantiate(owner.definition);home.bodyId=100;home.radius=120000;home.terrainHeight=0;home.gravitationalParameter=9.81*home.radius*home.radius;
            PlanetDefinition[] bodies=null;
            try{
                var model=new GalaxyModel(settings,local,home,out bodies);int node=model.NodeFor(home);double radius=home.radius+20000,mu=home.gravitationalParameter;var state=new SpacePose{position=new Double3(0,radius,0),rotation=Quaternion.identity,linearVelocity=new Double3(Math.Sqrt(mu/radius),0,0)};
                var forecast=new GalaxyPrediction(model);var plan=new OrbitHoldForecast{enabled=true,node=node,radius=radius,acceleration=40,heading=Vector3.right,right=Vector3.forward,planeNormal=new Double3(0,0,-1)};
                for(int i=0;i<20000&&forecast.Count==0;i++)forecast.Tick(home,state,false,plan);
                check(forecast.Count==GalaxyPrediction.Samples&&!forecast.Impact&&forecast.MaintainedOrbit,"The production multi-body forecast models powered orbit hold and publishes all 160 samples without a false impact");
                check(forecast.Status.Contains("PARTIAL")&&!forecast.FullOrbitWindow,"A deliberately short forecast cap explicitly labels a partial orbit rather than claiming a complete circle");
                settings.predictionHorizon=1800;forecast.Invalidate();for(int i=0;i<20000&&forecast.Count==0;i++)forecast.Tick(home,state,false,plan);
                check(forecast.FullOrbitWindow&&!forecast.Impact,"The normal forecast covers one complete maintained orbit when within its bounded horizon");
                var first=forecast.Positions[0]-forecast.PrimaryPositions[0];var last=forecast.Positions[forecast.Count-1]-forecast.PrimaryPositions[forecast.Count-1];
                Debug.Log($"Powered forecast radius {radius:F3}, first {first.Magnitude:F3}, last {last.Magnitude:F3}, closing error {(last-first).Magnitude:F3}, horizon {forecast.Horizon:F3}, primary {model.Nodes[forecast.FrameNode].name}");check((last-first).Magnitude<radius*.01,"The displayed powered trajectory returns around the planet to within one percent of its start radius");
                var systemHost=new GameObject("Inactive multi-body orbit hold fixture");systemHost.SetActive(false);var runtime=systemHost.AddComponent<PlanetSystemRuntime>();var previousBody=owner.definition;var previousSystem=owner.SystemRuntime;
                try{
                    typeof(PlanetSystemRuntime).GetProperty("Galaxy").SetValue(runtime,model);typeof(PlanetSystemRuntime).GetProperty("Bodies").SetValue(runtime,bodies);owner.SystemRuntime=runtime;owner.definition=home;
                    var actual=new PlanetSpacecraft(owner,new PlanetSurface(home),state);actual.ToggleOrbitalVelocityHold();double minRadius=radius,maxRadius=radius,maxRadial=0;int count=(int)Math.Ceiling(2*Math.PI*Math.Sqrt(radius*radius*radius/mu)/.05);
                    for(int i=0;i<count;i++){model.Publish((i+1)*.05,bodies);actual.Integrate(.05f,Vector3.zero,Vector3.zero);double length=actual.pose.position.Magnitude;minRadius=Math.Min(minRadius,length);maxRadius=Math.Max(maxRadius,length);maxRadial=Math.Max(maxRadial,Math.Abs(CruiseDot(actual.pose.position,actual.pose.linearVelocity)/length));}
                    check(actual.OrbitalVelocityHoldEnabled&&maxRadius-minRadius<.2&&maxRadial<.02,"The actual ship holds radius within 20 cm and radial speed within 0.02 m/s for a complete moving-body orbit with the key released");
                    Debug.Log($"Multi-body orbit hold: range {maxRadius-minRadius:F6} m; peak radial {maxRadial:F8} m/s");
                }finally{owner.definition=previousBody;owner.SystemRuntime=previousSystem;typeof(PlanetSystemRuntime).GetProperty("Bodies").SetValue(runtime,null);Object.DestroyImmediate(systemHost);model.Publish(0,bodies);}
                // A maximum-height bound above the vessel must not turn empty air into ground.
                home.terrainHeight=100000;home.stampTerrain=false;state.position=new Double3(0,home.radius+10000,0);state.linearVelocity=new Double3(Math.Sqrt(mu/state.position.Magnitude),0,0);plan.radius=state.position.Magnitude;
                var terrain=new PlanetSurface(home);var up=Vector3.up;double sampled=terrain.SampleHeight(up);home.oceanHeight=-100000;
                state.position=new Double3(up)*(home.radius+Math.Max(10000,sampled+100));state.linearVelocity=new Double3(Math.Sqrt(mu/state.position.Magnitude),0,0);plan.radius=state.position.Magnitude;
                forecast.Invalidate();for(int i=0;i<20000&&forecast.Count==0;i++)forecast.Tick(home,state,false,plan);
                check(forecast.Count>1,"Terrain-envelope forecast resolves actual sampled ground before declaring an impact");
            }finally{if(bodies!=null)foreach(var body in bodies)if(body!=null)Object.DestroyImmediate(body);if(home!=null)Object.DestroyImmediate(home);Object.DestroyImmediate(settings);Object.DestroyImmediate(local);}
        }
    }
    public static class PlanetOrbitalVelocityValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        #if UNITY_EDITOR
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){const string path="TestResults/Planetary/OrbitVelocity/request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(path))return;File.Delete(path);Run();}
        [MenuItem("Synthetic Front/Diagnostics/Validate orbital velocity and toggle hold")]
        public static void Run()=>RunTo("TestResults/Planetary/OrbitVelocity");
        #endif
        public static bool RunTo(string output){var report=new Report();GameObject host=null;PlanetDefinition body=null;try{host=new GameObject("Inactive orbital velocity integrator fixture");host.SetActive(false);var world=host.AddComponent<PlanetDevelopment>();body=ScriptableObject.CreateInstance<PlanetDefinition>();body.radius=10000;body.terrainHeight=0;body.oceanHeight=-4;body.hasAtmosphere=true;body.atmosphereHeight=500;body.gravitationalParameter=9.81*body.radius*body.radius;world.definition=body;PlanetSpacecraft.ValidateOrbitalVelocityAssist(world,(valid,label)=>{if(!valid)throw new InvalidOperationException(label);report.checks.Add(label);});}catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{if(host!=null)Object.DestroyImmediate(host);if(body!=null)Object.DestroyImmediate(body);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Debug.Log("Orbital velocity assist validation "+report.status+": "+report.checks.Count+" checks");}return report.status=="PASS";}
    }
}
#endif
