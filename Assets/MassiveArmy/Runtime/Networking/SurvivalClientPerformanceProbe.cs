#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        bool performanceStarted,performanceMeasuring;double performanceAt,performancePrevious;RenderTexture performanceTarget;
        static readonly bool performanceRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-performance")>=0;
        bool PerformanceRequested=>performanceRequested;
        static int PerformanceInterval(string name,int fallback,int minimum,int maximum)=>int.TryParse(Argument(name),out int value)?Math.Max(minimum,Math.Min(maximum,value)):fallback;
        readonly int performanceWarmupSeconds=PerformanceInterval("-survival-probe-performance-warmup",10,2,30),performanceSampleSeconds=PerformanceInterval("-survival-probe-performance-seconds",30,5,120);
        static readonly bool testClockRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-test-clock")>=0;
        int testClockPhase;bool testClockSubscribed;SurvivalAction testClockAction;SurvivalActionReply testClockReply;long testClockTick;double testClockSeconds;
        string TestClockDone=>Path.Combine(Path.GetDirectoryName(path),"test-clock-done.ack");
        void TestClockSend(int speed){testClockReply=null;testClockAction=new SurvivalAction{kind="test-time-scale",count=speed};session.Submit(testClockAction);}
        bool UpdateTestClock(){
            if(!testClockRequested)return true;if(!session.PrivateStateReady||!report.remotePlayerSeen)return false;
            if(Path.GetFileName(path)!="client-1.json")return File.Exists(TestClockDone);
            if(testClockPhase==0&&Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-pilot-weather")>=0&&session.Latest.vitals.controlledShip==0)return false;
            if(!testClockSubscribed){session.ActionReplied+=reply=>{if(reply.sequence==testClockAction?.sequence)testClockReply=reply;};testClockSubscribed=true;}
            if(testClockPhase==0){TestClockSend(session.Config.allowTestTimeControls?16:2);testClockPhase=1;return false;}
            if(testClockPhase==1){if(testClockReply==null)return false;if(testClockReply.accepted){_ = Finish(false,"Server accepted an invalid/disabled test clock command");return false;}report.testClockRejected=true;if(!session.Config.allowTestTimeControls){File.WriteAllText(TestClockDone,"disabled control rejected");testClockPhase=4;return true;}TestClockSend(8);testClockPhase=2;return false;}
            if(testClockPhase==2){if(session.Latest.testTimeScale!=8)return false;if(testClockTick==0){testClockTick=session.Latest.tick;testClockSeconds=session.Latest.simulationSeconds;}if(session.Latest.tick-testClockTick<session.Config.tickRate*3)return false;double expected=(session.Latest.tick-testClockTick)*8.0/session.Config.tickRate;if(Math.Abs(session.Latest.simulationSeconds-testClockSeconds-expected)>.001){_ = Finish(false,"Server test clock did not advance at eight simulation seconds per wall second");return false;}report.testClockAccelerated=true;TestClockSend(1);testClockPhase=3;return false;}
            if(testClockPhase==3){if(session.Latest.testTimeScale!=1)return false;report.testClockRestored=true;File.WriteAllText(TestClockDone,"8x clock and return to 1x verified");testClockPhase=4;}
            return testClockPhase==4;
        }
#if SURVIVAL_GRAPHICS_PROBE
        static readonly bool orbitWeatherRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-orbit-weather")>=0;
        static readonly bool orbitPointNoise=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-point-clouds")>=0;
        static readonly bool pilotWeatherRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-pilot-weather")>=0;
        bool pilotWeatherSent;
        double nextPilotImage;int pilotImage;RenderTexture pilotPreview;
        void CapturePilotSweep(){
            if(!pilotWeatherRequested||!orbitWeatherRequested||Time.realtimeSinceStartupAsDouble<nextPilotImage)return;nextPilotImage=Time.realtimeSinceStartupAsDouble+.25;
            if(pilotPreview==null){pilotPreview=new RenderTexture(640,360,0,RenderTextureFormat.ARGB32);pilotPreview.Create();}
            var old=RenderTexture.active;var shot=new Texture2D(640,360,TextureFormat.RGB24,false);try{Graphics.Blit(performanceTarget,pilotPreview);RenderTexture.active=pilotPreview;shot.ReadPixels(new Rect(0,0,640,360),0,0);shot.Apply();string folder=path+".orbit";Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,(pilotImage++).ToString("D4")+".png"),shot.EncodeToPNG());}finally{RenderTexture.active=old;Destroy(shot);}
        }
        double orbitWeatherStart=-1;MassiveArmy.Planetary.PlanetShipSimulationSnapshot orbitSavedView;bool orbitViewLoaded;
        internal void WeatherOrbitView(ref SurvivalMotion motion){
            if((!orbitWeatherRequested&&!pilotWeatherRequested)||session?.ClientWorld?.Ready!=true)return;
            var world=session.ClientWorld;
            if(performanceTarget==null){performanceTarget=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);performanceTarget.Create();}
            world.View.targetTexture=performanceTarget;world.View.pixelRect=new Rect(0,0,2560,1440);world.View.enabled=false;
            if(pilotWeatherRequested){
                world.WeatherForValidation.TraceEnabled=true;
                if(!pilotWeatherSent&&session.Latest?.motion?.interior==true&&session.Latest.motion.ship?.owner==session.ActorId){pilotWeatherSent=true;session.Submit(new SurvivalAction{kind="enter-pilot",entity=session.Latest.motion.ship.entity});}
                if(!orbitWeatherRequested||session.Latest?.vitals?.controlledShip==0)return;
            }
            if(!orbitViewLoaded){orbitViewLoaded=true;string viewPath=Argument("-survival-probe-orbit-view");if(!string.IsNullOrEmpty(viewPath))orbitSavedView=JsonUtility.FromJson<MassiveArmy.Planetary.PlanetShipSimulationSnapshot>(File.ReadAllText(viewPath));}
            if(orbitWeatherStart<0)orbitWeatherStart=Time.realtimeSinceStartupAsDouble;
            double angle=(Time.realtimeSinceStartupAsDouble-orbitWeatherStart)*.2,radius=world.definition.radius*1.1;
            var up=new Vector3((float)Math.Sin(angle),(float)Math.Cos(angle),0);var forward=new Vector3((float)Math.Cos(angle),-(float)Math.Sin(angle),0);var q=Quaternion.LookRotation(forward,up)*Quaternion.Euler(45,0,0);
            var point=new MassiveArmy.Planetary.Double3(up)*radius;
            if(orbitSavedView!=null){point=MassiveArmy.Planetary.Double3.Rotate(Quaternion.AngleAxis((float)(angle*Mathf.Rad2Deg),orbitSavedView.lockNormal),orbitSavedView.pose.position);up=point.Float.normalized;forward=Vector3.Cross(orbitSavedView.lockNormal,up).normalized;q=(Quaternion.LookRotation(forward,up)*orbitSavedView.lockOffset).normalized;}
            motion=motion.Copy();motion.interior=false;motion.vehicle=null;motion.ship=pilotWeatherRequested?motion.ship.Copy():new MassiveArmy.Industrial.SurvivalShipState();motion.ship.pose=new MassiveArmy.Industrial.FramePose{body=motion.pose.body,x=point.x,y=point.y,z=point.z};motion.ship.rotationX=q.x;motion.ship.rotationY=q.y;motion.ship.rotationZ=q.z;motion.ship.rotationW=q.w;motion.pose=motion.ship.pose;motion.pitch=motion.viewYaw=0;
            world.WeatherForValidation.TraceEnabled=true;world.WeatherForValidation.DiagnosticStage=0;world.WeatherForValidation.DiagnosticPointNoise=orbitPointNoise;
        }
#endif
        void UpdatePerformanceProbe(){
            if(!UpdateTestClock())return;
            if(!performanceStarted){if(!session.PrivateStateReady||!report.remotePlayerSeen)return;performanceStarted=true;performanceAt=Time.realtimeSinceStartupAsDouble;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
            if(!performanceMeasuring&&Time.realtimeSinceStartupAsDouble-performanceAt>=performanceWarmupSeconds){performanceMeasuring=true;performanceAt=Time.realtimeSinceStartupAsDouble;performancePrevious=performanceAt;session.ResetClientDiagnostics();}
            if(performanceMeasuring&&phase!=3&&phase!=4&&Time.realtimeSinceStartupAsDouble-performanceAt>=performanceSampleSeconds){
                File.WriteAllText(path+".performance.json",JsonUtility.ToJson(session.ClientDiagnosticReport(),true));
#if SURVIVAL_GRAPHICS_PROBE
                if(pilotWeatherRequested&&performanceTarget!=null){var old=RenderTexture.active;var shot=new Texture2D(performanceTarget.width,performanceTarget.height,TextureFormat.RGB24,false);try{RenderTexture.active=performanceTarget;shot.ReadPixels(new Rect(0,0,shot.width,shot.height),0,0);shot.Apply();File.WriteAllBytes(path+".pilot.png",shot.EncodeToPNG());}finally{RenderTexture.active=old;Destroy(shot);}}
                if(orbitWeatherRequested||pilotWeatherRequested)File.WriteAllText(path+".weather.json",session.ClientWorld.WeatherForValidation.TraceJson());
#endif
                lastPose=session.Latest.self.pose;phase=3;_ = Reconnect();
            }
            if(phase==4&&session.PrivateStateReady){if(session.Latest.self.id!=report.actor){_ = Finish(false,"Performance reconnect changed identity");return;}report.reconnected=true;_ = Finish(true,null);}
        }
        void LateUpdate(){
#if SURVIVAL_GRAPHICS_PROBE
            // Native rendering is required here: offscreen HDRP render requests
            // can retain the window's render area and create a separate cutoff.
            if(orbitWeatherRequested||pilotWeatherRequested){if(performanceStarted&&performanceTarget!=null&&session?.ClientWorld?.View!=null){RenderPipeline.SubmitRenderRequest(session.ClientWorld.View,new RenderPipeline.StandardRequest{destination=performanceTarget});var fence=AsyncGPUReadback.Request(performanceTarget,0,0,1,0,1,0,1);fence.WaitForCompletion();CapturePilotSweep();if(performanceMeasuring&&phase!=3&&phase!=4){double now=Time.realtimeSinceStartupAsDouble;session.RecordClientFrame(now-performancePrevious);performancePrevious=now;}}return;}
#endif
            if(finishing||!PerformanceRequested||report.headlessClient||report.dedicated||!performanceStarted||session?.ClientWorld?.Ready!=true)return;
            var camera=session.ClientWorld.View;
            if(performanceTarget==null){performanceTarget=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);performanceTarget.Create();}
            var old=camera.targetTexture;bool enabled=camera.enabled;
            try{camera.enabled=false;camera.targetTexture=performanceTarget;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=performanceTarget});var fence=AsyncGPUReadback.Request(performanceTarget,0,0,1,0,1,0,1);fence.WaitForCompletion();}
            finally{camera.targetTexture=old;camera.enabled=enabled;}
            if(performanceMeasuring&&phase!=3&&phase!=4){double now=Time.realtimeSinceStartupAsDouble;session.RecordClientFrame(now-performancePrevious);performancePrevious=now;}
        }
        void DisposePerformanceProbe(){
#if SURVIVAL_GRAPHICS_PROBE
            if(pilotPreview!=null){pilotPreview.Release();Destroy(pilotPreview);pilotPreview=null;}
#endif
            if(performanceTarget!=null){if(session?.ClientWorld?.View!=null&&session.ClientWorld.View.targetTexture==performanceTarget)session.ClientWorld.View.targetTexture=null;performanceTarget.Release();Destroy(performanceTarget);performanceTarget=null;}}
    }
}
#endif
