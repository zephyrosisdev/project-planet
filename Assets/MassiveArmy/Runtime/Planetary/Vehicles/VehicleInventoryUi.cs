using MassiveArmy.Industrial;
using UnityEngine.InputSystem;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class VehicleController {internal long SurvivalInventoryId;internal SpacePose InteractionPose=>Parking.Parked?Parking.Pose:Motor.Pose;}
    public sealed partial class PlanetVehiclePool {
        internal VehicleController InventoryTarget(){if(Controlled!=null)return Controlled;int index=FindSeat(out _);return index<0?null:vehicles[index];}
        internal VehicleController InventoryVehicle(long id){for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].SurvivalInventoryId==id)return vehicles[i];return null;}
        internal void SyncInventories(){var authority=world.IndustrialAuthority;if(authority==null)return;for(int i=0;i<Count;i++){var vehicle=vehicles[i];var store=authority.World.Machine(vehicle.SurvivalInventoryId);if(store==null)continue;if(!vehicle.Alive){store.integratedLogistics=false;store.displayName=vehicle.Definition.displayName+" cargo wreck";continue;}store.pose=world.RespawnPose(vehicle.InteractionPose.position);store.externalVehicle=(long)vehicle.EntityId+1;}}
    }
    public sealed partial class PlanetDevelopment {
        InteractionHold vehicleMenuHold;
        internal Double3 ControlledInventoryPosition=>groundVehicles?.Controlled?.InteractionPose.position??player.PreciseBodyPosition;
        internal void SyncVehicleInteractionState(){
            if(!IsSurvival||IndustrialAuthority==null)return;groundVehicles?.SyncInventories();var actor=IndustrialAuthority.World.Actor(1);
            if(actor!=null&&player!=null)actor.pose=RespawnPose(IsDriving?ControlledInventoryPosition:IsPiloting?spacecraft.Pose.position:player.PreciseBodyPosition);
        }
        internal bool OpenGroundVehicleInventory(){SyncVehicleInteractionState();if(!EquipmentInventory||groundVehicles==null)return false;var vehicle=groundVehicles.InventoryTarget();if(vehicle==null||!vehicle.Alive)return false;if(IndustrialAuthority.World.Machine(vehicle.SurvivalInventoryId)==null)vehicle.SurvivalInventoryId=IndustrialAuthority.EnsureVehicleInventory(1,(long)vehicle.EntityId+1,RespawnPose(vehicle.InteractionPose.position),vehicle.Definition.displayName);groundVehicles.SyncInventories();industry.OpenContainer(vehicle.SurvivalInventoryId);return true;}
        internal bool DriveInventoryVehicle(long inventory){var vehicle=groundVehicles?.InventoryVehicle(inventory);if(vehicle==null)return false;bool accepted=groundVehicles.Controlled==vehicle?groundVehicles.Exit():groundVehicles.EnterInventoryVehicle(vehicle);if(accepted)CloseIndustryMenu();else SurvivalNotice=groundVehicles.Notice;return accepted;}
        internal void UpdateVehicleInventoryUi(){
            SyncVehicleInteractionState();bool pressed=Keyboard.current?.eKey.isPressed==true;
            bool allowed=IsSurvival&&!LocalPlayerDead&&!Paused&&Application.isFocused&&!PersonalMenuOpen&&!IndustrialPlacementActive&&(IsFirstPerson||IsDriving);
            var vehicle=allowed?groundVehicles?.InventoryTarget():null;long industrial=allowed&&vehicle==null?industry?.VehicleMenuTarget()??0:0;
            long target=vehicle!=null?(long)vehicle.EntityId+1:industrial!=0?-industrial:0;
            if(vehicleMenuHold.Update(Time.unscaledTimeAsDouble,pressed,allowed,target)){
                if(vehicle!=null)OpenGroundVehicleInventory();else if(industrial!=0)industry.OpenContainer(industrial);
            }
        }
    }
    internal sealed partial class PlanetIndustry {internal void OpenContainer(long id){contextTarget=id;contextIsPile=false;OpenMenu("machine");}}
}
