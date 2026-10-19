using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal SurfaceInstallation ConstructionWarehouse(int team,Vector3 point){
            var colony=Settlements?[team];if(colony==null)return null;SurfaceInstallation nearest=null;float best=float.MaxValue;
            foreach(var building in Buildings)if(!building.removed&&building.team==team&&building.Complete&&building.definition.id=="civil_warehouse"){
                float distance=Distance(point,CubeSphere.Decode(building.position));if(distance>=best)continue;var item=SettlementLogistics.Find(colony,building.persistentId);if(item!=null){best=distance;nearest=item;}}
            return nearest;
        }
        internal bool ConstructionCost(int team,Vector3 point,ResourceCost cost,bool spend){
            var warehouse=ConstructionWarehouse(team,point);if(warehouse==null||ReferenceEquals(warehouse.inventory,Settlements?[team]?.inventory))return spend?Factions[team].wallet.Spend(cost):Factions[team].wallet.CanAfford(cost);
            for(int index=0;index<4;index++){string resource=null;foreach(var definition in Civilisation.Catalogue.resources)if(definition.legacy==index){resource=definition.id;break;}if(resource!=null&&warehouse.inventory.Get(resource)+1e-6<cost[index])return false;}
            if(Factions[team].wallet.Stock.research<cost.research)return false;if(!spend)return true;
            for(int index=0;index<4;index++)foreach(var definition in Civilisation.Catalogue.resources)if(definition.legacy==index){warehouse.inventory.Spend(definition.id,cost[index]);break;}
            return Factions[team].wallet.Spend(new ResourceCost{research=cost.research});
        }
        internal void RefundConstruction(int team,Vector3 point,ResourceCost cost){var warehouse=ConstructionWarehouse(team,point);if(warehouse==null||ReferenceEquals(warehouse.inventory,Settlements?[team]?.inventory)){Factions[team].wallet.Credit(cost);return;}
            for(int index=0;index<4;index++)foreach(var definition in Civilisation.Catalogue.resources)if(definition.legacy==index){warehouse.inventory.Deposit(definition.id,cost[index]);break;}Factions[team].wallet.Credit(new ResourceCost{research=cost.research});
        }
        internal bool PlanRegionalStorage(RtsBuilding root){
            if(root==null)return false;var colony=Settlements[root.team];
            foreach(var site in Buildings)if(site.team==root.team&&!site.removed&&site.Complete&&!site.roadSuspended&&site.definition.extraction>0&&Distance(CubeSphere.Decode(root.position),CubeSphere.Decode(site.position))>600){
                RtsBuilding nearby=null;foreach(var building in Buildings)if(building.team==root.team&&!building.removed&&building.definition.id=="civil_warehouse"&&Distance(CubeSphere.Decode(site.position),CubeSphere.Decode(building.position))<350){nearby=building;break;}
                if(nearby==null){var area=Area(site);var definition=Catalog.Buildings["civil_warehouse"];for(int attempt=0;attempt<8;attempt++){float angle=attempt*2.3999632f;var point=Grid.Point(area.face,area.x+area.width*.5+Math.Cos(angle)*8,area.y+area.depth*.5+Math.Sin(angle)*8);if(ConstructForTeam(definition.id,root.team,point))return true;}return false;}
                if(nearby.Complete&&Civilisation.Logistics.Connected(this,nearby)){int count=0;foreach(var vehicle in colony.cargoVehicles)if(vehicle.warehouse==nearby.persistentId&&!vehicle.freight)count++;foreach(var order in colony.cargoOrders)if(order.warehouse==nearby.persistentId&&!order.freight)count++;if(count<2&&Civilisation.Logistics.OrderRegionalCart(this,root.team,nearby.persistentId))return true;}
            }return false;
        }
    }
}
