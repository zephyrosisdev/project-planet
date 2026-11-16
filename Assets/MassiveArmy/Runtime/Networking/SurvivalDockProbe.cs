#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalDockSpec {public int version=1;public bool seeded;public string configPath,error;public long owner,passenger,ship,station;}
    internal sealed partial class SurvivalSessionProbe {
        SurvivalDockSpec dockSpec;int dockStep;long dockAt;SurvivalAction dockRequest;SurvivalActionReply dockReply;bool dockSubscribed;FramePose dockReconnectPose;
        string DockMarker(string suffix)=>Path.Combine(Path.GetDirectoryName(path),"dock-"+suffix+".ack");
        bool StartDockProbe(){var source=Argument("-survival-probe-dock");if(source==null)return true;try{if(!Path.GetFullPath(source).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid dock fixture path");dockSpec=JsonUtility.FromJson<SurvivalDockSpec>(File.ReadAllText(source));if(dockSpec?.version!=1||!dockSpec.seeded)throw new InvalidOperationException("Dock fixture is not prepared");report.dockRequired=true;deadline+=180;return true;}catch(Exception error){_ = Finish(false,error.Message);return false;}}
        void DockReply(SurvivalActionReply reply){if(reply.sequence==dockRequest?.sequence)dockReply=reply;}
        void DockSend(string kind){dockReply=null;dockRequest=new SurvivalAction{kind=kind,entity=kind=="enter-pilot"?dockSpec.ship:dockSpec.station};session.Submit(dockRequest);}
        bool DockReplied(bool accepted){if(dockReply==null)return false;if(dockReply.accepted!=accepted){_ = Finish(false,"Unexpected docking action: "+dockReply.message);return false;}dockReply=null;return true;}
        SurvivalStationState DockStation(){foreach(var entity in session.Visible.Values)if(entity.kind=="station"){var station=JsonUtility.FromJson<SurvivalStationState>(entity.data);if(station.entity==dockSpec.station)return station;}return null;}
        SurvivalInput DockControls(SurvivalInput input){input.x=input.y=input.z=input.roll=input.lookX=input.lookY=0;input.jump=input.boost=input.brake=input.cruise=input.matchOrbit=false;if(report.actor!=dockSpec.owner||dockStep!=3||session.Predicted?.ship==null)return input;var station=DockStation();if(station==null)return input;var state=session.Predicted.ship;var berth=SurvivalNetworkSession.StationBerth(station,session.Latest.simulationSeconds+(session.Predicted.tick-session.Latest.tick)/(double)session.Config.tickRate);var position=SurvivalVehicleSimulation.Position(state.pose);var error=berth.position-position;double distance=error.Magnitude;var target=berth.linearVelocity+error*(Math.Min(3,distance*.5)/Math.Max(.001,distance));var correction=(target-new Double3(state.velocityX,state.velocityY,state.velocityZ))*2;var local=Double3.Rotate(Quaternion.Inverse(new Quaternion(state.rotationX,state.rotationY,state.rotationZ,state.rotationW)),correction).Float/(float)session.Config.shipAcceleration;input.x=Mathf.Clamp(local.x,-1,1);input.y=Mathf.Clamp(local.z,-1,1);input.z=Mathf.Clamp(local.y,-1,1);return input;}
        void UpdateDockProbe(){
            if(!session.PrivateStateReady||!report.remotePlayerSeen||DockStation()==null)return;
            if(!dockSubscribed){session.ActionReplied+=DockReply;dockSubscribed=true;}
            var motion=session.Latest.motion;report.dockStep=dockStep;
            if(report.actor!=dockSpec.owner){
                if(dockStep==0){DockSend("station-dock");dockStep=1;}
                else if(dockStep==1&&DockReplied(false)){report.dockPassengerDenied=true;File.WriteAllText(DockMarker("passenger-ready"),"Passenger authority rejection verified before pilot frame changes");dockStep=2;}
                else if(dockStep==2&&File.Exists(DockMarker("owner-done"))){report.dockMoored=motion.ship?.dockedStation==dockSpec.station;_ = Finish(report.dockMoored,report.dockMoored?null:"Passenger lost the moored parent");}return;
            }
            switch(dockStep){
                case 0:if(!motion.interior||motion.ship?.dockedStation!=dockSpec.station)return;report.dockMoored=true;DockSend("enter-pilot");dockStep=1;break;
                case 1:if(dockReply!=null&&!DockReplied(true))return;if(motion.interior||session.Latest.vitals.controlledShip!=dockSpec.ship||session.Predicted?.epoch!=motion.epoch||!File.Exists(DockMarker("passenger-ready")))return;DockSend("station-dock");dockStep=2;break;
                case 2:if(dockReply!=null&&!DockReplied(true))return;if(motion.ship?.dockedStation!=0)return;report.dockUndocked=true;dockStep=3;break;
                case 3:var berth=SurvivalNetworkSession.StationBerth(DockStation(),session.Latest.simulationSeconds);var ship=motion.ship;if(ship==null||(berth.position-SurvivalVehicleSimulation.Position(ship.pose)).Magnitude>6||(berth.linearVelocity-new Double3(ship.velocityX,ship.velocityY,ship.velocityZ)).Magnitude>2)return;DockSend("station-dock");dockStep=4;break;
                case 4:if(dockReply!=null&&!DockReplied(true))return;if(motion.ship?.dockedStation!=dockSpec.station)return;report.dockAcquired=true;dockAt=session.Latest.tick;dockStep=5;break;
                case 5:if(session.Latest.tick-dockAt<60)return;var expected=SurvivalNetworkSession.StationBerth(DockStation(),session.Latest.simulationSeconds);if((expected.position-SurvivalVehicleSimulation.Position(motion.ship.pose)).Magnitude>.01){_ = Finish(false,"Moored transport state diverged from its canonical orbit");return;}DockSend("leave-pilot");dockStep=6;break;
                case 6:if(dockReply!=null&&!DockReplied(true))return;if(!motion.interior)return;dockReconnectPose=motion.pose;phase=3;dockStep=7;_ = Reconnect();break;
                case 7:if(phase!=4||!motion.interior)return;if(motion.ship?.dockedStation!=dockSpec.station||motion.pose.Distance(dockReconnectPose)>.25){_ = Finish(false,"Reconnect lost the mooring or ship-local character");return;}report.reconnected=report.dockReconnect=true;File.WriteAllText(DockMarker("owner-done"),"moored reconnect verified");_ = Finish(true,null);break;
            }
        }
        void StopDockProbe(){if(dockSubscribed&&session!=null)session.ActionReplied-=DockReply;dockSubscribed=false;}
    }
#if UNITY_EDITOR || UNITY_SERVER
    public sealed partial class SurvivalNetworkSession {
        internal static bool SeedDockFixture(string path){SurvivalDockSpec spec=null;SurvivalUniverse model=null;try{
            if(!DiagnosticPath(path))throw new InvalidOperationException("Dock fixtures require disposable Live paths");spec=JsonUtility.FromJson<SurvivalDockSpec>(File.ReadAllText(path));if(spec?.version!=1||spec.seeded||!DiagnosticPath(spec.configPath))throw new InvalidOperationException("Invalid dock fixture");var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();if(!DiagnosticPath(config.savePath))throw new InvalidOperationException("Invalid dock save");var world=IndustrialAuthority.Load(config.savePath);var ship=world.multiplayer.ships.Find(s=>s.owner==spec.owner);if(ship==null||world.Actor(spec.passenger)==null)throw new InvalidOperationException("Missing fixture characters or ship");model=world.universe==null?SurvivalUniverse.Create(config.worldId,config.instanceId,config.seed,ServerDefinition(config.seed)):SurvivalUniverse.Restore(world.universe,config.worldId,config.seed);var definition=model.Definition(ship.pose.body);double radius=definition.radius+Math.Max(30000,Math.Max(definition.ReliefBound+6000,definition.atmosphereHeight+10000));var station=new SurvivalStationState{entity=world.AllocateId(),owner=spec.owner,body=ship.pose.body,radius=radius,mu=definition.gravitationalParameter,epoch=world.multiplayer.simulationSeconds,uy=1,vx=1,dockedShip=ship.entity};world.multiplayer.stations.Add(station);spec.station=station.entity;spec.ship=ship.entity;var berth=StationBerth(station,world.multiplayer.simulationSeconds);ship.dockedStation=station.entity;ship.pose=new FramePose{body=station.body,x=berth.position.x,y=berth.position.y,z=berth.position.z};ship.physics=JsonUtility.ToJson(new PlanetShipSimulationSnapshot{pose=berth,gear=true});ship.landed=ship.autoFlight=false;ship.pilot=0;ship.rearOpen=ship.innerOpen=false;if(!ship.invitedActors.Contains(spec.passenger))ship.invitedActors.Add(spec.passenger);
            foreach(long id in new[]{spec.owner,spec.passenger}){var player=world.multiplayer.players.Find(p=>p.actor==id);SurvivalShipAuthority.Release(world,player);SurvivalVehicleAuthority.Release(world,player);player.attachedShip=ship.entity;player.pendingVehicleExit=0;player.interiorWalking=true;player.interiorPose=new FramePose{body=station.body,frame=ship.entity,x=id==spec.owner?1.8:3.5,y=-3.95,z=id==spec.owner?28:-20};player.shipSeat=id==spec.owner?-1:0;world.Actor(id).pose=SurvivalShipInteriorModel.World(ship,player.interiorPose);}
            using(var store=new IndustrialAuthority(world,config.savePath)){if(world.universe==null)store.InitializeSurvivalUniverse(model.State);store.Save();}spec.seeded=true;return true;
        }catch(Exception error){if(spec!=null)spec.error=error.Message;Debug.LogError("Dock fixture: "+error.Message);return false;}finally{model?.Dispose();if(spec!=null)File.WriteAllText(path,JsonUtility.ToJson(spec,true));}}
    }
#endif
}
#endif
