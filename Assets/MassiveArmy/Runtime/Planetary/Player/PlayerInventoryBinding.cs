using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
namespace MassiveArmy.Planetary {
    public sealed partial class PlayerInventory {
        IndustrialAuthority inventoryAuthority;long inventoryActor;int localEquippedSlot,cachedRevision=int.MinValue;
        readonly Dictionary<string,int> unifiedCounts=new Dictionary<string,int>();
        internal Inventory Unified=>inventoryAuthority?.World.Actor(inventoryActor)?.inventory;
        internal bool UnifiedInventory=>Unified!=null;
        internal void Bind(IndustrialAuthority authority,long actor,PersonalItemCatalogue catalogue){
            foreach(var definition in catalogue.items){IndustrialCatalog.RegisterPersonalItem(definition.id);IndustrialCatalog.Items[definition.id].stackLimit=definition.kind==PersonalItemKind.Consumable?20:1;}
            authority.EnableInventories();var who=authority.World.Actor(actor);bool initialImport=!who.personalInventoryImported;
            if(!who.personalInventoryImported){var imports=new List<ItemStack>();foreach(var pair in items)imports.Add(new ItemStack(pair.Key,pair.Value));if(!PersonalInitialised){imports.Add(new ItemStack("ration",3));imports.Add(new ItemStack("water",3));imports.Add(new ItemStack("bandage",2));}authority.ImportPersonalInventory(actor,imports);}
            inventoryAuthority=authority;inventoryActor=actor;items.Clear();PersonalInitialised=true;BlueprintGranted=true;
            // Only an initial empty belt receives starter assignments; loaded belt layouts stay intact.
            if(initialImport&&Unified.activeSlot<0){foreach(var initial in new[]{("repair-tool",0),("ration",1),("water",2),("bandage",3)})if(Unified.Count(initial.Item1)>0)Assign(initial.Item2,initial.Item1);EquippedSlot=0;}
            ClearMissingSlots();
        }
        IReadOnlyDictionary<string,int> UnifiedItems(){var source=Unified;if(cachedRevision!=source.revision){unifiedCounts.Clear();foreach(var item in source.items){unifiedCounts.TryGetValue(item.item,out int count);unifiedCounts[item.item]=(int)Math.Min(int.MaxValue,(long)count+item.count);}cachedRevision=source.revision;}return unifiedCounts;}
        bool UnifiedAssign(int slot,string item){if(slot<0||slot>=10)return false;var source=Unified;ItemStack found=null;foreach(var stack in source.items)if(stack.item==item){found=stack;break;}if(found==null)return false;if(found.slot==slot)return true;return inventoryAuthority.MoveInventory(inventoryActor,new InventoryAddress{slot=found.slot},new InventoryAddress{slot=slot},found.count)==null;}
        public int EquippedSlot {get=>Unified?.activeSlot??localEquippedSlot;set{if(inventoryAuthority!=null)inventoryAuthority.SelectInventorySlot(inventoryActor,value);else localEquippedSlot=value;}}
    }
}

