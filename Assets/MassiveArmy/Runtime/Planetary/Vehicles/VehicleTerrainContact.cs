using UnityEngine;

namespace MassiveArmy.Planetary
{
    public struct VehicleGroundContact {public Double3 position;public Vector3 normal;public Collider collider;}
    /// <summary>The same analytic planet surface as the observer, supplemented by bounded local collider queries. Never depends on streamed terrain colliders.</summary>
    public sealed class VehicleTerrainContact
    {
        PlanetDevelopment world;
        readonly PlanetSurface surface;readonly System.Func<Double3,Vector3> toRender;readonly System.Func<Vector3,Double3> toBody;
        readonly RaycastHit[] hits=new RaycastHit[32];readonly Collider[] overlaps=new Collider[32];
        public Transform IgnoredRoot;public IVehicleCarrier MovingCarrier;public SpacePose PreviousCarrierPose;
        public Collider IgnoredCollider;public Collider LastObstacle {get;private set;}
        public IVehicleDeck CarrierDeck;
        // Network worlds query canonical terrain plus their own authoritative obstacle
        // set. Client render colliders must not change predicted vehicle contacts.
        public bool UsePhysicsContacts=true,CanonicalOnlyGround;public Vector3? FixedUp;public float? FixedGravity;
        public delegate bool ContactProbe(Double3 point,Vector3 up,float above,float depth,out VehicleGroundContact contact);
        public delegate Vector3 ContactSweep(Double3 position,Quaternion rotation,VehicleDefinition definition,Vector3 delta,out Vector3 normal);
        public ContactProbe CanonicalProbe;public ContactSweep CanonicalSweep;
        public VehicleTerrainContact(PlanetDevelopment world,PlanetSurface surface):this(surface,world.RenderBodyPosition,world.RenderToBody){this.world=world;}
        public VehicleTerrainContact(PlanetSurface surface,System.Func<Double3,Vector3> toRender,System.Func<Vector3,Double3> toBody){this.surface=surface;this.toRender=toRender;this.toBody=toBody;}
        bool Own(Collider other)=>other!=null&&IgnoredCollider!=null&&(other==IgnoredCollider||other.transform.IsChildOf(IgnoredRoot!=null?IgnoredRoot:IgnoredCollider.transform));
        public Vector3 Up(Double3 position)=>FixedUp??PlanetPrecisionPolicy.Direction(position);
        public Double3 Acceleration(Double3 position){
            var galaxy=world?.SystemRuntime?.Galaxy;
            if(galaxy!=null){int reference=galaxy.NodeFor(world.definition);return galaxy.Acceleration(galaxy.Current[reference].position+position,galaxy.Current)-galaxy.Current[reference].acceleration;}
            return new Double3(Up(position))*(-Gravity(position));
        }
        public float Gravity(Double3 position)=>FixedGravity??(float)(surface.Definition.gravitationalParameter/System.Math.Max(1,position.Magnitude*position.Magnitude));
        public Double3 Surface(Double3 position,float clearance=0)=>surface.ProjectToSurface(position,clearance);
        Vector3 Normal(Double3 position){var d=position*(1/position.Magnitude);var up=d.Float;var tangent=Vector3.Cross(Mathf.Abs(up.y)>.9f?Vector3.right:Vector3.up,up).normalized;var bitangent=Vector3.Cross(up,tangent);double e=surface.Definition.experimentalGpuDoublePrecision?2.0/surface.Definition.radius:PlanetPrecisionPolicy.NormalSampleAngle(surface.Definition.radius);var t=new Double3(tangent)*e;var b=new Double3(bitangent)*e;float x=(float)((surface.SampleHeight(d+t)-surface.SampleHeight(d-t))/(2*e*surface.Definition.radius)),z=(float)((surface.SampleHeight(d+b)-surface.SampleHeight(d-b))/(2*e*surface.Definition.radius));return (up-tangent*x-bitangent*z).normalized;}
        public bool Probe(Double3 point,Vector3 up,float above,float depth,out VehicleGroundContact contact){
            var top=point+new Double3(up)*above;var ground=CanonicalOnlyGround?default:Surface(point);double distance=Vector3.Dot((top-ground).Float,up);
            bool found=!CanonicalOnlyGround&&distance>=-.01&&distance<=above+depth;contact=CanonicalOnlyGround?default:new VehicleGroundContact{position=ground,normal=Normal(point)};
            float best=found?(float)distance:above+depth;
            if(CanonicalProbe!=null&&CanonicalProbe(point,up,above,depth,out var canonical)){
                float d=Vector3.Dot((top-canonical.position).Float,up);if(d>=0&&d<=best){best=d;found=true;contact=canonical;}
            }
            if(CarrierDeck!=null&&CarrierDeck.SampleVehicleDeck(point,out var deck)){
                float d=Vector3.Dot((top-deck.position).Float,up);if(d>=0&&d<=best){best=d;found=true;contact=deck;}
            }
            int count=UsePhysicsContacts?Physics.RaycastNonAlloc(toRender(top),-up,hits,above+depth,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore):0;
            for(int i=0;i<count;i++)if(!Own(hits[i].collider)&&hits[i].distance<best&&Vector3.Dot(hits[i].normal,up)>.15f){best=hits[i].distance;found=true;contact=new VehicleGroundContact{position=toBody(hits[i].point),normal=hits[i].normal,collider=hits[i].collider};}
            return found;
        }
        public Vector3 Sweep(Double3 position,Quaternion rotation,VehicleDefinition definition,Vector3 delta)=>Sweep(position,rotation,definition,delta,out _);
        public Vector3 Sweep(Double3 position,Quaternion rotation,VehicleDefinition definition,Vector3 delta,out Vector3 hitNormal,bool includeFloor=false){
            hitNormal=Vector3.zero;LastObstacle=null;
            if(!UsePhysicsContacts)return CanonicalSweep!=null?CanonicalSweep(position,rotation,definition,delta,out hitNormal):delta;
            float length=delta.magnitude;if(length<.00001f)return delta;
            Vector3 Cast(Double3 start,Vector3 motion,bool carrierOnly,out Vector3 normal,out Collider obstacle){
                normal=Vector3.zero;obstacle=null;float length=motion.magnitude;if(length<.00001f)return motion;
                int count=Physics.BoxCastNonAlloc(toRender(start)+rotation*definition.chassisCentre,definition.chassisSize*.48f,motion/length,hits,rotation,length+.04f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                float allowed=length;for(int i=0;i<count;i++){var collider=hits[i].collider;bool carrier=MovingCarrier!=null&&MovingCarrier.Owns(collider);
                    if(Own(collider)||MovingCarrier!=null&&carrier!=carrierOnly||Vector3.Dot(motion,hits[i].normal)>=0)continue;
                    if(Vector3.Dot(hits[i].normal,rotation*Vector3.up)>.55f&&!includeFloor)continue;
                    float candidate=Mathf.Max(0,hits[i].distance-.04f);if(candidate<allowed){allowed=candidate;normal=hits[i].normal;obstacle=collider;}}
                return motion*(allowed/length);
            }
            var allowedWorld=Cast(position,delta,false,out hitNormal,out var worldObstacle);LastObstacle=worldObstacle;
            if(MovingCarrier==null)return allowedWorld;
            // Query the moving hull in its current pose. The vehicle's inertial
            // displacement remains intact; only its motion relative to the hull is clipped.
            var carried=VehicleParking.TransportPoint(PreviousCarrierPose,MovingCarrier.CarrierPose,position);
            var relative=(position+new Double3(delta)-carried).Float;
            var allowedRelative=Cast(carried,relative,true,out var carrierNormal,out var carrierObstacle);
            float worldFraction=allowedWorld.magnitude/length,carrierFraction=relative.sqrMagnitude>.000001f?allowedRelative.magnitude/relative.magnitude:1;
            if(carrierObstacle!=null&&carrierFraction<worldFraction){hitNormal=carrierNormal;LastObstacle=carrierObstacle;return (carried-position).Float+allowedRelative;}
            return allowedWorld;
        }
        public Double3 ObstacleVelocity(Double3 point)=>MovingCarrier!=null&&MovingCarrier.Owns(LastObstacle)?VehicleParking.PointVelocity(MovingCarrier.CarrierPose,point):default;
        public Double3 RecoverPenetration(Double3 position,Quaternion rotation,VehicleDefinition definition,bool includeMovingCarrier=false){
            if(IgnoredCollider==null)return position;
            for(int pass=0;pass<3;pass++){
                var render=toRender(position);int count=Physics.OverlapBoxNonAlloc(render+rotation*definition.chassisCentre,definition.chassisSize*.49f,overlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                bool corrected=false;
                for(int i=0;i<count;i++){
                    var other=overlaps[i];if(Own(other)||!includeMovingCarrier&&MovingCarrier!=null&&MovingCarrier.Owns(other))continue;
                    var colliderPosition=render;var colliderRotation=rotation;
                    if(IgnoredRoot!=null){colliderPosition+=rotation*IgnoredRoot.InverseTransformPoint(IgnoredCollider.transform.position);colliderRotation*=Quaternion.Inverse(IgnoredRoot.rotation)*IgnoredCollider.transform.rotation;}
                    if(Physics.ComputePenetration(IgnoredCollider,colliderPosition,colliderRotation,other,other.transform.position,other.transform.rotation,out var direction,out float distance)){
                        var correction=direction*Mathf.Min(distance+.02f,2);position+=new Double3(correction);render+=correction;corrected=true;
                    }
                }
                if(!corrected)break;
            }
            var floor=Surface(position,.025f);return position.Magnitude<floor.Magnitude?floor:position;
        }
        public bool TryRecovery(Double3 position,Quaternion rotation,VehicleDefinition definition,out Double3 recovered){
            var up=Up(position);var forward=Vector3.ProjectOnPlane(rotation*Vector3.forward,up).normalized;var right=Vector3.Cross(up,forward);
            for(int ring=0;ring<5;ring++)for(int direction=0;direction<9;direction++){
                float distance=.5f*(1<<ring);var offset=direction==0?up:up*.5f+(forward*Mathf.Cos((direction-1)*Mathf.PI/4)+right*Mathf.Sin((direction-1)*Mathf.PI/4));
                var candidate=position+new Double3(offset)*distance;var floor=Surface(candidate,.05f);if(candidate.Magnitude<floor.Magnitude)candidate=floor;
                int count=Physics.OverlapBoxNonAlloc(toRender(candidate)+rotation*definition.chassisCentre,definition.chassisSize*.5f,overlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                bool clear=count<overlaps.Length;for(int i=0;i<count;i++)if(!Own(overlaps[i])){clear=false;break;}
                if(clear){recovered=candidate;return true;}
            }
            recovered=position;return false;
        }
        public bool ExitClear(Double3 feet,Vector3 up)=>!Physics.CheckCapsule(toRender(feet)+up*(PlanetPlayerCollision.Radius+PlanetPlayerCollision.Skin),toRender(feet)+up*(PlanetPlayerCollision.Height-PlanetPlayerCollision.Radius),PlanetPlayerCollision.Radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
    }
}
