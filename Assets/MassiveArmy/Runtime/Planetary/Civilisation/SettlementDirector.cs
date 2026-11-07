using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    /// <summary>Small district lists and bounded, staggered infrastructure decisions.</summary>
    public sealed class SettlementDirector {
        internal const int SurveyBudget=64;
        sealed class Totals {
            internal readonly Dictionary<string,int> counts=new Dictionary<string,int>();
            internal int housing;internal double power,demand;internal RtsBuilding root;
            internal int Count(string id)=>counts.TryGetValue(id,out int n)?n:0;
            internal void Add(RtsBuilding b){if(b.definition.id=="command")root=b;counts[b.definition.id]=Count(b.definition.id)+1;housing+=b.definition.housingCapacity*b.level;power+=b.definition.generation;demand+=b.definition.consumption;}
        }
        sealed class Survey {
            internal int cursor,limit,turn,roadCursor,fallbackTurn;internal bool ready;
            internal double next;internal RtsBuilding pendingRoad;
            internal Totals[] current,working;
            internal Survey(int teams){current=New(teams);working=New(teams);}
            internal static Totals[] New(int teams){var result=new Totals[teams];for(int i=0;i<teams;i++)result[i]=new Totals();return result;}
        }
        readonly Dictionary<PlanetRtsBase,Survey> surveys=new Dictionary<PlanetRtsBase,Survey>();
        readonly CivilisationRuntime civilisation;readonly System.Random random=new System.Random(93017);
        static readonly string[] bootstrapFallback={"wind","civil_mine_water","civil_farm_wheat","civil_food_processing","civil_housing","lab"};
        static readonly string[][] DistrictBuildings={
            new[]{"civil_social_home","civil_housing","civil_rental_home"},
            new[]{"civil_farm_wheat","civil_farm_wheat","civil_farm_potatoes","civil_farm_vegetables","civil_beef_shed"},
            new[]{"civil_food_shop","civil_drinks_shop","civil_market","civil_pharmacy","civil_pub","civil_gun_shop","civil_blade_shop"},
            new[]{"civil_food_processing","civil_drinks_plant","civil_fabricator","civil_smelter","civil_butcher","civil_electronics","civil_chemistry","civil_oil_refinery"},
            new[]{"civil_school","civil_council","civil_gathering_camp","lab","advanced_lab","strategic_lab","civil_warehouse","civil_port","civil_freight_station","civil_oil_depot","civil_gas_depot"},
            new[]{"autocannon","factory","at_turret","aa_turret"},
            new[]{"civil_mine_iron","civil_mine_carbon","civil_mine_silicon","civil_mine_copper","civil_mine_methane","civil_oil_well","civil_mine_sulfur"}
        };
        public SettlementDirector(CivilisationRuntime civilisation){this.civilisation=civilisation;}
        public void Tick(PlanetRtsBase local){
            if(civilisation.Settings.regionalSettlements){civilisation.RegionalPlanner.Tick(local);return;}
            if(!local.World.StrategyEnabled)return;
            if(!surveys.TryGetValue(local,out var survey)){survey=new Survey(local.Factions.Length);surveys.Add(local,survey);}
            // Only infrastructure metadata. No CPU army mirror, and no full-city scan in one decision.
            if(survey.cursor==0)survey.limit=local.Buildings.Count;
            if(survey.limit>local.Buildings.Count){survey.cursor=0;survey.limit=local.Buildings.Count;survey.working=Survey.New(local.Factions.Length);}
            int end=Math.Min(survey.limit,survey.cursor+SurveyBudget);
            for(;survey.cursor<end;survey.cursor++){var b=local.Buildings[survey.cursor];if(!b.removed&&(b.node<0||local.Nodes[b.node].remaining>0))survey.working[b.team].Add(b);}
            if(survey.cursor==survey.limit){survey.current=survey.working;survey.working=Survey.New(local.Factions.Length);survey.cursor=0;survey.ready=true;civilisation.PlanRegionalConnections(local);}
            if(!survey.ready||civilisation.SimulationTime<survey.next)return;
            int active=0;for(int i=0;i<local.Factions.Length;i++)if(local.Settlements?[i]!=null&&civilisation.Autonomous(local,i))active++;
            survey.next=civilisation.SimulationTime+Math.Max(.1,civilisation.Settings.strategyTickSeconds/Math.Max(1,active));
            // Service one road endpoint, rather than testing every building's connectivity.
            if(local.Buildings.Count>0){var site=survey.pendingRoad??local.Buildings[survey.roadCursor++%local.Buildings.Count];survey.pendingRoad=null;
                if(!site.removed&&civilisation.Autonomous(local,site.team)&&civilisation.SimulationTime>=site.roadRetryAfter){
                    var root=survey.current[site.team].root;
                    if(root!=null&&!root.removed&&site!=root){
                        if(local.Roads.Connected(site,root)){if(site.roadSuspended){site.roadSuspended=false;site.roadFailures=0;local.NotifyChanged();}}
                        else if(!local.Roads.ConnectToNetwork(site,root,out _))site.roadRetryAfter=civilisation.SimulationTime+60;
                        else if(!local.Roads.Connected(site,root))survey.pendingRoad=site;
                    }
                }
            }
            int team=survey.turn++%local.Factions.Length;for(int skipped=0;skipped<local.Factions.Length-1&&(!civilisation.Autonomous(local,team)||local.Settlements?[team]==null);skipped++)team=survey.turn++%local.Factions.Length;
            if(!civilisation.Autonomous(local,team))return;
            var colony=local.Settlements?[team];if(colony==null)return;
            var totals=survey.current[team];var faction=civilisation.State.Faction(team);
            if(totals.Count("command")==0)return;
            bool Build(string id,bool essential=false){
                if(!local.Catalog.Buildings.TryGetValue(id,out var d))return false;var price=civilisation.Settings.ambientSettlementLife&&(id=="civil_park"||id.StartsWith("civil_farm_"))?new ResourceCost{materials=civilisation.Settings.landTileMaterialCost}:d.cost;if(!(essential?local.Factions[team].wallet.CanAfford(price):local.AiCanSpend(team,price))){faction.reason="Saving resources for "+d.name;return false;}
                faction.priority="Grow "+d.district+" district";faction.reason="Placing nearby "+d.name;
                bool built=(id=="civil_port"||id=="civil_freight_station")?totals.root!=null&&local.BuildTerminalNear(totals.root,id=="civil_port",id=="civil_freight_station"):local.TryConstructInfrastructure(id,team);
                if(built&&civilisation.Settings.ambientSettlementLife&&(id=="civil_park"||id.StartsWith("civil_farm_"))){totals.counts[id]=totals.Count(id)+1;return true;}
                if(built){var added=local.Buildings[local.Buildings.Count-1];totals.Add(added);survey.pendingRoad=added;if(survey.cursor>0&&local.Buildings.Count-1>=survey.limit)survey.working[team].Add(added);}
                else faction.reason="Waiting for a suitable nearby plot or resources";
                return built;
            }
            string required=null;
            if(totals.Count("civil_warehouse")==0)required="civil_warehouse";
            else if(totals.Count("mine")<civilisation.Settings.aiMinimumMaterialMines)required="mine";
            else if(totals.power<totals.demand+15)required="wind";
            else if(totals.Count("civil_mine_water")==0)required="civil_mine_water";
            else if(totals.Count("civil_farm_wheat")==0&&local.LandUse.CropCount(team,"wheat")==0)required="civil_farm_wheat";
            else if(totals.Count("civil_food_processing")==0)required="civil_food_processing";
            else if(totals.housing<Math.Max(colony.population+12,Math.Ceiling(colony.plannedWorkerDemand*1.3)+12))required=DistrictBuildings[0][random.Next(3)];
            else if(colony.population>=24&&totals.Count(CivilisationRuntime.PoliceHeadquarters)==0)required=CivilisationRuntime.PoliceHeadquarters;
            else if(colony.deaths.Count>0&&totals.Count(CivilisationRuntime.Graveyard)==0)required=CivilisationRuntime.Graveyard;
            else if(colony.deaths.Count>0&&totals.Count(CivilisationRuntime.FuneralDirectors)==0)required=CivilisationRuntime.FuneralDirectors;
            else if(totals.Count("lab")==0)required="lab";
            else if(colony.population>=24&&local.LandUse.ParkCount(team)==0)required="civil_park";
            if(required!=null){if(Build(required,true))return;
                // An unavailable deposit/plot must not block every other bootstrap service.
                // One alternate per thought, rotating across a fixed small list.
                string fallback=bootstrapFallback[survey.fallbackTurn++%bootstrapFallback.Length];
                bool needed=fallback=="wind"?totals.power<totals.demand+15:fallback=="civil_housing"?totals.housing<colony.population+12:fallback=="civil_farm_wheat"?local.LandUse.CropCount(team,"wheat")==0&&totals.Count(fallback)==0:totals.Count(fallback)==0;
                if(needed&&fallback!=required&&Build(fallback,true))return;
            }
            int fleet=colony.cargoVehicles.Count+colony.cargoOrders.Count;
            if(!civilisation.Settings.ambientSettlementLife&&fleet<Math.Min(civilisation.Settings.maximumCargoVehicles,Math.Max(3,(colony.population+19)/20))&&local.AiCanSpend(team,new ResourceCost{materials=civilisation.Settings.cargoVehicleMaterialCost})){
                if(civilisation.Logistics.Order(local,team,false))return;
            }
            if(!PlanetRtsBase.DevelopmentWorkforceReady(colony)){faction.reason="Growing housing and waiting for workforce";return;}
            // Essential services above are deterministic; optional growth samples one district/type.
            var choices=DistrictBuildings[1+random.Next(DistrictBuildings.Length-1)];
            string choice=choices[random.Next(choices.Length)];
            if(choice.StartsWith("civil_farm_")&&civilisation.Settings.ambientSettlementLife){if(local.LandUse.CropCount(team,choice.Substring(11))<Math.Max(2,colony.population/12))Build(choice);}else if(totals.Count(choice)<Math.Max(1,(colony.population+79)/80))Build(choice);
            civilisation.Orbitals.Plan(local,team);
        }
    }
}
