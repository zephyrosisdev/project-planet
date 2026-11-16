using System;
using System.IO;
using MassiveArmy.Industrial;
internal static class CampChecks {
    internal static void Run(Action<bool,string> check){
        var world=new IndustrialWorld();var a=world.Register(1,new FramePose{body=1,y=10000,z=125,x=-64});var b=world.Register(2,a.pose);a.inventory.EnableSlots(40);b.inventory.EnableSlots(40);
        var camp=new SurvivalCampState{id=world.nextId,name="Journal camp",pose=new FramePose{body=1,y=10000}};for(int i=0;i<2;i++)camp.pads.Add(new SurvivalCampPad{index=i,pose=new FramePose{body=1,x=i==0?-90:90,y=10008}});for(int i=0;i<3;i++)camp.shops.Add(new SurvivalCampShop{index=i,name="Shop "+i,pose=new FramePose{body=1,x=(i-1)*64,y=10000,z=125},stock=SurvivalBarterCatalogue.InitialStock(i)});
        string path=Path.GetFullPath("TestResults/Multiplayer/camp-domain-"+Guid.NewGuid().ToString("N")+".xml");
        using(var authority=new IndustrialAuthority(world,path)){authority.Save();authority.InitializeSurvivalCamp(camp);check(camp.Valid&&world.Camp(camp.id)==camp&&world.nextId==camp.id+1,"Neutral camps initialize through the existing authoritative journal");
            var command=new IndustrialCommand{sequence=1,kind="camp-barter",target=camp.id,source=0,item="ration",count=2};check(authority.Execute(1,command).accepted&&a.inventory.Count("stone")==56&&a.inventory.Count("ration")==2,"A validated barter transfers exact payment and deliveries");
            check(camp.shops[0].stock.Count("stone")==8&&camp.shops[0].stock.Count("ration")==198,"Shop stock retains payment and supplies the purchased goods");
            authority.Execute(1,command);check(a.inventory.Count("stone")==56&&a.inventory.Count("ration")==2&&camp.shops[0].stock.Count("ration")==198,"A duplicate barter cannot spend or deliver twice");
            long stone=b.inventory.Count("stone");b.pose.z+=20;check(!authority.Execute(2,new IndustrialCommand{sequence=1,kind="camp-barter",target=camp.id,source=0,item="ration",count=1}).accepted&&b.inventory.Count("stone")==stone,"Remote barter is rejected without mutating payment");b.pose=a.pose;
            check(!authority.Execute(2,new IndustrialCommand{sequence=1,kind="camp-barter",target=camp.id,source=1,item="ration",count=1}).accepted,"Clients cannot substitute a different shop for a priced offer");
            check(!authority.Execute(2,new IndustrialCommand{sequence=1,kind="camp-barter",target=camp.id,source=0,item="ration",count=long.MaxValue}).accepted,"Malformed barter quantities are rejected before multiplication");
        }
        var restored=IndustrialAuthority.Load(path);check(restored.camps.Count==1&&restored.camps[0].pads.Count==2&&restored.camps[0].shops[0].stock.Count("ration")==198&&restored.Actor(1).inventory.Count("ration")==2,"Crash recovery replays camp initialization and barter from a pre-camp checkpoint");
        using(var authority=new IndustrialAuthority(restored)){var c=restored.camps[0];var shop=c.shops[0];shop.stock.Extract("ration",197);a=restored.Actor(1);b=restored.Actor(2);a.pose=b.pose=shop.pose;long total=shop.stock.Count("ration")+a.inventory.Count("ration")+b.inventory.Count("ration");
            bool first=authority.Execute(a.id,new IndustrialCommand{sequence=a.lastSequence+1,kind="camp-barter",target=c.id,source=0,item="ration",count=1}).accepted;bool second=authority.Execute(b.id,new IndustrialCommand{sequence=b.lastSequence+1,kind="camp-barter",target=c.id,source=0,item="ration",count=1}).accepted;
            check(first&&!second&&shop.stock.Count("ration")==0&&shop.stock.Count("ration")+a.inventory.Count("ration")+b.inventory.Count("ration")==total,"Two buyers cannot oversell the final unit; inventory is conserved across player and shop");
            var tiny=restored.Register(3,shop.pose);tiny.inventory.Extract("fuel",8);tiny.inventory.EnableSlots(1);shop.stock.Add("ration",1);long before=tiny.inventory.Count("stone");check(!authority.Execute(3,new IndustrialCommand{sequence=1,kind="camp-barter",target=c.id,source=0,item="ration",count=1}).accepted&&tiny.inventory.Count("stone")==before&&shop.stock.Count("ration")==1,"Full player slots reject the entire trade without losing payment or stock");
            b.pose.body=2;check(!authority.Execute(b.id,new IndustrialCommand{sequence=b.lastSequence+1,kind="camp-barter",target=c.id,source=0,item="ration",count=1}).accepted,"Barter cannot cross planetary reference frames");
            check(restored.CampReserved(c.pose)&&!restored.CampReserved(new FramePose{body=1,x=1000,y=10000}),"Camp construction exclusion is bounded to its local surface footprint");
        }
        var config=new SurvivalServerConfig{evaBoostSpeed=1};bool rejected=false;try{config.Validate();}catch(ArgumentException){rejected=true;}check(rejected,"Server EVA limits reject a boost slower than ordinary suit motion");
    }
}
