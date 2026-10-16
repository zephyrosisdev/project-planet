using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class RtsWorld
    {
        sealed class AiPlan
        {
            public System.Random random;public int cycle,node=-1,routeAnchor=-1,objectiveBuilding=-1,defenceAnchor=-1;public float nextArmy,objectiveSince;public Vector2 objective,objectiveAnchor;public bool hasObjective;
            public readonly List<AiCompound> compounds=new List<AiCompound>();
            public readonly List<AiRiskSite> risks=new List<AiRiskSite>();public AiRiskSite clearance;public bool usedClearance;
            public readonly WallPreviewRouter router=new WallPreviewRouter();public readonly List<int> route=new List<int>();
            public readonly Dictionary<int,float> rejectedNodes=new Dictionary<int,float>();public readonly HashSet<int> fortified=new HashSet<int>();
            public readonly Queue<(string id,Vector2 position,int rotation)> defence=new Queue<(string,Vector2,int)>();
        }
        readonly AiPlan[] aiPlans={new AiPlan(),new AiPlan()};
        public readonly string[] AIActivity={"Starting","Starting"};
        public readonly int[] AIDecisions=new int[2];
        int aiPending,aiPlacementBudget;bool aiThinking;
        PlacementResult AIPlacementAllowed(BuildingDefinition d,int team,Vector2 p,int rotation=0)
        {if(aiThinking&&aiPlacementBudget--<=0)return new PlacementResult(false,"AI placement budget");p=SnapPlacement(d,p,rotation);if(AiRiskBlocked(team,p))return new PlacementResult(false,"Clear lost outpost area before rebuilding");var valid=ValidatePlacement(d,team,p,true,rotation:rotation);if(!valid.valid)return valid;if(!AiPlacementClear(d,team,p,rotation)||!AiPlacementPreservesAccess(d,team,p,rotation))return new PlacementResult(false,"Keep AI access lanes and structure clearance");return valid;}
        bool AiBuild(string id,int team)
        {var f=Factions[team];var d=Catalog.Buildings[id];if(f.tier<d.tier||!f.HasTech(d.requiredTech)||!f.wallet.CanAfford(d.cost)||aiPlacementBudget<=0)return false;
         if(!TryFindPlacement(id,team,out var p))return false;return Construct(id,team,p,true)!=null;}
        void ThinkAI(int team)
        {
            var plan=aiPlans[team];plan.random??=new System.Random(Battle.Settings.seed^(team==0?0x173a:0x5197));plan.cycle++;AIDecisions[team]++;aiPlacementBudget=96;aiThinking=true;
            try{
                var f=Factions[team];var owned=Buildings.Where(b=>!b.removed&&b.team==team).ToArray();bool Has(string id)=>owned.Any(b=>b.definition.id==id);
                foreach(var spent in owned.Where(b=>b.node>=0&&Nodes[b.node].remaining<=.01f&&b.Complete))Sell(spent);
                owned=Buildings.Where(b=>!b.removed&&b.team==team).ToArray();
                var command=owned.FirstOrDefault(b=>b.definition.id=="command");if(command==null)return;
                UpdateAiClearance(team,plan);
                foreach(var enemy in Buildings.Where(b=>!b.removed&&b.team!=team&&Battle.IsKnown(b.position,team)))if(!f.knownEnemyBases.Any(p=>Field.Distance(p,enemy.position)<15))f.knownEnemyBases.Add(enemy.position);
                foreach(var b in owned)if(b.Complete&&b.health<b.MaxHealth*.9f)b.repairing=true;
                bool built=false;AIActivity[team]="Developing "+f.profile;
                float pendingPower=owned.Where(b=>!b.Complete).Sum(b=>b.definition.generation);
                if(f.generation+pendingPower<f.demand+35){string power=f.HasTech("reactor")&&f.wallet.Stock.isotopes>30&&f.wallet.Stock.fuel>300?"reactor":f.HasTech("gas_power")&&f.wallet.Stock.fuel>500?"gas_turbine":"wind";built=AiBuild(power,team)||power!="wind"&&AiBuild("wind",team);AIActivity[team]="Power grid";}
                // A failed essential placement never prevents research, production or expansion.
                if(!built)foreach(string id in new[]{"mine","autocannon","barracks","lab","light_depot","factory","oil","rare","at_turret","aa_turret","point_defence"})if(!Has(id)&&AiBuild(id,team)){built=true;break;}
                string[] preferences=f.profile==StrategicProfile.AirPower?new[]{"aviation","advanced_air","radar"}:f.profile==StrategicProfile.Naval?new[]{"naval","advanced_naval","carrier"}:f.profile==StrategicProfile.Artillery?new[]{"mechanized","artillery","missiles"}:f.profile==StrategicProfile.Economic?new[]{"economy","gas_power","logistics","mechanized"}:new[]{"mechanized","radar","logistics","gas_power","command","heavy_armour","reactor"};
                foreach(var lab in owned.Where(b=>b.Complete&&b.powered&&b.definition.category==BuildingCategory.Research&&b.queue.Count==0)){
                    if(!lab.Upgrading&&lab.level<Math.Min(3,f.tier)&&Catalog.technologies.Any(t=>t.requiredBuilding==lab.definition.id&&t.requiredBuildingLevel>lab.level)&&f.wallet.Stock.materials>UpgradeCost(lab).materials+350)Upgrade(lab);
                    var tech=Catalog.technologies.OrderBy(t=>{int i=Array.IndexOf(preferences,t.id);return i>=0?i:t.tier*20;}).FirstOrDefault(t=>CanResearch(lab,t,out _)&&f.wallet.CanAfford(t.cost));if(tech!=null)EnqueueResearch(lab,tech.id);
                }
                bool expansion=NeedsExpansion(team,owned);
                if(!built&&(expansion||plan.node>=0))built=ExpandAI(team,owned,plan);
                if(f.tier<5&&!command.Upgrading&&f.wallet.Stock.materials>UpgradeCost(command).materials+(expansion?500:250))Upgrade(command);
                if(Has("mine")&&Has("autocannon")&&matchTime>30&&f.wallet.Stock.materials>650)DefendAI(team,owned,plan);
                float reserve=expansion?650:400;if(f.tier<5&&!command.Upgrading)reserve=Math.Max(reserve,UpgradeCost(command).materials+500);
                if(plan.clearance!=null)reserve=200;
                if(!built&&f.wallet.Stock.materials>350){
                    var priorities=new[]{"advanced_lab","strategic_lab","material_storage","fuel_storage","rare_storage","isotope_storage","logistics","repair_depot","field_command","radar_station","battery","ordnance","airbase","helipad","naval_yard","at_turret","aa_turret","sam","point_defence"};
                    foreach(string id in priorities.Concat(Catalog.buildings.Select(d=>d.id)).Distinct()){
                        var d=Catalog.Buildings[id];if(!new[]{"advanced_lab","strategic_lab"}.Contains(id)&&f.wallet.Stock.materials<reserve+d.cost.materials)continue;
                        if(Has(id)||d.terrain==TerrainRule.Node||new[]{"command","outpost","wall","gate","wind","gas_turbine","reactor"}.Contains(id))continue;
                        if(AiBuild(id,team)){built=true;break;}
                    }
                }
                UpdateAiObjective(team,command,plan);
                bool reinforced=false;
                foreach(var producer in owned.Where(b=>b.Complete&&b.powered&&b.definition.production.Length>0&&b.queue.Count<2).OrderBy(b=>plan.clearance!=null&&Battle.GetAiAreaReport(team).observed<Battle.GetAiAreaReport(team).cells*.9f&&b.definition.production.Contains("scout")?0:1)){
                    if(plan.clearance!=null&&reinforced)break;
                    if(f.wallet.Stock.materials<reserve)break;
                    if(plan.clearance==null&&producer.level<Math.Min(3,f.tier)&&!producer.Upgrading&&f.wallet.Stock.materials>UpgradeCost(producer).materials+600)Upgrade(producer);
                    var choices=producer.definition.production.Where(id=>CanProduce(producer,id,out _)&&f.wallet.CanAfford(Catalog.Units[id].cost)).ToArray();if(choices.Length==0)continue;
                    if(plan.clearance!=null){choices=choices.Where(id=>(int)Catalog.Units[id].movement<3&&Catalog.Units[id].damage>0&&Catalog.Units[id].cost.materials<=plan.clearance.reinforcementBudget).ToArray();if(choices.Length==0)continue;}
                    string id=plan.clearance!=null?(choices.Contains("scout")?"scout":choices.OrderBy(u=>Catalog.Units[u].cost.materials).First()):choices[plan.random.Next(choices.Length)];producer.rally=plan.objective;bool queued=EnqueueUnit(producer,id);reinforced|=queued;if(queued&&plan.clearance!=null)plan.clearance.reinforcementBudget-=Catalog.Units[id].cost.materials;
                }
                if((Battle.Settings.spectatorMode||plan.usedClearance)&&(matchTime>=45||plan.clearance!=null)&&matchTime>=plan.nextArmy){
                    plan.nextArmy=matchTime+12;Battle.RequestAiAdvance(team,plan.objective);
                }
                if(plan.clearance!=null)AIActivity[team]="Clearing hostile outpost site · construction held";
            }finally{aiThinking=false;}
        }
        bool NeedsExpansion(int team,RtsBuilding[] owned)
        {
            var mines=owned.Where(b=>b.node>=0&&Nodes[b.node].resource==ResourceKind.Materials).ToArray();
            if(mines.Length<Math.Min(6,1+(int)(matchTime/90)))return true;
            return owned.Any(b=>b.node>=0&&Nodes[b.node].remaining<Math.Max(100,b.definition.extraction*Nodes[b.node].rateMultiplier*b.level*180))||new[]{ResourceKind.Fuel,ResourceKind.RareElements,ResourceKind.Isotopes}.Any(kind=>Factions[team].wallet.Stock[(int)kind]<(kind==ResourceKind.Fuel?300:80)&&!owned.Any(b=>b.node>=0&&Nodes[b.node].resource==kind)&&Catalog.buildings.Any(d=>d.terrain==TerrainRule.Node&&d.nodes.Any(node=>Nodes.Any(n=>n.kind==node&&n.resource==kind))&&Factions[team].HasTech(d.requiredTech)&&Factions[team].tier>=d.tier));
        }
        bool ExpandAI(int team,RtsBuilding[] owned,AiPlan plan)
        {
            if(aiPlacementBudget<=0)return false;
            var f=Factions[team];var anchors=owned.Where(b=>b.Complete&&b.definition.buildRadius>0).ToArray();if(anchors.Length==0||owned.Any(b=>!b.Complete&&b.definition.id=="outpost"))return false;
            if(plan.node<0||Nodes[plan.node].remaining<=0||Nodes[plan.node].claimedBy>=0){
                plan.node=-1;plan.routeAnchor=-1;plan.route.Clear();
                var candidates=Nodes.Where(n=>n.claimedBy<0&&n.remaining>100&&!AiRiskBlocked(team,n.position)&&(!plan.rejectedNodes.TryGetValue(n.id,out float retry)||retry<=matchTime)&&Catalog.buildings.Any(d=>d.terrain==TerrainRule.Node&&d.nodes.Contains(n.kind)&&f.tier>=d.tier&&f.HasTech(d.requiredTech)));
                var node=candidates.OrderBy(n=>(owned.Any(b=>b.node>=0&&Nodes[b.node].resource==n.resource&&Nodes[b.node].remaining<Math.Max(100,b.definition.extraction*Nodes[b.node].rateMultiplier*b.level*180))?-300:n.resource==ResourceKind.Materials&&owned.Count(b=>b.node>=0&&Nodes[b.node].resource==n.resource)<Math.Min(6,1+(int)(matchTime/90))?0:f.wallet.Stock[(int)n.resource]<(n.resource==ResourceKind.Fuel?300:80)?0:800)+anchors.Min(a=>Field.Distance(a.position,n.position))/Math.Max(.5f,n.quality)).FirstOrDefault();
                if(node==null)return false;plan.node=node.id;
            }
            var target=Nodes[plan.node];AIActivity[team]="Expanding to "+target.resource+" node "+target.id;
            var extractor=Catalog.buildings.Where(d=>d.terrain==TerrainRule.Node&&d.nodes.Contains(target.kind)&&f.tier>=d.tier&&f.HasTech(d.requiredTech)).OrderByDescending(d=>d.extraction).First();
            if(anchors.Any(a=>Field.Distance(a.position,target.position)<a.definition.buildRadius-24)&&AIPlacementAllowed(extractor,team,target.position).valid){var built=Construct(extractor.id,team,target.position,true);if(built!=null){plan.node=-1;return true;}}
            if(f.wallet.Stock.materials<360)return false;
            if(plan.clearance!=null&&(AiExpansionBacklogged(team)||f.wallet.Stock.materials<AiExpansionReserve(team,owned)+Catalog.Buildings["outpost"].cost.materials))return false;
            var anchor=anchors.OrderBy(a=>Field.Distance(a.position,target.position)).First();
            if(plan.routeAnchor!=anchor.index||plan.route.Count==0){
                plan.routeAnchor=anchor.index;Vector2 start=Field.TraversableDestination(anchor.position),end=Field.TraversableDestination(target.position);
                if(!plan.router.Find(Field.Map.Index(start),Field.Map.Index(end),Field.Resolution,c=>Field.Deployment.Costs[c]<DeploymentMap.Blocked*.5f,plan.route,out _)){plan.rejectedNodes[target.id]=matchTime+90;plan.node=-1;return false;}
            }
            Vector2 waypoint=anchor.position;foreach(int cell in plan.route){var p=Field.Map.Position(cell);if(Field.Distance(anchor.position,p)>anchor.definition.buildRadius-32)break;waypoint=p;}
            if(Field.Distance(anchor.position,waypoint)<32){plan.rejectedNodes[target.id]=matchTime+90;plan.node=-1;return false;}
            var outpost=Catalog.Buildings["outpost"];
            for(int i=0;i<17;i++){float angle=i*Mathf.PI*.25f;Vector2 p=waypoint+(i==0?Vector2.zero:new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(i<9?8:16));if(AIPlacementAllowed(outpost,team,p).valid&&Construct("outpost",team,p,true)!=null)return true;}
            if(aiPlacementBudget<=0)return false;
            plan.rejectedNodes[target.id]=matchTime+90;plan.node=-1;return false;
        }
        void DefendAI(int team,RtsBuilding[] owned,AiPlan plan)
        {
            if(plan.defence.Count==0){
                var anchors=owned.Where(b=>b.Complete&&b.definition.buildRadius>0&&!plan.fortified.Contains(b.index)).ToArray();if(anchors.Length==0)return;var anchor=anchors[plan.cycle%anchors.Length];
                if(!PlanAiCompound(team,anchor,owned,plan))return;plan.fortified.Add(anchor.index);
            }
            int built=0,examined=0;while(plan.defence.Count>0&&built<2&&examined++<8&&aiPlacementBudget>0){
                var item=plan.defence.Peek();var d=Catalog.Buildings[item.id];if(!Factions[team].wallet.CanAfford(d.cost))return;
                if(item.id=="wall"&&plan.compounds.Any(c=>c.anchor==plan.defenceAnchor&&!AiCompoundGatesReady(c,team))){RepairAiCompoundGates(team,plan);return;}
                if(!AIPlacementAllowed(d,team,item.position,item.rotation).valid)return;
                if(Construct(item.id,team,item.position,true,item.rotation)!=null){plan.defence.Dequeue();built++;}else return;
            }
            if(built>0)AIActivity[team]="Fortifying walls, gates and turrets";
        }
    }
}
