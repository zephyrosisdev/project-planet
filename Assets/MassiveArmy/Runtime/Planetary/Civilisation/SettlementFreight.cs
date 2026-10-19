using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementLogistics {
        sealed class SeaCheck {public Vector3 a,b;public int next,steps,terrain;public bool blocked;public double checkedAt=-1;}
        readonly Dictionary<(int,long,long),SeaCheck> seaChecks=new Dictionary<(int,long,long),SeaCheck>();
        public double HullCapacity(CargoVehicleState vehicle)=>Math.Max(1,(vehicle.train?civilisation.Settings.trainCargoCapacity:vehicle.seaborne?civilisation.Settings.shipCargoCapacity:vehicle.freight?civilisation.Settings.airFreightCapacity:civilisation.Settings.cargoCapacity)*Math.Pow(2,Math.Clamp(vehicle.tier,1,3)-1)*(1+.25*(Math.Clamp(vehicle.level,1,3)-1)));
        public double Capacity(CargoVehicleState vehicle)=>vehicle.freight&&vehicle.phase!=CargoVehiclePhase.Idle&&vehicle.leg!=1?Math.Max(1,civilisation.Settings.cargoCapacity):HullCapacity(vehicle);
        internal static bool Terminal(RtsBuilding b,bool sea,bool rail=false)=>b!=null&&!b.removed&&b.Complete&&!b.paused&&b.powered&&b.definition.id==(rail?"civil_freight_station":sea?"civil_port":"civil_airport");
        internal Vector3 Berth(PlanetRtsBase local,RtsBuilding terminal){
            var up=CubeSphere.Decode(terminal.position);var east=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);var surface=new PlanetSurface(local.World.definition);Vector3 best=up;float height=surface.SampleHeight(up);
            for(int i=0;i<16;i++){float angle=i*Mathf.PI/8;var p=(up+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*32/local.World.definition.radius).normalized;float value=surface.SampleHeight(p);if(value<height){height=value;best=p;}}return best;
        }
        internal bool OceanRoute(PlanetRtsBase local,RtsBuilding a,RtsBuilding b){
            long low=Math.Min(a.persistentId,b.persistentId),high=Math.Max(a.persistentId,b.persistentId);var key=(local.World.definition.bodyId,low,high);int terrain=local.World.definition.Edits.Revision;
            if(!seaChecks.TryGetValue(key,out var check)||check.terrain!=terrain){var first=Berth(local,a);var last=Berth(local,b);check=new SeaCheck{a=first,b=last,steps=Math.Max(1,(int)Math.Ceiling(CubeSphere.ArcDistance(first,last,local.World.definition.radius)/128)),terrain=terrain};seaChecks[key]=check;}
            // Static infrastructure corridor checking is bounded; live ship poses stay on GPU.
            if(check.blocked)return false;if(check.checkedAt==civilisation.SimulationTime)return check.next>check.steps;check.checkedAt=civilisation.SimulationTime;var surface=new PlanetSurface(local.World.definition);
            for(int work=0;work<16&&check.next<=check.steps;work++,check.next++)if(surface.SampleHeight(Vector3.Slerp(check.a,check.b,(float)check.next/check.steps))>=local.World.definition.oceanHeight-.2f){check.blocked=true;return false;}
            return check.next>check.steps;
        }
        internal bool TryFreightRoute(PlanetRtsBase local,RtsBuilding home,RtsBuilding destination,bool sea,List<long> stops,bool rail=false){
            if(home==null||destination==null||home.team!=destination.team)return false;
            foreach(var from in local.Buildings){if(!Terminal(from,sea,rail)||from.team!=home.team||!local.Roads.Connected(home,from))continue;
                foreach(var to in local.Buildings){if(to==from||!Terminal(to,sea,rail)||to.team!=home.team||!local.Roads.Connected(destination,to))continue;
                    if(rail&&!local.Rails.Connected(from,to))continue;if(sea&&!OceanRoute(local,from,to))continue;
                    if(stops!=null){stops.Clear();stops.Add(home.persistentId);stops.Add(from.persistentId);stops.Add(to.persistentId);stops.Add(destination.persistentId);}return true;
                }
            }return false;
        }
        public bool OrderFreight(PlanetRtsBase local,int team,bool sea,bool rail=false){
            var colony=local.Settlements?[team];if(colony==null||colony.cargoVehicles.Count+colony.cargoOrders.Count>=civilisation.Settings.maximumCargoVehicles)return false;
            foreach(var producer in local.Buildings)if(Terminal(producer,sea,rail)&&producer.team==team&&Connected(local,producer)){
                double cost=rail?civilisation.Settings.trainCargoCost:sea?civilisation.Settings.shipCargoCost:civilisation.Settings.freightAircraftCost;
                var warehouse=RegionalWarehouse(local,colony,producer);if(warehouse==null||!warehouse.inventory.Spend("construction_materials",cost))return false;colony.cargoOrders.Add(new CargoVehicleOrder{producer=producer.persistentId,warehouse=warehouse.id,aerial=!sea&&!rail,seaborne=sea,train=rail,freight=true,tier=UnlockedFreightTier(local,team,sea,rail)});return true;
            }return false;
        }
        public int UnlockedFreightTier(PlanetRtsBase local,int team,bool sea,bool rail=false){string prefix=rail?"civil_heavy_rail":sea?"civil_bulk_shipping":"civil_heavy_airlift";return local.Factions[team].HasTech(prefix+"_3")?3:local.Factions[team].HasTech(prefix+"_2")?2:1;}
        public bool UpgradeCarrier(PlanetRtsBase local,int team,long id){var colony=local.Settlements?[team];var vehicle=colony?.cargoVehicles.Find(v=>v.id==id);if(vehicle==null||vehicle.phase!=CargoVehiclePhase.Idle||vehicle.loaded>1e-6||vehicle.upgradeRemaining>0)return false;
            int tier=vehicle.freight?UnlockedFreightTier(local,team,vehicle.seaborne,vehicle.train):1;int currentTier=Math.Clamp(vehicle.tier,1,3),level=Math.Clamp(vehicle.level,1,3);if(tier<=currentTier&&level>=3)return false;
            double cost=(vehicle.freight?240:80)*(tier>currentTier?currentTier+1:level);if(Find(colony,vehicle.warehouse)?.inventory.Spend("construction_materials",cost)!=true)return false;
            vehicle.pendingTier=tier>currentTier?currentTier+1:currentTier;vehicle.pendingLevel=tier>currentTier?level:level+1;vehicle.upgradeRemaining=30;return true;
        }
        internal static SurfaceInstallation RegionalWarehouse(PlanetRtsBase local,ColonyState colony,RtsBuilding producer){SurfaceInstallation best=null;float distance=float.MaxValue;
            foreach(var item in colony.installations)if(Storage(item)){var building=Building(local,item.id);if(building==null||!building.Complete||!local.Roads.Connected(producer,building))continue;float value=CubeSphere.ArcDistance(CubeSphere.Decode(building.position),CubeSphere.Decode(producer.position),local.World.definition.radius);if(value<distance){distance=value;best=item;}}return best;
        }
        public bool OrderRegionalCart(PlanetRtsBase local,int team,long warehouseId){var colony=local.Settlements?[team];var warehouse=colony==null?null:Find(colony,warehouseId);var producer=warehouse==null?null:Building(local,warehouseId);if(producer==null||!producer.Complete||colony.cargoVehicles.Count+colony.cargoOrders.Count>=civilisation.Settings.maximumCargoVehicles||!warehouse.inventory.Spend("construction_materials",civilisation.Settings.cargoVehicleMaterialCost))return false;
            colony.cargoOrders.Add(new CargoVehicleOrder{producer=warehouseId,warehouse=warehouseId});return true;
        }
        internal bool IsLongHaul(CargoVehicleState vehicle)=>vehicle.freight&&vehicle.leg==1;
        internal double LegSpeed(CargoVehicleState vehicle)=>!IsLongHaul(vehicle)?civilisation.Settings.groundCargoSpeed:vehicle.train?civilisation.Settings.trainCargoSpeed:vehicle.seaborne?civilisation.Settings.shipCargoSpeed:civilisation.Settings.freightAircraftSpeed;
        double LegTime(PlanetRtsBase local,CargoVehicleState vehicle,int leg){
            var from=Building(local,vehicle.stops[leg]);var to=Building(local,vehicle.stops[leg+1]);if(from==null||to==null)return civilisation.Settings.cargoHandlingSeconds;
            float distance=CubeSphere.ArcDistance(local.Roads.Port(from),local.Roads.Port(to),local.World.definition.radius);double speed=leg==1?(vehicle.train?civilisation.Settings.trainCargoSpeed:vehicle.seaborne?civilisation.Settings.shipCargoSpeed:civilisation.Settings.freightAircraftSpeed):civilisation.Settings.groundCargoSpeed;
            return distance/Math.Max(1,speed)+civilisation.Settings.cargoHandlingSeconds;
        }
        double MissionTime(PlanetRtsBase local,CargoVehicleState vehicle){if(!vehicle.freight)return TravelTime(local,vehicle.path,vehicle.aerial,vehicle.routeMetres);if(vehicle.stops.Count!=4)return 0;double time=0;for(int i=0;i<3;i++)time+=LegTime(local,vehicle,i);return time;}
        void ResetFreightLeg(PlanetRtsBase local,CargoVehicleState vehicle,double departure){vehicle.legDeparture=departure;vehicle.legArrival=departure+LegTime(local,vehicle,vehicle.leg);vehicle.navigationVersion=0;vehicle.gpuArrived=false;}
        void StageRoadLoad(CargoVehicleState vehicle){double capacity=Math.Max(1,civilisation.Settings.cargoCapacity);vehicle.buffered+=Math.Max(0,vehicle.loaded-capacity);vehicle.loaded=Math.Min(vehicle.loaded,capacity);vehicle.transferred=0;vehicle.shuttleReturning=false;}
        void BeginFreightLeg(PlanetRtsBase local,CargoVehicleState vehicle,double now){vehicle.leg=0;StageRoadLoad(vehicle);ResetFreightLeg(local,vehicle,now);}
        bool AdvanceFreight(PlanetRtsBase local,CargoVehicleState vehicle,double now){
            if(vehicle.stops.Count!=4)return false;
            // A bulk shipment uses repeated finite-capacity local truck shuttles at each terminal.
            // Buffered/transferred goods remain in shipment custody; they are never another truck's load.
            for(int budget=0;budget<8;budget++){
                if(now<vehicle.legArrival||!civilisation.ReplayingEconomy&&local.CargoNavigation?.Arrived(vehicle)!=true)return false;
                double departure=civilisation.ReplayingEconomy?vehicle.legArrival:now;
                if(vehicle.leg!=1){
                    if(vehicle.shuttleReturning&&vehicle.returningUndelivered){
                        // The last road shuttle physically returned to the receiving terminal. Its
                        // payload and goods still buffered there can now sail/fly/rail back together.
                        long terminal=vehicle.stops[2],originTerminal=vehicle.stops[1],homeId=vehicle.warehouse;
                        vehicle.loaded+=vehicle.buffered+vehicle.transferred;vehicle.buffered=vehicle.transferred=0;vehicle.stops.Clear();vehicle.stops.Add(terminal);vehicle.stops.Add(terminal);vehicle.stops.Add(originTerminal);vehicle.stops.Add(homeId);vehicle.source=terminal;vehicle.destination=homeId;vehicle.leg=1;vehicle.shuttleReturning=vehicle.returningUndelivered=false;vehicle.phase=CargoVehiclePhase.Delivering;ResetFreightLeg(local,vehicle,departure);if(!civilisation.ReplayingEconomy)return false;continue;
                    }
                    if(vehicle.shuttleReturning){vehicle.loaded=Math.Min(vehicle.buffered,Math.Max(1,civilisation.Settings.cargoCapacity));vehicle.buffered-=vehicle.loaded;vehicle.shuttleReturning=false;ResetFreightLeg(local,vehicle,departure);if(!civilisation.ReplayingEconomy)return false;continue;}
                    if(vehicle.leg==2&&vehicle.phase==CargoVehiclePhase.Delivering){
                        var home=Building(local,vehicle.warehouse);if(home==null)return false;var colony=local.Settlements[home.team];var destination=Find(colony,vehicle.destination);if(destination==null||Building(local,vehicle.destination)==null){vehicle.returningUndelivered=true;vehicle.shuttleReturning=true;ResetFreightLeg(local,vehicle,departure);return false;}
                        double accepted=destination.inventory.Deposit(vehicle.resource,vehicle.loaded);vehicle.loaded-=accepted;vehicle.delivered+=accepted;if(Storage(destination)&&accepted>0)DeliveredForValidation?.Invoke(colony.faction,vehicle.resource,accepted);
                        if(vehicle.loaded>1e-6){vehicle.legArrival=now+civilisation.Settings.economyTickSeconds;return false;}
                    }else {vehicle.transferred+=vehicle.loaded;vehicle.loaded=0;}
                    if(vehicle.buffered>1e-6){vehicle.shuttleReturning=true;ResetFreightLeg(local,vehicle,departure);if(!civilisation.ReplayingEconomy)return false;continue;}
                    if(vehicle.leg==0){vehicle.loaded=vehicle.transferred;vehicle.transferred=0;}
                }
                if(vehicle.leg>=2){vehicle.arrival=vehicle.legArrival;return true;}
                vehicle.leg++;if(vehicle.leg==2)StageRoadLoad(vehicle);ResetFreightLeg(local,vehicle,departure);if(!civilisation.ReplayingEconomy)return false;
            }return false;
        }
    }
    public sealed partial class PlanetRtsBase {
        readonly Dictionary<(int,int,bool),int> terminalSearch=new Dictionary<(int,int,bool),int>();
        internal bool BuildTerminalNear(RtsBuilding anchorBuilding,bool sea,bool rail=false){
            var id=rail?"civil_freight_station":sea?"civil_port":"civil_airport";var d=Catalog.Buildings[id];if(!AiCanSpend(anchorBuilding.team,d.cost))return false;
            var centre=CubeSphere.Decode(anchorBuilding.position);var east=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre).normalized;var north=Vector3.Cross(centre,east);var key=(anchorBuilding.team,anchorBuilding.index,sea);terminalSearch.TryGetValue(key,out int cursor);
            for(int work=0;work<8;work++){int index=cursor++;float range=sea?64+(index/16%12)*64:48+(index/16%4)*24,angle=index*2.3999632f;var point=(centre+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*range/fixture.definition.radius).normalized;
                if(ConstructForTeam(id,anchorBuilding.team,point)){terminalSearch[key]=cursor;return true;}}
            terminalSearch[key]=cursor;return false;
        }
        internal bool PlanRegionalRail(RtsBuilding root){
            if(Civilisation==null||root==null)return false;var colony=Settlements[root.team];
            foreach(var warehouse in Buildings)if(!warehouse.removed&&warehouse.team==root.team&&warehouse.Complete&&warehouse!=root&&warehouse.definition.id=="civil_warehouse"&&Roads.Connected(root,warehouse)&&Distance(CubeSphere.Decode(root.position),CubeSphere.Decode(warehouse.position))>300){
                RtsBuilding from=null,to=null;foreach(var station in Buildings)if(!station.removed&&station.team==root.team&&station.definition.id=="civil_freight_station"){
                    if(Distance(CubeSphere.Decode(station.position),CubeSphere.Decode(root.position))<180)from=station;if(Distance(CubeSphere.Decode(station.position),CubeSphere.Decode(warehouse.position))<180)to=station;}
                if(from==null)return BuildTerminalNear(root,false,true);if(to==null)return BuildTerminalNear(warehouse,false,true);
                if(!Roads.Connected(from,root))return Roads.ConnectToNetwork(from,root,out _);if(!Roads.Connected(to,warehouse))return Roads.ConnectToNetwork(to,warehouse,out _);
                if(!Rails.Connected(from,to))return Rails.ConnectToNetwork(to,from,out _);
                int count=0;foreach(var vehicle in colony.cargoVehicles)if(vehicle.train)count++;foreach(var order in colony.cargoOrders)if(order.train)count++;
                if(count<Math.Min(8,1+colony.population/100))return Civilisation.Logistics.OrderFreight(this,root.team,false,true);
            }return false;
        }
        internal bool PlanFreightAccess(RtsBuilding site,RtsBuilding root){
            if(Civilisation==null||site==root)return false;var colony=Settlements[site.team];
            foreach(bool sea in new[]{true,false}){
                RtsBuilding from=null,to=null;
                foreach(var terminal in Buildings)if(SettlementLogistics.Terminal(terminal,sea)&&terminal.team==site.team){if(Roads.Connected(root,terminal))from=terminal;if(Roads.Connected(site,terminal))to=terminal;}
                if(from!=null&&to!=null){if(!sea||Civilisation.Logistics.OceanRoute(this,from,to)){
                    int fleet=0,pending=0;foreach(var carrier in colony.cargoVehicles)if(carrier.freight&&!carrier.train&&carrier.seaborne==sea)fleet++;foreach(var order in colony.cargoOrders)if(order.freight&&!order.train&&order.seaborne==sea)pending++;
                    if(fleet+pending<Math.Min(8,1+colony.population/80)&&Civilisation.Logistics.OrderFreight(this,site.team,sea))return true;
                    if(fleet>0)return Civilisation.Logistics.Connected(this,site);
                }}
                // Complete or unfinished terminals already near an endpoint are reused and joined locally.
                RtsBuilding nearRoot=null,nearSite=null;float rootDistance=900,siteDistance=900;foreach(var terminal in Buildings)if(!terminal.removed&&terminal.team==site.team&&terminal.definition.id==(sea?"civil_port":"civil_airport")){
                    float rd=Distance(CubeSphere.Decode(root.position),CubeSphere.Decode(terminal.position));if(rd<rootDistance){rootDistance=rd;nearRoot=terminal;}
                    float sd=Distance(CubeSphere.Decode(site.position),CubeSphere.Decode(terminal.position));if(sd<siteDistance){siteDistance=sd;nearSite=terminal;}
                }
                if(nearSite==nearRoot&&!Roads.Connected(site,root))nearSite=null;
                if(nearRoot!=null&&!Roads.Connected(nearRoot,root)&&Roads.ConnectToNetwork(nearRoot,root,out _))return true;
                if(nearSite!=null&&!Roads.Connected(nearSite,site)&&Roads.Connect(nearSite,site,out _))return true;
                if(nearRoot==null&&BuildTerminalNear(root,sea))return true;
                if(nearSite==null&&BuildTerminalNear(site,sea))return true;
                // Sea corridors that cross land remain unavailable; aircraft provide the fallback.
            }return false;
        }
    }
}

