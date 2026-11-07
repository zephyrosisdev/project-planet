using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    public static partial class IndustrialCatalog {
        public static readonly Dictionary<string,string> ModuleItems=new Dictionary<string,string>();
        public static string MachineItem(string machine)=>"deploy-"+machine;
        public static string ModuleItem(string kind)=>"module-"+kind;
        public static bool MachineForItem(string item,out string machine){machine=item!=null&&item.StartsWith("deploy-",StringComparison.Ordinal)?item.Substring(7):null;return machine!=null&&Machines.ContainsKey(machine);}
        public static bool ModuleForItem(string item,out string kind)=>ModuleItems.TryGetValue(item??"",out kind);
        static void EquipmentRecipes(){
            foreach(var machine in Machines.Values){string id=MachineItem(machine.id);Items.Add(id,new ItemDefinition(id));R(id,machine.Vehicle?100:40,0,0,ResourceCost(machine.id),new[]{S(id,1)});Handcraft.Add(id);}
            foreach(string kind in new[]{"FoundationSquare","FoundationTriangle","Wall","DoorwaySingle","DoorwayDouble","DoorFrame","GarageFrame","DoorSingle","DoorDouble","GarageDoor","CeilingSquare","CeilingTriangle","StairsStraight","Stairs90","Stairs180","HabitatRoom","MedicalRoom","ResearchRoom","CommandRoom","LifeSupportRoom","AirlockRoom","CorridorRoom","JunctionRoom","GreenhouseRoom","WorkshopRoom","WarehouseRoom"}){string id=ModuleItem(kind);ModuleItems.Add(id,kind);Items.Add(id,new ItemDefinition(id));R(id,40,0,0,ModuleCost(kind),new[]{S(id,1)});Handcraft.Add(id);}
            foreach(string tool in new[]{"conveyor-planner","wire-tool","pipe-tool","stockpile-marker"}){Items.Add(tool,new ItemDefinition(tool));R(tool,20,0,0,new[]{S("stone",2)},new[]{S(tool,1)});Handcraft.Add(tool);}
        }
        public static string DisplayItem(string id){if(MachineForItem(id,out var machine))return machine;if(ModuleForItem(id,out var module)){var text=new System.Text.StringBuilder();foreach(char c in module){if(char.IsUpper(c)&&text.Length>0)text.Append(' ');text.Append(c);}return text.ToString();}return id?.Split('#')[0].Replace('-',' ');}
    }
}
