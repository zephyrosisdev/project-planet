using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    internal sealed class RespawnPointView:IDisposable {
        static readonly string[] Types={"sleeping-bag","sleeping-pod"};readonly PlanetDevelopment world;readonly Material material,previewMaterial;readonly Mesh bag,pod;readonly BoxCollider[] contacts=new BoxCollider[16];readonly Collider[] overlaps=new Collider[32];readonly Matrix4x4[] instances=new Matrix4x4[128];
        internal string Placing {get;private set;}internal string Notice="";FramePose candidate;bool valid;int turn;double inputReady;
        readonly RespawnPoint[] contactPoints=new RespawnPoint[16];
        struct ContactPose {public RespawnPoint point;public Vector3 position,size;public Quaternion rotation;public float distance;}
        readonly List<ContactPose> close=new List<ContactPose>(128);
        internal static Vector3 Size(string id)=>id=="sleeping-pod"?new Vector3(1.8f,1.5f,2.6f):new Vector3(.9f,.24f,2.1f);
        internal RespawnPointView(PlanetDevelopment world){
            this.world=world;bag=Model(false);pod=Model(true);material=ColonyPrototypeLibrary.Material("Respawn point prototypes");previewMaterial=ColonyPrototypeLibrary.Material("Respawn placement");previewMaterial.SetFloat("_Preview",1);previewMaterial.SetFloat("_ZWrite",0);previewMaterial.renderQueue=3000;
            for(int i=0;i<contacts.Length;i++){var go=new GameObject("Nearby sleeping point contact");go.transform.SetParent(world.transform);contacts[i]=go.AddComponent<BoxCollider>();go.SetActive(false);}
        }
        internal static Mesh Model(bool pod){
            var g=new ModularGeometry();var shell=new Color(.73f,.78f,.8f,0);var fabric=new Color(.15f,.33f,.28f,0);var dark=new Color(.045f,.065f,.08f,0);var amber=new Color(1,.48f,.06f,0);var cyan=new Color(.1f,.85f,1,1);
            if(!pod){g.Box(new Vector3(0,.08f,0),new Vector3(.9f,.16f,2.1f),dark);g.Box(new Vector3(0,.17f,-.18f),new Vector3(.82f,.1f,1.65f),fabric);g.Box(new Vector3(0,.18f,.76f),new Vector3(.72f,.08f,.4f),shell);g.Box(new Vector3(.36f,.23f,-.2f),new Vector3(.025f,.015f,1.5f),amber);}
            else {g.Box(new Vector3(0,.25f,0),new Vector3(1.4f,.5f,2.6f),dark);g.Box(new Vector3(0,.53f,0),new Vector3(1.15f,.12f,2.35f),fabric);g.Box(new Vector3(0,.63f,.86f),new Vector3(.9f,.15f,.5f),shell);for(int side=-1;side<=1;side+=2){g.Box(new Vector3(side*.64f,.82f,0),new Vector3(.12f,.65f,2.6f),shell);g.Box(new Vector3(side*.56f,1.05f,0),new Vector3(.04f,.05f,2.35f),cyan);}g.Box(new Vector3(0,1.1f,1.2f),new Vector3(1.4f,.8f,.2f),shell);g.Box(new Vector3(0,1.47f,.8f),new Vector3(1.4f,.06f,.9f),shell);g.Box(new Vector3(.78f,.95f,.75f),new Vector3(.22f,.2f,.35f),amber);}
            return g.Build(pod?"Sleeping pod prototype":"Sleeping bag prototype");
        }
        internal bool Owns(Collider c){foreach(var contact in contacts)if(contact==c)return true;return false;}
        internal void Begin(string item){Placing=item;turn=0;inputReady=Time.unscaledTimeAsDouble+.2;Notice="LMB place · R rotate · Esc cancel";}
        internal void Cancel(){Placing=null;valid=false;}
        internal void Tick(){
            var data=world.IndustrialAuthority?.World;if(data==null)return;close.Clear();int count=0;var bounds=new Bounds(world.View.transform.position,Vector3.one*1400);
            foreach(string type in Types){int batch=0;foreach(var point in data.respawnPoints){if(point.definition!=type||!world.TryResolveRespawnPose(point.pose,out var body,out var rotation))continue;var at=world.RenderBodyPosition(body);if((at-world.View.transform.position).sqrMagnitude>600*600)continue;instances[batch++]=Matrix4x4.TRS(at,rotation,Vector3.one);if(batch==instances.Length){Draw(type,material,batch,bounds);batch=0;}
                    float distance=(at-world.View.transform.position).sqrMagnitude;if(distance<24*24)close.Add(new ContactPose{point=point,position=at,rotation=rotation,size=Size(type),distance=distance});}
                if(batch>0)Draw(type,material,batch,bounds);
            }close.Sort((a,b)=>a.distance.CompareTo(b.distance));foreach(var pose in close){if(count==contacts.Length)break;contactPoints[count]=pose.point;var contact=contacts[count++];contact.transform.SetPositionAndRotation(pose.position,pose.rotation);contact.size=pose.size;contact.center=Vector3.up*contact.size.y*.5f;contact.gameObject.SetActive(true);}while(count<contacts.Length)contacts[count++].gameObject.SetActive(false);
            if(Placing==null||world.LocalPlayerDead||world.Paused||world.PersonalMenuOpen)return;
            if(Time.unscaledTimeAsDouble>=inputReady&&Keyboard.current?.escapeKey.wasPressedThisFrame==true){Cancel();return;}if(Keyboard.current?.rKey.wasPressedThisFrame==true)turn=(turn+1)%4;
            FindPlacement();previewMaterial.SetColor("_PreviewTint",valid?new Color(.1f,1,.6f,.55f):new Color(1,.15f,.1f,.55f));if(world.TryResolveRespawnPose(candidate,out var preview,out var q)){instances[0]=Matrix4x4.TRS(world.RenderBodyPosition(preview),q,Vector3.one);Draw(Placing,previewMaterial,1,bounds);}
            if(Application.isFocused&&Time.unscaledTimeAsDouble>=inputReady&&Mouse.current?.leftButton.wasPressedThisFrame==true){if(valid){Notice=world.IndustrialAuthority.PlaceRespawnPoint(1,Placing,candidate,world.RespawnBodyPose(candidate))??"Respawn point placed · ready in 2 minutes";world.ClearConsumedEquipment();if(Placing!=null&&(world.EquipmentInventory?world.PersonalInventory.Equipped==Placing:data.creativeMode||data.Actor(1).inventory.Count(Placing)>0))inputReady=Time.unscaledTimeAsDouble+.25;else Cancel();world.IndustrialAuthority.Save();}world.SurvivalNotice=Notice;}
        }
        void Draw(string type,Material mat,int count,Bounds bounds)=>Graphics.RenderMeshInstanced(new RenderParams(mat){camera=world.View,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},type=="sleeping-pod"?pod:bag,0,instances,count);
        void FindPlacement(){
            valid=false;var ray=new Ray(world.View.transform.position,world.View.transform.forward);var ship=world.ShipForValidation;bool aboard=world.PlayerForValidation.AttachedToShip&&world.HasRespawnCarrier;
            bool physical=Physics.Raycast(ray,out var hit,10,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);Double3 terrain=default;if(!physical&&(aboard||!world.RespawnSurfaceHit(ray,out terrain))){Notice="Aim at a floor within 10 m";candidate=world.RespawnPose(world.RenderToBody(ray.GetPoint(4)),turn*90);return;}
            if(Owns(hit.collider)){Notice="Choose an empty floor";return;}var point=physical?world.RenderToBody(hit.point):terrain;var up=aboard?ship.Rotation*Vector3.up:PlanetPrecisionPolicy.Direction(point);
            if(physical&&Vector3.Dot(hit.normal,up)<.92f||aboard&&!ship.Owns(hit.collider)){Notice="Choose a flat supported floor";return;}
            candidate=aboard?new FramePose{body=world.definition.bodyId,frame=1,x=ship.RenderToLocal(hit.point).x,y=ship.RenderToLocal(hit.point).y+.025,z=ship.RenderToLocal(hit.point).z,yaw=turn*90}:world.RespawnPose(point+new Double3(up)*.025,turn*90);
            if(aboard&&!ship.InteriorContains(new Vector3((float)candidate.x,(float)candidate.y,(float)candidate.z))){Notice="Place ship respawn points inside the hull";return;}
            // The carrier and attached player move after the industry tick. Validate reach
            // against this frame's player pose, just like the candidate and ship colliders.
            world.IndustrialAuthority.World.Actor(1).pose=world.RespawnPose(world.PlayerForValidation.PreciseBodyPosition);
            var resolved=world.RespawnBodyPose(candidate);Notice=world.IndustrialAuthority.World.PrepareRespawnPlacement(1,Placing,candidate,resolved,out _);if(Notice!=null)return;
            world.TryResolveRespawnPose(candidate,out _,out var rotation);var size=Size(Placing);int found=Physics.OverlapBoxNonAlloc(world.RenderBodyPosition(new Double3(resolved.x,resolved.y,resolved.z))+rotation*Vector3.up*(size.y*.5f+.015f),size*.5f-Vector3.one*.01f,overlaps,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(found>0){Notice="Respawn point overlaps "+overlaps[0].name;return;}Notice="Ready · LMB place · R rotate · Esc cancel";valid=true;
        }
        internal void RebaseShipContacts(){for(int i=0;i<contacts.Length;i++)if(contacts[i].gameObject.activeSelf&&contactPoints[i]?.pose.frame==1&&world.TryResolveRespawnPose(contactPoints[i].pose,out var body,out var q))contacts[i].transform.SetPositionAndRotation(world.RenderBodyPosition(body),q);}
        internal RespawnPoint Aim(){if(world.LocalPlayerDead)return null;var eye=world.View.transform.position;var direction=world.View.transform.forward;RespawnPoint best=null;float nearest=4;if(Physics.Raycast(eye,direction,out var obstruction,4,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&!Owns(obstruction.collider))nearest=obstruction.distance;foreach(var point in world.IndustrialAuthority.World.respawnPoints){if(point.owner!=1||!world.TryResolveRespawnPose(point.pose,out var body,out var rotation))continue;var delta=world.RenderBodyPosition(body)+rotation*Vector3.up*.3f-eye;float along=Vector3.Dot(delta,direction);if(along<0||along>nearest||(delta-direction*along).sqrMagnitude>.8f*.8f)continue;nearest=along;best=point;}return best;}
        public void Dispose(){foreach(var c in contacts)ArmyLifetime.Destroy(c.gameObject);ArmyLifetime.Destroy(bag);ArmyLifetime.Destroy(pod);ArmyLifetime.Destroy(material);ArmyLifetime.Destroy(previewMaterial);}
    }
}






