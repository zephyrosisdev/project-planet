using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    // Ship-local CPU solids share the authored catalogue used by the offline
    // renderer, with the same socket caps, cockpit fittings and pressure doors.
    internal sealed class SurvivalShipInteriorModel {
        [Serializable] sealed class Entry {public string id;public ColonyPrototypeSolid[] solids;}
        [Serializable] sealed class Catalogue {public int version;public Entry[] assets;}
        internal readonly struct Fitting {
            internal readonly Vector3 centre,size;internal readonly Quaternion rotation;internal readonly bool visible;
            internal Fitting(Vector3 centre,Vector3 size,Quaternion rotation,bool visible){this.centre=centre;this.size=size;this.rotation=rotation;this.visible=visible;}
        }
        static Dictionary<string,Entry> profiles;
        internal readonly SurvivalContacts contacts=new SurvivalContacts();internal readonly List<Fitting> fittings=new List<Fitting>();
        internal readonly ShipAssembly assembly;internal readonly long revision;internal readonly int body;internal readonly bool rearOpen,innerOpen;long nextShape;
        internal static ColonyPrototypeSolid[] Solids(string id){
            if(profiles==null){var source=Resources.Load<TextAsset>("MassiveArmy/ShipArt/ModularShipCatalogue");if(source==null)throw new InvalidOperationException("Ship collision catalogue is missing");Catalogue data;
                try{data=JsonUtility.FromJson<Catalogue>(source.text);}finally{Resources.UnloadAsset(source);}if(data.version!=1)throw new InvalidOperationException("Unsupported ship collision catalogue");
                profiles=new Dictionary<string,Entry>();foreach(var entry in data.assets)profiles.Add(entry.id,entry);
            }return profiles.TryGetValue(id,out var profile)?profile.solids:throw new InvalidOperationException("Unknown ship collision profile");
        }
        internal SurvivalShipInteriorModel(ShipAssembly assembly,SurvivalShipState ship){
            this.assembly=assembly;revision=assembly.revision;body=ship.pose.body;rearOpen=ship.rearOpen;innerOpen=ship.innerOpen;
            foreach(var module in assembly.modules){
                var source=Solids(module.definition);var solids=new BuildSolid[source.Length];for(int i=0;i<source.Length;i++)solids[i]=source[i].Shape();var q=Quaternion.Euler(0,module.pose.yaw,0);var at=new Vector3((float)module.pose.x,(float)module.pose.y,(float)module.pose.z);Put(at,q,solids);
                var spec=ShipModules.Definitions[module.definition];
                if(module.definition=="cockpit"){
                    Box(Point(module,ShipModules.P(0,-2.6,0)),new Vector3(1.2f,.8f,1.2f),q);
                    Box(Point(module,ShipModules.P(0,-1.8,-.5)),new Vector3(1.2f,1.3f,.25f),q);
                    if(!assembly.Occupied(module.id,"forward"))Box(Point(module,ShipModules.P(0,0,spec.depth/2+.25)),new Vector3((float)spec.width-1,6,.5f),q,false);
                }
                foreach(var socket in spec.sockets)if(socket.kind=="Hull"&&!assembly.Occupied(module.id,socket.name)&&!(module.parent!=0&&module.inputSocket==socket.name)){
                    if(module.parent==0&&socket.name=="aft"||module.definition=="cockpit"&&socket.name=="forward")continue;
                    float width=module.definition=="hangar"?(socket.name=="engineering"?12:19):(float)spec.width;
                    Box(Point(module,socket.pose),new Vector3(width,7,.35f),Quaternion.Euler(0,module.pose.yaw+socket.pose.yaw,0));
                }
            }
            Box(new Vector3(0,4.25f,10),new Vector3(18.5f,3.5f,.5f),Quaternion.identity);
            foreach(int side in new[]{-1,1})Box(new Vector3(side*17.5f,1,10),new Vector3(16.5f,10,.5f),Quaternion.identity);
            Hatch(-38.25f,25.5f,rearOpen,1.2f);Hatch(-31,8,innerOpen,.4f);
        }
        static Vector3 Point(ShipModuleRecord module,FramePose local){var p=ShipModules.Transform(module.pose,local);return new Vector3((float)p.x,(float)p.y,(float)p.z);}
        void Put(Vector3 at,Quaternion rotation,BuildSolid[] solids){
            double radius=0;foreach(var solid in solids)foreach(var p in solid.polygon)radius=Math.Max(radius,Math.Sqrt(p.x*p.x+p.y*p.y+Math.Max(solid.bottom*solid.bottom,solid.top*solid.top)));
            contacts.Put(new SurvivalContacts.Shape{id=++nextShape,body=body,origin=new Double3(at),rotation=rotation,solids=solids,radius=radius});
        }
        void Box(Vector3 centre,Vector3 size,Quaternion rotation,bool visible=true){var half=size*.5f;Put(centre,rotation,new[]{BuildSolid.Box(-half.x,half.x,-half.z,half.z,-half.y,half.y)});fittings.Add(new Fitting(centre,size,rotation,visible));}
        void Hatch(float z,float halfWidth,bool open,float aftStandOff){
            foreach(int side in new[]{-1,1})Box(new Vector3(side*(halfWidth+6)*.5f,1,z),new Vector3(halfWidth-6,10,.5f),Quaternion.identity);
            Box(new Vector3(0,5.5f,z),new Vector3(12,1,.5f),Quaternion.identity);
            Box(new Vector3(open?12.6f:0,.5f,z),new Vector3(12,9,.4f),Quaternion.identity);
            foreach(float offset in new[]{-aftStandOff,.4f})Box(new Vector3(-6.55f,-2.55f,z+offset),new Vector3(.6f,.9f,.18f),Quaternion.identity);
            if(aftStandOff>.4f)Box(new Vector3(-6.55f,-2.55f,z-(aftStandOff+.25f)*.5f),new Vector3(.24f,.45f,aftStandOff-.25f),Quaternion.identity);
        }
        internal bool Matches(ShipAssembly layout,SurvivalShipState ship)=>ReferenceEquals(assembly,layout)&&revision==layout.revision&&body==ship.pose.body&&rearOpen==ship.rearOpen&&innerOpen==ship.innerOpen;
        internal static bool Contains(ShipAssembly layout,FramePose local,float padding=.4f){
            foreach(var module in layout.modules){var d=ShipModules.Definitions[module.definition];if(!d.room)continue;var relative=Quaternion.Euler(0,-module.pose.yaw,0)*new Vector3((float)(local.x-module.pose.x),(float)(local.y-module.pose.y),(float)(local.z-module.pose.z));
                if(Math.Abs(relative.x)<d.width*.5+padding&&Math.Abs(relative.z)<d.depth*.5+padding&&relative.y>-d.height*.5-padding&&relative.y<d.height*.5+padding)return true;
            }return false;
        }
        internal bool Pressurized(FramePose local)=>Contains(assembly,local,0)&&(local.z<-31?!rearOpen:!rearOpen||!innerOpen);
        internal static Quaternion Rotation(SurvivalShipState ship)=>new Quaternion(ship.rotationX,ship.rotationY,ship.rotationZ,ship.rotationW).normalized;
        internal static FramePose World(SurvivalShipState ship,FramePose local){var at=new Double3(ship.pose.x,ship.pose.y,ship.pose.z)+Double3.Rotate(Rotation(ship),new Double3(local.x,local.y,local.z));return new FramePose{body=ship.pose.body,x=at.x,y=at.y,z=at.z,yaw=local.yaw};}
        internal static FramePose Local(SurvivalShipState ship,FramePose world){var at=Double3.Rotate(Quaternion.Inverse(Rotation(ship)),new Double3(world.x-ship.pose.x,world.y-ship.pose.y,world.z-ship.pose.z));return new FramePose{body=ship.pose.body,frame=ship.entity,x=at.x,y=at.y,z=at.z,yaw=world.yaw};}
        internal static void Step(SurvivalMotion motion,SurvivalInput input,float dt,double speed,SurvivalShipInteriorModel model){
            var previous=motion.pose;bool grounded=motion.grounded;var forward=Vector3.ProjectOnPlane(SurvivalMovement.Forward(motion),Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            forward=Quaternion.AngleAxis(input.lookX,Vector3.up)*forward;motion.pitch=Mathf.Clamp(motion.pitch-input.lookY,-85,85);var right=Vector3.Cross(Vector3.up,forward);var movement=Vector2.ClampMagnitude(new Vector2(input.x,input.y),1);
            if(grounded&&input.jump)motion.verticalSpeed=5;motion.verticalSpeed-=9.81*dt;
            var delta=(forward*movement.y+right*movement.x)*(float)(speed*dt)+Vector3.up*(float)(motion.verticalSpeed*dt);
            motion.pose.x+=delta.x;motion.pose.y+=delta.y;motion.pose.z+=delta.z;motion.pose.yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            motion.forwardX=forward.x;motion.forwardY=0;motion.forwardZ=forward.z;motion.sequence=input.sequence;motion.grounded=false;
            model.contacts.Resolve(motion,previous,grounded,Vector3.up);
        }
    }
}
