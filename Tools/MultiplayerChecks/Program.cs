using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MassiveArmy.Industrial;

static class Program {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Main(){
        var chatInput=new MassiveArmy.Networking.SurvivalChatInput();
        Check(!chatInput.Escape(0)&&!chatInput.Blocks(0),"Escape outside chat remains available to the game menu");
        Check(chatInput.Enter(1)==null&&chatInput.Typing&&chatInput.Blocks(2),"Enter opens chat and keeps gameplay blocked while typing");
        chatInput.Text="  hello crew  ";Check(chatInput.Enter(3)=="hello crew"&&!chatInput.Typing&&chatInput.Text=="","Enter sends a trimmed non-empty message and clears input");
        Check(chatInput.Blocks(3)&&!chatInput.Blocks(4),"Sending cannot leak the same Enter frame into gameplay");
        chatInput.Enter(5);chatInput.Text="cancel this";Check(chatInput.Escape(6)&&chatInput.Text==""&&!chatInput.Typing&&chatInput.Blocks(6),"Escape cancels, clears and consumes its frame instead of opening pause");
        chatInput.Enter(7);chatInput.Text="   ";Check(chatInput.Enter(8)==null&&!chatInput.Typing,"Empty chat closes without sending a message");
        Check(chatInput.CanTransmit(9,true,true,true)&&!chatInput.CanTransmit(9,false,true,true)&&!chatInput.CanTransmit(9,true,false,true)&&!chatInput.CanTransmit(9,true,true,false),"Push-to-talk requires focus, a held key and a joined voice channel");
        chatInput.Enter(10);Check(!chatInput.CanTransmit(11,true,true,true),"Opening text chat suppresses a held push-to-talk key");chatInput.Escape(12);Check(!chatInput.CanTransmit(12,true,true,true)&&chatInput.CanTransmit(13,true,true,true),"Cancelling text cannot unmute voice on the same input frame");
        var config=new SurvivalServerConfig();config.Validate();
        string profile=config.AuthenticationProfile();Check(profile.Length==30&&profile.StartsWith("server_",StringComparison.Ordinal),"Server Authentication profile stays within Unity's 30-character limit");Check(profile==config.AuthenticationProfile(),"Server profiles remain stable across restart");config.instanceId="other-instance";Check(profile!=config.AuthenticationProfile(),"Distinct server instances do not share an Authentication profile");config.instanceId="region-1";
        foreach(int invalid in new[]{0,150,200}){config.maxPlayers=invalid;bool rejected=false;try{config.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"Reject Relay capacity "+invalid);}config.maxPlayers=100;
        var clock=new SurvivalTickClock();int steps=0;for(int i=0;i<60;i++)clock.Advance(1.0/60,30,()=>steps++);Check(steps==30,"One wall second produces 30 simulation ticks");
        var recoveredClock=new SurvivalTickClock();recoveredClock.Restore(90000);recoveredClock.Advance(1.0/30,30,()=>{});Check(recoveredClock.Tick==90001,"Persistent cooldowns continue on the restored server clock");
        var budget=new SurvivalEntityTickBudget();Check(budget.TryAdvance(1,10)&&!budget.TryAdvance(1,10),"Leaving a pilot/driver seat cannot advance the same craft twice in one server tick");Check(budget.TryAdvance(1,11)&&budget.TryAdvance(2,10),"Other craft and later ticks retain independent simulation budgets");
        var queue=new SurvivalInputQueue();for(uint i=1;i<=90;i++)queue.Enqueue(new SurvivalInput{sequence=i,y=1},0,30);
        var inbound=new SurvivalMessageBudget();for(int i=0;i<30;i++)inbound.Action(0,30,0);Check(!inbound.Action(0,30,100),"Malformed action packets still consume the pre-parse connection budget");Check(inbound.Action(30,30,100),"Action admission refills only as the server clock advances");for(int i=0;i<4;i++)inbound.Action(30,30,16384);Check(!inbound.Action(30,30,100),"Oversized action throughput is bounded independently of command contents");for(int i=0;i<90;i++)inbound.Input(30,30,0);Check(!inbound.Input(30,30,100)&&inbound.Input(60,30,100),"Malformed input floods cannot bypass the pre-parse packet limit or advance time");
        for(int i=0;i<30;i++)queue.Next();Check(clock.Tick==30,"Input flooding cannot advance time");
        Check(!queue.Enqueue(new SurvivalInput{sequence=1,y=1},0,30),"Reject replay");Check(!queue.Enqueue(new SurvivalInput{sequence=100,x=float.NaN},0,30),"Reject NaN");
        queue.Clear();queue.Enqueue(new SurvivalInput{sequence=100,jump=true,y=1,lookX=5},30,30);queue.Next();var held=queue.Next();Check(!held.jump&&held.lookX==0,"Do not repeat edge inputs");for(int i=0;i<10;i++)held=queue.Next();Check(held.y==0,"Stop movement after missing inputs");
        var stamped=new SurvivalInterestDelivery();var stamp=new SurvivalInterestDelivery.Fingerprint{kind="ship",data="stable",pose=new FramePose{body=1,x=10,y=10000}};
        stamped.Commit(new[]{stamped.Stage(90,stamp,0,2,10,100)});Check(stamped.IsCurrent(90,stamp),"Unchanged structured fingerprints can skip JSON encoding before scheduling");
        var altered=stamp;altered.pose.x++;Check(!stamped.IsCurrent(90,altered),"Canonical position changes invalidate structured delivery fingerprints");
        altered=stamp;altered.pose.frame=500;Check(!stamped.IsCurrent(90,altered),"Parent-frame changes invalidate structured delivery fingerprints");
        altered=stamp;altered.kind="ship-proxy";Check(!stamped.IsCurrent(90,altered),"Detailed/proxy kind transitions remain visible even with unchanged data");
        stamped.Stage(90,altered,5,3,10,100);stamped.IsCurrent(90,stamp);Check(stamped.Stage(90,altered,99,3,10,100).pendingSince==99,"Returning to delivered state clears an obsolete pending-update age");
        var delivery=new SurvivalInterestDelivery();var desired=new HashSet<long>{1};
        var unsent=delivery.Stage(1,"state-a",0,2,20,100);
        Check(delivery.Removals(new HashSet<long>()).Count==0&&delivery.Stage(1,"state-a",1,2,20,100)!=null,"Budget-deferred entry is not recorded as delivered");
        delivery.Commit(new[]{unsent});Check(delivery.Stage(1,"state-a",2,2,20,100)==null,"Unchanged delivered state is suppressed");
        var removals=delivery.Removals(new HashSet<long>());
        Check(removals.Count==1&&delivery.Removals(new HashSet<long>()).Count==1,"An unsent removal remains pending for a later packet");
        delivery.CommitRemovals(removals);Check(delivery.Stage(1,"state-a",3,2,20,100)!=null,"Interest re-entry restores full state even when its signature is unchanged");
        var moved=delivery.Stage(1,"pose-changed",4,2,20,100);delivery.Commit(new[]{moved});Check(delivery.Stage(1,"pose-changed",5,2,20,100)==null,"Canonical pose changes participate in committed delta state");
        bool farDelivered=false;var fairness=new SurvivalInterestDelivery();int farTick=-1;
        for(int tick=0;tick<=65;tick++){
            var choices=new List<SurvivalInterestDelivery.Candidate>{fairness.Stage(10,"moving-"+tick,tick,2,1,100)};
            var far=fairness.Stage(20,"far",tick,3,100000,100);if(far!=null)choices.Add(far);
            var chosen=SurvivalInterestDelivery.Select(choices,tick,30,100);fairness.Commit(chosen);
            if(chosen.Exists(c=>c.id==20)){farDelivered=true;farTick=tick;break;}
        }
        Check(farDelivered&&farTick==60,"A continuously changing nearby entity cannot permanently starve a lower-priority proxy");
        var bounded=new SurvivalInterestDelivery();var choicesBySize=new List<SurvivalInterestDelivery.Candidate>{bounded.Stage(30,"large",0,1,1,1000),bounded.Stage(31,"small",0,2,2,80)};
        var fits=SurvivalInterestDelivery.Select(choicesBySize,0,30,100);Check(fits.Count==1&&fits[0].id==31,"Oversized changes do not block smaller changes or exceed a byte budget");
        Check(SurvivalInterestDelivery.Select(choicesBySize,0,30,-1).Count==0,"A base snapshot larger than available tokens commits no entity changes");
        bounded.RetainPending(new HashSet<long>{31});Check(bounded.Stage(30,"large",100,1,1,1000).pendingSince==100,"Unsent entities leaving interest do not retain stale scheduling priority");
        var churn=new SurvivalInterestDelivery();for(int i=1;i<=300;i++)churn.Commit(new[]{churn.Stage(i,"entry",0,2,i,1)});
        var firstExit=churn.Removals(new HashSet<long>());Check(firstExit.Count==256,"Boundary removals are bounded per packet");churn.CommitRemovals(firstExit);
        Check(churn.Removals(new HashSet<long>()).Count==44,"Remaining boundary removals survive the first committed batch");
        var spatial=new SurvivalInterestIndex();spatial.Add(new SurvivalInterestIndex.Entry{id=1,pose=new FramePose{body=1,x=-1}});spatial.Add(new SurvivalInterestIndex.Entry{id=2,pose=new FramePose{body=2,x=-1}});spatial.Add(new SurvivalInterestIndex.Entry{id=3,pose=new FramePose{body=1,frame=9,x=-1}});spatial.Add(new SurvivalInterestIndex.Entry{id=4,ship=true,pose=new FramePose{body=1,x=50000}});
        var nearby=new HashSet<long>();spatial.Query(new FramePose{body=1},500,nearby);Check(nearby.SetEquals(new[]{1L}),"Interest isolates body/frame and distant objects");nearby.Clear();spatial.Query(new FramePose{body=1},100000,nearby,true);Check(nearby.SetEquals(new[]{4L}),"Coarse ship interest only");
        spatial.Clear();nearby.Clear();spatial.Query(new FramePose{body=1},100000,nearby);Check(nearby.Count==0,"Index rebuild removes stale objects");
        string folder=Path.Combine("TestResults","Multiplayer");Directory.CreateDirectory(folder);string save=Path.Combine(Path.GetFullPath(folder),"identity-"+Guid.NewGuid().ToString("N")+".xml");
        using(var authority=new IndustrialAuthority(new IndustrialWorld())){
            foreach(string name in new[]{"bad\0name","bad\nname","bad\ud800name"}){bool rejected=false;try{authority.RegisterSurvivor("bad-name",name,default,"world","instance");}catch(ArgumentException){rejected=true;}Check(rejected&&authority.World.actors.Count==0&&authority.World.multiplayer==null,"Malformed display names cannot mutate or poison the world: "+name.Length);}
        }
        long first;using(var authority=new IndustrialAuthority(new IndustrialWorld(),save)){
            authority.Save();var a=authority.RegisterSurvivor("verified-A","A",new FramePose{body=1,y=120001},"world","instance");first=a.actor;
            var b=authority.RegisterSurvivor("verified-B","B",new FramePose{body=1,y=120001},"world","instance");Check(a.actor!=b.actor,"Distinct stable actor IDs");
            Check(authority.RegisterSurvivor("verified-A","renamed",default,"world","instance").actor==first,"Reconnect reuses character");
            authority.World.multiplayer.players[0].hunger=72;authority.World.multiplayer.serverTick=123;authority.World.Actor(first).pose.x=10;authority.Step();
        }
        var restored=IndustrialAuthority.Load(save);Check(restored.multiplayer.players[0].actor==first&&restored.multiplayer.players[0].hunger==72&&restored.multiplayer.serverTick==123,"Journal recovers multiplayer state");Check(restored.Actor(first).pose.x==10,"Journal recovers server movement");
        using(var authority=new IndustrialAuthority(restored,save)){
            string error=authority.ExecuteSurvivalAction(first,10,()=>{authority.SelectInventorySlot(first,2);return null;});Check(error==null,"Network action commits through the existing journal");
            Check(authority.ExecuteSurvivalAction(first,10,()=>throw new Exception("Replay executed"))=="Stale network action","Reject duplicate before executing it");
            try{authority.ExecuteSurvivalAction(first,11,()=>{authority.SelectInventorySlot(first,3);throw new IOException("Simulated failure after durable mutation");});}catch(IOException){}
        }
        restored=IndustrialAuthority.Load(save);Check(restored.Actor(first).inventory.activeSlot==3&&restored.multiplayer.players[0].lastActionSequence==11,"Durable mutation carries its network receipt even before the final checkpoint");
        using(var authority=new IndustrialAuthority(restored,save))Check(authority.ExecuteSurvivalAction(first,11,()=>throw new Exception("Crash replay executed"))=="Stale network action","Crash/reconnect cannot repeat an applied action");
        queue.Clear();Check(!queue.Enqueue(new SurvivalInput{sequence=101,epoch=1,y=1},60,30,2),"Old walking epoch cannot feed a driver seat");Check(queue.Enqueue(new SurvivalInput{sequence=101,epoch=2,y=1,z=1,roll=1,boost=true,toggleGear=true},60,30,2),"Rejected epoch does not consume the sequence number");queue.Next();held=queue.Next();Check(!held.toggleGear,"Lost packets cannot repeat gear-toggle edges");for(int i=0;i<10;i++)held=queue.Next();Check(held.z==0&&held.roll==0&&!held.boost&&held.brake,"Stale ship controls clear thrust, roll and boost and apply brake");
        using(var authority=new IndustrialAuthority(restored,save)){
            var world=authority.World;var player=world.multiplayer.players[0];var other=world.multiplayer.players[1];var actor=world.Actor(player.actor);
            var machine=new IndustrialMachine{id=world.AllocateId(),owner=player.actor,definition="truck",pose=actor.pose};world.machines.Add(machine);authority.Save();
            Check(SurvivalVehicleAuthority.Enter(world,other,machine.id)!=null,"Another player cannot occupy an owned vehicle");
            Check(authority.ExecuteSurvivalAction(player.actor,20,()=>SurvivalVehicleAuthority.Enter(world,player,machine.id))==null,"Owned reachable vehicle grants one driver lease");
            Check(SurvivalVehicleAuthority.Enter(world,player,machine.id)!=null,"An occupied driver cannot take a second seat");
            Check(SurvivalVehicleAuthority.Exit(world,player,new FramePose{body=2})!=null,"Exits cannot cross bodies or teleport");
            var vehicle=world.multiplayer.vehicles.Find(v=>v.entity==machine.id);vehicle.pose.x+=2;vehicle.velocityX=4;authority.Step();
            Check(player.controlledEntity==machine.id&&vehicle.driver==player.actor,"Movement checkpoints retain the matching seat owner");
        }
        restored=IndustrialAuthority.Load(save);var recoveredVehicle=restored.multiplayer.vehicles[0];Check(restored.Machine(recoveredVehicle.entity).pose.Distance(recoveredVehicle.pose)<.00001&&recoveredVehicle.velocityX==4,"Journal recovery restores moving vehicle state and canonical machine position together");
        using(var authority=new IndustrialAuthority(restored,save)){
            var world=authority.World;var player=world.multiplayer.players[0];var actor=world.Actor(player.actor);var vehicle=world.multiplayer.vehicles[0];
            var exit=vehicle.pose;exit.x+=6;Check(SurvivalVehicleAuthority.Exit(world,player,exit)==null&&vehicle.driver==0&&player.controlledEntity==0,"A validated exit releases its lease and places the character");
            vehicle.pose=world.Machine(vehicle.entity).pose;actor.pose=vehicle.pose;Check(SurvivalVehicleAuthority.Enter(world,player,vehicle.entity)==null,"Driver lease can be acquired after release");SurvivalVehicleAuthority.Release(world,player);Check(vehicle.driver==0&&player.controlledEntity==0,"Disconnect release leaves no stale driver owner");
            var assembly=ShipModules.Starter(player.actor);world.ships.Add(assembly);var ship=new SurvivalShipState{entity=assembly.id,owner=player.actor,pose=actor.pose};world.multiplayer.ships.Add(ship);authority.Save();
            Check(SurvivalShipAuthority.Enter(world,world.multiplayer.players[1],ship.entity,actor.pose)!=null,"Ships reject another player's pilot claim");
            var far=actor.pose;far.x+=100;Check(SurvivalShipAuthority.Enter(world,player,ship.entity,far)!=null,"Boarding requires authoritative ramp reach");
            Check(authority.ExecuteSurvivalAction(player.actor,21,()=>SurvivalShipAuthority.Enter(world,player,ship.entity,actor.pose))==null,"A powered owned ship grants its pilot lease");
            SurvivalShipAuthority.Release(world,player);Check(ship.pilot==0&&player.controlledShip==0&&player.attachedShip==ship.entity,"Pilot disconnect releases control while preserving the occupant's ship attachment");
            ship.landed=false;Check(SurvivalShipAuthority.Enter(world,player,ship.entity,far)==null,"An existing occupant can reclaim the pilot seat in flight");
            SurvivalShipAuthority.Release(world,player);player.attachedShip=0;Check(SurvivalShipAuthority.Enter(world,player,ship.entity,actor.pose)!=null,"An on-foot actor cannot board an airborne ship");
            ship.landed=true;assembly.wires.Clear();Check(SurvivalShipAuthority.Enter(world,player,ship.entity,actor.pose)!=null,"An unpowered cockpit cannot be piloted");authority.Save();
            var guest=world.multiplayer.players[1];world.Actor(guest.actor).pose=actor.pose;
            Check(SurvivalShipAuthority.BoardPassenger(world,guest,ship.entity,actor.pose)!=null,"Uninvited players cannot board as passengers");
            Check(SurvivalShipAuthority.Invite(world,guest.actor,ship.entity,player.actor,true)!=null,"A passenger cannot grant invitations to another owner's ship");
            Check(authority.ExecuteSurvivalAction(player.actor,22,()=>SurvivalShipAuthority.Invite(world,player.actor,ship.entity,guest.actor,true))==null,"Owner invitation commits through the durable action journal");
            var active=new HashSet<long>{player.actor,guest.actor};
            Check(SurvivalShipAuthority.BoardPassenger(world,guest,ship.entity,actor.pose,active)==null&&guest.shipSeat==0&&guest.controlledShip==0,"An invited player receives a passenger berth without pilot permission");
            Check(SurvivalShipAuthority.Enter(world,guest,ship.entity,actor.pose)!=null,"An invited passenger still cannot take the pilot seat");
            Check(authority.ExecuteSurvivalAction(player.actor,23,()=>SurvivalShipAuthority.Invite(world,player.actor,ship.entity,guest.actor,false))==null&&guest.attachedShip==ship.entity,"Revocation does not eject an existing passenger");
            guest.attachedShip=0;guest.shipSeat=-1;Check(SurvivalShipAuthority.BoardPassenger(world,guest,ship.entity,actor.pose,active)!=null,"Revocation prevents boarding again");
            SurvivalShipAuthority.Invite(world,player.actor,ship.entity,guest.actor,true);
            var seats=new HashSet<string>();for(int i=0;i<SurvivalShipAuthority.PassengerCapacity;i++){var crew=world.Register(100+i,actor.pose);var p=new SurvivalPlayerRecord{actor=crew.id,identity="crew-"+i,attachedShip=ship.entity,shipSeat=i};world.multiplayer.players.Add(p);active.Add(p.actor);seats.Add(SurvivalShipAuthority.SeatFeet(assembly,i).x+"/"+SurvivalShipAuthority.SeatFeet(assembly,i).z);}
            Check(seats.Count==8,"Passenger berths have distinct canonical local positions");Check(SurvivalShipAuthority.BoardPassenger(world,guest,ship.entity,actor.pose,active)!=null,"A full ship cannot allocate a ninth active passenger berth");
            active.Remove(100);Check(SurvivalShipAuthority.BoardPassenger(world,guest,ship.entity,actor.pose,active)==null&&guest.shipSeat==0,"A disconnected passenger releases the active berth for reuse");
            var privateCopy=ship.Copy();privateCopy.invitedActors.Clear();Check(ship.invitedActors.Contains(guest.actor),"Redacting public ship state cannot mutate owner invitations");
            Check(SurvivalShipAuthority.CollisionRadius(assembly)>=55,"Hull envelopes cover the existing capital ship");authority.Save();
        }
        restored=IndustrialAuthority.Load(save);Check(restored.multiplayer.ships[0].invitedActors.Contains(restored.multiplayer.players[1].actor)&&restored.multiplayer.players[1].shipSeat==0,"Invitation and passenger position survive server restart");
        ShipRespawnChecks.Run(Check,folder);
        string respawnSave=Path.Combine(Path.GetFullPath(folder),"respawn-"+Guid.NewGuid().ToString("N")+".xml");long respawnActor,bagId;
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),respawnSave)){
            var player=authority.RegisterSurvivor("respawn-owner","Owner",new FramePose{body=1,y=10000},"respawn-world","instance");respawnActor=player.actor;var actor=authority.World.Actor(player.actor);actor.inventory.Add("sleeping-bag",1);authority.Save();
            actor.inventory.activeSlot=actor.inventory.items.Find(s=>s.item=="sleeping-bag").slot;var pose=actor.pose;pose.x+=4;
            Check(authority.ExecuteSurvivalAction(player.actor,1,()=>authority.PlaceRespawnPoint(player.actor,"sleeping-bag",pose,pose))==null,"Network respawn placement uses the existing durable transaction");bagId=authority.World.respawnPoints[0].id;
            Check(actor.inventory.Count("sleeping-bag")==0&&authority.ExecuteSurvivalAction(player.actor,1,()=>throw new Exception("Placement replayed"))=="Stale network action","Replayed placement cannot consume or duplicate a bag");
            Check(authority.UseRespawnPoint(player.actor,bagId,pose)!=null,"A living multiplayer character cannot use respawn as teleportation");player.hunger=player.thirst=player.oxygen=0;authority.RecordPlayerDeath(player.actor);authority.Save();
            Check(authority.ExecuteSurvivalAction(player.actor,2,()=>authority.UseRespawnPoint(player.actor,bagId,pose))!=null,"Death cannot bypass a newly placed point's cooldown");
            for(int batch=0;batch<12;batch++)authority.Step(100);
            Check(authority.ExecuteSurvivalAction(player.actor,3,()=>authority.UseRespawnPoint(player.actor,bagId,pose))==null,"Ready multiplayer respawn commits with its action receipt");
            Check(authority.World.respawnPoints[0].readyAtTick==authority.World.tick+1200,"Multiplayer respawn restarts only that point's cooldown");
        }
        string[] respawnLines=File.ReadAllLines(respawnSave+".journal");Check(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(respawnLines[respawnLines.Length-1])).Contains("<survivalCheckpoint>true</survivalCheckpoint>"),"Crash fixture removes only the final network checkpoint");File.WriteAllLines(respawnSave+".journal",respawnLines[..^1]);
        var respawnRestored=IndustrialAuthority.Load(respawnSave);Check(respawnRestored.Actor(respawnActor).health==100&&respawnRestored.multiplayer.players[0].lastActionSequence==3&&respawnRestored.RespawnPoint(bagId).readyAtTick>respawnRestored.tick,"Restart recovers a respawn, cooldown and matching receipt together");
        var revived=respawnRestored.multiplayer.players[0];Check(revived.hunger==100&&revived.thirst==100&&revived.oxygen==100&&revived.attachedShip==0&&revived.controlledShip==0&&revived.controlledEntity==0,"Revival vitals and control release recover even when the following network checkpoint was lost");
        using(var authority=new IndustrialAuthority(respawnRestored,respawnSave))Check(authority.ExecuteSurvivalAction(respawnActor,3,()=>throw new Exception("Respawn replayed"))=="Stale network action","Reconnect cannot replay a completed revival");
        string deathSave=Path.Combine(Path.GetFullPath(folder),"death-"+Guid.NewGuid().ToString("N")+".xml");long deadActor,deathContainer;
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),deathSave)){
            var player=authority.RegisterSurvivor("death-owner","Owner",new FramePose{body=1,y=10000},"death-world","instance");deadActor=player.actor;var actor=authority.World.Actor(deadActor);actor.inventory.Add("copper-bar",3,9);actor.inventory.bulk=new MaterialLot{litres=7,iron=2,radioactive=1,activity=17};
            var craft=authority.Execute(deadActor,new IndustrialCommand{sequence=actor.lastSequence+1,kind="craft",item="wire",count=2});Check(craft.accepted&&actor.craft!=null&&actor.craftQueue.Count==1,"Death fixture reserves two paid crafts");authority.Save();actor.health=0;
            var local=new FramePose{body=1,frame=98765,x=2,y=-4,z=20};deathContainer=authority.DropSurvivorInventory(deadActor,local,actor.pose);var drop=authority.World.Dropped(deathContainer);
            Check(deathContainer>0&&actor.inventory.Used==0&&actor.inventory.activeSlot==-1,"Death moves the carried backpack and hotbar into one container");
            Check(actor.craft==null&&actor.craftQueue.Count==0&&actor.inventory.reserved==0&&actor.inventory.slotReservations.Count==0,"Death clears paid crafting jobs and their reservations together");
            Check(drop.inventory.Count("copper-bar")==3&&drop.inventory.Count("wire")==0&&Math.Abs(drop.inventory.ActivityOf("copper-bar",3)-9)<1e-8,"Cancelled crafting returns exact inputs and activity without creating outputs");
            Check(drop.inventory.bulk.litres==7&&drop.inventory.bulk.iron==2&&drop.inventory.bulk.activity==17,"Death conserves bulk composition and radioactivity");
            Check(drop.pose.frame==98765&&drop.pose.z==20&&drop.resolvedPose.Distance(actor.pose)==0,"Ship deaths preserve stable parent identity and canonical berth coordinates");
            Check(authority.DropSurvivorInventory(deadActor,local,actor.pose)==0&&authority.World.droppedItems.Count==1,"Repeated death processing cannot duplicate its container");
        }
        var deathRestored=IndustrialAuthority.Load(deathSave);Check(deathRestored.Actor(deadActor).health==0&&deathRestored.Actor(deadActor).inventory.Used==0&&deathRestored.Dropped(deathContainer).inventory.Count("copper-bar")==3,"Crash recovery restores the death transfer atomically");
        using(var authority=new IndustrialAuthority(deathRestored,deathSave)){
            var actor=deathRestored.Actor(deadActor);var drop=deathRestored.Dropped(deathContainer);var resolvedLoot=actor.pose;deathRestored.InventoryPoseResolver=p=>resolvedLoot;
            Check(authority.MoveInventory(deadActor,new InventoryAddress{section="ground",entity=deathContainer,slot=drop.inventory.items.Find(s=>s.item=="copper-bar").slot},new InventoryAddress{slot=0},1)!=null,"A dead character cannot loot its own container");
            Check(authority.UseRespawnPoint(deadActor,0,actor.pose)==null,"A dead character can respawn after losing its carried items");var stack=drop.inventory.items.Find(s=>s.item=="copper-bar");
            Check(authority.MoveInventory(deadActor,new InventoryAddress{section="ground",entity=deathContainer,slot=stack.slot},new InventoryAddress{slot=0},stack.count)==null&&actor.inventory.Count("copper-bar")==3&&drop.inventory.Count("copper-bar")==0,"A living character recovers a stack exactly once through the existing inventory gateway");
            actor.pose.x+=20;Check(authority.MoveInventory(deadActor,new InventoryAddress{section="ground",entity=deathContainer,slot=-2},new InventoryAddress{slot=-2},7)!=null,"Moving out of reach prevents recovering the remaining bulk");resolvedLoot=actor.pose;
            Check(authority.MoveInventory(deadActor,new InventoryAddress{section="ground",entity=deathContainer,slot=-2},new InventoryAddress{slot=-2},7)==null,"Ship-local loot resolves through the server's updated parent transform");
        }
        string joinSave=Path.Combine(Path.GetFullPath(folder),"joins-"+Guid.NewGuid().ToString("N")+".xml");
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),joinSave)){
            authority.Save();byte[] original=File.ReadAllBytes(joinSave);
            using(var checkpointReader=new FileStream(joinSave,FileMode.Open,FileAccess.Read,FileShare.Read)){
                for(int i=0;i<8;i++)authority.RegisterSurvivor("burst-"+i,"Player "+i,new FramePose{body=1,x=i,y=10000},"burst-world","burst-instance");
                Check(Convert.ToBase64String(File.ReadAllBytes(joinSave))==Convert.ToBase64String(original),"Eight registrations do not replace a checkpoint held by a reader");
            }
            long serial=authority.World.journalSequence;
            Check(authority.RegisterSurvivor("burst-3","Ignored",default,"burst-world","burst-instance").actor==4&&authority.World.journalSequence==serial,"Reconnect does not append another registration or duplicate starter items");
        }
        var joinRestored=IndustrialAuthority.Load(joinSave);
        Check(joinRestored.multiplayer.players.Count==8&&joinRestored.actors.Count==8&&joinRestored.multiplayer.worldId=="burst-world","Crash replay restores every identity from a pre-multiplayer checkpoint");
        foreach(var actor in joinRestored.actors)Check(actor.inventory.slotLimit==40&&actor.personalInventoryImported&&actor.inventory.Count("ration")==3&&actor.inventory.Count("repair-tool")==1&&actor.inventory.Count("stone")==64,"Journalled registration restores starter inventory and slot policy exactly once");
        using(var authority=new IndustrialAuthority(joinRestored,joinSave))authority.Save();
        Check(IndustrialAuthority.Load(joinSave).actors.Count==8,"Compacting registration journals preserves character identities");
        string timingSave=Path.Combine(Path.GetFullPath(folder),"timing-"+Guid.NewGuid().ToString("N")+".xml");int timedJournals=0,timedCheckpoints=0;bool finiteTimings=true;
        using(var authority=new IndustrialAuthority(new IndustrialWorld(),timingSave)){
            authority.JournalTiming=(serialize,flush)=>{timedJournals++;finiteTimings&=serialize>=0&&flush>=0&&!double.IsNaN(serialize)&&!double.IsNaN(flush);};authority.CheckpointTiming=milliseconds=>{timedCheckpoints++;finiteTimings&=milliseconds>=0&&!double.IsNaN(milliseconds);};
            authority.RegisterSurvivor("timed-1","Timed",new FramePose{body=1,y=10000},"timing","instance");authority.Save();
            Check(timedJournals>0&&timedCheckpoints>0&&finiteTimings,"Optional persistence instrumentation measures successful journal and checkpoint operations");
            authority.JournalTiming=(a,b)=>throw new Exception("Broken diagnostic listener");authority.CheckpointTiming=a=>throw new Exception("Broken diagnostic listener");
            authority.RegisterSurvivor("timed-2","Timed",new FramePose{body=1,y=10000},"timing","instance");Check(authority.World.multiplayer.players.Count==2,"A diagnostic listener cannot reject a flushed registration");authority.Save();
        }
        Check(IndustrialAuthority.Load(timingSave).multiplayer.players.Count==2,"Diagnostic failures do not prevent a durable checkpoint or journal recovery");
        var absentWorld=new IndustrialWorld();var absent=absentWorld.Register(1,new FramePose{body=1,y=10000});absent.inventory.bulk.activity=100;absent.networkAbsent=true;absentWorld.Step();Check(absent.dose==0&&absent.health==100,"Disconnected characters are absent from environmental damage while independent world work advances");absent.networkAbsent=false;absentWorld.Step();Check(absent.dose>0,"Reconnecting restores environmental simulation for the same character");
        checks+=FactoryChecks.Run(folder);
        checks+=ChunkChecks.Run();
        var process=Process.GetCurrentProcess();var report=new{status="PASS",checks,scope="Production domain checks; not Unity/cloud/standalone or capacity acceptance",memory=new{process.WorkingSet64,process.PrivateMemorySize64,managed=GC.GetTotalMemory(false)}};
        File.WriteAllText(Path.Combine(folder,"domain-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
