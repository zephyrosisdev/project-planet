using System;
using System.Collections.Generic;
using System.Xml.Serialization;
namespace MassiveArmy.Industrial {
    public sealed partial class Inventory {
        // Zero keeps old journal semantics. -1 is a volume-limited container;
        // positive limits bound player stacks. Overflow is retained during migration.
        public int slotLimit,activeSlot=-1;public List<ItemStack> slotReservations=new List<ItemStack>();
        [NonSerialized,XmlIgnore]public int revision;
        [NonSerialized,XmlIgnore]int totalsRevision=int.MinValue,totalsStacks=-1;[NonSerialized,XmlIgnore]long itemUnits;readonly Dictionary<string,long> totals=new Dictionary<string,long>();
        void RefreshTotals(){if(totalsRevision==revision&&totalsStacks==items.Count)return;totals.Clear();itemUnits=0;foreach(var stack in items){totals.TryGetValue(stack.item,out long amount);totals[stack.item]=amount+stack.count;itemUnits+=stack.count;}totalsRevision=revision;totalsStacks=items.Count;}
        long SlottedCount(string item){RefreshTotals();return totals.TryGetValue(item,out long value)?value:0;}
        long SlottedUsed(){RefreshTotals();return itemUnits+bulk.litres;}
        public ItemStack AtSlot(int slot){foreach(var item in items)if(item.slot==slot)return item;return null;}
        public static int StackLimit(string item)=>IndustrialCatalog.StackLimit(item);
        int VacantSlot(){int start=slotLimit==40?10:0;for(int i=start;i<Math.Max(start+1,slotLimit>0?slotLimit:items.Count+1);i++)if(AtSlot(i)==null)return i;if(start>0)for(int i=0;i<start;i++)if(AtSlot(i)==null)return i;for(int i=Math.Max(0,slotLimit);;i++)if(AtSlot(i)==null)return i;}
        public void EnableSlots(int limit){if(slotLimit!=0){slotLimit=limit;return;}slotLimit=limit;var old=new List<ItemStack>(items);items.Clear();foreach(var item in old){long left=item.count;while(left>0){long amount=Math.Min(left,StackLimit(item.item));items.Add(new ItemStack(item.item,amount){activity=item.activity*amount/item.count,slot=VacantSlot()});left-=amount;}}revision++;}
        public Inventory CloneSlots(){var clone=new Inventory{capacity=capacity,reserved=reserved,bulk=bulk,slotLimit=slotLimit,activeSlot=activeSlot};foreach(var item in items)clone.items.Add(new ItemStack(item.item,item.count){slot=item.slot,activity=item.activity});foreach(var item in slotReservations)clone.slotReservations.Add(new ItemStack(item.item,item.count));return clone;}
        bool SlotsFit(string incoming=null,long count=0){
            if(slotLimit<=0)return true;int free=slotLimit-items.Count;var room=new Dictionary<string,long>();foreach(var stack in items){room.TryGetValue(stack.item,out long value);room[stack.item]=value+Math.Max(0,StackLimit(stack.item)-stack.count);}
            bool Pack(string id,long amount){if(amount<=0)return true;int max=StackLimit(id);room.TryGetValue(id,out long available);long taken=Math.Min(available,amount);available-=taken;amount-=taken;if(amount>0){long slots=(amount+max-1)/max;if(slots>free)return false;free-=(int)slots;available+=slots*max-amount;}room[id]=available;return true;}
            foreach(var item in slotReservations)if(!Pack(item.item,item.count))return false;return incoming==null||Pack(incoming,count);
        }
        public bool CanAdd(string item,long count)=>count>0&&CanFit(count)&&SlotsFit(item,count);
        public bool CanAddBatch(IEnumerable<ItemStack> incoming){var copy=CloneSlots();foreach(var item in incoming)if(item.count>0&&!copy.Add(item.item,item.count,item.activity))return false;return true;}
        public bool CanReserve(IEnumerable<ItemStack> incoming){var copy=CloneSlots();foreach(var item in incoming)copy.slotReservations.Add(new ItemStack(item.item,item.count));return copy.SlotsFit();}
        public void ReserveSlots(IEnumerable<ItemStack> incoming){if(slotLimit<=0)return;foreach(var item in incoming)slotReservations.Add(new ItemStack(item.item,item.count));}
        public void ReleaseSlots(IEnumerable<ItemStack> outgoing){if(slotLimit<=0)return;foreach(var item in outgoing){long left=item.count;for(int i=slotReservations.Count-1;i>=0&&left>0;i--)if(slotReservations[i].item==item.item){long amount=Math.Min(left,slotReservations[i].count);slotReservations[i].count-=amount;left-=amount;if(slotReservations[i].count==0)slotReservations.RemoveAt(i);}}}
        public double ExtractAt(int slot,long count){var stack=AtSlot(slot);if(stack==null||count<1||count>stack.count)throw new InvalidOperationException("Stack changed");double activity=stack.activity*count/stack.count;stack.count-=count;stack.activity-=activity;if(stack.count==0)items.Remove(stack);revision++;return activity;}
        public bool CanAddAt(int slot,string item,long count){if(slot<0||slotLimit>0&&slot>=slotLimit||count<=0||!CanFit(count))return false;var target=AtSlot(slot);if(target!=null&&target.item!=item||count+(target?.count??0)>StackLimit(item))return false;return SlotsFit(item,count);}
        public void AddAt(int slot,string item,long count,double activity){if(!CanAddAt(slot,item,count))throw new InvalidOperationException("Destination stack changed");var stack=AtSlot(slot);if(stack==null)items.Add(new ItemStack(item,count){slot=slot,activity=activity});else{stack.count+=count;stack.activity+=activity;}revision++;}
    }
    public static partial class IndustrialCatalog {
        public static int StackLimit(string id){if(id==null)return 1000;if(Items.TryGetValue(id,out var metadata)&&metadata.stackLimit>0)return metadata.stackLimit;if(id.IndexOf('#')>=0||id.StartsWith("deploy-",StringComparison.Ordinal)||id=="sleeping-bag"||id=="sleeping-pod"||id=="repair-tool"||id=="suit-oxygen"||id.EndsWith("-tool",StringComparison.Ordinal)||id.EndsWith("-planner",StringComparison.Ordinal)||id=="stockpile-marker")return 1;if(id.StartsWith("module-",StringComparison.Ordinal))return 20;if(id=="ration"||id=="water"||id=="bandage"||id=="decontamination-kit")return 20;return 1000;}
        public static void RegisterPersonalItem(string id){if(!Items.ContainsKey(id))Items.Add(id,new ItemDefinition(id));}
    }
}

