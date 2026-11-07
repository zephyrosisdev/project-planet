using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        // Installation views forward to one colony stockpile, but serialize no duplicate stock.
        // Legacy stock which does not fit is retained separately and drained as space becomes free.
        internal void ShareInstallation(ColonyState c,SurfaceInstallation item)=>BindSharedInstallation(Catalogue,c,item);
        internal static void BindSharedInstallation(CivilisationCatalogue catalogue,ColonyState c,SurfaceInstallation item){
            if(ReferenceEquals(item.inventory,c.inventory))item.inventory=new ResourceInventory();
            foreach(var stock in item.inventory.stock)if(stock.amount>0){var found=c.transportReserve.stock.Find(x=>x.resource==stock.resource);if(found==null)c.transportReserve.stock.Add(new MaterialQuantity(stock.resource,stock.amount));else found.amount+=stock.amount;}
            item.inventory.stock.Clear();item.inventory.Bind(catalogue);item.inventory.Share(c.inventory);
        }
        internal void PrepareSharedEconomy(ColonyState c){
            c.transportReserve??=new ResourceInventory();c.transportReserve.Bind(Catalogue);
            for(int i=c.cargoVehicles.Count-1;i>=0;i--){var v=c.cargoVehicles[i];if(v.funeral||v.ambient)continue;if(!string.IsNullOrEmpty(v.resource)&&v.loaded+v.buffered+v.transferred>0){var entry=c.transportReserve.stock.Find(x=>x.resource==v.resource);if(entry==null)c.transportReserve.stock.Add(new MaterialQuantity(v.resource,v.loaded+v.buffered+v.transferred));else entry.amount+=v.loaded+v.buffered+v.transferred;}c.cargoVehicles.RemoveAt(i);}
            foreach(var order in c.cargoOrders){double refund=order.train?Settings.trainCargoCost:order.freight?(order.seaborne?Settings.shipCargoCost:Settings.freightAircraftCost):order.aerial?Settings.airCargoVehicleMaterialCost:Settings.cargoVehicleMaterialCost;var entry=c.transportReserve.stock.Find(x=>x.resource=="construction_materials");if(entry==null)c.transportReserve.stock.Add(new MaterialQuantity("construction_materials",refund));else entry.amount+=refund;}c.cargoOrders.Clear();
            for(int i=c.transportReserve.stock.Count-1;i>=0;i--){var entry=c.transportReserve.stock[i];entry.amount-=c.inventory.Deposit(entry.resource,entry.amount);if(entry.amount<1e-8)c.transportReserve.stock.RemoveAt(i);}
        }
        internal void TickAmbientTraffic(PlanetRtsBase local,ColonyState c){
            double now=SimulationTime;int count=0,populated=0,ordinal=0;foreach(var colony in local.Settlements)if(colony!=null){if(colony==c)ordinal=populated;populated++;}
            populated=Math.Max(1,populated);int extra=Settings.ambientCartLimit%populated;int day=(int)(now/Math.Max(60,Settings.settlementDaySeconds));int rank=(ordinal+day)%populated;int cap=Math.Min(Settings.ambientCartsPerSettlement,Settings.ambientCartLimit/populated+(rank<extra?1:0));
            int kept=0;for(int i=c.cargoVehicles.Count-1;i>=0;i--)if(c.cargoVehicles[i].ambient&&kept++>=cap)c.cargoVehicles.RemoveAt(i);
            foreach(var v in c.cargoVehicles)if(v.ambient){count++;if(count>cap){if(v.ambientVisible){v.ambientVisible=false;v.navigationVersion=0;}continue;}
                if(v.phase!=CargoVehiclePhase.Idle){if(local.CargoNavigation?.Arrived(v)!=true)continue;v.ambientVisible=false;v.phase=CargoVehiclePhase.Idle;v.navigationVersion=0;v.ambientNext=now+Math.Max(5,Settings.ambientCartInterval);continue;}if(now<v.ambientNext)continue;
                v.ambientVisible=false;v.phase=CargoVehiclePhase.Idle;v.ambientNext=now+Settings.ambientCartInterval*(.5+SettlementDailySchedule.Unit((ulong)v.id+(ulong)(now/Math.Max(1,Settings.ambientCartInterval))));
                RtsBuilding a=local.FindBuilding(v.destination),b=null;int n=local.Buildings.Count;if(n==0)continue;int start=(int)(SettlementDailySchedule.Hash((ulong)v.id+(ulong)now)% (uint)n);
                for(int probe=0;probe<Math.Min(64,n);probe++){var candidate=local.Buildings[(start+probe)%n];if(candidate.team!=c.faction||candidate.removed||!candidate.Complete||local.Roads.Node(local.Roads.Port(candidate))<0)continue;if(a==null)a=candidate;else if(candidate!=a&&local.Roads.Connected(a,candidate)){b=candidate;break;}}
                if(a==null||b==null)continue;v.warehouse=a.persistentId;v.source=a.persistentId;v.destination=b.persistentId;v.phase=CargoVehiclePhase.Delivering;v.ambientVisible=true;v.navigationVersion=0;
            }
            if(count<cap&&now>=c.nextAmbientCart){c.nextAmbientCart=now+Math.Max(5,Settings.ambientCartInterval);c.cargoVehicles.Add(new CargoVehicleState{id=State.AllocateId(),ambient=true,ambientNext=now});}
        }
    }
}
