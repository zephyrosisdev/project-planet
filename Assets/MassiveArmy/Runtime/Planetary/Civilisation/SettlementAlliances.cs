using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [Serializable] public sealed class SettlementAlliance {
        public string name="Capital Commonwealth";
        public int capitalFaction;
        public List<int> members=new List<int>();
        public bool IsCountry=>members.Count>=3;
        public bool Contains(int faction)=>members.Contains(faction);
    }
    public sealed partial class PlanetDevelopment {
        [Header("Starting settlement alliance")]
        public bool alliedStartingSettlements=true;
        [Min(15000)] public float settlementMinimumSeparation=15000;
        [Min(15000)] public float settlementMaximumSeparation=45000;
        internal bool AlliedStart=>alliedStartingSettlements&&!ClassicCountryFixture&&StrategyEnabled&&sessionMode!=PlanetGameMode.Swarm;
    }
    public sealed partial class PlanetRtsBase {
        public bool Allied(int a,int b)=>a==b||World.AlliedStart&&a>=0&&b>=0&&a<Factions.Length&&b<Factions.Length;
        // Settlements keep independent wallets/planners, while their army joins one
        // existing GPU combat side. Zombie side 2 is never remapped by Spawn.
        public int CombatTeam(int faction)=>World.AlliedStart?0:faction;
        void SelectAlliedHomes(){
            for(int team=1;team<Homes.Length;team++){
                Vector3 best=Vector3.zero;float score=float.MaxValue;
                var east=Vector3.Cross(Mathf.Abs(anchor.y)>.95f?Vector3.right:Vector3.up,anchor).normalized;var north=Vector3.Cross(anchor,east);
                float minimum=Mathf.Max(15000,World.settlementMinimumSeparation),maximum=Mathf.Max(minimum,World.settlementMaximumSeparation);
                for(int i=0;i<1536;i++){
                    float angle=i*2.39996323f,distance=Mathf.Lerp(minimum,maximum*.68f,(i%17)/16f);
                    var candidate=(anchor*Mathf.Cos(distance/World.definition.radius)+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*Mathf.Sin(distance/World.definition.radius)).normalized;
                    bool valid=true;for(int other=0;other<team;other++){float gap=CubeSphere.ArcDistance(candidate,Homes[other],World.definition.radius);if(gap<minimum||gap>maximum){valid=false;break;}}
                    if(!valid||!PlanetStartingRegion.ExpansionPlain(World.definition,candidate))continue;
                    float coast=PlanetStartingRegion.CoastDistance(World.definition,candidate);if(coast<score){score=coast;best=candidate;}if(coast<6500)break;
                }
                if(best.sqrMagnitude<.5f)throw new InvalidOperationException("No dry allied settlement site within the configured 15–45 km spacing. Reduce the starting settlement count or use a larger mainland.");
                Homes[team]=best;
            }
        }
        public string AllianceDescription {
            get {var alliance=Civilisation?.State.alliance;if(alliance==null||!World.AlliedStart)return "Independent settlement";
                return alliance.name+" · "+(alliance.IsCountry?"COUNTRY":"ALLIANCE")+" · "+alliance.members.Count+" settlements · Capital: "+(Settlements?[0]?.name??"Your HQ");}
        }
    }
}
