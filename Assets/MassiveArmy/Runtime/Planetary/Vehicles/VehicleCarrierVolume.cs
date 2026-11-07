using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    public static class VehicleCarrierFit
    {
        static readonly Collider[] overlaps=new Collider[64];
        public static bool Clear(PlanetDevelopment world,IVehicleCarrier carrier,VehicleDefinition definition,SpacePose pose){
            var half=definition.size*.5f;var centre=world.RenderBodyPosition(pose.position)+pose.rotation*(Vector3.up*half.y);
            int count=Physics.OverlapBoxNonAlloc(centre,Vector3.Max(Vector3.one*.01f,half-Vector3.one*.06f),overlaps,pose.rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(carrier.Owns(overlaps[i]))return false;
            return true;
        }
    }
    /// <summary>Reusable support adapter for authored ships/vehicles. Only actual carrier actors register, never GPU army units.</summary>
    public sealed class VehicleCarrierVolume:MonoBehaviour,IVehicleCarrier,IVehicleDeck,IVehicleInterior
    {
        internal static readonly List<VehicleCarrierVolume> Active=new List<VehicleCarrierVolume>();
        public PlanetDevelopment world;
        public MonoBehaviour parentCarrier;
        public Bounds[] interiors={new Bounds(new Vector3(0,3,0),new Vector3(10,6,20))};
        readonly RaycastHit[] hits=new RaycastHit[32];
        SpacePose previous;Double3 velocity;Vector3 angularVelocity;
        public string CarrierName=>name;
        public SpacePose CarrierPose=>new SpacePose{position=world.RenderToBody(transform.position),rotation=transform.rotation,linearVelocity=velocity,angularVelocity=angularVelocity};
        public IVehicleCarrier ParentCarrier=>parentCarrier as IVehicleCarrier;
        public VehicleCarrierBay[] VehicleBays=>System.Array.Empty<VehicleCarrierBay>();
        public bool BoardingAvailable=>true;
        public bool Owns(Collider collider)=>collider!=null&&collider.transform.IsChildOf(transform);
        void OnEnable(){if(world==null)world=GetComponentInParent<PlanetDevelopment>();if(world!=null&&!Active.Contains(this))Active.Add(this);if(world!=null)previous=CarrierPose;}
        void OnDisable(){Active.Remove(this);}
        void LateUpdate(){if(world==null)return;var pose=CarrierPose;float dt=Time.deltaTime;if(dt>0){velocity=(pose.position-previous.position)*(1/dt);var q=pose.rotation*Quaternion.Inverse(previous.rotation);q.ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;angularVelocity=axis*(angle*Mathf.Deg2Rad/dt);}previous=pose;}
        public bool SampleVehicleDeck(Double3 point,out VehicleGroundContact contact){
            contact=default;if(world==null)return false;var up=transform.up;var render=world.RenderBodyPosition(point);float nearest=4;bool found=false;
            int count=Physics.RaycastNonAlloc(render+up*1.25f,-up,hits,4,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)if(Owns(hits[i].collider)&&hits[i].distance<nearest&&Vector3.Dot(hits[i].normal,up)>.55f){nearest=hits[i].distance;found=true;contact=new VehicleGroundContact{position=world.RenderToBody(hits[i].point),normal=hits[i].normal,collider=hits[i].collider};}
            if(found)return true;
            // Author-defined room floors also provide support during collider streaming/rebases.
            // Split volumes around genuine holes instead of defining a floor across them.
            var local=transform.InverseTransformPoint(render);float floor=float.NegativeInfinity;
            foreach(var room in interiors)if(local.x>=room.min.x&&local.x<=room.max.x&&local.z>=room.min.z&&local.z<=room.max.z&&local.y>=room.min.y-1.2f&&local.y<=room.max.y)floor=Mathf.Max(floor,room.min.y);
            if(float.IsNegativeInfinity(floor))return false;
            contact=new VehicleGroundContact{position=world.RenderToBody(transform.TransformPoint(new Vector3(local.x,floor,local.z))),normal=up};return true;
        }
        public bool FitsVehicle(VehicleDefinition definition,SpacePose pose){
            if(world==null)return false;var local=transform.InverseTransformPoint(world.RenderBodyPosition(pose.position));var rotation=Quaternion.Inverse(transform.rotation)*pose.rotation;
            for(int x=-1;x<=1;x+=2)for(int y=0;y<=1;y++)for(int z=-1;z<=1;z+=2){var corner=local+rotation*new Vector3(x*definition.size.x*.5f,y*definition.size.y,z*definition.size.z*.5f);bool inside=false;foreach(var bounds in interiors)if(bounds.Contains(corner)){inside=true;break;}if(!inside)return false;}
            return VehicleCarrierFit.Clear(world,this,definition,pose);
        }
    }
}
