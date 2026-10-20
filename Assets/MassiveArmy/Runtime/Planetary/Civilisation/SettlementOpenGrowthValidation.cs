using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementHouseholdValidation {
        System.Collections.IEnumerator ValidateSeedBackhaul(){
            var local=world.Base;var sim=local.Civilisation;var colony=local.Settlements[0];foreach(var faction in sim.State.factions)faction.autonomous=false;sim.Settings.autoExtendSettlementRoads=false;world.Rig.InputEnabled=false;world.Paused=true;
            local.Factions[0].wallet.Credit(new ResourceCost{materials=2000,fuel=500});
            var primary=local.Buildings.Find(b=>b.team==0&&b.definition.id=="civil_warehouse");
            Check(local.TryConstructInfrastructure("civil_warehouse",0),"Recovery store uses normal paid grid construction");var store=local.Buildings[local.Buildings.Count-1];store.construction=store.definition.time;sim.Advance(1);
            Check(local.Roads.ConnectToNetwork(store,primary,out _),"Recovery store connects through real road modules");var inventory=colony.installations.Find(i=>i.id==store.persistentId).inventory;
            foreach(var item in colony.installations)item.inventory.Spend("seeds",item.inventory.Get("seeds"));inventory.Deposit("seeds",10);colony.inventory.Spend("food",colony.inventory.Get("food"));
            colony.cargoVehicles.Clear();colony.cargoOrders.Clear();var cart=new CargoVehicleState{id=sim.State.AllocateId(),warehouse=primary.persistentId};colony.cargoVehicles.Add(cart);
            sim.Logistics.Tick(local,colony,0);
            Check(cart.phase==CargoVehiclePhase.Collecting&&cart.source==store.persistentId&&cart.resource=="seeds"&&Math.Abs(cart.requested-7)<.001,"An empty seed store orders finite backhaul while reserving three seeds at the donor");
            Check(inventory.Get("seeds")==10&&colony.inventory.Get("seeds")==0,"Seeds remain at the source until the GPU cart physically arrives");
            for(int i=0;i<2400&&cart.delivered<7;i++){sim.State.time+=.25;sim.Planet(world.definition.bodyId).lastSimulatedTime=sim.State.time;local.CargoNavigation.Step(.25f,true);sim.Logistics.Tick(local,colony,.25);if(i%32==0)yield return null;}
            Check(Math.Abs(colony.inventory.Get("seeds")-7)<.001&&Math.Abs(inventory.Get("seeds")-3)<.001,"GPU backhaul delivers seven seeds and conserves all ten seeds without immediate warehouse ping-pong");
            foreach(var b in local.Buildings)if(b.team==0&&b.definition.housingCapacity>0){var item=colony.installations.Find(i=>i.id==b.persistentId);item.inventory.Spend("food",item.inventory.Get("food"));}
            var family=colony.households[0];var homeFood=colony.installations.Find(i=>i.id==family.home).inventory;homeFood.Deposit("food",10);int oldPopulation=colony.population,oldMembers=family.members.Count,otherMembers=colony.households[1].members.Count;colony.growthProgress=sim.Settings.populationGrowthSeconds;sim.Advance(1);
            Check(colony.population==oldPopulation+1&&family.members.Count==oldMembers+1&&colony.households[1].members.Count==otherMembers,"A supplied family home receives a resident while another home waits for food");
            homeFood.Spend("food",homeFood.Get("food"));colony.growthProgress=sim.Settings.populationGrowthSeconds;sim.Advance(1);Check(colony.population==oldPopulation+1,"Empty food stores cannot create new residents");
            Check(local.TryConstructInfrastructure("civil_rental_home",0),"A new rental family uses a paid home on the normal district grid");var rental=local.Buildings[local.Buildings.Count-1];rental.construction=rental.definition.time;sim.Advance(1);Check(local.Roads.ConnectToNetwork(rental,primary,out _),"New rental home joins the serviced street");colony.installations.Find(i=>i.id==rental.persistentId).inventory.Deposit("food",10);int families=colony.households.Count;Check(sim.GrowResidents(local,colony,1)==1&&colony.households.Count==families+1,"A supplied vacant rental home admits a new household");var tenant=colony.households[colony.households.Count-1];Check(tenant.home==rental.persistentId&&tenant.tenure==HomeTenure.PrivateRental&&!tenant.members.Contains(tenant.owner),"Arriving rental households retain an external private landlord");
            Check(local.TryConstructInfrastructure("civil_food_processing",0),"Cargo fairness fixture constructs a paid food processor");var processor=local.Buildings[local.Buildings.Count-1];processor.construction=processor.definition.time;sim.Advance(1);Check(local.Roads.ConnectToNetwork(processor,primary,out _),"Food processor joins the real cargo graph");var inputs=colony.installations.Find(i=>i.id==processor.persistentId).inventory;
            foreach(var item in colony.installations)if(item.definition=="civil_warehouse"&&!ReferenceEquals(item.inventory,colony.inventory)){foreach(string resource in new[]{"food","water","propellant"})item.inventory.Deposit(resource,1000);item.inventory.Deposit("seeds",10);item.inventory.Deposit("construction_materials",600);}
            foreach(var home in local.Buildings)if(home.team==0&&home.definition.housingCapacity>0){var food=colony.installations.Find(i=>i.id==home.persistentId).inventory;food.Spend("food",food.Get("food"));}
            colony.inventory.Deposit("food",1000);colony.inventory.Deposit("grain",200);colony.inventory.Deposit("water",200);double grain=colony.inventory.Get("grain")+inputs.Get("grain");colony.cargoVehicles.Clear();for(int i=0;i<3;i++)colony.cargoVehicles.Add(new CargoVehicleState{id=sim.State.AllocateId(),warehouse=primary.persistentId});sim.Logistics.Tick(local,colony,0);
            bool grainMission=false;double inTransit=0;foreach(var carrier in colony.cargoVehicles)if(carrier.resource=="grain"){grainMission|=carrier.phase==CargoVehiclePhase.Delivering;inTransit+=carrier.loaded;}
            Check(grainMission,"Continuous food requests cannot starve processor-input deliveries");Check(Math.Abs(colony.inventory.Get("grain")+inputs.Get("grain")+inTransit-grain)<.001,"Fair input dispatch keeps grain in the source or finite cargo holds");
            var displaced=new HouseholdState{id=sim.State.AllocateId(),surname="Recovery"};var person=new ResidentState{id=sim.State.AllocateId(),household=displaced.id,name="Displaced test resident",age=25};displaced.members.Add(person.id);colony.households.Add(displaced);colony.residents.Add(person);colony.population++;homeFood.Deposit("food",10);oldPopulation=colony.population;colony.growthProgress=sim.Settings.populationGrowthSeconds;sim.Advance(1);
            Check(colony.population==oldPopulation+1&&displaced.home==0,"An unhoused family elsewhere cannot veto a supplied local vacancy");
            var depot=local.Buildings.Find(b=>b.team==0&&b.definition.id=="light_depot");var depotInventory=colony.installations.Find(i=>i.id==depot.persistentId).inventory;depotInventory.Spend("food",depotInventory.Get("food"));sim.Advance(1);local.Tick(.0333333f);
            Check(depot.MinimumFoodEfficiency==.25f&&depot.Efficiency>0,"Cargo producer retains its staffed recovery rate across tactical and economy ticks");int carriers=colony.cargoVehicles.Count;Check(sim.Logistics.Order(local,0,false),"Recovery carrier is ordered using actual construction funds");for(int i=0;i<140;i++)sim.Advance(1);
            Check(colony.cargoVehicles.Count>carriers,"Paid cargo production completes while depot food is temporarily late");
            Check(local.TryConstructInfrastructure("civil_fabricator",0),"Demand fixture uses a real paid fabricator");int oldDemand=colony.plannedWorkerDemand;double oldStock=colony.inventory.Get("construction_materials");colony.inventory.Spend("construction_materials",oldStock);colony.inventory.Deposit("construction_materials",1000);
            try{colony.plannedWorkerDemand=0;Check(!local.AiSupplyShort("civil_fabricator",0),"Low warehouse stock alone does not order an endless chain of duplicate fabricators");colony.plannedWorkerDemand=1000;Check(local.AiSupplyShort("civil_fabricator",0),"Fabricator demand grows with the community instead of reaching a fixed building cap");}
            finally{colony.plannedWorkerDemand=oldDemand;colony.inventory.Spend("construction_materials",colony.inventory.Get("construction_materials"));colony.inventory.Deposit("construction_materials",oldStock);}
            Check(report.runtimeErrors==0,"Seed recovery, local immigration and fair cargo scheduling emit no runtime errors");
        }
        void ValidateOpenGrowthRules(){
            var sim=world.Base.Civilisation;var colony=world.Base.Settlements[0];var planet=sim.Planet(world.definition.bodyId);
            foreach(var crop in sim.Catalogue.crops){
                var field=new SurfaceInstallation{definition="civil_farm_"+crop.id,crop=crop.id,position=CubeSphere.Encode(world.Base.Homes[0]),soilFertility=0};field.inventory.Bind(sim.Catalogue);
                field.inventory.Deposit("seeds",3);field.inventory.Deposit("water",10000);
                double temperature=planet.environment.temperature;bool atmosphere=planet.environment.atmosphere;planet.environment.temperature=crop.temperature;planet.environment.atmosphere=true;
                try{for(int season=0;season<100;season++){
                    sim.GrowCrop(field,planet,crop.growthSeconds+1);
                    foreach(var output in crop.harvest)if(output.resource!="seeds")field.inventory.Spend(output.resource,field.inventory.Get(output.resource));
                }}finally{planet.environment.temperature=temperature;planet.environment.atmosphere=atmosphere;}
                Check(field.harvests==100&&field.inventory.Get("seeds")>=crop.seedCost,"Poor-soil "+crop.id+" preserves planting stock through 100 real harvest cycles");
            }
            int demand=colony.plannedWorkerDemand,workforce=colony.workforce;
            try{colony.plannedWorkerDemand=100;colony.workforce=99;Check(PlanetRtsBase.DevelopmentWorkforceReady(colony),"One vacant job cannot lock a mature settlement out of research and development");colony.workforce=20;Check(!PlanetRtsBase.DevelopmentWorkforceReady(colony),"Severe understaffing still suspends optional expansion");}
            finally{colony.plannedWorkerDemand=demand;colony.workforce=workforce;}
            Check(sim.Settings.maximumRoadTiles==0,"Default road network has no fixed tile-count growth cap");
            // Exercise an actual GPU storage clear beyond the old one-dimensional dispatch limit.
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementRoad"));
            const int stride=65536,slots=128,count=stride*slots;
            using(var seen=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,4))
            using(var work=new GraphicsBuffer(GraphicsBuffer.Target.Structured,slots,48)){
                int kernel=shader.FindKernel("ClearSearch");shader.SetInt("_RoadStride",stride);shader.SetInt("_SearchSlots",slots);shader.SetBuffer(kernel,"_RoadSeen",seen);shader.SetBuffer(kernel,"_RoadWork",work);
                seen.SetData(new uint[]{17},0,count-1,1);SettlementCargoNavigation.DispatchStorage(shader,kernel,count);var tail=new uint[1];seen.GetData(tail,0,count-1,1);
                Check(tail[0]==0,"GPU road storage clears the final element beyond 65,535 dispatch groups");
            }UnityEngine.Object.Destroy(shader);
        }
    }
}
