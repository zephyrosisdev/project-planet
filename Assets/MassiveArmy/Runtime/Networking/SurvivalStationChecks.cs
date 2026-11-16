#if UNITY_EDITOR || UNITY_SERVER
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal static void RunStationChecks(Action<bool,string> check){
            check(SupportsServerSaveContent(1)&&SupportsServerSaveContent(2)&&SupportsServerSaveContent(3)&&!SupportsServerSaveContent(0)&&!SupportsServerSaveContent(4),"Recall-aware servers migrate previous saves and reject unknown future content; the content-3 marker prevents older servers from misreading an in-flight recall");
            var legacyConfig=ReadServerConfig("{\"maxPlayers\":2,\"walkSnap\":4}");check(legacyConfig.maxPlayers==2&&legacyConfig.walkSnap==4&&legacyConfig.tickRate==30&&legacyConfig.walkAcceleration==40&&legacyConfig.shipBoostSpeed==360,"Existing server configs retain custom values and receive defaults for newly introduced movement profiles");
            int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length,cameras=Resources.FindObjectsOfTypeAll<Camera>().Length;
            var go=new GameObject("Station docking authority fixture");var session=go.AddComponent<SurvivalNetworkSession>();var world=new IndustrialWorld{multiplayer=new SurvivalSessionState{worldId="station-native",simulationSeconds=10}};var store=new IndustrialAuthority(world);session.authority=store;session.Config=new SurvivalServerConfig();
            try{
                var station=new SurvivalStationState{entity=100,owner=2,body=1,radius=10000,mu=981000000,epoch=10,uy=1,vx=1};world.multiplayer.stations.Add(station);var berth=StationBerth(station,10);var pose=new FramePose{body=1,x=berth.position.x,y=berth.position.y,z=berth.position.z};world.Register(1,pose);world.Register(2,pose);
                var ship=new SurvivalShipState{entity=200,owner=1,pilot=1,landed=false,pose=pose,rotationX=berth.rotation.x,rotationY=berth.rotation.y,rotationZ=berth.rotation.z,rotationW=berth.rotation.w,velocityX=berth.linearVelocity.x,velocityY=berth.linearVelocity.y,velocityZ=berth.linearVelocity.z,physics=JsonUtility.ToJson(new PlanetShipSimulationSnapshot{pose=berth,orbitHold=true,orbitAssistActive=true,orbitHoldRadius=10000})};world.multiplayer.ships.Add(ship);
                var record=new SurvivalPlayerRecord{actor=1,controlledShip=ship.entity,attachedShip=ship.entity};world.multiplayer.players.Add(record);var peer=new Peer{record=record,motion=new SurvivalMotion{actor=1,ship=ship,pose=pose,epoch=5}};session.peers.Add(1,peer);var owner=new Peer{record=new SurvivalPlayerRecord{actor=2}};
                check(session.ToggleStationDock(peer,station.entity)!=null,"A player-built station rejects an uninvited ship owner");
                check(session.InviteStation(peer,station.entity,1,true)!=null&&session.InviteStation(owner,station.entity,1,true)==null,"Only the station owner can grant docking access");
                ship.velocityX+=10;check(session.ToggleStationDock(peer,station.entity)!=null,"Excess relative approach speed cannot acquire a berth");ship.velocityX-=10;
                var before=ship.pose;ship.pose.z-=20;check(session.ToggleStationDock(peer,station.entity)!=null,"Docking cannot teleport a ship from outside the 12 metre approach envelope");ship.pose=before;
                check(!session.StationSweep(1,berth.position,berth.position,55)&&session.StationSweep(1,StationPose(station,10).position+Double3.Rotate(berth.rotation,new Double3(100,0,-60)),StationPose(station,10).position+Double3.Rotate(berth.rotation,new Double3(100,0,-60)),1),"Shared CPU station envelopes keep the berth clear and reject gantry penetration without render colliders");
                var futureBerth=StationBerth(station,10.55);check(!session.StationSweep(1,futureBerth.position,futureBerth.position,55,10.55)&&session.StationSweep(1,futureBerth.position,futureBerth.position,55,10),"Prediction contacts use the matching replay time rather than the older received station position");
                var earlier=StationBerth(station,10.55-1.0/30);check(!session.StationSweep(1,earlier.position,futureBerth.position,55,10.55,1f/30),"Moving-station collision queries compare both ends in the corresponding orbital frames");
                peer.input.Enqueue(new SurvivalInput{sequence=1,y=1,epoch=5},0,30);peer.actions.Enqueue(new SurvivalAction{epoch=5});
                check(session.ToggleStationDock(peer,station.entity)==null&&station.dockedShip==ship.entity&&ship.dockedStation==station.entity&&peer.motion.epoch==6&&peer.input.Next().y==0&&peer.actions.Count==0,"Dock acquisition reserves one berth and explicitly resets stale controls and prediction");
                var moored=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);check(!moored.orbitHold&&!moored.orbitAssistActive&&moored.orbitHoldRadius==0,"Docking cancels orbit hold so undocking cannot resume stale thruster control");
                session.StepDockedShip(ship,11);var advanced=StationBerth(station,11);check((SurvivalVehicleSimulation.Position(ship.pose)-advanced.position).Magnitude<1e-8,"A moored ship follows the analytic station clock independently of rendering and pilot input");
                world.multiplayer.simulationSeconds=11;check(session.ToggleStationDock(peer,station.entity)==null&&station.dockedShip==0&&ship.dockedStation==0&&peer.motion.epoch==7,"Authorized undocking releases the berth and resets prediction again");
                var flightDefinition=ServerDefinition(73129);flightDefinition.radius=5000;flightDefinition.terrainHeight=0;flightDefinition.oceanHeight=-100;flightDefinition.gravitationalParameter=station.mu;
                try{
                    var initial=StationBerth(station,11);var free=new SurvivalShipState{entity=201,owner=1,landed=false,pose=new FramePose{body=1,x=initial.position.x,y=initial.position.y,z=initial.position.z},physics=JsonUtility.ToJson(new PlanetShipSimulationSnapshot{pose=initial})};var missing=new SurvivalInputQueue();
                    for(int i=1;i<=15;i++){world.multiplayer.simulationSeconds=11+i/30.0;session.SimulateShip(free,ShipModules.Starter(1),flightDefinition,missing.Next(false),1f/30,world.multiplayer.simulationSeconds);}
                    check(new Double3(free.velocityX,free.velocityY,free.velocityZ).Magnitude>initial.linearVelocity.Magnitude*.95,"A delayed control-frame acknowledgement does not apply a synthesized brake to a freely orbiting ship");
                    var orbitState=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(free.physics);using(var clocked=new PlanetSpacecraft(flightDefinition,ShipModules.Starter(1),(a,b,q)=>true)){clocked.RestoreSimulation(orbitState);clocked.StepSimulation(1f/30,Vector3.zero,Vector3.zero,false,false,false,false,false,false,false,true);check(clocked.CaptureSimulation().rotationLocked,"Replicated attitude-hold input reaches the same authoritative orbital lock integrator as single player");}
                }finally{session.DisposeShipSimulations();DestroyImmediate(flightDefinition);}
                var recovered=IndustrialAuthority.Restore(IndustrialAuthority.Snapshot(world));check(recovered.multiplayer.stations.Count==1&&recovered.multiplayer.stations[0].invitedActors.Contains(1),"Player station ownership, orbital parameters and invitations survive the existing world checkpoint");
                session.PublishStations();var publication=session.entities[StationEntity(station.entity)];check(!JsonUtility.FromJson<SurvivalStationState>(publication.data).invitedActors.Contains(1),"Station public interest state omits its private invitation list");
                check(meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&cameras==Resources.FindObjectsOfTypeAll<Camera>().Length,"Station orbit, collision and docking authority allocate no render resources");
            }finally{session.authority=null;store.Dispose();DestroyImmediate(go);}
        }
    }
}
#endif
