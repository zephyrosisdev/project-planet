using System;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        string equippedConstructionItem;int equippedConstructionSlot=-2;bool equipmentMenuWasOpen;
        internal bool EquipmentInventory=>IndustrialAuthority?.World.inventoryWorkflow==true&&PersonalInventory?.UnifiedInventory==true;
        internal bool EquippedBuildingModule(out PlayerModuleKind kind){kind=default;return EquipmentInventory&&IndustrialCatalog.ModuleForItem(PersonalInventory.Equipped,out var name)&&Enum.TryParse(name,out kind);}
        internal void UpdateEquippedConstruction(){
            if(!EquipmentInventory)return;bool closing=equipmentMenuWasOpen&&!PersonalMenuOpen;equipmentMenuWasOpen=PersonalMenuOpen;if(PersonalMenuOpen){equippedConstructionItem=null;equippedConstructionSlot=-2;return;}PersonalInventory.ClearMissingSlots();string item=PersonalInventory.Equipped;int slot=PersonalInventory.EquippedSlot;
            if(!closing&&Keyboard.current?.escapeKey.wasPressedThisFrame==true&&!PersonalMenuOpen&&(RespawnPlacementActive||IndustrialPlacementActive||EquippedBuildingModule(out _))){PersonalInventory.EquippedSlot=-1;item=null;slot=-1;}
            if(!IsFirstPerson||IsDriving||IndustrialDriving){equippedConstructionItem=null;equippedConstructionSlot=-2;CancelRespawnPlacement();industry?.StopConstructionTools();return;}
            if(item==equippedConstructionItem&&slot==equippedConstructionSlot)return;
            equippedConstructionItem=item;equippedConstructionSlot=slot;if(item!=null&&item!="wire-tool")shipyard?.CancelTool();CancelRespawnPlacement();industry?.StopConstructionTools();
            if(LocalPlayerDead||!IsFirstPerson||IndustrialDriving||item==null)return;
            if(IndustrialWorld.RespawnItem(item)){respawnView??=new RespawnPointView(this);respawnView.Begin(item);return;}
            if(IndustrialCatalog.MachineForItem(item,out var machine)){industry.EquipMachine(machine);return;}
            if(EquippedBuildingModule(out var kind)){playerConstruction??=new PlayerBuildingWorld(this,surface);playerConstruction.SelectModule(kind);return;}
            if(item=="conveyor-planner"||item=="wire-tool"||item=="pipe-tool"||item=="stockpile-marker")industry.EquipConnectionTool(item);
        }
        internal void ClearConsumedEquipment(){if(!EquipmentInventory)return;bool closing=equipmentMenuWasOpen&&!PersonalMenuOpen;equipmentMenuWasOpen=PersonalMenuOpen;if(PersonalMenuOpen){equippedConstructionItem=null;equippedConstructionSlot=-2;return;}PersonalInventory.ClearMissingSlots();if(PersonalInventory.Equipped==null){CancelRespawnPlacement();industry?.StopConstructionTools();}}
    }
    internal sealed partial class PlanetIndustry {
        internal void UseProtectionItem(string item)=>Execute(Command(item=="radiation-suit"?"protection":"treat"));
        internal void EquipMachine(string machine){palette=Array.IndexOf(buildNames,machine);if(palette<0)return;placingPile=false;placementArmed=true;placementTurn=0;placementInputReady=Time.unscaledTimeAsDouble+.15;nextPlacement=0;}
        internal void EquipConnectionTool(string item){if(world.BuildingPolicyError!=null){message=world.BuildingPolicyError;return;}if(item=="wire-tool"&&world.PlayerForValidation.AttachedToShip){world.BeginShipWiring();return;}if(item=="stockpile-marker"){placingPile=true;placementArmed=true;placementInputReady=Time.unscaledTimeAsDouble+.15;nextPlacement=0;}else StartCableTool(item=="conveyor-planner"?"belt":item=="wire-tool"?"power":"water");}
    }
}

