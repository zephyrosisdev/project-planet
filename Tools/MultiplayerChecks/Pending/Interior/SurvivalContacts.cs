using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    // Canonical CPU prisms; the index never contains floating-origin transforms.
    internal sealed class SurvivalContacts {
        internal sealed class Shape {internal long id;internal int body;internal Double3 origin;internal Quaternion rotation;internal BuildSolid[] solids;internal Ramp[] ramps;internal double radius;}
        internal readonly struct Ramp {
            internal readonly float x0,x1,z0,z1,y0,y1;internal readonly bool alongX;
            internal Ramp(float x0,float x1,float z0,float z1,float y0,float y1,bool alongX=false){this.x0=x0;this.x1=x1;this.z0=z0;this.z1=z1;this.y0=y0;this.y1=y1;this.alongX=alongX;}
            internal float Slope=>(y1-y0)/(alongX?x1-x0:z1-z0);
            internal float Height(Vector3 point)=>y0+Slope*(alongX?point.x-x0:point.z-z0);
            internal bool Contains(Vector3 point)=>point.x>=x0&&point.x<=x1&&point.z>=z0&&point.z<=z1;
            internal bool Overlap(Vector3 position,Quaternion rotation)=>Overlap(Avatar,position,rotation);
            internal bool Overlap(BuildSolid input,Vector3 position,Quaternion rotation){
                Span<Vector3> ramp=stackalloc Vector3[8];Span<Vector3> avatar=stackalloc Vector3[16];
                ramp[0]=new Vector3(x0,0,z0);ramp[1]=new Vector3(x1,0,z0);ramp[2]=new Vector3(x1,0,z1);ramp[3]=new Vector3(x0,0,z1);for(int i=0;i<4;i++){var v=ramp[i];v.y=Height(v);ramp[i+4]=v;}
                int count=input.polygon.Length;for(int i=0;i<count;i++){var p=input.polygon[i];avatar[i]=position+rotation*new Vector3(p.x,input.bottom,p.y);avatar[i+count]=position+rotation*new Vector3(p.x,input.top,p.y);}avatar=avatar.Slice(0,count*2);
                var up=rotation*Vector3.up;Span<Vector3> edges=stackalloc Vector3[5]{Vector3.right,Vector3.forward,Vector3.up,new Vector3(1,alongX?Slope:0,0),new Vector3(0,alongX?0:Slope,1)};
                if(Separated(ramp,avatar,Vector3.right)||Separated(ramp,avatar,Vector3.forward)||Separated(ramp,avatar,Vector3.up)||Separated(ramp,avatar,new Vector3(alongX?-Slope:0,1,alongX?0:-Slope))||Separated(ramp,avatar,up))return false;
                for(int i=0;i<count;i++){var edge=avatar[(i+1)%count]-avatar[i];if(Separated(ramp,avatar,Vector3.Cross(edge,up)))return false;foreach(var e in edges)if(Separated(ramp,avatar,Vector3.Cross(edge,e)))return false;}foreach(var edge in edges)if(Separated(ramp,avatar,Vector3.Cross(edge,up)))return false;return true;
            }
            static bool Separated(ReadOnlySpan<Vector3> a,ReadOnlySpan<Vector3> b,Vector3 axis){float length=axis.magnitude;if(length<.000001f)return false;axis/=length;float amin=float.MaxValue,amax=float.MinValue,bmin=float.MaxValue,bmax=float.MinValue;foreach(var p in a){float n=Vector3.Dot(p,axis);amin=Math.Min(amin,n);amax=Math.Max(amax,n);}foreach(var p in b){float n=Vector3.Dot(p,axis);bmin=Math.Min(bmin,n);bmax=Math.Max(bmax,n);}return Math.Min(amax,bmax)-Math.Max(amin,bmin)<=.001f;}
        }
        readonly Dictionary<long,Shape> shapes=new Dictionary<long,Shape>();
        readonly Dictionary<(int body,long x,long y,long z),HashSet<long>> cells=new Dictionary<(int,long,long,long),HashSet<long>>();
        readonly Dictionary<long,List<(int,long,long,long)>> occupied=new Dictionary<long,List<(int,long,long,long)>>();
        readonly HashSet<long> candidates=new HashSet<long>();readonly List<Shape> near=new List<Shape>();readonly List<long> ordered=new List<long>();
        static readonly BuildSolid Avatar=new BuildSolid(new[]{new Vector2(.35f,0),new Vector2(.2475f,.2475f),new Vector2(0,.35f),new Vector2(-.2475f,.2475f),new Vector2(-.35f,0),new Vector2(-.2475f,-.2475f),new Vector2(0,-.35f),new Vector2(.2475f,-.2475f)},.02f,1.8f);
        const double Cell=32;
        internal void Remove(long id){if(occupied.Remove(id,out var keys))foreach(var key in keys)if(cells.TryGetValue(key,out var bucket)){bucket.Remove(id);if(bucket.Count==0)cells.Remove(key);}shapes.Remove(id);}
        internal void Put(Shape shape){Remove(shape.id);shapes[shape.id]=shape;var keys=new List<(int,long,long,long)>();double r=shape.radius;
            for(long x=(long)Math.Floor((shape.origin.x-r)/Cell);x<=(long)Math.Floor((shape.origin.x+r)/Cell);x++)for(long y=(long)Math.Floor((shape.origin.y-r)/Cell);y<=(long)Math.Floor((shape.origin.y+r)/Cell);y++)for(long z=(long)Math.Floor((shape.origin.z-r)/Cell);z<=(long)Math.Floor((shape.origin.z+r)/Cell);z++){var key=(shape.body,x,y,z);if(!cells.TryGetValue(key,out var bucket))cells[key]=bucket=new HashSet<long>();bucket.Add(shape.id);keys.Add(key);}occupied[shape.id]=keys;
        }
        internal void Clear(){shapes.Clear();cells.Clear();occupied.Clear();near.Clear();candidates.Clear();}
        void Query(int body,Double3 from,Double3 to,double margin=3){candidates.Clear();near.Clear();
            for(long x=(long)Math.Floor((Math.Min(from.x,to.x)-margin)/Cell);x<=(long)Math.Floor((Math.Max(from.x,to.x)+margin)/Cell);x++)for(long y=(long)Math.Floor((Math.Min(from.y,to.y)-margin)/Cell);y<=(long)Math.Floor((Math.Max(from.y,to.y)+margin)/Cell);y++)for(long z=(long)Math.Floor((Math.Min(from.z,to.z)-margin)/Cell);z<=(long)Math.Floor((Math.Max(from.z,to.z)+margin)/Cell);z++)if(cells.TryGetValue((body,x,y,z),out var bucket))foreach(long id in bucket)candidates.Add(id);
            // Stable order keeps replay independent of dictionary insertion history.
            ordered.Clear();ordered.AddRange(candidates);ordered.Sort();foreach(long id in ordered)near.Add(shapes[id]);
        }
        // Traverse only cells touched by the ray. Long weapon rays must not
        // query the entire three-dimensional bounding cube between endpoints.
        void QueryRay(int body,Double3 from,Double3 to){
            candidates.Clear();near.Clear();var delta=to-from;
            long x=(long)Math.Floor(from.x/Cell),y=(long)Math.Floor(from.y/Cell),z=(long)Math.Floor(from.z/Cell);
            long ex=(long)Math.Floor(to.x/Cell),ey=(long)Math.Floor(to.y/Cell),ez=(long)Math.Floor(to.z/Cell);
            int sx=Math.Sign(delta.x),sy=Math.Sign(delta.y),sz=Math.Sign(delta.z);
            double ax=sx==0?double.PositiveInfinity:Math.Abs(Cell/delta.x),ay=sy==0?double.PositiveInfinity:Math.Abs(Cell/delta.y),az=sz==0?double.PositiveInfinity:Math.Abs(Cell/delta.z);
            double tx=sx==0?double.PositiveInfinity:(((sx>0?x+1:x)*Cell)-from.x)/delta.x,ty=sy==0?double.PositiveInfinity:(((sy>0?y+1:y)*Cell)-from.y)/delta.y,tz=sz==0?double.PositiveInfinity:(((sz>0?z+1:z)*Cell)-from.z)/delta.z;
            long maximum=Math.Abs(ex-x)+Math.Abs(ey-y)+Math.Abs(ez-z)+4;
            for(long step=0;step<maximum;step++){
                if(cells.TryGetValue((body,x,y,z),out var bucket))foreach(long id in bucket)candidates.Add(id);
                if(x==ex&&y==ey&&z==ez)break;
                double next=Math.Min(tx,Math.Min(ty,tz));
                if(tx<=next){x+=sx;tx+=ax;}if(ty<=next){y+=sy;ty+=ay;}if(tz<=next){z+=sz;tz+=az;}
            }
            ordered.Clear();ordered.AddRange(candidates);ordered.Sort();foreach(long id in ordered)near.Add(shapes[id]);
        }
        static bool ClipRay(double value,double change,ref double low,ref double high){
            if(Math.Abs(change)<1e-12)return value>=-1e-7;
            double boundary=-value/change;if(change>0)low=Math.Max(low,boundary);else high=Math.Min(high,boundary);return low<=high;
        }
        internal static bool PrismRay(BuildSolid solid,Vector3 from,Vector3 delta,out double entry)=>PrismRay(solid,from,delta,out entry,out _);
        static bool PrismRay(BuildSolid solid,Vector3 from,Vector3 delta,out double entry,out double exit){
            double low=0,high=1;entry=exit=0;
            if(!ClipRay(from.y-solid.bottom,delta.y,ref low,ref high)||!ClipRay(solid.top-from.y,-delta.y,ref low,ref high))return false;
            var points=solid.polygon;double area=0;for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];area+=a.x*b.y-b.x*a.y;}double sign=area>=0?1:-1;
            for(int i=0;i<points.Length;i++){var a=points[i];var b=points[(i+1)%points.Length];double dx=b.x-a.x,dz=b.y-a.y;
                if(!ClipRay(sign*(dx*(from.z-a.y)-dz*(from.x-a.x)),sign*(dx*delta.z-dz*delta.x),ref low,ref high))return false;}
            if(high<0||low>1)return false;entry=Math.Max(0,low);exit=Math.Min(1,high);return true;
        }
        internal bool RayBlocked(int body,Double3 from,Double3 to){
            QueryRay(body,from,to);foreach(var shape in near){var inverse=Quaternion.Inverse(shape.rotation);var local=Double3.Rotate(inverse,from-shape.origin).Float;var delta=Double3.Rotate(inverse,to-from).Float;
                foreach(var solid in shape.solids)if(PrismRay(solid,local,delta,out _))return true;
                if(shape.ramps!=null)foreach(var ramp in shape.ramps){var box=BuildSolid.Box(ramp.x0,ramp.x1,ramp.z0,ramp.z1,0,Math.Max(ramp.y0,ramp.y1));if(!PrismRay(box,local,delta,out double entry,out double exit))continue;double low=entry,high=exit;
                    // Clip the actual inclined ceiling as well as the ramp bounds.
                    if(ClipRay(ramp.Height(local)-local.y,ramp.Slope*(ramp.alongX?delta.x:delta.z)-delta.y,ref low,ref high)&&low<=1)return true;}
            }return false;
        }
        bool Blocked(Double3 point,Quaternion avatarRotation){foreach(var shape in near){var local=Double3.Rotate(Quaternion.Inverse(shape.rotation),point-shape.origin).Float;var rotation=Quaternion.Inverse(shape.rotation)*avatarRotation;foreach(var prism in shape.solids)if(PlayerBuildingProfiles.Overlap(prism,Vector3.zero,Quaternion.identity,Avatar,local,rotation,.001f))return true;if(shape.ramps!=null)foreach(var ramp in shape.ramps)if(ramp.Overlap(local,rotation))return true;}return false;}
        bool RecoverOverlap(ref Double3 point,Quaternion rotation,Vector3 up,Double3 delta,Vector3 forward){
            if(!Blocked(point,rotation))return true;var right=Vector3.Cross(up,forward).normalized;var back=delta.Magnitude>1e-10?delta*(-1/delta.Magnitude):new Double3(up);
            var directions=new[]{new Double3(up),back,new Double3(forward),new Double3(-forward),new Double3(right),new Double3(-right),new Double3((forward+right).normalized),new Double3((forward-right).normalized),new Double3((-forward+right).normalized),new Double3((-forward-right).normalized),new Double3(-up)};
            for(int shell=1;shell<=20;shell++)foreach(var direction in directions){var candidate=point+direction*(shell*.025);if(Blocked(candidate,rotation))continue;point=candidate;return true;}return false;
        }
        static bool Inside(Vector2 p,Vector2[] polygon){bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)if((polygon[i].y>p.y)!=(polygon[j].y>p.y)&&p.x<(polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x)inside=!inside;return inside;}
        internal bool ProbeGround(int body,Double3 point,Vector3 up,float above,float depth,out VehicleGroundContact contact,long ignored=0){
            Query(body,point+new Double3(up)*above,point-new Double3(up)*depth,1);bool found=false;double best=-depth;contact=default;
            foreach(var shape in near){if(shape.id==ignored)continue;var inv=Quaternion.Inverse(shape.rotation);var local=Double3.Rotate(inv,point-shape.origin).Float;var direction=inv*up;if(direction.y<.15f)continue;
                foreach(var prism in shape.solids){double rise=(prism.top-local.y)/direction.y;if(rise<best||rise>above)continue;var hit=local+direction*(float)rise;if(!Inside(new Vector2(hit.x,hit.z),prism.polygon))continue;best=rise;found=true;contact=new VehicleGroundContact{position=point+new Double3(up)*rise,normal=shape.rotation*Vector3.up};}
                if(shape.ramps!=null)foreach(var ramp in shape.ramps){double denominator=direction.y-ramp.Slope*(ramp.alongX?direction.x:direction.z);if(denominator<.15)continue;double rise=(ramp.Height(local)-local.y)/denominator;if(rise<best||rise>above||!ramp.Contains(local+direction*(float)rise))continue;best=rise;found=true;contact=new VehicleGroundContact{position=point+new Double3(up)*rise,normal=shape.rotation*new Vector3(ramp.alongX?-ramp.Slope:0,1,ramp.alongX?0:-ramp.Slope).normalized};}
            }return found;
        }
        internal Vector3 SweepVehicle(int body,Double3 position,Quaternion rotation,VehicleDefinition definition,Vector3 delta,out Vector3 normal,long ignored=0){
            normal=Vector3.zero;if(delta.sqrMagnitude<1e-10)return delta;Query(body,position,position+new Double3(delta),definition.chassisSize.magnitude+definition.chassisCentre.magnitude);
            var bounds=new Bounds(definition.chassisCentre,definition.chassisSize*.96f);var min=bounds.min;var max=bounds.max;var box=BuildSolid.Box(min.x,max.x,min.z,max.z,min.y,max.y);int steps=Math.Max(1,(int)Math.Ceiling(delta.magnitude/.15));
            for(int i=1;i<=steps;i++){var candidate=position+new Double3(delta)*(i/(double)steps);bool blocked=false;
                foreach(var shape in near){if(shape.id==ignored)continue;var q=Quaternion.Inverse(shape.rotation);var local=Double3.Rotate(q,candidate-shape.origin).Float;var relative=q*rotation;foreach(var prism in shape.solids)if(PlayerBuildingProfiles.Overlap(prism,Vector3.zero,Quaternion.identity,box,local,relative)){blocked=true;break;}if(!blocked&&shape.ramps!=null)foreach(var ramp in shape.ramps)if(ramp.Overlap(box,local,relative)){blocked=true;break;}if(blocked)break;}
                if(blocked){normal=-delta.normalized;return delta*((i-1)/(float)steps);}
            }return delta;
        }
        internal bool SweptBuildings(int body,Double3 from,Double3 to,double radius){
            var delta=to-from;double length=delta.x*delta.x+delta.y*delta.y+delta.z*delta.z;
            foreach(var shape in shapes.Values){if(shape.id>=0||shape.body!=body)continue;var offset=shape.origin-from;double t=length>1e-12?Math.Max(0,Math.Min(1,(offset.x*delta.x+offset.y*delta.y+offset.z*delta.z)/length)):0;if((shape.origin-from-delta*t).Magnitude<radius+shape.radius)return true;}return false;
        }
        bool Support(ref Double3 point,Vector3 up,double maxRise,double previousVertical){
            if(previousVertical>0)return false;double highest=double.NegativeInfinity;
            foreach(var shape in near){var inv=Quaternion.Inverse(shape.rotation);var local=Double3.Rotate(inv,point-shape.origin).Float;var localUp=inv*up;if(localUp.y<.6f)continue;
                foreach(var prism in shape.solids){double rise=(prism.top+.05-local.y)/localUp.y;if(rise<-.16||rise>maxRise)continue;var hit=local+localUp*(float)rise;if(Inside(new Vector2(hit.x,hit.z),prism.polygon))highest=Math.Max(highest,rise);}
                if(shape.ramps!=null)foreach(var ramp in shape.ramps){var rotation=inv*Quaternion.FromToRotation(Vector3.up,up);for(int i=0;i<9;i++){var edge=i==8?Vector2.zero:Avatar.polygon[i];var sample=local+rotation*new Vector3(edge.x,0,edge.y);double denominator=localUp.y-ramp.Slope*(ramp.alongX?localUp.x:localUp.z);if(denominator<.3)continue;double rise=(ramp.Height(sample)+.05-sample.y)/denominator;if(rise<-.16||rise>maxRise)continue;if(ramp.Contains(sample+localUp*(float)rise))highest=Math.Max(highest,rise);}}
            }
            if(double.IsNegativeInfinity(highest))return false;point+=new Double3(up)*highest;return true;
        }
        internal bool ClearAt(int body,Double3 point,Vector3 up){Query(body,point,point);return !Blocked(point,Quaternion.FromToRotation(Vector3.up,up));}
        internal void Resolve(SurvivalMotion state,FramePose previous,bool wasGrounded,Vector3? fixedUp=null){
            var from=new Double3(previous.x,previous.y,previous.z);var target=new Double3(state.pose.x,state.pose.y,state.pose.z);Query(state.pose.body,from,target);if(near.Count==0)return;
            var up=fixedUp??PlanetPrecisionPolicy.Direction(from);var orientation=Quaternion.FromToRotation(Vector3.up,up);var delta=target-from;int steps=Math.Max(1,(int)Math.Ceiling(delta.Magnitude/.15));steps=Math.Min(steps,128);var increment=delta*(1.0/steps);var current=from;bool supported=false;
            RecoverOverlap(ref current,orientation,up,delta,SurvivalMovement.Tangent(SurvivalMovement.Forward(state),up));
            for(int i=0;i<steps;i++){
                var candidate=current+increment;var raised=candidate;bool floor=Support(ref raised,up,wasGrounded||supported?.36:.06,state.verticalSpeed);
                if(floor&&!Blocked(raised,orientation)){candidate=raised;supported=true;}
                if(!Blocked(candidate,orientation)){current=candidate;continue;}
                if((wasGrounded||supported)&&state.verticalSpeed<=0){var step=candidate+new Double3(up)*.36;if(!Blocked(step,orientation)){current=step;continue;}}
                // Preserve tangential motion along walls and stop at ceilings.
                var forward=SurvivalMovement.Tangent(SurvivalMovement.Forward(state),up);var right=Vector3.Cross(up,forward).normalized;
                for(int axisIndex=0;axisIndex<3;axisIndex++){var axis=axisIndex==0?forward:axisIndex==1?right:up;double distance=(increment.x*axis.x+increment.y*axis.y+increment.z*axis.z);var slide=current+new Double3(axis)*distance;if(!Blocked(slide,orientation))current=slide;else if(axisIndex==2&&distance>0)state.verticalSpeed=Math.Min(0,state.verticalSpeed);}
            }
            var final=current;if(Support(ref final,up,.08,state.verticalSpeed)&&!Blocked(final,orientation)){current=final;state.grounded=true;state.verticalSpeed=0;}
            state.pose.x=current.x;state.pose.y=current.y;state.pose.z=current.z;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        readonly SurvivalContacts serverContacts=new SurvivalContacts(),clientContacts=new SurvivalContacts();
        readonly Dictionary<int,string> contactBuildJson=new Dictionary<int,string>();readonly Dictionary<int,List<long>> contactBuildIds=new Dictionary<int,List<long>>();
        readonly Dictionary<long,string> contactMachineSignatures=new Dictionary<long,string>();
        static SurvivalContacts.Shape BuildingShape(int body,PlayerBuildSite site,PlayerModuleRecord module,PlayerModuleProfile profile){
            var shape=new SurvivalContacts.Shape{id=-module.id*4,body=body,origin=PlayerBuildAuthority.Point(site,module.position),rotation=PlayerBuildAuthority.Rotation(site,module),solids=profile.Shape(module.open),radius=profile.interactionBounds.center.magnitude+profile.interactionBounds.extents.magnitude};
            // Match the offline walking collision ramps, rather than render risers.
            if(module.kind==PlayerModuleKind.StairsStraight){shape.solids=Array.Empty<BuildSolid>();shape.ramps=new[]{new SurvivalContacts.Ramp(-1.45f,1.45f,-1.5f,1.5f,0,3)};}
            else if(module.kind==PlayerModuleKind.Stairs90||module.kind==PlayerModuleKind.Stairs180){bool turn=module.kind==PlayerModuleKind.Stairs90;shape.solids=new[]{BuildSolid.Box(-1.5f,turn?-.3f:1.5f,.3f,1.5f,0,1.5f)};shape.ramps=new[]{new SurvivalContacts.Ramp(-1.5f,-.3f,-1.5f,.3f,0,1.5f),turn?new SurvivalContacts.Ramp(-.3f,1.5f,.3f,1.5f,1.5f,3,true):new SurvivalContacts.Ramp(.3f,1.5f,-1.5f,.3f,3,1.5f)};}
            return shape;
        }
        void UpdateServerContacts(){
            foreach(var saved in authority.World.buildStates){if(contactBuildJson.TryGetValue(saved.body,out var old)&&old==saved.json)continue;if(contactBuildIds.TryGetValue(saved.body,out var ids))foreach(long id in ids)serverContacts.Remove(id);ids=new List<long>();
                foreach(var site in BuildingAuthority.Storage(saved.body).playerBuildSites)foreach(var module in site.modules){var shape=BuildingShape(saved.body,site,module,BuildingProfiles[(int)module.kind]);serverContacts.Put(shape);ids.Add(shape.id);}contactBuildIds[saved.body]=ids;contactBuildJson[saved.body]=saved.json;
            }
            var alive=new HashSet<long>();foreach(var machine in authority.World.machines)if(machine.pose.frame==0&&!machine.integratedLogistics){alive.Add(machine.id);string stamp=machine.definition+":"+JsonUtility.ToJson(machine.pose);if(contactMachineSignatures.TryGetValue(machine.id,out var previous)&&previous==stamp)continue;contactMachineSignatures[machine.id]=stamp;PutMachineContact(serverContacts,machine);}
            foreach(long id in new List<long>(contactMachineSignatures.Keys))if(!alive.Contains(id)){serverContacts.Remove(id*8+2);contactMachineSignatures.Remove(id);}
        }
        void PutMachineContact(SurvivalContacts contacts,IndustrialMachine machine){var data=FactoryCollisionData(machine);var solids=new BuildSolid[data.solids.Length];for(int i=0;i<solids.Length;i++)solids[i]=data.solids[i].Shape();var position=SurvivalVehicleSimulation.Position(machine.pose);contacts.Put(new SurvivalContacts.Shape{id=machine.id*8+2,body=machine.pose.body,origin=position,rotation=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(position))*Quaternion.Euler(0,machine.pose.yaw,0),solids=solids,radius=data.bound});}
        void ReceiveContacts(SurvivalSnapshot snapshot){foreach(long id in snapshot.removed)clientContacts.Remove(id);foreach(var entity in snapshot.changed){if(entity.kind=="building"&&clientBuildings.TryGetValue(entity.id,out var part))clientContacts.Put(BuildingShape(entity.pose.body,part.Site(),part.Record(),BuildingProfiles[(int)part.kind]));else if(entity.kind=="machine"&&clientMachines.TryGetValue(entity.id/8,out var machine)){if(machine.pose.frame==0&&!machine.integratedLogistics)PutMachineContact(clientContacts,machine);else clientContacts.Remove(entity.id);}}}
        void StepWalking(SurvivalMotion motion,SurvivalInput input,PlanetSurface surface,float dt,double speed,bool server){var previous=motion.pose;bool grounded=motion.grounded;SurvivalMovement.Step(motion,input,surface,dt,speed);(server?serverContacts:clientContacts).Resolve(motion,previous,grounded);}
        void ClearContacts(){serverContacts.Clear();clientContacts.Clear();contactBuildJson.Clear();contactBuildIds.Clear();contactMachineSignatures.Clear();}
    }
}
