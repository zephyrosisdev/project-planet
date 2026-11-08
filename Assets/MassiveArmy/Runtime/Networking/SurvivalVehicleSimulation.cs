using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    // A bounded gameplay vehicle uses the existing motor. No renderer, camera,
    // collider GameObject or army record is constructed by the server adapter.
    internal sealed class SurvivalVehicleSimulation:IDisposable {
        long contactEntity;SurvivalContacts canonicalContacts;readonly bool flat;readonly float maximumAllowed;readonly VehicleDefinition profile;readonly WheeledVehicleMotor motor;readonly VehicleTerrainContact contact;
        public SurvivalVehicleSimulation(string definition,PlanetSurface surface,SurvivalContacts contacts=null,int body=0,bool flat=false,SurvivalServerConfig config=null){
            this.flat=flat;canonicalContacts=contacts;
            profile=ScriptableObject.CreateInstance<VehicleDefinition>();profile.id=definition;profile.locomotion=VehicleLocomotion.Wheeled;
            profile.size=new Vector3(2.5f,3,5);profile.chassisSize=new Vector3(2.3f,1.6f,4.5f);profile.chassisCentre=new Vector3(0,1.5f,0);
            maximumAllowed=(float)(definition=="truck"?config?.vehicleSpeed??10:config?.workVehicleSpeed??5);profile.maximumSpeed=maximumAllowed;profile.acceleration=(float)(config?.vehicleAcceleration??3);profile.braking=(float)(config?.vehicleBraking??7);
            profile.wheels=new[]{new VehicleWheelDefinition{radius=.55f,position=new Vector3(-1,.6f,-1.5f)},new VehicleWheelDefinition{radius=.55f,position=new Vector3(1,.6f,-1.5f)},new VehicleWheelDefinition{radius=.55f,position=new Vector3(-1,.6f,1.5f),steering=true},new VehicleWheelDefinition{radius=.55f,position=new Vector3(1,.6f,1.5f),steering=true}};
            motor=new WheeledVehicleMotor(profile);contact=new VehicleTerrainContact(surface,p=>p.Float,p=>new Double3(p)){UsePhysicsContacts=false,CanonicalOnlyGround=flat,FixedUp=flat?(Vector3?)Vector3.up:null,FixedGravity=flat?(float?)9.81f:null};
            if(contacts!=null){contact.CanonicalProbe=(Double3 point,Vector3 up,float above,float depth,out VehicleGroundContact hit)=>canonicalContacts.ProbeGround(body,point,up,above,depth,out hit,contactEntity*8+2);contact.CanonicalSweep=(Double3 position,Quaternion rotation,VehicleDefinition shape,Vector3 delta,out Vector3 normal)=>canonicalContacts.SweepVehicle(body,position,rotation,shape,delta,out normal,contactEntity*8+2);}
        }
        internal void SetContacts(SurvivalContacts contacts){canonicalContacts=contacts;}
        internal static Quaternion Rotation(SurvivalVehicleState state)=>new Quaternion(state.rotationX,state.rotationY,state.rotationZ,state.rotationW).normalized;
        internal static Double3 Position(FramePose pose)=>new Double3(pose.x,pose.y,pose.z);
        public void Step(SurvivalVehicleState state,SurvivalInput input,float dt,IEnumerable<IndustrialMachine> obstacles){
            contactEntity=state.entity;
            if(!state.initialized){var up=flat?Vector3.up:PlanetPrecisionPolicy.Direction(Position(state.pose));SetRotation(state,Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,state.pose.yaw,0));state.initialized=true;}
            motor.Restore(new VehicleMotorSnapshot{pose=new SpacePose{position=Position(state.pose),rotation=Rotation(state),linearVelocity=new Double3(state.velocityX,state.velocityY,state.velocityZ)},yawVelocity=state.yawVelocity,verticalVelocity=state.verticalVelocity,steering=state.steering,state=(VehicleMotionState)state.motionState,grounded=state.grounded});
            profile.maximumSpeed=Mathf.Clamp(state.maximumSpeed,0,maximumAllowed);
            var before=motor.Pose;motor.Step(dt,new VehicleInput{movement=state.fueled?new Vector2(input.x,input.y):Vector2.zero,brake=input.brake||!state.fueled},contact);
            // Conservative canonical swept footprints agree on client/server and do not
            // depend on the local rendering origin or streamed physics objects.
            if(Blocked(state.entity,state.pose,before.position,motor.Pose.position,obstacles)){motor.Pose=before;motor.Stop();}
            var snapshot=motor.Capture();var p=snapshot.pose.position;state.pose.x=p.x;state.pose.y=p.y;state.pose.z=p.z;
            state.pose.yaw=(Quaternion.Inverse(flat?Quaternion.identity:Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(p)))*snapshot.pose.rotation).eulerAngles.y;
            SetRotation(state,snapshot.pose.rotation);state.velocityX=snapshot.pose.linearVelocity.x;state.velocityY=snapshot.pose.linearVelocity.y;state.velocityZ=snapshot.pose.linearVelocity.z;
            state.yawVelocity=snapshot.yawVelocity;state.verticalVelocity=snapshot.verticalVelocity;state.steering=snapshot.steering;state.grounded=snapshot.grounded;state.motionState=(int)snapshot.state;
        }
        internal static bool Blocked(long moving,FramePose pose,Double3 start,Double3 end,IEnumerable<IndustrialMachine> obstacles){
            if(obstacles==null)return false;var delta=end-start;double length=delta.x*delta.x+delta.y*delta.y+delta.z*delta.z;
            foreach(var machine in obstacles){if(machine.id==moving||machine.integratedLogistics||machine.pose.body!=pose.body||machine.pose.frame!=pose.frame)continue;
                var offset=Position(machine.pose)-start;double t=length>1e-12?Math.Max(0,Math.Min(1,(offset.x*delta.x+offset.y*delta.y+offset.z*delta.z)/length)):0;
                double radius=machine.Definition.Radius+3.2;var nearest=start+delta*t;
                if((Position(machine.pose)-nearest).Magnitude<radius&&(Position(machine.pose)-end).Magnitude<(Position(machine.pose)-start).Magnitude-.00001)return true;
            }return false;
        }
        static void SetRotation(SurvivalVehicleState state,Quaternion q){state.rotationX=q.x;state.rotationY=q.y;state.rotationZ=q.z;state.rotationW=q.w;}
        public void Dispose(){ArmyLifetime.Destroy(profile);}
    }
}
