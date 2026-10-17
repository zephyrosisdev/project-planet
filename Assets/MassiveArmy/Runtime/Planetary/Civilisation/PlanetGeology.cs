using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsBase {
        void InitializeGeology(){
            var civilisation=Civilisation;if(civilisation==null)return;var profile=civilisation.Planet(fixture.definition.bodyId).composition;
            // Preserve the original starter sites and their bootstrap guarantee; abundance changes their efficiency.
            foreach(var node in Nodes){string resource=node.resource==ResourceKind.Materials?"iron":node.resource==ResourceKind.Fuel?"hydrogen":node.resource==ResourceKind.RareElements?"rare_earth":"uranium";float richness=(float)profile.LocalAbundance(resource,CubeSphere.Decode(node.position),fixture.definition.terrainSeed);node.quality=richness;node.rateMultiplier=Mathf.Max(civilisation.Settings.geologicalMinimumEfficiency,richness*2);}
            var elements=System.Array.FindAll(civilisation.Catalogue.resources,element=>element.element&&profile.Get(element.id)>0);
            if(elements.Length==0)return;
            for(int team=0;team<2;team++){
                if(team==1&&fixture.sessionMode==PlanetGameMode.Swarm)continue;
                for(int at=0;at<civilisation.Settings.geologicalDepositsPerFaction;at++){
                    var element=elements[at%elements.Length];int reserve=at/elements.Length;
                    var extractor=Array.Find(Catalog.buildings,d=>d.terrain==TerrainRule.Node&&d.extractedResource==element.id);
                    // First complete material set is inside the starting construction area; reserves extend outwards.
                    for(int attempt=0;attempt<96;attempt++){
                        float angle=at*2.3999632f+attempt*.61f,range=reserve==0?140+(attempt%20)*3:360+reserve*180+attempt*3;
                        var direction=TeamOffset(team,new Vector2(Mathf.Cos(angle)*range,Mathf.Sin(angle)*range)+(reserve==0?new Vector2(-65,-55):Vector2.zero));
                        if(surface.SampleHeight(direction)<fixture.definition.oceanHeight)continue;bool clear=true;
                        foreach(var existing in Nodes)if(Distance(direction,CubeSphere.Decode(existing.position))<40){clear=false;break;}if(!clear)continue;
                        double richness=profile.LocalAbundance(element.id,direction,fixture.definition.terrainSeed);
                        float capacity=(float)(civilisation.Settings.depositCapacity*Math.Max(.1,richness));
                        Nodes.Add(new ResourceNode{id=Nodes.Count,kind=element.id=="crude_oil"?NodeKind.Oil:NodeKind.Metal,resource=ResourceKind.Materials,elementId=element.id,position=CubeSphere.Encode(direction),capacity=capacity,remaining=capacity,quality=(float)richness,rateMultiplier=(float)Math.Max(civilisation.Settings.geologicalMinimumEfficiency,richness*2)});
                        // A dry centre alone does not guarantee a usable extractor footprint or entrance.
                        bool accessible=reserve!=0||Buildings.Exists(b=>b.team==team&&b.Complete&&b.definition.buildRadius>0&&Distance(CubeSphere.Decode(b.position),direction)<b.definition.buildRadius-8);
                        if(extractor!=null&&(!accessible||!Validate(extractor,direction,0,false,team).valid)){Nodes.RemoveAt(Nodes.Count-1);continue;}
                        break;
                    }
                }
            }
        }
        public bool TryConstructInfrastructure(string id,int team){
            if(!Catalog.Buildings.ContainsKey(id)||team<0||team>=Factions.Length)return false;return BuildAI(id,team,AIDecisions[team]);
        }
    }
}

