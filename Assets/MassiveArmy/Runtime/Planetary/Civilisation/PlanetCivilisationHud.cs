using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsHud {
        bool civilisationOpen;double nextCivilisationRefresh;VisualElement civilisationContent;int inspectedFaction;string inspectedPlanet;
        void ToggleCivilisation(){civilisationOpen=!civilisationOpen;technologyOpen=false;var modal=front.Q("modal");modal.Clear();modal.style.display=civilisationOpen?DisplayStyle.Flex:DisplayStyle.None;if(!civilisationOpen)return;
            var panel=new VisualElement();panel.AddToClassList("panel");panel.style.width=1000;panel.style.maxHeight=700;modal.Add(panel);Label(panel,"SETTLEMENTS & INDUSTRY","heading");
            var actions=new VisualElement();actions.AddToClassList("row");panel.Add(actions);Button(actions,"CLOSE",ToggleCivilisation);if(fixture.sessionMode==PlanetGameMode.AiTest){Button(actions,"CYAN",()=>{inspectedFaction=0;nextCivilisationRefresh=0;});Button(actions,"RED",()=>{inspectedFaction=1;nextCivilisationRefresh=0;});Label(panel,"OBSERVER · TRUE UNIVERSE INFORMATION","muted");}else{inspectedFaction=0;Label(panel,"FACTION KNOWLEDGE · undiscovered worlds and enemy inventories remain hidden","muted");}
            civilisationContent=new ScrollView();civilisationContent.style.height=570;panel.Add(civilisationContent);nextCivilisationRefresh=0;RefreshCivilisation();
        }
        void RefreshCivilisation(){
            if(!civilisationOpen||civilisationContent==null||world.Civilisation==null||Time.realtimeSinceStartupAsDouble<nextCivilisationRefresh)return;nextCivilisationRefresh=Time.realtimeSinceStartupAsDouble+1;
            var simulation=world.Civilisation;var state=simulation.State;var faction=state.Faction(inspectedFaction);bool observer=fixture.sessionMode==PlanetGameMode.AiTest;civilisationContent.Clear();
            if(simulation.CatchingUp(fixture.definition.bodyId))Label(civilisationContent,"CATCHING UP DORMANT SETTLEMENT ECONOMY","heading");Label(civilisationContent,faction.name+" · "+faction.priority,"subheading");Label(civilisationContent,faction.reason,"muted");
            foreach(var colony in state.colonies){if(colony.faction!=inspectedFaction)continue;Label(civilisationContent,colony.name.ToUpperInvariant(),"heading");
                Label(civilisationContent,$"POPULATION {colony.population} · HOUSING {colony.housedPopulation}/{colony.housingCapacity} · {colony.stage}","body");
                Label(civilisationContent,$"POWER {colony.powerGeneration:0.0} / {colony.powerDemand:0.0} · BALANCE {colony.powerBalance:+0.0;-0.0;0}\nFOOD DEMAND {colony.foodDemand:0.000}/s · DELIVERED {colony.foodSupplied:0.000}/s · {(colony.foodBalance<-.0001?"SHORTAGE":"SUPPLIED")}","body");
                Label(civilisationContent,$"WAREHOUSE · {colony.cargoVehicles.Count} cargo vehicles · {colony.cargoOrders.Count} in production","subheading");
                foreach(var resource in simulation.Catalogue.resources){double amount=colony.inventory.Get(resource.id);if(amount>0)Label(civilisationContent,resource.name+": "+amount.ToString("0.0"),"body");}
                foreach(var installation in colony.installations){var definition=world.Catalog.Buildings[installation.definition];string info=definition.name+(string.IsNullOrEmpty(installation.recipe)?"":" · "+installation.recipe);
                    if(!string.IsNullOrEmpty(installation.crop))info+=$" · {installation.growth:P0} grown · soil {installation.soilFertility:P0} · {installation.harvests} harvests";Label(civilisationContent,info,"muted");
                    if(!ReferenceEquals(installation.inventory,colony.inventory))foreach(var resource in installation.inventory.stock)if(resource.amount>.001)Label(civilisationContent,"  Local "+resource.resource+": "+resource.amount.ToString("0.0"),"body");
                }
                foreach(var vehicle in colony.cargoVehicles)Label(civilisationContent,$"{(vehicle.aerial?"Air cargo":"Truck")} #{vehicle.id} · {vehicle.phase} · {vehicle.resource} {vehicle.loaded:0.0} · delivered {vehicle.delivered:0.0}","muted");
            }
            Label(civilisationContent,"PLANETARY KNOWLEDGE","heading");
            var choices=new System.Collections.Generic.List<string>();foreach(var planet in state.planets)if(observer||faction.knowledge.Knows(planet.body))choices.Add(planet.body);if(!choices.Contains(inspectedPlanet))inspectedPlanet=choices.Count>0?choices[0]:null;
            if(choices.Count>0){var selector=new DropdownField("Known worlds",choices,choices.IndexOf(inspectedPlanet));selector.RegisterValueChangedCallback(change=>{inspectedPlanet=change.newValue;nextCivilisationRefresh=0;});civilisationContent.Add(selector);}
            foreach(var planet in state.planets){if(planet.body!=inspectedPlanet)continue;var known=faction.knowledge.Find(planet.body);if(!observer&&known==null)continue;Label(civilisationContent,planet.body+" · "+(observer?planet.composition.archetype:known.level+" · confidence "+known.confidence.ToString("P0")),"subheading");
                if(observer)foreach(var abundance in planet.composition.abundance)Label(civilisationContent,abundance.resource+" "+abundance.amount.ToString("P0"),"muted");
                else foreach(var estimate in known.resources)Label(civilisationContent,estimate.resource+" "+estimate.estimate.ToString("P0")+" ±"+estimate.uncertainty.ToString("P0"),"muted");
            }
        }
    }
}

