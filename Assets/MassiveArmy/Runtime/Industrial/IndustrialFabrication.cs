using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class IndustrialCraftJob {public long id;public string recipe;public int progress;public List<ItemStack> inputs=new List<ItemStack>(),outputs=new List<ItemStack>();}
    [Serializable] public sealed class IndustrialConstructionSource {public long module;public FramePose pose;public double activity;}
    [Serializable] public sealed class IndustrialMount {public long site,module,owner;public int socket;public FramePose pose;public float width,depth;}
    public sealed partial class IndustrialActor {public IndustrialCraftJob craft;public long nextCraftId;public List<IndustrialCraftJob> craftQueue=new List<IndustrialCraftJob>();}
    public static partial class IndustrialCatalog {
        public static readonly HashSet<string> Handcraft=new HashSet<string>{"plate","wire","decontamination-kit"};
        public static bool LegacyHandcraft(string recipe)=>recipe=="workbench-kit"||recipe=="generator-kit"||recipe=="sluice-kit"||recipe=="foundry-kit"||recipe=="pump-kit"||recipe=="store-kit";
        public static readonly Dictionary<string,string> RoomKits=new Dictionary<string,string>();
        static void Fabrication(){
            foreach(var machine in Machines.Values){if(machine.Vehicle)continue;string kit=machine.id+"-kit";Items.Add(kit,new ItemDefinition(kit));R(kit,30,3,0,machine.cost,new[]{S(kit,1)});machine.cost=new[]{S(kit,1)};}
            Room("HabitatRoom","habitat-kit",new[]{S("plate",8),S("iron-bar",8)});
            Room("MedicalRoom","medical-kit",new[]{S("plate",8),S("electronics",4)});
            Room("ResearchRoom","lab-kit",new[]{S("plate",8),S("electronics",8)});
            Room("CommandRoom","command-kit",new[]{S("wire",8),S("electronics",8)});
            Room("LifeSupportRoom","life-support-kit",new[]{S("plate",8),S("motor",2)});
            Room("AirlockRoom","airlock-kit",new[]{S("plate",6),S("gear",2)});
            Room("CorridorRoom","corridor-kit",new[]{S("plate",4),S("iron-bar",4)});
            Room("JunctionRoom","junction-kit",new[]{S("plate",8),S("gear",2)});
            Room("GreenhouseRoom","greenhouse-kit",new[]{S("plate",8),S("wire",4)});
            Room("WorkshopRoom","workshop-kit",new[]{S("plate",12),S("gear",4)});
            Room("WarehouseRoom","warehouse-kit",new[]{S("plate",12),S("iron-bar",8)});
            foreach(var machine in Machines.Values)machine.cost=ResourceCost(machine.id);
        }
        static void Room(string module,string kit,ItemStack[] input){RoomKits.Add(module,kit);Items.Add(kit,new ItemDefinition(kit));R(kit,60,5,0,input,new[]{S(kit,1)});}
        public static bool WorkbenchKit(string recipe)=>Handcraft.Contains(recipe)||recipe=="press-kit"||recipe=="wire-mill-kit"||recipe=="assembler-kit"||recipe=="habitat-kit"||recipe=="corridor-kit"||recipe=="airlock-kit"||recipe=="junction-kit"||recipe=="workshop-kit"||recipe=="warehouse-kit";
    }
    public sealed partial class IndustrialWorld {
        public bool creativeMode;
        public List<IndustrialMount> mounts=new List<IndustrialMount>();
        public List<IndustrialConstructionSource> constructionSources=new List<IndustrialConstructionSource>();
        public void MigrateCraftIds(){foreach(var actor in actors)if(actor.craft!=null&&actor.craft.id==0)actor.craft.id=-1;}
        static long Amount(IEnumerable<ItemStack> stacks){long result=0;foreach(var stack in stacks)result+=stack.count;return result;}
        static string PrepareHandcraft(IndustrialActor a,string recipe,long count,out Action action,bool free=false,bool legacy=false){
            action=null;if(count<1||count>20)return "Choose between 1 and 20 crafts";
            if(a.craftQueue.Count+(a.craft==null?0:1)+count>32)return "Crafting queue is full (32 jobs)";
            if(recipe==null||!(legacy?IndustrialCatalog.LegacyHandcraft(recipe):IndustrialCatalog.Handcraft.Contains(recipe)))return "This recipe needs a workbench or assembler";
            var definition=IndustrialCatalog.Recipes[recipe];foreach(var input in definition.input)if(!free&&a.inventory.Count(input.item)<input.count*count)return "Missing crafting ingredients";
            if(a.inventory.Free+(free?0:Amount(definition.input)*count)<Amount(definition.output)*count)return "No room for the crafted items";
            if(a.inventory.slotLimit>0){var plan=a.inventory.CloneSlots();if(!free)foreach(var input in definition.input)plan.Extract(input.item,input.count*count);var outputs=new List<ItemStack>();for(int i=0;i<count;i++)outputs.AddRange(definition.output);if(!plan.CanReserve(outputs))return "Make inventory slots available for the crafting outputs";}
            action=()=>{for(int copy=0;copy<count;copy++){var job=new IndustrialCraftJob{id=++a.nextCraftId,recipe=recipe};double activity=0;if(!free)foreach(var input in definition.input){double source=a.inventory.Extract(input.item,input.count);job.inputs.Add(new ItemStack(input.item,input.count){activity=source});activity+=source;}for(int n=0;n<definition.output.Length;n++)job.outputs.Add(new ItemStack(definition.output[n].item,definition.output[n].count){activity=n==0?activity:0});a.inventory.ReserveSlots(job.outputs);a.inventory.reserved+=Amount(job.outputs);if(a.craft==null)a.craft=job;else a.craftQueue.Add(job);}};return null;
        }
        static IndustrialCraftJob CraftJob(IndustrialActor actor,long id)=>id==0||actor.craft?.id==id?actor.craft:actor.craftQueue.Find(x=>x.id==id);
        static void AdvanceCraft(IndustrialActor actor){if(actor.craft==null||actor.health<=0)return;var job=actor.craft;if(++job.progress<IndustrialCatalog.Recipes[job.recipe].ticks)return;actor.inventory.ReleaseSlots(job.outputs);actor.inventory.reserved-=Amount(job.outputs);foreach(var output in job.outputs)if(!actor.inventory.Add(output.item,output.count,output.activity))throw new InvalidOperationException("Craft reservation was lost");actor.craft=null;AdvanceQueue(actor);}
        static void AdvanceQueue(IndustrialActor actor){if(actor.craft!=null||actor.craftQueue.Count==0)return;actor.craft=actor.craftQueue[0];actor.craftQueue.RemoveAt(0);}
        public string PlacementError(long actorId,IndustrialCommand command,bool legacy=false){if(ConstructionPolicyError(actorId)!=null)return ConstructionPolicyError(actorId);
            var a=Actor(actorId);if(a==null||a.health<=0||!Valid(command.pose)||!Reach(a,command.pose))return "Aim within construction reach";
            if(!IndustrialCatalog.Machines.TryGetValue(command.definition??"",out var definition))return "Unknown machine";
            if(inventoryWorkflow&&(a.inventory.AtSlot(a.inventory.activeSlot)?.item!=IndustrialCatalog.MachineItem(definition.id)))return "Equip the crafted machine in a hotbar slot";
            if(!(creativeMode&&!legacy&&!inventoryWorkflow)&&!a.inventory.Has(legacy?IndustrialCatalog.LegacyMachineCost(definition.id):inventoryWorkflow?new[]{IndustrialCatalog.S(IndustrialCatalog.MachineItem(definition.id),1)}:definition.cost))return "Missing construction resources";
            if(command.attachmentModule!=0){var mount=mounts.Find(x=>x.site==command.attachmentSite&&x.module==command.attachmentModule&&x.socket==command.attachmentSocket&&x.owner==actorId);if(mount==null||mount.pose.Distance(command.pose)>.03)return "Socket is unavailable";float relative=command.pose.yaw-mount.pose.yaw;int turn=(int)Math.Round(relative/90);if(Math.Abs(relative-turn*90)>.1)return "Use quarter-turn socket rotations";double width=turn%2==0?definition.Width:definition.Depth,depth=turn%2==0?definition.Depth:definition.Width;if(width>mount.width+.001||depth>mount.depth+.001)return "Machine does not fit this socket";foreach(var m in machines)if(m.attachmentSite==mount.site&&m.attachmentModule==mount.module&&m.attachmentSocket==mount.socket)return "Machine socket occupied";}
            foreach(var m in machines)if(m.pose.Distance(command.pose)<m.Definition.Radius+definition.Radius)return "Machine footprint occupied";
            foreach(var p in piles)if(p.pose.Distance(command.pose)<8+definition.Radius)return "Keep stockpile clear";
            return null;
        }
    }
}


