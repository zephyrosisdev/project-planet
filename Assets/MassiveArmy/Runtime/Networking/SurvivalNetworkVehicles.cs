using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly Dictionary<(long,int,long),SurvivalVehicleSimulation> vehicleSimulations=new Dictionary<(long,int,long),SurvivalVehicleSimulation>();
        readonly Dictionary<long,IndustrialMachine> clientMachines=new Dictionary<long,IndustrialMachine>();
        readonly SurvivalEntityTickBudget vehicleTickBudget=new SurvivalEntityTickBudget();
        FramePose ActorPose(SurvivalMotion motion){
            if(motion.interior&&motion.ship!=null)return SurvivalShipInteriorModel.World(motion.ship,motion.pose);
            var pose=motion.pose;Double3 offset=default;
            if(motion.vehicle!=null)offset=Double3.Rotate(SurvivalVehicleSimulation.Rotation(motion.vehicle),new Double3(0,1.3,1.1));
            else if(motion.ship!=null){var player=authority.World.multiplayer.players.Find(p=>p.actor==motion.actor);var seat=SurvivalShipAuthority.SeatFeet(authority.World.Ship(motion.ship.owner),player?.shipSeat??-1);offset=Double3.Rotate(ShipRotation(motion.ship),new Double3(seat.x,seat.y,seat.z));}
            pose.x+=offset.x;pose.y+=offset.y;pose.z+=offset.z;if(motion.vehicle!=null&&pose.frame!=0&&motion.ship!=null)return SurvivalShipInteriorModel.World(motion.ship,pose);return pose;
        }
        SurvivalVehicleSimulation VehicleSimulation(SurvivalVehicleState state,string definition,PlanetSurface surface){
            var key=(state.entity,state.pose.body,state.pose.frame);SurvivalContacts contacts=IsServer?serverContacts:clientContacts;
            if(state.pose.frame!=0){var ship=authority!=null?authority.World.multiplayer.ships.Find(s=>s.entity==state.pose.frame):predicted?.ship?.entity==state.pose.frame?predicted.ship:clientShips.GetValueOrDefault(state.pose.frame);var layout=authority!=null?authority.World.Ship(ship?.owner??0):latest?.pilotedShip;if(layout==null)clientShipAssemblies.TryGetValue(state.pose.frame,out layout);if(ship!=null&&layout!=null)contacts=Interior(ship,layout).contacts;}
            if(!vehicleSimulations.TryGetValue(key,out var simulation)){simulation=new SurvivalVehicleSimulation(definition,surface,contacts,state.pose.body,state.pose.frame!=0,Config);vehicleSimulations.Add(key,simulation);}simulation.SetContacts(contacts);return simulation;
        }
        static void FollowVehicle(SurvivalMotion motion,SurvivalVehicleState state,SurvivalInput input){
            motion.interior=false;motion.vehicle=state;motion.pose=state.pose;motion.verticalSpeed=0;motion.grounded=state.grounded;
            motion.viewYaw=Mathf.Clamp(motion.viewYaw+input.lookX,-100,100);motion.pitch=Mathf.Clamp(motion.pitch-input.lookY,-60,60);
            var forward=SurvivalVehicleSimulation.Rotation(state)*Vector3.forward;motion.forwardX=forward.x;motion.forwardY=forward.y;motion.forwardZ=forward.z;motion.sequence=input.sequence;
        }
        void PredictMotion(SurvivalMotion motion,SurvivalInput input,float dt){
            // A held/missing server input can advance its clock without advancing
            // the acknowledged sequence. Distance snaps require matching time.
            motion.tick++;if(universe!=null&&latest!=null)universe.Advance(latest.simulationSeconds+Math.Max(0,motion.tick-latest.tick)/(double)Config.tickRate);
            var definition=ClientSimulationDefinition;var surface=new PlanetSurface(definition);
            if(motion.ship!=null){var state=motion.ship;bool pilot=latest.vitals.controlledShip==state.entity;if(state.owner==ActorId&&latest.pilotedShip!=null)SimulateShip(state,latest.pilotedShip,definition,pilot?input:default,dt);else {state.pose.x+=state.velocityX*dt;state.pose.y+=state.velocityY*dt;state.pose.z+=state.velocityZ*dt;}if(motion.vehicle!=null){VehicleSimulation(motion.vehicle,motion.vehicleDefinition,surface).Step(motion.vehicle,input,dt,clientMachines.Values);FollowVehicle(motion,motion.vehicle,input);}else if(motion.interior)PredictInterior(motion,input,dt);else FollowShip(motion,state,input,pilot);}
            else if(motion.vehicle!=null){VehicleSimulation(motion.vehicle,motion.vehicleDefinition,surface).Step(motion.vehicle,input,dt,clientMachines.Values);FollowVehicle(motion,motion.vehicle,input);}
            else StepWalking(motion,input,surface,dt,latest.self.dose>=50?4:latest.self.dose>=25?6:Config.walkSpeed,false);
        }
        void DriveServer(Peer peer,SurvivalInput input,float dt){
            var state=authority.World.multiplayer.vehicles.Find(v=>v.entity==peer.record.controlledEntity&&v.driver==peer.record.actor);
            var machine=state==null?null:authority.World.Machine(state.entity);
            if(machine==null){SurvivalVehicleAuthority.Release(authority.World,peer.record);peer.motion.vehicle=null;peer.motion.epoch++;peer.input.Clear();return;}
            if(!vehicleTickBudget.TryAdvance(clock.Tick,state.entity))return;
            if(!authority.World.creativeMode&&machine.enabled&&machine.integrity>0&&machine.progress==0&&!input.brake&&(input.x!=0||input.y!=0)){
                var actor=authority.World.Actor(peer.record.actor);authority.Execute(actor.id,new IndustrialCommand{sequence=actor.lastSequence+1,kind="vehicle-fuel",target=machine.id});
            }
            state.maximumSpeed=(float)(machine.definition=="truck"?Config.vehicleSpeed:Config.workVehicleSpeed)*(1-.4f*(float)machine.input.Used/Math.Max(1,machine.input.capacity));
            state.fueled=machine.enabled&&machine.integrity>0&&(authority.World.creativeMode||machine.progress>0);
            VehicleSimulation(state,machine.definition,surfaces[state.pose.body]).Step(state,input,dt,authority.World.machines);
            machine.pose=state.pose;peer.motion.vehicleDefinition=machine.definition;FollowVehicle(peer.motion,state,input);
        }
        void StepUnoccupiedVehicles(float dt){
            foreach(var state in authority.World.multiplayer.vehicles){if(state.driver!=0)continue;var machine=authority.World.Machine(state.entity);if(machine==null||machine.haul?.active==true)continue;
                if(Math.Abs(state.velocityX)+Math.Abs(state.velocityY)+Math.Abs(state.velocityZ)<.001&&state.grounded)continue;
                if(!vehicleTickBudget.TryAdvance(clock.Tick,state.entity))continue;
                state.fueled=false;VehicleSimulation(state,machine.definition,surfaces[state.pose.body]).Step(state,new SurvivalInput{brake=true},dt,authority.World.machines);machine.pose=state.pose;
            }
        }
        string EnterDriver(Peer peer,long entity){
            string error=SurvivalVehicleAuthority.Enter(authority.World,peer.record,entity);if(error!=null)return error;
            var state=authority.World.multiplayer.vehicles.Find(v=>v.entity==entity);var machine=authority.World.Machine(entity);
            state.maximumSpeed=(float)(machine.definition=="truck"?Config.vehicleSpeed:Config.workVehicleSpeed);state.fueled=authority.World.creativeMode||machine.progress>0;
            VehicleSimulation(state,machine.definition,surfaces[state.pose.body]).Step(state,default,0,authority.World.machines);
            peer.motion.vehicleDefinition=machine.definition;peer.motion.viewYaw=peer.motion.pitch=0;FollowVehicle(peer.motion,state,default);peer.motion.epoch++;peer.input.Clear();authority.World.Actor(peer.record.actor).pose=ActorPose(peer.motion);return null;
        }
        bool FindDriverExit(long entity,long actor,out FramePose exit){
            exit=default;var state=authority.World.multiplayer.vehicles.Find(v=>v.entity==entity);if(state==null||!surfaces.TryGetValue(state.pose.body,out var surface))return false;
            if(state.pose.frame!=0)return FindShipDriverExit(state,actor,out exit);
            var rotation=SurvivalVehicleSimulation.Rotation(state);var origin=SurvivalVehicleSimulation.Position(state.pose);
            foreach(var offset in new[]{Vector3.right*6,Vector3.left*6,Vector3.back*8,Vector3.right*10,Vector3.left*10}){
                var feet=surface.ProjectToSurface(origin+Double3.Rotate(rotation,new Double3(offset)),.05);var up=PlanetPrecisionPolicy.Direction(feet);
                if(Vector3.Dot(surface.SampleNormal(up),up)<.55f)continue;
                var candidate=new FramePose{body=state.pose.body,x=feet.x,y=feet.y,z=feet.z};bool clear=true;
                foreach(var machine in authority.World.machines)if(!machine.integratedLogistics&&candidate.Distance(machine.pose)<machine.Definition.Radius+.4){clear=false;break;}
                if(clear)foreach(var other in authority.World.actors)if(!other.networkAbsent&&other.id!=actor&&candidate.Distance(other.pose)<.8){clear=false;break;}
                if(clear&&BuildingAuthority.Overlaps(candidate.body,feet,Quaternion.FromToRotation(Vector3.up,up),new[]{BuildSolid.Box(-.4f,.4f,-.4f,.4f,.02f,1.8f)},2))clear=false;
                if(clear){exit=candidate;return true;}
            }return false;
        }
        string ExitDriver(Peer peer){
            if(!FindDriverExit(peer.record.controlledEntity,peer.record.actor,out var exit))return "Vehicle exit is blocked; move to a clear area";
            string error=SurvivalVehicleAuthority.Exit(authority.World,peer.record,exit);if(error!=null)return error;
            if(exit.frame!=0){var ship=authority.World.multiplayer.ships.Find(s=>s.entity==exit.frame);BeginInterior(peer,ship,exit);}
            ResetWalking(peer);peer.motion.pose=exit;peer.motion.vehicle=null;peer.motion.vehicleDefinition=null;peer.motion.viewYaw=peer.motion.pitch=0;peer.motion.verticalSpeed=0;peer.motion.epoch++;peer.input.Clear();return null;
        }
        void ReleaseDriver(Peer peer){
            if(peer.record.controlledEntity==0)return;
            if(ExitDriver(peer)!=null){peer.record.pendingVehicleExit=peer.record.controlledEntity;SurvivalVehicleAuthority.Release(authority.World,peer.record);}
        }
        bool RestoreDriverExit(SurvivalPlayerRecord record){
            if(record.pendingVehicleExit==0)return true;
            if(!FindDriverExit(record.pendingVehicleExit,record.actor,out var exit))return false;
            var canonical=ResolveNetworkInventoryPose(exit);if(!canonical.HasValue)return false;authority.World.Actor(record.actor).pose=canonical.Value;if(exit.frame!=0){record.interiorWalking=true;record.interiorPose=exit;record.attachedShip=exit.frame;}record.pendingVehicleExit=0;return true;
        }
        void ResetDriverLeases(){
            foreach(var player in authority.World.multiplayer.players){if(player.controlledEntity!=0)player.pendingVehicleExit=player.controlledEntity;SurvivalVehicleAuthority.Release(authority.World,player);}
        }
        void ReceiveMachineStates(SurvivalSnapshot snapshot){
            foreach(long id in snapshot.removed)if(id%8==2)clientMachines.Remove(id/8);
            foreach(var entity in snapshot.changed)if(entity.kind=="machine")clientMachines[entity.id/8]=JsonUtility.FromJson<IndustrialMachine>(entity.data);
        }
        void DisposeVehicleSimulations(){foreach(var simulation in vehicleSimulations.Values)simulation.Dispose();vehicleSimulations.Clear();clientMachines.Clear();}
    }
}
