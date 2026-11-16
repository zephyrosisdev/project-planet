using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly Dictionary<long,SurvivalShipInteriorModel> interiorModels=new Dictionary<long,SurvivalShipInteriorModel>();
        SurvivalShipInteriorModel Interior(SurvivalShipState ship,ShipAssembly layout){
            if(layout==null)return null;if(!interiorModels.TryGetValue(ship.entity,out var model)||!model.Matches(layout,ship))model=interiorModels[ship.entity]=new SurvivalShipInteriorModel(layout,ship,authority!=null&&surfaces.TryGetValue(ship.pose.body,out var surface)?surface:ClientSimulationDefinition!=null?new PlanetSurface(ClientSimulationDefinition):null);
            var alive=model.liveMachineIds;alive.Clear();IEnumerable<IndustrialMachine> machines=authority!=null?authority.World.machines:clientMachines.Values;
            foreach(var machine in machines)if(machine.pose.frame==ship.entity&&!machine.integratedLogistics){alive.Add(machine.id);var stamp=new SurvivalInterestDelivery.Fingerprint{data=machine.definition,pose=machine.pose};if(!model.machineSignatures.TryGetValue(machine.id,out var prior)||!prior.Same(stamp)){PutMachineContact(model.contacts,machine);model.machineSignatures[machine.id]=stamp;}}
            foreach(long id in model.machineIds)if(!alive.Contains(id)){model.contacts.Remove(id*8+2);model.machineSignatures.Remove(id);}model.machineIds.Clear();foreach(long id in alive)model.machineIds.Add(id);return model;
        }
        static void ResetWalking(Peer peer){SurvivalMovement.ResetWalkingVelocity(peer.motion);peer.record.walkVelocityX=peer.record.walkVelocityY=peer.record.walkVelocityZ=0;}
        static bool FiniteInterior(FramePose pose)=>SurvivalServerConfig.Finite(pose.x)&&SurvivalServerConfig.Finite(pose.y)&&SurvivalServerConfig.Finite(pose.z)&&SurvivalServerConfig.Finite(pose.yaw);
        bool ClearInteriorFeet(SurvivalShipState ship,SurvivalShipInteriorModel model,long actor,FramePose feet){
            if(!FiniteInterior(feet)||!model.ContainsWalking(feet)||!model.contacts.ClearAt(ship.pose.body,SurvivalVehicleSimulation.Position(feet),Vector3.up))return false;
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
                candidate.y=support.position.y+.05+.35*Math.Sqrt(support.normal.x*support.normal.x+support.normal.z*support.normal.z)/Math.Max(.1,support.normal.y);if(ClearInteriorFeet(ship,model,actor,candidate)){result=candidate;return true;}
            }return false;
        }
        void BeginInterior(Peer peer,SurvivalShipState ship,FramePose feet){
            ClearEva(peer.motion);peer.record.eva=false;
            feet.body=ship.pose.body;feet.frame=ship.entity;peer.record.attachedShip=ship.entity;peer.record.interiorWalking=true;peer.record.interiorPose=feet;
            peer.motion.ship=ship;peer.motion.vehicle=null;peer.motion.interior=true;peer.motion.pose=feet;peer.motion.verticalSpeed=0;peer.motion.grounded=true;peer.motion.pitch=peer.motion.viewYaw=0;
            var heading=Quaternion.Euler(0,feet.yaw,0)*Vector3.forward;peer.motion.forwardX=heading.x;peer.motion.forwardY=0;peer.motion.forwardZ=heading.z;
            ResetWalking(peer);peer.motion.epoch++;peer.input.Clear();authority.World.Actor(peer.record.actor).pose=SurvivalShipInteriorModel.World(ship,feet);
        }
        // Exterior walkers must collide with the ramp before their authoritative
        // parent-frame transition arrives. Render colliders do not drive NGO movement.
        void ResolveExteriorRamps(SurvivalMotion motion,FramePose previous,bool wasGrounded,bool server){
            IEnumerable<SurvivalShipState> ships=server?authority.World.multiplayer.ships:clientShips.Values;
            foreach(var ship in ships){
                if(!ship.landed||ship.autoFlight||ship.pose.body!=motion.pose.body||motion.pose.Distance(ship.pose)>96)continue;
                var layout=server?authority.World.Ship(ship.owner):clientShipAssemblies.GetValueOrDefault(ship.entity);if(layout==null)continue;
                var model=Interior(ship,layout);var local=SurvivalShipInteriorModel.Local(ship,motion.pose);var from=SurvivalShipInteriorModel.Local(ship,previous);
                if(!model.NearRampSupport(local)&&!model.NearRampSupport(from))continue;
                var inverse=Quaternion.Inverse(ShipRotation(ship));var sample=motion.Copy();sample.pose=local;
                var heading=inverse*SurvivalMovement.Forward(motion);sample.forwardX=heading.x;sample.forwardY=heading.y;sample.forwardZ=heading.z;
                model.contacts.Resolve(sample,from,wasGrounded,Vector3.up);
                motion.pose=SurvivalShipInteriorModel.World(ship,sample.pose);motion.grounded=sample.grounded;motion.verticalSpeed=sample.verticalSpeed;
            }
        }
        void TryWalkAboard(Peer peer){
            if(peer.record.attachedShip!=0||peer.motion.vehicle!=null)return;
            var actor=authority.World.Actor(peer.record.actor);
            foreach(var ship in authority.World.multiplayer.ships){if(ship.pose.body!=actor.pose.body||actor.pose.Distance(ship.pose)>96)continue;
                var model=Interior(ship,authority.World.Ship(ship.owner));var local=SurvivalShipInteriorModel.Local(ship,actor.pose);
                if(ship.landed?model.AtRampFoot(local):SpaceBoarding(peer,ship,local)){peer.motion.notice=BoardPassenger(peer,ship.entity);return;}
            }
        }
        void LeaveRamp(Peer peer,SurvivalShipState ship){
            var pose=SurvivalShipInteriorModel.World(ship,peer.motion.pose);var forward=ShipRotation(ship)*SurvivalMovement.Forward(peer.motion);
            SurvivalShipAuthority.Release(authority.World,peer.record);peer.record.attachedShip=0;peer.record.shipSeat=-1;peer.record.interiorWalking=peer.motion.interior=false;peer.motion.ship=null;peer.motion.pose=pose;peer.motion.epoch++;peer.input.Clear();ResetWalking(peer);
            var heading=SurvivalMovement.Tangent(forward,PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(pose)));peer.motion.forwardX=heading.x;peer.motion.forwardY=heading.y;peer.motion.forwardZ=heading.z;authority.World.Actor(peer.record.actor).pose=pose;
        }
        SurvivalAction ExteriorShipInteraction(){
            if(predicted==null||predicted.ship!=null)return null;
            var eye=SurvivalVehicleSimulation.Position(predicted.pose)+new Double3(PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(predicted.pose)))*1.65;
            var forward=SurvivalMovement.Forward(predicted);var up=PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(predicted.pose));var look=Quaternion.AngleAxis(predicted.pitch,Vector3.Cross(up,forward))*forward;
            double best=3.5;SurvivalAction action=null;
            foreach(var ship in clientShips.Values){if(ship.pose.body!=predicted.pose.body)continue;for(int door=0;door<2;door++){double z=door==0?-38.25:-31;foreach(var button in new[]{ShipModules.P(-6.55,-2.55,z-(door==0?1.2:.4)),ShipModules.P(-6.55,-2.55,z+.4)}){var point=SurvivalShipInteriorModel.World(ship,button);var delta=SurvivalVehicleSimulation.Position(point)-eye;double distance=delta.Magnitude;if(distance<best&&distance>.01&&Vector3.Dot(look,delta.Float/(float)distance)>.5){best=distance;action=new SurvivalAction{kind="ship-door",entity=ship.entity,slot=door};}}}}
            return action;
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
            if(ExitingSpaceDoor(ship,previous,peer.motion.pose)){pendingEvaExits[peer.record.actor]=peer;peer.record.interiorPose=peer.motion.pose;return;}
            if(!model.ContainsWalking(peer.motion.pose)){if(model.rampActive&&peer.motion.pose.z<-50&&Math.Abs(peer.motion.pose.x)<6.5){LeaveRamp(peer,ship);return;}peer.motion.pose=previous;peer.motion.verticalSpeed=0;peer.motion.grounded=true;peer.motion.notice="Use the boarding ramp when landed";}else peer.motion.notice=null;
            peer.record.interiorPose=peer.motion.pose;
        }
        void PredictInterior(SurvivalMotion motion,SurvivalInput input,float dt){
            var layout=latest?.pilotedShip;if(layout==null&&motion.ship!=null)clientShipAssemblies.TryGetValue(motion.ship.entity,out layout);
            var model=Interior(motion.ship,layout);if(model==null){motion.sequence=input.sequence;return;}
            motion.pose.body=motion.ship.pose.body;motion.pose.frame=motion.ship.entity;var previous=motion.pose;var ship=motion.ship;SurvivalShipInteriorModel.Step(motion,input,dt,Config.walkSpeed,model,Config.walkAcceleration,Config.walkBraking);if(ExitingSpaceDoor(ship,previous,motion.pose))DetachEva(motion,ship);
        }
        bool ShipPressurized(Peer peer){
            if(peer.motion.ship==null)return false;var ship=peer.motion.ship;var layout=authority.World.Ship(ship.owner);var model=Interior(ship,layout);if(model==null)return false;
            var local=peer.motion.vehicle!=null&&peer.motion.vehicle.pose.frame==ship.entity?VehicleSeatLocal(peer.motion.vehicle):peer.motion.interior?peer.motion.pose:SurvivalShipAuthority.SeatFeet(layout,peer.record.shipSeat);return model.Pressurized(local);
        }
        string ToggleShipDoor(Peer peer,long entity,int door){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);if(ship==null||door<0||door>1)return "Hatch unavailable";
            if(peer.record.attachedShip!=ship.entity){string access=SurvivalShipAuthority.BoardingPermission(authority.World,peer.record.actor,ship);if(access!=null)return access;}
            if(peer.record.attachedShip!=0&&peer.record.attachedShip!=ship.entity)return "This hatch belongs to another ship";
            var local=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(peer.record.actor).pose);local.y+=1.65;float z=door==0?-38.25f:-31;double best=double.PositiveInfinity;
            foreach(var control in new[]{ShipModules.P(-6.55,-2.55,z-(door==0?1.2:.4)),ShipModules.P(-6.55,-2.55,z+.4),ShipModules.P(0,.5,z)}){var point=local;point.frame=0;point.body=0;var button=control;button.frame=0;button.body=0;best=Math.Min(best,point.Distance(button));}
            if(best>3.5)return "Move within 3.5 m of the hatch control";
            bool open=door==0?ship.rearOpen:ship.innerOpen;
            if(open){
                foreach(var other in peers.Values)if(other.record.attachedShip==ship.entity&&authority.World.Actor(other.record.actor).health>0){var at=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(other.record.actor).pose);if(Math.Abs(at.x)<6.4&&Math.Abs(at.z-z)<.7&&at.y>-5.8&&at.y<5.4)return "Clear the hatch before closing it";}
                foreach(var machine in authority.World.machines)if(machine.pose.frame==ship.entity&&Math.Abs(machine.pose.z-z)<machine.Definition.Radius+.5&&Math.Abs(machine.pose.x)<6+machine.Definition.Radius)return "Move the vehicle or machinery clear of the hatch";
            }
            if(door==0)ship.rearOpen=!open;else ship.innerOpen=!open;interiorModels.Remove(ship.entity);return null;
        }
        bool SealForTakeoff(SurvivalShipState ship){
            foreach(var peer in peers.Values)if(peer.record.attachedShip==ship.entity&&peer.record.interiorWalking&&authority.World.Actor(peer.record.actor).health>0){var at=peer.motion.pose;if(at.z< -37.5&&at.z>=-53&&Math.Abs(at.x)<6.4)return false;}
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

