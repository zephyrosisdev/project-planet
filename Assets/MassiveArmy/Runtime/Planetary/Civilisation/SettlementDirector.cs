using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    /// <summary>Paid, bounded settlement planning; HQ road connectivity and supply precede military spending.</summary>
    public sealed class SettlementDirector {
        int roadTurn;readonly System.Collections.Generic.Dictionary<(int,int),int> roadCursors=new System.Collections.Generic.Dictionary<(int,int),int>();
        readonly CivilisationRuntime civilisation;readonly System.Collections.Generic.Dictionary<int,double> nextThoughts=new System.Collections.Generic.Dictionary<int,double>();
        public SettlementDirector(CivilisationRuntime civilisation){this.civilisation=civilisation;}
        public void Tick(PlanetRtsBase local){if(!local.World.StrategyEnabled||nextThoughts.TryGetValue(local.World.definition.bodyId,out double next)&&civilisation.SimulationTime<next)return;nextThoughts[local.World.definition.bodyId]=civilisation.SimulationTime+civilisation.Settings.strategyTickSeconds;
            int roadBudget=Mathf.Max(1,civilisation.Settings.roadExtensionsPerTick),first=roadTurn++&1;
            for(int slot=0;slot<2;slot++){int team=(first+slot)&1;var faction=civilisation.State.Faction(team);var colony=civilisation.Colony(civilisation.BodyId(local.World.definition.bodyId),team);if(colony==null)continue;
                RtsBuilding warehouse=null,hq=null;int producers=0,housing=0,destinations=0;double installedPower=0,futureDemand=0;
                foreach(var b in local.Buildings)if(b.team==team&&!b.removed){if(warehouse==null&&b.definition.id=="civil_warehouse")warehouse=b;if(b.definition.id=="command")hq=b;if(b.definition.extraction>0||b.definition.production.Length>0||b.definition.id.StartsWith("civil_"))producers++;
                    if(b.definition.foodUse>0)destinations++;housing+=b.definition.housingCapacity*b.level;installedPower+=b.definition.generation;futureDemand+=b.definition.consumption;}
                var root=hq??warehouse;bool extended=false,criticalRoad=false;
                if(root!=null&&root.Complete&&(civilisation.Settings.autoExtendSettlementRoads||faction.autonomous)){
                    var pending=new System.Collections.Generic.List<RtsBuilding>();int priority=3;
                    foreach(var b in local.Buildings)if(b.team==team&&!b.removed&&b!=root&&!local.Roads.Connected(b,root)){int rank=b.definition.extraction>0||b==warehouse?0:b.definition.id.StartsWith("civil_")?1:2;if(rank<priority){pending.Clear();priority=rank;}if(rank==priority)pending.Add(b);}
                    criticalRoad=pending.Count>0&&priority<2;
                    if(pending.Count>0&&roadBudget>0){roadBudget--;var key=(local.World.definition.bodyId,team);roadCursors.TryGetValue(key,out int cursor);var building=pending[cursor%pending.Count];roadCursors[key]=cursor+1;
                        extended=local.Roads.ConnectToNetwork(building,root,out string reason);faction.reason=extended?"HQ road connected: "+building.definition.name:building.definition.name+": "+reason;if(team==0)local.Notice=faction.reason;}
                }
                if(!faction.autonomous||local.World.sessionMode==PlanetGameMode.Swarm)continue;
                bool Build(string id){faction.reason="Constructing "+local.Catalog.Buildings[id].name;return local.TryConstructInfrastructure(id,team);}
                bool Required(string id){if(local.HasProductiveBuilding(id,team))return false;faction.priority="Secure supply: "+local.Catalog.Buildings[id].name;if(!Build(id))local.ExpandForDeposit(id,team);return true;}
                if(criticalRoad||extended){faction.priority="Connect production to main HQ";continue;}
                if(warehouse==null){faction.priority="Build settlement storage";Build("civil_warehouse");continue;}
                if(local.ProductiveCount("mine",team)<civilisation.Settings.aiMinimumMaterialMines){faction.priority="Establish material production";if(!Build("mine"))local.ExpandForDeposit("mine",team);continue;}
                // Food-starved existing power/housing is a delivery problem, not a reason to duplicate structures.
                if(installedPower<futureDemand+15){faction.priority="Increase installed electrical capacity";Build("wind");continue;}
                if(colony.cargoVehicles.Count+colony.cargoOrders.Count<Math.Min(civilisation.Settings.maximumCargoVehicles,Math.Max(3,(Math.Max(destinations,producers)+1)/2))){faction.priority="Increase supply transport capacity";if((colony.cargoVehicles.Count+colony.cargoOrders.Count<3||local.AiCanSpend(team,new ResourceCost{materials=civilisation.Settings.cargoVehicleMaterialCost}))&&civilisation.Logistics.Order(local,team,false))continue;}
                if(Required("civil_mine_water")||Required("civil_farm_wheat")||Required("civil_food_processing"))continue;
                if(colony.inventory.Get("propellant")<civilisation.Settings.aiLogisticsFuelReserve*2&&Required("oil"))continue;
                int desiredMines=local.Factions[team].wallet.Stock.materials<civilisation.Settings.aiConstructionReserve*2&&local.MaterialBacklog(team)<civilisation.Settings.cargoCapacity*2?civilisation.Settings.aiMaximumMaterialMines:civilisation.Settings.aiMinimumMaterialMines;
                if(local.ProductiveCount("mine",team)<desiredMines){faction.priority="Scale material extraction";if((local.AiCanSpend(team,local.Catalog.Buildings["mine"].cost)&&Build("mine"))||local.ExpandForDeposit("mine",team))continue;}
                if(housing<Math.Max(colony.population+8,colony.plannedWorkerDemand+8)){faction.priority="Provide civilian housing";Build("civil_housing");continue;}
                foreach(string supply in new[]{"civil_mine_water","civil_farm_wheat","civil_food_processing"})if(local.AiSupplyShort(supply,team)&&local.AiCanSpend(team,local.Catalog.Buildings[supply].cost)&&Build(supply)){extended=true;break;}
                if(extended)continue;
                if(colony.plannedWorkerDemand>colony.workforce){faction.priority="Grow civilian workforce";faction.reason="Waiting for supplied housing: population "+colony.population+", planned jobs "+colony.plannedWorkerDemand;continue;}
                string[] priorities={"civil_mine_water","civil_farm_wheat","civil_food_processing","civil_farm_potatoes","civil_mine_iron","civil_mine_carbon","civil_smelter","civil_mine_silicon","civil_fabricator","civil_beef_shed","civil_butcher","civil_drinks_plant","civil_food_shop","civil_drinks_shop","civil_pharmacy","civil_market","civil_mine_methane","civil_mine_copper","civil_electronics","civil_oil_well","civil_oil_refinery","civil_farm_vegetables","civil_chemistry","civil_mine_sulfur"};
                faction.priority="Balance connected production";faction.reason=$"Materials {local.Factions[team].wallet.Stock.materials:0}; extractors {local.ProductiveCount("mine",team)}; reserve {civilisation.Settings.aiConstructionReserve:0}";
                foreach(string id in priorities)if(local.AiSupplyShort(id,team)&&local.AiCanSpend(team,local.Catalog.Buildings[id].cost)){if(Build(id)||local.ExpandForDeposit(id,team))break;}
            }
        }
    }
}
