using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed partial class IndustrialActor {
        public long id,lastSequence; public FramePose pose; public Inventory inventory=new Inventory{capacity=2000};
        public FramePose vehicleObstacle;public double vehicleObstacleRadius;public double collisionRadius=.35;public double dose,exposure,health=100,protection;public bool wearingProtection; public string lastCommand;
    }
    [Serializable] public sealed class IndustrialMachine {
        public long externalVehicle;public string displayName;public long id,owner,attachmentSite,attachmentModule;public int attachmentSocket=-1;public double constructionActivity;public bool resourceConstruction;public List<ItemStack> constructionPaid=new List<ItemStack>();public FramePose pose;public string definition,recipe,status="Idle";public Inventory input=new Inventory(),output=new Inventory();
        public double energy,powerAvailable,powerUsed,chargePower;public int progress;public bool closed=true,enabled=true;public double integrity=1;public long water,waterTarget;public long powerSource,waterIntake;
        public Inventory frontTailings;[System.ComponentModel.DefaultValue(0L)] public long tailingsDump;public bool ShouldSerializefrontTailings()=>frontTailings!=null;
        public IndustrialHaulJob haul;[System.ComponentModel.DefaultValue(false)] public bool customConveyorPorts;[System.ComponentModel.DefaultValue(false)] public bool integratedLogistics;public FramePose conveyorInput,conveyorOutput;public bool ShouldSerializeconveyorInput()=>customConveyorPorts;public bool ShouldSerializeconveyorOutput()=>customConveyorPorts;
        public List<FramePose> powerRoute,waterRoute;
        public List<ItemStack> reserved=new List<ItemStack>();
        [NonSerialized] string cachedDefinitionId;[NonSerialized] MachineDefinition cachedDefinition;
        [XmlIgnore] public MachineDefinition Definition {get{if(cachedDefinition==null||cachedDefinitionId!=definition){cachedDefinition=IndustrialCatalog.Machines[definition];cachedDefinitionId=definition;}return cachedDefinition;}}
        [XmlIgnore] public Inventory Export=>Definition.storage?input:output;
    }
    [Serializable] public sealed class BeltItem { public string item;public double distance,activity; }
    [Serializable] public sealed class ConveyorLane {
        public List<FramePose> route;[NonSerialized,XmlIgnore] public IndustrialConveyorPath path;
        public long id,owner,source,target;public double length,speed=2,spacing=.6;public string filter;public bool enabled=true;
        public bool resourceConstruction;public double constructionActivity;public List<ItemStack> constructionPaid=new List<ItemStack>();public List<BeltItem> items=new List<BeltItem>();
    }
    [Serializable] public sealed class DirtPile {
        static readonly double[] CentralWeights=CreateCentralWeights();
        static double[] CreateCentralWeights(){var weights=new double[256];for(int i=0;i<256;i++)weights[i]=Math.Sqrt((i%16-8)*(i%16-8)+(i/16-8)*(i/16-8))*650;return weights;}
        [System.ComponentModel.DefaultValue(false)] public bool tailingsPile;public Inventory tailings;public bool ShouldSerializetailings()=>tailings!=null;
        [XmlIgnore] public long Volume=>material.litres+(tailings?.Used??0);
        [XmlIgnore] public double Activity=>material.activity+IndustrialWorld.TailingsActivity(tailings);
        public long id,owner;public FramePose pose;public MaterialLot material;public long[] cells=new long[256];public int revision;
        public void Add(MaterialLot lot,int x=8,int z=8){if(tailingsPile)throw new InvalidOperationException("Keep tailings separate from pay dirt");if(lot.litres<=0)return;AddCells(lot.litres,x,z);material.Add(lot);}
        internal void AddCells(long amountToAdd,int x=8,int z=8){
            if(amountToAdd<=0)return;
            // Deposit in small conservative packets; repose relaxation uses integer litres.
            long left=amountToAdd;while(left>0){int best=0;double score=double.MaxValue;for(int i=0;i<256;i++){double q=cells[i]+(x==8&&z==8?CentralWeights[i]:Math.Sqrt((i%16-x)*(i%16-x)+(i/16-z)*(i/16-z))*650);if(q<score){score=q;best=i;}}long amount=Math.Min(left,100);cells[best]+=amount;left-=amount;}
            revision++;
        }
        public MaterialLot Scoop(long limit,int x=8,int z=8){
            long amount=Math.Min(limit,material.litres);if(amount<=0)return default;RemoveCells(amount,x,z);return material.Take(amount);}
        internal void RemoveCells(long amount,int x=8,int z=8){long left=amount;
            while(left>0){int best=-1;double score=double.MaxValue;for(int i=0;i<256;i++)if(cells[i]>0){double q=(i%16-x)*(i%16-x)+(i/16-z)*(i/16-z);if(q<score){score=q;best=i;}}if(best<0)throw new InvalidOperationException("Pile volume does not match its cells");long take=Math.Min(left,cells[best]);cells[best]-=take;left-=take;}
            revision++;
        }
    }
    [Serializable] public sealed class ConstructionStorageRecord {public int body;public string json;}
    [Serializable] public sealed class SurfaceDeposit {public int layoutVersion;public long id;public FramePose pose;public MaterialLot remaining;public long removed;public string resource="paydirt";}
    [Serializable] public sealed class IndustrialCommand {
        public long sequence,source,target,count,attachmentSite,attachmentModule;public int attachmentSocket=-1;public string kind,item,definition;public FramePose pose;
        public string operationRole;public int routeSplit;public List<FramePose> route;
        string RouteFingerprint(){if(route==null||route.Count==0)return "";var text=new System.Text.StringBuilder("|route");foreach(var p in route)text.Append("|").Append(p.body).Append("|").Append(p.frame).Append("|").Append(p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append("|").Append(p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append("|").Append(p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));return text.ToString();}
        public string Fingerprint()=>(attachmentModule==0?"":attachmentSite+"|"+attachmentModule+"|"+attachmentSocket+"|")+kind+"|"+source+"|"+target+"|"+count+"|"+item+"|"+definition+"|"+pose.body+"|"+pose.frame+"|"+pose.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+pose.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+pose.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+pose.yaw.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+RouteFingerprint()+(kind=="haul-start"?"|pickup|"+routeSplit:"")+(operationRole==null?"":"|role|"+operationRole);
    }
    public readonly struct CommandResult {
        public readonly bool accepted;public readonly string message;public readonly long entity;
        public CommandResult(bool accepted,string message,long entity=0){this.accepted=accepted;this.message=message;this.entity=entity;}
    }
    [Serializable] public sealed partial class IndustrialWorld {
        public const int Version=1;public int version=Version;public long nextId=1,tick,journalSequence;
        public List<IndustrialActor> actors=new List<IndustrialActor>();public List<IndustrialMachine> machines=new List<IndustrialMachine>();
        public List<ConstructionStorageRecord> buildStates=new List<ConstructionStorageRecord>();
        [System.ComponentModel.DefaultValue(false)] public bool miningWorkflow;
        public List<ConveyorLane> belts=new List<ConveyorLane>();public List<DirtPile> piles=new List<DirtPile>();public List<SurfaceDeposit> deposits=new List<SurfaceDeposit>();
        readonly Dictionary<long,(IndustrialMachine machine,int slot)> machineIndex=new Dictionary<long,(IndustrialMachine,int)>();long indexedNextId;
        readonly Dictionary<long,double> powerBudget=new Dictionary<long,double>();
        public long AllocateId()=>nextId++;
        public IndustrialActor Actor(long id)=>actors.Find(x=>x.id==id);
        void RebuildMachineIndex(){machineIndex.Clear();for(int i=0;i<machines.Count;i++)machineIndex.Add(machines[i].id,(machines[i],i));indexedNextId=nextId;}
        public IndustrialMachine Machine(long id){if(id==0)return null;if(machineIndex.Count!=machines.Count)RebuildMachineIndex();if(machineIndex.TryGetValue(id,out var value)){if(value.slot<machines.Count&&ReferenceEquals(machines[value.slot],value.machine))return value.machine;RebuildMachineIndex();return machineIndex.TryGetValue(id,out value)?value.machine:null;}if(indexedNextId!=nextId){RebuildMachineIndex();if(machineIndex.TryGetValue(id,out value))return value.machine;}return null;}
        public DirtPile Pile(long id){foreach(var pile in piles)if(pile.id==id)return pile;return null;}
        public SurfaceDeposit Deposit(long id){foreach(var deposit in deposits)if(deposit.id==id)return deposit;return null;}
        public IndustrialActor Register(long id,FramePose pose){var a=Actor(id);if(a!=null){if(a.craft!=null&&a.craft.id==0)a.craft.id=-1;return a;}a=new IndustrialActor{id=id,pose=pose};a.inventory.Add("stone",64);a.inventory.Add("fuel",8);if(inventoryWorkflow)a.inventory.EnableSlots(40);actors.Add(a);return a;}
        static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        static bool Valid(FramePose p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Finite(p.yaw);
        bool Reach(IndustrialActor a,FramePose p)=>Valid(p)&&a.pose.Distance(p)<=12;
        bool Own(IndustrialActor a,IndustrialMachine m)=>m!=null&&m.owner==a.id&&Reach(a,m.pose);
        bool Own(IndustrialActor a,DirtPile p)=>p!=null&&p.owner==a.id&&Reach(a,p.pose);
        public CommandResult Execute(long actorId,IndustrialCommand c,Action beforeApply=null,bool legacyEconomy=false){
            var a=Actor(actorId);if(a==null||c==null||c.sequence<=0)return Fail("Unknown actor or command");
            if(c.sequence<=a.lastSequence)return Fail(c.sequence==a.lastSequence&&c.Fingerprint()==a.lastCommand?"Already applied":"Stale or conflicting command");
            if(a.health<=0)return Fail("Player is dead");
            if(!Valid(a.pose)||!Valid(c.pose))return Fail("Invalid position");
            if(c.kind=="place"||c.kind=="pack"||c.kind=="pile"||c.kind=="belt"||c.kind=="remove-belt"||c.kind=="power"||c.kind=="water"||c.kind=="ship-place"||c.kind=="ship-remove"||c.kind=="ship-wire"||c.kind=="ship-unwire"){
                string policyError=ConstructionPolicyError(actorId);if(policyError!=null)return Fail(policyError);
            }
            Action apply=null;long entity=0;string message="Applied";
            var from=Machine(c.source);var to=Machine(c.target);
            switch(c.kind){
                case "haul-start":
                    if((c.item=="creative-vehicle"||c.item=="vehicle-operation")&&to!=null&&to.integrity<=0)return Fail("Repair the vehicle before auto operation");string haulError=HaulCommandError(actorId,c);if(haulError!=null)return Fail(haulError);var haulRoute=new List<FramePose>(c.route);apply=()=>{ReleaseHaul(to);if(c.item=="creative-vehicle"||c.item=="vehicle-operation"){to.enabled=true;to.status="Auto operation";}to.haul=new IndustrialHaulJob{source=c.source,destination=c.count,role=c.operationRole,lastServiceTick=tick,automaticSource=c.definition=="auto-source",freeFuel=creativeMode&&c.item=="creative-vehicle",route=haulRoute,pickupIndex=c.routeSplit};miningSiteRevision++;};message="Autonomous collection assigned";break;
                case "haul-stop":
                    if(!Own(a,to)||!to.Definition.Vehicle)return Fail("Vehicle unavailable");apply=()=>ReleaseHaul(to);message="Collection stopped; cargo retained";break;
                case "recover-tailings":
                    var wastePile=Pile(c.source);var wasteMachine=Machine(c.source);var wasteInventory=wastePile?.tailingsPile==true?wastePile.tailings:wasteMachine?.definition=="sluice"?wasteMachine.frontTailings:null;
                    if(wasteInventory==null||(wastePile!=null?!Own(a,wastePile):!Own(a,wasteMachine))||c.count<=0||c.count>1000||TailingsCount(wasteInventory)<c.count||!a.inventory.CanFit(c.count))return Fail("Move within reach of your tailings and make inventory room");
                    apply=()=>{MoveTailings(wasteInventory,a.inventory,c.count);wastePile?.RemoveCells(c.count);};break;
                case "creative":
                    if(a.id!=1||c.count<0||c.count>1)return Fail("Only the world host can change creative mode");apply=()=>creativeMode=c.count==1;message=c.count==1?"Creative mode enabled: free building and hand crafting":"Resource costs enabled";break;
                case "creative-item":
                    if(!creativeMode||!IndustrialCatalog.Items.ContainsKey(c.item??"")||IndustrialCatalog.RetiredConstructionRecipe(c.item)||c.count<1||c.count>10000||!a.inventory.CanAdd(c.item,c.count))return Fail("Enable creative mode and make inventory space first");apply=()=>a.inventory.Add(c.item,c.count);break;
                case "creative-dirt":
                    if(!creativeMode||c.count<1||c.count>10000||!a.inventory.CanAdd(c.item,c.count))return Fail("Enable creative mode and make room for pay dirt");apply=()=>a.inventory.bulk.Add(new MaterialLot{litres=c.count,iron=c.count/5,copper=c.count/10});break;
                case "recycle-legacy":
                    if(!IndustrialCatalog.RetiredConstructionRecipe(c.item)||!IndustrialCatalog.Recipes.TryGetValue(c.item,out var oldRecipe))return Fail("Not a legacy construction item");long batch=oldRecipe.output[0].count,legacyStock=a.inventory.Count(c.item);if(legacyStock<batch)return Fail("Collect a complete legacy batch before recovering materials");long copies=legacyStock/batch,returned=IndustrialConstruction.Quantity(oldRecipe.input)*copies;if(a.inventory.Free+copies*batch<returned)return Fail("Make room for recovered materials");var recoveryPlan=a.inventory.CloneSlots();recoveryPlan.Extract(c.item,copies*batch);var recoveryItems=new List<ItemStack>();foreach(var input in oldRecipe.input)recoveryItems.Add(new ItemStack(input.item,input.count*copies));if(!recoveryPlan.CanAddBatch(recoveryItems))return Fail("Make slots available for recovered materials");apply=()=>{double activity=a.inventory.Extract(c.item,copies*batch);var refund=new List<ItemStack>();foreach(var item in oldRecipe.input)refund.Add(new ItemStack(item.item,item.count*copies){activity=refund.Count==0?activity:0});IndustrialConstruction.Return(a.inventory,refund);};break;
                case "craft":
                    var craftError=PrepareHandcraft(a,c.item,c.count,out apply,creativeMode&&!legacyEconomy,legacyEconomy);if(craftError!=null)return Fail(craftError);break;
                case "cancel-craft":
                    var craft=CraftJob(a,c.target);if(craft==null)return Fail("Craft job is no longer queued");if(a.inventory.Free+Amount(craft.outputs)<Amount(craft.inputs))return Fail("Make room for returned ingredients");var cancelPlan=a.inventory.CloneSlots();cancelPlan.ReleaseSlots(craft.outputs);cancelPlan.reserved-=Amount(craft.outputs);if(!cancelPlan.CanAddBatch(craft.inputs))return Fail("Make slots available for returned ingredients");apply=()=>{a.inventory.ReleaseSlots(craft.outputs);a.inventory.reserved-=Amount(craft.outputs);foreach(var input in craft.inputs)a.inventory.Add(input.item,input.count,input.activity);if(a.craft==craft){a.craft=null;AdvanceQueue(a);}else a.craftQueue.Remove(craft);};break;
                case "place":
                    string placementError=PlacementError(actorId,c,legacyEconomy);if(placementError!=null)return Fail(placementError);var d=IndustrialCatalog.Machines[c.definition];entity=nextId;
                    apply=()=>{var paid=IndustrialConstruction.Receipt(a.inventory,legacyEconomy?IndustrialCatalog.LegacyMachineCost(d.id):inventoryWorkflow?new[]{IndustrialCatalog.S(IndustrialCatalog.MachineItem(d.id),1)}:d.cost,creativeMode&&!legacyEconomy&&!inventoryWorkflow,true);double activity=0;foreach(var item in paid)activity+=item.activity;machines.Add(new IndustrialMachine{id=AllocateId(),owner=a.id,pose=c.pose,definition=d.id,recipe=d.recipe,input=new Inventory{capacity=d.capacity},output=new Inventory{capacity=d.capacity},attachmentSite=c.attachmentSite,attachmentModule=c.attachmentModule,attachmentSocket=c.attachmentSocket,constructionActivity=activity,resourceConstruction=!legacyEconomy,constructionPaid=paid});};break;
                case "pack":
                    if(to?.integratedLogistics==true)return Fail("Remove the empty construction module to recover its logistics ports");
                    if(!Own(a,to)||HaulEndpointInUse(to.id)||to.input.reserved>0||to.input.Used>0||to.output.Used>0||TailingsCount(to.frontTailings)>0||to.reserved.Count>0||to.progress>0||to.water>0||to.energy>.001)return Fail("Empty buffers, stop production, and discharge stored energy before dismantling");
                    if(to.powerSource!=0||to.waterTarget!=0||machines.Exists(x=>x.powerSource==to.id||x.waterTarget==to.id)||belts.Exists(x=>x.source==to.id||x.target==to.id))return Fail("Disconnect belts, power and water before packing");
                    var recovered=legacyEconomy?IndustrialConstruction.WithActivity(IndustrialCatalog.LegacyMachineCost(to.definition),to.constructionActivity):to.resourceConstruction?to.constructionPaid:IndustrialConstruction.WithActivity(IndustrialCatalog.ResourceCost(to.definition),to.constructionActivity);if(!a.inventory.CanAddBatch(recovered))return Fail("Make room for recovered resources");apply=()=>{IndustrialConstruction.Return(a.inventory,recovered);machines.Remove(to);};break;
                case "disconnect":
                    if(!Own(a,to))return Fail("Machine unavailable");apply=()=>{to.powerSource=0;to.waterTarget=0;to.powerRoute=null;to.waterRoute=null;foreach(var other in machines){if(other.powerSource==to.id){other.powerSource=0;other.powerRoute=null;}if(other.waterTarget==to.id){other.waterTarget=0;other.waterRoute=null;}}connectionRevision++;};break;
                case "drain-energy":
                    if(!Own(a,to)||to.definition!="battery")return Fail("Battery unavailable");apply=()=>to.energy=0;break;
                case "pump-intake":
                    var intake=deposits.Find(x=>x.id==c.source);if(!Own(a,to)||to.definition!="pump"||intake==null||intake.resource!="water"||intake.remaining.activity!=0||to.pose.Distance(intake.pose)>8)return Fail("Place the pump within 8 m of a clean WATER deposit");apply=()=>to.waterIntake=intake.id;break;
                case "fill-water":
                    if(!Own(a,to)||c.count<=0||c.count>10000||to.water>10000-c.count||a.inventory.Count("water")<c.count||a.inventory.ActivityOf("water",c.count)>0)return Fail("Need clean carried water and reservoir space");apply=()=>{a.inventory.Remove("water",c.count);to.water+=c.count;};break;
                case "drain-water":
                    if(!Own(a,to)||to.water<=0||!a.inventory.CanAdd("water",to.water))return Fail("Make room to recover reservoir water");apply=()=>{a.inventory.Add("water",to.water);to.water=0;};break;
                case "pile":
                    string pileError=PilePlacementError(actorId,c.pose,legacyEconomy);if(pileError!=null)return Fail(pileError);
                    entity=nextId;apply=()=>{if(!creativeMode||legacyEconomy)a.inventory.Remove("stone",2);piles.Add(new DirtPile{id=AllocateId(),owner=a.id,pose=c.pose});};break;
                case "deposit-item":case "withdraw-item":case "withdraw-input":
                    var machine=c.kind=="deposit-item"?to:from;if(!Own(a,machine)||!IndustrialCatalog.Items.ContainsKey(c.item??"")||c.count<=0)return Fail("Invalid inventory transfer");
                    var src=c.kind=="deposit-item"?a.inventory:c.kind=="withdraw-input"?machine.input:machine.Export;var dst=c.kind=="deposit-item"?machine.input:a.inventory;
                    if(src.Count(c.item)<c.count||!dst.CanAdd(c.item,c.count))return Fail("Insufficient stock or capacity");
                    apply=()=>Inventory.Transfer(src,dst,c.item,c.count);break;
                case "dig":
                    var deposit=deposits.Find(x=>x.id==c.source);if(deposit==null||!Reach(a,deposit.pose)||c.count<=0||c.count>100||deposit.remaining.litres<c.count||(deposit.resource=="paydirt"?!a.inventory.CanFit(c.count):!a.inventory.CanAdd(deposit.resource,c.count)))return Fail("Deposit depleted, bucket full or out of reach");
                    foreach(var m in machines)if((deposit.resource=="paydirt"||deposit.resource=="stone")&&!m.Definition.Vehicle&&m.pose.Distance(deposit.pose)<6)return Fail("Excavation would undermine construction");
                    apply=()=>{var material=deposit.remaining.Take(c.count);if(deposit.resource=="paydirt")a.inventory.bulk.Add(material);else a.inventory.Add(deposit.resource,material.litres,material.activity);deposit.removed+=c.count;};break;
                case "dump":
                    var pile=Pile(c.target);if(pile?.tailingsPile==true)return Fail("Use tailings cleanup for this waste pile");if(!Own(a,pile)||c.count<=0||c.count>1000||a.inventory.bulk.litres<c.count||pile.material.litres+PileHaulReserved(pile.id)+c.count>100000)return Fail("Invalid pile discharge");
                    apply=()=>{pile.Add(a.inventory.bulk.Take(c.count));};break;
                case "scoop":
                    var scoop=Pile(c.source);if(!Own(a,scoop)||c.count<=0||c.count>1000||scoop.material.litres<c.count||!a.inventory.CanFit(c.count))return Fail("Invalid scoop");
                    apply=()=>a.inventory.bulk.Add(scoop.Scoop(c.count));break;
                case "bulk-transfer":
                    var sourcePile=Pile(c.source);var targetPile=Pile(c.target);if(sourcePile?.tailingsPile==true||targetPile?.tailingsPile==true)return Fail("Keep tailings separate; use tailings cleanup or recovery controls");
                    if(c.count<=0||c.count>1000)return Fail("Invalid bucket volume");
                    if(c.source!=0&&(from==null?!Own(a,sourcePile):!Own(a,from)))return Fail("Source unavailable");
                    if(c.target!=0&&(to==null?!Own(a,targetPile):!Own(a,to)))return Fail("Destination unavailable");
                    if(from?.haul?.active==true||to?.haul?.active==true)return Fail("Stop autonomous collection before manually moving truck cargo");
                    if(c.source==c.target)return Fail("Choose distinct endpoints");
                    var sourcePose=c.source==0?a.pose:from!=null?from.pose:sourcePile.pose;var targetPose=c.target==0?a.pose:to!=null?to.pose:targetPile.pose;
                    if(sourcePose.Distance(targetPose)>8)return Fail("Move the bucket within 8m of both endpoints");
                    if(to!=null&&to.definition!="sluice"&&!to.Definition.storage)return Fail("Machine does not accept bulk dirt");
                    var sourceInv=c.source==0?a.inventory:from?.input;var targetInv=c.target==0?a.inventory:to?.input;
                    long stock=sourcePile!=null?sourcePile.material.litres:sourceInv.bulk.litres;long free=targetPile!=null?100000-targetPile.material.litres-PileHaulReserved(targetPile.id):targetInv.Free;
                    if(stock<c.count||free<c.count)return Fail("Insufficient material or capacity");
                    apply=()=>{var part=sourcePile!=null?sourcePile.Scoop(c.count):sourceInv.bulk.Take(c.count);if(targetPile!=null)targetPile.Add(part);else targetInv.bulk.Add(part);};break;
                case "dig-vehicle":
                    var earth=deposits.Find(x=>x.id==c.source);if(!Own(a,to)||to.definition!="excavator"||earth==null||to.pose.Distance(earth.pose)>8||c.count<1||c.count>200||earth.remaining.litres<c.count||!to.input.CanFit(c.count))return Fail("Excavator bucket cannot reach this deposit");
                    foreach(var m in machines)if((earth.resource=="paydirt"||earth.resource=="stone")&&m!=to&&!m.Definition.Vehicle&&m.pose.Distance(earth.pose)<6)return Fail("Excavation would undermine construction");
                    apply=()=>{var part=earth.remaining.Take(c.count);if(earth.resource=="paydirt")to.input.bulk.Add(part);else to.input.Add(earth.resource,part.litres,part.activity);earth.removed+=c.count;};break;
                case "feed":
                    if(!Own(a,to)||to.definition!="sluice"||c.count<=0||c.count>1000||a.inventory.bulk.litres<c.count||!to.input.CanFit(c.count))return Fail("Sluice unavailable or bucket empty");
                    apply=()=>Inventory.TransferBulk(a.inventory,to.input,c.count);break;
                case "belt":
                    if(c.definition=="spline-conveyor-v1"){
                        string beltError=ConveyorError(actorId,c,out var spline);if(beltError!=null)return Fail(beltError);if(!string.IsNullOrEmpty(c.item)&&!IndustrialCatalog.Items.ContainsKey(c.item))return Fail("Unknown belt filter");var cost=RoutedConveyorCost(spline.Length);if(!creativeMode&&!a.inventory.Has(cost))return Fail("Belt needs plates and gears per 5 m");var anchors=new List<FramePose>(c.route);
                        entity=nextId;apply=()=>{var paid=IndustrialConstruction.Receipt(a.inventory,cost,creativeMode,true);belts.Add(new ConveyorLane{id=AllocateId(),owner=a.id,source=from.id,target=to.id,length=spline.Length,route=anchors,path=spline,filter=c.item,resourceConstruction=true,constructionPaid=paid,constructionActivity=ReceiptActivity(paid)});connectionRevision++;};break;
                    }
                    if(!Own(a,from)||!Own(a,to)||from==to||from.pose.Distance(to.pose)>30||!(creativeMode&&!legacyEconomy)&&!a.inventory.Has(legacyEconomy?new[]{IndustrialCatalog.S("belt-kit",1)}:IndustrialCatalog.ConveyorCost))return Fail("Belt needs reachable endpoints, length <=30m, and construction resources");
                    if(!string.IsNullOrEmpty(c.item)&&!IndustrialCatalog.Items.ContainsKey(c.item))return Fail("Unknown belt filter");
                    entity=nextId;apply=()=>{var paid=IndustrialConstruction.Receipt(a.inventory,legacyEconomy?new[]{IndustrialCatalog.S("belt-kit",1)}:IndustrialCatalog.ConveyorCost,creativeMode&&!legacyEconomy,true);belts.Add(new ConveyorLane{id=AllocateId(),owner=a.id,source=from.id,target=to.id,length=Math.Max(1,from.pose.Distance(to.pose)),filter=c.item,resourceConstruction=!legacyEconomy,constructionPaid=paid,constructionActivity=ReceiptActivity(paid)});connectionRevision++;};break;
                case "power":case "water":
                    bool legacyWaterLoop=c.route==null&&c.kind=="water"&&from==to&&Own(a,from)&&from.definition=="pump";string connectionError=legacyWaterLoop?null:ConnectionError(actorId,c);if(connectionError!=null)return Fail(connectionError);var routeCopy=c.route==null?null:new List<FramePose>(c.route);apply=()=>{if(c.kind=="power"){to.powerSource=from.id;to.powerRoute=routeCopy;}else{from.waterTarget=to.id;from.waterRoute=routeCopy;}connectionRevision++;};break;
                case "remove-power":case "remove-water":
                    if(!Own(a,to))return Fail("Machine unavailable");apply=()=>{if(c.kind=="remove-power"){to.powerSource=0;to.powerRoute=null;}else{to.waterTarget=0;to.waterRoute=null;}connectionRevision++;};break;
                case "recipe":
                    if(!Own(a,to)||to.progress>0||to.reserved.Count>0||!IndustrialCatalog.Recipes.ContainsKey(c.item??"")||!IndustrialCatalog.Supports(to.definition,c.item)||!legacyEconomy&&IndustrialCatalog.RetiredConstructionRecipe(c.item))return Fail("Recipe incompatible or machine busy");apply=()=>to.recipe=c.item;break;
                case "protection":
                    if(a.inventory.Count("radiation-suit")<1)return Fail("Manufacture a radiation suit first");apply=()=>a.wearingProtection=!a.wearingProtection;break;
                case "treat":
                    if(a.inventory.Count("decontamination-kit")<1)return Fail("Manufacture a decontamination kit first");apply=()=>{a.inventory.Remove("decontamination-kit",1);a.dose=Math.Max(0,a.dose-20);};break;
                case "vehicle-fuel":
                    if(!Own(a,to)||(to.definition!="truck"&&to.definition!="loader"&&to.definition!="excavator")||to.progress>0||to.input.Count("fuel")<1)return Fail("Vehicle needs fuel in its payload");apply=()=>{to.input.Remove("fuel",1);to.progress=100;};break;
                case "use-item":
                    if((c.item!="missile"&&c.item!="ammo")||c.count!=1||a.inventory.Count(c.item)<1)return Fail("Manufacture ammunition first");apply=()=>a.inventory.Remove(c.item,1);break;
                case "toggle":
                    if(!Own(a,to))return Fail("Machine unavailable");apply=()=>{to.enabled=!to.enabled;to.status=to.enabled?"Idle":"Disabled";};break;
                case "shield":
                    if(!Own(a,to))return Fail("Storage unavailable");apply=()=>to.closed=!to.closed;break;
                case "remove-belt":
                    var lane=belts.Find(x=>x.id==c.target);var laneSource=lane==null?null:Machine(lane.source);var laneTarget=lane==null?null:Machine(lane.target);if(lane==null||lane.owner!=a.id||a.health<=0||laneSource==null||laneTarget==null||laneSource.owner!=a.id||laneTarget.owner!=a.id||Math.Min(a.pose.Distance(laneSource.pose),a.pose.Distance(laneTarget.pose))>12)return Fail("Move within 12 m of either owned belt endpoint");
                    var beltRefund=lane.resourceConstruction?lane.constructionPaid:IndustrialConstruction.WithActivity(new[]{IndustrialCatalog.S("belt-kit",1)},0);var beltRecovery=new List<ItemStack>(beltRefund);foreach(var item in lane.items)beltRecovery.Add(new ItemStack(item.item,1){activity=item.activity});if(!a.inventory.CanAddBatch(beltRecovery))return Fail("Make room to recover belt contents");
                    apply=()=>{foreach(var item in lane.items)a.inventory.Add(item.item,1,item.activity);IndustrialConstruction.Return(a.inventory,beltRefund);belts.Remove(lane);connectionRevision++;};break;
                case "ship-initialize":case "ship-place":case "ship-remove":case "ship-wire":case "ship-unwire":
                    string shipError=PrepareShip(a,c,out apply);if(shipError!=null)return Fail(shipError);break;
                default:
                    if(c.kind==null||!c.kind.StartsWith("tribe-",StringComparison.Ordinal))return Fail("Unknown action");
                    string governmentError=PrepareGovernment(a,c,out apply);if(governmentError!=null)return Fail(governmentError);message="Tribe management updated";break;
            }
            beforeApply?.Invoke();apply();a.lastSequence=c.sequence;a.lastCommand=c.Fingerprint();return new CommandResult(true,message,entity);
        }
        static double ReceiptActivity(IEnumerable<ItemStack> receipt){double activity=0;foreach(var stack in receipt)activity+=stack.activity;return activity;}
        public string PilePlacementError(long owner,FramePose pose,bool legacy=false){var actor=Actor(owner);if(actor==null||!Reach(actor,pose)||(!creativeMode||legacy)&&actor.inventory.Count("stone")<2)return "Pile needs 2 stone and a reachable position";foreach(var m in machines)if(m.pose.Distance(pose)<8+m.Definition.Radius)return "Leave more space between the stockpile footprint and machinery";foreach(var p in piles)if(p.pose.Distance(pose)<16)return "Stockpile overlaps another pile";return null;}
        static CommandResult Fail(string why)=>new CommandResult(false,why);
        public void Step(){
            tick++;foreach(var owner in actors)AdvanceCraft(owner);foreach(var vehicle in machines)if(vehicle.definition=="truck"||vehicle.definition=="loader"||vehicle.definition=="excavator"){if(vehicle.haul?.active==true)StepHaul(vehicle);else if(vehicle.progress>0)vehicle.progress--;}
            // Stable creation order gives repeatable power allocation and conveyor merge priority.
            BeginPower();var available=powerBudget;
            foreach(var m in machines){
                if(!m.enabled||m.integrity<=0){m.status="Disabled";continue;}
                if(m.definition=="pump"){StepPump(m,available);continue;}if(m.definition=="water-reservoir"){StepReservoir(m);continue;}
                var machineDefinition=m.Definition;if(machineDefinition.PowerProvider||machineDefinition.storage||string.IsNullOrEmpty(m.recipe))continue;
                double required=m.definition=="workbench"?0:m.recipe=="wash"?5:IndustrialCatalog.Recipes[m.recipe].power;
                if(required>0&&(!available.TryGetValue(m.powerSource,out var power)||power<required)){m.status="No power";continue;}
                if(required>0)available[m.powerSource]-=required;
                if(m.recipe=="wash"){Wash(m);continue;}
                var r=IndustrialCatalog.Recipes[m.recipe];
                if(m.reserved.Count==0){
                    if(!m.input.Has(r.input)){m.status="Needs input";continue;}
                    long size=0;foreach(var output in r.output)size+=output.count;
                    if(!m.output.CanFit(size)){m.status="Output blocked";continue;}
                    if(m.water<r.water){m.status="Needs water";continue;}
                    double activity=0;foreach(var input in r.input)activity+=m.input.Extract(input.item,input.count);m.water-=r.water;double weight=0;foreach(var output in r.output)weight+=IndustrialCatalog.Items[output.item].activity*output.count;for(int k=0;k<r.output.Length;k++){var output=r.output[k];m.reserved.Add(new ItemStack(output.item,output.count){activity=weight>0?activity*IndustrialCatalog.Items[output.item].activity*output.count/weight:k==0?activity:0});}m.progress=0;
                }
                m.status="Processing";m.progress++;
                if(m.progress>=r.ticks){foreach(var output in m.reserved)if(!m.output.Add(output.item,output.count,output.activity))throw new InvalidOperationException("Reserved output capacity lost");m.reserved.Clear();m.progress=0;m.status="Complete";}
            }
            FinishPower();
            foreach(var belt in belts){if(!belt.enabled)continue;var source=Machine(belt.source);var target=Machine(belt.target);if(source==null||target==null)continue;
                // Head is first: blocked outputs propagate all the way to the source.
                for(int i=0;i<belt.items.Count;i++){var item=belt.items[i];double limit=i==0?belt.length:belt.items[i-1].distance-belt.spacing;item.distance=Math.Min(limit,item.distance+belt.speed*.1);}
                if(belt.items.Count>0&&belt.items[0].distance>=belt.length&&target.input.CanAdd(belt.items[0].item,1)){target.input.Add(belt.items[0].item,1,belt.items[0].activity);belt.items.RemoveAt(0);}
                if(belt.items.Count==0||belt.items[belt.items.Count-1].distance>=belt.spacing){var inv=source.Export;for(int i=0;i<inv.items.Count;i++){var stack=inv.items[i];if(!string.IsNullOrEmpty(belt.filter)&&belt.filter!=stack.item)continue;string item=stack.item;double activity=inv.Extract(item,1);belt.items.Add(new BeltItem{item=item,activity=activity});break;}}
            }
            foreach(var a in actors){a.protection=a.wearingProtection&&a.inventory.Count("radiation-suit")>0?.75:0;double exposure=0;foreach(var m in machines){double range=a.pose.Distance(m.pose);if(range>56)continue;if(m.frontTailings!=null&&m.frontTailings.items.Count>0&&range<=56){double frontRange=a.pose.Distance(TailingsPose(m));if(frontRange<=50)exposure+=TailingsActivity(m.frontTailings)/(1+frontRange*frontRange);}if(range>50)continue;double shielding=m.closed?1-(1-m.Definition.shielding)*m.integrity:1;double raw=m.constructionActivity+Activity(m.input,m.reserved)+Activity(m.output,null);exposure+=raw*shielding/(1+range*range);}
                foreach(var deposit in deposits){double r=a.pose.Distance(deposit.pose);if(r<=50)exposure+=deposit.remaining.activity*.001/(1+r*r);}
                foreach(var p in piles){double r=a.pose.Distance(p.pose);if(r<=50)exposure+=p.Activity/(1+r*r);}
                foreach(var b in belts){var src=Machine(b.source);var dst=Machine(b.target);if(src==null||dst==null)continue;if(b.constructionActivity>0){var midpoint=src.pose;midpoint.x=(src.pose.x+dst.pose.x)*.5;midpoint.y=(src.pose.y+dst.pose.y)*.5;midpoint.z=(src.pose.z+dst.pose.z)*.5;double distance=a.pose.Distance(midpoint);if(distance<=50)exposure+=b.constructionActivity/(1+distance*distance);}foreach(var item in b.items){double t=item.distance/b.length;var p=src.pose;p.x+=(dst.pose.x-p.x)*t;p.y+=(dst.pose.y-p.y)*t;p.z+=(dst.pose.z-p.z)*t;double r=a.pose.Distance(p);if(r<=50)exposure+=item.activity/(1+r*r);}}
                foreach(var drop in droppedItems){var pose=drop.pose.frame==0?drop.pose:drop.resolvedPose;double distance=a.pose.Distance(pose);if(distance<=50)exposure+=Activity(drop.inventory,null)/(1+distance*distance);}
                foreach(var source in constructionSources){double distance=a.pose.Distance(source.pose);if(distance<=50)exposure+=source.activity/(1+distance*distance);}
                foreach(var job in a.craftQueue)foreach(var output in job.outputs)exposure+=output.activity;exposure+=Activity(a.inventory,a.craft?.outputs);a.exposure=exposure*(1-Math.Max(0,Math.Min(1,a.protection)));a.dose=Math.Max(0,Math.Min(100,a.dose+(a.exposure>0?a.exposure*.01:-.02)));if(a.dose>=75)a.health=Math.Max(0,a.health-(a.dose-70)*.01);if(a.dose>=100)a.health=0;
            }
        }
        static double Activity(Inventory inv,List<ItemStack> reserved){double value=inv.bulk.activity;foreach(var s in inv.items)value+=s.activity;if(reserved!=null)foreach(var s in reserved)value+=s.activity;return value;}
        void Wash(IndustrialMachine m){if(miningWorkflow){WashToFront(m);return;}WashLegacy(m);}
        static void WashLegacy(IndustrialMachine m){
            if(m.input.bulk.litres<10){m.status="Needs pay dirt";return;}if(m.water<2){m.status="Needs water";return;}
            // Split composition first, reserve all outputs, then commit both ends atomically.
            var remaining=m.input.bulk;var batch=remaining.Take(10);long ores=batch.iron+batch.copper+batch.radioactive;
            if(!m.output.CanFit(Math.Max(batch.litres,ores))){m.status="Output blocked";return;}
            m.input.bulk=remaining;m.water--; // one of two water units is recycled
            long waste=Math.Max(0,batch.litres-ores);long activeUnits=batch.radioactive+waste;
            double oreActivity=activeUnits>0?(double)batch.activity*batch.radioactive/activeUnits:batch.activity;
            if(batch.iron>0)m.output.Add("iron-ore",batch.iron,activeUnits==0?oreActivity:0);if(batch.copper>0)m.output.Add("copper-ore",batch.copper,activeUnits==0&&batch.iron==0?oreActivity:0);if(batch.radioactive>0)m.output.Add("radioactive-ore",batch.radioactive,oreActivity);
            if(waste>0)m.output.Add(batch.activity>0?"radioactive-tailings":"tailings",waste,batch.activity-oreActivity);m.status="Washing";
        }
    }
}





