using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly Dictionary<long,SurvivalStationState> clientStations=new Dictionary<long,SurvivalStationState>();
        Mesh stationMesh;string stationInviteActor="";
        static readonly Vector3[] StationCentres={new Vector3(-100,0,-60),new Vector3(100,0,-60),new Vector3(0,0,95),new Vector3(0,95,-60),new Vector3(0,-95,-60)};
        static readonly Vector3[] StationSizes={new Vector3(10,180,300),new Vector3(10,180,300),new Vector3(210,180,10),new Vector3(210,10,300),new Vector3(210,10,300)};
        Mesh StationMesh(){if(stationMesh!=null)return stationMesh;var geometry=new ModularGeometry();for(int i=0;i<StationCentres.Length;i++)geometry.Box(StationCentres[i],StationSizes[i],new Color(.25f,.34f,.4f),Quaternion.identity);return stationMesh=geometry.Build("Shared player-built orbital docking gantry");}
        bool StationSweep(int body,Double3 from,Double3 to,double radius,double? queryTime=null,float elapsed=0){
            IEnumerable<SurvivalStationState> stations=authority!=null?authority.World.multiplayer.stations:clientStations.Values;
            foreach(var station in stations){if(station.body!=body)continue;double time=queryTime??StationTime();var pose=StationPose(station,time);var previous=elapsed>0?StationPose(station,Math.Max(0,time-elapsed)):pose;var a=Double3.Rotate(Quaternion.Inverse(previous.rotation),from-previous.position);var b=Double3.Rotate(Quaternion.Inverse(pose.rotation),to-pose.position);if(a.Magnitude>400+radius&&(b-a).Magnitude<a.Magnitude-400-radius)continue;var delta=(b-a).Float;float length=delta.magnitude;for(int i=0;i<StationCentres.Length;i++){var bounds=new Bounds(StationCentres[i],StationSizes[i]+Vector3.one*(float)((radius+350*pose.angularVelocity.magnitude*elapsed)*2));if(bounds.Contains(a.Float)||length>0&&bounds.IntersectRay(new Ray(a.Float,delta/length),out float distance)&&distance<=length)return true;}}
            return false;
        }
        string InviteStation(Peer peer,long entity,long actor,bool grant){var station=Station(entity);if(station==null||station.owner!=peer.record.actor)return "Only the station owner can change docking invitations";if(actor==station.owner||!authority.World.multiplayer.players.Exists(p=>p.actor==actor))return "Choose another registered survivor";if(grant){if(!station.invitedActors.Contains(actor))station.invitedActors.Add(actor);}else station.invitedActors.Remove(actor);return null;}
        void StationGUI(){
            networkUi.Label("PLAYER-BUILT ORBITAL STATIONS");if(networkUi.Button("Deploy crafted docking station kit from piloted ship"))Submit(new SurvivalAction{kind="station-deploy"});
            stationInviteActor=networkUi.TextField(stationInviteActor,20);foreach(var station in clientStations.Values)if(station.owner==ActorId){networkUi.Label("Station #"+station.entity+" · "+(station.dockedShip==0?"berth free":"berth occupied"));if(long.TryParse(stationInviteActor,out long actor)){networkUi.BeginHorizontal();if(networkUi.Button("Invite survivor #"+actor))Submit(new SurvivalAction{kind="station-invite",entity=station.entity,count=actor});if(networkUi.Button("Revoke docking"))Submit(new SurvivalAction{kind="station-revoke",entity=station.entity,count=actor});networkUi.EndHorizontal();}}
        }
        void StationGuidance(){if(predicted?.ship==null||latest.vitals.controlledShip==0)return;var station=NearestStation(predicted.ship);if(station==null)return;var berth=StationBerth(station,StationTime(predicted));double distance=(berth.position-SurvivalVehicleSimulation.Position(predicted.ship.pose)).Magnitude,speed=(new Double3(predicted.ship.velocityX,predicted.ship.velocityY,predicted.ship.velocityZ)-berth.linearVelocity).Magnitude;networkUi.Label(new Rect(12,194,1000,50),"Station #"+station.entity+" · owner #"+station.owner+" · berth "+distance.ToString("0")+" m · relative "+speed.ToString("0.0")+" m/s · J dock/undock\nAlign with orbital travel direction; dock within 12 m, 10° and 5 m/s. Walk inside your moored ship with F.");}
        static long StationEntity(long id)=>checked(-id*8-1);
        static SpacePose StationPose(SurvivalStationState station,double time){
            var p=station.Position(time);var up=PlanetPrecisionPolicy.Direction(new Double3(p.x,p.y,p.z));
            var normal=Double3.Cross(new Double3(station.ux,station.uy,station.uz),new Double3(station.vx,station.vy,station.vz));
            var velocity=Double3.Cross(normal,new Double3(p.x,p.y,p.z))*Math.Sqrt(station.mu/(station.radius*station.radius*station.radius));
            return new SpacePose{position=new Double3(p.x,p.y,p.z),linearVelocity=velocity,rotation=Quaternion.LookRotation(velocity.Float.normalized,up),angularVelocity=normal.Float*(float)Math.Sqrt(station.mu/(station.radius*station.radius*station.radius))};
        }
        internal static SpacePose StationBerth(SurvivalStationState station,double time){var pose=StationPose(station,time);var offset=Double3.Rotate(pose.rotation,new Double3(0,4,-120));pose.position+=offset;pose.linearVelocity+=new Double3(Vector3.Cross(pose.angularVelocity,offset.Float));return pose;}
        SurvivalStationState Station(long entity)=>authority!=null?authority.World.multiplayer.stations?.Find(s=>s.entity==entity):clientStations.GetValueOrDefault(entity);
        double StationTime(SurvivalMotion motion=null)=>authority!=null?authority.World.multiplayer.simulationSeconds:(latest?.simulationSeconds??0)+Math.Max(0,(motion?.tick??latest?.tick??0)-(latest?.tick??0))*Config.testTimeScale/(double)Config.tickRate;
        string DeployStation(Peer peer){
            var ship=peer.motion.ship;if(ship==null||ship.owner!=peer.record.actor||ship.pilot!=peer.record.actor||peer.record.controlledShip!=ship.entity||ship.dockedStation!=0)return "Pilot your own free-flying ship to deploy a station";
            var definition=surfaces[ship.pose.body].Definition;var position=SurvivalVehicleSimulation.Position(ship.pose);var up=PlanetPrecisionPolicy.Direction(position);
            if(position.Magnitude<definition.radius+Math.Max(20000,definition.ReliefBound+6000))return "Deploy stations above the surface and atmosphere (at least 20 km altitude)";
            if(position.Magnitude<definition.radius+definition.atmosphereHeight+6000)return "Leave the atmosphere before deploying a station";
            var forward=SurvivalMovement.Tangent(ShipRotation(ship)*Vector3.forward,up);var origin=position+new Double3(forward)*600;var radial=origin*(1/origin.Magnitude);var tangent=SurvivalMovement.Tangent(forward,radial.Float);
            var station=new SurvivalStationState{owner=peer.record.actor,body=ship.pose.body,radius=origin.Magnitude,mu=definition.gravitationalParameter,epoch=StationTime(),ux=radial.x,uy=radial.y,uz=radial.z,vx=tangent.x,vy=tangent.y,vz=tangent.z};
            foreach(var existing in authority.World.multiplayer.stations)if(existing.body==station.body&&existing.Position(StationTime()).Distance(station.Position(StationTime()))<1000)return "Leave 1 km between stations";
            if(!ShipPathClear(0,ship.pose.body,origin,origin,250))return "Station deployment volume is obstructed";
            return authority.DeploySurvivalStation(peer.record.actor,station);
        }
        void ResetShipPrediction(SurvivalShipState ship){foreach(var peer in peers.Values)if(peer.record.attachedShip==ship.entity){peer.motion.epoch++;peer.input.Clear();peer.actions.Clear();}}
        string ToggleStationDock(Peer peer,long target){
            var ship=peer.motion.ship;if(ship==null||peer.record.controlledShip!=ship.entity||ship.pilot!=peer.record.actor||ship.owner!=peer.record.actor)return "Only the owner in the pilot seat can dock";
            if(ship.dockedStation!=0){var previous=Station(ship.dockedStation);if(previous==null)return "Docking station unavailable";var berth=StationBerth(previous,StationTime());var end=berth.position+Double3.Rotate(berth.rotation,new Double3(0,0,-100));if(!ShipPathClear(ship.entity,ship.pose.body,berth.position,end,ship.collisionRadius))return "Undocking corridor is obstructed";previous.dockedShip=0;ship.dockedStation=0;var physics=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);physics.pose.linearVelocity=berth.linearVelocity+Double3.Rotate(berth.rotation,new Double3(0,0,-12));physics.assist=false;ship.physics=JsonUtility.ToJson(physics);ResetShipPrediction(ship);return null;}
            var station=Station(target);if(station==null||station.body!=ship.pose.body||station.owner!=peer.record.actor&&!station.invitedActors.Contains(peer.record.actor))return "Dock at your station or one whose owner invited you";
            if(station.dockedShip!=0)return "Station berth is occupied";
            var dock=StationBerth(station,StationTime());var velocity=new Double3(ship.velocityX,ship.velocityY,ship.velocityZ);
            if(ship.landed||ship.autoFlight||(dock.position-SurvivalVehicleSimulation.Position(ship.pose)).Magnitude>12||(velocity-dock.linearVelocity).Magnitude>5||Quaternion.Angle(ShipRotation(ship),dock.rotation)>10)return "Align with the berth within 12 m, below 5 m/s relative speed";
            if(ship.collisionRadius>70)return "This ship is too large for the station berth";
            if(!ShipPathClear(ship.entity,ship.pose.body,SurvivalVehicleSimulation.Position(ship.pose),dock.position,ship.collisionRadius))return "Docking corridor is obstructed";
            station.dockedShip=ship.entity;ship.dockedStation=station.entity;StepDockedShip(ship,StationTime());ResetShipPrediction(ship);return null;
        }
        bool StepDockedShip(SurvivalShipState ship,double time){
            if(ship.dockedStation==0)return false;var station=Station(ship.dockedStation);if(station==null)return false;var dock=StationBerth(station,time);var physics=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(ship.physics);physics.pose=dock;physics.orbitHold=physics.orbitAssistActive=false;physics.orbitHoldRadius=0;physics.orbitAssistStatus="";physics.landed=physics.autoFlight=physics.cruiseActive=false;physics.assist=false;physics.angularVelocity=Vector3.zero;ship.physics=JsonUtility.ToJson(physics);ship.pose=new FramePose{body=station.body,x=dock.position.x,y=dock.position.y,z=dock.position.z};ship.rotationX=dock.rotation.x;ship.rotationY=dock.rotation.y;ship.rotationZ=dock.rotation.z;ship.rotationW=dock.rotation.w;ship.velocityX=dock.linearVelocity.x;ship.velocityY=dock.linearVelocity.y;ship.velocityZ=dock.linearVelocity.z;ship.spinX=dock.angularVelocity.x;ship.spinY=dock.angularVelocity.y;ship.spinZ=dock.angularVelocity.z;ship.landed=ship.autoFlight=false;return true;
        }
        void PublishStations(){foreach(var station in authority.World.multiplayer.stations){var p=station.Position(StationTime());var publicState=new SurvivalStationState{entity=station.entity,owner=station.owner,body=station.body,dockedShip=station.dockedShip,radius=station.radius,mu=station.mu,epoch=station.epoch,ux=station.ux,uy=station.uy,uz=station.uz,vx=station.vx,vy=station.vy,vz=station.vz};AddEntity(StationEntity(station.entity),"station",p,JsonUtility.ToJson(publicState),300);entities[StationEntity(station.entity)].fingerprintPose=new FramePose{body=station.body,frame=station.entity};}}
        void ReceiveStations(SurvivalSnapshot snapshot){foreach(long id in snapshot.removed)if(id<0)clientStations.Remove((-id-1)/8);foreach(var entity in snapshot.changed)if(entity.kind=="station"){var station=JsonUtility.FromJson<SurvivalStationState>(entity.data);if(station!=null&&station.Valid)clientStations[station.entity]=station;}}
        SurvivalStationState NearestStation(SurvivalShipState ship){SurvivalStationState closest=null;double distance=double.MaxValue;foreach(var station in clientStations.Values){if(station.body!=ship.pose.body)continue;double candidate=(StationBerth(station,StationTime(predicted)).position-SurvivalVehicleSimulation.Position(ship.pose)).Magnitude;if(candidate<distance){distance=candidate;closest=station;}}return closest;}
        void StationInput(){if(ChatInputCaptured||ClientMenuOpen||!Application.isFocused||clientWorld.PersonalMenuOpen)return;var keys=UnityEngine.InputSystem.Keyboard.current;if(keys?.jKey.wasPressedThisFrame==true&&predicted?.ship!=null&&latest.vitals.controlledShip==predicted.ship.entity)Submit(new SurvivalAction{kind="station-dock",entity=NearestStation(predicted.ship)?.entity??0});}
    }
}

