using System;
using System.IO;
using MassiveArmy.Industrial;
static class RespawnChecks {
    public static void Run(Action<bool,string> check){
        var w=new IndustrialWorld();var actor=w.Register(1,new FramePose{body=1,y=10000});actor.inventory.Add("iron-bar",20);actor.inventory.Add("plate",20);actor.inventory.Add("wire",20);actor.inventory.Add("electronics",10);
        check(IndustrialCatalog.Handcraft.Contains("sleeping-bag")&&IndustrialCatalog.Handcraft.Contains("sleeping-pod"),"Sleeping bags and pods are discoverable hand-crafting recipes");
        long stone=actor.inventory.Count("stone");var craft=w.Execute(1,new IndustrialCommand{sequence=1,kind="craft",item="sleeping-bag",count=1});for(int i=0;i<50;i++)w.Step();check(craft.accepted&&actor.inventory.Count("sleeping-bag")==1&&actor.inventory.Count("stone")==stone-12,"Crafting a sleeping bag consumes resources and produces a real deployable");
        string path=Path.GetFullPath("TestResults/Survival/Respawn/core-"+Guid.NewGuid().ToString("N")+".xml");Directory.CreateDirectory(Path.GetDirectoryName(path));long bag,pod;
        using(var authority=new IndustrialAuthority(w,path)){
            var ground=new FramePose{body=1,x=4,y=10000};check(authority.PlaceRespawnPoint(1,"sleeping-bag",ground,ground)==null,"A crafted sleeping bag places through an authoritative transaction");bag=w.respawnPoints[0].id;
            check(actor.inventory.Count("sleeping-bag")==0&&w.RespawnPoint(bag).readyAtTick==w.tick+1200,"Placement consumes one item and starts that point's 120-second cooldown");
            check(authority.PlaceRespawnPoint(1,"sleeping-bag",ground,ground)!=null&&w.respawnPoints.Count==1,"Repeated placement cannot duplicate a consumed deployable");
            actor.inventory.Add("sleeping-pod",1);authority.Save();var local=new FramePose{body=1,frame=1,x=2,y=-4,z=25};var resolved=new FramePose{body=1,x=8,y=10000};check(authority.PlaceRespawnPoint(1,"sleeping-pod",local,resolved)==null,"A sleeping pod preserves a ship-relative canonical pose");pod=w.respawnPoints[1].id;
            check(authority.UseRespawnPoint(1,bag,ground)!=null,"Living players cannot use death respawn to teleport");authority.RecordPlayerDeath(1);check(authority.UseRespawnPoint(1,bag,ground)!=null&&actor.health==0,"A dead player cannot bypass a point's cooldown");
            for(int batch=0;batch<12;batch++)authority.Step(100);long podReady=w.RespawnPoint(pod).readyAtTick;check(authority.UseRespawnPoint(1,bag,ground)==null&&actor.health==100&&actor.dose==0&&actor.exposure==0,"A ready point revives the dead player and clears lethal dose state");check(w.RespawnPoint(bag).readyAtTick==w.tick+1200&&w.RespawnPoint(pod).readyAtTick==podReady,"Using one respawn point leaves every other cooldown independent");
            authority.RecordPlayerDeath(1);var other=w.Register(2,ground);other.health=0;check(authority.UseRespawnPoint(2,pod,resolved)!=null,"Players cannot respawn at someone else's pod");
            check(authority.UseRespawnPoint(1,0,ground)==null,"A default surface spawn remains available while bags cool down");
            check(authority.ManageRespawnPoint(1,bag,ground,"Mining camp")==null&&w.RespawnPoint(bag).name=="Mining camp","Owned reachable respawn points can be named");
            check(authority.ManageRespawnPoint(1,bag,ground,new string('x',49))!=null,"Respawn point names have a bounded size");
            // Keep the final snapshot behind the journal to exercise crash recovery.
        }
        var restored=IndustrialAuthority.Load(path);check(restored.RespawnPoint(bag).name=="Mining camp"&&restored.RespawnPoint(pod).pose.frame==1&&restored.RespawnPoint(pod).pose.z==25,"Save and journal recovery preserve names, identities and ship-local poses");check(restored.RespawnPoint(bag).readyAtTick>restored.tick&&restored.RespawnPoint(pod).readyAtTick<=restored.tick,"Recovery preserves independent remaining cooldowns without offline catch-up");var again=IndustrialAuthority.Load(path);check(again.respawnPoints.Count==2&&again.Actor(1).health==100,"Replaying the journal again does not duplicate points or revivals");
        using(var authority=new IndustrialAuthority(restored)){var pose=restored.RespawnPoint(bag).pose;check(authority.ManageRespawnPoint(1,bag,pose)==null&&restored.RespawnPoint(bag)==null&&restored.Actor(1).inventory.Count("sleeping-bag")==1,"Picking up a point removes it from the respawn list and returns one deployable");check(authority.UseRespawnPoint(1,bag,pose)!=null,"A removed point cannot be used by a stale selection");}
    }
}

