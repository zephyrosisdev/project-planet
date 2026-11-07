using System;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementLogistics {
        void OrderDepotTankers(PlanetRtsBase local,ColonyState colony){
            foreach(var depot in colony.installations){if(CivilisationRuntime.DepotResource(depot.definition)==null)continue;
                var building=Building(local,depot.id);if(building==null||!building.Complete||!Connected(local,building))continue;
                bool exists=colony.cargoVehicles.Exists(x=>x.warehouse==depot.id)||colony.cargoOrders.Exists(x=>x.warehouse==depot.id);if(exists||colony.cargoVehicles.Count+colony.cargoOrders.Count>=civilisation.Settings.maximumCargoVehicles)continue;
                var producer=local.Buildings.Find(x=>x.team==colony.faction&&!x.removed&&x.Complete&&x.powered&&x.definition.id=="light_depot");
                if(producer==null||!local.Roads.Connected(producer,building)||colony.inventory.Get("construction_materials")<civilisation.Settings.aiConstructionReserve+civilisation.Settings.cargoVehicleMaterialCost)continue;
                if(colony.inventory.Spend("construction_materials",civilisation.Settings.cargoVehicleMaterialCost))colony.cargoOrders.Add(new CargoVehicleOrder{producer=producer.persistentId,warehouse=depot.id});
            }
        }
        bool AssignTanker(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation depot,RtsBuilding home,string resource){
            foreach(var item in colony.installations){if(item.id==depot.id||Busy(colony,item.id,resource))continue;var b=Building(local,item.id);if(b==null||!b.Complete)continue;
                var recipe=DispatchRecipe(item);if(recipe!=null)foreach(var input in recipe.inputs)if(input.resource==resource){double wanted=InputTarget(input,recipe,b,SupplyHorizon(local,home,b,false))-item.inventory.Get(resource);
                    if(wanted>1&&depot.inventory.Get(resource)>0&&Start(local,vehicle,home,b,resource,Math.Min(wanted,depot.inventory.Get(resource)),false,colony))return true;}
            }
            double room=depot.inventory.Capacity(resource)-depot.inventory.Get(resource);if(room<1)return false;
            foreach(var item in colony.installations){var b=Building(local,item.id);if(b==null||!b.Complete||Busy(colony,item.id,resource)||!(b.definition.extractedResource==resource||b.definition.id=="civil_port"))continue;
                double amount=Math.Min(room,item.inventory.Get(resource));if(amount>.1&&Start(local,vehicle,home,b,resource,amount,true,colony))return true;
            }return false;
        }
    }
}
