using System;
using System.Linq;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Only bounded settlement metadata is inspected; army and cargo routing remain on the GPU.
    public sealed partial class PlanetRtsBase {
        internal int ProductiveCount(string id,int team)=>Buildings.Count(b=>b.team==team&&!b.removed&&!b.roadSuspended&&b.definition.id==id&&(b.node<0||Nodes[b.node].remaining>0));
        internal bool AiCanSpend(int team,ResourceCost cost)=>Factions[team].wallet.CanAfford(cost)&&(Civilisation==null||(Factions[team].wallet.Stock.materials-cost.materials>=Civilisation.Settings.aiConstructionReserve&&Factions[team].wallet.Stock.fuel-cost.fuel>=Civilisation.Settings.aiLogisticsFuelReserve));
        // Spare vacancies in a growing town must not block research and all new industries.
        internal static bool DevelopmentWorkforceReady(ColonyState colony)=>colony.workforce>=Math.Ceiling(colony.plannedWorkerDemand*.8);
        internal bool AiEconomyReady(int team,bool requireWorkforce=true){
            if(Civilisation==null)return true;var settlement=Settlements?[team];if(requireWorkforce&&settlement!=null&&!DevelopmentWorkforceReady(settlement))return false;if(Factions[team].wallet.Stock.materials<Civilisation.Settings.aiConstructionReserve*(requireWorkforce?2:1))return false;var hq=Buildings.Find(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.id=="command");if(hq==null||Roads==null)return false;
            int mines=0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&b.Complete&&b.definition.id=="mine"&&b.node>=0&&Nodes[b.node].remaining>0&&(Roads.Connected(b,hq)||Civilisation.Logistics.Connected(this,b)))mines++;
            if(mines<Civilisation.Settings.aiMinimumMaterialMines||Settlements?[team]?.cargoVehicles.Count<3)return false;
            foreach(string id in new[]{"civil_mine_water","civil_farm_wheat","civil_food_processing"})if(!Buildings.Exists(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.id==id&&(Roads.Connected(b,hq)||Civilisation.Logistics.Connected(this,b))))return false;
            return true;
        }
        internal double MaterialBacklog(int team){double stock=0;var colony=Settlements?[team];if(colony==null)return 0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&b.definition.id=="mine"){var installation=colony.installations.Find(i=>i.id==b.persistentId);if(installation!=null)stock+=installation.inventory.Get("construction_materials");}return stock;}
        internal bool ExpandForDeposit(string id,int team){
            var definition=Catalog.Buildings[id];if(CivilianExpansion(definition))return false;if(definition.terrain!=TerrainRule.Node||Buildings.Exists(b=>b.team==team&&!b.removed&&!b.Complete&&b.definition.buildRadius>0))return false;
            var budget=definition.cost;budget.materials+=Catalog.Buildings["outpost"].cost.materials+80;
            budget.materials+=Civilisation.Settings.aiConstructionReserve;if(!Factions[team].wallet.CanAfford(budget))return false;
            foreach(var node in Nodes.Where(n=>n.claimedBy<0&&n.remaining>0&&Array.IndexOf(definition.nodes,n.kind)>=0&&string.Equals(n.elementId??"",definition.extractedResource??"",StringComparison.Ordinal)).OrderBy(n=>Distance(Homes[team],CubeSphere.Decode(n.position)))){
                var target=CubeSphere.Decode(node.position);if(!OwnDeposit(team,target))continue;
                var closest=Buildings.Where(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.buildRadius>0).OrderBy(b=>Distance(CubeSphere.Decode(b.position),target)).FirstOrDefault();if(closest==null)continue;
                var start=CubeSphere.Decode(closest.position);float distance=Distance(start,target);if(distance<=closest.definition.buildRadius)continue;
                var next=Vector3.Slerp(start,target,Mathf.Min(.7f,(closest.definition.buildRadius-40)/distance));var east=Vector3.Cross(Mathf.Abs(next.y)>.95f?Vector3.right:Vector3.up,next).normalized;
                for(int attempt=0;attempt<8;attempt++){float angle=attempt*2.3999632f,r=attempt==0?0:12+attempt*2;var spot=(next+(east*Mathf.Cos(angle)+Vector3.Cross(next,east)*Mathf.Sin(angle))*r/fixture.definition.radius).normalized;if(ConstructForTeam("outpost",team,spot))return true;}
            }return false;
        }
        IndustryRecipe AiRecipe(RtsBuilding building){var installation=Settlements?[building.team]?.installations.Find(i=>i.id==building.persistentId);IndustryRecipe fallback=null;
            foreach(var recipe in Civilisation.Catalogue.recipes)if(recipe.building==building.definition.id){if(recipe.id==installation?.recipe)return recipe;if(fallback==null)fallback=recipe;}return fallback;}
        double AiInputDemand(string resource,int team){double demand=resource=="food"?(Settlements?[team]?.population??0)*Civilisation.Settings.populationFoodPerSecond:0;
            foreach(var b in Buildings)if(b.team==team&&!b.removed&&!b.roadSuspended){if(resource=="food")demand+=b.definition.foodUse*b.level;if(b.definition.retailResource==resource)demand+=Math.Min(Settlements?[team]?.population??0,100*b.level)*b.definition.retailPerResident;var recipe=AiRecipe(b);if(recipe!=null)foreach(var input in recipe.inputs)if(input.resource==resource)demand+=input.amount/recipe.seconds*b.level;
                if(resource=="water"&&b.definition.id.StartsWith("civil_farm_"))foreach(var crop in Civilisation.Catalogue.crops)if("civil_farm_"+crop.id==b.definition.id)demand+=crop.waterPerSecond*b.level;}
            return demand;
        }
        internal bool AiSupplyShort(string id,int team){int count=ProductiveCount(id,team);if(count==0)return true;
            var d=Catalog.Buildings[id];var colony=Settlements?[team];if(colony==null)return false;
            if(!string.IsNullOrEmpty(d.retailResource)){int capacity=0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&!b.roadSuspended&&b.definition.id==id)capacity+=100*b.level;return capacity<colony.population;}
            if(id=="civil_fabricator")return Factions[team].wallet.Stock.materials<Civilisation.Settings.aiConstructionReserve*2&&count<Math.Max(1,(colony.population+colony.plannedWorkerDemand+179)/180);
            if(d.terrain==TerrainRule.Node&&!string.IsNullOrEmpty(d.extractedResource)){double rate=0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&!b.roadSuspended&&b.definition.id==id&&b.node>=0&&Nodes[b.node].remaining>0)rate+=d.extraction*Nodes[b.node].rateMultiplier*b.level;
                double demand=AiInputDemand(d.extractedResource,team);return rate<demand*1.25&&colony.inventory.Get(d.extractedResource)<Math.Max(10,demand*Civilisation.Settings.logisticsReserveSeconds);}
            foreach(var crop in Civilisation.Catalogue.crops)if(id=="civil_farm_"+crop.id){foreach(var harvest in crop.harvest){double demand=AiInputDemand(harvest.resource,team);double capacity=0;foreach(var field in Buildings)if(field.team==team&&!field.removed&&!field.paused&&!field.roadSuspended&&field.definition.id==id){var parcel=colony.installations.Find(i=>i.id==field.persistentId);capacity+=harvest.amount/crop.growthSeconds*.8*field.level*Math.Max(.2,parcel?.soilFertility??1);}if(capacity<demand*1.25&&colony.inventory.Get(harvest.resource)<Math.Max(10,demand*Civilisation.Settings.logisticsReserveSeconds))return true;}return false;}
            var sample=Buildings.Find(b=>b.team==team&&!b.removed&&!b.roadSuspended&&b.definition.id==id);var recipe=sample==null?null:AiRecipe(sample);if(recipe!=null)foreach(var output in recipe.outputs){double demand=AiInputDemand(output.resource,team);double rate=0;foreach(var b in Buildings)if(b.team==team&&!b.removed&&!b.paused&&!b.roadSuspended&&b.definition.id==id)rate+=b.level*output.amount/recipe.seconds;if(rate<demand*1.25&&colony.inventory.Get(output.resource)<Math.Max(10,demand*Civilisation.Settings.logisticsReserveSeconds))return true;}return false;
        }
    }
}
