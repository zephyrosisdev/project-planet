using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        double ClientDistance(FramePose pose){
            if(pose.frame==0)return latest.self.pose.Distance(pose);
            SurvivalShipState ship=predicted?.ship?.entity==pose.frame?predicted.ship:null;if(ship==null)clientShips.TryGetValue(pose.frame,out ship);
            return ship==null?double.PositiveInfinity:latest.self.pose.Distance(SurvivalShipInteriorModel.World(ship,pose));
        }
        string ValidateShipMachine(IndustrialActor actor,IndustrialCommand command){
            if(command.kind!="place"||command.attachmentModule!=0||!IndustrialCatalog.Machines.TryGetValue(command.definition??"",out var definition))return "Choose a supported ship machine";
            var pose=command.pose;var ship=authority.World.multiplayer.ships.Find(s=>s.entity==pose.frame&&s.owner==actor.id&&s.pose.body==pose.body);var player=authority.World.multiplayer.players.Find(p=>p.actor==actor.id);
            if(ship==null||player?.interiorWalking!=true||player.attachedShip!=ship.entity||!FiniteInterior(pose)||player.interiorPose.Distance(pose)>12)return "Build within reach inside your own ship";
            double height=IndustrialCablePath.Height(definition.id);if(definition.Width>20||definition.Depth>20||height>10)return "This machine does not fit the ship deck";
            var model=Interior(ship,authority.World.Ship(ship.owner));var position=SurvivalVehicleSimulation.Position(pose);
            if(!model.contacts.ProbeGround(pose.body,position,Vector3.up,.3f,.3f,out var floor)||Math.Abs(position.y-floor.position.y-.05)>.3)return "Choose supported ship deck";
            pose.y=floor.position.y+.05;pose.yaw=Mathf.Repeat((float)Math.Round(pose.yaw/90)*90,360);var rotation=Quaternion.Euler(0,pose.yaw,0);
            // Test the complete footprint and height against CPU hull/fitting contacts.
            int width=Math.Max(1,(int)Math.Ceiling(definition.Width)),depth=Math.Max(1,(int)Math.Ceiling(definition.Depth));
            for(int x=0;x<=width;x++)for(int z=0;z<=depth;z++){
                var offset=rotation*new Vector3((float)(definition.Width*(x/(double)width-.5)),0,(float)(definition.Depth*(z/(double)depth-.5)));var at=new Double3(pose.x+offset.x,pose.y,pose.z+offset.z);
                if(!model.contacts.ProbeGround(pose.body,at,Vector3.up,.15f,.15f,out var support)||Math.Abs(support.position.y-floor.position.y)>.1)return "The whole machine needs level deck support";
                for(int level=0;level<=Math.Ceiling(Math.Max(0,height-1.7));level++)if(!model.contacts.ClearAt(pose.body,at+new Double3(0,Math.Min(level,Math.Max(0,height-1.7)),0),Vector3.up))return "Machine intersects the hull, fittings or other machinery";
            }
            foreach(var peer in peers.Values)if(peer.record.attachedShip==ship.entity&&authority.World.Actor(peer.record.actor).health>0){var feet=SurvivalShipInteriorModel.Local(ship,authority.World.Actor(peer.record.actor).pose);var delta=Quaternion.Inverse(rotation)*new Vector3((float)(feet.x-pose.x),(float)(feet.y-pose.y),(float)(feet.z-pose.z));if(Math.Abs(delta.x)<definition.Width*.5+.4&&Math.Abs(delta.z)<definition.Depth*.5+.4&&delta.y<height+.2&&delta.y> -1.8)return "Keep machine placement clear of players";}
            command.pose=pose;return null;
        }
        string ShipFactoryObstruction(IndustrialCommand command,FramePose point,double clearance){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==point.frame&&s.pose.body==point.body);if(ship==null)return "Ship routing frame unavailable";
            var model=Interior(ship,authority.World.Ship(ship.owner));var position=SurvivalVehicleSimulation.Position(point);
            if(!SurvivalShipInteriorModel.Contains(model.assembly,point))return "Keep ship routing inside the hull";
            long ignore=0,ignoreOther=0;if(command.kind=="belt"){var from=authority.World.Machine(command.source);var to=authority.World.Machine(command.target);if(from!=null&&point.Distance(IndustrialConveyorPath.Pin(from,true))<1.1)ignore=from.id*8+2;if(to!=null&&point.Distance(IndustrialConveyorPath.Pin(to,false))<1.1)ignoreOther=to.id*8+2;}
            foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})if(model.contacts.RayBlocked(point.body,position-new Double3(axis)*clearance,position+new Double3(axis)*clearance,ignore,ignoreOther))return "Route intersects the hull or machinery; add routing points";
            return null;
        }
    }
}
