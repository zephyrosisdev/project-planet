using System;
using UnityEngine;

namespace MassiveArmy
{
    // Explicit benchmark/gallery preparation only. Uses normal structure and queue APIs.
    public sealed partial class BattleApp { public void SelectBuildingForUiBenchmark(RtsBuilding building){SelectedBuilding=building;} }
    public static class UiBenchmarkFixture
    {
        public static RtsBuilding Prepare(RtsWorld world,out RtsBuilding laboratory)
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-ui-benchmark")<0&&Array.IndexOf(args,"-ui-gallery")<0&&!Application.isEditor)throw new InvalidOperationException("Fixture requires opt-in");
            var faction=world.Factions[0];faction.tier=3;
            faction.wallet.Capacity=new ResourceCost{materials=100000,fuel=100000,rareElements=100000,isotopes=100000,research=100000};faction.wallet.Stock=faction.wallet.Capacity;
            RtsBuilding Create(string id)
            {
                var existing=world.Buildings.Find(b=>b.team==0&&!b.removed&&b.definition.id==id);if(existing!=null)return existing;
                Vector2 home=world.BasePosition(0);
                for(int ring=1;ring<=12;ring++)for(int i=0;i<32;i++)
                {
                    float angle=i*Mathf.PI/16;Vector2 point=home+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(40+ring*15);
                    var building=world.Construct(id,0,point,true);if(building==null)continue;
                    building.construction=building.definition.time;building.health=building.MaxHealth;building.paused=true;return building;
                }
                throw new InvalidOperationException("No fixture foundation for "+id);
            }
            var producer=Create("barracks");laboratory=Create("lab");
            for(int i=0;i<24;i++)world.EnqueueUnit(producer,producer.definition.production[0]);
            foreach(var tech in world.Catalog.technologies)if(world.CanResearch(laboratory,tech,out _))world.EnqueueResearch(laboratory,tech.id);
            world.Notice="";return producer;
        }
    }
}
