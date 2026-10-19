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
                    if(b.roadSuspended)continue;if(b.definition.foodUse>0)destinations++;housing+=b.definition.housingCapacity*b.level;installedPower+=b.definition.generation;futureDemand+=b.definition.consumption;}
                var root=hq??warehouse;bool extended=false,criticalRoad=false;
                if(root!=null&&faction.autonomous){foreach(var site in local.Buildings)if(site.team==team&&!site.removed&&site.roadSuspended){if(civilisation.Logistics.Connected(local,site)){site.roadSuspended=false;site.roadRetryAfter=0;site.roadFailures=0;local.NotifyChanged();}else {extended=local.PlanFreightAccess(site,root);break;}}}
                if(faction.autonomous&&colony.inventory.Get("construction_materials")>civilisation.Settings.aiConstructionReserve*2)foreach(var carrier in colony.cargoVehicles)if(carrier.freight&&carrier.phase==CargoVehiclePhase.Idle&&civilisation.Logistics.UpgradeCarrier(local,team,carrier.id))break;
                if(root!=null&&root.Complete&&(civilisation.Settings.autoExtendSettlementRoads||faction.autonomous)){
                    var pending=new System.Collections.Generic.List<RtsBuilding>();int priority=3;
                    foreach(var b in local.Buildings)if(b.team==team&&!b.removed&&b!=root){if(local.Roads.Connected(b,root)||civilisation.Logistics.Connected(local,b)){if(b.roadSuspended){b.roadSuspended=false;b.roadFailures=0;b.roadRetryAfter=0;local.NotifyChanged();}continue;}if(civilisation.SimulationTime<b.roadRetryAfter)continue;int rank=b.definition.extraction>0||b==warehouse?0:b.definition.id.StartsWith("civil_")?1:2;if(rank<priority){pending.Clear();priority=rank;}if(rank==priority)pending.Add(b);}
                    criticalRoad=pending.Count>0&&priority<2;
                    if(pending.Count>0&&roadBudget>0){roadBudget--;var key=(local.World.definition.bodyId,team);roadCursors.TryGetValue(key,out int cursor);var building=pending[cursor%pending.Count];roadCursors[key]=cursor+1;
                        extended=local.Roads.ConnectToNetwork(building,root,out string reason);
                        if(extended){building.roadFailures=0;building.roadRetryAfter=0;if(local.Roads.Connected(building,root))building.roadSuspended=false;}
                        else if(reason.StartsWith("No dry",StringComparison.Ordinal)&&++building.roadFailures>=3){building.roadSuspended=true;building.roadRetryAfter=civilisation.SimulationTime+60;local.NotifyChanged();}
                        faction.reason=extended?reason:building.definition.name+": "+reason;if(team==0)local.Notice=faction.reason;}
                }
                if(!faction.autonomous||local.World.sessionMode==PlanetGameMode.Swarm)continue;
                bool Build(string id){var definition=local.Catalog.Buildings[id];faction.reason="Constructing "+definition.name;if(local.TryConstructInfrastructure(id,team))return true;
                    bool expanded=definition.terrain==TerrainRule.Node?local.ExpandForDeposit(id,team)||local.SurveyFrontierDeposit(id,team):local.BuildSettlementFrontier(id,team);
                    faction.reason=expanded?"Developing frontier for "+definition.name:local.Factions[team].wallet.CanAfford(definition.cost)?"Seeking a clear serviced site for "+definition.name:"Waiting for construction resources: "+definition.name;return expanded;}
                bool Required(string id){if(local.HasProductiveBuilding(id,team))return false;faction.priority="Secure supply: "+local.Catalog.Buildings[id].name;if(!Build(id))local.ExpandForDeposit(id,team);return true;}
                if(criticalRoad||extended){faction.priority="Connect production to main HQ";continue;}
                if(warehouse==null){faction.priority="Build settlement storage";Build("civil_warehouse");continue;}
                int minimumMines=Math.Max(civilisation.Settings.aiMinimumMaterialMines,(producers+23)/24);
                if(local.ProductiveCount("mine",team)<minimumMines){faction.priority="Establish material production";if(Build("mine")||local.Factions[team].wallet.Stock.materials<civilisation.Settings.aiConstructionReserve*2)continue;}
                // Food-starved existing power/housing is a delivery problem, not a reason to duplicate structures.
                if(installedPower<futureDemand+15){faction.priority="Increase installed electrical capacity";Build("wind");continue;}
                if(colony.cargoVehicles.Count+colony.cargoOrders.Count<Math.Min(civilisation.Settings.maximumCargoVehicles,Math.Max(3,(Math.Max(destinations,producers)+1)/2))){faction.priority="Increase supply transport capacity";if((colony.cargoVehicles.Count+colony.cargoOrders.Count<3||local.AiCanSpend(team,new ResourceCost{materials=civilisation.Settings.cargoVehicleMaterialCost}))&&civilisation.Logistics.Order(local,team,false))continue;}
                if(Required("civil_mine_water")||Required("civil_farm_wheat")||Required("civil_food_processing"))continue;
                if(colony.inventory.Get("propellant")<civilisation.Settings.aiLogisticsFuelReserve*2&&Required("oil"))continue;
                int desiredMines=local.Factions[team].wallet.Stock.materials<civilisation.Settings.aiConstructionReserve*2&&local.MaterialBacklog(team)<civilisation.Settings.cargoCapacity*2?Math.Max(minimumMines,civilisation.Settings.aiMaximumMaterialMines+producers/32):minimumMines;
                if(local.ProductiveCount("mine",team)<desiredMines){faction.priority="Scale material extraction";if((local.AiCanSpend(team,local.Catalog.Buildings["mine"].cost)&&Build("mine"))||local.ExpandForDeposit("mine",team))continue;}
                foreach(string supply in new[]{"civil_mine_water","civil_farm_wheat","civil_food_processing"})if(local.AiSupplyShort(supply,team)&&local.AiCanSpend(team,local.Catalog.Buildings[supply].cost)&&Build(supply)){extended=true;break;}
                if(extended)continue;
                if(housing<Math.Max(colony.population+12,(int)Math.Ceiling(colony.plannedWorkerDemand*1.3)+12)){faction.priority="Provide civilian housing";Build(colony.households.Count%3==0?"civil_social_home":colony.households.Count%3==1?"civil_rental_home":"civil_housing");continue;}
                if(colony.council.treasury>=5&&local.AiEconomyReady(team)){
                    if(colony.council.priority==CivicPriority.Education&&!local.HasProductiveBuilding("civil_school",team)&&local.AiCanSpend(team,local.Catalog.Buildings["civil_school"].cost)&&Build("civil_school"))continue;
                    if(colony.council.priority==CivicPriority.Defence&&local.AiCanSpend(team,local.Catalog.Buildings["autocannon"].cost)){if(!local.HasProductiveBuilding("autocannon",team)&&Build("autocannon"))continue;if(local.BuildLocalDefences(team,0))continue;}
                }
                if(colony.plannedWorkerDemand>colony.workforce){faction.priority="Grow civilian workforce";faction.reason="Waiting for supplied housing: population "+colony.population+", planned jobs "+colony.plannedWorkerDemand;continue;}
                if(local.AiEconomyReady(team)&&(local.PlanRegionalStorage(root)||local.PlanRegionalRail(warehouse??root))){faction.priority="Expand regional freight infrastructure";continue;}
                string[] priorities={"civil_school","civil_council","civil_gathering_camp","civil_mine_water","civil_farm_wheat","civil_food_processing","civil_farm_potatoes","civil_mine_iron","civil_mine_carbon","civil_smelter","civil_mine_silicon","civil_fabricator","civil_beef_shed","civil_butcher","civil_drinks_plant","civil_food_shop","civil_drinks_shop","civil_pharmacy","civil_market","civil_mine_methane","civil_mine_copper","civil_electronics","civil_oil_well","civil_oil_refinery","civil_farm_vegetables","civil_chemistry","civil_mine_sulfur"};
                faction.priority="Balance connected production";faction.reason=$"Materials {local.Factions[team].wallet.Stock.materials:0}; extractors {local.ProductiveCount("mine",team)}; reserve {civilisation.Settings.aiConstructionReserve:0}";
                foreach(string id in priorities)if(local.AiSupplyShort(id,team)&&local.AiCanSpend(team,local.Catalog.Buildings[id].cost)){if(Build(id)||local.ExpandForDeposit(id,team)){extended=true;break;}}
                if(!extended&&colony.workforce>=colony.plannedWorkerDemand+4&&local.AiEconomyReady(team)){
                    string[] enterprises={"civil_farm_wheat","civil_food_processing","civil_gathering_camp","civil_drinks_plant","civil_food_shop","civil_fabricator"};
                    int start=(int)(civilisation.SimulationTime/civilisation.Settings.strategyTickSeconds)%enterprises.Length;
                    for(int i=0;i<enterprises.Length;i++){string id=enterprises[(start+i)%enterprises.Length];if(local.AiCanSpend(team,local.Catalog.Buildings[id].cost)&&Build(id)){faction.priority="Expand local employment";break;}}
                }
            }
        }
    }
}
