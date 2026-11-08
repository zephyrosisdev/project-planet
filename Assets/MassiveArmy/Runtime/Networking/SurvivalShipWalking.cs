using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly Dictionary<long,SurvivalShipInteriorModel> interiorModels=new Dictionary<long,SurvivalShipInteriorModel>();
        SurvivalShipInteriorModel Interior(SurvivalShipState ship,ShipAssembly layout){
            if(layout==null)return null;if(!interiorModels.TryGetValue(ship.entity,out var model)||!model.Matches(layout,ship))model=interiorModels[ship.entity]=new SurvivalShipInteriorModel(layout,ship);
            var alive=model.liveMachineIds;alive.Clear();IEnumerable<IndustrialMachine> machines=authority!=null?authority.World.machines:clientMachines.Values;
            foreach(var machine in machines)if(machine.pose.frame==ship.entity&&!machine.integratedLogistics){alive.Add(machine.id);var stamp=new SurvivalInterestDelivery.Fingerprint{data=machine.definition,pose=machine.pose};if(!model.machineSignatures.TryGetValue(machine.id,out var prior)||!prior.Same(stamp)){PutMachineContact(model.contacts,machine);model.machineSignatures[machine.id]=stamp;}}
            foreach(long id in model.machineIds)if(!alive.Contains(id)){model.contacts.Remove(id*8+2);model.machineSignatures.Remove(id);}model.machineIds.Clear();foreach(long id in alive)model.machineIds.Add(id);return model;
        }
        static void ResetWalking(Peer peer){SurvivalMovement.ResetWalkingVelocity(peer.motion);peer.record.walkVelocityX=peer.record.walkVelocityY=peer.record.walkVelocityZ=0;}
        static bool FiniteInterior(FramePose pose)=>SurvivalServerConfig.Finite(pose.x)&&SurvivalServerConfig.Finite(pose.y)&&SurvivalServerConfig.Finite(pose.z)&&SurvivalServerConfig.Finite(pose.yaw);
        bool ClearInteriorFeet(SurvivalShipState ship,SurvivalShipInteriorModel model,long actor,FramePose feet){
            if(!FiniteInterior(feet)||!SurvivalShipInteriorModel.Contains(model.assembly,feet)||!model.contacts.ClearAt(ship.pose.body,SurvivalVehicleSimulation.Position(feet),Vector3.up))return false;
            foreach(var other in peers.Values)if(other.record.actor!=actor&&other.record.attachedShip==ship.entity&&authority.World.Actor(other.record.actor).health>0){
                var local=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(other.record.actor).pose);if(Math.Abs(local.y-feet.y)<1.8&&Math.Sqrt((local.x-feet.x)*(local.x-feet.x)+(local.z-feet.z)*(local.z-feet.z))<.75)return false;
            }return true;
        }
        bool ChooseInteriorFeet(SurvivalShipState ship,long actor,FramePose preferred,out FramePose result){
            result=default;var model=Interior(ship,authority.World.Ship(ship.owner));if(model==null||!FiniteInterior(preferred))return false;
            for(int i=0;i<49;i++){
                double angle=(i-1)*Math.PI/4,radius=i==0?0:1+(i-1)/8;var candidate=preferred;candidate.body=ship.pose.body;candidate.frame=ship.entity;candidate.x+=Math.Cos(angle)*radius;candidate.z+=Math.Sin(angle)*radius;
                var point=SurvivalVehicleSimulation.Position(candidate);
                if(!model.contacts.ProbeGround(ship.pose.body,point,Vector3.up,1,8,out var support))continue;
                candidate.y=support.position.y+.05;if(ClearInteriorFeet(ship,model,actor,candidate)){result=candidate;return true;}
            }return false;
        }
        void BeginInterior(Peer peer,SurvivalShipState ship,FramePose feet){
            feet.body=ship.pose.body;feet.frame=ship.entity;peer.record.attachedShip=ship.entity;peer.record.interiorWalking=true;peer.record.interiorPose=feet;
            peer.motion.ship=ship;peer.motion.vehicle=null;peer.motion.interior=true;peer.motion.pose=feet;peer.motion.verticalSpeed=0;peer.motion.grounded=true;peer.motion.pitch=peer.motion.viewYaw=0;
            var heading=Quaternion.Euler(0,feet.yaw,0)*Vector3.forward;peer.motion.forwardX=heading.x;peer.motion.forwardY=0;peer.motion.forwardZ=heading.z;
            ResetWalking(peer);peer.motion.epoch++;peer.input.Clear();authority.World.Actor(peer.record.actor).pose=SurvivalShipInteriorModel.World(ship,feet);
        }
        bool RestoreInterior(Peer peer,SurvivalShipState ship){
            var preferred=peer.record.interiorWalking&&peer.record.interiorPose.frame==ship.entity?peer.record.interiorPose:SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),peer.record.shipSeat);
            if(ship.owner==peer.record.actor&&!peer.record.interiorWalking)preferred=PilotExitLocal(authority.World.Ship(ship.owner));
            if(!ChooseInteriorFeet(ship,peer.record.actor,preferred,out var feet))return false;BeginInterior(peer,ship,feet);return true;
        }
        static FramePose PilotExitLocal(ShipAssembly layout){var cockpit=layout?.modules.Find(m=>m.definition=="cockpit");return cockpit==null?ShipModules.P(1.8,-4,27):ShipModules.Transform(cockpit.pose,ShipModules.P(1.8,-3,-3));}
        void StepInterior(Peer peer,SurvivalShipState ship,SurvivalInput input,float dt){
            var model=Interior(ship,authority.World.Ship(ship.owner));peer.motion.ship=ship;peer.motion.pose.body=ship.pose.body;peer.motion.pose.frame=ship.entity;
            var previous=peer.motion.pose;SurvivalShipInteriorModel.Step(peer.motion,input,dt,Config.walkSpeed,model,Config.walkAcceleration,Config.walkBraking);
            if(!SurvivalShipInteriorModel.Contains(model.assembly,peer.motion.pose)){peer.motion.pose=previous;peer.motion.verticalSpeed=0;peer.motion.grounded=true;peer.motion.notice="Use E to disembark when landed or docked";}else peer.motion.notice=null;
            peer.record.interiorPose=peer.motion.pose;
        }
        void PredictInterior(SurvivalMotion motion,SurvivalInput input,float dt){
            var layout=latest?.pilotedShip;if(layout==null&&motion.ship!=null)clientShipAssemblies.TryGetValue(motion.ship.entity,out layout);
            var model=Interior(motion.ship,layout);if(model==null){motion.sequence=input.sequence;return;}
            motion.pose.body=motion.ship.pose.body;motion.pose.frame=motion.ship.entity;SurvivalShipInteriorModel.Step(motion,input,dt,Config.walkSpeed,model,Config.walkAcceleration,Config.walkBraking);
        }
        bool ShipPressurized(Peer peer){
            if(peer.motion.ship==null)return false;var ship=peer.motion.ship;var layout=authority.World.Ship(ship.owner);var model=Interior(ship,layout);if(model==null)return false;
            var local=peer.motion.vehicle!=null&&peer.motion.vehicle.pose.frame==ship.entity?VehicleSeatLocal(peer.motion.vehicle):peer.motion.interior?peer.motion.pose:SurvivalShipAuthority.SeatFeet(layout,peer.record.shipSeat);return model.Pressurized(local);
        }
        string ToggleShipDoor(Peer peer,long entity,int door){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);if(ship==null||door<0||door>1)return "Hatch unavailable";
            if(ship.owner!=peer.record.actor&&!ship.invitedActors.Contains(peer.record.actor)&&peer.record.attachedShip!=ship.entity)return "The ship owner must invite you aboard";
            if(peer.record.attachedShip!=0&&peer.record.attachedShip!=ship.entity)return "This hatch belongs to another ship";
            var local=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(peer.record.actor).pose);local.y+=1.65;float z=door==0?-38.25f:-31;double best=double.PositiveInfinity;
            foreach(var control in new[]{ShipModules.P(-6.55,-2.55,z-(door==0?1.2:.4)),ShipModules.P(-6.55,-2.55,z+.4),ShipModules.P(0,.5,z)}){var point=local;point.frame=0;point.body=0;var button=control;button.frame=0;button.body=0;best=Math.Min(best,point.Distance(button));}
            if(best>3.5)return "Move within 3.5 m of the hatch control";
            bool open=door==0?ship.rearOpen:ship.innerOpen;
            if(door==0&&!open&&!ship.landed)return "Land or dock before opening the exterior hatch";
            if(open){
                foreach(var other in peers.Values)if(other.record.attachedShip==ship.entity&&authority.World.Actor(other.record.actor).health>0){var at=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(other.record.actor).pose);if(Math.Abs(at.x)<6.4&&Math.Abs(at.z-z)<.7&&at.y>-5.8&&at.y<5.4)return "Clear the hatch before closing it";}
                foreach(var machine in authority.World.machines)if(machine.pose.frame==ship.entity&&Math.Abs(machine.pose.z-z)<machine.Definition.Radius+.5&&Math.Abs(machine.pose.x)<6+machine.Definition.Radius)return "Move the vehicle or machinery clear of the hatch";
            }
            if(door==0)ship.rearOpen=!open;else ship.innerOpen=!open;interiorModels.Remove(ship.entity);return null;
        }
        bool SealForTakeoff(SurvivalShipState ship){
            if(!ship.rearOpen)return true;
            foreach(var peer in peers.Values)if(peer.record.attachedShip==ship.entity&&authority.World.Actor(peer.record.actor).health>0){var at=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(peer.record.actor).pose);if(Math.Abs(at.x)<6.4&&Math.Abs(at.z+38.25)<.7&&at.y>-5.8&&at.y<5.4)return false;}
            foreach(var machine in authority.World.machines)if(machine.pose.frame==ship.entity&&Math.Abs(machine.pose.z+38.25)<machine.Definition.Radius+.5&&Math.Abs(machine.pose.x)<6+machine.Definition.Radius)return false;
            ship.rearOpen=false;interiorModels.Remove(ship.entity);return true;
        }
        SurvivalAction InteriorInteraction(){
            if(predicted?.interior!=true||predicted.ship==null)return null;
            var eye=SurvivalVehicleSimulation.Position(predicted.pose)+new Double3(0,1.65,0);var forward=SurvivalMovement.Forward(predicted);float pitch=predicted.pitch*Mathf.Deg2Rad;var look=forward*Mathf.Cos(pitch)-Vector3.up*Mathf.Sin(pitch);double best=3.5;SurvivalAction action=null;
            void Consider(FramePose point,SurvivalAction candidate){var delta=SurvivalVehicleSimulation.Position(point)-eye;double distance=delta.Magnitude;if(distance>=best||distance<.01||Vector3.Dot(look,delta.Float/(float)distance)<.5f)return;best=distance;action=candidate;}
            for(int door=0;door<2;door++){double z=door==0?-38.25:-31;foreach(var point in new[]{ShipModules.P(-6.55,-2.55,z-(door==0?1.2:.4)),ShipModules.P(-6.55,-2.55,z+.4),ShipModules.P(0,.5,z)})Consider(point,new SurvivalAction{kind="ship-door",entity=predicted.ship.entity,slot=door});}
            if(predicted.ship.owner==ActorId&&clientShipAssemblies.TryGetValue(predicted.ship.entity,out var layout)){var point=SurvivalShipAuthority.SeatFeet(layout,-1);point.y+=1.2;Consider(point,new SurvivalAction{kind="enter-pilot",entity=predicted.ship.entity});}return action;
        }
        FramePose AttachedActorPose(SurvivalPlayerRecord player,SurvivalShipState ship){
            var vehicle=player.controlledEntity==0?null:authority.World.multiplayer.vehicles.Find(v=>v.entity==player.controlledEntity&&v.pose.frame==ship.entity);
            var local=vehicle!=null?VehicleSeatLocal(vehicle):player.interiorWalking?player.interiorPose:SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),player.shipSeat);return SurvivalShipInteriorModel.World(ship,local);
        }
        double CorrectionDistance(SurvivalMotion motion)=>motion.vehicle!=null?Config.vehicleSnap:motion.interior?Config.walkSnap:motion.ship!=null?Config.shipSnap:Config.walkSnap;
    }
}
