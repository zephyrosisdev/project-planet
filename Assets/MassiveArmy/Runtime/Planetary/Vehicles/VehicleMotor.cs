using UnityEngine;

namespace MassiveArmy.Planetary
{
    public struct VehicleInput {public Vector2 movement;public Vector3 aim;public bool brake,fire,boost,recover;}
    public struct VehicleMotorSnapshot {
        public SpacePose pose;public float yawVelocity,verticalVelocity,steering;
        public VehicleMotionState state;public bool grounded;
    }
    public abstract class VehicleMotor
    {
        internal static float LocalStepDuration(float elapsed)=>Mathf.Clamp(elapsed,0,.05f);
        protected readonly VehicleDefinition definition;
        protected float yawVelocity,verticalVelocity;
        public SpacePose Pose;
        public VehicleMotionState State {get;protected set;}
        public bool Grounded {get;protected set;}
        public float Steering {get;protected set;}
        public float ForwardSpeed=>Vector3.Dot(Pose.linearVelocity.Float,Pose.rotation*Vector3.forward);
        public VehicleMotorSnapshot Capture()=>new VehicleMotorSnapshot{pose=Pose,yawVelocity=yawVelocity,verticalVelocity=verticalVelocity,steering=Steering,state=State,grounded=Grounded};
        public void Restore(VehicleMotorSnapshot snapshot){Pose=snapshot.pose;yawVelocity=snapshot.yawVelocity;verticalVelocity=snapshot.verticalVelocity;Steering=snapshot.steering;State=snapshot.state;Grounded=snapshot.grounded;}
        protected VehicleMotor(VehicleDefinition definition){this.definition=definition;}
        public void Follow(SpacePose pose){Pose=pose;verticalVelocity=0;Grounded=true;State=pose.linearVelocity.Magnitude>.1?VehicleMotionState.Walking:VehicleMotionState.Idle;}
        public void Release(SpacePose pose){Pose=pose;Grounded=false;verticalVelocity=0;State=VehicleMotionState.Falling;}
        public void StepFlight(float dt,VehicleTerrainContact terrain){
            if(dt<=0)return;
            // A moving-hull sweep uses one coherent previous/current carrier pose.
            int steps=terrain.MovingCarrier!=null?1:Mathf.Clamp(Mathf.CeilToInt(dt*60),1,6);double h=Mathf.Min(dt,.1f)/steps;
            for(int i=0;i<steps;i++){
                var acceleration=terrain.Acceleration(Pose.position);
                var delta=Pose.linearVelocity*h+acceleration*(h*h*.5);
                var allowed=terrain.Sweep(Pose.position,Pose.rotation,definition,delta.Float,out var hitNormal,true);
                var next=Pose.position+new Double3(allowed);
                var velocity=Pose.linearVelocity+(acceleration+terrain.Acceleration(next))*(h*.5);
                var up=terrain.Up(next);
                if(terrain.Probe(Pose.position,up,.1f,Mathf.Max(.08f,-Vector3.Dot(delta.Float,up)+.1f),out var contact)&&Vector3.Dot((next-contact.position).Float,up)<=.03f&&Vector3.Dot(velocity.Float,contact.normal)<=0){
                    next=contact.position+new Double3(contact.normal)*.025;
                    velocity=new Double3(Vector3.ProjectOnPlane(velocity.Float,contact.normal));Grounded=true;State=VehicleMotionState.Landing;
                }
                if(!terrain.CanonicalOnlyGround){var floor=terrain.Surface(next,.025f);
                if(next.Magnitude<floor.Magnitude){next=floor;velocity=new Double3(Vector3.ProjectOnPlane(velocity.Float,up));Grounded=true;State=VehicleMotionState.Landing;}}
                if(hitNormal.sqrMagnitude>.5f){
                    var obstacleVelocity=terrain.ObstacleVelocity(next);var relativeVelocity=velocity-obstacleVelocity;if(Vector3.Dot(relativeVelocity.Float,hitNormal)<0)velocity=relativeVelocity-new Double3(hitNormal)*Vector3.Dot(relativeVelocity.Float,hitNormal)+obstacleVelocity;
                    if(Vector3.Dot(hitNormal,up)>.55f&&Vector3.Dot(acceleration.Float,hitNormal)<0){Grounded=true;State=VehicleMotionState.Landing;verticalVelocity=0;}
                }
                Pose.position=next;Pose.linearVelocity=velocity;
                if(Grounded)break;
            }
        }
        public void Stop(){Pose.linearVelocity=default;yawVelocity=verticalVelocity=0;State=VehicleMotionState.Idle;}
        protected virtual float DesiredYaw(VehicleInput input,float speed)=>input.movement.x*definition.turnSpeed;
        protected virtual float SpeedLimit=>definition.maximumSpeed;
        public void Step(float dt,VehicleInput input,VehicleTerrainContact terrain,IVehicleCarrier deck=null){
            if(dt<=0)return;int steps=Mathf.Clamp(Mathf.CeilToInt(dt*60),1,6);float h=Mathf.Min(dt,.1f)/steps;
            for(int iteration=0;iteration<steps;iteration++){
                Vector3 up=deck==null?terrain.Up(Pose.position):deck.CarrierPose.rotation*Vector3.up;
                var previous=Pose.position;var heading=Vector3.ProjectOnPlane(Pose.rotation*Vector3.forward,up).normalized;
                if(heading.sqrMagnitude<.01f)heading=Vector3.ProjectOnPlane(Vector3.forward,up).normalized;
                float current=Vector3.Dot(Pose.linearVelocity.Float,heading),target=input.brake?0:input.movement.y*(input.movement.y>=0?SpeedLimit:Mathf.Min(SpeedLimit,definition.reverseSpeed));
                float speed=Mathf.MoveTowards(current,target,(Mathf.Abs(target)<Mathf.Abs(current)?definition.braking:definition.acceleration)*h);
                float wanted=DesiredYaw(input,speed);yawVelocity=Mathf.MoveTowards(yawVelocity,wanted,definition.turnAcceleration*h);Steering=Mathf.MoveTowards(Steering,input.movement.x*30,90*h);
                heading=Quaternion.AngleAxis(yawVelocity*h,up)*heading;var velocity=Vector3.ProjectOnPlane(Pose.linearVelocity.Float,up);
                velocity=Vector3.MoveTowards(velocity,heading*speed,definition.grip*h+definition.acceleration*h);var rotation=Quaternion.LookRotation(heading,up);
                float gravity=terrain.Gravity(Pose.position);verticalVelocity-=gravity*h;
                var delta=terrain.Sweep(Pose.position,rotation,definition,(velocity+up*verticalVelocity)*h);
                var proposed=Pose.position+new Double3(delta);
                // Evaluate every footprint support before accepting translation; centre-only height checks miss ridges under wheels/feet.
                int supports=0;Vector3 supportNormal=Vector3.zero;float lift=float.NegativeInfinity;
                int count=definition.locomotion==VehicleLocomotion.Walker?definition.legs.Length:definition.wheels.Length;
                for(int i=0;i<count;i++){
                    var p=definition.locomotion==VehicleLocomotion.Walker?definition.legs[i].stance:definition.wheels[i].position-Vector3.up*definition.wheels[i].radius;
                    if(definition.locomotion==VehicleLocomotion.Walker)p.y=0;
                    var sample=proposed+Double3.Rotate(rotation,new Double3(p));
                    if(!terrain.Probe(sample,up,definition.traversal.maximumStep+.25f,definition.traversal.maximumDrop,out var contact))continue;
                    float slope=Vector3.Angle(contact.normal,up),rise=Vector3.Dot((contact.position-sample).Float,up);
                    if(rise>definition.traversal.maximumStep&&delta.sqrMagnitude>.00001f){proposed=previous;velocity=Vector3.zero;lift=0;break;}
                    if(slope>definition.traversal.maximumSlope&&rise>.05f)continue;
                    supports++;supportNormal+=contact.normal;lift=Mathf.Max(lift,rise);
                }
                bool wasGrounded=Grounded;Grounded=supports>0&&lift>=-.04f;
                if(Grounded){proposed+=new Double3(up)*Mathf.Max(0,lift);// Positional depenetration is not physical launch energy. Retain only the
                    // climb velocity implied by travel along the supporting surface.
                    var normal=supportNormal.normalized;
                    float climb=-Vector3.Dot(velocity,normal)/Mathf.Max(.25f,Vector3.Dot(up,normal));
                    if(Mathf.Abs(climb)<.1f)climb=0;
                    verticalVelocity=wasGrounded?Mathf.Lerp(verticalVelocity,climb,1-Mathf.Exp(-Mathf.Max(1,definition.suspensionDamping)*h)):climb;
                    if(velocity.sqrMagnitude<.01f&&climb==0)verticalVelocity=0;State=!wasGrounded?VehicleMotionState.Landing:Mathf.Abs(speed)>.2f?(Mathf.Abs(speed)>definition.maximumSpeed*.55f?VehicleMotionState.Running:VehicleMotionState.Walking):Mathf.Abs(yawVelocity)>1?VehicleMotionState.Turning:VehicleMotionState.Idle;}
                else {State=VehicleMotionState.Falling;}
                // Planet vehicles retain analytic recovery. Ship-local decks use their CPU contact set.
                if(!terrain.CanonicalOnlyGround){var floor=terrain.Surface(proposed,.025f);if(proposed.Magnitude<floor.Magnitude){proposed=floor;verticalVelocity=0;Grounded=true;}}
                // Carrier support is a persistent local constraint, not a one-frame ray hit.
                // Recheck after slope handling and terrain recovery so neither can push us through a deck.
                if(deck is IVehicleDeck carrierDeck&&carrierDeck.SampleVehicleDeck(proposed,out var support)){
                    float penetration=Vector3.Dot((support.position-proposed).Float,up)+.03f;
                    if(penetration>0&&penetration<1.25f){proposed+=new Double3(up)*penetration;verticalVelocity=0;Grounded=true;}
                }
                Pose.position=proposed;Pose.linearVelocity=new Double3(velocity+up*verticalVelocity);Pose.rotation=rotation;Pose.angularVelocity=up*(yawVelocity*Mathf.Deg2Rad);
            }
        }
    }
    public sealed class WalkerVehicleMotor:VehicleMotor {public WalkerVehicleMotor(VehicleDefinition definition):base(definition){} protected override float SpeedLimit=>WalkerKinematics.WalkingSpeed(definition);}
    public sealed class WheeledVehicleMotor:VehicleMotor
    {
        public WheeledVehicleMotor(VehicleDefinition definition):base(definition){}
        protected override float DesiredYaw(VehicleInput input,float speed){float length=Mathf.Max(1,definition.size.z*.6f);return Mathf.Clamp(speed/length*Mathf.Tan(input.movement.x*30*Mathf.Deg2Rad)*Mathf.Rad2Deg,-definition.turnSpeed,definition.turnSpeed);}
    }
    public sealed class TrackedVehicleMotor:VehicleMotor {public TrackedVehicleMotor(VehicleDefinition definition):base(definition){} }
}
