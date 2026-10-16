using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        sealed class AiRiskSite{public Vector2 position;public int losses;public bool cleared;public float retryAfter,safeSince=-1,reinforcementBudget,budgetTime;public uint lastSample;}
        public bool AiClearing(int team)=>aiPlans[team].clearance!=null;
        public int AiLostSites(int team)=>aiPlans[team].risks.Count;
        bool AiControlled(int team)=>Battle.Settings.spectatorMode||team==1&&Battle.Settings.enemyAI;
        bool AiRiskBlocked(int team,Vector2 p)=>aiPlans[team].risks.Any(r=>(!r.cleared||matchTime<r.retryAfter)&&Field.Distance(r.position,p)<160);
        bool AiExpansionBacklogged(int team)=>aiPlans[team].risks.Count(r=>!r.cleared)>=3;
        float AiExpansionReserve(int team,RtsBuilding[] owned)
        {
            var f=Factions[team];float reserve=650;var command=owned.FirstOrDefault(b=>b.definition.id=="command");
            if(f.tier<5&&command!=null&&!command.Upgrading)reserve=Mathf.Max(reserve,UpgradeCost(command).materials+500);
            // Protect unlocked research/strategic capital, including missile silos,
            // before committing funds to another speculative forward post.
            foreach(var d in Catalog.buildings)if((d.category==BuildingCategory.Research||d.category==BuildingCategory.Strategic)&&d.tier<=f.tier&&f.HasTech(d.requiredTech)&&!owned.Any(b=>b.definition.id==d.id))reserve=Mathf.Max(reserve,d.cost.materials+400);
            return reserve;
        }
        void RegisterAiOutpostLoss(RtsBuilding b)
        {
            if(b.definition.id!="outpost"||!AiControlled(b.team))return;var plan=aiPlans[b.team];
            var site=plan.risks.FirstOrDefault(r=>Field.Distance(r.position,b.position)<160);
            if(site==null){if(plan.risks.Count>=64){var retired=plan.risks.FirstOrDefault(r=>r.cleared);if(retired!=null)plan.risks.Remove(retired);}site=new AiRiskSite{position=b.position};plan.risks.Add(site);}
            site.losses++;site.cleared=false;site.safeSince=-1;site.lastSample=0;site.retryAfter=matchTime+Mathf.Min(120,30*site.losses);
            plan.fortified.Remove(b.index);plan.compounds.RemoveAll(c=>c.anchor==b.index);if(plan.defenceAnchor==b.index){plan.defence.Clear();plan.defenceAnchor=-1;}
            plan.node=-1;plan.routeAnchor=-1;plan.route.Clear();
            // Do not oscillate between multiple attacked sites: finish the current one.
            if(plan.clearance==null)StartAiClearance(b.team,plan,site);
        }
        void StartAiClearance(int team,AiPlan plan,AiRiskSite site)
        {plan.clearance=site;plan.usedClearance=true;plan.hasObjective=false;plan.nextArmy=0;site.budgetTime=matchTime;site.reinforcementBudget=Mathf.Min(400,Mathf.Max(0,Factions[team].wallet.Stock.materials-200)*.25f);Battle.SetAiAreaQuery(team,site.position,200,true);}
        void UpdateAiClearance(int team,AiPlan plan)
        {
            if(plan.clearance==null){var pending=plan.risks.FirstOrDefault(r=>!r.cleared);if(pending!=null)StartAiClearance(team,plan,pending);return;}
            var site=plan.clearance;var report=Battle.GetAiAreaReport(team);
            // Spending quota, not free resources: preserve income for tech/capital.
            site.reinforcementBudget=Mathf.Min(400,site.reinforcementBudget+Mathf.Max(0,matchTime-site.budgetTime)*5);site.budgetTime=matchTime;
            if(!report.valid||Battle.Tick-report.tick>90){site.safeSince=-1;return;}
            if(report.tick==site.lastSample)return;site.lastSample=report.tick;
            bool safe=report.hostiles==0&&report.combatants>=4&&report.cells>0&&report.observed>=report.cells*.9f;
            if(!safe){site.safeSince=-1;return;}
            if(site.safeSince<0){site.safeSince=matchTime;return;}
            if(matchTime-site.safeSince<Mathf.Min(60,12*site.losses)||matchTime<site.retryAfter)return;
            site.cleared=true;plan.clearance=null;plan.hasObjective=false;plan.nextArmy=0;Battle.SetAiAreaQuery(team,site.position,200,false);
            var next=plan.risks.FirstOrDefault(r=>!r.cleared);if(next!=null)StartAiClearance(team,plan,next);
        }
    }
}
