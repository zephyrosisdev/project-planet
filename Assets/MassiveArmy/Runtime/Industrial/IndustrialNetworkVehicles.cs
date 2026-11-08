using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalVehicleState {
        public long entity,driver;public FramePose pose;
        public double velocityX,velocityY,velocityZ;
        public float rotationX,rotationY,rotationZ,rotationW=1,yawVelocity,verticalVelocity,steering;
        public int motionState;public bool grounded=true,initialized;
        public float maximumSpeed;public bool fueled;
        public SurvivalVehicleState Copy()=>(SurvivalVehicleState)MemberwiseClone();
    }
    public static class SurvivalVehicleAuthority {
        public static string Enter(IndustrialWorld world,SurvivalPlayerRecord player,long entity){
            var actor=world.Actor(player.actor);var machine=world.Machine(entity);
            if(actor==null||actor.health<=0||machine==null||!machine.Definition.Vehicle)return "Vehicle unavailable";
            if(player.controlledEntity!=0||player.controlledShip!=0||player.attachedShip!=0&&(machine.pose.frame!=player.attachedShip||!player.interiorWalking))return "Leave the current seat first";
            if(machine.owner!=actor.id)return "This vehicle belongs to another player";
            if(world.InteractionDistance(actor.pose,machine.pose)>12)return "Move within 12 m of the vehicle";
            if(machine.integrity<=0||!machine.enabled)return "Repair and enable the vehicle first";
            if(machine.haul?.active==true)return "Stop autonomous collection before driving";
            var state=world.multiplayer.vehicles.Find(v=>v.entity==entity);
            if(state==null){state=new SurvivalVehicleState{entity=entity,pose=machine.pose};world.multiplayer.vehicles.Add(state);}
            if(state.driver!=0)return "The driver seat is occupied";
            state.pose=machine.pose;state.driver=actor.id;player.controlledEntity=entity;player.interiorWalking=false;return null;
        }
        public static string Exit(IndustrialWorld world,SurvivalPlayerRecord player,FramePose destination){
            var state=world.multiplayer.vehicles.Find(v=>v.entity==player.controlledEntity&&v.driver==player.actor);
            if(state==null)return "No occupied driver seat";
            if(!Finite(destination)||destination.frame!=state.pose.frame||destination.Distance(state.pose)>12)return "Vehicle exit is unavailable";
            var resolved=destination.frame==0?(FramePose?)destination:world.InventoryPoseResolver?.Invoke(destination);if(!resolved.HasValue)return "Vehicle exit frame unavailable";world.Actor(player.actor).pose=resolved.Value;if(destination.frame!=0){player.interiorWalking=true;player.interiorPose=destination;}state.driver=0;player.controlledEntity=0;return null;
        }
        public static void Release(IndustrialWorld world,SurvivalPlayerRecord player){
            foreach(var state in world.multiplayer.vehicles)if(state.driver==player.actor)state.driver=0;
            player.controlledEntity=0;
        }
        static bool Finite(FramePose p)=>SurvivalServerConfig.Finite(p.x)&&SurvivalServerConfig.Finite(p.y)&&SurvivalServerConfig.Finite(p.z)&&SurvivalServerConfig.Finite(p.yaw);
    }
    public sealed partial class IndustrialWorld {
        // Reapply driver-controlled transforms before replaying a factory tick. Autonomous
        // vehicle jobs retain their existing journalled simulation and cannot be driven.
        internal void RestoreNetworkVehiclePoses(){
            if(multiplayer?.vehicles==null)return;
            foreach(var vehicle in multiplayer.vehicles){var machine=Machine(vehicle.entity);if(machine!=null&&machine.haul?.active!=true)machine.pose=vehicle.pose;}
        }
    }
}
