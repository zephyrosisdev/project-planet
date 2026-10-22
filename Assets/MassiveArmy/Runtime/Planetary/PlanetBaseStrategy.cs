using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    // Bounded infrastructure decisions reuse catalogue, wallets, queues and progression.
    // Army movement and combat remain GPU-only. Placement work is capped per decision.
    public sealed partial class PlanetRtsBase
    {
        public readonly int[] AIDecisions=new int[PlanetCountries.Maximum];
        public readonly string[] AIActivity=Enumerable.Repeat("Starting",PlanetCountries.Maximum).ToArray();
        readonly float[] nextThink=new float[PlanetCountries.Maximum];float strategyTime;
        bool playerAutoPilot;
        public bool AutoPilotEnabled=>Civilisation?.State.Faction(0)?.autonomous??playerAutoPilot;
        internal bool BaseAIEnabled(int team)=>Civilisation?.State.Faction(team)?.autonomous??(team==0?playerAutoPilot||fixture.sessionMode==PlanetGameMode.AiTest:fixture.sessionMode!=PlanetGameMode.Swarm);
        public void SetAutoPilot(bool enabled){
            if(fixture.sessionMode==PlanetGameMode.AiTest||!fixture.StrategyEnabled||!Buildings.Any(b=>b.team==0&&!b.removed&&b.Complete&&b.definition.id=="command"))return;
            playerAutoPilot=enabled;if(Civilisation!=null)Civilisation.State.Faction(0).autonomous=enabled;
            nextThink[0]=strategyTime;AIActivity[0]=enabled?"Developing economy and production":"Manual control";
            Notice=enabled?"Auto Pilot enabled: AI manages construction, research and production":"Auto Pilot disabled: manual base control";NotifyChanged();
        }
        public Vector3 TeamOffset(int team,Vector2 metres){var up=Homes[team];var east=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;return (up+(east*metres.x+Vector3.Cross(up,east)*metres.y)/fixture.definition.radius).normalized;}
        void FindEnemyHome(){
            var wanted=Homes[1];for(int attempt=0;attempt<128;attempt++){
                float angle=attempt*2.3999632f;var candidate=(wanted+(right*Mathf.Cos(angle)+forward*Mathf.Sin(angle))*(Mathf.Sqrt(attempt)*65/fixture.definition.radius)).normalized;
                bool clear=true;foreach(var offset in new[]{new Vector2(-65,-55),new Vector2(30,-75),new Vector2(90,-30),new Vector2(-90,15)}){
                    Homes[1]=candidate;string id=offset.x==-65?"command":offset.x==30?"barracks":offset.x==90?"light_depot":"solar";
                    if(!Validate(Catalog.Buildings[id],TeamOffset(1,offset*Grid.PresentationScale),0,false,1).valid){clear=false;break;}}
                if(clear)return;
            }throw new InvalidOperationException("No safe land for the second faction's complete starter base");
        }
        // Four reserves of each original RTS resource, in addition to the elemental deposits.
        void SeedResourceNodes(int team){var kinds=new[]{NodeKind.Metal,NodeKind.Oil,NodeKind.RareEarth,NodeKind.Uranium};
            for(int reserve=0;reserve<4;reserve++)for(int i=0;i<4;i++){
                for(int attempt=0;attempt<48;attempt++){float angle=(reserve*4+i)*2.3999632f+attempt*.61f;float range=reserve==0?155+attempt*2:reserve==1?160+attempt*2:360+reserve*170+attempt*4;
                    var d=TeamOffset(team,(reserve==0&&attempt==0?new Vector2((i-1.5f)*65,115):new Vector2(Mathf.Cos(angle)*range,Mathf.Sin(angle)*range)+(reserve==1?new Vector2(-65,-55):Vector2.zero))*Grid.PresentationScale);
                    if(surface.SampleHeight(d)<fixture.definition.oceanHeight)continue;bool clear=true;foreach(var node in Nodes)if(Distance(d,CubeSphere.Decode(node.position))<40*Grid.PresentationScale){clear=false;break;}if(!clear)continue;
                    Nodes.Add(new ResourceNode{id=Nodes.Count,kind=kinds[i],resource=(ResourceKind)i,position=CubeSphere.Encode(d),capacity=20000,remaining=20000,rateMultiplier=1,quality=1});break;
                }
            }
        }
        internal bool HasProductiveBuilding(string id,int team){foreach(var b in Buildings)if(b.team==team&&!b.removed&&!b.roadSuspended&&b.definition.id==id&&(b.node<0||Nodes[b.node].remaining>0))return true;return false;}
        internal bool ConstructForTeam(string id,int team,Vector3 direction){var d=Catalog.Buildings[id];return ConstructOriented(d,team,direction,Civilisation==null?0:EntranceRotation(d,direction,team));}
        readonly Dictionary<(int,string,int),double> resourceSiteRetries=new Dictionary<(int,string,int),double>();
        internal bool ResourceSiteCooling(string id,int team,int node)=>Civilisation!=null&&resourceSiteRetries.TryGetValue((team,id,node),out double until)&&Civilisation.SimulationTime<until;
        bool BuildAI(string id,int team,int cycle){var d=Catalog.Buildings[id];var f=Factions[team];if(f.tier<d.tier||!f.HasTech(d.requiredTech))return false;
            if(d.terrain==TerrainRule.Node){foreach(var node in Nodes.Where(n=>n.claimedBy<0&&n.remaining>0&&Array.IndexOf(d.nodes,n.kind)>=0&&string.Equals(n.elementId??"",d.extractedResource??"",StringComparison.Ordinal)).OrderBy(n=>Distance(Homes[team],CubeSphere.Decode(n.position))))if((Civilisation==null||OwnDeposit(team,CubeSphere.Decode(node.position)))&&!ResourceSiteCooling(id,team,node.id)){
                if(ConstructForTeam(id,team,CubeSphere.Decode(node.position)))return true;
                if(Civilisation!=null&&f.wallet.CanAfford(d.cost))resourceSiteRetries[(team,id,node.id)]=Civilisation.SimulationTime+60;
            }return false;}
            if(Civilisation!=null)return BuildDistrict(d,team);
            var anchors=Buildings.Where(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.buildRadius>0).ToArray();if(anchors.Length==0)return false;
            for(int i=0;i<24;i++){var b=anchors[(i+cycle)%anchors.Length];var up=CubeSphere.Decode(b.position);var east=Vector3.Cross(Vector3.up,up).normalized;float angle=(i+cycle*7)*2.3999632f,range=65+(i/8)*45;var p=(up+(east*Mathf.Cos(angle)+Vector3.Cross(up,east)*Mathf.Sin(angle))*range/fixture.definition.radius).normalized;if(ConstructForTeam(id,team,p))return true;}return false;
        }
        static readonly string[] orbitalResearchPath={"civil_industry","civil_rocketry","civil_tracking","civil_refining","civil_satellites","civil_astronomy","civil_spectroscopy","civil_communications","civil_probes","civil_navigation","civil_mining","civil_colonisation","civil_logistics","civil_orbital_construction"};
        int ResearchPriority(string id){int rank=Array.IndexOf(orbitalResearchPath,id);return rank<0?orbitalResearchPath.Length:rank;}
        void ThinkPlanetAI(float dt){if(!fixture.StrategyEnabled)return;strategyTime+=dt;
            for(int team=0;team<Factions.Length;team++){if(!BaseAIEnabled(team)||strategyTime<nextThink[team])continue;nextThink[team]=strategyTime+4;int cycle=++AIDecisions[team];var f=Factions[team];var owned=Buildings.Where(b=>b.team==team&&!b.removed).ToArray();var command=owned.FirstOrDefault(b=>b.definition.id=="command");if(command==null){AIActivity[team]="Command lost";continue;}bool Has(string id)=>HasProductiveBuilding(id,team);
                foreach(var b in owned)if(b.Complete&&b.health<b.MaxHealth*.9f)b.repairing=AiCanSpend(team,new ResourceCost{materials=1});
                bool built=false;AIActivity[team]="Developing economy and production";
                if(Civilisation==null&&!Has("mine"))built=BuildAI("mine",team,cycle);
                bool economyReady=AiEconomyReady(team),scienceReady=Civilisation==null?economyReady:AiEconomyReady(team,false);
                if(Civilisation!=null&&!built&&scienceReady&&!Has("lab")&&f.wallet.CanAfford(Catalog.Buildings["lab"].cost))built=BuildAI("lab",team,cycle);
                if(!built&&Civilisation==null&&f.generation+owned.Where(b=>!b.Complete).Sum(b=>b.definition.generation)<f.demand+35)built=BuildAI("wind",team,cycle);
                if(!built&&economyReady)foreach(string id in new[]{"lab","autocannon","oil","rare","factory","at_turret","aa_turret","advanced_lab","strategic_lab"})if(!Has(id)&&AiCanSpend(team,Catalog.Buildings[id].cost)&&BuildAI(id,team,cycle)){built=true;AIActivity[team]="Constructing "+Catalog.Buildings[id].name;break;}
                if(!built&&economyReady&&cycle%3==0)built=BuildLocalDefences(team,cycle);
                if(scienceReady)foreach(var lab in owned.Where(b=>b.Complete&&b.powered&&b.queue.Count==0&&b.definition.category==BuildingCategory.Research)){
                    var tech=Catalog.technologies.OrderBy(t=>Civilisation==null?0:ResearchPriority(t.id)).ThenBy(t=>t.tier).FirstOrDefault(t=>t.requiredBuilding==lab.definition.id&&t.requiredBuildingLevel<=lab.level&&!f.HasTech(t.id)&&!f.researching.Contains(t.id)&&t.prerequisites.All(f.HasTech)&&AiCanSpend(team,t.cost));if(tech!=null)Research(lab,tech.id);
                    else if(lab.level<Math.Min(3,f.tier)&&!lab.Upgrading&&AiCanSpend(team,lab.definition.cost*(.65f*lab.level)))Upgrade(lab);
                }
                if(economyReady&&AiCanSpend(team,command.definition.cost*(.65f*command.level))&&!command.Upgrading&&command.level<5&&f.wallet.Stock.materials>command.definition.cost.materials*.65f*command.level+450)Upgrade(command);
                if(economyReady)foreach(var producer in owned.Where(b=>b.Complete&&b.powered&&b.queue.Count<2&&b.definition.production.Length>0)){
                    var choices=producer.definition.production.Where(id=>f.HasTech(Catalog.Units[id].requiredTech)&&Catalog.Units[id].producerLevel<=producer.level&&AiCanSpend(team,Catalog.Units[id].cost)).ToArray();if(choices.Length>0&&f.wallet.Stock.materials>250)QueueUnit(producer,choices[(cycle+team)%choices.Length]);
                }
                // Continue claiming reachable deposits; expand construction radius with normal outposts.
                if(Civilisation==null&&!built&&cycle%3==0){var node=Nodes.Where(n=>n.claimedBy<0&&n.remaining>0&&!owned.Any(b=>b.Complete&&b.definition.buildRadius>0&&Distance(CubeSphere.Decode(b.position),CubeSphere.Decode(n.position))<=b.definition.buildRadius)).OrderBy(n=>Distance(Homes[team],CubeSphere.Decode(n.position))).FirstOrDefault();if(node!=null){var target=CubeSphere.Decode(node.position);var closest=owned.Where(b=>b.Complete&&b.definition.buildRadius>0).OrderBy(b=>Distance(CubeSphere.Decode(b.position),target)).FirstOrDefault();if(closest!=null&&Distance(CubeSphere.Decode(closest.position),target)>closest.definition.buildRadius){var start=CubeSphere.Decode(closest.position);float distance=Distance(start,target);var p=Vector3.Slerp(start,target,Mathf.Min(.7f,(closest.definition.buildRadius-40)/Mathf.Max(1,distance)));ConstructForTeam("outpost",team,p);}}}
            }
        }
    }
}


