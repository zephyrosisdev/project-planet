using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        internal Dictionary<long,SurvivalShipState>.ValueCollection NearbyNavigationShips=>clientShips.Values;
        internal long OccupiedNavigationShip=>predicted?.ship?.entity??0;
        internal bool TryOwnedShipNavigation(out UniversePosition position,out long entity){
            position=default;entity=0;if(latest==null||universe==null)return false;var beacon=latest.shipNavigation;
            if(beacon.entity<=0||beacon.entity!=latest.ownedShip||beacon.pose.frame!=0)return false;
            var body=universe.Definition(beacon.pose.body);if(body==null)return false;
            var point=clientShips.TryGetValue(beacon.entity,out var ship)&&ship.pose.body==beacon.pose.body&&presentedShips.TryGetValue(beacon.entity,out var view)?view.point:SurvivalVehicleSimulation.Position(beacon.pose);
            position=body.position.Add(point);entity=beacon.entity;return true;
        }
        internal bool TryNearbyShipNavigation(SurvivalShipState ship,out UniversePosition position){
            position=default;var body=universe?.Definition(ship.pose.body);if(body==null||ship.entity<=0)return false;
            var point=presentedShips.TryGetValue(ship.entity,out var presented)?presented.point:SurvivalVehicleSimulation.Position(ship.pose);position=body.position.Add(point);return true;
        }
    }
}
