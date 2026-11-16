using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        sealed class ShipSimulation {public PlanetSpacecraft ship;public string restored;public int body;public double queryTime;public float queryElapsed;public SurvivalShipState current;}
        readonly Dictionary<long,ShipSimulation> shipSimulations=new Dictionary<long,ShipSimulation>();
        readonly Dictionary<long,SurvivalShipState> clientShips=new Dictionary<long,SurvivalShipState>();
        readonly Dictionary<long,ShipAssembly> clientShipAssemblies=new Dictionary<long,ShipAssembly>();
        readonly Dictionary<long,(long revision,string json)> publicShipLayouts=new Dictionary<long,(long,string)>();
        readonly HashSet<long> connectedActors=new HashSet<long>();
        readonly SurvivalEntityTickBudget shipTickBudget=new SurvivalEntityTickBudget();
        string BoardPassenger(Peer peer,long entity){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);var actor=authority.World.Actor(peer.record.actor);
            if(ship==null||actor.health<=0)return "Ship unavailable";
            if(peer.record.attachedShip!=0||peer.record.controlledEntity!=0||peer.record.controlledShip!=0)return "Leave the current seat first";
            string access=SurvivalShipAuthority.BoardingPermission(authority.World,actor.id,ship);if(access!=null)return access;
            var feet=SurvivalShipInteriorModel.Local(ship,actor.pose);var model=Interior(ship,authority.World.Ship(ship.owner));bool space=SpaceBoarding(peer,ship,feet);
            if(ship.autoFlight||!ship.landed&&!space)return "Enter the open rear hatch below 8 m/s relative speed, or use the landed boarding ramp";
            if(space){var preferred=feet;preferred.z=-37.5;preferred.y=-4;if(!ChooseInteriorFeet(ship,actor.id,preferred,out feet))return "The airlock entry is obstructed";}
            else {var clearance=feet;clearance.y=Math.Max(clearance.y,model.RampHeight(feet.z)+.05+.35*Math.Abs((-4-model.rampFootY)/14));if(!model.AtRampFoot(feet)||!ClearInteriorFeet(ship,model,actor.id,clearance))return "Walk onto the foot of the boarding ramp";}
            if(ship.owner!=actor.id){if(space){int seat=SurvivalShipAuthority.FreePassengerSeat(authority.World,entity,actor.id,connectedActors);if(seat<0)return "Passenger berths are full";peer.record.shipSeat=seat;}else {string error=SurvivalShipAuthority.BoardPassenger(authority.World,peer.record,entity,actor.pose,connectedActors);if(error!=null)return error;}}
            else peer.record.shipSeat=-1;
            var heading=Quaternion.Inverse(ShipRotation(ship))*SurvivalMovement.Forward(peer.motion);feet.yaw=Mathf.Atan2(heading.x,heading.z)*Mathf.Rad2Deg;float pitch=peer.motion.pitch;
            BeginInterior(peer,ship,feet);peer.motion.pitch=pitch;return null;
        }
        bool RestorePassengerBerth(SurvivalPlayerRecord record){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==record.attachedShip);
            if(ship==null||ship.owner==record.actor)return true;
            int seat=SurvivalShipAuthority.FreePassengerSeat(authority.World,ship.entity,record.actor,connectedActors);if(seat<0)return false;record.shipSeat=seat;return true;
        }
        System.Collections.Generic.List<SurvivalShipInvite> ShipInvites(SurvivalShipState ship){
            var invites=new System.Collections.Generic.List<SurvivalShipInvite>();if(ship==null)return invites;
            foreach(long actor in ship.invitedActors){var player=authority.World.multiplayer.players.Find(p=>p.actor==actor);if(player!=null)invites.Add(new SurvivalShipInvite{actor=actor,name=player.name});}return invites;
        }
        void ShipInvitationsGUI(){
            foreach(var invitation in latest.incomingInvitations)networkUi.Label("Passenger invitation: "+invitation.ownerName+" · board at their ship ramp with F");
            if(latest.ownedShip==0)return;
            networkUi.Label("SHIP PASSENGERS · you retain pilot control");
            foreach(var invite in latest.shipInvites)if(networkUi.Button("Revoke invitation: "+invite.name))Submit(new SurvivalAction{kind="ship-revoke",entity=latest.ownedShip,count=invite.actor});
            foreach(var entity in visible.Values)if(entity.kind=="player"){
                var player=JsonUtility.FromJson<SurvivalPlayerProxy>(entity.data);if(player.actor==ActorId||latest.shipInvites.Exists(p=>p.actor==player.actor))continue;
                if(networkUi.Button("Invite "+player.name+" · "+latest.self.pose.Distance(entity.pose).ToString("0")+" m"))Submit(new SurvivalAction{kind="ship-invite",entity=latest.ownedShip,count=player.actor});
            }
        }
        List<SurvivalIncomingInvitation> IncomingInvitations(long actor){
            var result=new List<SurvivalIncomingInvitation>();foreach(var ship in authority.World.multiplayer.ships)if(ship.invitedActors.Contains(actor)){var owner=authority.World.multiplayer.players.Find(p=>p.actor==ship.owner);result.Add(new SurvivalIncomingInvitation{ship=ship.entity,ownerName=owner?.name??"Survivor"});}return result;
        }
        [Serializable] sealed class ShipProxy {public FramePose pose;public float rotationX,rotationY,rotationZ,rotationW;}
        string ShipLayout(SurvivalShipState ship){
            var assembly=authority.World.Ship(ship.owner);if(publicShipLayouts.TryGetValue(ship.entity,out var cached)&&cached.revision==assembly.revision)return cached.json;
            var copy=JsonUtility.FromJson<ShipAssembly>(JsonUtility.ToJson(assembly));foreach(var module in copy.modules)module.paid.Clear();foreach(var wire in copy.wires)wire.paid.Clear();
            string json=JsonUtility.ToJson(copy);publicShipLayouts[ship.entity]=(assembly.revision,json);return json;
        }
        void ReceiveShipStates(SurvivalSnapshot snapshot){
            ReceiveStations(snapshot);
            foreach(long id in snapshot.removed){if(id%8==5){clientShips.Remove(id/8);entityPositions.Remove(id);shipPresentationRotations.Remove(id/8);}if(id%8==6)clientShipAssemblies.Remove(id/8);}
            foreach(var entity in snapshot.changed){if(entity.kind=="ship"||entity.kind=="ship-proxy")clientShips[entity.id/8]=JsonUtility.FromJson<SurvivalShipState>(entity.data);else if(entity.kind=="ship-layout")clientShipAssemblies[entity.id/8]=JsonUtility.FromJson<ShipAssembly>(entity.data);}
            if(snapshot.pilotedShip==null&&snapshot.motion.ship!=null&&latest?.motion.ship?.entity==snapshot.motion.ship.entity)snapshot.pilotedShip=latest.pilotedShip;
            if(snapshot.pilotedShip!=null&&snapshot.motion.ship!=null)clientShipAssemblies[snapshot.motion.ship.entity]=snapshot.pilotedShip;
        }
        static Quaternion ShipRotation(SurvivalShipState state)=>new Quaternion(state.rotationX,state.rotationY,state.rotationZ,state.rotationW).normalized;
        FramePose ClientShipEntry(SurvivalShipState ship)=>ship.landed&&clientShipAssemblies.TryGetValue(ship.entity,out var layout)?Interior(ship,layout).RampEntry(ship):ShipEntry(ship);
        static FramePose ShipEntry(SurvivalShipState ship){var p=SurvivalVehicleSimulation.Position(ship.pose)+Double3.Rotate(ShipRotation(ship),new Double3(0,-8,-52));return new FramePose{body=ship.pose.body,x=p.x,y=p.y,z=p.z};}
        ShipSimulation GetShipSimulation(SurvivalShipState state,ShipAssembly modules,PlanetDefinition definition){
            if(shipSimulations.TryGetValue(state.entity,out var previous)&&previous.body!=definition.bodyId){previous.ship.Dispose();shipSimulations.Remove(state.entity);}
            if(!shipSimulations.TryGetValue(state.entity,out var simulation)){
                simulation=new ShipSimulation{body=definition.bodyId,current=state};var created=simulation;
                simulation.ship=new PlanetSpacecraft(definition,modules,(from,to,q)=>ShipPathClear(created.current.entity,created.current.pose.body,from,to,created.current.collisionRadius,created.queryTime,created.queryElapsed)&&(!created.current.recalling&&created.current.landingCamp==0||RecallPathClear(created.current,from,to,created.queryElapsed)),universe?.Galaxy,universe?.Bodies,(from,to,dt)=>universe==null?1:universe.SweepCelestials(state.pose.body,from,to,dt,out _),new PlanetShipSimulationLimits{assistSpeed=(float)Config.shipAssistSpeed,assistBoost=(float)Config.shipBoostSpeed,acceleration=(float)Config.shipAcceleration,boostAcceleration=(float)Config.shipBoostAcceleration,assistAcceleration=(float)Config.shipAssistAcceleration,assistBraking=(float)Config.shipAssistBraking,warpSpeed=Config.warpSpeed});shipSimulations.Add(state.entity,simulation);
            }
            simulation.current=state;simulation.ship.SetAssembly(modules);
            if(simulation.restored!=state.physics){simulation.ship.RestoreSimulation(JsonUtility.FromJson<PlanetShipSimulationSnapshot>(state.physics));simulation.restored=state.physics;}
            return simulation;
        }
        bool ShipPathClear(long moving,int body,Double3 from,Double3 to,double movingRadius=55,double? queryTime=null,float queryElapsed=0){
            if((IsServer?serverContacts:clientContacts).SweptBuildings(body,from,to,movingRadius)||StationSweep(body,from,to,movingRadius,queryTime,queryElapsed))return false;
            IEnumerable<IndustrialMachine> machines=IsServer?authority.World.machines:clientMachines.Values;
            foreach(var machine in machines){if(machine.pose.body!=body||machine.pose.frame!=0||machine.integratedLogistics)continue;
                if(SegmentDistance(from,to,SurvivalVehicleSimulation.Position(machine.pose))<machine.Definition.Radius+movingRadius)return false;
            }
            if(authority!=null)foreach(var camp in authority.World.camps)if(camp.pose.body==body)foreach(var shop in camp.shops)if(SegmentDistance(from,to,SurvivalCampGeneration.Point(shop.pose))<movingRadius+15)return false;
            if(authority==null)foreach(var camp in clientCamps.Values)if(camp.pose.body==body)foreach(var shop in camp.shops)if(SegmentDistance(from,to,SurvivalCampGeneration.Point(shop.pose))<movingRadius+15)return false;
            IEnumerable<SurvivalShipState> ships=IsServer?authority.World.multiplayer.ships:clientShips.Values;
            foreach(var ship in ships)if(ship.entity!=moving&&ship.pose.body==body&&SegmentDistance(from,to,SurvivalVehicleSimulation.Position(ship.pose))<movingRadius+ship.collisionRadius)return false;
            return true;
        }
        static double SegmentDistance(Double3 a,Double3 b,Double3 point){var d=b-a;double length=d.x*d.x+d.y*d.y+d.z*d.z;var p=point-a;double t=length>1e-12?Math.Max(0,Math.Min(1,(p.x*d.x+p.y*d.y+p.z*d.z)/length)):0;return (point-a-d*t).Magnitude;}
        static void CaptureShip(SurvivalShipState state,ShipSimulation simulation){
            var snapshot=simulation.ship.CaptureSimulation();state.physics=JsonUtility.ToJson(snapshot);simulation.restored=state.physics;
            var p=snapshot.pose.position;state.pose.x=p.x;state.pose.y=p.y;state.pose.z=p.z;var q=snapshot.pose.rotation;
            state.rotationX=q.x;state.rotationY=q.y;state.rotationZ=q.z;state.rotationW=q.w;state.velocityX=snapshot.pose.linearVelocity.x;state.velocityY=snapshot.pose.linearVelocity.y;state.velocityZ=snapshot.pose.linearVelocity.z;state.spinX=snapshot.pose.angularVelocity.x;state.spinY=snapshot.pose.angularVelocity.y;state.spinZ=snapshot.pose.angularVelocity.z;
            state.landed=snapshot.landed;state.autoFlight=snapshot.autoFlight;
            state.recalling=snapshot.recallActive;if(state.recalling||!string.IsNullOrEmpty(state.recallStatus))state.recallStatus=simulation.ship.LandingStatus;
        }
        void SimulateShip(SurvivalShipState state,ShipAssembly assembly,PlanetDefinition definition,SurvivalInput input,float dt,double? simulationTime=null){
            if(IsServer&&!shipTickBudget.TryAdvance(simulationStep,state.entity))return;
            if(state.dockedStation!=0){StepDockedShip(state,simulationTime??StationTime());return;}
            var simulation=GetShipSimulation(state,assembly,definition);simulation.queryTime=simulationTime??StationTime();simulation.queryElapsed=dt;var previous=simulation.ship.CaptureSimulation();if(state.landingCamp!=0&&simulation.ship.AutoFlightActive&&!simulation.ship.RecallPowered)simulation.ship.CancelRecall("PAD FLIGHT STOPPED · NO POWER");
            simulation.ship.StepSimulation(dt,new Vector3(input.x,input.z,input.y),new Vector3(Mathf.Clamp(-input.lookY*.035f/.12f,-1,1),Mathf.Clamp(input.lookX*.035f/.12f,-1,1),input.roll),input.brake,input.boost,input.toggleAssist,input.toggleGear,input.jump,input.cruise,input.matchOrbit,input.toggleLock,input.toggleOrbit);
            var next=simulation.ship.CaptureSimulation();if(!ShipPathClear(state.entity,state.pose.body,previous.pose.position,next.pose.position,state.collisionRadius,simulation.queryTime,dt)){
                previous.pose.linearVelocity=default;previous.angularVelocity=Vector3.zero;previous.autoFlight=previous.recallActive=false;simulation.ship.RestoreSimulation(previous);if(state.recalling)simulation.ship.CancelRecall("RECALL STOPPED · PATH BLOCKED");
            }
            CaptureShip(state,simulation);
        }
        static void FollowShip(SurvivalMotion motion,SurvivalShipState state,SurvivalInput input,bool pilot){
            if(motion.interior&&!pilot){motion.ship=state;motion.vehicle=null;motion.pose.body=state.pose.body;motion.pose.frame=state.entity;motion.sequence=input.sequence;return;}
            motion.interior=false;motion.ship=state;motion.vehicle=null;motion.pose=state.pose;motion.verticalSpeed=0;motion.grounded=state.landed;
            if(pilot){motion.pitch=motion.viewYaw=0;}else{motion.pitch=Mathf.Clamp(motion.pitch-input.lookY,-85,85);motion.viewYaw=Mathf.Clamp(motion.viewYaw+input.lookX,-100,100);}
            var forward=ShipRotation(state)*Vector3.forward;motion.forwardX=forward.x;motion.forwardY=forward.y;motion.forwardZ=forward.z;motion.sequence=input.sequence;
        }
        void EnsurePlayerShip(SurvivalPlayerRecord record){
            if(authority.World.Actor(record.actor).health<=0)return;
            if(authority.World.multiplayer.ships.Exists(s=>s.owner==record.actor))return;
            var actor=authority.World.Actor(record.actor);double plannedRadius=SurvivalShipAuthority.CollisionRadius(authority.World.Ship(actor.id)??ShipModules.Starter(actor.id));var surface=surfaces[actor.pose.body];var origin=SurvivalVehicleSimulation.Position(actor.pose);var up=PlanetPrecisionPolicy.Direction(origin);
            var east=Vector3.Cross(Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);Double3 location=default;Quaternion rotation=default;bool found=false;
            for(int i=0;i<256;i++){
                double angle=i*2.3999632297,radius=140+140*(i/16);var candidate=surface.ProjectToSurface(origin+new Double3(east)*(Math.Cos(angle)*radius)+new Double3(north)*(Math.Sin(angle)*radius),8);
                var normal=PlanetPrecisionPolicy.Direction(candidate);var q=Quaternion.LookRotation(Vector3.ProjectOnPlane(north,normal),normal);double highest=0,lowest=double.MaxValue;
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){var foot=candidate+Double3.Rotate(q,new Double3(x*26,0,z*50));double h=surface.Definition.radius+surface.SampleHeight(foot);highest=Math.Max(highest,h);lowest=Math.Min(lowest,h);}
                candidate=new Double3(normal)*(highest+8);if(authority.World.CampReserved(new FramePose{body=actor.pose.body,x=candidate.x,y=candidate.y,z=candidate.z},plannedRadius))continue;if(highest-lowest>5||lowest<surface.Definition.radius+surface.Definition.oceanHeight+.2||!ShipPathClear(-1,actor.pose.body,candidate,candidate,plannedRadius))continue;
                location=candidate;rotation=q;found=true;break;
            }
            if(!found){Debug.LogWarning("No clear flat ship berth near survivor "+record.actor+"; ship creation deferred");return;}
            var assembly=authority.World.Ship(actor.id);if(assembly==null){var result=authority.Execute(actor.id,new IndustrialCommand{sequence=actor.lastSequence+1,kind="ship-initialize"});if(!result.accepted)throw new InvalidOperationException(result.message);assembly=authority.World.Ship(actor.id);}
            var state=new SurvivalShipState{entity=assembly.id,owner=actor.id,collisionRadius=plannedRadius,pose=new FramePose{body=actor.pose.body,x=location.x,y=location.y,z=location.z}};
            var snapshot=new PlanetShipSimulationSnapshot{pose=new SpacePose{position=location,rotation=rotation},gear=true,landed=true,assist=true,heldClearance=88};
            state.physics=JsonUtility.ToJson(snapshot);state.rotationX=rotation.x;state.rotationY=rotation.y;state.rotationZ=rotation.z;state.rotationW=rotation.w;
            authority.World.multiplayer.ships.Add(state);if(record.initialSpawnPending){var entry=ShipEntry(state);var feet=surface.ProjectToSurface(SurvivalVehicleSimulation.Position(entry),.05);actor.pose=record.spawn=new FramePose{body=state.pose.body,x=feet.x,y=feet.y,z=feet.z};record.initialSpawnPending=false;}authority.Save();
        }
        void StepServerShips(float dt){
            foreach(var ship in authority.World.multiplayer.ships){
                // Detailed flight only near connected players. Persistent industrial
                // work and analytic celestial/station orbits have independent clocks.
                bool active=ship.recalling;foreach(var peer in peers.Values){var actor=authority.World.Actor(peer.record.actor);if(peer.record.attachedShip==ship.entity||actor.pose.Distance(ship.pose)<=Config.proxyRadius){active=true;break;}}
                if(!active&&ship.dockedStation==0)continue;
                if(ship.pilot==0&&(!ship.landed||ship.autoFlight))SimulateShip(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition,default,dt);
                TransitionShipRegion(ship);
                foreach(var player in authority.World.multiplayer.players)if(player.attachedShip==ship.entity)authority.World.Actor(player.actor).pose=AttachedActorPose(player,ship);
            }
            foreach(var peer in peers.Values)if(peer.record.attachedShip!=0){var ship=authority.World.multiplayer.ships.Find(s=>s.entity==peer.record.attachedShip);if(ship!=null){peer.motion.ship=ship;if(peer.motion.vehicle!=null)peer.motion.pose=peer.motion.vehicle.pose;else if(peer.motion.interior){peer.motion.pose.body=ship.pose.body;peer.motion.pose.frame=ship.entity;}else peer.motion.pose=ship.pose;}}
        }
        void DriveServerShip(Peer peer,SurvivalInput input,float dt){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==peer.record.attachedShip);
            if(ship==null){peer.record.attachedShip=peer.record.controlledShip=0;peer.motion.ship=null;peer.motion.epoch++;peer.input.Clear();return;}
            bool pilot=ship.pilot==peer.record.actor&&peer.record.controlledShip==ship.entity;
            if(!pilot&&peer.motion.interior){StepInterior(peer,ship,input,dt);return;}
            if(pilot){peer.motion.notice=null;if(ship.landed&&input.jump&&!SealForTakeoff(ship)){input.jump=false;peer.motion.notice="Clear the exterior hatch before takeoff";}SimulateShip(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition,input,dt);}
            FollowShip(peer.motion,ship,input,pilot);
        }
        string EnterPilot(Peer peer,long entity){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);if(ship==null)return "Ship unavailable";
            if(ship.owner!=peer.record.actor)return "Only the ship owner can pilot";
            if(!peer.motion.interior||peer.record.attachedShip!=entity||peer.motion.pose.Distance(new FramePose{body=ship.pose.body,frame=ship.entity,x=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).x,y=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).y,z=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).z})>3.5)return "Approach the pilot seat inside your ship";
            string error=SurvivalShipAuthority.Enter(authority.World,peer.record,entity,ShipEntry(ship));if(error!=null)return error;
            peer.motion.interior=peer.record.interiorWalking=false;FollowShip(peer.motion,ship,default,true);peer.motion.epoch++;peer.input.Clear();authority.World.Actor(peer.record.actor).pose=ActorPose(peer.motion);return null;
        }
        string LeavePilot(Peer peer){
            if(peer.record.controlledShip==0)return "No occupied pilot seat";
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==peer.record.controlledShip);
            if(ship==null||!ChooseInteriorFeet(ship,peer.record.actor,PilotExitLocal(authority.World.Ship(ship.owner)),out var feet))return "The pilot exit is blocked";
            SurvivalShipAuthority.Release(authority.World,peer.record);BeginInterior(peer,ship,feet);return null;
        }
        string ExitShip(Peer peer){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==peer.record.attachedShip);if(ship==null)return "Not aboard a ship";
            if(!ship.landed||ship.autoFlight)return "Land the ship before disembarking";
            var entry=ShipEntry(ship);var surface=surfaces[ship.pose.body];var feet=surface.ProjectToSurface(SurvivalVehicleSimulation.Position(entry),.05);
            var exit=new FramePose{body=entry.body,x=feet.x,y=feet.y,z=feet.z};
            foreach(var machine in authority.World.machines)if(!machine.integratedLogistics&&exit.Distance(machine.pose)<machine.Definition.Radius+.4)return "Boarding ramp is blocked";
            SurvivalShipAuthority.Release(authority.World,peer.record);peer.record.attachedShip=0;peer.record.shipSeat=-1;var heading=SurvivalMovement.Tangent(ShipRotation(ship)*SurvivalMovement.Forward(peer.motion),PlanetPrecisionPolicy.Direction(feet));peer.motion.forwardX=heading.x;peer.motion.forwardY=heading.y;peer.motion.forwardZ=heading.z;
            peer.record.interiorWalking=peer.motion.interior=false;peer.motion.ship=null;ResetWalking(peer);peer.motion.pose=exit;peer.motion.epoch++;peer.input.Clear();authority.World.Actor(peer.record.actor).pose=exit;return null;
        }
        void ResetPilotLeases(){foreach(var actor in authority.World.actors)actor.networkAbsent=true;foreach(var player in authority.World.multiplayer.players)SurvivalShipAuthority.Release(authority.World,player);foreach(var ship in authority.World.multiplayer.ships)ship.collisionRadius=SurvivalShipAuthority.CollisionRadius(authority.World.Ship(ship.owner));}
        void DisposeShipSimulations(){foreach(var simulation in shipSimulations.Values)simulation.ship.Dispose();shipSimulations.Clear();interiorModels.Clear();clientShips.Clear();clientShipAssemblies.Clear();publicShipLayouts.Clear();}
    }
}





