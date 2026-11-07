using System;
using System.Collections.Generic;
using System.Collections;

namespace MassiveArmy.Industrial {
    // Bounded navigation for industrial trucks only. It never reads or mirrors army units.
    public static class IndustrialHaulNavigation {
        struct Basis {public double ux,uy,uz,xx,xy,xz,zx,zy,zz;}
        static Basis Axes(FramePose p){double r=Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z);var b=new Basis{uy=1,xx=1,zz=1};if(r<1)return b;b.ux=p.x/r;b.uy=p.y/r;b.uz=p.z/r;if(b.uy<-.999999){b.xy=0;b.xz=0;b.zx=0;b.zy=0;b.zz=-1;return b;}double d=1+b.uy;b.xx=1-b.ux*b.ux/d;b.xy=-b.ux;b.xz=-b.ux*b.uz/d;b.zx=b.xz;b.zy=-b.uz;b.zz=1-b.uz*b.uz/d;return b;}
        public static bool ShipClearanceContains(FramePose ship,FramePose point){var b=Axes(ship);double dx=point.x-ship.x,dy=point.y-ship.y,dz=point.z-ship.z;double x=dx*b.xx+dy*b.xy+dz*b.xz,z=dx*b.zx+dy*b.zy+dz*b.zz,y=dx*b.ux+dy*b.uy+dz*b.uz,angle=ship.yaw*Math.PI/180,c=Math.Cos(angle),s=Math.Sin(angle);double localX=x*c-z*s,localZ=x*s+z*c;if(y<=-11||y>=9)return false;return Math.Abs(localX)<28.8&&localZ>-42.6&&localZ<13.3||Math.Abs(localX)<12.3&&localZ>6.7&&localZ<41.3||Math.Abs(localX)<8.8&&localZ>-54.8&&localZ<-35.2||Math.Abs(Math.Abs(localX)-18)<4.3&&localZ>3.2&&localZ<20.8;}
        public static bool BuildingContains(IndustrialHaulObstacle obstacle,FramePose point,double padding){
            var pose=obstacle.pose;if(pose.body!=point.body||pose.frame!=point.frame)return false;
            if(obstacle.halfWidth<=0||obstacle.halfDepth<=0)return pose.Distance(point)<obstacle.radius+padding;
            double dx=point.x-pose.x,dy=point.y-pose.y,dz=point.z-pose.z,reach=obstacle.radius+padding;if(Math.Abs(dx)>reach+obstacle.height||Math.Abs(dy)>reach+obstacle.height||Math.Abs(dz)>reach+obstacle.height)return false;
            var b=Axes(pose);double x=dx*b.xx+dy*b.xy+dz*b.xz,z=dx*b.zx+dy*b.zy+dz*b.zz,y=dx*b.ux+dy*b.uy+dz*b.uz,a=pose.yaw*Math.PI/180,c=Math.Cos(a),sn=Math.Sin(a);
            return y>=-padding&&y<=obstacle.height+padding&&Math.Abs(x*c-z*sn)<obstacle.halfWidth+padding&&Math.Abs(x*sn+z*c)<obstacle.halfDepth+padding;
        }
        public static float Yaw(FramePose from,FramePose to){var b=Axes(from);double x=to.x-from.x,y=to.y-from.y,z=to.z-from.z;return (float)(Math.Atan2(x*b.xx+y*b.xy+z*b.xz,x*b.zx+y*b.zy+z*b.zz)*180/Math.PI);}
        static FramePose Offset(FramePose p,Basis b,double x,double z){p.x+=b.xx*x+b.zx*z;p.y+=b.xy*x+b.zy*z;p.z+=b.xz*x+b.zz*z;return p;}
        public static FramePose SurfaceOffset(FramePose pose,double x,double z){double a=pose.yaw*Math.PI/180,c=Math.Cos(a),sn=Math.Sin(a);return Ground(Offset(pose,Axes(pose),x*c+z*sn,z*c-x*sn),pose,null).Value;}
        static bool SlotTaken(IndustrialWorld world,FramePose p,long truck){foreach(var m in world.machines)if(m.id!=truck&&m.haul?.active==true){var j=m.haul;if(j.route[j.pickupIndex].Distance(p)<5.9||j.role!="excavate"&&j.route[j.route.Count-1].Distance(p)<5.9)return true;}return false;}
        static double Radius(FramePose p)=>Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z);
        static FramePose? Ground(FramePose p,FramePose origin,Func<FramePose,FramePose?> project){if(project!=null)return project(p);double r=Radius(p),target=Radius(origin);if(target>=10&&r>0){p.x*=target/r;p.y*=target/r;p.z*=target/r;}return p;}
        static bool Clear(IndustrialWorld world,long truck,FramePose a,FramePose b,Func<FramePose,FramePose?> project,List<FramePose> samples=null){
            double distance=a.Distance(b);if(double.IsInfinity(distance)||distance>500)return false;int count=Math.Max(1,(int)Math.Ceiling(distance/.5));var last=a;double startRadius=Radius(a),endRadius=Radius(b);
            for(int i=1;i<=count;i++){double t=i/(double)count;var p=a;p.x+=(b.x-a.x)*t;p.y+=(b.y-a.y)*t;p.z+=(b.z-a.z)*t;FramePose? ground;if(project!=null)ground=project(p);else{double r=Radius(p);if(startRadius>=10&&endRadius>=10&&r>0){double scale=(startRadius+(endRadius-startRadius)*t)/r;p.x*=scale;p.y*=scale;p.z*=scale;}ground=p;}if(!ground.HasValue)return false;p=i==count?b:ground.Value;if(world.HaulBlocked(p,truck,.15)!=null)return false;if(Math.Abs(Radius(p)-Radius(last))>.05&&Math.Abs(Radius(p)-Radius(last))>last.Distance(p)*.5&&Radius(a)>=10)return false;if(i%4==0||i==count)samples?.Add(p);last=p;}return true;
        }
        sealed class Heap {
            readonly List<int> nodes=new List<int>();readonly List<double> priorities=new List<double>();public int Count=>nodes.Count;
            public void Push(int node,double priority){int i=nodes.Count;nodes.Add(node);priorities.Add(priority);while(i>0){int p=(i-1)/2;if(priorities[p]<=priority)break;nodes[i]=nodes[p];priorities[i]=priorities[p];i=p;}nodes[i]=node;priorities[i]=priority;}
            public int Pop(){int result=nodes[0],last=nodes.Count-1,node=nodes[last];double priority=priorities[last];nodes.RemoveAt(last);priorities.RemoveAt(last);if(last==0)return result;int i=0;while(i*2+1<last){int child=i*2+1;if(child+1<last&&priorities[child+1]<priorities[child])child++;if(priorities[child]>=priority)break;nodes[i]=nodes[child];priorities[i]=priorities[child];i=child;}nodes[i]=node;priorities[i]=priority;return result;}
        }
        sealed class SearchBudget {public int remaining=4096;}
        static List<FramePose> Leg(IndustrialWorld world,long truck,FramePose start,FramePose end,Func<FramePose,FramePose?> project,SearchBudget budget,double margin=24){
            var direct=new List<FramePose>{start};if(Clear(world,truck,start,end,project,direct))return direct;
            var basis=Axes(start);double dx=end.x-start.x,dy=end.y-start.y,dz=end.z-start.z,ex=dx*basis.xx+dy*basis.xy+dz*basis.xz,ez=dx*basis.zx+dy*basis.zy+dz*basis.zz;
            const double cell=3;double minX=Math.Floor((Math.Min(0,ex)-margin)/cell)*cell,minZ=Math.Floor((Math.Min(0,ez)-margin)/cell)*cell;int width=(int)Math.Ceiling((Math.Max(0,ex)+margin-minX)/cell)+1,height=(int)Math.Ceiling((Math.Max(0,ez)+margin-minZ)/cell)+1;if(width>129||height>129)return null;
            int size=width*height;var points=new FramePose[size];var known=new byte[size];var costs=new double[size];var parent=new int[size];var closed=new bool[size];for(int i=0;i<size;i++){costs[i]=double.MaxValue;parent[i]=-1;}
            bool Read(int index){if(known[index]!=0)return known[index]==1;var p=Ground(Offset(start,basis,minX+(index%width)*cell,minZ+(index/width)*cell),start,project);known[index]=(byte)(p.HasValue&&world.HaulBlocked(p.Value,truck,.15)==null?1:2);if(p.HasValue)points[index]=p.Value;return known[index]==1;}
            int begin=(int)Math.Round(-minX/cell)+(int)Math.Round(-minZ/cell)*width,goal=(int)Math.Round((ex-minX)/cell)+(int)Math.Round((ez-minZ)/cell)*width;if(world.HaulBlocked(start,truck)!=null||world.HaulBlocked(end,truck)!=null)return null;points[begin]=start;known[begin]=1;points[goal]=end;known[goal]=1;
            var open=new Heap();costs[begin]=0;open.Push(begin,0);
            while(open.Count>0&&budget.remaining-->0){int current=open.Pop();if(closed[current])continue;closed[current]=true;if(current==goal){var reversed=new List<FramePose>();for(int n=goal;n!=-1;n=parent[n])reversed.Add(points[n]);reversed.Reverse();var route=new List<FramePose>{start};foreach(var p in reversed)if(!Clear(world,truck,route[route.Count-1],p,project,route))return null;if(!Clear(world,truck,route[route.Count-1],end,project,route))return null;return route;}
                int x=current%width,z=current/width;for(int oz=-1;oz<=1;oz++)for(int ox=-1;ox<=1;ox++){if(ox==0&&oz==0||x+ox<0||x+ox>=width||z+oz<0||z+oz>=height)continue;int next=current+ox+oz*width;if(closed[next]||!Read(next))continue;if(ox!=0&&oz!=0&&(!Read(current+ox)||!Read(current+oz*width)))continue;double distance=points[current].Distance(points[next]);if(Radius(start)>=10&&Math.Abs(Radius(points[current])-Radius(points[next]))>distance*.5)continue;if(!Clear(world,truck,points[current],points[next],project))continue;double cost=costs[current]+distance;if(cost>=costs[next])continue;costs[next]=cost;parent[next]=current;open.Push(next,cost+points[next].Distance(end));}}
            return null;
        }
        public sealed class PlanningResult {public IndustrialCommand command;public string error;public double plannedMetres;}
        sealed class LegResult {public List<FramePose> points;public string error;}
        static double SurfaceDistance(FramePose a,FramePose b){double ra=Radius(a),rb=Radius(b);if(ra<10||rb<10)return a.Distance(b);double dot=Math.Max(-1,Math.Min(1,(a.x*b.x+a.y*b.y+a.z*b.z)/(ra*rb)));return Math.Acos(dot)*(ra+rb)*.5+Math.Abs(ra-rb);}
        static FramePose SurfacePoint(FramePose a,FramePose b,double t){double ra=Radius(a),rb=Radius(b);if(ra<10||rb<10){a.x+=(b.x-a.x)*t;a.y+=(b.y-a.y)*t;a.z+=(b.z-a.z)*t;return a;}double dot=Math.Max(-1,Math.Min(1,(a.x*b.x+a.y*b.y+a.z*b.z)/(ra*rb))),angle=Math.Acos(dot),radius=ra+(rb-ra)*t;if(angle<1e-7){a.x+=(b.x-a.x)*t;a.y+=(b.y-a.y)*t;a.z+=(b.z-a.z)*t;double r=Radius(a);a.x*=radius/r;a.y*=radius/r;a.z*=radius/r;return a;}if(dot<-.999999){var basis=Axes(a);double c=Math.Cos(Math.PI*t),sn=Math.Sin(Math.PI*t);a.x=radius*(a.x/ra*c+basis.zx*sn);a.y=radius*(a.y/ra*c+basis.zy*sn);a.z=radius*(a.z/ra*c+basis.zz*sn);return a;}double x=Math.Sin((1-t)*angle)/Math.Sin(angle),y=Math.Sin(t*angle)/Math.Sin(angle);a.x=radius*(a.x/ra*x+b.x/rb*y);a.y=radius*(a.y/ra*x+b.y/rb*y);a.z=radius*(a.z/ra*x+b.z/rb*y);return a;}
        static IEnumerator LongLeg(IndustrialWorld world,long truck,FramePose start,FramePose end,Func<FramePose,FramePose?> project,LegResult result,PlanningResult progress){
            double distance=SurfaceDistance(start,end);if(double.IsNaN(distance)||double.IsInfinity(distance)){result.error="Route must remain on the same body and frame";yield break;}
            if(distance/2>IndustrialWorld.MaximumHaulRoutePoints){result.error="Route data budget reached; use an intermediate logistics site";yield break;}
            int sections=Math.Max(1,(int)Math.Ceiling(distance/120));var points=new List<FramePose>{start};
            for(int section=1;section<=sections;section++){
                var checkpoint=section==sections?(FramePose?)end:Ground(SurfacePoint(start,end,section/(double)sections),start,project);if(!checkpoint.HasValue){result.error="Route crosses water or unsupported terrain";yield break;}
                List<FramePose> local=null;var basis=Axes(checkpoint.Value);
                for(int attempt=0;attempt<(section==sections?1:7)&&local==null;attempt++){
                    var target=attempt==0?checkpoint:Ground(Offset(checkpoint.Value,basis,((attempt+1)/2)*12*(attempt%2==1?1:-1),0),start,project);
                    if(target.HasValue)for(int expansion=0;expansion<3&&local==null;expansion++){
                        local=Leg(world,truck,points[points.Count-1],target.Value,project,new SearchBudget(),expansion==0?24:expansion==1?60:120);
                        if(local==null)yield return null;
                    }
                    if(local==null)yield return null;
                }
                if(local==null){result.error=world.HaulBlocked(points[points.Count-1],truck)??world.HaulBlocked(checkpoint.Value,truck)??"No drivable route in the searched corridor (water, slope or obstacles)";yield break;}
                for(int n=1;n<local.Count;n++)points.Add(local[n]);if(points.Count>IndustrialWorld.MaximumHaulRoutePoints){result.error="Route data budget reached; use an intermediate logistics site";yield break;}
                progress.plannedMetres+=distance/sections;if(section<sections)yield return null;
            }
            result.points=points;
        }
        public static IEnumerator PlanIncremental(IndustrialWorld world,long owner,long truckId,long source,long destination,Func<FramePose,FramePose?> project,PlanningResult result,bool remote=false,string operationRole=null){
            var truck=world.Machine(truckId);if(truck==null||!truck.Definition.Vehicle||truck.owner!=owner){result.error="Choose your vehicle";yield break;}
            if(!world.HaulSource(owner,source,out var pickup,out long stock)||stock==0&&truck.input.bulk.litres==0&&!world.CanWaitForHaulSource(source)||!world.HaulDestination(owner,destination,out var drop,out var clearance)){result.error="Choose a pay-dirt source and a delivery destination";yield break;}
            if(truck.pose.body!=pickup.body||truck.pose.frame!=pickup.frame||truck.pose.body!=drop.body||truck.pose.frame!=drop.frame){result.error="Collection sites must share the vehicle's body and frame";yield break;}
            List<FramePose> first=null,second=null;var pickupBasis=Axes(pickup);double radius=world.Pile(source)!=null?15:7;
            FramePose loaderPose=default;bool loadingSite=operationRole=="transport"&&world.MiningLoaderPose(source,owner,truck.pose,out loaderPose);
            double angle=(operationRole=="tailings"?Yaw(pickup,drop):Yaw(pickup,truck.pose))*Math.PI/180;for(int i=0;i<8&&first==null;i++){double t=angle+i*Math.PI*.25;var candidate=Ground(Offset(pickup,pickupBasis,Math.Sin(t)*radius,Math.Cos(t)*radius),pickup,project);if(!candidate.HasValue||candidate.Value.Distance(pickup)>(world.Pile(source)!=null?16:8)||loadingSite&&candidate.Value.Distance(loaderPose)>11.9||SlotTaken(world,candidate.Value,truckId))continue;var leg=new LegResult();var iterator=LongLeg(world,truckId,truck.pose,candidate.Value,project,leg,result);while(iterator.MoveNext())yield return null;first=leg.points;result.error=leg.error;}
            if(first==null){result.error=result.error??"No clear route or free collection berth";yield break;}
            if(operationRole=="excavate"){int pickupIndex=first.Count-1;first.Add(first[pickupIndex]);var excavation=new IndustrialCommand{kind="haul-start",source=source,target=truckId,count=destination,route=first,routeSplit=pickupIndex,operationRole=operationRole};result.error=world.HaulCommandError(owner,excavation,!remote);if(result.error==null)result.command=excavation;yield break;}
            var lastPickup=first[first.Count-1];var dropBasis=Axes(drop);angle=Yaw(drop,lastPickup)*Math.PI/180;for(int i=0;i<8&&second==null;i++){double t=angle+i*Math.PI*.25;var candidate=Ground(Offset(drop,dropBasis,Math.Sin(t)*clearance,Math.Cos(t)*clearance),drop,project);if(!candidate.HasValue||candidate.Value.Distance(drop)>clearance+1||SlotTaken(world,candidate.Value,truckId))continue;var leg=new LegResult();var iterator=LongLeg(world,truckId,lastPickup,candidate.Value,project,leg,result);while(iterator.MoveNext())yield return null;second=leg.points;result.error=leg.error;}
            if(second==null){result.error=result.error??"No clear route or free delivery berth";yield break;}int split=first.Count-1;for(int i=1;i<second.Count;i++)first.Add(second[i]);if(first.Count>IndustrialWorld.MaximumHaulRoutePoints){result.error="Route data budget reached; use an intermediate logistics site";yield break;}
            var command=new IndustrialCommand{kind="haul-start",source=source,target=truckId,count=destination,route=first,routeSplit=split,operationRole=operationRole??(world.IsTailingsSource(source)?"tailings":null)};result.error=world.HaulCommandError(owner,command,!remote);if(result.error==null)result.command=command;
        }
        public static IndustrialCommand Plan(IndustrialWorld world,long owner,long truckId,long source,long destination,Func<FramePose,FramePose?> project,out string error,bool remote=false,string operationRole=null){var result=new PlanningResult();var iterator=PlanIncremental(world,owner,truckId,source,destination,project,result,remote,operationRole);while(iterator.MoveNext()){}error=result.error;return result.command;}
    }
}
