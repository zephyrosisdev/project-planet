using System;
using System.Collections;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal RtsBuilding FreightFixtureBuilding(string id,Vector3 centre){
            var east=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre).normalized;var north=Vector3.Cross(centre,east);
            for(int attempt=0;attempt<96;attempt++){float angle=attempt*2.3999632f,range=attempt==0?0:24+Mathf.Sqrt(attempt)*8;var point=(centre+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*range/fixture.definition.radius).normalized;
                if(ConstructForTeam(id,0,point)){var result=Buildings[Buildings.Count-1];result.construction=result.definition.time;return result;}}return null;
        }
        internal RtsBuilding CoastFixtureBuilding(){
            var definition=Catalog.Buildings["civil_port"];const int resolution=32;
            for(int face=0;face<6;face++)for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++){
                var cell=new PlanetNavCoordinate(fixture.definition.bodyId,(CubeFace)face,x,y);var a=CubeSphere.Direction(cell,resolution);var b=CubeSphere.Direction(CubeSphere.Neighbour(cell,1,0,resolution),resolution);
                bool wet=surface.SampleHeight(a)<fixture.definition.oceanHeight;if(wet==(surface.SampleHeight(b)<fixture.definition.oceanHeight))continue;
                for(int step=0;step<16;step++){var middle=Vector3.Slerp(a,b,.5f);if((surface.SampleHeight(middle)<fixture.definition.oceanHeight)==wet)a=middle;else b=middle;}
                var point=Vector3.Slerp(a,b,.5f);for(int rotation=0;rotation<4;rotation++){var area=Grid.Area(definition,point,rotation);if(surface.SampleHeight(Grid.Direction(Grid.Port(area)))<fixture.definition.oceanHeight+.1f)continue;
                    if(ConstructOriented(definition,0,point,rotation)){var port=Buildings[Buildings.Count-1];port.construction=port.definition.time;return port;}}
            }return null;
        }
        internal RtsBuilding SecondCoastFixture(RtsBuilding first){
            var centre=CubeSphere.Decode(first.position);var east=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre).normalized;var north=Vector3.Cross(centre,east);var definition=Catalog.Buildings["civil_port"];
            for(int attempt=0;attempt<256;attempt++){float angle=attempt*2.3999632f,range=48+Mathf.Sqrt(attempt)*16;var a=(centre+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*range/fixture.definition.radius).normalized;
                var b=(a+Vector3.ProjectOnPlane(Civilisation.Logistics.Berth(this,first)-centre,a).normalized*128/fixture.definition.radius).normalized;
                bool wet=surface.SampleHeight(a)<fixture.definition.oceanHeight;if(wet==(surface.SampleHeight(b)<fixture.definition.oceanHeight))continue;
                for(int step=0;step<12;step++){var middle=Vector3.Slerp(a,b,.5f);if((surface.SampleHeight(middle)<fixture.definition.oceanHeight)==wet)a=middle;else b=middle;}
                var point=Vector3.Slerp(a,b,.5f);for(int rotation=0;rotation<4;rotation++){var area=Grid.Area(definition,point,rotation);if(surface.SampleHeight(Grid.Direction(Grid.Port(area)))<fixture.definition.oceanHeight+.1f)continue;
                    if(ConstructOriented(definition,0,point,rotation)){var port=Buildings[Buildings.Count-1];port.construction=port.definition.time;return port;}}
            }return null;
        }
    }
    public sealed partial class SettlementHouseholdValidation {
        struct CargoPose {public Vector4 position,right,up,forward,properties;}
        double FreightTotal(ColonyState colony,string resource){CivilisationRuntime.ResourceTotals(colony,resource,out double stored,out double local,out double transit);return stored+local+transit;}
        IEnumerator FreightTrip(CargoVehicleState carrier,double delivered,bool requireAircraft,bool requireTrain,bool requireShip){
            var local=world.Base;var simulation=local.Civilisation;bool air=false,rail=false,sea=false,road=false;int budget=12000;
            while(carrier.delivered<delivered-1e-6&&budget-->0){simulation.State.time+=.25;simulation.Planet(world.definition.bodyId).lastSimulatedTime=simulation.State.time;local.CargoNavigation.Step(.25f,true);simulation.Logistics.Tick(local,local.Settlements[0],0);
                Check(carrier.loaded<=simulation.Logistics.Capacity(carrier)+1e-6,"Carrier load stays within the active vehicle limit");
                if(carrier.leg==1){air|=carrier.aerial;rail|=carrier.train;sea|=carrier.seaborne;}else road=true;
                if(budget%32==0)yield return null;
            }
            Check(budget>0&&carrier.delivered>=delivered-1e-6,"Actual GPU road/terminal freight arrives with its finite load");Check(road&&(!requireAircraft||air)&&(!requireTrain||rail)&&(!requireShip||sea),"Shipment uses local road shuttles and the required long-haul mode");
        }
        IEnumerator FreightReturn(CargoVehicleState carrier){var local=world.Base;var simulation=local.Civilisation;int budget=6000;while(carrier.phase!=CargoVehiclePhase.Idle&&budget-->0){simulation.State.time+=.25;simulation.Planet(world.definition.bodyId).lastSimulatedTime=simulation.State.time;local.CargoNavigation.Step(.25f,true);simulation.Logistics.Tick(local,local.Settlements[0],0);if(budget%32==0)yield return null;}Check(budget>0,"Freight carrier returns to its home warehouse before reuse");}
        IEnumerator ValidateFreight(){
            var local=world.Base;var simulation=local.Civilisation;var colony=local.Settlements[0];foreach(var faction in simulation.State.factions)faction.autonomous=false;simulation.Settings.autoExtendSettlementRoads=false;world.Rig.InputEnabled=false;
            foreach(var vehicle in colony.cargoVehicles)vehicle.manualDispatchForValidation=true;
            // Explicit fixture capital; the separate autonomous soak uses no grants.
            colony.inventory.Deposit("construction_materials",8000);colony.inventory.Deposit("propellant",1000);colony.inventory.Deposit("food",1000);
            var warehouse=local.Buildings.Find(b=>b.team==0&&b.definition.id=="civil_warehouse");
            var airportA=local.FreightFixtureBuilding("civil_airport",local.TeamOffset(0,new Vector2(-100,90)));var airportB=local.FreightFixtureBuilding("civil_airport",local.TeamOffset(0,new Vector2(480,320)));
            Check(airportA!=null&&airportB!=null,"Two paid civilian airports can be placed without forward bases");
            var remote=local.FreightFixtureBuilding("civil_warehouse",CubeSphere.Decode(airportB.position));Check(remote!=null,"A remote area has its own paid warehouse");simulation.Advance(1);
            Check(local.Roads.Connect(airportA,warehouse,out _),"Source airport joins its warehouse road network");Check(local.Roads.Connect(airportB,remote,out _),"Destination airport joins its separate warehouse road network");Check(!local.Roads.Connected(warehouse,remote),"The two warehouse inventories are in disconnected road networks");
            var remoteInventory=SettlementLogistics.Find(colony,remote.persistentId).inventory;Check(remoteInventory.Get("construction_materials")==0,"New regional warehouse does not inherit global stock");
            var stationDefinition=local.Catalog.Buildings["civil_freight_station"];Check(!local.ConstructionCost(0,CubeSphere.Decode(remote.position),stationDefinition.cost,false),"An empty regional warehouse cannot borrow distant construction materials");
            var aircraft=new CargoVehicleState{id=simulation.State.AllocateId(),warehouse=warehouse.persistentId,aerial=true,freight=true,manualDispatchForValidation=true};colony.cargoVehicles.Add(aircraft);
            double total=FreightTotal(colony,"construction_materials");Check(simulation.Logistics.StartFreightForValidation(local,aircraft,warehouse,remote,"construction_materials",100000),"A finite air shipment uses both airports");Check(aircraft.requested==160&&aircraft.loaded<=80&&aircraft.buffered>0,"Aircraft 160-unit hold requires repeated 80-unit truck shuttles");
            yield return FreightTrip(aircraft,160,true,false,false);Check(Math.Abs(FreightTotal(colony,"construction_materials")-total)<.001,"Transfers conserve warehouse, truck, aircraft and buffered materials");yield return FreightReturn(aircraft);
            Check(simulation.Logistics.StartFreightForValidation(local,aircraft,warehouse,remote,"construction_materials",100000),"A second load is a separate paid-fuel flight");yield return FreightTrip(aircraft,320,true,false,false);yield return FreightReturn(aircraft);
            Check(remoteInventory.Get("construction_materials")==320&&local.ConstructionCost(0,CubeSphere.Decode(remote.position),stationDefinition.cost,false),"Delivered regional stock unlocks local construction");
            var stationA=local.FreightFixtureBuilding("civil_freight_station",local.TeamOffset(0,new Vector2(-140,110)));var stationB=local.FreightFixtureBuilding("civil_freight_station",CubeSphere.Decode(remote.position));Check(stationA!=null&&stationB!=null,"Freight stations use paid regional materials");simulation.Advance(1);
            Check(local.Roads.Connect(stationA,warehouse,out _)&&local.Roads.Connect(stationB,remote,out _),"Rail terminals connect to their local roads");
            var train=new CargoVehicleState{id=simulation.State.AllocateId(),warehouse=warehouse.persistentId,freight=true,train=true,manualDispatchForValidation=true};colony.cargoVehicles.Add(train);
            Check(!simulation.Logistics.StartFreightForValidation(local,train,warehouse,remote,"food",100000),"Train cannot travel between disconnected tracks");Check(local.Rails.Connect(stationA,stationB,out string railReason),"Paid rail modules connect both freight stations: "+railReason);
            double food=FreightTotal(colony,"food");Check(simulation.Logistics.StartFreightForValidation(local,train,warehouse,remote,"food",100000)&&train.requested==480,"Train hold is larger than ship and aircraft holds");yield return FreightTrip(train,480,false,true,false);Check(Math.Abs(FreightTotal(colony,"food")-food)<.001,"Rail delivery conserves all food including transfer buffers");yield return FreightReturn(train);
            foreach(var item in colony.installations)if(item.definition=="civil_warehouse")item.inventory.Deposit("construction_materials",6000);
            var portA=local.CoastFixtureBuilding();Check(portA!=null,"A real coastal cargo port can be placed");var portB=local.SecondCoastFixture(portA);Check(portB!=null,"A second real coastal terminal can be placed");simulation.Advance(1);
            Check(local.Roads.Place(local.Roads.Port(portA),local.Roads.Port(portA),0,out _)&&local.Roads.Place(local.Roads.Port(portB),local.Roads.Port(portB),0,out _),"Coastal terminals have dry road access cells");
            bool ocean=false;for(int attempt=0;attempt<64&&!ocean;attempt++){simulation.State.time++;ocean=simulation.Logistics.OceanRoute(local,portA,portB);}Check(ocean,"Shipping route is validated over water instead of crossing land");
            var stock=SettlementLogistics.Find(colony,portA.persistentId).inventory;stock.Deposit("food",1000);stock.Deposit("propellant",1000);stock.Deposit("construction_materials",1000);var ship=new CargoVehicleState{id=simulation.State.AllocateId(),warehouse=portA.persistentId,freight=true,seaborne=true,manualDispatchForValidation=true};colony.cargoVehicles.Add(ship);
            food=FreightTotal(colony,"food");Check(simulation.Logistics.StartFreightForValidation(local,ship,portA,portB,"food",100000)&&ship.requested==240,"Cargo ship has a finite hold larger than aircraft");yield return FreightTrip(ship,240,false,false,true);Check(Math.Abs(FreightTotal(colony,"food")-food)<.001,"Ocean shipping conserves cargo across truck and ship transfers");yield return FreightReturn(ship);
            var cart=new CargoVehicleState{id=simulation.State.AllocateId(),warehouse=warehouse.persistentId,manualDispatchForValidation=true};colony.cargoVehicles.Add(cart);Check(simulation.Logistics.StartFreightForValidation(local,cart,warehouse,airportA,"food",100000)&&cart.loaded==80,"Original settlement carts enforce an 80-unit payload");
            Check(simulation.Logistics.HullCapacity(ship)>simulation.Logistics.HullCapacity(aircraft)&&simulation.Settings.shipCargoSpeed<simulation.Settings.freightAircraftSpeed,"Ships trade speed for larger holds");
            // Paid upgrades change future capacity while an idle vehicle is empty.
            double materials=colony.inventory.Get("construction_materials");Check(simulation.Logistics.UpgradeCarrier(local,0,aircraft.id),"Idle aircraft can buy a paid hold upgrade");simulation.Logistics.Tick(local,colony,30);Check(aircraft.level==2&&simulation.Logistics.HullCapacity(aircraft)==200&&colony.inventory.Get("construction_materials")<materials,"Paid retrofit increases aircraft capacity without creating resources");
            Check(!local.Factions[0].HasTech("civil_bulk_shipping_2")&&simulation.Logistics.UnlockedFreightTier(local,0,true)==1,"Large cargo ships remain research locked");
            var lab=local.FreightFixtureBuilding("lab",local.TeamOffset(0,new Vector2(-100,-110)));Check(lab!=null,"A real laboratory supports freight research");lab.WorkforceEfficiency=lab.FoodEfficiency=1;local.Factions[0].wallet.Credit(new ResourceCost{research=1000});
            Check(local.Research(lab,"civil_bulk_shipping_2"),"Larger ship tier is queued through paid original RTS research");for(int i=0;i<600&&!local.Factions[0].HasTech("civil_bulk_shipping_2");i++)local.Tick(1);
            Check(local.Factions[0].HasTech("civil_bulk_shipping_2")&&simulation.Logistics.UnlockedFreightTier(local,0,true)==2,"Completed research unlocks larger ships over time");
            Check(simulation.Logistics.UpgradeCarrier(local,0,ship.id),"Existing ship can purchase the unlocked larger tier");simulation.Logistics.Tick(local,colony,30);Check(ship.tier==2&&simulation.Logistics.HullCapacity(ship)==480,"Larger ship retrofit has a finite doubled hold");
            var restored=CivilisationRuntime.Deserialize(simulation.Serialize(),simulation.State.galaxySeed,simulation.Catalogue);Check(restored.Planet(simulation.BodyId(world.definition.bodyId)).rails.Count==local.Rails.Tiles.Count&&restored.colonies[0].cargoVehicles.Find(v=>v.id==ship.id).tier==2,"Strategic saves preserve tracks, regional stock, freight missions and upgraded carriers");Check(report.runtimeErrors==0,"Multimodal GPU freight emits no runtime errors");
        }
    }
}
