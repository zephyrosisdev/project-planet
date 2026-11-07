using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        readonly ModularGeometry conveyorGeometry=new ModularGeometry();Mesh conveyorMesh;long conveyorRevision=-1;Double3 conveyorAnchor;int conveyorBody=int.MinValue;
        struct BeltObstacleSample {public FramePose pose;public long source,target;}
        readonly Dictionary<(int,long,long,long,long),List<BeltObstacleSample>> conveyorCells=new Dictionary<(int,long,long,long,long),List<BeltObstacleSample>>();long conveyorCellsRevision=-1;Double3 conveyorCellsAnchor;int conveyorCellsBody=int.MinValue;
        static (int,long,long,long,long) ConveyorCell(FramePose p)=>(p.body,p.frame,(long)Math.Floor(p.x/2),(long)Math.Floor(p.y/2),(long)Math.Floor(p.z/2));
        string ConveyorIntersection(IndustrialConveyorPath path,IndustrialMachine from,IndustrialMachine to){
            if(conveyorCellsRevision!=Authority.World.connectionRevision||conveyorCellsBody!=actor.pose.body||(Body(actor.pose)-conveyorCellsAnchor).Magnitude>20){conveyorCells.Clear();conveyorCellsRevision=Authority.World.connectionRevision;conveyorCellsBody=actor.pose.body;conveyorCellsAnchor=Body(actor.pose);
                foreach(var lane in Authority.World.belts){var start=Authority.World.Machine(lane.source);var end=Authority.World.Machine(lane.target);if(start==null||end==null||start.pose.body!=actor.pose.body||Math.Min(start.pose.Distance(actor.pose),end.pose.Distance(actor.pose))>225)continue;var existing=BeltPath(lane);for(double d=0;d<=existing.Length;d+=.5){var point=existing.At(d);var cell=ConveyorCell(point);if(!conveyorCells.TryGetValue(cell,out var list)){list=new List<BeltObstacleSample>();conveyorCells.Add(cell,list);}list.Add(new BeltObstacleSample{pose=point,source=lane.source,target=lane.target});}}
            }
            var first=path.points[0];var last=path.points[path.points.Count-1];foreach(var point in path.points){var cell=ConveyorCell(point);for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)if(conveyorCells.TryGetValue((cell.Item1,cell.Item2,cell.Item3+x,cell.Item4+y,cell.Item5+z),out var list))foreach(var obstacle in list){if((obstacle.source==from.id||obstacle.target==from.id)&&point.Distance(first)<1.1||(to.id!=0&&(obstacle.source==to.id||obstacle.target==to.id)&&point.Distance(last)<1.1))continue;if(point.Distance(obstacle.pose)<.75)return "Belt intersects an existing conveyor; add nodes or raise the support height";}}
            return null;
        }
        IndustrialConveyorPath BeltPath(ConveyorLane lane){if(lane.path==null){var from=Authority.World.Machine(lane.source);var to=Authority.World.Machine(lane.target);if(from!=null&&to!=null)lane.path=new IndustrialConveyorPath(from,to,lane.route);}return lane.path;}
        internal static void AddConveyorGeometry(ModularGeometry geometry,IndustrialConveyorPath path,Double3 origin,Color color){
            for(int i=1;i<path.points.Count;i++){var a=new Double3(path.points[i-1].x,path.points[i-1].y,path.points[i-1].z);var b=new Double3(path.points[i].x,path.points[i].y,path.points[i].z);var up=PlanetPrecisionPolicy.Direction((a+b)*.5);var forward=(b-a).Float.normalized;var side=Vector3.Cross(up,forward).normalized*.4f;if(side.sqrMagnitude<.01f)continue;var x=(a-origin).Float;var y=(b-origin).Float;var down=-up*.12f;geometry.Quad(x-side,y-side,y+side,x+side,color);geometry.Quad(x-side+down,y-side+down,y-side,x-side,color);geometry.Quad(x+side,y+side,y+side+down,x+side+down,color);}
        }
        void AddConveyorSupport(ModularGeometry geometry,FramePose pin,Double3 origin,Color color){
            var p=Body(pin);var up=PlanetPrecisionPolicy.Direction(p);var floor=surface==null?p-new Double3(up):surface.ProjectToSurface(p);float height=Mathf.Clamp((float)(p-floor).Magnitude,.2f,10);var q=Quaternion.FromToRotation(Vector3.up,up);geometry.Box((p-origin).Float-up*height*.5f,new Vector3(.12f,height,.12f),color,q);geometry.Box((p-origin).Float-up*.15f,new Vector3(1,.1f,.2f),color,q);
        }
        string ConveyorShapeError(IndustrialConveyorPath path){
            var start=path.points[0];var end=path.points[path.points.Count-1];for(int i=1;i<path.points.Count;i++){
                var a=path.points[i-1];var b=path.points[i];string discharge=Authority.World.FrontDischargeObstruction(b,.4);if(discharge!=null)return discharge;var delta=(Body(b)-Body(a)).Float;var up=PlanetPrecisionPolicy.Direction(Body(b));float vertical=Mathf.Abs(Vector3.Dot(delta,up)),horizontal=Vector3.ProjectOnPlane(delta,up).magnitude;if(horizontal>.001f&&vertical>horizontal*.75f)return "Belt slope too steep; spread the height change across more nodes";
                if(surface!=null&&Body(b).Magnitude<world.definition.radius+surface.SampleHeight(Body(b))+.15)return "Belt intersects terrain; raise the support height";
                if(host!=null&&a.Distance(start)>.65&&b.Distance(end)>.65&&delta.sqrMagnitude>.000001f&&Physics.SphereCast(world.RenderBodyPosition(Body(a)),.39f,delta.normalized,out var hit,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return "Belt intersects "+hit.collider.name+"; add a routing node";
            }return null;
        }
        string CheckConveyorPath(IndustrialConveyorPath path,IndustrialMachine from,IndustrialMachine to)=>ConveyorShapeError(path)??ConveyorIntersection(path,from,to);
        string ConveyorObstruction(IndustrialCommand command){var from=Authority.World.Machine(command.source);var to=Authority.World.Machine(command.target);return from==null||to==null?"Missing conveyor socket":CheckConveyorPath(new IndustrialConveyorPath(from,to,command.route),from,to);}
        void BuildConveyorToolGeometry(){
            var from=Authority.World.Machine(cableSource);IndustrialConveyorPath path=null;
            if(from!=null){var to=cableHover;if(to==null){var aim=CableAimPoint();to=new IndustrialMachine{definition="store",pose=aim,customConveyorPorts=true,conveyorInput=aim,conveyorOutput=aim};}path=new IndustrialConveyorPath(from,to,cableAnchors);if(cableReason==null)cableReason=CheckConveyorPath(path,from,to);if(cableReason!=null)cablePrompt+="\n"+cableReason;var color=cableReason==null?new Color(.1f,.95f,.4f,1):new Color(1,.1f,.05f,1);AddConveyorGeometry(cablePreviewGeometry,path,cableAnchor,color);foreach(var point in cableAnchors)AddConveyorSupport(cablePreviewGeometry,point,cableAnchor,color);if(cableHover==null)AddConveyorSupport(cablePreviewGeometry,CableAimPoint(),cableAnchor,color);}
            cablePreviewMesh??=new Mesh{name="Conveyor planner preview"};cablePreviewGeometry.BuildInto(cablePreviewMesh);cablePortGeometry.Clear();
            foreach(var machine in Authority.World.machines){if(machine.owner!=actor.id||machine.Definition.Vehicle||machine.pose.body!=actor.pose.body||machine.pose.frame!=actor.pose.frame||actor.pose.Distance(machine.pose)>24)continue;foreach(bool output in new[]{false,true}){var pin=IndustrialConveyorPath.Pin(machine,output);var color=output?new Color(.1f,.95f,.35f,1):new Color(.1f,.65f,1,1);if(machine==cableHover&&output==(cableSource==0))color=Color.yellow;cablePortGeometry.Box((Body(pin)-cableAnchor).Float,new Vector3(.45f,.45f,.15f),color,Rotation(machine.pose));}}
            cablePortMesh??=new Mesh{name="Conveyor input output handles"};cablePortGeometry.BuildInto(cablePortMesh);
        }
        void RenderConveyorRuns(Bounds bounds){
            if(conveyorMesh==null||conveyorRevision!=Authority.World.connectionRevision||conveyorBody!=actor.pose.body||(Body(actor.pose)-conveyorAnchor).Magnitude>20){conveyorGeometry.Clear();conveyorAnchor=Body(actor.pose);conveyorBody=actor.pose.body;conveyorRevision=Authority.World.connectionRevision;
                foreach(var lane in Authority.World.belts){var from=Authority.World.Machine(lane.source);var to=Authority.World.Machine(lane.target);if(from==null||to==null||from.pose.body!=actor.pose.body||Math.Min(from.pose.Distance(actor.pose),to.pose.Distance(actor.pose))>225)continue;var path=BeltPath(lane);AddConveyorGeometry(conveyorGeometry,path,conveyorAnchor,new Color(.14f,.18f,.21f,0));if(lane.route!=null)foreach(var pin in lane.route)AddConveyorSupport(conveyorGeometry,pin,conveyorAnchor,new Color(.95f,.38f,.04f,0));}
                conveyorMesh??=new Mesh{name="Nearby spline conveyor runs"};conveyorGeometry.BuildInto(conveyorMesh);
            }
            if(conveyorMesh.vertexCount>0)Draw(conveyorMesh,prototypeMaterial,Matrix4x4.Translate(world.RenderBodyPosition(conveyorAnchor)),bounds);
            foreach(var lane in Authority.World.belts){var from=Authority.World.Machine(lane.source);var to=Authority.World.Machine(lane.target);if(from==null||to==null||from.pose.body!=actor.pose.body||Math.Min(from.pose.Distance(actor.pose),to.pose.Distance(actor.pose))>225)continue;var path=BeltPath(lane);
                foreach(var item in lane.items){double d=lane.length<=0?0:item.distance/lane.length*path.Length;var at=path.At(d);var next=path.At(Math.Min(path.Length,d+.1));var up=PlanetPrecisionPolicy.Direction(Body(at));var direction=(Body(next)-Body(at)).Float;var q=direction.sqrMagnitude<.000001f?Rotation(at):Quaternion.LookRotation(direction,up);var size=item.item.EndsWith("-bar",StringComparison.Ordinal)?new Vector3(.2f,.1f,.45f):item.item=="plate"?new Vector3(.4f,.05f,.4f):Vector3.one*.25f;itemBatches[item.item].Add(Matrix4x4.TRS(world.RenderBodyPosition(Body(at))+up*.2f,q,size));}
            }
        }
        void DisposeConveyors(){ArmyLifetime.Destroy(conveyorMesh);}
    }
}
