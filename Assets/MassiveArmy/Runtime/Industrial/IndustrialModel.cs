using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MassiveArmy.Industrial {
    [Serializable] public struct WorldEntityId { public long value; public WorldEntityId(long value){this.value=value;} }
    [Serializable] public struct FramePose {
        public int body; public long frame; public double x,y,z; public float yaw;
        public double Distance(FramePose b)=>body!=b.body||frame!=b.frame?double.PositiveInfinity:Math.Sqrt((x-b.x)*(x-b.x)+(y-b.y)*(y-b.y)+(z-b.z)*(z-b.z));
    }
    // Bulk units are litres; composition and activity travel with the material, never with its mesh.
    [Serializable] public struct MaterialLot {
        public long litres,iron,copper,radioactive,activity;
        public MaterialLot Take(long count){
            if(count<=0||count>litres)throw new ArgumentOutOfRangeException(nameof(count));
            var part=new MaterialLot{litres=count,iron=Share(iron,count,litres),copper=Share(copper,count,litres),radioactive=Share(radioactive,count,litres),activity=Share(activity,count,litres)};
            litres-=part.litres;iron-=part.iron;copper-=part.copper;radioactive-=part.radioactive;activity-=part.activity;return part;
        }
        static long Share(long value,long count,long total)=>(long)((decimal)value*count/total);
        public void Add(MaterialLot other){litres=checked(litres+other.litres);iron=checked(iron+other.iron);copper=checked(copper+other.copper);radioactive=checked(radioactive+other.radioactive);activity=checked(activity+other.activity);}
    }
    [Serializable] public sealed class ItemStack { public int slot=-1;public string item; public long count; public double activity; public ItemStack(){} public ItemStack(string item,long count){this.item=item;this.count=count;} }
    public sealed class ItemDefinition { public string id;public int stackLimit; public double activity; public ItemDefinition(string id,double activity=0){this.id=id;this.activity=activity;} }
    [Serializable] public sealed partial class Inventory {
        public long capacity=1000,reserved; public List<ItemStack> items=new List<ItemStack>(); public MaterialLot bulk;
        [XmlIgnore] public long Used {get{if(slotLimit!=0)return SlottedUsed();long n=bulk.litres;foreach(var s in items)n=checked(n+s.count);return n;}}
        [XmlIgnore] public long Free=>capacity-Used-reserved;
        public long Count(string item){if(slotLimit!=0)return SlottedCount(item);long count=0;foreach(var s in items)if(s.item==item)count+=s.count;return count;}
        public bool CanFit(long count)=>count>=0&&count<=Free;
        public bool Add(string item,long count,double? activity=null){
            if(string.IsNullOrEmpty(item)||count<=0||!CanAdd(item,count))return false;double radiation=activity??(IndustrialCatalog.Items.TryGetValue(item,out var definition)?definition.activity*count:0);long left=count;
            foreach(var stack in items)if(stack.item==item){long add=slotLimit==0?left:Math.Min(left,Math.Max(0,StackLimit(item)-stack.count));stack.count=checked(stack.count+add);stack.activity+=radiation*add/count;left-=add;if(left==0){revision++;return true;}}
            while(left>0){long add=slotLimit==0?left:Math.Min(left,StackLimit(item));items.Add(new ItemStack(item,add){activity=radiation*add/count,slot=slotLimit==0?-1:VacantSlot()});left-=add;}revision++;return true;
        }
        public double ActivityOf(string item,long count){double activity=0;long left=count;var preferred=slotLimit!=0?AtSlot(activeSlot):null;if(preferred?.item==item){long amount=Math.Min(left,preferred.count);activity+=preferred.activity*amount/preferred.count;left-=amount;}for(int i=items.Count-1;i>=0&&left>0;i--){var stack=items[i];if(stack.item!=item||ReferenceEquals(stack,preferred))continue;long amount=Math.Min(left,stack.count);activity+=stack.activity*amount/stack.count;left-=amount;}return activity;}
        public double Extract(string item,long count){if(count<=0||Count(item)<count)throw new InvalidOperationException("Insufficient stock");double activity=0;long left=count;var preferred=slotLimit!=0?AtSlot(activeSlot):null;if(preferred?.item==item){long amount=Math.Min(left,preferred.count);activity+=ExtractAt(preferred.slot,amount);left-=amount;}for(int i=items.Count-1;i>=0&&left>0;i--){var stack=items[i];if(stack.item!=item)continue;long amount=Math.Min(left,stack.count);activity+=stack.activity*amount/stack.count;stack.activity-=stack.activity*amount/stack.count;stack.count-=amount;left-=amount;if(stack.count==0)items.RemoveAt(i);}revision++;return activity;}
        public bool Remove(string item,long count){if(count<=0||Count(item)<count)return false;Extract(item,count);return true;}
        public static bool Transfer(Inventory from,Inventory to,string item,long count){if(ReferenceEquals(from,to)||count<=0||from.Count(item)<count||!to.CanAdd(item,count))return false;double activity=from.Extract(item,count);if(!to.Add(item,count,activity))throw new InvalidOperationException("Transfer capacity lost");return true;}
        public static bool TransferBulk(Inventory from,Inventory to,long count){if(ReferenceEquals(from,to)||count<=0||from.bulk.litres<count||!to.CanFit(count))return false;var b=from.bulk;var part=b.Take(count);from.bulk=b;to.bulk.Add(part);return true;}
        public bool Has(ItemStack[] cost){foreach(var c in cost)if(Count(c.item)<c.count)return false;return true;}
        public bool Spend(ItemStack[] cost){if(!Has(cost))return false;foreach(var c in cost)Remove(c.item,c.count);return true;}
    }
    public sealed class RecipeDefinition {
        public string id; public ItemStack[] input,output; public int ticks,water; public double power;
        public RecipeDefinition(string id,int ticks,double power,int water,ItemStack[] input,ItemStack[] output){this.id=id;this.ticks=ticks;this.power=power;this.water=water;this.input=input;this.output=output;}
    }
    public sealed class MachineDefinition {
        public string id,recipe;public long capacity;public ItemStack[] cost;public double generation,shielding;public bool storage;
        public const double BatteryCapacity=6000;
        public bool PowerProvider=>id=="generator"||id=="solar-panel"||id=="wind-turbine"||id=="battery";
        public bool Vehicle=>id=="excavator"||id=="truck"||id=="loader";
        public double Width=>id=="water-reservoir"?3:id=="solar-panel"?3.2:id=="wind-turbine"?5:id=="battery"?2:id=="workbench"||id=="pump"?1.5:id=="foundry"?2.8:id=="refinery"?2.6:id=="sluice"||id=="generator"||id=="assembler"||id=="munitions"?2.4:id=="shielded-store"?2.2:Vehicle?2.5:2;
        public double Depth=>id=="water-reservoir"?3:id=="solar-panel"?3.2:id=="wind-turbine"?3:id=="battery"?1.5:id=="workbench"?1:id=="pump"?1.5:id=="sluice"?4:id=="foundry"?3.6:id=="refinery"?3.2:id=="generator"?2.4:id=="assembler"||id=="munitions"?2.6:id=="shielded-store"?2.2:Vehicle?5:2;
        [NonSerialized] string radiusId;[NonSerialized] double cachedRadius;
        public double Radius {get{if(radiusId!=id){cachedRadius=Vehicle?(id=="truck"?3.2:6):Math.Sqrt(Width*Width+Depth*Depth)*.5;radiusId=id;}return cachedRadius;}}
        public MachineDefinition(string id,string recipe,long capacity,ItemStack[] cost,double generation=0,double shielding=1,bool storage=false){this.id=id;this.recipe=recipe;this.capacity=capacity;this.cost=cost;this.generation=generation;this.shielding=shielding;this.storage=storage;}
    }
    // Gameplay recipes, not real-world metallurgical or nuclear engineering specifications.
    public static partial class IndustrialCatalog {
        public static ItemStack S(string id,long count)=>new ItemStack(id,count);
        public static readonly Dictionary<string,ItemDefinition> Items=new Dictionary<string,ItemDefinition>();
        public static readonly Dictionary<string,RecipeDefinition> Recipes=new Dictionary<string,RecipeDefinition>();
        public static readonly Dictionary<string,MachineDefinition> Machines=new Dictionary<string,MachineDefinition>();
        static IndustrialCatalog(){
            foreach(string id in new[]{"stone","water","radiation-suit","decontamination-kit","iron-ore","copper-ore","tailings","slag","iron-bar","copper-bar","steel","plate","wire","gear","electronics","motor","frame","hull-panel","engine","reactor-part","optics","capacitor","guidance","ammo","missile","nuclear-missile","excavator-kit","truck-kit","loader-kit","belt-kit","fuel"})Items.Add(id,new ItemDefinition(id));
            foreach(string id in new[]{"radioactive-ore","isotope","radioactive-tailings"})Items.Add(id,new ItemDefinition(id,id=="isotope"?20:5));
            R("iron",30,8,0,new[]{S("iron-ore",4)},new[]{S("iron-bar",2),S("slag",2)});
            R("copper",30,8,0,new[]{S("copper-ore",4)},new[]{S("copper-bar",2),S("slag",2)});
            R("steel",40,12,0,new[]{S("iron-bar",4),S("fuel",1)},new[]{S("steel",3),S("slag",2)});
            R("plate",15,3,0,new[]{S("iron-bar",2)},new[]{S("plate",2)});
            R("wire",15,3,0,new[]{S("copper-bar",1)},new[]{S("wire",2)});
            R("gear",20,4,0,new[]{S("iron-bar",2)},new[]{S("gear",1)});
            R("electronics",25,4,0,new[]{S("wire",2),S("plate",1)},new[]{S("electronics",1)});
            R("motor",30,5,0,new[]{S("wire",2),S("gear",2),S("plate",1)},new[]{S("motor",1)});
            R("frame",30,5,0,new[]{S("steel",4),S("plate",2)},new[]{S("frame",1)});
            R("hull-panel",30,5,0,new[]{S("steel",2),S("plate",4)},new[]{S("hull-panel",1)});
            R("engine",60,10,0,new[]{S("frame",2),S("motor",4),S("electronics",2)},new[]{S("engine",1)});
            R("reactor-part",60,10,2,new[]{S("steel",4),S("electronics",4)},new[]{S("reactor-part",1)});
            R("optics",30,5,0,new[]{S("plate",2),S("electronics",2)},new[]{S("optics",1)});
            R("capacitor",30,5,0,new[]{S("wire",4),S("electronics",2)},new[]{S("capacitor",1)});
            R("guidance",30,5,0,new[]{S("electronics",3)},new[]{S("guidance",1)});
            R("isotope",60,15,2,new[]{S("radioactive-ore",4)},new[]{S("isotope",1),S("radioactive-tailings",3)});
            R("ammo",20,5,0,new[]{S("iron-bar",2),S("copper-bar",1)},new[]{S("ammo",3)});
            R("missile",60,10,0,new[]{S("frame",1),S("guidance",1),S("fuel",4)},new[]{S("missile",1)});
            R("nuclear-missile",120,20,4,new[]{S("missile",1),S("isotope",4),S("reactor-part",2)},new[]{S("nuclear-missile",1)});
            foreach(string vehicle in new[]{"excavator","truck","loader"})R(vehicle+"-kit",100,10,0,new[]{S("frame",2),S("motor",2),S("gear",4)},new[]{S(vehicle+"-kit",1)});
            R("radiation-suit",60,5,0,new[]{S("steel",2),S("electronics",1)},new[]{S("radiation-suit",1)});
            R("decontamination-kit",30,3,0,new[]{S("water",10),S("stone",2)},new[]{S("decontamination-kit",1)});
            R("belt-kit",10,2,0,new[]{S("plate",1),S("gear",1)},new[]{S("belt-kit",4)});
            M("workbench","gear",500,new[]{S("stone",4)});
            M("generator",null,100,new[]{S("stone",6)},30);
            M("sluice","wash",2000,new[]{S("stone",10)});
            M("foundry","iron",1000,new[]{S("stone",12)});
            M("press","plate",500,new[]{S("iron-bar",4)});
            M("wire-mill","wire",500,new[]{S("iron-bar",4)});
            M("assembler","electronics",1000,new[]{S("plate",4),S("wire",4)});
            M("munitions","ammo",1000,new[]{S("frame",4),S("electronics",4)});
            M("refinery","isotope",1000,new[]{S("steel",8),S("electronics",4)},0,.02);
            M("pump",null,100,new[]{S("stone",6)});
            M("store",null,10000,new[]{S("stone",6)},0,1,true);
            M("shielded-store",null,10000,new[]{S("steel",8)},0,.01,true);
            M("excavator",null,1000,new[]{S("excavator-kit",1)},0,1,true);
            M("truck",null,10000,new[]{S("truck-kit",1)},0,1,true);
            M("loader",null,1000,new[]{S("loader-kit",1)},0,1,true);
            M("solar-panel",null,100,new[]{S("stone",8)},20);
            M("wind-turbine",null,100,new[]{S("iron-bar",4),S("gear",2)},24);
            M("battery",null,100,new[]{S("iron-bar",4),S("wire",4)},30);
            M("water-reservoir",null,100,new[]{S("iron-bar",8),S("plate",4)});
            Fabrication();RespawnRecipes();EquipmentRecipes();
        }
        static void R(string id,int ticks,double power,int water,ItemStack[] a,ItemStack[] b)=>Recipes.Add(id,new RecipeDefinition(id,ticks,power,water,a,b));
        static void M(string id,string recipe,long cap,ItemStack[] cost,double generation=0,double shielding=1,bool storage=false)=>Machines.Add(id,new MachineDefinition(id,recipe,cap,cost,generation,shielding,storage));
        public static bool Supports(string machine,string recipe){
            if(machine=="workbench")return WorkbenchKit(recipe)||recipe=="plate"||recipe=="wire"||recipe=="gear"||recipe=="belt-kit";
            if(machine=="foundry")return recipe=="iron"||recipe=="copper"||recipe=="steel";
            if(machine=="assembler")return recipe=="radiation-suit"||recipe=="decontamination-kit"||recipe=="electronics"||recipe=="motor"||recipe=="frame"||recipe=="hull-panel"||recipe=="engine"||recipe=="reactor-part"||recipe=="optics"||recipe=="capacitor"||recipe=="guidance"||recipe.EndsWith("-kit",StringComparison.Ordinal);
            if(machine=="munitions")return recipe=="ammo"||recipe=="missile"||recipe=="nuclear-missile";
            return Machines.TryGetValue(machine,out var d)&&d.recipe==recipe;
        }
    }
}





