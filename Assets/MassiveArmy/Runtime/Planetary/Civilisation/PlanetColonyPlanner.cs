using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        double nextColonisation;int colonyTeam=-1,colonyTarget=-1,colonyTurn,colonyAttempts;IEnumerator<Vector3> colonySites;
        readonly Dictionary<(int,int),double> failedColonySites=new Dictionary<(int,int),double>();
        internal bool Autonomous(PlanetRtsBase local,int team){var colony=local.Settlements?[team];return colony?.autoBuildOverride==true?colony.autoBuild:State.Faction(team).autonomous;}
        // One strategic expansion/preparation at a time. No unit poses or distant army ticks.
        internal void PlanColonisation(){
            if(ReplayingEconomy||!system.ActiveWorld.StrategyEnabled||!locals.TryGetValue(system.Bodies[0].bodyId,out var home))return;
            if(colonyTeam>=0){
                if(!Autonomous(home.local,colonyTeam)){EndColonyPlan();return;}
                var target=system.WorldForColonyValidation(colonyTarget);if(target?.Ready!=true){if(system.PreparationFailed(colonyTarget)){failedColonySites[(colonyTeam,colonyTarget)]=State.time+600;EndColonyPlan();}return;}
                if(Colony(BodyId(target.definition.bodyId),colonyTeam)!=null){EndColonyPlan();return;}
                if(colonySites==null)colonySites=PlanetStartingRegion.Candidates(target.definition,target.Base.Homes[0]).GetEnumerator();
                if(colonyAttempts++>=128||!colonySites.MoveNext()){failedColonySites[(colonyTeam,colonyTarget)]=State.time+600;EndColonyPlan();return;}
                var point=colonySites.Current;if(target.Base.SettlementTerritoryOwner(point)>=0)return;
                if(RequestColony(target.Base,point,0,colonyTeam,out string reason)){State.Faction(colonyTeam).reason=reason;EndColonyPlan();}return;
            }
            if(State.time<nextColonisation)return;nextColonisation=State.time+15;
            int team=colonyTurn++%home.local.Factions.Length;if(!Autonomous(home.local,team)||home.colonies[team]==null)return;
            var cost=ColonyCost(home.local);cost.materials+=Settings.aiConstructionReserve;
            if(!home.local.Factions[team].wallet.CanAfford(cost)||home.colonies[team].inventory.Get("food")<Settings.colonySupplyFood)return;
            bool pad=false;foreach(var building in home.local.Buildings)if(ColonyPad(home.local,building,team)){pad=true;break;}if(!pad)return;
            int node=system.Galaxy.NodeFor(system.Bodies[0]),homeSystem=system.Galaxy.Nodes[node].system;int best=-1;double distance=double.MaxValue;
            for(int i=1;i<system.Bodies.Length;i++){var body=system.Bodies[i];if(failedColonySites.TryGetValue((team,i),out double retry)&&State.time<retry)continue;if(system.Galaxy.Nodes[system.Galaxy.NodeFor(body)].system!=homeSystem||Colony(BodyId(body.bodyId),team)!=null)continue;
                bool pending=false;foreach(var delivery in State.colonyDeliveries)if(!delivery.delivered&&delivery.destination==body.bodyId&&delivery.faction==team){pending=true;break;}if(pending)continue;
                double range=body.position.RelativeTo(system.Bodies[0].position).Magnitude;if(range<distance){distance=range;best=i;}}
            if(best<0)return;colonyTeam=team;colonyTarget=best;colonyAttempts=0;system.PreparePlanet(best);
        }
        void EndColonyPlan(){colonySites?.Dispose();colonySites=null;colonyTeam=colonyTarget=-1;nextColonisation=State.time+60;}
    }
}
