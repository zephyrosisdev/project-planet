using System;
using System.Collections;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementHouseholdValidation {
        IEnumerator ValidateSettlementRecovery(){
            var local=world.Base;var sim=local.Civilisation;var c=local.Settlements[0];foreach(var f in sim.State.factions)f.autonomous=false;sim.Settings.autoExtendSettlementRoads=false;
            foreach(var v in c.cargoVehicles)v.manualDispatchForValidation=true;
            // Explicit fixture capital and inputs. Autonomous growth is tested separately without grants.
            c.inventory.Deposit("construction_materials",8000);c.inventory.Deposit("propellant",1000);
            var home=local.Buildings.Find(b=>b.team==0&&b.definition.id=="civil_warehouse");
            Check(local.TryConstructInfrastructure("wind",0),"Recovery wind turbine uses paid placement");var wind=local.Buildings[local.Buildings.Count-1];sim.Advance(1);
            foreach(var b in local.Buildings)if(b.team==0&&b.Complete){b.FoodEfficiency=0;if(b.definition.generation>0)b.powered=false;else if(b.definition.id!="civil_warehouse"){b.repairing=true;b.health=Mathf.Max(1,b.health-50);}}
            foreach(var i in c.installations)i.inventory.Spend("food",i.inventory.Get("food"));
            CivilisationRuntime.AllocateWorkers(local,c,0);local.Tick(1);
            Check(local.Factions[0].generation==0&&wind.constructionWorkers>0&&wind.construction>0,"Assigned builders black-start paid generation despite starvation, zero grid and pending repairs");
            for(int step=0;step<800&&!wind.Complete;step++){sim.Advance(1);local.Tick(1);if(step%60==0)yield return null;}
            local.RefreshSettlementPower(0);Check(wind.Complete&&local.Factions[0].generation>0,"Wind generation completes and supplies real grid power without food");
            foreach(var b in local.Buildings)if(b.team==0){b.repairing=false;if(b.definition.generation>0)b.powered=true;}
            var food=local.FreightFixtureBuilding("civil_food_processing",local.TeamOffset(0,new Vector2(80,100)));Check(food!=null,"Paid food processor can be constructed for recovery test");sim.Advance(1);Check(local.Roads.ConnectToNetwork(food,home,out _),"Recovery food producer joins warehouse roads");
            var inventory=SettlementLogistics.Find(c,food.persistentId).inventory;inventory.Deposit("grain",80);inventory.Deposit("water",80);inventory.Spend("food",inventory.Get("food"));food.FoodEfficiency=0;double grain=inventory.Get("grain");
            for(int step=0;step<80;step++){sim.Advance(1);local.Tick(1);if(step%30==0)yield return null;}
            Check(inventory.Get("food")>0&&inventory.Get("grain")<grain,"Staffed food processing recovers from empty food using finite real recipe inputs");
            var remote=local.FreightFixtureBuilding("civil_warehouse",local.TeamOffset(0,new Vector2(180,110)));Check(remote!=null,"Paid alternate cargo destination exists");sim.Advance(1);Check(local.Roads.Connect(home,remote,out _),"Alternate warehouse shares road network");
            var remoteStock=SettlementLogistics.Find(c,remote.persistentId).inventory;remoteStock.Deposit("construction_materials",remoteStock.Capacity("construction_materials"));
            var cart=new CargoVehicleState{id=sim.State.AllocateId(),warehouse=home.persistentId,manualDispatchForValidation=true};c.cargoVehicles.Add(cart);
            double total=FreightTotal(c,"construction_materials");Check(sim.Logistics.StartFreightForValidation(local,cart,home,remote,"construction_materials",80),"Ground cart dispatches finite full load toward full storage");
            yield return FreightReturn(cart);Check(Math.Abs(FreightTotal(c,"construction_materials")-total)<.001&&cart.loaded==0,"Full destination redirects actual GPU cargo without losing materials or reserving the cart forever");
            remoteStock.Spend("construction_materials",remoteStock.Get("construction_materials"));total=FreightTotal(c,"construction_materials");Check(sim.Logistics.StartFreightForValidation(local,cart,home,remote,"construction_materials",80),"Second ground cargo mission begins");local.Damage(remote,(uint)Mathf.CeilToInt(remote.health)+1,false);sim.Advance(1);
            yield return FreightReturn(cart);Check(Math.Abs(FreightTotal(c,"construction_materials")-total)<.001&&cart.loaded==0,"Destroyed destination preserves payload and reroutes from the reached historical endpoint");
            var airportA=local.FreightFixtureBuilding("civil_airport",local.TeamOffset(0,new Vector2(-100,90)));var airportB=local.FreightFixtureBuilding("civil_airport",local.TeamOffset(0,new Vector2(480,320)));
            Check(airportA!=null&&airportB!=null,"Recovery flight uses two paid airports");var far=local.FreightFixtureBuilding("civil_warehouse",CubeSphere.Decode(airportB.position));Check(far!=null,"Recovery flight has a regional destination");sim.Advance(1);
            Check(local.Roads.Connect(airportA,home,out _)&&local.Roads.Connect(airportB,far,out _),"Freight recovery uses actual terminal road shuttles");c.inventory.Deposit("food",500);
            var air=new CargoVehicleState{id=sim.State.AllocateId(),warehouse=home.persistentId,aerial=true,freight=true,manualDispatchForValidation=true};c.cargoVehicles.Add(air);double foodTotal=FreightTotal(c,"food");
            Check(sim.Logistics.StartFreightForValidation(local,air,home,far,"food",160),"Recovery shipment carries a finite 160-unit aircraft batch");local.Damage(far,(uint)Mathf.CeilToInt(far.health)+1,false);sim.Advance(1);foodTotal=FreightTotal(c,"food");
            yield return FreightReturn(air);Check(Math.Abs(FreightTotal(c,"food")-foodTotal)<.001&&air.loaded+air.buffered+air.transferred==0,"Destroyed freight recipient returns its entire finite batch by physical road and air legs");
            sim.Settings.populationGrowthSeconds=1;foreach(var b in local.Buildings)if(b.team==0&&!b.removed&&b.Complete){var item=SettlementLogistics.Find(c,b.persistentId);if(item==null)continue;item.inventory.Spend("food",item.inventory.Get("food"));if(b.definition.housingCapacity>0)item.inventory.Deposit("food",50);}
            int population=c.population;sim.Advance(1);Check(c.population>population&&c.foodBalance<0,"Supplied homes admit workers while an undersupplied nonessential building still pays its production penalty");
            foreach(var b in local.Buildings)if(b.team==0&&b.definition.housingCapacity>0){var item=SettlementLogistics.Find(c,b.persistentId);item?.inventory.Spend("food",item.inventory.Get("food"));}population=c.population;sim.Advance(1);Check(c.population==population,"Empty household food still prevents population growth");
            Check(report.runtimeErrors==0,"Settlement recovery has no runtime errors");
        }
    }
}

