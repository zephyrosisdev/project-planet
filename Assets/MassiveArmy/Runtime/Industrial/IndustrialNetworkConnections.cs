using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalFactoryLink {
        public long entity,source,target;public string kind;public List<FramePose> route;
    }
    // Network commands become ordinary, journalled industrial commands after validation.
    public static class SurvivalFactoryConnections {
        public static bool Creates(string kind)=>kind=="power"||kind=="water"||kind=="belt";
        public static long WireId(long entity,string kind)=>checked(-(entity*4+(kind=="power"?1:kind=="water"?2:3)));
        public static string Prepare(IndustrialWorld world,long actor,IndustrialCommand command,Func<FramePose,double,string> obstruction,ContactIndex contacts=null){
            if(!Creates(command.kind))return null;
            var from=world.Machine(command.source);var to=world.Machine(command.target);
            if(from==null||to==null||from.Definition.Vehicle||to.Definition.Vehicle||from.pose.frame!=to.pose.frame||from.integratedLogistics||to.integratedLogistics)return "Choose stationary machine sockets in one reference frame";
            if(from.pose.frame!=0&&(world.multiplayer==null||!world.multiplayer.ships.Exists(s=>s.entity==from.pose.frame&&s.owner==actor&&s.pose.body==from.pose.body)))return "Choose machinery in your own ship";
            var user=command.route==null?new List<FramePose>():new List<FramePose>(command.route);
            int maximum=command.kind=="belt"?IndustrialConveyorPath.MaximumAnchors:IndustrialCablePath.MaximumUserAnchors;
            if(user.Count>maximum)return "Too many routing points";
            foreach(var point in user)if(point.body!=from.pose.body||point.frame!=from.pose.frame||!SurvivalServerConfig.Finite(point.x)||!SurvivalServerConfig.Finite(point.y)||!SurvivalServerConfig.Finite(point.z)||!SurvivalServerConfig.Finite(point.yaw))return "Invalid routing point";
            command.route=user;
            if(command.kind=="belt")command.definition="spline-conveyor-v1";
            else command.route=IndustrialCablePath.WithSocketClearance(from,to,command.kind,user);
            string error;IndustrialConveyorPath conveyor=null;
            if(command.kind=="belt")error=world.ConveyorError(actor,command,out conveyor);else error=world.ConnectionError(actor,command);
            if(error!=null)return error;
            error=world.ConstructionPolicyError(actor);if(error!=null)return error;
            var points=conveyor?.points??Points(world,new SurvivalFactoryLink{kind=command.kind,source=from.id,target=to.id,route=command.route});
            double radius=command.kind=="belt"?.39:command.kind=="power"?.025:.07;
            for(int i=1;i<points.Count;i++){
                var a=points[i-1];var b=points[i];double length=a.Distance(b);
                if(command.kind=="belt"){
                    double dx=b.x-a.x,dy=b.y-a.y,dz=b.z-a.z,r=Math.Sqrt(b.x*b.x+b.y*b.y+b.z*b.z),vertical=b.frame!=0||r<1?Math.Abs(dy):Math.Abs((dx*b.x+dy*b.y+dz*b.z)/r),horizontal=Math.Sqrt(Math.Max(0,dx*dx+dy*dy+dz*dz-vertical*vertical));
                    if(horizontal>.001&&vertical>horizontal*.75)return "Conveyor slope is too steep";
                }
                int samples=Math.Max(1,(int)Math.Ceiling(length/Math.Max(.05,radius*.5)));
                for(int n=0;n<=samples;n++){
                    double t=n/(double)samples;var p=a;p.x+=(b.x-a.x)*t;p.y+=(b.y-a.y)*t;p.z+=(b.z-a.z)*t;
                    error=world.FrontDischargeObstruction(p,radius)??obstruction?.Invoke(p,radius);if(error!=null)return error;
                }
            }
            if(conveyor!=null)return (contacts??new ContactIndex()).Check(world,conveyor,from.id,to.id);
            return null;
        }
        public sealed class ContactIndex {
            struct Sample {public FramePose pose;public long source,target;}
            IndustrialWorld indexed;long revision=long.MinValue;readonly Dictionary<(int,long,long,long,long),List<Sample>> cells=new Dictionary<(int,long,long,long,long),List<Sample>>();
            static (int,long,long,long,long) Cell(FramePose p)=>(p.body,p.frame,(long)Math.Floor(p.x/.75),(long)Math.Floor(p.y/.75),(long)Math.Floor(p.z/.75));
            public string Check(IndustrialWorld world,IndustrialConveyorPath candidate,long source,long target){
                if(indexed!=world||revision!=world.connectionRevision){
                    cells.Clear();indexed=world;revision=world.connectionRevision;
                    foreach(var lane in world.belts){var from=world.Machine(lane.source);var to=world.Machine(lane.target);if(from==null||to==null)continue;var path=lane.path??(lane.path=new IndustrialConveyorPath(from,to,lane.route));
                        for(double d=0;d<=path.Length;d+=.4){var point=path.At(d);var key=Cell(point);if(!cells.TryGetValue(key,out var bucket)){bucket=new List<Sample>();cells.Add(key,bucket);}bucket.Add(new Sample{pose=point,source=lane.source,target=lane.target});}
                    }
                }
                for(double d=0;d<=candidate.Length;d+=.4){var point=candidate.At(d);var key=Cell(point);
                    for(long x=-1;x<=1;x++)for(long y=-1;y<=1;y++)for(long z=-1;z<=1;z++)if(cells.TryGetValue((key.Item1,key.Item2,key.Item3+x,key.Item4+y,key.Item5+z),out var bucket))foreach(var sample in bucket){
                        if((sample.source==source||sample.target==source)&&d<1.1||(sample.source==target||sample.target==target)&&candidate.Length-d<1.1)continue;
                        if(point.Distance(sample.pose)<.75)return "Conveyor intersects an existing belt; route around it";
                    }
                }return null;
            }
        }
        public static List<FramePose> Points(IndustrialWorld world,SurvivalFactoryLink link){
            return Points(world.Machine(link.source),world.Machine(link.target),link);
        }
        public static List<FramePose> Points(IndustrialMachine from,IndustrialMachine to,SurvivalFactoryLink link){
            if(from==null||to==null)return new List<FramePose>();
            if(link.kind=="belt")return new IndustrialConveyorPath(from,to,link.route).points;
            var points=new List<FramePose>();for(int span=0;span<=(link.route?.Count??0);span++){
                var a=IndustrialCablePath.Anchor(from,to,link.kind,link.route,span);var b=IndustrialCablePath.Anchor(from,to,link.kind,link.route,span+1);if(span==0)points.Add(a);
                for(int n=1;n<=IndustrialCablePath.SamplesPerSpan;n++)points.Add(IndustrialCablePath.Point(a,b,n/(double)IndustrialCablePath.SamplesPerSpan,link.kind));
            }return points;
        }
        public static IEnumerable<SurvivalFactoryLink> Links(IndustrialWorld world){
            foreach(var machine in world.machines){
                if(machine.powerSource!=0)yield return new SurvivalFactoryLink{entity=machine.id,kind="power",source=machine.powerSource,target=machine.id,route=machine.powerRoute};
                if(machine.waterTarget!=0)yield return new SurvivalFactoryLink{entity=machine.id,kind="water",source=machine.id,target=machine.waterTarget,route=machine.waterRoute};
            }
            foreach(var lane in world.belts)yield return new SurvivalFactoryLink{entity=lane.id,kind="belt",source=lane.source,target=lane.target,route=lane.route};
        }
    }
}
