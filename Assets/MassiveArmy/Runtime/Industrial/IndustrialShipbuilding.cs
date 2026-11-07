using System;
using System.Collections.Generic;
using System.Xml.Serialization;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class ShipSocket {public string name,kind;public FramePose pose;}
    public sealed class ShipModuleDefinition {
        public string id;public double width,height,depth,mass,power,generation;public bool room;public ItemStack[] cost;public ShipSocket[] sockets;
        public ShipModuleDefinition(string id,double w,double h,double d,double mass,double power,double generation,bool room,ItemStack[] cost,params ShipSocket[] sockets){this.id=id;width=w;height=h;depth=d;this.mass=mass;this.power=power;this.generation=generation;this.room=room;this.cost=cost;this.sockets=sockets;}
    }
    [Serializable] public sealed class ShipModuleRecord {public long id,parent;public string definition,parentSocket,inputSocket;public FramePose pose;public List<ItemStack> paid=new List<ItemStack>();[XmlIgnore] public bool powered;}
    [Serializable] public sealed class ShipPowerWire {public long source,target;public List<ItemStack> paid=new List<ItemStack>();public List<FramePose> route=new List<FramePose>();}
    [Serializable] public sealed class ShipAssembly {
        public long id,owner,nextModule=1,revision;public string name="Expedition utility ship";public List<ShipModuleRecord> modules=new List<ShipModuleRecord>();public List<ShipPowerWire> wires=new List<ShipPowerWire>();
        public ShipModuleRecord Module(long id)=>modules.Find(m=>m.id==id);
        [XmlIgnore] public double Mass {get{double mass=0;foreach(var m in modules)mass+=ShipModules.Definitions[m.definition].mass;return mass;}}
        public bool Occupied(long module,string socket)=>modules.Exists(m=>m.parent==module&&m.parentSocket==socket);
        public void UpdatePower(){
            foreach(var m in modules)m.powered=ShipModules.Definitions[m.definition].generation>0;
            foreach(var core in modules){double budget=ShipModules.Definitions[core.definition].generation;if(budget<=0)continue;
                foreach(var m in modules){var spec=ShipModules.Definitions[m.definition];if(spec.power<=0||m.powered||!wires.Exists(w=>w.source==core.id&&w.target==m.id))continue;if(budget>=spec.power){budget-=spec.power;m.powered=true;}}
            }
        }
        public int PoweredCount(string definition){int count=0;foreach(var m in modules)if(m.definition==definition&&m.powered)count++;return count;}
        public double PoweredFraction(string definition){int total=0,powered=0;foreach(var m in modules)if(m.definition==definition){total++;if(m.powered)powered++;}return total==0?0:(double)powered/total;}
    }
    public static class ShipModules {
        public static readonly Dictionary<string,ShipModuleDefinition> Definitions=CreateDefinitions();
        public static FramePose P(double x,double y,double z,float yaw=0)=>new FramePose{x=x,y=y,z=z,yaw=yaw};
        static ShipSocket S(string name,string kind,double x,double y,double z,float yaw=0)=>new ShipSocket{name=name,kind=kind,pose=P(x,y,z,yaw)};
        static Dictionary<string,ShipModuleDefinition> CreateDefinitions(){
            var d=new Dictionary<string,ShipModuleDefinition>();
            var sockets=new List<ShipSocket>{S("aft","Hull",0,-2,-24,180),S("forward","Hull",0,-2,24),S("core","Equipment",18,-3.3,-16),S("engineering","Hull",-26,-2,4,-90)};
            foreach(int side in new[]{-1,1}){sockets.Add(S("engine-"+side,"Engine",side*30,-1,-18,180));foreach(int z in new[]{-12,16})sockets.Add(S("thruster-"+side+"-"+z,"Thruster",side*28,-1,z,90*side));}
            d.Add("hangar",new ShipModuleDefinition("hangar",52,11,48,60000,0,0,true,new[]{IndustrialCatalog.S("hull-panel",48),IndustrialCatalog.S("frame",32)},sockets.ToArray()));
            foreach(string id in new[]{"corridor","cockpit","engineering"}){double w=id=="engineering"?12:19,l=id=="cockpit"?16:12;var ports=new List<ShipSocket>{S("aft","Hull",0,0,-l/2,180),S("forward","Hull",0,0,l/2)};if(id=="cockpit")ports.Add(S("power","PowerInput",8.5,-1.5,-6));if(id=="engineering")ports.Add(S("core","Equipment",0,-1.3,0));d.Add(id,new ShipModuleDefinition(id,w,7,l,id=="corridor"?5000:12000,id=="cockpit"?30:0,0,true,new[]{IndustrialCatalog.S("hull-panel",id=="corridor"?6:12),IndustrialCatalog.S("electronics",id=="cockpit"?8:2)},ports.ToArray()));}
            d.Add("power-core",new ShipModuleDefinition("power-core",5,3.4,7,8000,0,2000,false,new[]{IndustrialCatalog.S("reactor-part",8),IndustrialCatalog.S("wire",16)},S("mount","Equipment",0,0,0),S("power","PowerOutput",0,1.6,3.6)));
            d.Add("main-engine",new ShipModuleDefinition("main-engine",8,7,18,10000,600,0,false,new[]{IndustrialCatalog.S("engine",4),IndustrialCatalog.S("frame",4)},S("mount","Engine",0,0,0,180),S("power","PowerInput",0,2,7.5)));
            d.Add("thruster",new ShipModuleDefinition("thruster",4,4,6,2000,60,0,false,new[]{IndustrialCatalog.S("engine",1),IndustrialCatalog.S("motor",2)},S("mount","Thruster",0,0,0,180),S("power","PowerInput",0,1.8,2)));
            return d;
        }
        public static FramePose Transform(FramePose parent,FramePose local){double a=parent.yaw*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);return P(parent.x+local.x*c+local.z*s,parent.y+local.y,parent.z-local.x*s+local.z*c,parent.yaw+local.yaw);}
        public static ShipSocket Socket(ShipModuleRecord m,string name)=>m==null?null:Array.Find(Definitions[m.definition].sockets,s=>s.name==name);
        public static FramePose Attach(ShipModuleRecord parent,string output,string kind,string input){var target=Transform(parent.pose,Socket(parent,output).pose);var pin=Array.Find(Definitions[kind].sockets,s=>s.name==input);float yaw=target.yaw-pin.pose.yaw+(pin.kind=="Hull"?180:0);var rotated=Transform(P(0,0,0,yaw),pin.pose);return P(target.x-rotated.x,target.y-rotated.y,target.z-rotated.z,yaw);}
        public static ShipModuleRecord Add(ShipAssembly ship,long parent,string output,string kind,string input){var m=new ShipModuleRecord{id=ship.nextModule++,parent=parent,parentSocket=output,inputSocket=input,definition=kind,pose=parent==0?P(0,1,-14):Attach(ship.Module(parent),output,kind,input)};ship.modules.Add(m);ship.revision++;return m;}
        public static ShipAssembly Starter(long owner){var ship=new ShipAssembly{id=owner,owner=owner};var root=Add(ship,0,null,"hangar",null);var corridor=Add(ship,root.id,"forward","corridor","aft");Add(ship,corridor.id,"forward","cockpit","aft");var core=Add(ship,root.id,"core","power-core","mount");foreach(var socket in Definitions["hangar"].sockets)if(socket.kind=="Engine"||socket.kind=="Thruster")Add(ship,root.id,socket.name,socket.kind=="Engine"?"main-engine":"thruster","mount");foreach(var m in ship.modules)if(Definitions[m.definition].power>0)ship.wires.Add(new ShipPowerWire{source=core.id,target=m.id});ship.UpdatePower();return ship;}
        public static bool Overlap(ShipModuleRecord a,ShipModuleRecord b){var x=Definitions[a.definition];var y=Definitions[b.definition];double ar=a.pose.yaw*Math.PI/180,br=b.pose.yaw*Math.PI/180;double aw=Math.Abs(Math.Cos(ar))*x.width+Math.Abs(Math.Sin(ar))*x.depth,ad=Math.Abs(Math.Sin(ar))*x.width+Math.Abs(Math.Cos(ar))*x.depth,bw=Math.Abs(Math.Cos(br))*y.width+Math.Abs(Math.Sin(br))*y.depth,bd=Math.Abs(Math.Sin(br))*y.width+Math.Abs(Math.Cos(br))*y.depth;return Math.Abs(a.pose.x-b.pose.x)<(aw+bw)/2-.12&&Math.Abs(a.pose.y-b.pose.y)<(x.height+y.height)/2-.12&&Math.Abs(a.pose.z-b.pose.z)<(ad+bd)/2-.12;}
    }
    public sealed partial class IndustrialWorld {
        public List<ShipAssembly> ships=new List<ShipAssembly>();
        [NonSerialized,XmlIgnore] public Func<IndustrialActor,ShipAssembly,IndustrialCommand,string> ShipActionValidator;
        public ShipAssembly Ship(long owner){foreach(var ship in ships)if(ship.owner==owner)return ship;return null;}
        public string ShipCommandError(long actor,IndustrialCommand command){var who=Actor(actor);return who==null?"Unknown actor":PrepareShip(who,command,out _);}
        string PrepareShip(IndustrialActor actor,IndustrialCommand command,out Action apply){
            apply=null;var ship=Ship(actor.id);if(command.kind=="ship-initialize"){if(ship!=null)return "Ship already initialized";apply=()=>ships.Add(ShipModules.Starter(actor.id));return null;}
            if(ship==null||ship.owner!=actor.id)return "No owned ship";string access=ShipActionValidator?.Invoke(actor,ship,command);if(access!=null)return access;
            var parent=ship.Module(command.source);
            if(command.kind=="ship-place"){
                if(ship.modules.Count>=64)return "Prototype ship module budget reached (64)";
                if(parent==null||!ShipModules.Definitions.TryGetValue(command.definition??"",out var spec))return "Unknown module or parent";
                var output=ShipModules.Socket(parent,command.item);var input=Array.Find(spec.sockets,s=>s.name==command.operationRole);
                if(output==null||input==null||output.kind!=input.kind||output.kind.StartsWith("Power",StringComparison.Ordinal))return "Incompatible structural sockets";
                if(ship.Occupied(parent.id,output.name)||parent.parent!=0&&parent.inputSocket==output.name)return "Socket occupied";
                if(parent.definition=="hangar"&&parent.parent==0&&output.name=="aft")return "Keep the rear hatch and vehicle ramp clear";
                if(!creativeMode&&!actor.inventory.Has(spec.cost))return "Missing manufactured components";
                var pose=ShipModules.Attach(parent,output.name,spec.id,input.name);var candidate=new ShipModuleRecord{definition=spec.id,pose=pose};foreach(var other in ship.modules)if(other.id!=parent.id&&ShipModules.Overlap(candidate,other)&&!(spec.id=="power-core"&&ShipModules.Definitions[other.definition].room))return "Module overlaps another ship section";
                apply=()=>{var receipt=IndustrialConstruction.Receipt(actor.inventory,spec.cost,creativeMode,true);var added=ShipModules.Add(ship,parent.id,output.name,spec.id,input.name);added.paid=receipt;};return null;
            }
            if(command.kind=="ship-remove"){
                if(parent==null||parent.parent==0)return "The root hangar cannot be removed";
                if(ship.modules.Exists(m=>m.parent==parent.id))return "Remove attached sections first";
                if(ship.wires.Exists(w=>w.source==parent.id||w.target==parent.id))return "Disconnect power cables before removing this section";
                if(!actor.inventory.CanReserve(parent.paid))return "Make room for the recovered components";
                apply=()=>{IndustrialConstruction.Return(actor.inventory,parent.paid);ship.wires.RemoveAll(w=>w.source==parent.id||w.target==parent.id);ship.modules.Remove(parent);ship.revision++;};return null;
            }
            if(command.kind=="ship-wire"||command.kind=="ship-unwire"){
                var target=ship.Module(command.target);if(parent==null||target==null||parent.id==target.id||ShipModules.Definitions[parent.definition].generation<=0||ShipModules.Definitions[target.definition].power<=0)return "Connect a power core output to a powered system";
                if(command.kind=="ship-unwire"){var cable=ship.wires.Find(w=>w.source==parent.id&&w.target==target.id);if(cable==null)return "No such cable";if(!actor.inventory.CanReserve(cable.paid))return "Make inventory space to recover the wire";apply=()=>{IndustrialConstruction.Return(actor.inventory,cable.paid);ship.wires.Remove(cable);ship.revision++;};return null;}
                if(ship.wires.Exists(w=>w.target==target.id))return "System already wired; disconnect its cable first";
                if(command.route!=null&&command.route.Count>8)return "At most eight cable route points";
                double length=0;var previous=ShipModules.Transform(parent.pose,ShipModules.Socket(parent,"power").pose);if(command.route!=null)foreach(var point in command.route){if(!Valid(point)||Math.Abs(point.x)>200||Math.Abs(point.y)>200||Math.Abs(point.z)>200)return "Invalid cable route";length+=previous.Distance(point);previous=point;}var end=ShipModules.Socket(target,"power");length+=previous.Distance(end==null?target.pose:ShipModules.Transform(target.pose,end.pose));if(length>150)return "Ship cable route exceeds 150 m";
                long cost=Math.Max(1,(long)Math.Ceiling(length/5));if(!creativeMode&&actor.inventory.Count("wire")<cost)return "Missing wire";
                apply=()=>{var receipt=IndustrialConstruction.Receipt(actor.inventory,new[]{IndustrialCatalog.S("wire",cost)},creativeMode,true);ship.wires.Add(new ShipPowerWire{source=parent.id,target=target.id,paid=receipt,route=command.route==null?new List<FramePose>():new List<FramePose>(command.route)});ship.revision++;};return null;
            }
            return "Unknown ship action";
        }
    }
}
