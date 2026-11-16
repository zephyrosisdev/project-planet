using System;
using System.Globalization;
using MassiveArmy.Industrial;
namespace MassiveArmy.Planetary {
    // Shared recipe vocabulary for offline and network UI; never mutates inventories.
    internal static class CraftingRecipePresentation {
        internal static readonly string[] Categories={"All","Starter","Building","Machines","Vehicles","Tools","Logistics","Components","Survival","Munitions","Refining","Colony rooms"};
        internal static string Name(string id)=>string.IsNullOrEmpty(id)?"None":CultureInfo.InvariantCulture.TextInfo.ToTitleCase(IndustrialCatalog.DisplayItem(id));
        internal static string Category(string id){
            if(IndustrialCatalog.MachineForItem(id,out var machine))return IndustrialCatalog.Machines[machine].Vehicle?"Vehicles":"Machines";
            if(IndustrialCatalog.ModuleForItem(id,out _))return "Building";
            if(id=="conveyor-planner"||id=="wire-tool"||id=="pipe-tool"||id=="stockpile-marker")return "Tools";
            if(IndustrialWorld.RespawnItem(id)||id=="radiation-suit"||id=="decontamination-kit"||id=="docking-station-kit")return "Survival";
            if(IndustrialCatalog.RoomKits.ContainsValue(id))return "Colony rooms";
            if(id=="belt-kit")return "Logistics";
            if(id=="ammo"||id.Contains("missile")||id=="guidance")return "Munitions";
            if(id=="excavator-kit"||id=="truck-kit"||id=="loader-kit")return "Vehicles";
            if(id.EndsWith("-kit",StringComparison.Ordinal))return "Machines";
            if(id=="iron"||id=="copper"||id=="steel"||id=="isotope")return "Refining";
            return IndustrialCatalog.Handcraft.Contains(id)?"Starter":"Components";
        }
        internal static string Station(string id){if(IndustrialCatalog.Handcraft.Contains(id))return "By hand";foreach(var station in new[]{"workbench","foundry","press","wire-mill","assembler","refinery","munitions"})if(IndustrialCatalog.Supports(station,id))return Name(station);return "Processing machine";}
        internal static int MaxBatch(IndustrialActor actor,RecipeDefinition recipe){
            if(actor==null||!IndustrialCatalog.Handcraft.Contains(recipe.id))return 0;
            long maximum=Math.Min(20,32-actor.craftQueue.Count-(actor.craft==null?0:1));
            foreach(var input in recipe.input)maximum=Math.Min(maximum,actor.inventory.Count(input.item)/input.count);
            // Account for both physical capacity and slot reservations, as the server does.
            for(int count=(int)Math.Max(0,maximum);count>0;count--){var plan=actor.inventory.CloneSlots();long consumed=0,produced=0;foreach(var input in recipe.input){consumed+=input.count*count;plan.Extract(input.item,input.count*count);}var outputs=new System.Collections.Generic.List<ItemStack>();for(int i=0;i<count;i++)foreach(var output in recipe.output){outputs.Add(output);produced+=output.count;}if(actor.inventory.Free+consumed>=produced&&(plan.slotLimit<=0||plan.CanReserve(outputs)))return count;}
            return 0;
        }
    }
}
