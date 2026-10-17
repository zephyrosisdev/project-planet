using System;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        // Retail represents aggregate resident purchases, with real delivered inventory and no shopper objects.
        internal void ServeRetail(PlanetRtsBase local,ColonyState colony,double dt){
            double food=0,drinks=0,medicine=0;int population=Math.Max(0,colony.population);
            foreach(var b in local.Buildings){if(b.team!=colony.faction||!b.Complete||b.paused||!b.powered||string.IsNullOrEmpty(b.definition.retailResource)||!Logistics.Connected(local,b))continue;
                string resource=b.definition.retailResource;double served=resource=="food"?food:resource=="drinks"?drinks:medicine;
                double customers=Math.Min(Math.Max(0,population-served),100*b.level)*b.Efficiency;if(customers<=0)continue;
                var item=SettlementLogistics.Find(colony,b.persistentId);if(item==null)continue;
                double perPerson=b.definition.retailPerResident*dt,available=item.inventory.Get(resource);
                if(resource=="food")available=Math.Max(0,available-b.definition.foodUse*b.level*30);
                double supplied=perPerson>0?Math.Min(customers,available/perPerson):0;
                if(supplied>0)item.inventory.Spend(resource,supplied*perPerson);
                if(resource=="food")food+=supplied;else if(resource=="drinks")drinks+=supplied;else medicine+=supplied;
            }
            colony.retailFoodCoverage=population>0?food/population:0;colony.retailDrinksCoverage=population>0?drinks/population:0;colony.retailMedicineCoverage=population>0?medicine/population:0;
        }
    }
}
