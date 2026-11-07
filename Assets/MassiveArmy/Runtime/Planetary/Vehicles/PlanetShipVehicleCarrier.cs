using UnityEngine;

namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft : IVehicleCarrier, IVehicleDeck, IVehicleInterior
    {
        // The aft central corridor is the straight-through parking lane behind the inner airlock.
        VehicleCarrierBay[] vehicleBays={new VehicleCarrierBay{name="Rear cargo bay",interior=new Bounds(new Vector3(0,1,-23),new Vector3(15,10,28)),entrance=new Vector2(12,9)}};
        public string CarrierName=>"Capital ship";
        public SpacePose CarrierPose=>pose;
        public IVehicleCarrier ParentCarrier=>null;
        public VehicleCarrierBay[] VehicleBays=>vehicleBays;
        public bool BoardingAvailable=>(Landed||Docked)&&Speed<.5f&&DoorFraction(0)>.95f&&DoorFraction(1)>.95f;
        public bool SampleVehicleDeck(Double3 point,out VehicleGroundContact contact){
            var local=BodyToLocal(point);contact=default;if(local.z>=-38&&ModularDeck(point,out contact))return true;
            if(Mathf.Abs(local.x)>(local.z>10?8.9f:25.4f)||local.z< -52||local.z>38)return false;
            float floor=-4;
            if(local.z< -38){if(ramp==null||!ramp.gameObject.activeInHierarchy||Mathf.Abs(local.x)>6)return false;
                var normal=ramp.localRotation*Vector3.up;var top=ramp.localPosition+normal*.2f;
                floor=top.y-(normal.x*(local.x-top.x)+normal.z*(local.z-top.z))/Mathf.Max(.01f,normal.y);
                contact.normal=pose.rotation*normal;
            }else contact.normal=pose.rotation*Vector3.up;
            if(local.y<floor-1.2f)return false;
            contact.position=LocalToBodyPrecise(new Vector3(local.x,floor,local.z));return true;
        }
        public bool FitsVehicle(VehicleDefinition definition,SpacePose vehicle){
            var p=BodyToLocal(vehicle.position);var rotation=Quaternion.Inverse(pose.rotation)*vehicle.rotation;
            for(int x=-1;x<=1;x+=2)for(int y=0;y<=1;y++)for(int z=-1;z<=1;z+=2){
                var corner=p+rotation*new Vector3(x*definition.size.x*.5f,y*definition.size.y,z*definition.size.z*.5f);
                if(!InteriorContains(corner))return false;
            }
            return VehicleCarrierFit.Clear(world,this,definition,vehicle);
        }
    }
}
