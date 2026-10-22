using UnityEngine;
namespace MassiveArmy.Planetary
{
    // One observer capsule queries bounded ship/building colliders. Army records never enter PhysX.
    public sealed class PlanetPlayerCollision
    {
        public const float Radius=.35f,Height=1.8f,Skin=.015f;
        readonly CapsuleCollider capsule;readonly RaycastHit[] hits=new RaycastHit[32];readonly Collider[] overlaps=new Collider[32];
        public PlanetPlayerCollision(CapsuleCollider capsule){this.capsule=capsule;}
        void Ends(Vector3 feet,Vector3 up,out Vector3 a,out Vector3 b){a=feet+up*(Radius+Skin);b=feet+up*(Height-Radius);}
        public bool Clear(Vector3 feet,Vector3 up){Ends(feet,up,out var a,out var b);return !Physics.CheckCapsule(a,b,Radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);}
        bool Cast(Vector3 feet,Vector3 up,Vector3 direction,float distance,out RaycastHit nearest,bool floorOnly=false){Ends(feet,up,out var a,out var b);int count=Physics.CapsuleCastNonAlloc(a,b,Radius,direction,hits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);nearest=default;float best=float.MaxValue;bool found=false;for(int i=0;i<count;i++)if(hits[i].distance<best&&(!floorOnly||Vector3.Dot(hits[i].normal,up)>.55f)){best=hits[i].distance;nearest=hits[i];found=true;}return found;}
        public Vector3 Move(Vector3 feet,Vector3 up,Vector3 delta,bool snap,out Collider support){support=null;var rotation=Quaternion.FromToRotation(Vector3.up,up);
            // Recover a capsule displaced by a moving hull, rather than accepting an initial overlap.
            for(int pass=0;pass<3;pass++){Ends(feet,up,out var a,out var b);int count=Physics.OverlapCapsuleNonAlloc(a,b,Radius,overlaps,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);bool pushed=false;for(int i=0;i<count;i++){var other=overlaps[i];if(Physics.ComputePenetration(capsule,feet,rotation,other,other.transform.position,other.transform.rotation,out var direction,out float depth)){feet+=direction*(depth+Skin);pushed=true;}}if(!pushed)break;}
            for(int slide=0;slide<5&&delta.sqrMagnitude>1e-10f;slide++){float distance=delta.magnitude;if(!Cast(feet,up,delta/distance,distance+Skin,out var hit)){feet+=delta;break;}float step=Mathf.Max(0,Mathf.Min(distance,hit.distance-Skin));feet+=delta/distance*step;delta-=delta/distance*step;if(Vector3.Dot(hit.normal,up)>.55f){support=hit.collider;
                    // Gravity must stop at a walkable surface. Projecting gravity onto
                    // the floor creates a downhill velocity even while standing still.
                    delta-=up*Mathf.Min(0,Vector3.Dot(delta,up));
                }delta=Vector3.ProjectOnPlane(delta,hit.normal);}
            if(snap&&Cast(feet+up*.06f,up,-up,.32f,out var floor,true)&&Vector3.Dot(floor.normal,up)>.55f){feet-=up*Mathf.Max(-.06f,floor.distance-.06f-Skin);support=floor.collider;}
            return feet;
        }
    }
}
