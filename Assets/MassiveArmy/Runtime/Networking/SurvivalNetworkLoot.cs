using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        FramePose? ResolveNetworkInventoryPose(FramePose pose){
            if(pose.frame==0)return pose;
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==pose.frame&&s.pose.body==pose.body);if(ship==null)return null;
            var p=SurvivalVehicleSimulation.Position(ship.pose)+Double3.Rotate(ShipRotation(ship),new Double3(pose.x,pose.y,pose.z));
            return new FramePose{body=pose.body,x=p.x,y=p.y,z=p.z};
        }
        void DropDeadSurvivor(SurvivalPlayerRecord player){
            var actor=authority.World.Actor(player.actor);if(actor.health>0)return;
            var canonical=actor.pose;
            if(player.attachedShip!=0){var ship=authority.World.multiplayer.ships.Find(s=>s.entity==player.attachedShip);if(ship!=null){var vehicle=player.controlledEntity==0?null:authority.World.multiplayer.vehicles.Find(v=>v.entity==player.controlledEntity&&v.pose.frame==ship.entity);var seat=vehicle!=null?VehicleSeatLocal(vehicle):player.interiorWalking?player.interiorPose:SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),player.shipSeat);canonical=new FramePose{body=ship.pose.body,frame=ship.entity,x=seat.x,y=seat.y,z=seat.z};}}
            authority.DropSurvivorInventory(actor.id,canonical,actor.pose);
        }
        void ProcessNetworkDeaths(){
            foreach(var player in authority.World.multiplayer.players)DropDeadSurvivor(player);
        }
        void GroundLootGUI(){
            foreach(var entity in visible.Values)if(entity.kind=="loot"&&latest.self.pose.Distance(entity.pose)<=12){
                var drop=JsonUtility.FromJson<DroppedInventory>(entity.data);GUILayout.Label("RECOVERABLE CONTAINER #"+drop.id);
                foreach(var stack in drop.inventory.items)if(GUILayout.Button("Take "+stack.item+" ×"+stack.count)){
                    int destination=-1;foreach(var item in latest.self.inventory.items)if(item.item==stack.item&&item.count<Inventory.StackLimit(item.item)){destination=item.slot;break;}
                    if(destination<0)for(int slot=0;slot<latest.self.inventory.slotLimit;slot++)if(latest.self.inventory.AtSlot(slot)==null){destination=slot;break;}
                    if(destination<0){Status="Make room in your inventory";continue;}
                    Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{section="ground",entity=drop.id,slot=stack.slot},to=new InventoryAddress{slot=destination},count=stack.count});
                }
                if(drop.inventory.bulk.litres>0&&GUILayout.Button("Take pay dirt ×"+drop.inventory.bulk.litres))Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{section="ground",entity=drop.id,slot=-2},to=new InventoryAddress{slot=-2},count=drop.inventory.bulk.litres});
            }
        }
    }
}
