using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementLogistics {
        // Reroute only after a GPU arrival, using historical building coordinates. Never download vehicle poses.
        bool RedirectArrivedCargo(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,double now){
            if(vehicle.freight)return false;long endpoint=vehicle.phase==CargoVehiclePhase.Returning?vehicle.warehouse:vehicle.destination;
            var from=local.FindBuilding(endpoint,true);if(from==null)return false;
            bool rehome=Building(local,vehicle.warehouse)==null;RtsBuilding best=null;double bestScore=double.MaxValue;
            foreach(var candidate in local.Buildings){if(candidate.removed||candidate.team!=colony.faction||candidate.persistentId==endpoint||(!candidate.Complete&&!candidate.awaitingProjectMaterials))continue;
                var item=Find(colony,candidate.persistentId);if(item==null||!vehicle.aerial&&!local.Roads.Connected(from,candidate))continue;bool storage=Storage(item);if(rehome&&!storage)continue;
                double wanted=0;int priority=4;
                if(storage){wanted=item.inventory.Capacity(vehicle.resource)-item.inventory.Get(vehicle.resource);}
                else if(candidate.awaitingProjectMaterials){foreach(var input in local.ProjectMaterials(candidate))if(input.resource==vehicle.resource)wanted=input.amount-item.inventory.Get(vehicle.resource);priority=1;}
                else if(vehicle.resource=="food"){double upkeep=candidate.definition.foodUse*candidate.level+(candidate.definition.housingCapacity>0?Math.Min(colony.population,candidate.definition.housingCapacity*candidate.level)*civilisation.Settings.populationFoodPerSecond:0);wanted=FoodTarget(upkeep,SupplyHorizon(local,from,candidate,vehicle.aerial))-item.inventory.Get("food");priority=CivilisationRuntime.RecoveryEssential(candidate.definition)?0:2;}
                else {foreach(var recipe in civilisation.Catalogue.recipes)if(recipe.id==item.recipe&&recipe.building==item.definition)foreach(var input in recipe.inputs)if(input.resource==vehicle.resource)wanted=InputTarget(input,recipe,candidate,SupplyHorizon(local,from,candidate,vehicle.aerial))-item.inventory.Get(vehicle.resource);priority=CivilisationRuntime.RecoveryEssential(candidate.definition)?0:3;}
                foreach(var other in colony.cargoVehicles)if(other.id!=vehicle.id&&other.phase!=CargoVehiclePhase.Idle&&other.destination==candidate.persistentId&&other.resource==vehicle.resource)wanted-=other.phase==CargoVehiclePhase.Collecting?other.requested:other.loaded+other.buffered+other.transferred;
                if(wanted<Math.Max(.01,Math.Min(vehicle.loaded,1)))continue;
                double score=priority*1e9+CubeSphere.ArcDistance(CubeSphere.Decode(from.position),CubeSphere.Decode(candidate.position),local.World.definition.radius);if(score>=bestScore)continue;bestScore=score;best=candidate;
            }
            if(best==null){vehicle.arrival=now+civilisation.Settings.economyTickSeconds;return false;}
            if(rehome)vehicle.warehouse=best.persistentId;vehicle.source=endpoint;vehicle.destination=best.persistentId;vehicle.phase=CargoVehiclePhase.Delivering;vehicle.path.Clear();vehicle.path.Add(from.position);vehicle.path.Add(best.position);vehicle.departure=now;vehicle.arrival=now+MissionTime(local,vehicle);vehicle.navigationVersion=0;return true;
        }
    }
}
