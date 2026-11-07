using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>One slot in a bounded observer pool. Grounded AI stays GPU-authoritative; player, airborne and cargo motion use local physics.</summary>
    public sealed partial class VehicleController:IDisposable
    {
        sealed class Foot
        {
            public Double3 anchor,start,target;public Vector3 normal;
            public bool planted,stepping;public float time;
        }
        readonly PlanetDevelopment world;readonly VehicleTerrainContact terrain;readonly VehicleRigPool rigPool;readonly Foot[] feet;
        VehicleRig rig;float nextProbe,wheelAngle,heave,heaveVelocity,bodyPitch,bodyRoll,wheelHeaveTarget;int phase;
        Vector3 supportNormal;
        IVehicleCarrier departureCarrier;SpacePose departurePose;
        SpacePose lastCarrierPose;bool carriedLastTick;Vector3 carrierRelativeVelocity;
        public readonly VehicleDefinition Definition;
        public readonly VehicleSeatManager Seats;
        public readonly VehicleMotor Motor;
        public readonly VehicleParking Parking=new VehicleParking();
        public uint EntityId {get;internal set;}
        public uint OwnerTeam {get;internal set;}
        public int BackingType {get;internal set;}
        public bool Alive {get;internal set;}=true;
        public bool Detailed=>rig!=null;
        public bool Controlled=>Seats.PlayerSeat>=0;
        public bool LocalPhysics=>LocalMovement||Parking.Parked||Airborne;
        public bool Airborne {get;private set;}
        public float BoostCharge {get;private set;}=1;
        public bool Boosting {get;private set;}
        float boostBurst,boostDelay,stalledTime;bool boostLatched;
        public bool MotionBlocked {get;private set;}public string BlockingObject=>terrain.LastObstacle!=null?terrain.LastObstacle.name:"terrain step or support";
        // Detail is presentation only: grounded AI follows the shared GPU flow field.
        // Local authority is reserved for a player driver, cargo attachment and flight.
        public Vector3 TargetDirection {get;internal set;}
        public Vector3 PlayerAimDirection {get;internal set;}
        public Quaternion VisualRotation=>Motor.Pose.rotation*Quaternion.Euler(bodyPitch,0,-bodyRoll);
        public float BodyHeave=>heave;
        internal bool OwnsCollider(Collider collider)=>rig!=null&&collider!=null&&collider.transform.IsChildOf(rig.transform);
        internal bool IsOwnCarrier(IVehicleCarrier carrier)=>rig!=null&&carrier.Owns(rig.chassis);
        public event Action<int,Double3,float> FootPlanted;
        public VehicleController(PlanetDevelopment world,VehicleTerrainContact terrain,VehicleRigPool rigPool,VehicleDefinition definition,uint id,SpacePose pose){
            this.world=world;this.terrain=terrain;this.rigPool=rigPool;Definition=definition;EntityId=id;Seats=new VehicleSeatManager(definition);
            Motor=definition.locomotion==VehicleLocomotion.Walker?(VehicleMotor)new WalkerVehicleMotor(definition):definition.locomotion==VehicleLocomotion.Tracked?new TrackedVehicleMotor(definition):new WheeledVehicleMotor(definition);Motor.Follow(pose);
            feet=new Foot[definition.legs.Length];for(int i=0;i<feet.Length;i++)feet[i]=new Foot();supportNormal=terrain.Up(pose.position);
        }
        public void FollowGpu(UnitState state){
            OwnerTeam=state.packed&255u;
            Alive=(state.flags&(1u|1048576u))==(1u|1048576u);if(!Alive||LocalPhysics)return;
            Vector3 up=CubeSphere.Decode(state.position);uint face=(uint)Mathf.Min(5,Mathf.FloorToInt(state.position.x));var right=Vector3.ProjectOnPlane(CubeSphere.Right[(int)face],up).normalized;var forward=Vector3.Cross(right,up);
            var heading=right*Mathf.Sin(state.facing)+forward*Mathf.Cos(state.facing);
            var low=(state.flags&268435456u)!=0?state.previous:Vector2.zero;
            var cube=new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*(2*((double)state.position.x-face+low.x)-1)+new Double3(CubeSphere.Up[face])*(2*((double)state.position.y+low.y)-1);
            var exact=cube*(world.definition.radius/cube.Magnitude);
            Motor.Follow(new SpacePose{position=terrain.Surface(exact,.025f),rotation=Quaternion.LookRotation(heading,up),linearVelocity=new Double3(right*state.velocity.x+forward*state.velocity.y)});
        }
        public event Action<VehicleInput> MovementRequested;
        public void SetDesiredMovement(Vector2 movement,Vector3 aim,bool fire=false){AIInput=new VehicleInput{movement=Vector2.ClampMagnitude(movement,1),aim=aim,fire=fire};MovementRequested?.Invoke(AIInput);}
        public void SetDesiredAim(Vector3 aim){var input=AIInput;input.aim=aim;AIInput=input;}
        public VehicleInput AIInput {get;private set;}
        public void SetDetail(bool detailed){
            if(!Alive)detailed=false;if(detailed==(rig!=null))return;
            if(!detailed){rigPool.Release(rig);rig=null;return;}
            rig=rigPool.Acquire(Definition);if(rig==null)return;var go=rig.gameObject;
            var lod=go.GetComponent<LODGroup>();if(lod!=null)lod.ForceLOD(0);
            nextProbe=0;for(int i=0;i<feet.Length;i++)feet[i].planted=false;
        }
        public void Tick(float dt,VehicleInput input,IVehicleCarrier deck){
            if(!Alive){SetDetail(false);return;}
            terrain.IgnoredCollider=rig!=null?rig.chassis:null;terrain.IgnoredRoot=rig!=null?rig.transform:null;
            deck=Parking.Carrier??deck;
            if(!Parking.Parked&&!Airborne&&deck!=null&&Parking.TryFollow(Definition,Motor.Pose,deck))carrierRelativeVelocity=LocalMovement?Double3.Rotate(Quaternion.Inverse(deck.CarrierPose.rotation),Motor.Pose.linearVelocity-Parking.Pose.linearVelocity).Float:Vector3.zero;
            if(Parking.Parked)departureCarrier=null;
            if(departureCarrier!=null){float radius=64;foreach(var bay in departureCarrier.VehicleBays)radius=Mathf.Max(radius,bay.interior.center.magnitude+bay.interior.extents.magnitude);if(departureCarrier is VehicleCarrierVolume volume)foreach(var room in volume.interiors)radius=Mathf.Max(radius,room.center.magnitude+room.extents.magnitude);if((Motor.Pose.position-departureCarrier.CarrierPose.position).Magnitude>radius+Definition.size.magnitude+32)departureCarrier=null;}
            terrain.MovingCarrier=Airborne?departureCarrier:null;terrain.PreviousCarrierPose=departurePose;terrain.CarrierDeck=deck as IVehicleDeck;
            if(Parking.Parked){
                var parent=Parking.Carrier.CarrierPose;
                if(carriedLastTick){
                    Double3 Carry(Double3 point)=>parent.position+Double3.Rotate(parent.rotation,Double3.Rotate(Quaternion.Inverse(lastCarrierPose.rotation),point-lastCarrierPose.position));
                    var turn=parent.rotation*Quaternion.Inverse(lastCarrierPose.rotation);
                    foreach(var foot in feet){if(!foot.planted)continue;foot.anchor=Carry(foot.anchor);foot.start=Carry(foot.start);foot.target=Carry(foot.target);foot.normal=turn*foot.normal;}supportNormal=turn*supportNormal;
                }
                var attached=Parking.Pose;attached.linearVelocity=LocalMovement?Double3.Rotate(parent.rotation,new Double3(carrierRelativeVelocity)):new Double3();Motor.Follow(attached);
            }
            if(dt>0&&(LocalMovement||Airborne)){
                var pose=Motor.Pose;pose.position=terrain.RecoverPenetration(pose.position,pose.rotation,Definition);Motor.Pose=pose;
                if(LocalMovement&&input.recover&&terrain.TryRecovery(pose.position,pose.rotation,Definition,out var safe)){
                    if(Parking.Parked){Parking.FollowSupport(Parking.Carrier,pose);ReleaseCarrier(out var released);pose.linearVelocity+=released.linearVelocity;}
                    pose.position=safe;Motor.Release(pose);Airborne=true;
                    foreach(var foot in feet){foot.planted=false;foot.stepping=false;}
                }
            }
            var beforeMovement=Motor.Pose.position;
            StepBooster(dt,input);if(Airborne&&departureCarrier!=null){terrain.MovingCarrier=departureCarrier;terrain.PreviousCarrierPose=departurePose;}
            if(Airborne&&!Parking.Parked){var pose=Motor.Pose;var up=terrain.Up(pose.position);if(terrain.Probe(pose.position,up,.3f,.3f,out var resting)){float gap=Vector3.Dot((pose.position-resting.position).Float,up);if(gap>=-.05f&&gap<=.08f&&Vector3.Dot(pose.linearVelocity.Float,up)<=.1f){pose.position=resting.position+new Double3(up)*.025;pose.linearVelocity=new Double3(Vector3.ProjectOnPlane(pose.linearVelocity.Float,resting.normal));Motor.Follow(pose);Airborne=false;}}}
            if(Airborne&&!Parking.Parked){Motor.StepFlight(dt,terrain);Airborne=!Motor.Grounded;}
            else if(LocalMovement){var previous=Motor.Pose;Motor.Step(dt,input,terrain,deck);ConstrainPlantedLegs(previous,dt);if(!Motor.Grounded&&!Parking.Parked){Airborne=true;Motor.Release(Motor.Pose);}}
            if(Parking.Parked){
                if(deck is IVehicleDeck support&&support.SampleVehicleDeck(Motor.Pose.position,out var contact)&&System.Math.Abs(Vector3.Dot((Motor.Pose.position-contact.position).Float,deck.CarrierPose.rotation*Vector3.up))<1.25f){
                    Parking.FollowSupport(deck,Motor.Pose);carrierRelativeVelocity=Double3.Rotate(Quaternion.Inverse(deck.CarrierPose.rotation),Motor.Pose.linearVelocity).Float;
                }else {var current=Motor.Pose;var relative=current.linearVelocity;
                    Parking.FollowSupport(deck,current);ReleaseCarrier(out var released);
                    released.linearVelocity+=relative;Motor.Release(released);Airborne=true;
                    terrain.CarrierDeck=null;carrierRelativeVelocity=Vector3.zero;
                    foreach(var foot in feet){foot.planted=false;foot.stepping=false;}}
            }else if(!Airborne&&Parking.TryFollow(Definition,Motor.Pose,deck)){
                carrierRelativeVelocity=LocalMovement?Double3.Rotate(Quaternion.Inverse(deck.CarrierPose.rotation),Motor.Pose.linearVelocity-Parking.Pose.linearVelocity).Float:Vector3.zero;
                var attached=Parking.Pose;attached.linearVelocity=Motor.Pose.linearVelocity;Motor.Follow(attached);
            }
            if(LocalMovement&&Motor.Grounded&&input.movement.sqrMagnitude>.1f&&(Motor.Pose.position-beforeMovement).Magnitude<.001)stalledTime+=dt;else stalledTime=0;
            MotionBlocked=stalledTime>.2f;
            if(stalledTime>.75f){foreach(var foot in feet){foot.planted=false;foot.stepping=false;}stalledTime=0;}
            if(departureCarrier!=null){var settled=Motor.Pose;settled.position=terrain.RecoverPenetration(settled.position,settled.rotation,Definition,true);Motor.Pose=settled;departurePose=departureCarrier.CarrierPose;}
            carriedLastTick=Parking.Parked;if(carriedLastTick)lastCarrierPose=Parking.Carrier.CarrierPose;
            Present(dt);
        }
        void ReleaseCarrier(out SpacePose released){departureCarrier=Parking.Carrier;departurePose=departureCarrier.CarrierPose;Parking.Release(out released);terrain.MovingCarrier=departureCarrier;terrain.PreviousCarrierPose=departurePose;terrain.CarrierDeck=null;}
        void StepBooster(float dt,VehicleInput input){
            Boosting=false;if(dt<=0)return;
            boostDelay=input.boost?Definition.boostRechargeDelay:Mathf.Max(0,boostDelay-dt);
            if(!input.boost){boostLatched=false;boostBurst=0;}
            if(LocalMovement&&input.boost&&!boostLatched&&BoostCharge>0&&boostBurst<Definition.boostMaximumBurst){
                float active=Mathf.Min(dt,Mathf.Min(BoostCharge*Mathf.Max(.1f,Definition.boostFuelSeconds),Definition.boostMaximumBurst-boostBurst));
                if(Parking.Parked){Parking.FollowSupport(Parking.Carrier,Motor.Pose);var relative=Motor.Pose.linearVelocity;ReleaseCarrier(out var released);released.linearVelocity+=relative;Motor.Release(released);}
                var pose=Motor.Pose;pose.linearVelocity+=new Double3(pose.rotation*Vector3.up)*(Definition.boostAcceleration*active);Motor.Release(pose);Airborne=true;
                foreach(var foot in feet){foot.planted=false;foot.stepping=false;}
                BoostCharge=Mathf.Max(0,BoostCharge-active/Mathf.Max(.1f,Definition.boostFuelSeconds));boostBurst+=active;boostDelay=Definition.boostRechargeDelay;Boosting=true;
                if(BoostCharge<=.001f||boostBurst>=Definition.boostMaximumBurst-.001f)boostLatched=true;
            }else if(!input.boost&&boostDelay<=0)BoostCharge=Mathf.Min(1,BoostCharge+dt/Mathf.Max(.1f,Definition.boostRechargeSeconds));
        }
        void ConstrainPlantedLegs(SpacePose previous,float dt){
            if(feet.Length==0||!Motor.Grounded)return;
            var proposed=Motor.Pose;
            bool Supported(float t){
                var position=previous.position+(proposed.position-previous.position)*t;var rotation=Quaternion.Slerp(previous.rotation,proposed.rotation,t)*Quaternion.Euler(bodyPitch,0,-bodyRoll);
                for(int i=0;i<feet.Length;i++){var foot=feet[i];if(!foot.planted||foot.stepping)continue;var leg=Definition.legs[i];var hip=position+Double3.Rotate(rotation,new Double3(leg.hip))+new Double3(previous.rotation*Vector3.up)*heave;
                    if((hip-foot.anchor).Magnitude>WalkerKinematics.Reach(leg)-.03f)return false;}return true;
            }
            if(Supported(1)||!Supported(0))return;
            float a=0,b=1;for(int i=0;i<9;i++){float mid=(a+b)*.5f;if(Supported(mid))a=mid;else b=mid;}
            proposed.position=previous.position+(proposed.position-previous.position)*a;proposed.rotation=Quaternion.Slerp(previous.rotation,proposed.rotation,a);
            if(dt>0)proposed.linearVelocity=(proposed.position-previous.position)*(1/dt);Motor.Pose=proposed;
        }
        public void Rebase(){if(rig==null)return;rig.transform.SetPositionAndRotation(world.RenderBodyPosition(Motor.Pose.position)+Motor.Pose.rotation*Vector3.up*heave,VisualRotation);}
        void Present(float dt){
            if(rig==null)return;Rebase();var pose=Motor.Pose;var up=pose.rotation*Vector3.up;var forward=pose.rotation*Vector3.forward;
            float distance=(world.View.transform.position-rig.transform.position).magnitude;int lod=Controlled?0:distance<Definition.detailDistances.x?0:distance<Definition.detailDistances.y?1:2;
            nextProbe-=Mathf.Max(0,dt);bool probe=nextProbe<=0;float interval=lod==0?(Controlled?1/50f:1/25f):lod==1?1/10f:1/3f;if(probe)nextProbe=interval;
            if(feet.Length>0)PresentWalker(dt,lod,probe);
            else if(Definition.wheels.Length>0)PresentWheels(dt,lod,probe);
            if(rig.torso!=null&&(Definition.locomotion==VehicleLocomotion.Walker||Definition.locomotion==VehicleLocomotion.Tracked)){
                var aim=Controlled&&(Seats.PlayerPermissions&VehicleSeatPermissions.Aim)!=0&&PlayerAimDirection.sqrMagnitude>.5f?PlayerAimDirection:AIInput.aim.sqrMagnitude>.1f?AIInput.aim:TargetDirection.sqrMagnitude>.1f?Vector3.ProjectOnPlane(TargetDirection-pose.position.Float.normalized,up):forward;
                var local=Quaternion.Inverse(pose.rotation)*aim;float yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-Definition.torsoYaw,Definition.torsoYaw),pitch=Mathf.Clamp(-Mathf.Atan2(local.y,Mathf.Max(.01f,new Vector2(local.x,local.z).magnitude))*Mathf.Rad2Deg,-Definition.torsoPitch,Definition.torsoPitch);
                rig.torso.localRotation=Quaternion.Slerp(rig.torso.localRotation,Quaternion.Euler(Definition.locomotion==VehicleLocomotion.Tracked?0:pitch,yaw,0),1-Mathf.Exp(-6*dt));
            }
        }
        void PresentWheels(float dt,int lod,bool probe){
            var pose=Motor.Pose;var up=pose.rotation*Vector3.up;wheelAngle+=Motor.ForwardSpeed*dt/Mathf.Max(.1f,Definition.wheels[0].radius)*Mathf.Rad2Deg;
            Vector3 normal=Vector3.zero;int contacts=0;float compression=0;
            for(int i=0;i<rig.wheels.Length;i++){
                var w=Definition.wheels[i];var wheel=rig.wheels[i];var local=wheel.localPosition;
                if(probe&&lod<2){var mount=pose.position+Double3.Rotate(pose.rotation,new Double3(w.position));if(terrain.Probe(mount,up,w.suspension,w.radius+w.suspension,out var hit)){
                    float suspension=Mathf.Clamp(Vector3.Dot((hit.position-mount).Float,up)+w.radius,-w.suspension,w.suspension);local=w.position+Vector3.up*suspension;normal+=hit.normal;contacts++;compression+=suspension;
                }else local=w.position-Vector3.up*w.suspension;wheel.localPosition=local;}
                float steer=w.steering?Motor.Steering:0;
                // Slightly different inside/outside steering angles reduce tyre scrub.
                if(w.steering&&Mathf.Abs(steer)>1)steer*=1-Mathf.Sign(steer)*Mathf.Sign(w.position.x)*.12f;
                wheel.localRotation=Quaternion.Euler(0,steer,0)*Quaternion.AngleAxis(wheelAngle,Vector3.right);
            }
            if(probe&&contacts>0)supportNormal=normal.normalized;
            if(probe)wheelHeaveTarget=contacts>0?-compression/contacts*.15f:0;
            heave=Mathf.SmoothDamp(heave,wheelHeaveTarget,ref heaveVelocity,.3f,.5f,Mathf.Max(.0001f,dt));
            Stabilize(dt);
        }
        void PresentWalker(float dt,int lod,bool probe){
            var pose=Motor.Pose;var up=pose.rotation*Vector3.up;var forward=pose.rotation*Vector3.forward;float speed=(float)pose.linearVelocity.Magnitude;
            var gait=Definition.gaits[speed>Definition.maximumSpeed*.55f&&Definition.gaits.Length>1?1:0];int stepping=0,grounded=0;for(int i=0;i<feet.Length;i++){if(feet[i].stepping)stepping++;else if(feet[i].planted)grounded++;}
            int phaseCount=1;foreach(int group in gait.groups)phaseCount=Mathf.Max(phaseCount,group+1);
            if(probe){
                int scheduled=0;for(int i=0;i<feet.Length;i++)if(gait.groups[i]==phase)scheduled++;
                bool mayStep=stepping==0&&grounded-scheduled>=gait.minimumGrounded;
                var normal=Vector3.zero;int contacts=0;bool started=false;
                for(int i=0;i<feet.Length;i++){
                    var leg=Definition.legs[i];var foot=feet[i];var desired=pose.position+Double3.Rotate(pose.rotation,new Double3(leg.stance))+pose.linearVelocity*(leg.stepDuration*.6f);
                    var hip=pose.position+Double3.Rotate(VisualRotation,new Double3(leg.hip));
                    // Keep the next foothold reachable; clipping IK endpoints would make planted feet skate.
                    if((desired-hip).Magnitude>WalkerKinematics.Reach(leg)-.08f)desired=pose.position+Double3.Rotate(pose.rotation,new Double3(leg.stance));
                    if(!terrain.Probe(desired,up,leg.probeHeight,leg.probeDepth,out var contact))continue;
                    if(Vector3.Angle(contact.normal,up)>Definition.traversal.maximumSlope)continue;
                    if(!foot.planted){foot.anchor=contact.position;foot.normal=contact.normal;foot.planted=true;}
                    normal+=contact.normal;contacts++;
                    float error=(float)(contact.position-foot.anchor).Magnitude;
                    float vertical=Mathf.Abs(Vector3.Dot((contact.position-foot.anchor).Float,up));
                    if(mayStep&&gait.groups[i]==phase&&!foot.stepping&&(error>leg.stride*gait.strideMultiplier*.45f||vertical>leg.stepHeight)){
                        if((contact.position-hip).Magnitude>Mathf.Min(leg.maxReach,leg.upperLength+leg.lowerLength)||vertical>Definition.traversal.maximumStep+leg.stepHeight)continue;
                        foot.start=foot.anchor;foot.target=contact.position;foot.time=0;foot.stepping=true;foot.normal=contact.normal;started=true;
                    }
                }
                if(contacts>0)supportNormal=normal.normalized;
                if(started||stepping==0)phase=(phase+1)%phaseCount;
            }
            float bob=0;for(int i=0;i<feet.Length;i++)if(feet[i].stepping)bob-=Mathf.Sin(Mathf.Clamp01(feet[i].time)*Mathf.PI)*gait.bodyBob;
            heave=Mathf.SmoothDamp(heave,bob,ref heaveVelocity,.18f,Mathf.Infinity,Mathf.Max(.0001f,dt));
            Stabilize(dt);
            for(int i=0;i<feet.Length;i++){
                var leg=Definition.legs[i];var foot=feet[i];Double3 target=foot.anchor;
                if(foot.stepping){foot.time+=dt/Mathf.Max(.1f,leg.stepDuration*1.4f/gait.cadence);float t=Mathf.Clamp01(foot.time),smooth=t*t*(3-2*t);target=foot.start+(foot.target-foot.start)*smooth+new Double3(up)*(Mathf.Sin(t*Mathf.PI)*leg.stepHeight);if(t>=1){foot.anchor=foot.target;foot.stepping=false;FootPlanted?.Invoke(i,foot.anchor,speed);}}
                if(!foot.planted)continue;var hip=rig.transform.TransformPoint(leg.hip);var targetRender=world.RenderBodyPosition(target);var pole=rig.transform.TransformDirection(leg.pole);
                bool reached=WalkerKinematics.Solve(hip,targetRender,pole,up,leg,out var knee,out var ankle);WalkerKinematics.Present(rig.legs[i],hip,knee,ankle,forward,foot.normal.sqrMagnitude>.5f?foot.normal:up);
                rig.legs[i].anchor=targetRender;rig.legs[i].target=targetRender;rig.legs[i].state=foot.stepping?WalkerFootState.Stepping:reached?WalkerFootState.Planted:WalkerFootState.Unsupported;
            }
        }
        void Stabilize(float dt){
            var angles=VehicleAttitude.SuspensionAngles(Motor.Pose.rotation,supportNormal);
            bodyPitch=Mathf.Lerp(bodyPitch,angles.x,1-Mathf.Exp(-5*dt));bodyRoll=Mathf.Lerp(bodyRoll,angles.y,1-Mathf.Exp(-5*dt));
            Rebase();
        }
        public bool SafeExit(out Double3 position){
            var pose=Motor.Pose;var up=pose.rotation*Vector3.up;
            int seat=Mathf.Max(0,Seats.PlayerSeat);var exit=Definition.seats[seat].exit;
            for(int i=0;i<8;i++){
                Vector3 local=Quaternion.AngleAxis(i*45,Vector3.up)*exit;var desired=pose.position+Double3.Rotate(pose.rotation,new Double3(local));
                if(!terrain.Probe(desired,up,Definition.size.y+1,Definition.size.y+2,out var contact)||Vector3.Angle(contact.normal,up)>45)continue;
                var candidate=contact.position+new Double3(up)*.03;if(terrain.ExitClear(candidate,up)){position=candidate;return true;}
            }
            position=default;return false;
        }
        public void Dispose(){if(rig!=null)rigPool.Release(rig);rig=null;}
    }
}
