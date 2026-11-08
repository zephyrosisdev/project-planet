using System;
using System.Collections.Generic;
using System.Xml.Serialization;
namespace MassiveArmy.Industrial {
    [Serializable]public sealed class InventoryAddress {public long entity;public string section="player";public int slot;}
    [Serializable]public sealed class DroppedInventory {public long id;public FramePose pose,resolvedPose;public Inventory inventory=new Inventory{capacity=100000,slotLimit=-1};}
    [Serializable]public sealed class InventoryDropPosition {public long id;public FramePose pose;}
    [Serializable]public sealed class InventoryTransaction {public string kind,item,name;public long externalVehicle;public long actor,id,count;public int slot;public InventoryAddress from,to;public FramePose pose,resolvedPose;public double activity;public List<ItemStack> imports;}
    public sealed partial class IndustrialActor {public bool personalInventoryImported;}
    public sealed partial class IndustrialWorld {
        public bool inventoryWorkflow;public List<DroppedInventory> droppedItems=new List<DroppedInventory>();
        [NonSerialized,XmlIgnore]public Func<FramePose,FramePose?> InventoryPoseResolver;
        public DroppedInventory Dropped(long id)=>droppedItems.Find(d=>d.id==id);
        internal List<InventoryDropPosition> CaptureDropPositions(){List<InventoryDropPosition> result=null;foreach(var drop in droppedItems)if(drop.pose.frame!=0){var pose=InventoryPoseResolver?.Invoke(drop.pose);if(pose.HasValue){drop.resolvedPose=pose.Value;result??=new List<InventoryDropPosition>();result.Add(new InventoryDropPosition{id=drop.id,pose=pose.Value});}}return result;}
        public Inventory InventoryAt(long actor,InventoryAddress address,bool write=false){
            var who=Actor(actor);if(who==null||who.health<=0||address==null)return null;
            if(address.section=="player")return address.entity==0?who.inventory:null;
            if(address.section=="ground"){var drop=Dropped(address.entity);if(drop==null)return null;var resolved=drop.pose.frame==0?(FramePose?)drop.pose:InventoryPoseResolver?.Invoke(drop.pose);return resolved.HasValue&&Reach(who,resolved.Value)?drop.inventory:null;}
            var machine=Machine(address.entity);if(!Own(who,machine))return null;if(address.section=="input")return machine.input;if(address.section=="output")return write?null:machine.Export;return null;
        }
        internal void EnableInventoryWorkflow(){if(inventoryWorkflow)return;inventoryWorkflow=true;foreach(var actor in actors){actor.inventory.EnableSlots(40);if(actor.craft!=null)actor.inventory.ReserveSlots(actor.craft.outputs);foreach(var craft in actor.craftQueue)actor.inventory.ReserveSlots(craft.outputs);}foreach(var machine in machines){machine.input.EnableSlots(-1);machine.output.EnableSlots(-1);}}
        internal string PrepareInventoryMove(long actorId,InventoryAddress from,InventoryAddress to,long count,out InventoryTransaction change){
            change=null;var source=InventoryAt(actorId,from);if(source==null||to==null)return "Move within reach of the inventory";
            if(to.section=="reservoir"){var machine=Machine(to.entity);var water=source.AtSlot(from.slot);if(!Own(Actor(actorId),machine)||(machine.definition!="sluice"&&machine.definition!="water-reservoir")||water?.item!="water"||water.activity>0||count<=0||count>water.count||machine.water>=10000)return "This buffer needs clean water and available capacity";change=new InventoryTransaction{kind="fill-reservoir",actor=actorId,from=from,to=to,count=Math.Min(count,10000-machine.water)};return null;}
            var destination=InventoryAt(actorId,to);if(destination==null)return "Move within reach of the inventory";
            if(from.slot==-2||to.slot==-2){if(from.slot!=-2||to.slot!=-2||ReferenceEquals(source,destination)||count<=0||source.bulk.litres<count||!destination.CanFit(count)||to.section=="output")return "Choose a compatible bulk-material slot with space";var bulkTarget=to.section=="input"?Machine(to.entity):null;if(bulkTarget!=null&&!bulkTarget.Definition.storage&&bulkTarget.definition!="sluice")return "This machine does not accept pay dirt";if(from.section=="input"&&Machine(from.entity)?.haul?.active==true||bulkTarget?.haul?.active==true)return "Stop autonomous collection before moving payload";change=new InventoryTransaction{kind="bulk-move",actor=actorId,from=from,to=to,count=count};return null;}
            if(source.slotLimit==0)source.EnableSlots(-1);if(destination.slotLimit==0)destination.EnableSlots(-1);
            var stack=source.AtSlot(from.slot);if(stack==null||count<=0||count>stack.count)return "The source stack changed";
            bool same=ReferenceEquals(source,destination);if(same&&from.slot==to.slot)return "Choose a different slot";
            if(!same&&to.section=="output")return "Machine output only allows taking items";
            if(to.slot<0||destination.slotLimit>0&&to.slot>=destination.slotLimit)return "Destination slot is unavailable";
            var target=destination.AtSlot(to.slot);bool swap=target!=null&&target.item!=stack.item;
            if(!swap)count=Math.Min(count,Inventory.StackLimit(stack.item)-(target?.count??0));if(count<=0)return "Destination stack is full";
            if(swap&&(count!=stack.count||!same&&from.section=="output"))return "Only complete writable stacks can swap";
            var src=source.CloneSlots();var dst=same?src:destination.CloneSlots();double activity=src.ExtractAt(from.slot,count);
            if(swap){double back=dst.ExtractAt(to.slot,target.count);if(!src.CanAddAt(from.slot,target.item,target.count))return "Source cannot accept the swapped stack";src.AddAt(from.slot,target.item,target.count,back);}
            if(!dst.CanAddAt(to.slot,stack.item,count))return "Not enough stack space or inventory capacity";
            change=new InventoryTransaction{kind=swap?"swap":"move",actor=actorId,from=from,to=to,count=count};return null;
        }
        internal void ApplyInventory(InventoryTransaction c){
            if(c.kind=="survival-death"){ApplySurvivalDeathDrop(c);return;}
            var actor=Actor(c.actor)??throw new InvalidOperationException("Inventory actor missing");
            Inventory Resolve(InventoryAddress address){if(address.section=="player")return actor.inventory;if(address.section=="ground")return Dropped(address.entity).inventory;var machine=Machine(address.entity);var result=address.section=="input"?machine.input:machine.Export;if(result.slotLimit==0)result.EnableSlots(-1);return result;}
            if(c.kind=="vehicle-store"){if(nextId!=c.id)throw new InvalidOperationException("Vehicle inventory identity mismatch");machines.Add(new IndustrialMachine{id=AllocateId(),owner=c.actor,externalVehicle=c.externalVehicle,displayName=c.name,definition="store",pose=c.pose,integratedLogistics=true,resourceConstruction=true,input=new Inventory{capacity=2000,slotLimit=-1},output=new Inventory{capacity=2000,slotLimit=-1}});return;}
            if(c.kind=="enable"){EnableInventoryWorkflow();return;}
            if(c.kind=="import"){
                foreach(var item in c.imports){IndustrialCatalog.RegisterPersonalItem(item.item);if(!actor.inventory.Add(item.item,item.count,item.activity)){
                    // Old saves can exceed the new slot budget. Preserve every item in visible overflow.
                    int limit=actor.inventory.slotLimit;actor.inventory.slotLimit=-1;long capacity=actor.inventory.capacity;actor.inventory.capacity=Math.Max(capacity,actor.inventory.Used+actor.inventory.reserved+item.count);actor.inventory.Add(item.item,item.count,item.activity);actor.inventory.capacity=capacity;actor.inventory.slotLimit=limit;
                }}actor.personalInventoryImported=true;return;
            }
            if(c.kind=="equip"){actor.inventory.activeSlot=c.slot;actor.inventory.revision++;return;}
            if(c.kind=="add"){IndustrialCatalog.RegisterPersonalItem(c.item);if(!actor.inventory.Add(c.item,c.count,c.activity))throw new InvalidOperationException("Inventory addition lost capacity");return;}
            if(c.kind=="consume"){actor.inventory.Extract(c.item,c.count);return;}
            if(c.kind=="fill-reservoir"){Resolve(c.from).ExtractAt(c.from.slot,c.count);Machine(c.to.entity).water+=c.count;return;}
            if(c.kind=="layout"){Resolve(c.from).EnableSlots(c.slot);return;}
            if(c.kind=="bulk-move"){if(!Inventory.TransferBulk(Resolve(c.from),Resolve(c.to),c.count))throw new InvalidOperationException("Bulk transfer changed");if(c.from.section=="ground"&&Resolve(c.from).Used==0)droppedItems.Remove(Dropped(c.from.entity));return;}
            if(c.kind=="drop"||c.kind=="drop-bulk"){
                var source=Resolve(c.from);var drop=new DroppedInventory{id=AllocateId(),pose=c.pose,resolvedPose=c.resolvedPose};drop.inventory.capacity=Math.Max(drop.inventory.capacity,c.count);if(c.kind=="drop-bulk")drop.inventory.bulk=source.bulk.Take(c.count);else{var stack=source.AtSlot(c.from.slot);string item=stack.item;double activity=source.ExtractAt(c.from.slot,c.count);drop.inventory.Add(item,c.count,activity);}droppedItems.Add(drop);return;
            }
            var from=Resolve(c.from);var to=Resolve(c.to);var outgoing=from.AtSlot(c.from.slot);string key=outgoing.item;var incoming=c.kind=="swap"?to.AtSlot(c.to.slot):null;long backCount=incoming?.count??0;string backKey=incoming?.item;double moved=from.ExtractAt(c.from.slot,c.count);if(incoming!=null){double back=to.ExtractAt(c.to.slot,backCount);from.AddAt(c.from.slot,backKey,backCount,back);}to.AddAt(c.to.slot,key,c.count,moved);
            if(c.from.section=="ground"&&from.Used==0)droppedItems.Remove(Dropped(c.from.entity));
        }
    }
    public sealed partial class IndustrialAuthority {
        public Inventory EnsureInventoryLayout(long actor,InventoryAddress address){lock(gate){var inventory=World.InventoryAt(actor,address);if(inventory?.slotLimit==0){var change=new InventoryTransaction{kind="layout",actor=actor,from=address,slot=address.section=="player"?40:-1};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);}return inventory;}}
        internal long EnsureVehicleInventory(long actor,long entity,FramePose pose,string name){lock(gate){foreach(var m in World.machines)if(m.externalVehicle==entity&&m.pose.body==pose.body&&m.owner==actor)return m.id;var change=new InventoryTransaction{kind="vehicle-store",actor=actor,id=World.nextId,externalVehicle=entity,pose=pose,name=name};EnsureRespawnSnapshot();Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);return change.id;}}
        public void EnableInventories(){lock(gate){if(World.inventoryWorkflow)return;EnsureRespawnSnapshot();var change=new InventoryTransaction{kind="enable",actor=1};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);}}
        public void ImportPersonalInventory(long actor,List<ItemStack> items){lock(gate){if(World.Actor(actor).personalInventoryImported)return;EnsureRespawnSnapshot();var change=new InventoryTransaction{kind="import",actor=actor,imports=items};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);}}
        public bool ChangePersonalItem(long actor,string item,long count,bool add){lock(gate){var who=World.Actor(actor);if(who==null||who.health<=0||count<=0||add&&!who.inventory.CanAdd(item,count)||!add&&who.inventory.Count(item)<count)return false;var change=new InventoryTransaction{kind=add?"add":"consume",actor=actor,item=item,count=count,activity=add&&IndustrialCatalog.Items.TryGetValue(item,out var definition)?definition.activity*count:0};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);return true;}}
        public void SelectInventorySlot(long actor,int slot){lock(gate){var who=World.Actor(actor);if(who==null||slot<-1||slot>=10||who.inventory.activeSlot==slot)return;var change=new InventoryTransaction{kind="equip",actor=actor,slot=slot};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);}}
        public string MoveInventory(long actor,InventoryAddress from,InventoryAddress to,long count,string expected=null){lock(gate){if(expected!=null&&from.slot!=-2&&World.InventoryAt(actor,from)?.AtSlot(from.slot)?.item!=expected)return "Source stack changed during drag";var error=World.PrepareInventoryMove(actor,from,to,count,out var change);if(error!=null)return error;Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);return null;}}
        internal string DropInventory(long actor,InventoryAddress from,long count,FramePose canonical,FramePose resolved,string expected=null){lock(gate){if(expected!=null&&from.slot!=-2&&World.InventoryAt(actor,from)?.AtSlot(from.slot)?.item!=expected)return "Source stack changed during drag";var who=World.Actor(actor);var source=World.InventoryAt(actor,from);if(source==null||(from.slot==-2?source.bulk.litres<count:source.AtSlot(from.slot)==null||source.AtSlot(from.slot).count<count)||count<=0||canonical.frame<0||canonical.frame>1||resolved.frame!=0||resolved.Distance(resolved)!=0||who.pose.Distance(resolved)>4)return "Drop position or source stack is unavailable";if(World.droppedItems.Count>=256)return "Ground loot budget reached (256 piles). Recover a pile before dropping more.";var change=new InventoryTransaction{kind=from.slot==-2?"drop-bulk":"drop",actor=actor,from=from,count=count,pose=canonical,resolvedPose=resolved,id=World.nextId};Append(new IndustrialJournalEntry{inventory=change});World.ApplyInventory(change);return null;}}
    }
}






