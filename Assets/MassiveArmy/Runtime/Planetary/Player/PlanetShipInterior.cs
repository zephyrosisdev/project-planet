using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        sealed class Hatch {public Transform panel;public Vector3 closed;public float fraction;public bool requested;public string name;}
        readonly List<Hatch> hatches=new List<Hatch>(2);readonly List<Mesh> interiorMeshes=new List<Mesh>();readonly List<Light> cabinLights=new List<Light>();
        readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();int interiorFrame=-1;
        Double3 rampPosition;Quaternion rampRotation;bool rampPrepared;VolumeProfile cabinExposure;Material window;Transform ramp;public static readonly Vector3 PilotEye=new Vector3(0,-1.5f,31);
        public static readonly Vector3 PilotExit=new Vector3(1.8f,-4,27);
        public bool PressurizedAt(Vector3 local)=>InteriorContains(local)&&(local.z<-31?hatches[0].fraction<.01f:hatches[0].fraction<.01f||hatches[1].fraction<.01f);
        public bool Sealed=>hatches.TrueForAll(h=>h.fraction<.01f);
        public int DoorCount=>hatches.Count;
        internal int ActiveCabinLights{get{int count=0;foreach(var light in cabinLights)if(light.enabled)count++;return count;}}
        internal void CopyDoorsFrom(PlanetSpacecraft other){for(int i=0;i<hatches.Count;i++){hatches[i].fraction=other.hatches[i].fraction;hatches[i].requested=other.hatches[i].requested;}interiorFrame=-1;}
        public float DoorFraction(int index)=>hatches[index].fraction;
        public UniversePosition Position=>world.definition.position.Add(pose.position);
        public Quaternion Rotation=>pose.rotation;
        public Vector3 LocalToRender(Vector3 local)=>origin.RenderPosition(Position.Add(Double3.Rotate(pose.rotation,new Double3(local))));
        public Vector3 RenderToLocal(Vector3 render)=>Quaternion.Inverse(pose.rotation)*(render-origin.RenderPosition(Position));
        public Vector3 BodyToLocal(Vector3 body)=>BodyToLocal(new Double3(body));
        public Vector3 BodyToLocal(Double3 body)=>Double3.Rotate(Quaternion.Inverse(pose.rotation),body-pose.position).Float;
        public Double3 LocalToBodyPrecise(Vector3 local)=>pose.position+Double3.Rotate(pose.rotation,new Double3(local));
        public Vector3 LocalToBody(Vector3 local)=>LocalToBodyPrecise(local).Float;
        public UniversePose ResolvePlayer(Vector3 local,Quaternion rotation)=>frames.Resolve(new SpacePose{frameId=1,position=new Double3(local),rotation=rotation});
        public Vector3 VelocityAt(Vector3 local)=>pose.linearVelocity.Float+Vector3.Cross(pose.angularVelocity,pose.rotation*local);
        public bool Owns(Collider collider)=>collider!=null&&collider.transform.IsChildOf(hull.transform);
        public bool NearDeck(Vector3 local)=>Mathf.Abs(local.x)<27&&local.z>-52&&local.z<39&&local.y>-11&&local.y<4;
        public bool InteriorContains(Vector3 local)=>local.y>-4.3f&&local.y<2&&local.z>-38&&local.z<38&&Mathf.Abs(local.x)<(local.z>10?8.8f:25.2f);
        public string RoomName(Vector3 local)=>local.z<-31?"AIRLOCK":local.z>22?"BRIDGE":local.x<-8?"COMMAND ROOM":local.x>8?(local.z<-10?"ENGINE ROOM":"VEHICLE BAY"):"MAIN CORRIDOR";
        Transform Part(string name,Vector3 p,Vector3 scale,Material material,Quaternion? rotation=null,bool dynamic=false){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(hull.transform,false);go.transform.localPosition=p;go.transform.localRotation=rotation??Quaternion.identity;go.transform.localScale=scale;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;
            if(!dynamic){if(!batches.TryGetValue(material,out var list)){list=new List<CombineInstance>();batches.Add(material,list);}list.Add(new CombineInstance{mesh=go.GetComponent<MeshFilter>().sharedMesh,transform=Matrix4x4.TRS(p,go.transform.localRotation,scale)});renderer.enabled=false;}return go.transform;}
        void HatchAt(string name,float z,float halfWidth){Part(name+" port frame",new Vector3(-(halfWidth+1.5f)*.5f,-1,z),new Vector3(halfWidth-1.5f,6,.5f),steel);Part(name+" starboard frame",new Vector3((halfWidth+1.5f)*.5f,-1,z),new Vector3(halfWidth-1.5f,6,.5f),steel);Part(name+" header",new Vector3(0,.5f,z),new Vector3(3,3,.5f),steel);var p=new Vector3(0,-2.5f,z);hatches.Add(new Hatch{panel=Part(name,p,new Vector3(3,3,.4f),glass,dynamic:true),closed=p,name=name});}
        void CabinLight(Vector3 local){var go=new GameObject("Local cabin light");go.transform.SetParent(hull.transform,false);go.transform.localPosition=local;var light=go.AddComponent<Light>();light.type=LightType.Point;go.AddComponent<HDAdditionalLightData>();light.lightUnit=LightUnit.Lumen;light.intensity=300000;light.color=new Color(.7f,.86f,1);light.range=24;light.shadows=LightShadows.None;light.enabled=false;cabinLights.Add(light);}
        void BuildInterior(){
            var exposureHost=new GameObject("Cabin eye adaptation");exposureHost.transform.SetParent(hull.transform,false);exposureHost.transform.localPosition=new Vector3(0,-1,-8);var region=exposureHost.AddComponent<BoxCollider>();region.isTrigger=true;region.size=new Vector3(53,8,96);var volume=exposureHost.AddComponent<Volume>();volume.isGlobal=false;volume.priority=200;volume.blendDistance=2;cabinExposure=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=cabinExposure;var exposure=cabinExposure.Add<Exposure>();exposure.mode.Override(ExposureMode.Fixed);exposure.fixedExposure.Override(Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings").exposure-4);

            window=new Material(glass);window.SetFloat("_SurfaceType",1);window.SetFloat("_BlendMode",0);window.SetFloat("_EnableBlendModePreserveSpecularLighting",0);window.SetFloat("_TransparentZWrite",0);window.SetColor("_BaseColor",new Color(.04f,.2f,.3f,.12f));window.renderQueue=(int)RenderQueue.Transparent;HDMaterial.ValidateMaterial(window);
            Part("Aft deck",new Vector3(0,-4.25f,-14),new Vector3(51,.5f,48),steel);Part("Bridge deck",new Vector3(0,-4.25f,24),new Vector3(18,.5f,28),steel);
            Part("Aft roof",new Vector3(0,2.25f,-14),new Vector3(52,.5f,49),steel);Part("Bridge roof",new Vector3(0,2.25f,24),new Vector3(19,.5f,29),steel);
            foreach(int side in new[]{-1,1}){Part("Outer room wall",new Vector3(side*25.75f,-1,-14),new Vector3(.5f,6,48),steel);Part("Bridge side wall",new Vector3(side*9.25f,-1,24),new Vector3(.5f,6,28),steel);Part("Shoulder bulkhead",new Vector3(side*17.5f,-1,10),new Vector3(16.5f,6,.5f),steel);}
            Part("Front windshield",new Vector3(0,-1,38.25f),new Vector3(18,6,.5f),window);
            Part("Command aft wall",new Vector3(-8,-1,-24),new Vector3(.4f,6,28),steel);Part("Command forward wall",new Vector3(-8,-1,2),new Vector3(.4f,6,16),steel);
            Part("Engine aft wall",new Vector3(8,-1,-32),new Vector3(.4f,6,12),steel);Part("Bay forward wall",new Vector3(8,-1,-6),new Vector3(.4f,6,32),steel);
            Part("Engine bay divider left",new Vector3(11.5f,-1,-10),new Vector3(7,6,.4f),steel);Part("Engine bay divider right",new Vector3(23,-1,-10),new Vector3(5.5f,6,.4f),steel);
            HatchAt("REAR HATCH",-38.25f,25.5f);HatchAt("INNER AIRLOCK",-31,8);
            var normal=new Vector3(0,12,-4).normalized;ramp=Part("Boarding ramp",new Vector3(0,-6,-44)-normal*.2f,new Vector3(4,.4f,Mathf.Sqrt(160)),steel,Quaternion.Euler(-Mathf.Atan2(4,12)*Mathf.Rad2Deg,0,0),true);
            Part("Pilot chair",new Vector3(0,-3.6f,30),new Vector3(1.2f,.8f,1.2f),steel);Part("Pilot backrest",new Vector3(0,-2.8f,29.5f),new Vector3(1.2f,1.3f,.25f),steel);Part("Flight console",new Vector3(0,-2.9f,33),new Vector3(4,1.4f,1),glass);
            Part("Command table",new Vector3(-17,-3.4f,-2),new Vector3(5,1.2f,3),glass);Part("Engine core",new Vector3(18,-2.3f,-30),new Vector3(5,3.4f,7),engine);Part("Bay supplies",new Vector3(22,-3.2f,5),new Vector3(3,1.6f,4),steel);
            foreach(int side in new[]{-1,1}){Part("Engine exhaust",new Vector3(side*19,-1,-39),new Vector3(9,4,1.5f),engine);Part("Landing strut",new Vector3(side*18,-6,12),new Vector3(3,4,12),steel);}
            CabinLight(new Vector3(0,0,-43));CabinLight(new Vector3(0,1,25));CabinLight(new Vector3(0,1,-31));CabinLight(new Vector3(0,1,-5));CabinLight(new Vector3(-18,1,-8));CabinLight(new Vector3(18,1,-15));
            foreach(var batch in batches){var mesh=new Mesh{name="Combined ship interior"};mesh.CombineMeshes(batch.Value.ToArray(),true,true);interiorMeshes.Add(mesh);var visual=new GameObject("Shared ship shell");visual.transform.SetParent(hull.transform,false);visual.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=visual.AddComponent<MeshRenderer>();renderer.sharedMaterial=batch.Key;renderer.shadowCastingMode=batch.Key==window?ShadowCastingMode.Off:ShadowCastingMode.On;}batches.Clear();
        }
        bool Occupied(Hatch hatch){var player=world.PlayerForValidation;if(player==null||!player.Active&&(!player.AttachedToShip||world.IsPiloting))return false;var local=player.AttachedToShip?player.ShipLocalPosition:BodyToLocal(player.PreciseBodyPosition);return local.y>-5.8f&&local.y<1&&local.x>-2&&local.x<6&&Mathf.Abs(local.z-hatch.closed.z)<.95f;}
        public bool ToggleDoor(int index){var door=hatches[index];if(door.requested&&Occupied(door)){world.Base.Notice="Clear the hatch before closing it";return false;}door.requested=!door.requested;return true;}
        void UpdateInterior(){if(interiorFrame==Time.frameCount)return;interiorFrame=Time.frameCount;
            foreach(var door in hatches){if(!door.requested&&door.fraction>0&&Occupied(door))door.requested=true;door.fraction=Mathf.MoveTowards(door.fraction,door.requested?1:0,Mathf.Min(Time.deltaTime,.05f)*2);door.panel.localPosition=door.closed+Vector3.right*(door.fraction*3.7f);}
            ramp.gameObject.SetActive(Landed&&GearDeployed);if(Landed&&GearDeployed&&(!rampPrepared||(pose.position-rampPosition).Magnitude>.02||Quaternion.Angle(pose.rotation,rampRotation)>.01f)){var body=LocalToBody(new Vector3(0,0,-52));var direction=body.normalized;var foot=BodyToLocal(surface.GetSurfacePosition(direction,.05f));var start=new Vector3(0,foot.y,-52);var end=new Vector3(0,-4,-38);var delta=end-start;var rotation=Quaternion.Euler(-Mathf.Atan2(delta.y,delta.z)*Mathf.Rad2Deg,0,0);ramp.localPosition=(start+end)*.5f-rotation*Vector3.up*.2f;ramp.localRotation=rotation;ramp.localScale=new Vector3(4,.4f,delta.magnitude);rampPosition=pose.position;rampRotation=pose.rotation;rampPrepared=true;}var observer=world.PlayerForValidation;var eye=world.IsPiloting?PilotEye:world.IsFirstPerson&&observer!=null&&observer.AttachedToShip?observer.ShipLocalPosition+Vector3.up*observer.EyeHeight:RenderToLocal(camera.transform.position);int first=-1,second=-1;float firstDistance=float.MaxValue,secondDistance=float.MaxValue;for(int i=0;i<cabinLights.Count;i++){float distance=(cabinLights[i].transform.localPosition-eye).sqrMagnitude;if(distance<firstDistance){second=first;secondDistance=firstDistance;first=i;firstDistance=distance;}else if(distance<secondDistance){second=i;secondDistance=distance;}}for(int i=0;i<cabinLights.Count;i++)cabinLights[i].enabled=(i==first&&firstDistance<24*24||i==second&&secondDistance<24*24);
        }
        public int FindInteraction(Vector3 eye,Vector3 look){float best=3.5f;int found=-1;for(int i=0;i<=hatches.Count;i++){var local=i<hatches.Count?hatches[i].closed:new Vector3(0,-2.7f,30);var point=LocalToRender(local);var delta=point-eye;float distance=delta.magnitude;if(distance>best||distance<.01f||Vector3.Dot(look,delta/distance)<.65f)continue;
                if(Physics.Raycast(eye,delta/distance,out var hit,distance-.2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&!(i<hatches.Count&&hit.transform==hatches[i].panel)&&!(i==hatches.Count&&hit.transform.name.StartsWith("Pilot ")))continue;best=distance;found=i;}return found;}
        public string InteractionPrompt(int interaction)=>interaction<0?"":interaction<hatches.Count?"F - "+(hatches[interaction].requested?"CLOSE ":"OPEN ")+hatches[interaction].name:"F - PILOT SEAT";
        public bool CanExitSeat(PlanetPlayerCollision collision,out Vector3 local){foreach(var candidate in new[]{PilotExit,new Vector3(-1.8f,-4,27),new Vector3(0,-4,25)})if(collision.Clear(LocalToRender(candidate),pose.rotation*Vector3.up)){local=candidate;return true;}local=default;return false;}
        void DisposeInterior(){ArmyLifetime.Destroy(cabinExposure);ArmyLifetime.Destroy(window);foreach(var mesh in interiorMeshes)ArmyLifetime.Destroy(mesh);}
    }
}
