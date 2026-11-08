#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        SurvivalInteriorFixtureSpec interiorSpec;int interiorStep;double interiorAt;FramePose? interiorGoal;FramePose interiorBeforeReconnect;SurvivalAction interiorRequest;SurvivalActionReply interiorReply;bool interiorSubscribed,interiorTakeoffSent;
        string InteriorMarker(string name)=>Path.Combine(Path.GetDirectoryName(path),"interior-"+name+".ack");
        bool InteriorOwner=>report.actor==interiorSpec.owner;
        bool StartInteriorProbe(){
            string source=Argument("-survival-probe-interior");if(source==null)return true;
            try{if(report.factoryRequired||report.chatRequired||!Path.GetFullPath(source).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid isolated interior probe");
                interiorSpec=JsonUtility.FromJson<SurvivalInteriorFixtureSpec>(File.ReadAllText(source));if(interiorSpec?.seeded!=true||interiorSpec.version!=1)throw new InvalidOperationException("Interior world was not prepared offline");
                report.interiorRequired=true;deadline+=300;report.scope+=" Includes production transport ship-local walking, hatch blocking, owner pilot controls, moving parents and reconnect. No physical UI, voice or capacity acceptance.";return true;
            }catch(Exception error){_ = Finish(false,error.Message);return false;}
        }
        void InteriorReply(SurvivalActionReply reply){if(interiorRequest?.sequence==reply.sequence)interiorReply=reply;}
        void InteriorSend(string kind,int door=0){interiorReply=null;interiorRequest=new SurvivalAction{kind=kind,entity=interiorSpec.ship,slot=door};session.Submit(interiorRequest);}
        bool InteriorReplied(bool expected){if(interiorReply==null)return false;if(interiorReply.accepted!=expected){_ = Finish(false,"Unexpected interior command result: "+interiorReply.message);return false;}interiorReply=null;return true;}
        bool InteriorAtGoal(){if(!interiorGoal.HasValue)return false;if(session.Latest.motion.pose.Distance(interiorGoal.Value)>1.2)return false;interiorGoal=null;return true;}
        void InteriorWalk(double x,double z)=>interiorGoal=new FramePose{body=session.Latest.motion.pose.body,frame=interiorSpec.ship,x=x,y=-3.95,z=z};
        SurvivalInput InteriorControls(SurvivalInput input){
            input.x=input.y=0;
            if(InteriorOwner&&interiorStep==6&&!interiorTakeoffSent&&session?.Latest?.vitals.controlledShip==interiorSpec.ship&&session.Predicted?.interior==false){input.jump=true;interiorTakeoffSent=true;return input;}
            if(!interiorGoal.HasValue||session?.Predicted?.interior!=true)return input;
            var motion=session.Predicted;var direction=new Vector3((float)(interiorGoal.Value.x-motion.pose.x),0,(float)(interiorGoal.Value.z-motion.pose.z)).normalized;var forward=SurvivalMovement.Tangent(SurvivalMovement.Forward(motion),Vector3.up);var right=Vector3.Cross(Vector3.up,forward);input.x=Vector3.Dot(direction,right);input.y=Vector3.Dot(direction,forward);return input;
        }
        void UpdateInteriorProbe(){
            if(!session.PrivateStateReady)return;if(!interiorSubscribed){session.ActionReplied+=InteriorReply;interiorSubscribed=true;startPose=session.Latest.self.pose;}
            if(report.actor!=interiorSpec.owner&&report.actor!=interiorSpec.passenger){_ = Finish(false,"Unexpected authenticated interior character");return;}
            report.interiorStepNow=interiorStep;var motion=session.Latest.motion;
            if(motion.interior&&motion.ship?.entity==interiorSpec.ship){var expected=SurvivalShipInteriorModel.World(motion.ship,motion.pose);if(expected.Distance(session.Latest.self.pose)>.001){_ = Finish(false,"Ship-local motion disagrees with canonical industrial position");return;}report.interiorCanonical=true;}
            if(InteriorOwner)InteriorOwnerTick(motion);else InteriorPassengerTick(motion);
        }
        void InteriorOwnerTick(SurvivalMotion motion){
            switch(interiorStep){
                case 0:if(!report.remotePlayerSeen)return;InteriorSend("board-passenger");interiorStep=1;break;
                case 1:if(interiorReply!=null&&!InteriorReplied(true))return;if(!motion.interior||motion.ship?.entity!=interiorSpec.ship)return;report.interiorBoarded=true;InteriorWalk(0,-28);interiorAt=Time.realtimeSinceStartupAsDouble;interiorStep=2;break;
                case 2:if(Time.realtimeSinceStartupAsDouble-interiorAt<2)return;if(motion.pose.z>=-31.4){_ = Finish(false,"Walker crossed the closed authoritative hatch");return;}report.interiorHatchBlocked=true;interiorGoal=null;InteriorSend("ship-door",1);interiorStep=3;break;
                case 3:if(interiorReply!=null&&!InteriorReplied(true))return;if(motion.ship?.innerOpen!=true)return;InteriorWalk(1.8,28);interiorStep=4;break;
                case 4:if(!InteriorAtGoal())return;report.interiorWalked=true;interiorStep=5;break;
                case 5:if(!File.Exists(InteriorMarker("passenger-ready")))return;InteriorSend("enter-pilot");interiorStep=6;break;
                case 6:if(interiorReply!=null&&!InteriorReplied(true))return;if(session.Latest.vitals.controlledShip!=interiorSpec.ship||motion.interior)return;report.interiorPilot=true;if(!interiorTakeoffSent)return;if(motion.ship.landed||motion.ship.autoFlight)return;report.interiorParentMoved=true;InteriorSend("leave-pilot");interiorStep=7;break;
                case 7:if(interiorReply!=null&&!InteriorReplied(true))return;if(!motion.interior)return;interiorBeforeReconnect=motion.pose;phase=3;interiorStep=8;_ = Reconnect();break;
                case 8:if(phase!=4||!motion.interior)return;if(session.Latest.self.id!=report.actor||motion.pose.Distance(interiorBeforeReconnect)>.25){_ = Finish(false,"Reconnect lost the same ship-local character position");return;}report.reconnected=report.interiorReconnect=true;File.WriteAllText(InteriorMarker("owner-done"),"moving-parent reconnect passed");interiorStep=9;break;
                case 9:if(!File.Exists(InteriorMarker("passenger-done")))return;report.movementMetres=startPose.Distance(session.Latest.self.pose);_ = Finish(true,null);break;
            }
        }
        void InteriorPassengerTick(SurvivalMotion motion){
            switch(interiorStep){
                case 0:if(!report.remotePlayerSeen)return;InteriorSend("board-passenger");interiorStep=1;break;
                case 1:if(interiorReply!=null&&!InteriorReplied(true))return;if(!motion.interior)return;report.interiorBoarded=true;InteriorSend("enter-pilot");interiorStep=2;break;
                case 2:if(!InteriorReplied(false))return;report.interiorPilotDenied=true;interiorStep=3;break;
                case 3:if(motion.ship?.innerOpen!=true)return;if(!interiorGoal.HasValue)InteriorWalk(3.5,-20);if(!InteriorAtGoal())return;report.interiorWalked=true;File.WriteAllText(InteriorMarker("passenger-ready"),"invited walking passed");interiorStep=4;break;
                case 4:if(motion.ship!=null&&!motion.ship.landed&&motion.interior)report.interiorParentMoved=true;if(!File.Exists(InteriorMarker("owner-done")))return;if(!report.interiorParentMoved){_ = Finish(false,"Passenger did not remain in the moving ship frame");return;}File.WriteAllText(InteriorMarker("passenger-done"),"moving parent stayed canonical");report.movementMetres=startPose.Distance(session.Latest.self.pose);_ = Finish(true,null);break;
            }
        }
        void StopInteriorProbe(){if(interiorSubscribed&&session!=null)session.ActionReplied-=InteriorReply;interiorSubscribed=false;}
    }
}
#endif
