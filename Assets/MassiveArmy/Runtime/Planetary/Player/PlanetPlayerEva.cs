using MassiveArmy.Industrial;
using MassiveArmy.Networking;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        SurvivalShipInteriorModel evaInterior;
        internal bool EvaOwnsCollider(Collider value)=>FlightOwns(value);
        internal SurvivalShipInteriorModel EvaInterior(){var q=pose.rotation;var state=new SurvivalShipState{entity=assembly.id,owner=assembly.owner,landed=Landed,pose=new FramePose{body=SimulationDefinition.bodyId,x=pose.position.x,y=pose.position.y,z=pose.position.z},rotationX=q.x,rotationY=q.y,rotationZ=q.z,rotationW=q.w,rearOpen=hatches.Count>0&&hatches[0].fraction>.99f,innerOpen=hatches.Count>1&&hatches[1].fraction>.99f};if(evaInterior==null||!evaInterior.Matches(assembly,state))evaInterior=new SurvivalShipInteriorModel(assembly,state,surface);return evaInterior;}
    }
    public sealed partial class PlanetFirstPersonController {
        SpacePose evaPreviousShipPose;bool evaShipPoseKnown;double evaBoardAfter;
        void ResetEvaShipPose(){evaShipPoseKnown=ship!=null;if(ship!=null)evaPreviousShipPose=ship.Pose;}
        bool ResolveOfflineEvaShip(ref Double3 proposed,float dt){
            if(ship==null)return false;var current=ship.Pose;if(!evaShipPoseKnown){evaPreviousShipPose=current;evaShipPoseKnown=true;}var previous=evaPreviousShipPose;evaPreviousShipPose=current;
            if((position-previous.position).Magnitude>120&&(proposed-current.position).Magnitude>120)return false;
            var motion=new SurvivalMotion{eva=true,pose=new FramePose{body=surface.Definition.bodyId,x=proposed.x,y=proposed.y,z=proposed.z},forwardX=forward.x,forwardY=forward.y,forwardZ=forward.z,evaVelocityX=evaCarrier.x,evaVelocityY=evaCarrier.y,evaVelocityZ=evaCarrier.z,evaSuitX=evaSuit.x,evaSuitY=evaSuit.y,evaSuitZ=evaSuit.z};var old=new FramePose{body=surface.Definition.bodyId,x=position.x,y=position.y,z=position.z};
            var model=ship.EvaInterior();if(SurvivalEvaContacts.Resolve(motion,old,previous,current,model,out float impact)){proposed=new Double3(motion.pose.x,motion.pose.y,motion.pose.z);evaCarrier=new Double3(motion.evaVelocityX,motion.evaVelocityY,motion.evaVelocityZ);evaSuit=new Double3(motion.evaSuitX,motion.evaSuitY,motion.evaSuitZ);if(impact>50)Environment.Health=0;}
            var local=ship.BodyToLocal(proposed);var velocity=evaCarrier+evaSuit-new Double3(ship.VelocityAt(local));if(Time.realtimeSinceStartupAsDouble<evaBoardAfter||!model.rearOpen||!model.ContainsWalking(new FramePose{body=surface.Definition.bodyId,frame=ship.Assembly.id,x=local.x,y=local.y,z=local.z})||Mathf.Abs(local.x)>=5.6f||local.z< -39.5f||local.z> -37.3f||local.y< -4.4f||local.y>3.2f||velocity.Magnitude>8)return false;
            var feet=local;feet.z=Mathf.Max(-37.5f,feet.z);feet.y=-3.95f;eva=false;bool clear=collision.Clear(ship.LocalToRender(feet),ship.Rotation*Vector3.up);eva=true;if(!clear)return false;
            var facing=Quaternion.Inverse(ship.Rotation)*forward;EnterShip(ship,feet,facing);return true;
        }
    }
}
