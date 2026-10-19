using System;
using System.Collections;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        // Isolated capacity fixture: real placement, payment, curved footprints and obstacle upload.
        internal bool AddCapacityFixtureBuilding(){
            var definition=Catalog.Buildings["civil_fence"];var anchorBuilding=Buildings.Find(b=>b.team==0&&b.definition.id=="command");var area=Area(anchorBuilding);
            for(int y=-20;y<=20;y+=2)for(int x=-20;x<=20;x+=2){var point=Grid.Point(area.face,area.x+area.width*.5+x,area.y+area.depth*.5+y);if(ConstructForTeam(definition.id,0,point))return true;}return false;
        }
    }
    public sealed partial class SettlementHouseholdValidation {
        IEnumerator ValidateGrowthCapacity(){
            var local=world.Base;var simulation=local.Civilisation;var colony=local.Settlements[0];
            // Explicit fixture funding only; the separate long autonomous soak grants nothing.
            local.Factions[0].wallet.Credit(new ResourceCost{materials=10000,fuel=1000});
            while(local.Buildings.Count<150){Check(local.AddCapacityFixtureBuilding(),"Paid construction continues past the old 128-building limit: "+local.Buildings.Count);if(local.Buildings.Count%8==0)yield return null;}
            foreach(var building in local.Buildings)if(building.team==0)building.construction=building.definition.time;
            var target=local.Buildings[local.Buildings.Count-1];var hq=local.Buildings.Find(b=>b.team==0&&b.definition.id=="command");
            Check(local.Roads.ConnectToNetwork(target,hq,out _),"Building above index 128 joins the HQ road network");
            simulation.Advance(1);uint tick=world.SimulationTicks;world.Paused=false;while(world.SimulationTicks<tick+4)yield return null;world.Paused=true;
            var person=colony.residents[0];person.workplace=target.persistentId;simulation.State.time=simulation.RenderTime+600;double time=simulation.State.time;double hour=((time/simulation.Settings.settlementDaySeconds*24+8)%24+24)%24;simulation.State.time+=(10-hour)*simulation.Settings.settlementDaySeconds/24;simulation.Planet(world.definition.bodyId).lastSimulatedTime=simulation.State.time;
            for(int i=0;i<1800;i++){local.Citizens.Step(.25f,true);if(i%30==0)yield return null;}
            var walkers=new Walker[local.Citizens.Count];local.Citizens.WalkerStatesForValidation.GetData(walkers);
            Check(walkers[0].node==local.Roads.Node(local.Roads.Port(target)),"GPU pedestrian reaches a destination above building index 128");
            Check(local.Citizens.FlowStorageBytes<=Mathf.NextPowerOfTwo(local.Roads.Tiles.Count)*128L*8,"Pedestrian route storage remains bounded to 128 local destinations");
            foreach(var node in local.Nodes)if(node.kind==NodeKind.Metal&&string.IsNullOrEmpty(node.elementId))node.remaining=0;
            bool discovered=false;for(int attempt=0;attempt<8&&!discovered;attempt++)discovered=local.SurveyFrontierDeposit("mine",0);
            Check(discovered,"Exhausted starter materials trigger a finite deterministic frontier survey");var planet=simulation.Planet(world.definition.bodyId);var deposit=planet.frontierDeposits[planet.frontierDeposits.Count-1];float reserve=deposit.remaining;deposit.remaining-=1;
            var restored=CivilisationRuntime.Deserialize(simulation.Serialize(),simulation.State.galaxySeed,simulation.Catalogue);var saved=restored.Planet(planet.body).frontierDeposits;
            Check(saved.Count==planet.frontierDeposits.Count&&saved[saved.Count-1].remaining==reserve-1,"Strategic save preserves discovered deposits and their finite remaining reserves");
            Check(!local.SurveyFrontierDeposit("mine",0)&&deposit.remaining==reserve-1,"A usable prospect is reused without refilling its resource reserve");
            for(int i=0;i<20;i++)yield return null;
            Check(report.runtimeErrors==0,"Expanded obstacle, combat and pedestrian buffers produce no runtime errors");
        }
    }
}

