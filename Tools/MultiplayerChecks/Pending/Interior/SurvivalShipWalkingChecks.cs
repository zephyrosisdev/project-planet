#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunShipWalkingChecks(Action<bool,string> check){
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,materials=Resources.FindObjectsOfTypeAll<Material>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            var go=new GameObject("Ship-local walking authority fixture");var session=go.AddComponent<SurvivalNetworkSession>();IndustrialAuthority authority=null;
            const string path="TestResults/Multiplayer/ship-walking-native.xml";
            try{
                foreach(string suffix in new[]{"",".journal",".bak"})if(File.Exists(path+suffix))File.Delete(path+suffix);
                var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="ship-walking-native"}};var owner=world.Register(1,new FramePose{body=1,y=10020});var guest=world.Register(2,owner.pose);var layout=ShipModules.Starter(1);world.ships.Add(layout);
                var q=Quaternion.Euler(15,20,35);var ship=new SurvivalShipState{entity=layout.id,owner=1,pose=new FramePose{body=1,y=10020},rotationX=q.x,rotationY=q.y,rotationZ=q.z,rotationW=q.w};world.multiplayer.ships.Add(ship);
                var record=new SurvivalPlayerRecord{actor=1,identity="walking-owner"};var passenger=new SurvivalPlayerRecord{actor=2,identity="walking-guest"};world.multiplayer.players.Add(record);world.multiplayer.players.Add(passenger);
                var peer=new Peer{record=record,motion=new SurvivalMotion{actor=1,pose=owner.pose,forwardZ=1}};var crew=new Peer{record=passenger,motion=new SurvivalMotion{actor=2,pose=guest.pose,forwardZ=1}};
                authority=new IndustrialAuthority(world,path);session.authority=authority;session.Config=new SurvivalServerConfig();session.peers[1]=peer;session.peers[2]=crew;session.connectedActors.Add(1);session.connectedActors.Add(2);
                owner.pose=guest.pose=ShipEntry(ship);
                check(session.BoardPassenger(crew,ship.entity)!=null&&session.BoardPassenger(peer,ship.entity)==null,"Boarding requires invitation while the owner can enter their own ship as a walker");
                check(peer.motion.interior&&peer.motion.pose.frame==ship.entity&&owner.pose.frame==0&&owner.pose.Distance(Actor(session,peer.motion))<1e-7,"Controlled walking uses a stable ship frame while industrial authority retains canonical body coordinates");
                SurvivalShipAuthority.Invite(world,1,ship.entity,2,true);
                check(session.BoardPassenger(crew,ship.entity)==null&&crew.motion.interior&&guest.pose.Distance(owner.pose)>=.75&&session.EnterPilot(crew,ship.entity)!=null,"An invited passenger receives clear walking space and cannot obtain pilot authority");
                var start=peer.motion.Copy();for(int i=0;i<40;i++){session.StepInterior(peer,ship,new SurvivalInput{sequence=(uint)(i+1),y=1},1f/30);owner.pose=session.ActorPose(peer.motion);}
                check(peer.motion.pose.z<-31.4&&peer.motion.pose.z>-33&&Math.Abs(peer.motion.pose.y+3.95)<.03,"Authored CPU floors support a walker and the closed inner hatch stops passage in a rotated ship");
                var stopped=peer.motion.Copy();check(session.ToggleShipDoor(peer,ship.entity,1)==null,"A nearby authorized walker can open the inner hatch");
                for(int i=0;i<40;i++){session.StepInterior(peer,ship,new SurvivalInput{sequence=(uint)(50+i),y=1},1f/30);owner.pose=session.ActorPose(peer.motion);}
                check(peer.motion.pose.z>-30&&peer.motion.pose.frame==ship.entity,"Opening the authoritative hatch restores passage without a world-frame teleport");
                var first=start.Copy();var replay=start.Copy();var model=session.Interior(ship,layout);for(int i=0;i<30;i++){var input=new SurvivalInput{sequence=(uint)(100+i),x=.2f,y=1};SurvivalShipInteriorModel.Step(first,input,1f/30,7,model);SurvivalShipInteriorModel.Step(replay,input,1f/30,7,model);}
                check(first.pose.Distance(replay.pose)<1e-8&&first.sequence==replay.sequence,"Prediction replay uses the same ship-local floor and hatch contacts as authority");
                var local=peer.motion.pose;var before=session.ActorPose(peer.motion);ship.pose.x+=1000;session.StepServerShips(1f/30);
                check(peer.motion.pose.Distance(local)==0&&Math.Abs(owner.pose.x-before.x-1000)<1e-6,"Parent motion transports walking passengers without rewriting local coordinates or advancing walking twice");
                var localRoundTrip=SurvivalShipInteriorModel.Local(ship,SurvivalShipInteriorModel.World(ship,local));check(localRoundTrip.Distance(local)<1e-7,"Canonical ship/body coordinate conversion is independent of rendering origin");
                ship.rearOpen=true;session.BeginInterior(peer,ship,new FramePose{body=1,frame=ship.entity,x=-6.55,y=-3.95,z=-37.85});session.BeginInterior(crew,ship,new FramePose{body=1,frame=ship.entity,y=-3.95,z=-38.25});
                check(session.ToggleShipDoor(peer,ship.entity,0)!=null&&ship.rearOpen,"The exterior hatch cannot close onto another living passenger");session.BeginInterior(crew,ship,new FramePose{body=1,frame=ship.entity,x=2,y=-3.95,z=-35});
                check(session.ToggleShipDoor(peer,ship.entity,0)==null&&!ship.rearOpen,"A clear exterior hatch closes through validated authority");
                ship.rearOpen=ship.innerOpen=true;model=session.Interior(ship,layout);check(!model.Pressurized(ShipModules.P(0,-3.95,-20)),"Both open pressure doors expose the main compartment");ship.innerOpen=false;model=session.Interior(ship,layout);check(model.Pressurized(ShipModules.P(0,-3.95,-20))&&!model.Pressurized(ShipModules.P(0,-3.95,-35)),"The inner hatch isolates the main compartment from an open airlock");
                session.BeginInterior(peer,ship,new FramePose{body=1,frame=ship.entity,x=1.8,y=-3.95,z=28});long epoch=peer.motion.epoch;
                check(session.EnterPilot(peer,ship.entity)==null&&!peer.motion.interior&&peer.motion.epoch>epoch&&record.controlledShip==ship.entity&&session.CorrectionDistance(peer.motion)==session.Config.shipSnap,"Owner pilot entry validates seat reach and explicitly resets the walking prediction frame");
                peer.input.Enqueue(new SurvivalInput{sequence=1,y=1},1,30);epoch=peer.motion.epoch;
                check(session.LeavePilot(peer)==null&&peer.motion.interior&&peer.motion.epoch>epoch&&peer.input.Next().y==0&&session.CorrectionDistance(peer.motion)==session.Config.walkSnap,"Leaving the pilot seat selects clear authored floor space, releases held controls and restores walking correction limits");
                authority.Save();authority.Dispose();authority=null;var saved=IndustrialAuthority.Load(path);
                check(saved.multiplayer.players[0].interiorWalking&&saved.multiplayer.players[0].interiorPose.frame==ship.entity&&saved.multiplayer.ships[0].rearOpen,"Player-local coordinates and authoritative hatch states survive the existing checkpoint store");
                check(meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&materials==Resources.FindObjectsOfTypeAll<Material>().Length&&cameras==Resources.FindObjectsOfTypeAll<Camera>().Length,"Ship-local collision, handoff and persistence allocate no render resources");
            }finally{session.authority=null;authority?.Dispose();DestroyImmediate(go);}
        }
        static FramePose Actor(SurvivalNetworkSession session,SurvivalMotion motion)=>session.ActorPose(motion);
    }
}
#endif
