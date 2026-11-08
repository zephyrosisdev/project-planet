using System;
namespace MassiveArmy.Industrial {
    public sealed partial class IndustrialWorld {
        internal void ApplySurvivalDeathDrop(InventoryTransaction change){
            var actor=Actor(change.actor)??throw new InvalidOperationException("Death actor missing");
            if(nextId!=change.id||Dropped(change.id)!=null)throw new InvalidOperationException("Death container identity mismatch");
            var carried=actor.inventory;var loot=carried.CloneSlots();loot.slotLimit=-1;loot.activeSlot=-1;loot.reserved=0;loot.slotReservations.Clear();
            long refund=0;void Count(IndustrialCraftJob job){if(job!=null)foreach(var input in job.inputs)refund=checked(refund+input.count);}
            Count(actor.craft);foreach(var job in actor.craftQueue)Count(job);loot.capacity=Math.Max(loot.capacity,checked(loot.Used+refund));
            void Refund(IndustrialCraftJob job){if(job!=null)foreach(var input in job.inputs)if(!loot.Add(input.item,input.count,input.activity))throw new InvalidOperationException("Death crafting refund lost capacity");}
            Refund(actor.craft);foreach(var job in actor.craftQueue)Refund(job);
            actor.inventory=new Inventory{capacity=carried.capacity,slotLimit=carried.slotLimit,activeSlot=-1,revision=carried.revision+1};actor.craft=null;actor.craftQueue.Clear();actor.health=0;actor.wearingProtection=false;actor.protection=0;
            droppedItems.Add(new DroppedInventory{id=AllocateId(),pose=change.pose,resolvedPose=change.resolvedPose,inventory=loot});
        }
    }
    public sealed partial class IndustrialAuthority {
        // Called only by the authoritative simulation. Clients cannot select death contents or position.
        internal long DropSurvivorInventory(long actorId,FramePose canonical,FramePose resolved){lock(gate){
            var actor=World.Actor(actorId);if(actor==null||actor.health>0)return 0;
            if(actor.inventory.Used==0&&actor.craft==null&&actor.craftQueue.Count==0)return 0;
            if(canonical.body!=actor.pose.body||canonical.frame<0||resolved.frame!=0||!SurvivalServerConfig.Finite(canonical.x)||!SurvivalServerConfig.Finite(canonical.y)||!SurvivalServerConfig.Finite(canonical.z)||!SurvivalServerConfig.Finite(resolved.x)||!SurvivalServerConfig.Finite(resolved.y)||!SurvivalServerConfig.Finite(resolved.z)||actor.pose.Distance(resolved)>.01)throw new ArgumentException("Invalid authoritative death position");
            EnsureRespawnSnapshot();var change=new InventoryTransaction{kind="survival-death",actor=actorId,id=World.nextId,pose=canonical,resolvedPose=resolved};Append(new IndustrialJournalEntry{inventory=change});World.ApplySurvivalDeathDrop(change);return change.id;
        }}
    }
}
