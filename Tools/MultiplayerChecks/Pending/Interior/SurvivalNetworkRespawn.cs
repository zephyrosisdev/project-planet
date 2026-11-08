using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        Vector2 respawnScroll;long renamingRespawn;string respawnName="";
        bool ClearRespawnFeet(FramePose pose,long actor){
            if(!surfaces.TryGetValue(pose.body,out var surface)||pose.frame!=0)return false;
            var p=SurvivalVehicleSimulation.Position(pose);var up=PlanetPrecisionPolicy.Direction(p);
            if(surface.SampleHeight(up)<=surface.Definition.oceanHeight+.2||Vector3.Dot(surface.SampleNormal(up),up)<.8f)return false;
            if(BuildingAuthority.Overlaps(pose.body,p,Quaternion.FromToRotation(Vector3.up,up),new[]{BuildSolid.Box(-.4f,.4f,-.4f,.4f,.02f,1.8f)},2))return false;
            foreach(var machine in authority.World.machines)if(!machine.integratedLogistics&&pose.Distance(machine.pose)<machine.Definition.Radius+.5)return false;
            foreach(var ship in authority.World.multiplayer.ships)if(pose.Distance(ship.pose)<ship.collisionRadius+1)return false;
            foreach(var other in authority.World.actors)if(other.id!=actor&&other.health>0&&pose.Distance(other.pose)<1)return false;
            return true;
        }
        string PlaceNetworkRespawn(Peer peer,SurvivalAction action){
            if(peer.record.attachedShip!=0||peer.record.controlledEntity!=0)return "Leave the vehicle before placing a respawn point";
            if(action.command==null||!IndustrialWorld.RespawnItem(action.item))return "Choose a sleeping bag or pod";
            var pose=action.command.pose;var actor=authority.World.Actor(peer.record.actor);
            if(!SurvivalServerConfig.Finite(pose.x)||!SurvivalServerConfig.Finite(pose.y)||!SurvivalServerConfig.Finite(pose.z)||!SurvivalServerConfig.Finite(pose.yaw)||pose.body!=actor.pose.body||pose.frame!=0||actor.pose.Distance(pose)>12)return "Choose nearby supported ground";
            var ground=surfaces[pose.body].ProjectToSurface(SurvivalVehicleSimulation.Position(pose),.05);
            if((ground-SurvivalVehicleSimulation.Position(pose)).Magnitude>.5)return "Choose supported ground";
            pose.x=ground.x;pose.y=ground.y;pose.z=ground.z;pose.yaw=Mathf.Repeat(pose.yaw,360);
            if(!ClearRespawnFeet(pose,actor.id))return "Respawn point needs dry, clear, level ground";
            return authority.PlaceRespawnPoint(actor.id,action.item,pose,pose);
        }
        string ManageNetworkRespawn(Peer peer,SurvivalAction action,bool rename){
            var point=authority.World.RespawnPoint(action.entity);
            if(point==null||point.pose.frame!=0)return "Respawn point unavailable";
            return authority.ManageRespawnPoint(peer.record.actor,point.id,point.pose,rename?action.item:null);
        }
        string RespawnNetworkPlayer(Peer peer,long pointId){
            var actor=authority.World.Actor(peer.record.actor);if(actor.health>0)return "Player is alive";
            var point=authority.World.RespawnPoint(pointId);
            if(pointId!=0&&(point==null||point.owner!=actor.id))return "Respawn point is no longer yours";
            var destination=pointId==0?peer.record.spawn:point.pose;
            if(destination.frame!=0||!surfaces.TryGetValue(destination.body,out var surface))return "Respawn region unavailable";
            bool found=false;FramePose exit=default;
            // Resolve terrain edits and occupancy on the server; never accept a client destination.
            var origin=SurvivalVehicleSimulation.Position(destination);var up=PlanetPrecisionPolicy.Direction(origin);var tangent=Vector3.Cross(up,Vector3.right).normalized;if(tangent.sqrMagnitude<.1f)tangent=Vector3.Cross(up,Vector3.forward).normalized;
            for(int i=0;i<65;i++){
                var offset=i==0?Vector3.zero:Quaternion.AngleAxis((i-1)*45,up)*tangent*(1+(i-1)/8)*2;
                var feet=surface.ProjectToSurface(origin+new Double3(offset),.05);var candidate=new FramePose{body=destination.body,x=feet.x,y=feet.y,z=feet.z,yaw=destination.yaw};
                if(ClearRespawnFeet(candidate,actor.id)){exit=candidate;found=true;break;}
            }
            if(!found)return "Respawn area is blocked; choose another point";
            string error=authority.UseRespawnPoint(actor.id,pointId,exit);if(error!=null)return error;
            SurvivalVehicleAuthority.Release(authority.World,peer.record);SurvivalShipAuthority.Release(authority.World,peer.record);
            peer.record.pendingVehicleExit=peer.record.attachedShip=0;peer.record.shipSeat=-1;peer.record.hunger=peer.record.thirst=peer.record.oxygen=100;
            peer.record.interiorWalking=peer.motion.interior=false;peer.motion.ship=null;peer.motion.vehicle=null;peer.motion.vehicleDefinition=null;peer.motion.pose=exit;peer.motion.pitch=peer.motion.viewYaw=0;peer.motion.verticalSpeed=0;peer.motion.grounded=true;peer.motion.epoch++;peer.input.Clear();return null;
        }
        void RespawnPointsGUI(bool dead){
            if(dead){GUILayout.Label("Choose a respawn location");if(GUILayout.Button("Home region"))Submit(new SurvivalAction{kind="respawn"});}
            else GUILayout.Label("YOUR RESPAWN POINTS");
            foreach(var point in latest.respawnPoints){
                double cooldown=point.RemainingSeconds(latest.industryTick);GUILayout.Label(point.name+(cooldown>0?" · ready in "+Math.Ceiling(cooldown)+" s":""));
                if(dead){GUI.enabled=cooldown<=0;if(GUILayout.Button("Respawn here"))Submit(new SurvivalAction{kind="respawn",entity=point.id});GUI.enabled=true;}
                else if(latest.self.pose.Distance(point.pose)<=12){
                    GUILayout.BeginHorizontal();if(GUILayout.Button("Rename")){renamingRespawn=point.id;respawnName=point.name;}if(GUILayout.Button("Pack"))Submit(new SurvivalAction{kind="respawn-pack",entity=point.id});GUILayout.EndHorizontal();
                    if(renamingRespawn==point.id){respawnName=GUILayout.TextField(respawnName,48);if(GUILayout.Button("Save name")){Submit(new SurvivalAction{kind="respawn-rename",entity=point.id,item=respawnName});renamingRespawn=0;}}
                }
            }
        }
        void DeadRespawnGUI(){
            var panel=new Rect(Screen.width/2-220,Screen.height/2-200,440,400);GUI.Box(panel,"YOU DIED");GUILayout.BeginArea(new Rect(panel.x+12,panel.y+30,panel.width-24,panel.height-44));respawnScroll=GUILayout.BeginScrollView(respawnScroll);RespawnPointsGUI(true);GUILayout.Space(12);if(GUILayout.Button("Disconnect"))_ = Disconnect();GUILayout.EndScrollView();GUILayout.EndArea();
        }
    }
}
