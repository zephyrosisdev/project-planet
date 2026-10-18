using System;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        void GatherSupplies(PlanetRtsBase local,SurfaceInstallation site,RtsBuilding building,double seconds){
            var planet=State.Planet(Colony(BodyId(local.World.definition.bodyId),building.team).body);var cell=local.Grid.Cell(CubeSphere.Decode(building.position));string key=cell.face+":"+(cell.x/32)+":"+(cell.y/32);
            var region=planet.gatheringRegions.Find(r=>r.key==key);if(region==null){region=new GatheringRegion{key=key};planet.gatheringRegions.Add(region);}
            double wood=Math.Min(region.timber,seconds*.5),scrap=Math.Min(region.scrap,seconds*.2);region.timber-=site.inventory.Deposit("timber",wood);region.scrap-=site.inventory.Deposit("scrap",scrap);
        }
        internal void SponsorProject(PlanetRtsBase local,RtsBuilding building){
            var c=local.Settlements?[building.team];if(c==null)return;bool business=building.definition.district==SettlementDistrict.Agricultural||building.definition.district==SettlementDistrict.Industrial||building.definition.district==SettlementDistrict.Commercial;
            ResidentState sponsor=null,contractor=null;foreach(var p in c.residents)if(p.alive&&p.age>=18){if(sponsor==null||p.savings>sponsor.savings)sponsor=p;if(contractor==null||p.education>contractor.education)contractor=p;}
            if(contractor==null)return;double fee=5;
            if(business&&sponsor!=null&&sponsor!=contractor&&sponsor.savings>=fee){sponsor.savings-=fee;contractor.savings+=fee;c.council.decision="Private project: "+building.definition.name;}
            else if(c.council.treasury>=fee){c.council.treasury-=fee;contractor.savings+=fee;c.council.infrastructureSpent+=fee;c.council.decision="Community project: "+building.definition.name;}
        }
    }
}
