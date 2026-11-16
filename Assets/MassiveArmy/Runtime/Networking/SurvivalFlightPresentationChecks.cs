#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        static void RunFlightPresentationChecks(Action<bool,string> check){
            bool multiple=DiagnosticMultipleSessions;DiagnosticMultipleSessions=true;var go=new GameObject("Reboard prediction and accelerated flight presentation");var session=go.AddComponent<SurvivalNetworkSession>();var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=120000;definition.terrainHeight=0;definition.oceanHeight=-35;definition.atmosphereHeight=66000;definition.gravitationalParameter=9.81*120000*120000;
            try{
                session.Config=new SurvivalServerConfig{allowTestTimeControls=true,testTimeScale=8};session.welcome=new SurvivalWelcome{actor=1};session.HeadlessProbeClient=true;session.headlessDefinition=definition;
                var layout=ShipModules.Starter(1);layout.id=10;session.clientShipAssemblies[10]=layout;
                var initial=new PlanetShipSimulationSnapshot{pose=new SpacePose{position=new Double3(0,150000,0),rotation=Quaternion.identity},assist=true};
                var ship=new SurvivalShipState{entity=10,owner=1,pilot=1,landed=false,pose=new FramePose{body=1,y=150000},physics=JsonUtility.ToJson(initial)};
                var motion=new SurvivalMotion{actor=1,tick=10,epoch=2,sequence=30,ship=ship,pose=ship.pose,forwardZ=1};
                session.latest=new SurvivalSnapshot{tick=10,motion=motion.Copy(),self=new IndustrialActor{id=1,health=100},vitals=new SurvivalPlayerRecord{actor=1,controlledShip=10}};
                session.PredictMotion(motion,new SurvivalInput{sequence=31,epoch=2,y=1},1f/30);
                check(session.latest.pilotedShip==null&&motion.ship.velocityZ>10&&motion.pose.z>1&&motion.tick==11,"Reboarding predicts full owner ship physics at 8x using its cached layout even when the private layout was absent");
                using(var reference=new PlanetSpacecraft(definition,layout,(a,b,q)=>true)){
                    reference.RestoreSimulation(initial);for(int i=0;i<8;i++)reference.StepSimulation(1f/30,Vector3.forward,Vector3.zero,false,false,false,false,false);
                    check((reference.CaptureSimulation().pose.position-SurvivalVehicleSimulation.Position(motion.ship.pose)).Magnitude<1e-7,"Cached-layout reboard prediction matches eight authoritative fixed flight steps");
                    reference.RestoreSimulation(new PlanetShipSimulationSnapshot{pose=new SpacePose{position=new Double3(0,150000,0),rotation=Quaternion.identity}});reference.ToggleOrbitalVelocityHold();
                    check(reference.OrbitalVelocityHoldEnabled&&PlanetSpacecraft.MinimumOrbitHoldAltitude(definition)==30000,"Stable orbit hold can engage at exactly 30 km inside a taller visual atmosphere");
                    var engaged=reference.CaptureSimulation();for(int i=0;i<2;i++)reference.StepSimulation(1f/30,Vector3.zero,Vector3.zero,false,false,false,false,false);check(reference.OrbitalVelocityHoldEnabled&&!reference.Landed,"A hold engaged from rest at 30 km remains active while acquiring orbital velocity instead of switching off after its first gravity step");reference.RestoreSimulation(engaged);
                    var orbit=reference.CaptureSimulation();ship=ship.Copy();ship.physics=JsonUtility.ToJson(orbit);ship.pose=new FramePose{body=1,y=150000};ship.pilot=0;motion=new SurvivalMotion{actor=1,tick=10,epoch=3,sequence=40,ship=ship,interior=true,pose=new FramePose{body=1,frame=10,y=-3.95,z=-10},forwardZ=1};
                    session.latest.motion=motion.Copy();session.latest.vitals.controlledShip=0;session.PredictMotion(motion,new SurvivalInput{sequence=41,epoch=3},1f/30);
                    for(int i=0;i<8;i++)reference.StepSimulation(1f/30,Vector3.zero,Vector3.zero,false,false,false,false,false);
                    check((reference.CaptureSimulation().pose.position-SurvivalVehicleSimulation.Position(motion.ship.pose)).Magnitude<1e-6&&motion.pose.frame==10,"Walking inside at 8x predicts the orbiting parent using the same gravity and orbit hold as authority");
                    reference.RestoreSimulation(new PlanetShipSimulationSnapshot{pose=new SpacePose{position=new Double3(0,149999,0),rotation=Quaternion.identity}});reference.ToggleOrbitalVelocityHold();check(!reference.OrbitalVelocityHoldEnabled,"The 30 km orbit floor still rejects lower flight");
                }
                SurvivalMotion State(uint sequence,long tick,double x,float yaw,bool interior){var q=Quaternion.Euler(0,yaw,0);return new SurvivalMotion{actor=1,epoch=9,sequence=sequence,tick=tick,interior=interior,pose=new FramePose{body=1,frame=interior?10:0,x=interior?x*.01:x},ship=new SurvivalShipState{entity=10,owner=1,pilot=interior?0:1,pose=new FramePose{body=1,x=x,y=150000},rotationX=q.x,rotationY=q.y,rotationZ=q.z,rotationW=q.w}};}
                foreach(bool interior in new[]{false,true}){
                    session.ResetPresentation();var prior=State(39,49,90,0,interior);var before=State(40,50,100,10,interior);var after=State(40,53,120,30,interior);session.previousRenderState=prior;session.predicted=after;session.clientAccumulator=.5f/session.Config.tickRate;session.predictionHistory[39]=State(39,52,110,20,interior);
                    var expected=SurvivalClientPresentation.Sample(prior,before,.5f,Vector2.zero,default);session.ReconcilePresentation(before);
                    var shown=SurvivalClientPresentation.Sample(session.previousRenderState,after,.5f,Vector2.zero,session.visualCorrection,session.visualShipCorrection,session.visualShipRotationCorrection);
                    check(shown.pose.Distance(expected.pose)<.0001&&shown.ship.pose.Distance(expected.ship.pose)<.0001&&Quaternion.Angle(SurvivalShipInteriorModel.Rotation(shown.ship),SurvivalShipInteriorModel.Rotation(expected.ship))<.01,"Reconciliation preserves the displayed "+(interior?"walking interior and parent":"pilot camera and hull")+" across acknowledged server-clock changes");
                }
                var a=State(50,100,0,0,false);var b=State(51,104,100,0,false);check(Math.Abs(SurvivalClientPresentation.Sample(a,b,.5f,Vector2.zero,default).pose.x-50)<.001,"Adjacent input samples interpolate even when server tick alignment changes");
                session.ResetPresentation();check(session.previousRenderState==null&&session.visualCorrection.Magnitude==0&&session.visualShipCorrection.Magnitude==0&&Quaternion.Angle(session.visualShipRotationCorrection,Quaternion.identity)<.001,"Authorized frame changes clear all camera and ship correction offsets");
                var confirmed=new SurvivalMotion{actor=1,tick=100,sequence=50,epoch=4,pose=new FramePose{body=1,y=150000},ship=new SurvivalShipState{entity=10,owner=1,pilot=1,landed=false,pose=new FramePose{body=1,y=150000},physics=JsonUtility.ToJson(initial)}};var authoritative=confirmed.Copy();var future=confirmed.Copy();future.tick++;future.sequence++;
                string unchanged=JsonUtility.ToJson(authoritative);
                check(SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1)&&JsonUtility.ToJson(authoritative)==unchanged,"A verified matching owner ship acknowledgement can retain pending prediction without mutating authoritative state");
                authoritative.pose.x=.0005;authoritative.ship.pose.x=.0005;var tiny=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(authoritative.ship.physics);tiny.pose.position.x=.0005;authoritative.ship.physics=JsonUtility.ToJson(tiny);
                check(SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"Sub-millimetre confirmed flight roundoff does not force hundreds of identical substeps to replay");
                authoritative=confirmed.Copy();authoritative.pose.x=.01;check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"A changed authoritative position takes the normal correction/replay path");
                authoritative=confirmed.Copy();var changed=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(authoritative.ship.physics);changed.assist=false;authoritative.ship.physics=JsonUtility.ToJson(changed);check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"A changed authoritative flight-assist mode cannot reuse old prediction");
                authoritative=confirmed.Copy();authoritative.ship.rearOpen=true;check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"Authoritative door/contact changes require normal replay");
                authoritative=confirmed.Copy();authoritative.tick++;check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"Held or lost input clock mismatches cannot bypass replay");
                authoritative=confirmed.Copy();authoritative.epoch++;check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"Respawn and control authority changes cannot retain old prediction");
                authoritative=confirmed.Copy();authoritative.ship.owner=2;check(!SurvivalPredictionReuse.CanRetain(confirmed,authoritative,future,1),"Foreign or private-physics-redacted ships do not use owner prediction reuse");
                session.universe=SurvivalUniverse.Create("retained-clock","native",73129,ServerDefinition(73129));session.latest=new SurvivalSnapshot{tick=99,simulationSeconds=999,motion=confirmed.Copy(),self=new IndustrialActor{id=1,health=100},vitals=new SurvivalPlayerRecord{actor=1,controlledShip=10},testTimeScale=8};session.predicted=future;session.predictionHistory.Clear();session.predictionHistory[50]=confirmed.Copy();session.predictionHistory[51]=future.Copy();session.unacknowledged.Clear();session.unacknowledged.Add(new SurvivalInput{sequence=51,epoch=4});session.retainedShipSnapshots=0;
                session.Receive(new SurvivalSnapshot{tick=100,simulationSeconds=1000,motion=confirmed.Copy(),self=new IndustrialActor{id=1,health=100},vitals=new SurvivalPlayerRecord{actor=1,controlledShip=10},testTimeScale=8});
                check(session.retainedShipSnapshots==1&&ReferenceEquals(session.predicted,future)&&Math.Abs(session.universe.Galaxy.Time-(1000+8.0/30))<1e-8,"Retaining verified prediction restores the celestial clock to the same pending-input time instead of leaving planets at the acknowledged past time");
            }finally{session.universe?.Dispose();session.universe=null;session.DisposeShipSimulations();session.welcome=null;DestroyImmediate(go);DestroyImmediate(definition);DiagnosticMultipleSessions=multiple;}
        }
    }
}
#endif
