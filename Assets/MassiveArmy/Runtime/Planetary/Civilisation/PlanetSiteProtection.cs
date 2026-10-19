using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        readonly double[] nextResponse=new double[2];readonly int[] recoveryCursor=new int[2];double nextRecoveryThink;ProtectionSite placingProtection;
        double ProtectionTime=>Civilisation?.SimulationTime??fixture.SimulationTicks*BattleSettings.Tick;
        internal void RecordSiteAttack(RtsBuilding b,bool lethal){
            if(!fixture.StrategyEnabled||Civilisation==null||Settlements?[b.team]==null)return;
            if(b.persistentId==0)b.persistentId=Civilisation.State.AllocateId();
            var colony=Settlements[b.team];
            // Keep surviving fortifications when the rebuilt main structure is hit again.
            var reusable=colony.protectionSites.Find(old=>old.finished&&!old.cancelled&&old.project==b.persistentId&&old.fortifications.Count==5&&old.fortifications.TrueForAll(id=>ProtectionBuilding(id)?.Complete==true));if(reusable!=null)reusable.finished=false;
            var site=colony.protectionSites.Find(s=>!s.cancelled&&!s.finished&&(s.original==b.persistentId||b.protectionSite!=0&&s.original==b.protectionSite));
            if(site==null){colony.protectionSites.RemoveAll(s=>s.finished||s.cancelled);if(colony.protectionSites.Count>=32)return;
                site=new ProtectionSite{original=b.persistentId,definition=b.definition.id,position=b.position,rotation=b.rotation,node=b.node};colony.protectionSites.Add(site);}
            site.lastHit=ProtectionTime;site.status="Under attack: sending available infantry";
            if(lethal&&b.persistentId==site.original&&b.definition.id!="command")site.destroyed=true;
            b.repairing=!lethal;
            if(ProtectionTime>=nextResponse[b.team]){nextResponse[b.team]=ProtectionTime+2;fixture.EncounterForValidation?.WakeDefenceOrders();fixture.Commands?.RespondToSite(b.team,ProtectionRally(site,b.team),1800,fixture.definition.radius);}
        }
        internal bool TryProtectionTarget(int team,out Vector3 target){
            target=default;if(!fixture.StrategyEnabled)return false;var colony=Settlements?[team];if(colony==null)return false;ProtectionSite best=null;
            foreach(var site in colony.protectionSites)if(!site.cancelled&&!site.finished&&(site.destroyed||ProtectionTime-site.lastHit<45)&&(best==null||site.lastHit>best.lastHit))best=site;
            if(best==null)return false;target=ProtectionRally(best,team);return true;
        }
        Vector3 ProtectionRally(ProtectionSite site,int team){var centre=CubeSphere.Decode(site.position);var tangent=Homes[team]-centre*Vector3.Dot(Homes[team],centre);if(tangent.sqrMagnitude<1e-10f)tangent=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre);return (centre+tangent.normalized*70/fixture.definition.radius).normalized;}
        internal void CancelSiteRecovery(RtsBuilding b){var colony=Settlements?[b.team];if(colony==null)return;foreach(var site in colony.protectionSites)if(site.original==b.persistentId||site.original==b.protectionSite)site.cancelled=true;}
        bool RecoveryNodeReserved(int node,int team){var colony=Settlements?[team];if(colony==null)return false;foreach(var site in colony.protectionSites)if(site.destroyed&&!site.cancelled&&!site.finished&&site.node==node&&site!=placingProtection)return true;return false;}
        internal void RefundPendingProject(RtsBuilding b){var item=SettlementLogistics.Find(Settlements[b.team],b.persistentId);var destination=ConstructionWarehouse(b.team,CubeSphere.Decode(b.position));if(item==null||destination==null||item==destination)return;foreach(var resource in Civilisation.Catalogue.resources){double returned=destination.inventory.Deposit(resource.id,item.inventory.Get(resource.id));item.inventory.Spend(resource.id,returned);}}
        bool ProtectionAccess(BuildingDefinition definition,Vector3 point,int team)=>placingProtection!=null&&(definition.id=="wall"||definition.id=="autocannon")&&Distance(point,CubeSphere.Decode(placingProtection.position))<120;
        internal List<MaterialQuantity> ProjectMaterials(RtsBuilding b){var result=new List<MaterialQuantity>();
            foreach(var resource in Civilisation.Catalogue.resources)if(resource.legacy>=0&&resource.legacy<4&&b.definition.cost[resource.legacy]>0)result.Add(new MaterialQuantity(resource.id,b.definition.cost[resource.legacy]));return result;
        }
        internal bool ConsumeProjectMaterials(RtsBuilding b){var colony=Settlements?[b.team];var item=colony==null?null:SettlementLogistics.Find(colony,b.persistentId);if(item==null)return false;
            var materials=ProjectMaterials(b).ToArray();if(!item.inventory.Spend(materials))return false;b.awaitingProjectMaterials=false;Civilisation.SponsorProject(this,b);NotifyChanged();return true;
        }
        RtsBuilding ProtectionBuilding(long id)=>Buildings.Find(b=>b.persistentId==id&&!b.removed);
        RtsBuilding CreateProtectionProject(ProtectionSite site,int team,string id,Vector3 point,int rotation){
            var definition=Catalog.Buildings[id];point=SnapBuilding(definition,point,rotation);placingProtection=site;PlacementResult valid;
            try{valid=Validate(definition,point,rotation,true,team);}finally{placingProtection=null;}
            if(!valid.valid||!fixture.IsFootprintClear(point,FootprintRadius(definition)))return null;
            if(!Factions[team].wallet.Spend(new ResourceCost{research=definition.cost.research}))return null;
            var building=Create(definition,point,rotation,false,team);building.persistentId=Civilisation.State.AllocateId();var installation=new SurfaceInstallation{id=building.persistentId,definition=id,position=building.position};installation.inventory.Bind(Civilisation.Catalogue);Settlements[team].installations.Add(installation);Civilisation.Planet(fixture.definition.bodyId).surfaceInstallations.Add(installation.id);building.node=valid.node;building.protectionSite=site.original;building.awaitingProjectMaterials=true;
            if(building.node>=0)Nodes[building.node].claimedBy=building.index;fixture.RefreshBuildingCosts(Buildings);Rebase();return building;
        }
        internal void TickSiteProtection(){
            if(!fixture.StrategyEnabled||Civilisation==null||Settlements==null||ProtectionTime<nextRecoveryThink)return;nextRecoveryThink=ProtectionTime+3;
            for(int team=0;team<2;team++){var colony=Settlements[team];if(colony==null||colony.protectionSites.Count==0)continue;
                var site=colony.protectionSites[recoveryCursor[team]++%colony.protectionSites.Count];if(site.finished||site.cancelled)continue;
                if(!site.destroyed){var original=ProtectionBuilding(site.original);if(original!=null&&ProtectionTime-site.lastHit>45){site.finished=original.health>=original.MaxHealth*.99f;}continue;}
                if(ProtectionTime-site.lastHit<20||ProtectionTime<site.nextAttempt){site.status="Defenders securing site before reconstruction";continue;}
                var project=ProtectionBuilding(site.project);
                if(site.project!=0&&project==null){site.project=0;site.nextAttempt=ProtectionTime+30;site.status="Project lost: regrouping before retry";continue;}
                if(project!=null){
                    if(!project.Complete){site.status=project.awaitingProjectMaterials?"Waiting for physical construction deliveries":"Engineers constructing "+project.definition.name;
                        var home=ConstructionWarehouse(team,CubeSphere.Decode(project.position));var anchor=home==null?null:ProtectionBuilding(home.id);if(anchor!=null&&!Roads.Connected(project,anchor))Roads.ConnectToNetwork(project,anchor,out _);continue;}
                    if(site.stage==5){site.finished=true;site.status="Site rebuilt and protected";continue;}
                    site.fortifications.Add(project.persistentId);site.project=0;site.stage++;
                }
                if(site.node>=0&&(Nodes[site.node].remaining<=0||Nodes[site.node].claimedBy>=0)){site.cancelled=true;site.status="Deposit depleted or claimed: recovery cancelled";continue;}
                string id=site.stage==0?"autocannon":site.stage<5?"wall":site.definition;var centre=CubeSphere.Decode(site.position);
                if(site.stage<5){var originalDefinition=Catalog.Buildings[site.definition];var area=Grid.Area(originalDefinition,centre,site.rotation);bool placed=false;
                    for(int attempt=0;attempt<12&&!placed;attempt++){int corner=site.stage==0?attempt%4:site.stage-1;float gap=3+attempt/4;double x=area.x+((corner&1)==0?-gap:area.width+gap),y=area.y+((corner&2)==0?-gap:area.depth+gap);
                        var candidate=Grid.Point(area.face,x,y);project=CreateProtectionProject(site,team,id,candidate,EntranceRotation(Catalog.Buildings[id],candidate,team));placed=project!=null;}
                }else project=CreateProtectionProject(site,team,id,centre,site.rotation);
                if(project!=null){site.project=project.persistentId;site.status="Deliver supplies for "+project.definition.name;}else {site.nextAttempt=ProtectionTime+12;site.status="Waiting for local funds, accessible terrain or a clear project footprint";}
            }
        }
    }
    public sealed partial class SettlementLogistics {
        bool DeliverProjects(PlanetRtsBase local,ColonyState colony,CargoVehicleState vehicle,SurfaceInstallation warehouse,RtsBuilding home){
            if(!colony.protectionSites.Exists(site=>!site.finished&&!site.cancelled&&site.destroyed))return false;
            foreach(var b in local.Buildings)if(b.team==colony.faction&&!b.removed&&b.awaitingProjectMaterials){var project=Find(colony,b.persistentId);if(project==null)continue;
                foreach(var requirement in local.ProjectMaterials(b)){double shortage=requirement.amount-project.inventory.Get(requirement.resource);if(shortage>.01&&!Busy(colony,b.persistentId,requirement.resource)&&warehouse.inventory.Get(requirement.resource)>.01&&Start(local,vehicle,home,b,requirement.resource,Math.Min(shortage,warehouse.inventory.Get(requirement.resource)),false,colony))return true;}
            }return false;
        }
    }
}
