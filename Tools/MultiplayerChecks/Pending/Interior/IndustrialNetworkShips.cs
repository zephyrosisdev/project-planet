using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalShipState {
        public long entity,owner,pilot;public FramePose pose;
        public double velocityX,velocityY,velocityZ;
        public float rotationX,rotationY,rotationZ,rotationW=1;
        public bool landed=true,autoFlight,rearOpen,innerOpen;public string physics;
        public List<long> invitedActors=new List<long>();
        public double collisionRadius=55;
        public SurvivalShipState Copy(){var copy=(SurvivalShipState)MemberwiseClone();copy.invitedActors=new List<long>(invitedActors);return copy;}
    }
    public static class SurvivalShipAuthority {
        public const int PassengerCapacity=8;
        public static FramePose SeatFeet(ShipAssembly ship,int seat){
            if(seat<0){var cockpit=ship?.modules.Find(m=>m.definition=="cockpit");return cockpit==null?ShipModules.P(0,-2.7,30):ShipModules.Transform(cockpit.pose,ShipModules.P(0,-1.7,0));}
            if(seat>=PassengerCapacity)throw new ArgumentOutOfRangeException(nameof(seat));
            var root=ship?.modules.Find(m=>m.parent==0);if(root==null)return ShipModules.P(0,-4,-14);
            double x=(seat%4<2?8:16)*(seat%2==0?-1:1),z=seat<4?-10:0;double floor=-ShipModules.Definitions[root.definition].height*.5+.5;
            return ShipModules.Transform(root.pose,ShipModules.P(x,floor,z));
        }
        public static double CollisionRadius(ShipAssembly ship){
            double radius=55;if(ship==null)return radius;
            foreach(var module in ship.modules){var d=ShipModules.Definitions[module.definition];double offset=Math.Sqrt(module.pose.x*module.pose.x+module.pose.y*module.pose.y+module.pose.z*module.pose.z);radius=Math.Max(radius,offset+Math.Sqrt(d.width*d.width+d.height*d.height+d.depth*d.depth)*.5);}return radius;
        }
        public static string Invite(IndustrialWorld world,long owner,long entity,long target,bool grant){
            var ship=world.multiplayer.ships.Find(s=>s.entity==entity);
            if(ship==null||ship.owner!=owner)return "Only the ship owner can manage invitations";
            if(target==owner||world.multiplayer.players.Find(p=>p.actor==target)==null)return "Choose another survivor";
            if(grant){if(!ship.invitedActors.Contains(target))ship.invitedActors.Add(target);}else ship.invitedActors.Remove(target);
            return null;
        }
        public static string BoardPassenger(IndustrialWorld world,SurvivalPlayerRecord player,long entity,FramePose entry,ISet<long> connected=null){
            var actor=world.Actor(player.actor);var ship=world.multiplayer.ships.Find(s=>s.entity==entity);
            if(actor==null||actor.health<=0||ship==null)return "Ship unavailable";
            if(player.controlledEntity!=0||player.controlledShip!=0||player.attachedShip!=0)return "Leave the current seat first";
            if(!ship.invitedActors.Contains(actor.id))return "The ship owner must invite you aboard";
            if(!ship.landed||ship.autoFlight||actor.pose.Distance(entry)>12)return "Move within 12 m of the boarding ramp of the landed ship";
            int seat=FreePassengerSeat(world,entity,player.actor,connected);if(seat<0)return "Passenger berths are full";
            player.attachedShip=entity;player.shipSeat=seat;return null;
        }
        public static int FreePassengerSeat(IndustrialWorld world,long entity,long actor,ISet<long> connected=null){
            for(int seat=0;seat<PassengerCapacity;seat++){
                bool used=false;foreach(var player in world.multiplayer.players)if(player.actor!=actor&&player.attachedShip==entity&&player.shipSeat==seat&&(connected==null||connected.Contains(player.actor))){used=true;break;}
                if(!used)return seat;
            }return -1;
        }
        public static string Enter(IndustrialWorld world,SurvivalPlayerRecord player,long entity,FramePose entry){
            var actor=world.Actor(player.actor);var ship=world.multiplayer.ships.Find(s=>s.entity==entity);
            if(actor==null||actor.health<=0||ship==null)return "Ship unavailable";
            if(player.controlledEntity!=0||player.controlledShip!=0)return "Leave the current seat first";
            if(ship.owner!=actor.id)return "This ship belongs to another player";
            if(ship.pilot!=0)return "The pilot seat is occupied";
            if(player.attachedShip!=entity&&(!ship.landed||ship.autoFlight||actor.pose.Distance(entry)>12))return "Move within 12 m of the boarding ramp of your landed ship";
            var assembly=world.Ship(actor.id);assembly?.UpdatePower();
            if(assembly==null||assembly.PoweredCount("cockpit")==0)return "Connect power to the cockpit first";
            ship.pilot=actor.id;player.controlledShip=entity;player.attachedShip=entity;player.shipSeat=-1;return null;
        }
        public static void Release(IndustrialWorld world,SurvivalPlayerRecord player){
            foreach(var ship in world.multiplayer.ships)if(ship.pilot==player.actor)ship.pilot=0;player.controlledShip=0;
        }
    }
}
