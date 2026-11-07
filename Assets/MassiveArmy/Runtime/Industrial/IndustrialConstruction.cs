using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    public static class IndustrialConstruction {
        public static long Quantity(IEnumerable<ItemStack> stacks){long count=0;foreach(var stack in stacks)count=checked(count+stack.count);return count;}
        public static List<ItemStack> Receipt(Inventory inventory,ItemStack[] cost,bool free,bool spend=false){var result=new List<ItemStack>();if(free)return result;foreach(var stack in cost)result.Add(new ItemStack(stack.item,stack.count){activity=spend?inventory.Extract(stack.item,stack.count):inventory.ActivityOf(stack.item,stack.count)});return result;}
        public static List<ItemStack> WithActivity(ItemStack[] cost,double activity){var result=new List<ItemStack>();foreach(var stack in cost)result.Add(new ItemStack(stack.item,stack.count){activity=result.Count==0?activity:0});return result;}
        public static void Return(Inventory inventory,IEnumerable<ItemStack> receipt){foreach(var stack in receipt)if(!inventory.Add(stack.item,stack.count,stack.activity))throw new InvalidOperationException("Construction refund capacity was lost");}
    }
    public static partial class IndustrialCatalog {
        static readonly ItemStack[] FoundationCost={S("stone",2)},PanelCost={S("iron-bar",2)},StairCost={S("iron-bar",4)},DoorCost={S("iron-bar",4),S("gear",1)};
        public static readonly ItemStack[] ConveyorCost={S("plate",1),S("gear",1)};
        public static bool RetiredConstructionRecipe(string id)=>id!=null&&id.EndsWith("-kit",StringComparison.Ordinal)&&id!="decontamination-kit";
        public static ItemStack[] ModuleCost(string kind){if(RoomKits.TryGetValue(kind,out var recipe))return Recipes[recipe].input;if(kind.StartsWith("Foundation",StringComparison.Ordinal))return FoundationCost;if(kind.StartsWith("Stairs",StringComparison.Ordinal))return StairCost;if(kind=="DoorSingle"||kind=="DoorDouble"||kind=="GarageDoor")return DoorCost;return PanelCost;}
        public static ItemStack[] ResourceCost(string machine)=>Recipes[machine+"-kit"].input;
        public static ItemStack[] LegacyMachineCost(string machine)=>new[]{S(machine+"-kit",1)};
    }
}
