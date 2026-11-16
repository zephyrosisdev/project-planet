using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        RtsUiController inventoryToolkit;InventorySlotUi onlineSlots;VisualElement inventoryRoot,inventoryPanel,lootSlots;Label inventoryNotice,onlineCrosshair;readonly Button[] actionSlots=new Button[10];double nextInventoryRefresh;string lootSignature="";ProgressBar onlineHealth,onlineHunger,onlineThirst,onlineOxygen;Label evaHelp;PlanetNavigationCompass onlineCompass;
        void BuildOnlineInventory(VisualElement root){
            root.Clear();root.pickingMode=PickingMode.Ignore;inventoryRoot=root;
            onlineCompass=new PlanetNavigationCompass(clientWorld);root.Add(onlineCompass);
            onlineCrosshair=new Label("+"){name="online-crosshair",pickingMode=PickingMode.Ignore};onlineCrosshair.style.position=Position.Absolute;onlineCrosshair.style.left=Length.Percent(50);onlineCrosshair.style.top=Length.Percent(50);onlineCrosshair.style.width=20;onlineCrosshair.style.height=20;onlineCrosshair.style.marginLeft=-10;onlineCrosshair.style.marginTop=-10;onlineCrosshair.style.fontSize=20;onlineCrosshair.style.color=Color.white;onlineCrosshair.style.unityTextAlign=TextAnchor.MiddleCenter;root.Add(onlineCrosshair);
            var meters=new VisualElement{pickingMode=PickingMode.Ignore};meters.style.position=Position.Absolute;meters.style.left=18;meters.style.bottom=100;meters.style.width=380;root.Add(meters);
            onlineHealth=new ProgressBar{title="Health",lowValue=0,highValue=100};onlineHunger=new ProgressBar{title="Hunger",lowValue=0,highValue=100};onlineThirst=new ProgressBar{title="Thirst",lowValue=0,highValue=100};onlineOxygen=new ProgressBar{title="Suit oxygen",lowValue=0,highValue=100};foreach(var meter in new[]{onlineHealth,onlineHunger,onlineThirst,onlineOxygen}){meter.style.height=18;meters.Add(meter);}
            evaHelp=new Label("EVA · WASD thrust · R/C up/down · Shift boost · F7 camps"){pickingMode=PickingMode.Ignore};evaHelp.style.position=Position.Absolute;evaHelp.style.left=Length.Percent(30);evaHelp.style.bottom=100;evaHelp.style.color=Color.white;root.Add(evaHelp);
            var bar=new VisualElement{name="online-action-bar"};bar.style.position=Position.Absolute;bar.style.left=Length.Percent(15);bar.style.width=Length.Percent(70);bar.style.bottom=18;bar.style.flexDirection=FlexDirection.Row;bar.style.justifyContent=Justify.Center;root.Add(bar);
            inventoryPanel=new VisualElement{name="online-inventory"};inventoryPanel.style.position=Position.Absolute;inventoryPanel.style.left=Length.Percent(8);inventoryPanel.style.top=Length.Percent(9);inventoryPanel.style.width=Length.Percent(84);inventoryPanel.style.height=Length.Percent(76);inventoryPanel.style.backgroundColor=new Color(.02f,.035f,.055f,.98f);inventoryPanel.style.paddingLeft=inventoryPanel.style.paddingRight=16;inventoryPanel.style.paddingTop=inventoryPanel.style.paddingBottom=16;root.Add(inventoryPanel);
            var heading=new Label("INVENTORY");heading.style.color=Color.white;heading.style.fontSize=24;inventoryPanel.Add(heading);
            var controls=new VisualElement();controls.style.flexDirection=FlexDirection.Row;inventoryPanel.Add(controls);
            controls.Add(new Button(()=>{inventoryOpen=false;industryOpen=true;industryTools=false;UpdateNetworkCursor();}){text="CRAFTING / CONSTRUCTION [F4]"});controls.Add(new Button(()=>{inventoryOpen=false;UpdateNetworkCursor();}){text="CLOSE [I / ESC]"});
            inventoryNotice=new Label("Drag stacks between slots and the action bar. Right-drag splits a stack. Number keys equip; LMB uses the equipped item.");inventoryNotice.style.color=Color.white;inventoryNotice.style.whiteSpace=WhiteSpace.Normal;inventoryPanel.Add(inventoryNotice);
            var scroll=new ScrollView();scroll.style.flexGrow=1;scroll.style.minHeight=0;inventoryPanel.Add(scroll);
            onlineSlots?.Dispose();onlineSlots=new InventorySlotUi(root,ReadOnlineInventory,MoveOnlineInventory,DropOnlineInventory,slot=>Submit(new SurvivalAction{kind="slot",slot=slot}),()=>latest?.self==null||latest.self.health<=0,()=>inventoryOpen?inventoryPanel:null,a=>{},text=>Status=text);
            var columns=new VisualElement();columns.style.flexDirection=FlexDirection.Row;columns.style.flexWrap=Wrap.Wrap;scroll.Add(columns);
            var pack=new VisualElement();pack.style.marginRight=24;columns.Add(pack);onlineSlots.AddGrid(pack,new InventoryAddress(),10,30,"BACKPACK");onlineSlots.AddBulk(pack,new InventoryAddress(),"CARRIED BULK MATERIAL");onlineSlots.AddDropZone(pack);
            var belt=new VisualElement();columns.Add(belt);onlineSlots.AddGrid(belt,new InventoryAddress(),0,10,"ACTION BAR / EQUIPMENT",10);belt.Add(new Button(()=>Submit(new SurvivalAction{kind="use-equipped"})){text="USE EQUIPPED CONSUMABLE"});
            lootSlots=new VisualElement();scroll.Add(lootSlots);
            for(int i=0;i<10;i++){int slot=i;var button=new Button(()=>Submit(new SurvivalAction{kind="slot",slot=slot})){name="online-action-slot-"+i,focusable=false};button.style.width=95;button.style.height=60;button.style.whiteSpace=WhiteSpace.Normal;button.style.color=Color.white;button.style.backgroundColor=new Color(.15f,.2f,.23f);button.style.borderBottomWidth=3;bar.Add(button);actionSlots[i]=button;onlineSlots.Bind(button,new InventoryAddress{slot=i},inventoryPanel);}
            nextInventoryRefresh=0;lootSignature="";
            BuildOnlineCrafting(root);BuildOnlineCampMenu(root);
        }
        readonly System.Collections.Generic.Dictionary<long,(string json,Inventory inventory)> lootInventoryCache=new System.Collections.Generic.Dictionary<long,(string,Inventory)>();
        int lastEquippedSlot=-2;string lastEquippedItem;
        Inventory ReadOnlineInventory(InventoryAddress address){
            if(!PrivateStateReady||latest?.self==null)return null;
            if(address.section=="player"&&address.entity==0)return latest.self.inventory;
            if(address.section=="ground"&&visible.TryGetValue(address.entity*8,out var entity)&&latest.self.pose.Distance(entity.pose)<=12){if(lootInventoryCache.TryGetValue(address.entity,out var cached)&&cached.json==entity.data)return cached.inventory;var data=JsonUtility.FromJson<DroppedInventory>(entity.data).inventory;lootInventoryCache[address.entity]=(entity.data,data);return data;}
            if(clientMachines.TryGetValue(address.entity,out var machine)&&machine.owner==ActorId&&ClientDistance(machine.pose)<=12){if(address.section=="input")return machine.input;if(address.section=="output")return machine.Export;}
            return null;
        }
        string MoveOnlineInventory(InventoryAddress from,InventoryAddress to,long count,string item){if(ChatInputCaptured||!PrivateStateReady)return "Wait for your inventory";Submit(new SurvivalAction{kind="inventory-move",from=from,to=to,count=count,item=item});return null;}
        string DropOnlineInventory(InventoryAddress from,long count,string item){if(ChatInputCaptured||!PrivateStateReady)return "Wait for your inventory";Submit(new SurvivalAction{kind="inventory-drop",from=from,count=count,item=item});return "Drop requested";}
        void RefreshOnlineInventory(){
            if(inventoryRoot==null||latest?.self==null)return;
            inventoryRoot.style.display=latest.self.health>0?DisplayStyle.Flex:DisplayStyle.None;
            inventoryPanel.style.display=inventoryOpen?DisplayStyle.Flex:DisplayStyle.None;inventoryPanel.SetEnabled(PrivateStateReady&&!ChatInputCaptured);inventoryRoot.SetEnabled(!ChatInputCaptured);
            if(!inventoryOpen)onlineSlots.Cancel();
            onlineCompass.Refresh();onlineCrosshair.style.display=!ClientMenuOpen&&!ChatInputCaptured&&latest.self.health>0?DisplayStyle.Flex:DisplayStyle.None;
            RefreshOnlineCrafting();RefreshOnlineCampMenu();evaHelp.style.display=predicted?.eva==true&&!ClientMenuOpen?DisplayStyle.Flex:DisplayStyle.None;
            if(Time.unscaledTimeAsDouble<nextInventoryRefresh)return;nextInventoryRefresh=Time.unscaledTimeAsDouble+.1;
            onlineHealth.value=(float)latest.self.health;onlineHunger.value=(float)latest.vitals.hunger;onlineThirst.value=(float)latest.vitals.thirst;onlineOxygen.value=(float)latest.vitals.oxygen;
            var inventory=latest.self.inventory;
            for(int i=0;i<10;i++){var stack=inventory.AtSlot(i);var button=actionSlots[i];button.text=(i==9?"0":(i+1).ToString())+" · "+(stack==null?"Empty":InventorySlotUi.Name(stack.item))+(stack?.count>1?" ×"+stack.count:"");button.style.borderBottomColor=inventory.activeSlot==i?Color.cyan:Color.gray;button.SetEnabled(PrivateStateReady&&!ChatInputCaptured);button.tooltip=stack==null?"Empty":InventorySlotUi.Name(stack.item)+" ×"+stack.count;}
            var equipped=inventory.AtSlot(inventory.activeSlot);
            if(!ClientMenuOpen&&!ChatInputCaptured&&(lastEquippedSlot!=inventory.activeSlot||lastEquippedItem!=equipped?.item)){
                lastEquippedSlot=inventory.activeSlot;lastEquippedItem=equipped?.item;placingMachine=null;placementPose=null;
                if(equipped!=null){if(equipped.item.StartsWith("deploy-",StringComparison.Ordinal)&&IndustrialCatalog.Machines.ContainsKey(equipped.item.Substring(7)))BeginMachinePlacement(equipped,equipped.item.Substring(7));else if(IndustrialWorld.RespawnItem(equipped.item))BeginMachinePlacement(equipped,equipped.item);}
            }
            if(inventoryOpen){
                string signature="";foreach(var entity in visible.Values)if(entity.kind=="loot"&&latest.self.pose.Distance(entity.pose)<=12)signature+=entity.id+":";foreach(var machine in clientMachines.Values)if(machine.owner==ActorId&&ClientDistance(machine.pose)<=12)signature+="M"+machine.id+":";
                if(signature!=lootSignature){lootSignature=signature;lootSlots.Clear();foreach(var entity in visible.Values)if(entity.kind=="loot"&&latest.self.pose.Distance(entity.pose)<=12){var drop=JsonUtility.FromJson<DroppedInventory>(entity.data);onlineSlots.AddGrid(lootSlots,new InventoryAddress{section="ground",entity=drop.id},0,Math.Max(1,drop.inventory.items.Count),"NEARBY CONTAINER #"+drop.id);onlineSlots.AddBulk(lootSlots,new InventoryAddress{section="ground",entity=drop.id},"CONTAINER BULK");}foreach(var machine in clientMachines.Values)if(machine.owner==ActorId&&ClientDistance(machine.pose)<=12){onlineSlots.AddGrid(lootSlots,new InventoryAddress{section="input",entity=machine.id},0,12,machine.definition+" #"+machine.id+" INPUT");if(!ReferenceEquals(machine.input,machine.Export))onlineSlots.AddGrid(lootSlots,new InventoryAddress{section="output",entity=machine.id},0,12,"OUTPUT");}}
                onlineSlots.Refresh();
            }
        }
        void TickOnlineInventory(){if(HeadlessProbeClient)return;if(inventoryToolkit==null){inventoryToolkit=new RtsUiController(transform,BuildOnlineInventory,RefreshOnlineInventory,()=>Online);inventoryToolkit.Attach();}inventoryToolkit.Tick();}
        void DisposeOnlineInventory(){lootInventoryCache.Clear();lastEquippedSlot=-2;lastEquippedItem=null;onlineSlots?.Dispose();onlineSlots=null;inventoryToolkit?.Dispose();inventoryToolkit=null;inventoryRoot=inventoryPanel=lootSlots=null;campMenu=null;campPanel=null;campMenuOpen=false;craftingPanel=craftingGrid=craftingDetails=craftingQueue=null;updateCraftingDetails=null;craftingTiles.Clear();craftingStationIds.Clear();craftingMachine=0;industryTools=false;}
        string DropNetworkInventory(Peer peer,SurvivalAction action){
            if(action.from==null||action.from.section!="player"||action.from.entity!=0)return "Only carried items can be dropped";
            var actor=authority.World.Actor(peer.record.actor);var canonical=peer.motion.interior?peer.motion.pose:actor.pose;
            return authority.DropInventory(actor.id,action.from,action.count,canonical,actor.pose,action.item);
        }
    }
}



