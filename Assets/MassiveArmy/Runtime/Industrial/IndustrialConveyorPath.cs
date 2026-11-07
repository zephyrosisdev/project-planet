using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    // Shared canonical spline, sampled once per lane. No item physics objects.
    public sealed class IndustrialConveyorPath {
        public const int MaximumAnchors=16;public const double MaximumLength=100;
        public readonly List<FramePose> points=new List<FramePose>();public readonly List<double> distances=new List<double>();public double Length=>distances[distances.Count-1];
        static FramePose Add(FramePose p,double x,double y,double z){p.x+=x;p.y+=y;p.z+=z;return p;}
        public static FramePose Pin(IndustrialMachine machine,bool output){
            if(machine.customConveyorPorts)return output?machine.conveyorOutput:machine.conveyorInput;
            var p=machine.pose;double r=Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z),ux=r<1?0:p.x/r,uy=r<1?1:p.y/r,uz=r<1?0:p.z/r;
            double d=1+uy,xx=uy<-.999999?1:1-ux*ux/d,xy=uy<-.999999?0:-ux,xz=uy<-.999999?0:-ux*uz/d,zx=xz,zy=uy<-.999999?0:-uz,zz=uy<-.999999?-1:1-uz*uz/d;
            double a=p.yaw*Math.PI/180,z=(output?-1:1)*machine.Definition.Depth*.5,x=Math.Sin(a)*z;z*=Math.Cos(a);return Add(p,ux+xx*x+zx*z,uy+xy*x+zy*z,uz+xz*x+zz*z);
        }
        public IndustrialConveyorPath(IndustrialMachine from,IndustrialMachine to,List<FramePose> route){
            var knots=new List<FramePose>{Pin(from,true)};if(route!=null)knots.AddRange(route);knots.Add(Pin(to,false));points.Add(knots[0]);distances.Add(0);
            for(int i=0;i<knots.Count-1;i++){
                var a=knots[i];var b=knots[i+1];var prior=i==0?a:knots[i-1];var next=i+2==knots.Count?b:knots[i+2];
                double tx=(b.x-prior.x)*.4,ty=(b.y-prior.y)*.4,tz=(b.z-prior.z)*.4,sx=(next.x-a.x)*.4,sy=(next.y-a.y)*.4,sz=(next.z-a.z)*.4;
                // Leave and enter sockets normal to their machine face.
                if(i==0){var centre=from.pose;double ux=a.x-centre.x,uy=a.y-centre.y,uz=a.z-centre.z,rr=Math.Sqrt(centre.x*centre.x+centre.y*centre.y+centre.z*centre.z),dot=rr<1?uy:(ux*centre.x+uy*centre.y+uz*centre.z)/rr;ux-=rr<1?0:dot*centre.x/rr;uy-=rr<1?dot:dot*centre.y/rr;uz-=rr<1?0:dot*centre.z/rr;double r=Math.Sqrt(ux*ux+uy*uy+uz*uz),scale=Math.Min(2,a.Distance(b)*.4)/Math.Max(.001,r);tx=ux*scale;ty=uy*scale;tz=uz*scale;}
                if(i==knots.Count-2){var centre=to.pose;double ux=centre.x-b.x,uy=centre.y-b.y,uz=centre.z-b.z,rr=Math.Sqrt(centre.x*centre.x+centre.y*centre.y+centre.z*centre.z),dot=rr<1?uy:(ux*centre.x+uy*centre.y+uz*centre.z)/rr;ux-=rr<1?0:dot*centre.x/rr;uy-=rr<1?dot:dot*centre.y/rr;uz-=rr<1?0:dot*centre.z/rr;double r=Math.Sqrt(ux*ux+uy*uy+uz*uz),scale=Math.Min(2,a.Distance(b)*.4)/Math.Max(.001,r);sx=ux*scale;sy=uy*scale;sz=uz*scale;}
                int samples=Math.Max(8,Math.Min(128,(int)Math.Ceiling(a.Distance(b)*4)));
                for(int n=1;n<=samples;n++){double t=n/(double)samples,t2=t*t,t3=t2*t,h0=2*t3-3*t2+1,h1=t3-2*t2+t,h2=-2*t3+3*t2,h3=t3-t2;var p=a;p.x=h0*a.x+h1*tx+h2*b.x+h3*sx;p.y=h0*a.y+h1*ty+h2*b.y+h3*sy;p.z=h0*a.z+h1*tz+h2*b.z+h3*sz;distances.Add(distances[distances.Count-1]+points[points.Count-1].Distance(p));points.Add(p);}
            }
        }
        public FramePose At(double distance){distance=Math.Max(0,Math.Min(Length,distance));int lo=0,hi=distances.Count-1;while(hi-lo>1){int mid=(lo+hi)/2;if(distances[mid]<distance)lo=mid;else hi=mid;}double span=distances[hi]-distances[lo],t=span<1e-9?0:(distance-distances[lo])/span;var a=points[lo];var b=points[hi];return Add(a,(b.x-a.x)*t,(b.y-a.y)*t,(b.z-a.z)*t);}
    }
    public sealed partial class IndustrialWorld {
        public string ConveyorError(long actorId,IndustrialCommand command,out IndustrialConveyorPath path){
            path=null;var a=Actor(actorId);var from=Machine(command.source);var to=Machine(command.target);if(a==null||a.health<=0||from==null||from.owner!=actorId||!Own(a,to)||from==to||from.Definition.Vehicle||to.Definition.Vehicle)return "Choose an owned output and input; finish within 12 m of the input";
            if(from.pose.body!=to.pose.body||from.pose.frame!=to.pose.frame)return "Belt endpoints must share a frame";
            if(command.route==null||command.route.Count>IndustrialConveyorPath.MaximumAnchors)return "Belt allows at most 16 support nodes";
            foreach(var p in command.route)if(!Valid(p)||p.body!=from.pose.body||p.frame!=from.pose.frame)return "Invalid belt support node";
            path=new IndustrialConveyorPath(from,to,command.route);if(double.IsNaN(path.Length)||double.IsInfinity(path.Length)||path.Length>IndustrialConveyorPath.MaximumLength||path.Length<.5)return "Belt length must be 0.5 to 100 m";if(!string.IsNullOrEmpty(command.item)&&!IndustrialCatalog.Items.ContainsKey(command.item))return "Unknown belt filter";if(!creativeMode&&!a.inventory.Has(RoutedConveyorCost(path.Length)))return "Need one plate and one gear per 5 m of conveyor";return null;
        }
        public static ItemStack[] RoutedConveyorCost(double length){long units=Math.Max(1,(long)Math.Ceiling(length/5));return new[]{IndustrialCatalog.S("plate",units),IndustrialCatalog.S("gear",units)};}
    }
}
