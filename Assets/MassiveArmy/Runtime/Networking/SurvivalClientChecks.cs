#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunClientPresentationChecks(Action<bool,string> check){
            RunFlightPresentationChecks(check);
            RunShipLandingPresentationChecks(check);
            GameObject clockObject=null;PlanetDefinition clockDefinition=null;bool previousMultiple=DiagnosticMultipleSessions;
            try{
                DiagnosticMultipleSessions=true;clockObject=new GameObject("Fixed-step test clock checks");var clockSession=clockObject.AddComponent<SurvivalNetworkSession>();clockSession.Config=new SurvivalServerConfig();
                check(clockSession.RequestTestTimeScale(2)!=null&&clockSession.pendingTestTimeScale==0,"Production sessions reject client requests to accelerate the clock");
                clockSession.Config.allowTestTimeControls=true;check(clockSession.RequestTestTimeScale(3)!=null&&clockSession.pendingTestTimeScale==0,"Test sessions reject malformed speed ladder values");
                clockSession.Config.testTimeScale=8;clockSession.HeadlessProbeClient=true;clockDefinition=ScriptableObject.CreateInstance<PlanetDefinition>();clockDefinition.bodyId=1;clockDefinition.radius=120000;clockDefinition.terrainHeight=0;clockDefinition.gravitationalParameter=9.81*120000*120000;clockSession.headlessDefinition=clockDefinition;
                clockSession.latest=new SurvivalSnapshot{tick=20,simulationSeconds=10,self=new IndustrialActor{health=100},vitals=new SurvivalPlayerRecord()};
                var surface=new PlanetSurface(clockDefinition);var point=surface.ProjectToSurface(new Double3(0,120000,0),.05);var accelerated=new SurvivalMotion{actor=1,tick=20,epoch=1,pose=new FramePose{body=1,x=point.x,y=point.y,z=point.z},grounded=true,forwardZ=1};var reference=accelerated.Copy();
                var input=new SurvivalInput{sequence=1,epoch=1,y=1,lookX=8,lookY=3,jump=true};clockSession.PredictMotion(accelerated,input,1f/30);
                for(int sub=0;sub<8;sub++)clockSession.PredictMotionStep(reference,input.Substep(sub),1f/30,10+(sub+1)/30.0);
                check(accelerated.tick==21&&accelerated.pose.Distance(reference.pose)<1e-8&&accelerated.pitch==reference.pitch&&Math.Abs(accelerated.verticalSpeed-reference.verticalSpeed)<1e-8,"An 8x predicted input runs eight ordinary fixed steps, consumes look/jump once and advances one acknowledged transport tick");
                check(clockSession.RequestTestTimeScale(8)==null&&clockSession.pendingTestTimeScale==8,"Enabled test controls stage a bounded server-wide clock change");check(clockSession.RequestTestTimeScale(16)!=null&&clockSession.pendingTestTimeScale==8,"Test controls reject speeds above the measured 8x ceiling without changing the pending clock");
                var clockPacket=new SurvivalSnapshot{testTimeScale=8,self=new IndustrialActor(),vitals=new SurvivalPlayerRecord()};check(Envelope(clockPacket).testTimeScale==8&&LeanSnapshot(clockPacket).testTimeScale==8,"Large and lean snapshot envelopes preserve the authoritative test clock");
            }finally{if(clockObject!=null)UnityEngine.Object.DestroyImmediate(clockObject);if(clockDefinition!=null)UnityEngine.Object.DestroyImmediate(clockDefinition);DiagnosticMultipleSessions=previousMultiple;}
            var a=new SurvivalMotion{actor=1,tick=10,epoch=1,pose=new FramePose{body=1,y=10000},forwardZ=1};var b=a.Copy();b.tick++;b.pose.x=.2;
            var view=SurvivalClientPresentation.Sample(a,b,.5f,new Vector2(5,2),default);
            check(Math.Abs(view.pose.x-.1)<1e-8&&Math.Abs(view.pitch+2)<1e-6&&view.forwardX>0,"A render between simulation ticks interpolates walking and applies unconsumed mouse look without a server round trip");
            check(b.pose.x==.2&&b.pitch==0&&b.forwardX==0&&b.tick==11,"Render sampling cannot mutate prediction, input history or authoritative simulation time");
            var next=SurvivalClientPresentation.Sample(a,b,.75f,new Vector2(8,3),default);check(next.pose.x>view.pose.x&&next.forwardX>view.forwardX&&next.pitch<view.pitch,"Multiple render frames can advance presentation and mouse look between fixed simulation ticks");
            b.epoch++;view=SurvivalClientPresentation.Sample(a,b,.2f,Vector2.zero,default);check(view.pose.x==b.pose.x,"Respawns and control-frame resets do not interpolate across authority epochs");
            var world=new IndustrialWorld{multiplayer=new SurvivalSessionState()};var actor=world.Register(71,new FramePose{body=1,y=10000});actor.inventory.EnableSlots(40);actor.inventory.Add("water",8);var layout=ShipModules.Starter(actor.id);layout.id=world.AllocateId();world.ships.Add(layout);var ship=new SurvivalShipState{entity=layout.id,owner=actor.id,pose=actor.pose};world.multiplayer.ships.Add(ship);
            using(var authority=new IndustrialAuthority(world)){
                world.InventoryPoseResolver=pose=>SurvivalShipInteriorModel.World(ship,pose);
                int slot=actor.inventory.items.Find(s=>s.item=="water").slot;
                check(authority.MoveInventory(actor.id,new InventoryAddress{slot=slot},new InventoryAddress{slot=5},4,"water")==null&&actor.inventory.Count("water")==8&&actor.inventory.AtSlot(5).count==4,"Backpack/action-bar split transfers conserve items through the authoritative inventory transaction");
                check(authority.MoveInventory(actor.id,new InventoryAddress{slot=5},new InventoryAddress{slot=6},1,"ration")!=null&&actor.inventory.AtSlot(5).count==4,"A stale dragged-item identity cannot move a replacement stack");
                var local=new FramePose{body=1,frame=ship.entity};check(authority.DropInventory(actor.id,new InventoryAddress{slot=5},2,local,actor.pose,"water")==null&&world.droppedItems.Count==1&&actor.inventory.Count("water")==6,"Carried items can be dropped into a recoverable container in a stable multiplayer ship frame");
                var drop=world.droppedItems[0];var stack=drop.inventory.items[0];check(authority.MoveInventory(actor.id,new InventoryAddress{section="ground",entity=drop.id,slot=stack.slot},new InventoryAddress{slot=5},2,"water")==null&&actor.inventory.Count("water")==8,"Recovering a ship-local dropped stack conserves the same carried inventory");
            }
        }
        static void RunShipLandingPresentationChecks(Action<bool,string> check){
            bool multiple=DiagnosticMultipleSessions;DiagnosticMultipleSessions=true;
            var go=new GameObject("Recall proxy promotion and landed ramp presentation");var session=go.AddComponent<SurvivalNetworkSession>();var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=10000;definition.terrainHeight=0;definition.oceanHeight=-35;
            try{
                const long id=85;var ship=new SurvivalShipState{entity=10,owner=1,landed=false,pose=new FramePose{body=1,y=12000}};
                session.visible[id]=new SurvivalEntity{id=id,kind="ship-proxy",pose=ship.pose};session.clientShips[ship.entity]=ship;session.entityPositions[id]=new Double3(0,15000,0);
                session.PrepareShipPoses(.2f);check(Math.Abs(session.presentedShips[10].point.y-14400)<.001,"Airborne ship presentation interpolates from the previous coarse proxy");
                session.visible[id].kind="ship";for(int i=0;i<60;i++)session.PrepareShipPoses(.2f);
                check(Math.Abs(session.presentedShips[10].point.y-12000)<.01&&(session.entityPositions[id]-session.presentedShips[10].point).Magnitude<1e-9,"Detailed ship promotion updates the presentation origin every frame instead of remaining suspended near the old proxy");
                ship.pose.y=10008.3;ship.landed=true;session.entityPositions[id]=new Double3(0,14000,0);session.shipPresentationRotations[10]=Quaternion.Euler(0,0,40);session.PrepareShipPoses(.001f);
                check((session.presentedShips[10].point-new Double3(0,10008.3,0)).Magnitude<1e-9&&Quaternion.Angle(session.presentedShips[10].rotation,Quaternion.identity)<.001,"Landing immediately aligns the rendered ship and attitude with authoritative boarding contacts, even at a low render blend");
                var drawn=ship.Copy();var p=session.presentedShips[10];drawn.pose.x=p.point.x;drawn.pose.y=p.point.y;drawn.pose.z=p.point.z;drawn.rotationX=p.rotation.x;drawn.rotationY=p.rotation.y;drawn.rotationZ=p.rotation.z;drawn.rotationW=p.rotation.w;
                var layout=ShipModules.Starter(1);var surface=new PlanetSurface(definition);var authorityRamp=new SurvivalShipInteriorModel(layout,ship,surface);var visualRamp=new SurvivalShipInteriorModel(layout,drawn,surface);
                check(Math.Abs(authorityRamp.rampFootY-visualRamp.rampFootY)<.001&&Math.Abs(visualRamp.rampFootY+8.3)<.3,"The displayed landed ramp has the same reachable ground endpoint and slope as server walking collision");
                session.ReceiveShipStates(new SurvivalSnapshot{motion=new SurvivalMotion(),removed=new System.Collections.Generic.List<long>{id}});
                check(!session.entityPositions.ContainsKey(id)&&!session.shipPresentationRotations.ContainsKey(10)&&!session.clientShips.ContainsKey(10),"Leaving interest clears the ship's old presentation origin and attitude");
                ship.pose.y=20000;ship.landed=false;session.clientShips[10]=ship;session.PrepareShipPoses(.001f);
                check(Math.Abs(session.presentedShips[10].point.y-20000)<1e-9,"Interest re-entry starts at the current ship position without dragging it from stale cached coordinates");
            }finally{session.ClearParentPresentations();DestroyImmediate(go);DestroyImmediate(definition);DiagnosticMultipleSessions=multiple;}
        }
    }
}
#endif
