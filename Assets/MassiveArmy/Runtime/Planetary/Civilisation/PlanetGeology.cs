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
                        Nodes.Add(new ResourceNode{id=Nodes.Count,kind=element.id=="crude_oil"?NodeKind.Oil:element.id=="methane"?NodeKind.Gas:NodeKind.Metal,resource=ResourceKind.Materials,elementId=element.id,position=CubeSphere.Encode(direction),capacity=capacity,remaining=capacity,quality=(float)richness,rateMultiplier=(float)Math.Max(civilisation.Settings.geologicalMinimumEfficiency,richness*2)});
                        // A dry centre alone does not guarantee a usable extractor footprint or entrance.
                        bool accessible=reserve!=0||Buildings.Exists(b=>b.team==team&&b.Complete&&b.definition.buildRadius>0&&Distance(CubeSphere.Decode(b.position),direction)<b.definition.buildRadius-8);
                        if(extractor!=null&&(!accessible||!Validate(extractor,direction,0,false,team).valid)){Nodes.RemoveAt(Nodes.Count-1);continue;}
                        break;
                    }
                }
            }
            foreach(var discovered in civilisation.Planet(fixture.definition.bodyId).frontierDeposits){discovered.id=Nodes.Count;Nodes.Add(discovered);}
        }
        internal bool SurveyFrontierDeposit(string id,int team){
            var d=Catalog.Buildings[id];if(d.terrain!=TerrainRule.Node||d.nodes.Length==0)return false;
            // A usable, unclaimed local prospect must be developed before surveying another.
            foreach(var node in Nodes)if(node.claimedBy<0&&node.remaining>0&&Array.IndexOf(d.nodes,node.kind)>=0&&string.Equals(node.elementId??"",d.extractedResource??"",StringComparison.Ordinal)){
                var direction=CubeSphere.Decode(node.position);if(Distance(direction,Homes[team])<=Distance(direction,Homes[1-team])&&!ResourceSiteCooling(id,team,node.id)&&Validate(d,direction,0,false,team).valid)return false;
            }
            var planet=Civilisation.Planet(fixture.definition.bodyId);var survey=planet.frontierSurveys.Find(s=>s.team==team&&s.resource==id);
            if(survey==null){survey=new FrontierSurvey{team=team,resource=id};planet.frontierSurveys.Add(survey);}
            string element=d.extractedResource??"";string abundance=element.Length>0?element:d.nodes[0]==NodeKind.Oil?"hydrogen":d.nodes[0]==NodeKind.RareEarth?"rare_earth":d.nodes[0]==NodeKind.Uranium?"uranium":"iron";
            if(planet.composition.Get(abundance)<=0)return false;
            var home=Homes[team];var east=Vector3.Cross(Mathf.Abs(home.y)>.95f?Vector3.right:Vector3.up,home).normalized;var north=Vector3.Cross(home,east);
            for(int work=0;work<8;work++){
                int index=survey.cursor++;double phase=PlanetResourceProfile.Random(fixture.definition.terrainSeed+":"+team+":"+id)*Math.PI*2;double bearing=index*2.399963229728653+phase,arc=(1800+Math.Sqrt(index)*140)/fixture.definition.radius;
                var direction=(home*(float)Math.Cos(arc)+(east*(float)Math.Cos(bearing)+north*(float)Math.Sin(bearing))*(float)Math.Sin(arc)).normalized;
                if(surface.SampleHeight(direction)<fixture.definition.oceanHeight+.2f||Distance(direction,Homes[team])>Distance(direction,Homes[1-team]))continue;
                bool clear=true;foreach(var existing in Nodes)if(Distance(direction,CubeSphere.Decode(existing.position))<60){clear=false;break;}if(!clear)continue;
                float richness=(float)planet.composition.LocalAbundance(abundance,direction,fixture.definition.terrainSeed);float capacity=Civilisation.Settings.depositCapacity*Mathf.Max(.1f,richness);
                var node=new ResourceNode{id=Nodes.Count,kind=d.nodes[0],resource=d.nodes[0]==NodeKind.Oil?ResourceKind.Fuel:d.nodes[0]==NodeKind.RareEarth?ResourceKind.RareElements:d.nodes[0]==NodeKind.Uranium?ResourceKind.Isotopes:ResourceKind.Materials,elementId=element,position=CubeSphere.Encode(direction),capacity=capacity,remaining=capacity,quality=richness,rateMultiplier=Mathf.Max(Civilisation.Settings.geologicalMinimumEfficiency,richness*2)};
                Nodes.Add(node);if(!Validate(d,direction,0,false,team).valid){Nodes.RemoveAt(Nodes.Count-1);continue;}
                planet.frontierDeposits.Add(node);NotifyChanged();return true;
            }return false;
        }
        public bool TryConstructInfrastructure(string id,int team){
            if(!Catalog.Buildings.ContainsKey(id)||team<0||team>=Factions.Length)return false;return BuildAI(id,team,AIDecisions[team]);
        }
    }
}

