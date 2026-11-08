using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MassiveArmy.Industrial {
    // Canonical, engine-independent cable geometry. Only routing anchors are persisted.
    public static class IndustrialCablePath {
        public const double MaximumLength=50;
        public const int MaximumUserAnchors=8,MaximumAnchors=MaximumUserAnchors+2, SamplesPerSpan=12;
        public static double Height(string id){switch(id){case "water-reservoir":return 3.4;case "wind-turbine":return 6.95;case "generator":return 3.675;case "refinery":return 3.875;case "solar-panel":return 1.7;case "sluice":return 1.55;case "workbench":return 2.05;case "pump":return 2.375;case "battery":case "shielded-store":return 2.2;case "store":case "wire-mill":return 2;case "press":return 2.4;case "foundry":case "assembler":case "munitions":return 2.6;default:return 2.5;}}
        public static FramePose Pin(IndustrialMachine machine,string kind){var p=machine.pose;double r=p.frame!=0?0:Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z),height=Height(machine.definition)+(kind=="power"?.5:.22);if(r<.001)p.y+=height;else{p.x+=p.x/r*height;p.y+=p.y/r*height;p.z+=p.z/r*height;}return p;}
        static double Radius(FramePose p)=>p.frame!=0?p.y:Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z);
        public static FramePose SocketExit(IndustrialMachine machine,string kind,double roofRadius=0){var pin=Pin(machine,kind);double r=Radius(pin),rise=Math.Max(0,roofRadius-r)+.75;if(pin.frame!=0||r<1)pin.y+=rise;else{pin.x+=pin.x/r*rise;pin.y+=pin.y/r*rise;pin.z+=pin.z/r*rise;}return pin;}
        public static List<FramePose> WithSocketClearance(IndustrialMachine from,IndustrialMachine to,string kind,List<FramePose> userPoints){double roof=Math.Max(Radius(Pin(from,kind)),Radius(Pin(to,kind)));var route=new List<FramePose>{SocketExit(from,kind,roof)};if(userPoints!=null)route.AddRange(userPoints);route.Add(SocketExit(to,kind,roof));return route;}
        public static FramePose Point(FramePose a,FramePose b,double t,string kind){var p=a;p.x+=(b.x-a.x)*t;p.y+=(b.y-a.y)*t;p.z+=(b.z-a.z)*t;double x=(a.x+b.x)*.5,y=(a.y+b.y)*.5,z=(a.z+b.z)*.5,r=Math.Sqrt(x*x+y*y+z*z),sag=4*t*(1-t)*Math.Min(kind=="power"?.75:.15,a.Distance(b)*.025);if(p.frame!=0||r<.001)p.y-=sag;else{p.x-=x/r*sag;p.y-=y/r*sag;p.z-=z/r*sag;}return p;}
        public static FramePose Anchor(IndustrialMachine from,IndustrialMachine to,string kind,List<FramePose> route,int index)=>index==0?Pin(from,kind):index==(route?.Count??0)+1?Pin(to,kind):route[index-1];
        public static double Length(IndustrialMachine from,IndustrialMachine to,string kind,List<FramePose> route){double length=0;for(int span=0;span<=(route?.Count??0);span++){var a=Anchor(from,to,kind,route,span);var b=Anchor(from,to,kind,route,span+1);var previous=a;for(int n=1;n<=SamplesPerSpan;n++){var p=Point(a,b,n/(double)SamplesPerSpan,kind);length+=previous.Distance(p);previous=p;}}return length;}
    }
    public sealed partial class IndustrialWorld {
        [XmlIgnore] public long connectionRevision;
        public string ConnectionError(long actorId,IndustrialCommand command){
            var actor=Actor(actorId);var from=Machine(command.source);var to=Machine(command.target);
            if(actor==null||actor.health<=0||from==null||from.owner!=actorId||!Own(actor,to))return "Choose your own machines; move within 12 m of the destination";
            if(from==to)return "Choose a different machine";
            if(from.pose.body!=to.pose.body||from.pose.frame!=to.pose.frame)return "Connections must share a reference frame";
            if(command.kind=="power"){
                if(!from.Definition.PowerProvider||to.Definition.PowerProvider&&to.definition!="battery"||from.definition=="battery"&&to.definition=="battery")return "Connect a power output to a consumer or a generating source to a battery";
            }else if(command.kind!="water"||from.definition!="pump"&&from.definition!="water-reservoir")return "Water pipes start at a pump or reservoir";
            if(command.route!=null){
                if(command.route.Count>IndustrialCablePath.MaximumAnchors)return "Connection allows eight routing points plus two socket exits";
                foreach(var p in command.route)if(!Valid(p)||p.body!=from.pose.body||p.frame!=from.pose.frame)return "Invalid routing anchor";
                double length=IndustrialCablePath.Length(from,to,command.kind,command.route);
                if(!Finite(length)||length>IndustrialCablePath.MaximumLength)return "Route exceeds the 50 m connection limit";
            }else if(from.pose.Distance(to.pose)>IndustrialCablePath.MaximumLength)return "Connection exceeds 50 m";
            return null;
        }
    }
}
