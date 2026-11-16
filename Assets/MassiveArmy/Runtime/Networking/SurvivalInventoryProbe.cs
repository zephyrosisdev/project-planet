#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        bool InventoryRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-survival-probe-inventory")>=0;
        int craftingProbeStep;long craftingProbeStock,craftingProbeSecond;
        bool CraftingProbeReady(){
            var actor=session.Latest.self;var root=session.OnlineInventoryRoot;
            if(craftingProbeStep==8)return true;report.inventoryStepNow=100+craftingProbeStep;
            if(craftingProbeStep==0){if(actor.craft!=null||actor.craftQueue.Count!=0||actor.inventory.Count("stone")<32){_ = Finish(false,"Crafting UI fixture needs an empty queue and 32 stone");return false;}craftingProbeStock=actor.inventory.Count("stone");session.SetCraftingProbeOpen();craftingProbeStep=1;return false;}
            if(craftingProbeStep==1){var search=root.Q<TextField>("online-craft-search");if(search==null)return false;search.value="solar";StartCoroutine(CapturePresentation(false,".crafting-open.png"));craftingProbeStep=2;return false;}
            if(craftingProbeStep==2){if(root.Q<Button>("online-recipe-plate")!=null){_ = Finish(false,"Attached crafting search did not filter recipes");return false;}if(!ClickCraftingButton(root.Q<Button>("online-recipe-deploy-solar-panel")))return false;craftingProbeStep=3;return false;}
            if(craftingProbeStep==3){var tile=root.Q<Button>("online-recipe-deploy-solar-panel");if(tile==null||!tile.ClassListContains("selected"))return false;var amount=root.Q<IntegerField>("online-craft-quantity");if(amount==null)return false;amount.value=999;if(amount.value!=20){_ = Finish(false,"Crafting quantity callback did not clamp to 20");return false;}amount.value=2;craftingProbeStep=4;return false;}
            if(craftingProbeStep==4){var button=root.Q<Button>("online-craft-submit");if(button==null||!button.text.Contains("×2")||!ClickCraftingButton(button))return false;craftingProbeStep=5;return false;}
            if(craftingProbeStep==5){if(actor.craft?.recipe!="deploy-solar-panel"||actor.craftQueue.Count!=1)return false;var first=root.Q<Button>("online-craft-cancel-"+actor.craft.id);craftingProbeSecond=actor.craftQueue[0].id;if(!ClickCraftingButton(first))return false;StartCoroutine(CapturePresentation(false,".crafting.png"));craftingProbeStep=6;return false;}
            if(craftingProbeStep==6){if(actor.craft?.id!=craftingProbeSecond||actor.craftQueue.Count!=0||!ClickCraftingButton(root.Q<Button>("online-craft-cancel-"+craftingProbeSecond)))return false;craftingProbeStep=7;return false;}
            if(actor.craft!=null||actor.craftQueue.Count!=0||actor.inventory.Count("stone")!=craftingProbeStock)return false;
            Debug.Log("Crafting UI PASS: search, recipe selection and quantity callbacks queued two server jobs; cancellation refunded all inputs");session.SetInventoryProbeOpen(true);craftingProbeStep=8;return true;
        }
        bool ClickCraftingButton(Button button){if(button==null||!button.enabledInHierarchy)return false;var bounds=button.worldBound;if(bounds.width<=0||bounds.height<=0||float.IsNaN(bounds.width)||float.IsNaN(bounds.height)||float.IsNaN(bounds.x)||float.IsNaN(bounds.y)||float.IsInfinity(bounds.width)||float.IsInfinity(bounds.height))return false;var hit=button.panel?.Pick(bounds.center);while(hit!=null&&hit!=button)hit=hit.parent;if(hit!=button)return false;Debug.Log("Crafting UI click: "+button.name+" · "+button.text+" · "+bounds);using(var down=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=bounds.center}))button.SendEvent(down);using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=bounds.center}))button.SendEvent(up);return true;}
        int inventoryStep,inventoryOriginal,inventoryPack,inventoryBelt;long inventoryCount,inventoryHalf,inventoryTotal,inventoryDrop;string inventoryItem;
        bool InventoryProbeReady(){
            if(!InventoryRequested)return true;
            report.inventoryStepNow=inventoryStep;
            var data=session.Latest.self.inventory;
            if(!session.PrivateStateReady||session.OnlineInventoryRoot?.panel==null)return false;
            if(!CraftingProbeReady())return false;
            switch(inventoryStep){
                case 0:
                    ItemStack selected=null;foreach(var stack in data.items)if(stack.count>=2){selected=stack;break;}if(selected==null){_ = Finish(false,"Inventory test needs an existing divisible carried stack");return false;}
                    inventoryOriginal=selected.slot;inventoryItem=selected.item;inventoryCount=selected.count;inventoryHalf=(inventoryCount+1)/2;inventoryTotal=data.Count(inventoryItem);
                    inventoryPack=-1;inventoryBelt=-1;for(int i=10;i<40;i++)if(data.AtSlot(i)==null){inventoryPack=i;break;}for(int i=0;i<10;i++)if(data.AtSlot(i)==null){inventoryBelt=i;break;}
                    if(inventoryPack<0||inventoryBelt<0){_ = Finish(false,"Inventory test needs one free backpack and belt slot");return false;}
                    session.SetInventoryProbeOpen(true);session.Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{slot=inventoryOriginal},to=new InventoryAddress{slot=inventoryPack},count=inventoryCount,item=inventoryItem});inventoryStep=1;return false;
                case 1:
                    if(data.AtSlot(inventoryPack)?.count!=inventoryCount)return false;
                    if(!DragInventory("inventory-slot-player-0-"+inventoryPack,"online-action-slot-"+inventoryBelt,1))return false;
                    StartCoroutine(CapturePresentation(false,".inventory.png"));inventoryStep=2;return false;
                case 2:
                    if(data.AtSlot(inventoryBelt)?.count!=inventoryHalf||data.AtSlot(inventoryPack)?.count!=inventoryCount-inventoryHalf)return false;
                    if(!DragInventory("online-action-slot-"+inventoryBelt,"online-action-slot-"+inventoryBelt,0))return false;inventoryStep=3;return false;
                case 3:
                    if(data.activeSlot!=inventoryBelt)return false;
                    if(!DragInventory("online-action-slot-"+inventoryBelt,"inventory-drop-zone",0))return false;inventoryStep=4;return false;
                case 4:
                    if(data.AtSlot(inventoryBelt)!=null)return false;
                    foreach(var entity in session.Visible.Values)if(entity.kind=="loot"){var drop=JsonUtility.FromJson<DroppedInventory>(entity.data);foreach(var stack in drop.inventory.items)if(stack.item==inventoryItem&&stack.count==inventoryHalf){inventoryDrop=drop.id;if(!DragInventory("inventory-slot-ground-"+drop.id+"-"+stack.slot,"inventory-slot-player-0-"+inventoryPack,0))return false;inventoryStep=5;return false;}}return false;
                case 5:
                    if(data.Count(inventoryItem)!=inventoryTotal||data.AtSlot(inventoryPack)?.count!=inventoryCount)return false;
                    session.Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{slot=inventoryPack},to=new InventoryAddress{slot=inventoryOriginal},count=inventoryCount,item=inventoryItem});inventoryStep=6;return false;
                case 6:
                    if(data.AtSlot(inventoryOriginal)?.count!=inventoryCount)return false;
                    report.inventorySlotsVerified=true;session.SetInventoryProbeOpen(false);inventoryStep=7;return true;
                default:return true;
            }
        }
        bool DragInventory(string source,string target,int button){var root=session.OnlineInventoryRoot;var from=root.Q<VisualElement>(source);var to=root.Q<VisualElement>(target);if(from==null||to==null||from.worldBound.width<=0||to.worldBound.width<=0)return false;
            Debug.Log("Inventory UI drag: "+source+" -> "+target+" button="+button+" source="+from.worldBound+" target="+to.worldBound+" picked="+root.panel.Pick(to.worldBound.center)?.name);
            using(var down=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=button,mousePosition=from.worldBound.center}))from.SendEvent(down);
            using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=button,mousePosition=to.worldBound.center}))root.SendEvent(up);return true;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        internal VisualElement OnlineInventoryRoot=>inventoryRoot;
        internal void SetCraftingProbeOpen(){industryOpen=true;industryTools=false;inventoryOpen=networkMenu=false;UpdateNetworkCursor();}
        internal void SetInventoryProbeOpen(bool value){inventoryOpen=value;industryOpen=networkMenu=false;UpdateNetworkCursor();}
    }
}
#endif

