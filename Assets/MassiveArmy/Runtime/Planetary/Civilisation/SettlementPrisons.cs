using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        internal int PrisonOccupancy(ColonyState c,long building){int count=0;foreach(var p in c.residents)if(p.alive&&Confined(p)&&p.crime.facility==building)count++;return count;}
        internal void PlanPrisons(PlanetRtsBase local,ColonyState c){
            if(!c.crime.prisonPlanner||SimulationTime<c.crime.nextPrisonThought)return;c.crime.nextPrisonThought=SimulationTime+20;
            int held=0,awaiting=0;foreach(var p in c.residents)if(p.alive&&Confined(p)){held++;if(p.crime.custody==CustodyStage.AwaitingPrison||p.crime.custody==CustodyStage.FederalPrison)awaiting++;}
            bool Build(string id){if(!local.Catalog.Buildings.ContainsKey(id)||!local.AiCanSpend(c.faction,local.Catalog.Buildings[id].cost))return false;return local.TryConstructInfrastructure(id,c.faction);}
            bool Has(string id)=>local.Buildings.Exists(b=>b.team==c.faction&&!b.removed&&b.definition.id==id);
            if(held>0&&!Has(Court)){Build(Court);return;}if(held>=6&&!Has(CountyJail)){Build(CountyJail);return;}if(awaiting==0)return;
            if(!Has(FederalPrison)){Build(FederalPrison);return;}
            foreach(var root in local.Buildings)if(root.team==c.faction&&!root.removed&&root.definition.id==FederalPrison){var plan=c.crime.prisons.Find(p=>p.building==root.persistentId);if(plan==null){plan=new PrisonPlan{building=root.persistentId};c.crime.prisons.Add(plan);}if(!root.Complete){plan.decision="Finish the secure reception, cell block and perimeter";continue;}
                plan.wings.RemoveAll(id=>local.FindBuilding(id)==null);plan.capacity=16*root.level;foreach(long id in plan.wings){var wing=local.FindBuilding(id);if(wing?.Complete==true)plan.capacity+=16*wing.level;}
                if(plan.yard==0||local.FindBuilding(plan.yard)==null){plan.decision="Build a secure recreation yard";if(local.TryPrisonExtension(root,PrisonYard,out var yard))plan.yard=yard.persistentId;return;}
                if(awaiting>=plan.capacity*.8&&plan.wings.Count<8){plan.decision="Add a staffed cell wing before capacity is exhausted";if(local.TryPrisonExtension(root,PrisonWing,out var wing))plan.wings.Add(wing.persistentId);return;}
                plan.decision="Maintain staffed gates, meals, cells and rehabilitation";
            }
        }
    }
    public sealed partial class PlanetRtsBase {
        internal bool TryPrisonExtension(RtsBuilding root,string id,out RtsBuilding result){result=null;var d=Catalog.Buildings[id];if(!AiCanSpend(root.team,d.cost))return false;var a=Area(root);float radius=Grid.FootprintRadius(root.definition)+Grid.FootprintRadius(d)+Grid.TileSize;
            for(int n=0;n<6;n++){float angle=(float)(n+Civilisation.SimulationTime/20%6)*Mathf.PI/3;var up=Grid.Centre(a);var east=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var p=(up+(east*Mathf.Cos(angle)+Vector3.Cross(up,east)*Mathf.Sin(angle))*radius/World.definition.radius).normalized;if(!ConstructForTeam(id,root.team,p))continue;result=Buildings[Buildings.Count-1];Roads.Connect(result,root,out _);return true;}return false;
        }
    }
}
