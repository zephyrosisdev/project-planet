using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum CargoVehiclePhase {Idle,Collecting,Delivering,Returning}
    [Serializable]public sealed class CargoVehicleState {
        public uint navigationStatus;public long id,warehouse,source,destination;public bool aerial,freight,seaborne,train,returningUndelivered;public bool shuttleReturning;public int tier=1,level=1,pendingTier,pendingLevel,leg;public double upgradeRemaining;public double legDeparture,legArrival;public List<long> stops=new List<long>();public CargoVehiclePhase phase;
        public string resource;public double requested,loaded,buffered,transferred,departure,arrival,delivered,routeMetres;
        public List<Vector2> path=new List<Vector2>(); // Strategic endpoints only; no CPU road route or live pose.
        [NonSerialized]internal bool manualDispatchForValidation;[NonSerialized]public uint navigationVersion;[NonSerialized]public bool gpuArrived;[NonSerialized]public double gpuRouteMetres;
    }
    [Serializable]public sealed class CargoVehicleOrder {public long producer,warehouse;public bool aerial,freight,seaborne,train;public int tier=1;public double progress;}
    public sealed partial class SettlementLogistics {
        readonly CivilisationRuntime civilisation;
        readonly Dictionary<long,int> exportCursors=new Dictionary<long,int>();
        internal Action<int,string,double> DeliveredForValidation;
        public SettlementLogistics(CivilisationRuntime civilisation){this.civilisation=civilisation;}
        static bool Storage(SurfaceInstallation item)=>item.definition=="civil_warehouse"||item.definition=="civil_market";
        public static SurfaceInstallation Find(ColonyState colony,long id){foreach(var item in colony.installations)if(item.id==id)return item;return null;}
        static RtsBuilding Building(PlanetRtsBase local,long id)=>local.FindBuilding(id);
        public bool Order(PlanetRtsBase local,int faction,bool aerial){
            var colony=civilisation.Colony(civilisation.BodyId(local.World.definition.bodyId),faction);if(colony==null||colony.cargoVehicles.Count+colony.cargoOrders.Count>=civilisation.Settings.maximumCargoVehicles)return false;
            RtsBuilding producer=null;foreach(var b in local.Buildings)if(b.team==faction&&!b.removed&&b.Complete&&(aerial?b.definition.id=="helipad":b.definition.id=="light_depot")){producer=b;break;}
            if(producer==null||!Connected(local,producer))return false;double cost=aerial?civilisation.Settings.airCargoVehicleMaterialCost:civilisation.Settings.cargoVehicleMaterialCost;
            var warehouse=RegionalWarehouse(local,colony,producer);if(warehouse==null||!warehouse.inventory.Spend("construction_materials",cost))return false;colony.cargoOrders.Add(new CargoVehicleOrder{producer=producer.persistentId,warehouse=warehouse.id,aerial=aerial});return true;
        }
        public bool Connected(PlanetRtsBase local,RtsBuilding building){
            var colony=civilisation.Colony(civilisation.BodyId(local.World.definition.bodyId),building.team);if(colony==null)return false;
            foreach(var item in colony.installations)if(Storage(item)){var warehouse=Building(local,item.id);if(warehouse==null||!warehouse.Complete)continue;if(local.Roads.Connected(building,warehouse))return true;}
            foreach(var vehicle in colony.cargoVehicles)if(vehicle.aerial&&!vehicle.freight)return true;
            foreach(var vehicle in colony.cargoVehicles)if(vehicle.freight){var home=Building(local,vehicle.warehouse);if(home!=null&&TryFreightRoute(local,home,building,vehicle.seaborne,null,vehicle.train))return true;}return false;
        }
        public void Tick(PlanetRtsBase local,ColonyState colony,double dt){
            var settings=civilisation.Settings;double now=civilisation.SimulationTime;
            for(int i=colony.cargoOrders.Count-1;i>=0;i--){var order=colony.cargoOrders[i];var producer=Building(local,order.producer);if(producer==null){colony.cargoOrders.RemoveAt(i);continue;}if(!producer.powered||producer.paused||!producer.Complete)continue;order.progress+=dt*producer.Efficiency;if(order.progress<settings.cargoVehicleSeconds)continue;
                SurfaceInstallation home=order.warehouse>0?Find(colony,order.warehouse):null;if(home==null)foreach(var item in colony.installations)if(item.definition=="civil_warehouse"){home=item;break;}if(home==null)continue;
                colony.cargoVehicles.Add(new CargoVehicleState{id=civilisation.State.AllocateId(),warehouse=home.id,aerial=order.aerial,freight=order.freight,seaborne=order.seaborne,train=order.train,tier=Math.Clamp(order.tier,1,3)});colony.cargoOrders.RemoveAt(i);
            }
            foreach(var vehicle in colony.cargoVehicles){
                if(vehicle.upgradeRemaining>0){vehicle.upgradeRemaining=Math.Max(0,vehicle.upgradeRemaining-dt);if(vehicle.upgradeRemaining<=0){vehicle.tier=vehicle.pendingTier;vehicle.level=vehicle.pendingLevel;}continue;}
                if(vehicle.freight&&vehicle.phase!=CargoVehiclePhase.Idle&&!AdvanceFreight(local,vehicle,now))continue;
                if(vehicle.phase!=CargoVehiclePhase.Idle&&now>=vehicle.arrival&&(civilisation.ReplayingEconomy||local.CargoNavigation?.Arrived(vehicle)==true)){
                    if(vehicle.phase==CargoVehiclePhase.Returning){if(Building(local,vehicle.warehouse)==null){RedirectArrivedCargo(local,colony,vehicle,now);continue;}vehicle.navigationVersion=0;vehicle.phase=CargoVehiclePhase.Idle;Assign(local,colony,vehicle);continue;}
                    var source=Find(colony,vehicle.source);var destination=Find(colony,vehicle.destination);
                    if(vehicle.phase==CargoVehiclePhase.Collecting){
                        vehicle.navigationVersion=0;
                        if(source==null||destination==null){vehicle.destination=vehicle.source;vehicle.path.Reverse();if(vehicle.freight)vehicle.stops.Reverse();vehicle.departure=now;vehicle.arrival=now+MissionTime(local,vehicle);if(vehicle.freight)BeginFreightLeg(local,vehicle,now);vehicle.phase=CargoVehiclePhase.Returning;continue;}
                        vehicle.loaded=Math.Min(HullCapacity(vehicle),Math.Min(vehicle.requested,source.inventory.Get(vehicle.resource)));source.inventory.Spend(vehicle.resource,vehicle.loaded);vehicle.path.Reverse();if(vehicle.freight)vehicle.stops.Reverse();vehicle.departure=now;vehicle.arrival=now+MissionTime(local,vehicle);if(vehicle.freight)BeginFreightLeg(local,vehicle,now);vehicle.phase=CargoVehiclePhase.Delivering;
                    }else{
                        if(destination==null||Building(local,destination.id)==null){RedirectArrivedCargo(local,colony,vehicle,now);continue;}
                        double accepted=destination.inventory.Deposit(vehicle.resource,vehicle.loaded);if(Storage(destination)&&accepted>0)DeliveredForValidation?.Invoke(colony.faction,vehicle.resource,accepted);vehicle.loaded-=accepted;vehicle.delivered+=accepted;
                        // If storage fills, the cargo remains aboard and retries; it is never silently destroyed.
                        if(vehicle.loaded>1e-6){if(!RedirectArrivedCargo(local,colony,vehicle,now))vehicle.arrival=now+settings.economyTickSeconds;continue;}
                        vehicle.navigationVersion=0;if(destination.id!=vehicle.warehouse){vehicle.path.Reverse();if(vehicle.freight)vehicle.stops.Reverse();vehicle.departure=now;vehicle.arrival=now+MissionTime(local,vehicle);if(vehicle.freight)BeginFreightLeg(local,vehicle,now);vehicle.phase=CargoVehiclePhase.Returning;}else vehicle.phase=CargoVehiclePhase.Idle;
                    }
                }
                if(vehicle.phase==CargoVehiclePhase.Idle)Assign(local,colony,vehicle);
            }
        }
        double TravelTime(PlanetRtsBase local,List<Vector2> path,bool aerial,double routeMetres=0){double metres=0;for(int i=1;i<path.Count;i++)metres+=CubeSphere.ArcDistance(CubeSphere.Decode(path[i-1]),CubeSphere.Decode(path[i]),local.World.definition.radius);return Math.Max(metres,routeMetres)/(aerial?civilisation.Settings.airCargoSpeed:civilisation.Settings.groundCargoSpeed)+civilisation.Settings.cargoHandlingSeconds;}
        bool Busy(ColonyState colony,long installation,string resource){foreach(var truck in colony.cargoVehicles)if(truck.phase!=CargoVehiclePhase.Idle&&truck.resource==resource&&(truck.source==installation||truck.destination==installation))return true;return false;}
        double SupplyHorizon(PlanetRtsBase local,RtsBuilding warehouse,RtsBuilding producer,bool aerial){
            var settings=civilisation.Settings;double distance=CubeSphere.ArcDistance(local.Roads.Port(warehouse),local.Roads.Port(producer),local.World.definition.radius);
            return Math.Max(settings.buildingInputReserveSeconds,3*(distance*1.5/(aerial?settings.airCargoSpeed:settings.groundCargoSpeed)+settings.cargoHandlingSeconds));
        }
        double RetailTarget(ColonyState colony,RtsBuilding b,double horizon)=>Math.Max(civilisation.Settings.cargoCapacity*.25,Math.Min(colony.population,100*b.level)*b.definition.retailPerResident*horizon)+(b.definition.retailResource=="food"?FoodTarget(b.definition.foodUse*b.level,horizon):0);
        double FoodTarget(double upkeep,double horizon)=>Math.Max(upkeep*horizon,civilisation.Settings.cargoCapacity*.25);
        double InputTarget(MaterialQuantity input,IndustryRecipe recipe,RtsBuilding producer,double horizon)=>producer.definition.id=="civil_launch_pad"&&input.resource=="propellant"?200:Math.Max(civilisation.Settings.cargoCapacity*.25,Math.Max(input.amount*civilisation.Settings.inputBufferBatches,input.amount/recipe.seconds*producer.level*horizon));
        // Reserve idle vehicles for the earliest food shortage across the whole settlement before exports.
        // Fixed installation order otherwise lets a busy early mine starve later housing indefinitely.
        bool DeliverFood(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation warehouse,RtsBuilding home){
            if(warehouse.inventory.Get("food")<=.01)return false;
            SurfaceInstallation recipient=null;RtsBuilding target=null;double earliest=double.MaxValue,requested=0;int bestPriority=int.MaxValue;
            foreach(var item in colony.installations){
                if(item.id==warehouse.id||Busy(colony,item.id,"food"))continue;
                var building=Building(local,item.id);if(building==null||!building.Complete)continue;
                double upkeep=building.definition.foodUse*building.level+(building.definition.housingCapacity>0?Math.Min(colony.population,building.definition.housingCapacity*building.level)*civilisation.Settings.populationFoodPerSecond:0);
                if(upkeep<=0)continue;double stored=item.inventory.Get("food"),reserve=FoodTarget(upkeep,SupplyHorizon(local,home,building,vehicle.aerial));
                if(stored>=reserve*.5||reserve-stored<=.01||(!vehicle.aerial&&!vehicle.freight&&!local.Roads.Connected(home,building)))continue;
                int priority=CivilisationRuntime.RecoveryEssential(building.definition)?0:building.definition.housingCapacity>0?1:2;double seconds=stored/upkeep;if(priority>bestPriority||priority==bestPriority&&seconds>=earliest)continue;bestPriority=priority;earliest=seconds;recipient=item;target=building;requested=reserve-stored;
            }
            return recipient!=null&&Start(local,vehicle,home,target,"food",Math.Min(requested,warehouse.inventory.Get("food")),false,colony);
        }
        // Replenish every consumer before taking optional exports from earlier installations.
        bool DeliverInputs(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation warehouse,RtsBuilding home,bool essentialOnly=false){
            foreach(var item in colony.installations){if(item.id==warehouse.id)continue;var producer=Building(local,item.id);if(producer==null||!producer.Complete)continue;if(essentialOnly&&item.definition!="civil_warehouse"&&!CivilisationRuntime.RecoveryEssential(producer.definition))continue;
                if(item.definition=="civil_warehouse")foreach(string supply in new[]{"construction_materials","food","propellant","water","seeds"}){if(essentialOnly&&supply=="construction_materials")continue;double target=supply=="construction_materials"?civilisation.Settings.aiConstructionReserve: supply=="seeds"?10:100;double shortage=target-item.inventory.Get(supply);if(shortage>target*.5&&warehouse.inventory.Get(supply)>.1&&!Busy(colony,item.id,supply)&&Start(local,vehicle,home,producer,supply,Math.Min(shortage,warehouse.inventory.Get(supply)),false,colony))return true;}
                if(!string.IsNullOrEmpty(producer.definition.retailResource)){string resource=producer.definition.retailResource;double target=RetailTarget(colony,producer,SupplyHorizon(local,home,producer,vehicle.aerial)),stock=item.inventory.Get(resource);if(stock<target*.5&&warehouse.inventory.Get(resource)>.01&&!Busy(colony,item.id,resource)&&Start(local,vehicle,home,producer,resource,Math.Min(target-stock,warehouse.inventory.Get(resource)),false,colony))return true;}
                // Inputs are replenished before exporting output, using finite warehouse stock and reserved vehicle capacity.
                IndustryRecipe recipe=null;foreach(var candidate in civilisation.Catalogue.recipes)if(candidate.id==item.recipe&&candidate.building==item.definition){recipe=candidate;break;}
                if(recipe!=null)foreach(var input in recipe.inputs){double target=InputTarget(input,recipe,producer,SupplyHorizon(local,home,producer,vehicle.aerial)),shortage=target-item.inventory.Get(input.resource);if(item.inventory.Get(input.resource)<target*.5&&shortage>.01&&warehouse.inventory.Get(input.resource)>.01&&!Busy(colony,item.id,input.resource)&&Start(local,vehicle,home,producer,input.resource,Math.Min(shortage,warehouse.inventory.Get(input.resource)),false,colony))return true;}
                if(item.definition.StartsWith("civil_farm_")){
                    foreach(string input in new[]{"seeds","water","fertiliser"}){double minimum=input=="water"?30:input=="seeds"?3:2;double shortage=minimum-item.inventory.Get(input);if(item.inventory.Get(input)<minimum*.5&&shortage>.01&&warehouse.inventory.Get(input)>.01&&!Busy(colony,item.id,input)&&Start(local,vehicle,home,producer,input,Math.Min(shortage,warehouse.inventory.Get(input)),false,colony))return true;}
                }
            }return false;
        }
        double RegionalReserve(SurfaceInstallation item,string resource)=>item.definition=="civil_launch_pad"?(resource=="construction_materials"?120:resource=="propellant"?200:resource=="orbital_kit"?64:0):item.definition!="civil_warehouse"?0:resource=="construction_materials"?civilisation.Settings.aiConstructionReserve:resource=="food"||resource=="propellant"||resource=="water"?100:resource=="seeds"?10:0;
        bool CollectFood(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation warehouse,RtsBuilding home){
            double demand=colony.population*civilisation.Settings.populationFoodPerSecond;foreach(var b in local.Buildings)if(b.team==colony.faction&&!b.removed)demand+=b.definition.foodUse*b.level;
            if(warehouse.inventory.Get("food")>=Math.Max(civilisation.Settings.cargoCapacity,demand*civilisation.Settings.logisticsReserveSeconds))return false;
            foreach(var item in colony.installations){if(item.id==warehouse.id||Busy(colony,item.id,"food"))continue;var b=Building(local,item.id);if(b==null||!b.Complete||b.awaitingProjectMaterials)continue;
                double upkeep=b.definition.foodUse*b.level+(b.definition.housingCapacity>0?Math.Min(colony.population,b.definition.housingCapacity*b.level)*civilisation.Settings.populationFoodPerSecond:0);
                double surplus=item.inventory.Get("food")-Math.Max(RegionalReserve(item,"food"),FoodTarget(upkeep,SupplyHorizon(local,home,b,vehicle.aerial)));if(surplus>1&&Start(local,vehicle,home,b,"food",surplus,true,colony))return true;
            }return false;
        }
        bool CollectConstruction(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation warehouse,RtsBuilding home){
            if(warehouse.inventory.Get("construction_materials")>=civilisation.Settings.aiConstructionReserve*2)return false;
            int haulers=0;foreach(var cart in colony.cargoVehicles)if(cart.phase!=CargoVehiclePhase.Idle&&cart.resource=="construction_materials")haulers++;
            if(haulers>=Math.Max(1,(colony.cargoVehicles.Count+1)/3))return false;
            RtsBuilding best=null;double amount=0,score=double.MaxValue;
            foreach(var item in colony.installations){if(item.id==warehouse.id)continue;var b=Building(local,item.id);if(b==null||!b.Complete||b.awaitingProjectMaterials)continue;
                double available=item.inventory.Get("construction_materials")-RegionalReserve(item,"construction_materials");int traffic=0;
                foreach(var cart in colony.cargoVehicles)if(cart.phase!=CargoVehiclePhase.Idle&&cart.resource=="construction_materials"&&cart.source==item.id){traffic++;if(cart.phase==CargoVehiclePhase.Collecting)available-=cart.requested;}
                if(available<=civilisation.Settings.cargoCapacity*.25||(!vehicle.aerial&&!vehicle.freight&&!local.Roads.Connected(home,b)))continue;
                double distance=CubeSphere.ArcDistance(local.Roads.Port(home),local.Roads.Port(b),local.World.definition.radius)*(1+traffic);
                if(distance>=score)continue;score=distance;best=b;amount=available;
            }return best!=null&&Start(local,vehicle,home,best,"construction_materials",amount,true,colony);
        }
        void Assign(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle){if(vehicle.manualDispatchForValidation)return;
            var warehouse=Find(colony,vehicle.warehouse);var home=warehouse==null?null:Building(local,warehouse.id);if(home==null||!home.Complete)return;
            if(CollectFood(local,colony,vehicle,warehouse,home)||DeliverFood(local,colony,vehicle,warehouse,home)||DeliverInputs(local,colony,vehicle,warehouse,home,true)||DeliverProjects(local,colony,vehicle,warehouse,home)||CollectConstruction(local,colony,vehicle,warehouse,home)||DeliverInputs(local,colony,vehicle,warehouse,home))return;
            exportCursors.TryGetValue(colony.id,out int cursor);
            for(int scan=0;scan<colony.installations.Count;scan++){int at=(cursor+scan)%colony.installations.Count;var item=colony.installations[at];
                if(item.id==warehouse.id)continue;var producer=Building(local,item.id);if(producer==null||!producer.Complete)continue;
                double upkeep=producer.definition.foodUse*producer.level+(producer.definition.housingCapacity>0?Math.Min(colony.population,producer.definition.housingCapacity*producer.level)*civilisation.Settings.populationFoodPerSecond:0);
                IndustryRecipe recipe=null;foreach(var candidate in civilisation.Catalogue.recipes)if(candidate.id==item.recipe&&candidate.building==item.definition){recipe=candidate;break;}
                foreach(var resource in civilisation.Catalogue.resources){if(item.definition=="civil_launch_pad"&&resource.id=="orbital_kit")continue;double surplus=item.inventory.Get(resource.id)-RegionalReserve(item,resource.id);if(recipe!=null){foreach(var input in recipe.inputs)if(input.resource==resource.id)surplus-=InputTarget(input,recipe,producer,SupplyHorizon(local,home,producer,vehicle.aerial));}if(item.definition.StartsWith("civil_farm_")&&(resource.id=="water"||resource.id=="seeds"||resource.id=="fertiliser"))continue;
                    if(resource.id=="food")surplus-=FoodTarget(upkeep,SupplyHorizon(local,home,producer,vehicle.aerial));
                    if(resource.id==producer.definition.retailResource)surplus=Math.Min(surplus,item.inventory.Get(resource.id)-RetailTarget(colony,producer,SupplyHorizon(local,home,producer,vehicle.aerial)));
                    double minimumLoad=warehouse.inventory.Get(resource.id)<1?.1:civilisation.Settings.cargoCapacity*.25;
                    if(surplus>minimumLoad&&!Busy(colony,item.id,resource.id)&&warehouse.inventory.Capacity(resource.id)-warehouse.inventory.Get(resource.id)>.1&&Start(local,vehicle,home,producer,resource.id,Math.Min(surplus,warehouse.inventory.Capacity(resource.id)-warehouse.inventory.Get(resource.id)),true,colony)){exportCursors[colony.id]=(at+1)%colony.installations.Count;return;}
                }
            }
        }
        bool Start(PlanetRtsBase local,CargoVehicleState vehicle,RtsBuilding warehouse,RtsBuilding producer,string resource,double amount,bool collect,ColonyState colony){
            if(!vehicle.aerial&&!vehicle.freight&&!local.Roads.Connected(warehouse,producer))return false;
            if(vehicle.freight&&!TryFreightRoute(local,warehouse,producer,vehicle.seaborne,vehicle.stops,vehicle.train))return false;
            var road=new[]{local.Roads.Port(warehouse),local.Roads.Port(producer)};
            vehicle.routeMetres=0;vehicle.path.Clear();foreach(var direction in road)vehicle.path.Add(CubeSphere.Encode(direction));double duration=MissionTime(local,vehicle);
            double speed=vehicle.aerial?civilisation.Settings.airCargoSpeed:civilisation.Settings.groundCargoSpeed;
            double fuel=Math.Max(0,duration-civilisation.Settings.cargoHandlingSeconds)*speed*(vehicle.aerial?civilisation.Settings.airFuelPerMetre:civilisation.Settings.groundFuelPerMetre)*2;
            if(Find(colony,warehouse.persistentId)?.inventory.Spend("propellant",fuel)!=true)return false;
            vehicle.returningUndelivered=false;vehicle.navigationVersion=0;vehicle.resource=resource;vehicle.requested=Math.Min(HullCapacity(vehicle),Math.Max(0,amount));vehicle.loaded=0;vehicle.source=collect?producer.persistentId:warehouse.persistentId;vehicle.destination=collect?warehouse.persistentId:producer.persistentId;
            vehicle.departure=civilisation.SimulationTime;vehicle.arrival=vehicle.departure+duration;
            if(collect)vehicle.phase=CargoVehiclePhase.Collecting;
            else {var source=Find(colony,warehouse.persistentId);vehicle.loaded=Math.Min(vehicle.requested,source.inventory.Get(resource));source.inventory.Spend(resource,vehicle.loaded);vehicle.phase=CargoVehiclePhase.Delivering;}
            if(vehicle.freight)BeginFreightLeg(local,vehicle,vehicle.departure);return true;
        }
        internal bool StartFreightForValidation(PlanetRtsBase local,CargoVehicleState vehicle,RtsBuilding warehouse,RtsBuilding destination,string resource,double amount,bool collect=false)=>Start(local,vehicle,warehouse,destination,resource,amount,collect,local.Settlements[warehouse.team]);
    }
}
