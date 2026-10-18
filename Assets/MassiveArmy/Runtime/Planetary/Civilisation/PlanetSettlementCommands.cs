using System.Linq;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsBase {
        internal void RefreshSettlementPower(int team){var faction=Factions[team];faction.generation=faction.demand=0;faction.wallet.Capacity=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};foreach(var b in Buildings)if(b.team==team&&!b.removed&&b.Complete){if(b.definition.storage>0){int resource=b.definition.id=="fuel_storage"?1:b.definition.id=="rare_storage"?2:b.definition.id=="isotope_storage"?3:0;faction.wallet.Capacity[resource]+=b.definition.storage*b.level;}if(!b.powered)continue;faction.generation+=b.definition.generation*(Civilisation==null?1:b.FoodEfficiency);faction.demand+=b.definition.consumption;}faction.efficiency=faction.demand<=0?1:Mathf.Clamp(faction.generation/faction.demand,Civilisation==null?.25f:0,1);}
        internal void AdvanceDormantInfrastructure(float elapsed){
            foreach(var b in Buildings){if(b.removed)continue;var faction=Factions[b.team];if(!b.Complete){b.construction=Mathf.Min(b.definition.time,b.construction+elapsed*faction.efficiency*Mathf.Min(1,b.constructionWorkers));continue;}if(!b.powered||b.paused)continue;
                if(b.Upgrading){b.upgradeProgress+=elapsed*b.Efficiency;if(b.upgradeProgress>=b.upgradeTime){b.level++;b.upgradeTime=0;b.health=b.MaxHealth;}}
                if(b.definition.researchRate>0)faction.wallet.Deposit(ResourceKind.Research,b.definition.researchRate*b.Efficiency*elapsed);
                if(b.queue.Count==0)continue;var order=b.queue[0];order.progress=Mathf.Min(order.duration,order.progress+elapsed*b.Efficiency);if(order.research&&order.progress>=order.duration){faction.technologies.Add(order.id);faction.researching.Remove(order.id);b.queue.RemoveAt(0);}
                // Army queues remain GPU-owned and spawn only when the local tactical world resumes.
            }NotifyChanged();
        }
        public SettlementCargoNavigation CargoNavigation {get;internal set;}
        public bool RoadPlacing {get;private set;}bool roadStarted;Vector3 roadStart;
        public void BeginRoadPlacement(){CancelPlacement();RoadPlacing=true;roadStarted=false;Notice="ROAD: click the start, then the end. Right-click cancels. Roads cost construction materials.";}
        public void CancelRoadPlacement(){RoadPlacing=false;roadStarted=false;}
        public void RoadClick(Vector3 point){if(!roadStarted){roadStart=point;roadStarted=true;Notice="Choose road end";return;}if(Roads!=null&&Roads.Place(roadStart,point,0,out var reason)){Notice=reason;roadStart=point;}else Notice="Road placement failed: check water, buildings, terrain slope and available materials";}
        public bool ConnectSelectedToWarehouse(){if(Selected==null||Selected.team!=0||Roads==null)return false;var warehouse=Buildings.FirstOrDefault(b=>b.team==0&&b.Complete&&b.definition.id=="civil_warehouse");if(warehouse==null){Notice="Build a warehouse first";return false;}bool ok=Roads.Connect(Selected,warehouse,out var reason);Notice=reason;return ok;}
    }
}
