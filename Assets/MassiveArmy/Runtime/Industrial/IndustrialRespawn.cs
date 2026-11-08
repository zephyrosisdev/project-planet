using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    [Serializable]public sealed class RespawnPoint {
        public long id,owner,readyAtTick;public string definition,name;public FramePose pose;public double activity;
        public double RemainingSeconds(long tick)=>Math.Max(0,readyAtTick-tick)*.1;
    }
    [Serializable]public sealed class RespawnTransaction {
        public string kind,name;public bool shipRespawn;public long attachedShip;public FramePose interiorPose;public long actor,id,readyAtTick;public RespawnPoint point;public FramePose resolvedPose;public double activity;
    }
    public sealed partial class IndustrialWorld {
        public const long RespawnCooldownTicks=1200;public const int MaximumRespawnPointsPerPlayer=128;
        public List<RespawnPoint> respawnPoints=new List<RespawnPoint>();
        public static bool RespawnItem(string id)=>id=="sleeping-bag"||id=="sleeping-pod";
        public RespawnPoint RespawnPoint(long id)=>respawnPoints.Find(p=>p.id==id);
        internal string PrepareRespawnPlacement(long actorId,string definition,FramePose pose,FramePose resolved,out RespawnTransaction change){
            change=null;var actor=Actor(actorId);if(actor==null||actor.health<=0)return "You must be alive to place a respawn point";
            if(!RespawnItem(definition)||!Valid(pose)||!Valid(resolved)||resolved.frame!=0||pose.frame<0||!Reach(actor,resolved))return "Choose a supported floor within 12 m";
            if(pose.frame!=0&&(multiplayer==null?pose.frame!=1:!multiplayer.ships.Exists(ship=>ship.entity==pose.frame&&ship.owner==actorId&&ship.pose.body==pose.body)))return "Use an owned ship reference frame";
            if(pose.frame==0&&pose.Distance(resolved)>.001)return "Ground placement does not match its canonical pose";
            int owned=0;foreach(var point in respawnPoints){if(point.owner==actorId)owned++;if(point.pose.frame==pose.frame&&point.pose.body==pose.body){double x=point.pose.x-pose.x,y=point.pose.y-pose.y,z=point.pose.z-pose.z;if(x*x+y*y+z*z<9)return "Leave 3 m between respawn points";}}
            if(owned>=MaximumRespawnPointsPerPlayer)return "Respawn point limit reached (128 per player)";
            if(inventoryWorkflow&&actor.inventory.AtSlot(actor.inventory.activeSlot)?.item!=definition)return "Equip the crafted respawn point in a hotbar slot";if((!creativeMode||inventoryWorkflow)&&actor.inventory.Count(definition)<1)return "Craft a "+definition.Replace('-',' ')+" first";
            change=new RespawnTransaction{kind="place",actor=actorId,id=nextId,activity=creativeMode&&!inventoryWorkflow?0:actor.inventory.ActivityOf(definition,1),point=new RespawnPoint{id=nextId,owner=actorId,definition=definition,name=(definition=="sleeping-bag"?"Sleeping bag":"Sleeping pod")+" #"+nextId,pose=pose,readyAtTick=tick+RespawnCooldownTicks}};return null;
        }
        internal string PrepareRespawnUse(long actorId,long id,FramePose resolved,out RespawnTransaction change){
            change=null;var actor=Actor(actorId);if(actor==null||actor.health>0)return "Respawning is only available after death";
            if(!Valid(resolved)||resolved.frame!=0)return "Respawn destination is unavailable";
            var point=RespawnPoint(id);if(id!=0&&(point==null||point.owner!=actorId))return "Respawn point is no longer yours";
            if(point!=null&&point.readyAtTick>tick)return "Respawn point is cooling down";
            change=new RespawnTransaction{kind="use",actor=actorId,id=id,resolvedPose=resolved,readyAtTick=tick+RespawnCooldownTicks};return null;
        }
        internal void ApplyRespawn(RespawnTransaction change){
            var actor=Actor(change.actor)??throw new InvalidOperationException("Respawn actor missing");
            if(change.kind=="place"){
                if(nextId!=change.id||RespawnPoint(change.id)!=null)throw new InvalidOperationException("Respawn identity mismatch");
            if(!creativeMode||inventoryWorkflow)actor.inventory.Extract(change.point.definition,1);change.point.activity=change.activity;AllocateId();respawnPoints.Add(change.point);
            }else if(change.kind=="use"){
                if(change.id!=0)(RespawnPoint(change.id)??throw new InvalidOperationException("Respawn point missing")).readyAtTick=change.readyAtTick;
                actor.health=100;actor.dose=0;actor.exposure=0;actor.pose=change.resolvedPose;
                // This is part of the journalled revival itself, including recovery
                // from a crash before the network adapter's following checkpoint.
                var survivor=multiplayer?.players.Find(p=>p.actor==actor.id);
                if(survivor!=null){SurvivalVehicleAuthority.Release(this,survivor);SurvivalShipAuthority.Release(this,survivor);survivor.attachedShip=survivor.pendingVehicleExit=0;survivor.interiorWalking=false;survivor.shipSeat=-1;survivor.oxygen=survivor.hunger=survivor.thirst=100;survivor.walkVelocityX=survivor.walkVelocityY=survivor.walkVelocityZ=0;survivor.verticalSpeed=0;survivor.pitch=0;if(change.shipRespawn){survivor.attachedShip=change.attachedShip;survivor.interiorPose=change.interiorPose;survivor.interiorWalking=true;}}
            }else if(change.kind=="death"){actor.health=0;}
            else if(change.kind=="rename"){RespawnPoint(change.id).name=change.name;}
            else if(change.kind=="pack"){
                var point=RespawnPoint(change.id)??throw new InvalidOperationException("Respawn point missing");actor.inventory.Add(point.definition,1,change.activity);respawnPoints.Remove(point);
            }
        }
    }
    public sealed partial class IndustrialAuthority {
        // Placement and clear-exit validation belong to the authoritative Unity adapter.
        // These typed transactions are deliberately absent from the raw client command gateway.
        internal string PlaceRespawnPoint(long actor,string definition,FramePose canonical,FramePose resolved){lock(gate){string error=World.PrepareRespawnPlacement(actor,definition,canonical,resolved,out var change);if(error!=null)return error;EnsureRespawnSnapshot();Append(new IndustrialJournalEntry{respawn=change});World.ApplyRespawn(change);return null;}}
        internal string UseRespawnPoint(long actor,long id,FramePose resolved){lock(gate){string error=World.PrepareRespawnUse(actor,id,resolved,out var change);if(error!=null)return error;EnsureRespawnSnapshot();Append(new IndustrialJournalEntry{respawn=change});World.ApplyRespawn(change);return null;}}
        internal string UseShipRespawnPoint(long actor,long id,long ship,FramePose local,FramePose resolved){lock(gate){var point=World.RespawnPoint(id);var parent=World.multiplayer?.ships.Find(p=>p.entity==ship&&p.owner==actor);if(point==null||point.owner!=actor||point.pose.frame!=ship||parent==null||local.frame!=ship||local.body!=parent.pose.body)return "Ship respawn point unavailable";string error=World.PrepareRespawnUse(actor,id,resolved,out var change);if(error!=null)return error;change.shipRespawn=true;change.attachedShip=ship;change.interiorPose=local;EnsureRespawnSnapshot();Append(new IndustrialJournalEntry{respawn=change});World.ApplyRespawn(change);return null;}}
        internal void RecordPlayerDeath(long actor){lock(gate){if(World.Actor(actor)==null)return;EnsureRespawnSnapshot();var change=new RespawnTransaction{kind="death",actor=actor};Append(new IndustrialJournalEntry{respawn=change});World.ApplyRespawn(change);}}
        internal string ManageRespawnPoint(long actorId,long id,FramePose resolved,string name=null){lock(gate){var actor=World.Actor(actorId);var point=World.RespawnPoint(id);if(actor==null||actor.health<=0||point==null||point.owner!=actorId||actor.pose.Distance(resolved)>12)return "Move within reach of your respawn point";if(name==null&&!actor.inventory.CanAdd(point.definition,1))return "Make inventory space before picking this up";if(name!=null){name=name.Trim();if(name.Length==0||name.Length>48)return "Choose a name between 1 and 48 characters";foreach(char letter in name)if(char.IsControl(letter))return "Names cannot contain control characters";}var change=new RespawnTransaction{kind=name==null?"pack":"rename",actor=actorId,id=id,name=name,activity=point.activity};EnsureRespawnSnapshot();Append(new IndustrialJournalEntry{respawn=change});World.ApplyRespawn(change);return null;}}
        void EnsureRespawnSnapshot(){if(path!=null&&!System.IO.File.Exists(path))Save();}
    }
    public static partial class IndustrialCatalog {
        static void RespawnRecipes(){
            Items.Add("sleeping-bag",new ItemDefinition("sleeping-bag"));Items.Add("sleeping-pod",new ItemDefinition("sleeping-pod"));
            // Starter survival fabric is abstracted into the current metal/resource economy.
            R("sleeping-bag",50,0,0,new[]{S("stone",12),S("iron-bar",2)},new[]{S("sleeping-bag",1)});
            R("sleeping-pod",120,0,0,new[]{S("plate",8),S("wire",4),S("electronics",2)},new[]{S("sleeping-pod",1)});
            Handcraft.Add("sleeping-bag");Handcraft.Add("sleeping-pod");
        }
    }
}


