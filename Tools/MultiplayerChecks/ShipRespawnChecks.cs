using System;
using System.IO;
using MassiveArmy.Industrial;

static partial class ShipRespawnChecks {
    internal static void Run(Action<bool,string> check,string folder){
        string path=Path.Combine(Path.GetFullPath(folder),"ship-respawn-"+Guid.NewGuid().ToString("N")+".xml");long actorId,pointId;
        var canonical=new FramePose{body=1,x=30,y=10000};var local=new FramePose{body=1,frame=97821,x=2,y=-3.95,z=-20};
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),path)){
            var player=authority.RegisterSurvivor("ship-respawn-owner","Owner",canonical,"respawn-world","instance");actorId=player.actor;var actor=authority.World.Actor(actorId);
            authority.World.multiplayer.ships.Add(new SurvivalShipState{entity=local.frame,owner=actorId,pose=canonical});actor.inventory.Add("sleeping-bag",2);actor.inventory.activeSlot=actor.inventory.items.Find(s=>s.item=="sleeping-bag").slot;authority.Save();
            var foreign=local;foreign.frame++;
            check(authority.PlaceRespawnPoint(actorId,"sleeping-bag",foreign,canonical)!=null&&actor.inventory.Count("sleeping-bag")==2,"Unknown ship frames cannot consume respawn items");
            check(authority.PlaceRespawnPoint(actorId,"sleeping-bag",local,canonical)==null&&actor.inventory.Count("sleeping-bag")==1,"Owned stable ship IDs support paid local respawn placement");pointId=authority.World.respawnPoints[0].id;
            check(authority.PlaceRespawnPoint(actorId,"sleeping-bag",local,canonical)!=null&&actor.inventory.Count("sleeping-bag")==1,"Overlapping ship respawn points cannot consume a second item");
            authority.RecordPlayerDeath(actorId);authority.World.RespawnPoint(pointId).readyAtTick=0;authority.Save();
            check(authority.UseShipRespawnPoint(actorId,pointId,foreign.frame,foreign,canonical)!=null,"Revival cannot substitute a different parent identity");
            check(authority.UseShipRespawnPoint(actorId,pointId,local.frame,local,canonical)==null,"Ready owned ship respawn commits revival and local attachment together");
        }
        var recovered=IndustrialAuthority.Load(path);var survivor=recovered.multiplayer.players.Find(p=>p.actor==actorId);
        check(recovered.Actor(actorId).health==100&&survivor.interiorWalking&&survivor.attachedShip==local.frame&&survivor.interiorPose.Distance(local)==0,"Journal recovery restores living ship-local attachment without a subsequent network checkpoint");
        check(recovered.RespawnPoint(pointId).readyAtTick>recovered.tick&&recovered.Actor(actorId).inventory.Count("sleeping-bag")==1,"Ship revival preserves cooldown and paid inventory through crash recovery");
        recovered.InventoryPoseResolver=pose=>new FramePose{body=pose.body,x=canonical.x+pose.x,y=canonical.y+pose.y,z=canonical.z+pose.z};
        check(recovered.InteractionDistance(canonical,local)<30&&double.IsPositiveInfinity(recovered.InteractionDistance(new FramePose{body=2},local)),"Cross-frame reach resolves canonical parent coordinates while preserving body isolation");
        var machine=new IndustrialMachine{definition="store",pose=local};var pin=IndustrialCablePath.Pin(machine,"power");var conveyor=IndustrialConveyorPath.Pin(machine,true);
        check(Math.Abs(pin.x-local.x)<1e-8&&Math.Abs(pin.z-local.z)<1e-8&&pin.y>local.y&&Math.Abs(conveyor.y-local.y-1)<1e-8,"Ship-local cables and conveyor sockets use deck up rather than a radial origin");
        var owner=recovered.Actor(actorId);owner.pose=recovered.InventoryPoseResolver(local).Value;owner.inventory.Add("plate",20);owner.inventory.Add("gear",20);
        IndustrialMachine Add(string kind,double z){var pose=local;pose.z=z;var value=new IndustrialMachine{id=recovered.AllocateId(),owner=actorId,definition=kind,pose=pose};recovered.machines.Add(value);return value;}
        var generator=Add("generator",-14);var consumer=Add("workbench",-22);var power=new IndustrialCommand{kind="power",source=generator.id,target=consumer.id,route=new System.Collections.Generic.List<FramePose>()};
        check(SurvivalFactoryConnections.Prepare(recovered,actorId,power,null)==null&&power.route.TrueForAll(p=>p.frame==local.frame),"Ship-local factory power routes preserve stable parent IDs and validate canonical interaction reach");
        var source=Add("store",-14);var destination=Add("store",-22);var belt=new IndustrialCommand{kind="belt",source=source.id,target=destination.id,route=new System.Collections.Generic.List<FramePose>()};
        check(SurvivalFactoryConnections.Prepare(recovered,actorId,belt,null)==null,"A resource-priced conveyor can connect reachable storage in an owned ship");
        recovered.multiplayer.ships[0].owner++;
        check(SurvivalFactoryConnections.Prepare(recovered,actorId,belt,null)!=null,"Owning machine endpoints cannot grant access to another player's ship frame");
    }
}
