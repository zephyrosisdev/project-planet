using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>Carrier-local occupied bounds, measured from the vehicle's ground pivot.</summary>
    [System.Serializable] public sealed class VehicleCarrierBay
    {
        public string name="Hangar";
        public Bounds interior;
        public Vector2 entrance=new Vector2(3,3);
        public float clearance=.12f;
        public bool FitsEntrance(VehicleDefinition definition)=>definition.size.x+clearance*2<=entrance.x&&definition.size.y+clearance<=entrance.y;
        public bool Contains(VehicleDefinition definition,Vector3 localPosition,Quaternion localRotation){
            var half=definition.size*.5f;var c=localPosition+localRotation*(Vector3.up*half.y);
            // Absolute rotation matrix yields the exact enclosing AABB of the occupied OBB.
            var x=localRotation*Vector3.right;var y=localRotation*Vector3.up;var z=localRotation*Vector3.forward;
            var extent=new Vector3(Mathf.Abs(x.x)*half.x+Mathf.Abs(y.x)*half.y+Mathf.Abs(z.x)*half.z,Mathf.Abs(x.y)*half.x+Mathf.Abs(y.y)*half.y+Mathf.Abs(z.y)*half.z,Mathf.Abs(x.z)*half.x+Mathf.Abs(y.z)*half.y+Mathf.Abs(z.z)*half.z)+Vector3.one*clearance;
            // The deck itself supports the wheels/feet: the floor does not need an extra air gap.
            extent.y-=clearance;return interior.Contains(c-extent)&&interior.Contains(c+extent);
        }
    }
    public interface IVehicleCarrier
    {
        string CarrierName {get;}
        SpacePose CarrierPose {get;}
        IVehicleCarrier ParentCarrier {get;}
        VehicleCarrierBay[] VehicleBays {get;}
        bool BoardingAvailable {get;}
        bool Owns(Collider collider);
    }
    public interface IVehicleDeck {bool SampleVehicleDeck(Double3 point,out VehicleGroundContact contact);}
    public interface IVehicleInterior {bool FitsVehicle(VehicleDefinition definition,SpacePose pose);}
    /// <summary>Local parking constraint. Resolving includes carrier angular velocity; rebasing never changes cargo offsets.</summary>
    public sealed class VehicleParking
    {
        public static bool IsCarriedBy(IVehicleCarrier attachedTo,IVehicleCarrier carrier){
            if(carrier==null)return false;
            for(int depth=0;attachedTo!=null&&depth<16;depth++,attachedTo=attachedTo.ParentCarrier)
                if(ReferenceEquals(attachedTo,carrier))return true;
            return false;
        }

        public IVehicleCarrier Carrier {get;private set;}
        public int Bay {get;private set;}=-1;
        public Vector3 LocalPosition {get;private set;}
        public Quaternion LocalRotation {get;private set;}=Quaternion.identity;
        public bool Parked=>Carrier!=null;
        public static Double3 TransportPoint(SpacePose previous,SpacePose current,Double3 point)=>current.position+Double3.Rotate(current.rotation,Double3.Rotate(Quaternion.Inverse(previous.rotation),point-previous.position));
        public static Double3 PointVelocity(SpacePose parent,Double3 point)=>parent.linearVelocity+Double3.Cross(new Double3(parent.angularVelocity),point-parent.position);
        public static SpacePose Resolve(IVehicleCarrier carrier,Vector3 local,Quaternion rotation){
            var parent=carrier.CarrierPose;var offset=Double3.Rotate(parent.rotation,new Double3(local));
            return new SpacePose{frameId=parent.frameId,position=parent.position+offset,rotation=parent.rotation*rotation,linearVelocity=parent.linearVelocity+Double3.Cross(new Double3(parent.angularVelocity),offset),angularVelocity=parent.angularVelocity};
        }
        public bool TryPark(VehicleDefinition definition,SpacePose pose,IVehicleCarrier carrier,out string reason,IVehicleCarrier owner=null){
            reason="";if(carrier==null){reason="No carrier nearby";return false;}
            if(owner!=null){int depth=0;for(var ancestor=carrier;ancestor!=null;ancestor=ancestor.ParentCarrier){if(ancestor==owner||++depth>16){reason="A carrier cannot be parked inside itself or its cargo";return false;}}}
            if(!carrier.BoardingAvailable){reason="Carrier must be stationary with its boarding doors open";return false;}
            var parent=carrier.CarrierPose;if(pose.frameId!=parent.frameId){reason="Vehicle and carrier reference frames differ";return false;}
            if((pose.linearVelocity-parent.linearVelocity).Magnitude>1.5){reason="Stop before securing the vehicle";return false;}
            var local=Double3.Rotate(Quaternion.Inverse(parent.rotation),pose.position-parent.position).Float;var rotation=Quaternion.Inverse(parent.rotation)*pose.rotation;
            for(int i=0;i<carrier.VehicleBays.Length;i++){
                var bay=carrier.VehicleBays[i];if(!bay.FitsEntrance(definition)||!bay.Contains(definition,local,rotation))continue;
                Carrier=carrier;Bay=i;LocalPosition=local;LocalRotation=rotation;return true;
            }
            reason="Vehicle must fit the entrance and be completely inside a parking bay";return false;
        }
        public SpacePose Pose=>Parked?Resolve(Carrier,LocalPosition,LocalRotation):default;
        public bool TryFollow(VehicleDefinition definition,SpacePose pose,IVehicleCarrier carrier){
            if(carrier==null||pose.frameId!=carrier.CarrierPose.frameId)return false;
            var parent=carrier.CarrierPose;var local=Double3.Rotate(Quaternion.Inverse(parent.rotation),pose.position-parent.position).Float;var rotation=Quaternion.Inverse(parent.rotation)*pose.rotation;
            if(carrier is IVehicleInterior interior&&carrier is IVehicleDeck deck){
                if(!deck.SampleVehicleDeck(pose.position,out var support))return false;
                float gap=Vector3.Dot((pose.position-support.position).Float,parent.rotation*Vector3.up);
                if(gap< -1.2f||gap>.65f)return false;
                pose.position=support.position+new Double3(parent.rotation*Vector3.up)*.03;
                if(!interior.FitsVehicle(definition,pose))return false;
                local=Double3.Rotate(Quaternion.Inverse(parent.rotation),pose.position-parent.position).Float;
                Restore(carrier,-1,local,rotation);return true;
            }
            for(int i=0;i<carrier.VehicleBays.Length;i++){
                var bay=carrier.VehicleBays[i];if(!bay.FitsEntrance(definition))continue;
                float floor=bay.interior.min.y;
                if(local.y<floor-1.2f||local.y>floor+.6f)continue;
                local.y=floor+.03f;if(!bay.Contains(definition,local,rotation))continue;
                Restore(carrier,i,local,rotation);return true;
            }
            return false;
        }
        internal void Restore(IVehicleCarrier carrier,int bay,Vector3 local,Quaternion rotation){Carrier=carrier;Bay=bay;LocalPosition=local;LocalRotation=rotation;}
        public void FollowSupport(IVehicleCarrier carrier,SpacePose pose){var parent=carrier.CarrierPose;Restore(carrier,-1,Double3.Rotate(Quaternion.Inverse(parent.rotation),pose.position-parent.position).Float,Quaternion.Inverse(parent.rotation)*pose.rotation);}
        public bool Release(out SpacePose pose){pose=Pose;if(!Parked)return false;Carrier=null;Bay=-1;return true;}
    }
}
