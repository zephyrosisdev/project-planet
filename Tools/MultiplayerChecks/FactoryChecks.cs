using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MassiveArmy.Industrial;

static class FactoryChecks {
    public static int Run(string folder){
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var world=new IndustrialWorld();var actor=world.Register(1,new FramePose{body=1,y=10000,z=-10});world.Register(2,actor.pose);
        IndustrialMachine Machine(string kind,double x,double z){var m=new IndustrialMachine{id=world.AllocateId(),owner=1,definition=kind,pose=new FramePose{body=1,y=10000,x=x,z=z}};world.machines.Add(m);return m;}
        var source=Machine("store",0,10);var target=Machine("store",0,-10);var generator=Machine("generator",5,10);var consumer=Machine("workbench",5,-10);var pump=Machine("pump",-5,10);
        actor.inventory.Add("plate",100,7);actor.inventory.Add("gear",100);source.input.Add("iron-bar",4,12);
        IndustrialCommand Command(string kind,long from,long to)=>new IndustrialCommand{kind=kind,source=from,target=to,route=new List<FramePose>()};
        var power=Command("power",generator.id,consumer.id);
        Check(SurvivalFactoryConnections.Prepare(world,1,power,null)==null&&power.route.Count==2,"Network power routes add server socket exits");
        Check(SurvivalFactoryConnections.Prepare(world,2,Command("power",generator.id,consumer.id),null)!=null,"Network factory creation rejects another player's endpoints");
        var oldPose=actor.pose;actor.pose.x=100;Check(SurvivalFactoryConnections.Prepare(world,1,Command("power",generator.id,consumer.id),null)!=null,"Factory completion requires reach of the owned destination");actor.pose=oldPose;
        var invalid=Command("power",generator.id,consumer.id);invalid.route.Add(new FramePose{body=1,y=double.NaN});Check(SurvivalFactoryConnections.Prepare(world,1,invalid,null)!=null,"Factory routes reject non-finite anchors");
        invalid=Command("power",generator.id,consumer.id);invalid.route.Add(new FramePose{body=2,y=10005});Check(SurvivalFactoryConnections.Prepare(world,1,invalid,null)!=null,"Factory routes reject cross-body anchors");
        invalid=Command("power",generator.id,consumer.id);for(int i=0;i<9;i++)invalid.route.Add(new FramePose{body=1,y=10005});Check(SurvivalFactoryConnections.Prepare(world,1,invalid,null)!=null,"Only eight user power/pipe routing points are accepted");
        Check(SurvivalFactoryConnections.Prepare(world,1,Command("power",generator.id,consumer.id),(p,r)=>"blocked")=="blocked","Headless contact rejection reaches the authority gateway");
        var previous=source.pose;source.pose.frame=123;Check(SurvivalFactoryConnections.Prepare(world,1,Command("belt",source.id,target.id),null)!=null,"Network factory creation rejects unsupported ship-local endpoints");source.pose=previous;
        var belt=Command("belt",source.id,target.id);Check(SurvivalFactoryConnections.Prepare(world,1,belt,null)==null&&belt.definition=="spline-conveyor-v1","Network belts force resource-priced spline construction");
        var path=new IndustrialConveyorPath(source,target,belt.route);long paid=(long)Math.Ceiling(path.Length/5);long originalPlates=actor.inventory.Count("plate"),originalGears=actor.inventory.Count("gear");
        string save=Path.Combine(Path.GetFullPath(folder),"factory-"+Guid.NewGuid().ToString("N")+".xml");
        using(var authority=new IndustrialAuthority(world,save)){
            authority.Save();power.sequence=1;Check(authority.Execute(1,power).accepted&&consumer.powerSource==generator.id,"Validated network power commands use the industrial journal");
            var water=Command("water",pump.id,target.id);Check(SurvivalFactoryConnections.Prepare(world,1,water,null)==null,"Owned pump routes use the same canonical validation");water.sequence=2;Check(authority.Execute(1,water).accepted&&pump.waterTarget==target.id,"Water connection state is owned by the server");
            belt.sequence=3;var placed=authority.Execute(1,belt);Check(placed.accepted&&actor.inventory.Count("plate")==originalPlates-paid&&actor.inventory.Count("gear")==originalGears-paid,"A conveyor spends exact length-based materials once");
            Check(!authority.Execute(1,belt).accepted&&world.belts.Count==1,"Replayed industrial commands cannot build a second belt");
            Check(SurvivalFactoryConnections.Prepare(world,1,Command("belt",source.id,target.id),null)!=null,"A duplicate route is blocked by the server conveyor contact index");
            authority.Step(200);Check(target.input.Count("iron-bar")==4&&source.input.Count("iron-bar")==0&&world.belts[0].items.Count==0,"Factory conveyor simulation transfers inventory independently of visibility");
            Check(Math.Abs(target.input.ActivityOf("iron-bar",4)-12)<1e-8,"Conveyor transfer conserves item activity");
        }
        var restored=IndustrialAuthority.Load(save);Check(restored.Machine(consumer.id).powerSource==generator.id&&restored.Machine(pump.id).waterTarget==target.id&&restored.belts.Count==1,"Crash replay restores power, water and conveyors from the existing journal");
        Check(restored.Machine(target.id).input.Count("iron-bar")==4,"Crash replay preserves transported inventory exactly once");
        using(var authority=new IndustrialAuthority(restored,save)){
            var remove=new IndustrialCommand{sequence=restored.Actor(1).lastSequence+1,kind="remove-belt",target=restored.belts[0].id};Check(authority.Execute(1,remove).accepted&&restored.Actor(1).inventory.Count("plate")==originalPlates&&restored.Actor(1).inventory.Count("gear")==originalGears,"Conveyor removal refunds paid construction exactly once");
            Check(!authority.Execute(1,remove).accepted&&restored.belts.Count==0,"Duplicate conveyor removal cannot duplicate its refund");
        }
        var spatial=new SurvivalInterestIndex();spatial.Add(new SurvivalInterestIndex.Entry{id=-10,pose=new FramePose{body=1,x=1005},extent=20});var found=new HashSet<long>();spatial.Query(new FramePose{body=1,x=490},500,found);Check(found.Contains(-10),"Interest includes a long connection crossing a cell/range boundary");found.Clear();spatial.Query(new FramePose{body=1,x=490},100000,found,true);Check(found.Count==0,"Ship-only proxy queries exclude factory links");
        Check(SurvivalFactoryConnections.WireId(1,"power")!=SurvivalFactoryConnections.WireId(1,"water")&&SurvivalFactoryConnections.WireId(1,"water")!=SurvivalFactoryConnections.WireId(1,"belt")&&SurvivalFactoryConnections.WireId(1,"belt")<0,"Factory wire IDs reserve distinct stable namespaces");
        string locked=Path.Combine(Path.GetFullPath(folder),"checkpoint-lock-"+Guid.NewGuid().ToString("N")+".xml");
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),locked)){
            authority.World.Register(1,new FramePose{body=1});authority.Save();authority.Save();Exception threadError=null;using(var held=new ManualResetEventSlim()){
                var reader=new Thread(()=>{try{using(var file=new FileStream(locked+".bak",FileMode.Open,FileAccess.Read,FileShare.Read)){held.Set();Thread.Sleep(40);}}catch(Exception e){threadError=e;held.Set();}});reader.Start();held.Wait();
                try{authority.World.Actor(1).health=75;authority.Save();}finally{reader.Join();}
                Check(threadError==null&&authority.World.Actor(1).health==75,"Atomic checkpoint replacement tolerates a transient Windows backup reader");
            }
        }
        Check(IndustrialAuthority.Load(locked).Actor(1).health==75,"A retried atomic replacement remains recoverable after closing its writer");
        return checks;
    }
}
