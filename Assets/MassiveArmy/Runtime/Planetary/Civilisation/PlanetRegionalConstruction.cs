using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal RegionalSettlement PlanningSettlement;internal RtsBuilding[] RegionalAnchors;
        internal bool InPlanningSettlement(RtsBuilding b)=>b!=null&&!b.removed&&(PlanningSettlement==null||b.settlementId==PlanningSettlement.id||b.persistentId==PlanningSettlement.hub);
        internal bool InPlanningRange(Vector3 point)=>PlanningSettlement==null||CubeSphere.ArcDistance(point,CubeSphere.Decode(PlanningSettlement.position),World.definition.radius)<=Civilisation.Settings.regionalTownRadius;
        internal static bool CommandLandAllowed(int owner,int team)=>owner<0||owner==team;
        internal bool FoundRegionalSettlement(RegionalSettlement town,int team,out string reason){
            var d=Catalog.Buildings["command"];var point=CubeSphere.Decode(town.position);if(!CommandLandAllowed(SettlementTerritoryOwner(point),team)){reason="Another country owns this site";return false;}var valid=Validate(d,point,0,false,team);if(!valid.valid){reason=valid.reason;return false;}
            if(fixture.ResidentActive&&!fixture.IsFootprintClear(point,FootprintRadius(d))){reason="Units occupy the proposed town centre";return false;}
            if(!Factions[team].wallet.Spend(d.cost)){reason="Insufficient founding resources";return false;}
            var b=Create(d,point,0,false,team);b.persistentId=Civilisation.State.AllocateId();b.settlementId=town.id;town.hub=b.persistentId;town.position=b.position;town.status="Founding town centre";RefreshConstructionCosts();Rebase();reason=town.status;return true;
        }
    }
}
