using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsHud {
        bool resourcesView;
        void OpenResources(){residentsView=false;resourcesView=true;if(civilisationOpen){civilisationOpen=false;}ToggleCivilisation();}
        bool civilisationOpen;double nextCivilisationRefresh;VisualElement civilisationContent;int inspectedFaction;string inspectedPlanet;
        void ToggleCivilisation(){civilisationOpen=!civilisationOpen;technologyOpen=false;var modal=front.Q("modal");modal.Clear();modal.style.display=civilisationOpen?DisplayStyle.Flex:DisplayStyle.None;if(!civilisationOpen)return;
            var panel=new VisualElement();panel.AddToClassList("panel");panel.style.width=1000;panel.style.maxHeight=700;modal.Add(panel);Label(panel,"RESOURCES & SETTLEMENT ECONOMY","heading");
            var actions=new VisualElement();actions.AddToClassList("row");panel.Add(actions);Button(actions,"CLOSE",ToggleCivilisation);Button(actions,"PEOPLE & COUNCIL",()=>{residentsView=true;resourcesView=false;nextCivilisationRefresh=0;});Button(actions,"ALL RESOURCES",()=>{residentsView=false;resourcesView=true;nextCivilisationRefresh=0;});Button(actions,"INDUSTRY & WORLDS",()=>{residentsView=false;resourcesView=false;nextCivilisationRefresh=0;});if(fixture.sessionMode==PlanetGameMode.AiTest){Button(actions,"CYAN",()=>{inspectedFaction=0;nextCivilisationRefresh=0;});Button(actions,"RED",()=>{inspectedFaction=1;nextCivilisationRefresh=0;});Label(panel,"OBSERVER · TRUE UNIVERSE INFORMATION","muted");}else{inspectedFaction=0;Label(panel,"FACTION KNOWLEDGE · undiscovered worlds and enemy inventories remain hidden","muted");}
            civilisationContent=new ScrollView();civilisationContent.style.height=570;panel.Add(civilisationContent);nextCivilisationRefresh=0;RefreshCivilisation();
        }
        void RefreshCivilisation(){
            if(!civilisationOpen||civilisationContent==null||world.Civilisation==null||Time.realtimeSinceStartupAsDouble<nextCivilisationRefresh)return;nextCivilisationRefresh=Time.realtimeSinceStartupAsDouble+1;
            var simulation=world.Civilisation;var state=simulation.State;var faction=state.Faction(inspectedFaction);bool observer=fixture.sessionMode==PlanetGameMode.AiTest;float scroll=((ScrollView)civilisationContent).scrollOffset.y;civilisationContent.Clear();
            if(residentsView){RefreshResidents(simulation,scroll);return;}
            if(resourcesView){RefreshResources(simulation,scroll);return;}
            if(simulation.CatchingUp(fixture.definition.bodyId))Label(civilisationContent,"CATCHING UP DORMANT SETTLEMENT ECONOMY","heading");Label(civilisationContent,faction.name+" · "+faction.priority,"subheading");Label(civilisationContent,faction.reason,"muted");
            foreach(var colony in state.colonies){if(colony.faction!=inspectedFaction)continue;Label(civilisationContent,colony.name.ToUpperInvariant(),"heading");
                Label(civilisationContent,$"POPULATION {colony.population} · HOUSING {colony.housedPopulation}/{colony.housingCapacity} · WORKERS {colony.employed}/{colony.workforce} · JOBS {colony.workerDemand} · {colony.stage}","body");
                Label(civilisationContent,$"POWER {colony.powerGeneration:0.0} / {colony.powerDemand:0.0} · BALANCE {colony.powerBalance:+0.0;-0.0;0}\nFOOD DEMAND {colony.foodDemand:0.000}/s · DELIVERED {colony.foodSupplied:0.000}/s · {(colony.foodBalance<-.0001?"SHORTAGE":"SUPPLIED")}","body");
                Label(civilisationContent,$"WAREHOUSE · {colony.cargoVehicles.Count} cargo vehicles · {colony.cargoOrders.Count} in production","subheading");
                foreach(var resource in simulation.Catalogue.resources){double amount=colony.inventory.Get(resource.id);if(amount>0)Label(civilisationContent,resource.name+": "+amount.ToString("0.0"),"body");}
                foreach(var installation in colony.installations){var definition=world.Catalog.Buildings[installation.definition];string info=definition.name+(string.IsNullOrEmpty(installation.recipe)?"":" · "+installation.recipe);
                    if(!string.IsNullOrEmpty(installation.crop))info+=$" · {installation.growth:P0} grown · soil {installation.soilFertility:P0} · {installation.harvests} harvests";Label(civilisationContent,info,"muted");
                    if(!ReferenceEquals(installation.inventory,colony.inventory))foreach(var resource in installation.inventory.stock)if(resource.amount>.001)Label(civilisationContent,"  Local "+resource.resource+": "+resource.amount.ToString("0.0"),"body");
                }
                foreach(var warehouse in colony.installations)if(warehouse.definition=="civil_warehouse")Label(civilisationContent,$"REGIONAL WAREHOUSE #{warehouse.id} · materials {warehouse.inventory.Get("construction_materials"):0.0} · food {warehouse.inventory.Get("food"):0.0} · fuel {warehouse.inventory.Get("propellant"):0.0}","body");
                FreightFleet(simulation,colony);
            }
            Label(civilisationContent,"PLANETARY KNOWLEDGE","heading");
            var choices=new System.Collections.Generic.List<string>();foreach(var planet in state.planets)if(observer||faction.knowledge.Knows(planet.body))choices.Add(planet.body);if(!choices.Contains(inspectedPlanet))inspectedPlanet=choices.Count>0?choices[0]:null;
            if(choices.Count>0){var selector=new DropdownField("Known worlds",choices,choices.IndexOf(inspectedPlanet));selector.RegisterValueChangedCallback(change=>{inspectedPlanet=change.newValue;nextCivilisationRefresh=0;});civilisationContent.Add(selector);}
            foreach(var planet in state.planets){if(planet.body!=inspectedPlanet)continue;var known=faction.knowledge.Find(planet.body);if(!observer&&known==null)continue;Label(civilisationContent,planet.body+" · "+(observer?planet.composition.archetype:known.level+" · confidence "+known.confidence.ToString("P0")),"subheading");
                if(observer)foreach(var abundance in planet.composition.abundance)Label(civilisationContent,abundance.resource+" "+abundance.amount.ToString("P0"),"muted");
                else foreach(var estimate in known.resources)Label(civilisationContent,estimate.resource+" "+estimate.estimate.ToString("P0")+" ±"+estimate.uncertainty.ToString("P0"),"muted");
            }
        }
        void FreightFleet(CivilisationRuntime simulation,ColonyState colony){
            foreach(var site in colony.protectionSites)if(!site.finished&&!site.cancelled)Label(civilisationContent,$"DEFENCE / RECOVERY · {site.definition} · {site.status}","muted");
            Label(civilisationContent,"CARGO FLEET · FINITE LOADS","subheading");
            Label(civilisationContent,"Base capacity: carts 80 · aircraft 160 · ships 240 · trains 480. Ships are slower than aircraft; rail requires connected tracks. Freight tiers double capacity; paid upgrades add 25% per level. Road legs transfer cargo at terminals.","muted");
            bool editable=fixture.sessionMode!=PlanetGameMode.AiTest&&colony.faction==0&&colony.body==simulation.BodyId(fixture.definition.bodyId);
            if(editable){var row=new VisualElement();row.AddToClassList("row");civilisationContent.Add(row);Button(row,"ORDER FREIGHT TRAIN",()=>{world.Notice=simulation.Logistics.OrderFreight(world,0,false,true)?"Freight train ordered":"Requires a connected freight station, fleet capacity and local materials";nextCivilisationRefresh=0;});Button(row,"ORDER CARGO SHIP",()=>{world.Notice=simulation.Logistics.OrderFreight(world,0,true)?"Cargo ship service ordered":"Requires a connected cargo port, fleet capacity and materials";nextCivilisationRefresh=0;});Button(row,"ORDER FREIGHT AIRCRAFT",()=>{world.Notice=simulation.Logistics.OrderFreight(world,0,false)?"Freight aircraft service ordered":"Requires a connected freight airport, fleet capacity and materials";nextCivilisationRefresh=0;});}
            foreach(var carrier in colony.cargoVehicles){string kind=carrier.train?"Freight train":carrier.seaborne?"Cargo ship":carrier.freight?"Freight aircraft":carrier.aerial?"Cargo helicopter":"Cart";
                Label(civilisationContent,$"{kind} #{carrier.id} · tier {Math.Max(1,carrier.tier)} / upgrade {Math.Max(1,carrier.level)} · {carrier.loaded:0.0}/{simulation.Logistics.Capacity(carrier):0} {carrier.resource} · {carrier.phase} · {(carrier.navigationStatus==3?"blocked road/gate":carrier.navigationStatus==2&&carrier.loaded>0?"waiting to unload":carrier.navigationStatus==1?"moving":"searching route")} · buffered {carrier.buffered+carrier.transferred:0.0}"+(carrier.upgradeRemaining>0?$" · upgrading {carrier.upgradeRemaining:0}s":""),"muted");
                if(editable){long id=carrier.id;Button(civilisationContent,"UPGRADE #"+id,()=>{world.Notice=simulation.Logistics.UpgradeCarrier(world,0,id)?"Paid cargo upgrade started":"Requires an idle empty carrier, available upgrade and materials";nextCivilisationRefresh=0;});}
            }
        }
        void RefreshResources(CivilisationRuntime simulation,float scroll){
            Label(civilisationContent,"Warehouses hold separate inventories. Nearby construction uses its nearest warehouse. Goods move physically by road, sea, air or rail; transit includes loads and reserved transfer batches.","muted");
            foreach(var colony in simulation.State.colonies){if(colony.faction!=inspectedFaction)continue;
                bool dormant=colony.body!=simulation.BodyId(fixture.definition.bodyId)||!fixture.SimulationActive;
                Label(civilisationContent,colony.name.ToUpperInvariant()+(dormant?" · DORMANT SNAPSHOT":""),"heading");
                Label(civilisationContent,$"POPULATION {colony.population} / {colony.housingCapacity} housing · EMPLOYED {colony.employed}/{colony.workforce} · OPEN JOBS {Math.Max(0,colony.workerDemand-colony.employed)} · PLANNED JOBS {colony.plannedWorkerDemand}","body").name="resource-population";
                Label(civilisationContent,$"FOOD delivered {colony.foodSupplied:0.000}/s / demand {colony.foodDemand:0.000}/s · POWER {colony.powerGeneration:0.0}/{colony.powerDemand:0.0}","body");
                Label(civilisationContent,$"SHOPS · Food {colony.retailFoodCoverage:P0} · Drinks {colony.retailDrinksCoverage:P0} · Medical supplies {colony.retailMedicineCoverage:P0} of residents served","body");
                if(!dormant)FreightFleet(simulation,colony);
                string growth=colony.population>=colony.housingCapacity?"Build more housing":colony.foodBalance<-.000001?"Food deliveries insufficient":colony.powerBalance<0?"More power required":$"Population growing · next resident in {Math.Max(0,simulation.Settings.populationGrowthSeconds-colony.growthProgress):0}s";
                Label(civilisationContent,growth+" · Workers prioritize food, water and extraction. Factories slow or stop without staff.","muted");
                var header=ResourceRow(civilisationContent,"RESOURCE","WAREHOUSE","LOCAL","IN TRANSIT","TOTAL");header.AddToClassList("subheading");
                foreach(var resource in simulation.Catalogue.resources){CivilisationRuntime.ResourceTotals(colony,resource.id,out double stored,out double local,out double transit);
                    var row=ResourceRow(civilisationContent,resource.name,stored.ToString("0.0"),local.ToString("0.0"),transit.ToString("0.0"),(stored+local+transit).ToString("0.0"));row.name="resource-row-"+resource.id;
                }
            }
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
        static VisualElement ResourceRow(VisualElement parent,params string[] cells){
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.minHeight=26;parent.Add(row);
            for(int i=0;i<cells.Length;i++){var cell=new UnityEngine.UIElements.Label(cells[i]);cell.style.width=i==0?280:140;row.Add(cell);}return row;
        }
    }
}

