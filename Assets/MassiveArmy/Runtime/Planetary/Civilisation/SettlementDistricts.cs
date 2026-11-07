using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public static class SettlementDistricts {
        public static Color Colour(SettlementDistrict d){switch(d){case SettlementDistrict.Residential:return new Color(.15f,.85f,.35f,.34f);case SettlementDistrict.Commercial:return new Color(.2f,.65f,1,.34f);case SettlementDistrict.Industrial:return new Color(1,.5f,.1f,.34f);case SettlementDistrict.Agricultural:return new Color(.85f,.85f,.15f,.34f);case SettlementDistrict.Utilities:return new Color(.7f,.3f,1,.34f);case SettlementDistrict.Extraction:return new Color(.9f,.25f,.2f,.34f);case SettlementDistrict.Military:return new Color(.5f,.6f,.65f,.34f);default:return new Color(.1f,1,.9f,.34f);}}
        public static Vector2 Offset(SettlementDistrict d){switch(d){case SettlementDistrict.Residential:return new Vector2(9,13);case SettlementDistrict.Commercial:return new Vector2(4,7);case SettlementDistrict.Industrial:return new Vector2(-10,-8);case SettlementDistrict.Agricultural:return new Vector2(-10,11);case SettlementDistrict.Utilities:return new Vector2(-15,1);case SettlementDistrict.Military:return new Vector2(10,-8);default:return Vector2.zero;}}
    }
    public sealed partial class PlanetRtsBase {
        public bool DistrictOverlay {get;private set;}
        public void ToggleDistrictOverlay(){DistrictOverlay=!DistrictOverlay;}
        internal Vector3 EntranceAnchor(int team,Vector3 point){Vector3 result=Homes[team];float best=float.MaxValue;
            foreach(var b in Buildings)if(b.team==team&&!b.removed&&b.Complete&&(b.definition.id=="command"||b.definition.id=="outpost")){var p=CubeSphere.Decode(b.position);float distance=(p-point).sqrMagnitude;if(distance<best){best=distance;result=p;}}return result;}
        internal int EntranceRotation(BuildingDefinition d,Vector3 direction,int team){var target=EntranceAnchor(team,direction);int best=0;float distance=float.MaxValue;
            for(int rotation=0;rotation<4;rotation++){float value=(Grid.Direction(Grid.Port(Grid.Area(d,direction,rotation)))-target).sqrMagnitude;if(value<distance){distance=value;best=rotation;}}return best;}
        // Fixed-size frontier rings are fed when infrastructure is created/restored.
        // Planning reads a bounded number of recent edge anchors, not the city history.
        sealed class PlanningFrontier {
            internal readonly RtsBuilding[] sites=new RtsBuilding[128];internal int count,next;
            sealed class Edge {internal readonly RtsBuilding[] sites=new RtsBuilding[8];internal readonly float[] score=new float[8];}
            readonly Edge[] edges=new Edge[6];
            internal void Add(RtsBuilding b,PlanetConstructionGrid.Footprint area){sites[next]=b;next=(next+1)%sites.Length;count=Math.Min(count+1,sites.Length);var edge=edges[(int)area.face]??(edges[(int)area.face]=new Edge());float x=area.x+area.width*.5f,y=area.y+area.depth*.5f;
                for(int i=0;i<8;i++){float score=i==0?x:i==1?-x:i==2?y:i==3?-y:i==4?x+y:i==5?x-y:i==6?-x+y:-x-y;if(edge.sites[i]==null||edge.sites[i].removed||score>edge.score[i]){edge.sites[i]=b;edge.score[i]=score;}}}
            // Persistent support points survive the recent-site ring wrapping.
            internal RtsBuilding GetEdge(int offset){for(int i=0;i<6;i++){var edge=edges[((offset/8)%6+i)%6];if(edge!=null)return edge.sites[offset&7];}return null;}
            internal RtsBuilding Get(int offset)=>count==0?null:sites[(next-1-offset%count+sites.Length)%sites.Length];
        }
        readonly Dictionary<(int,SettlementDistrict),PlanningFrontier> planningFrontiers=new Dictionary<(int,SettlementDistrict),PlanningFrontier>();
        readonly Dictionary<(int,string),PlanningFrontier> typeFrontiers=new Dictionary<(int,string),PlanningFrontier>();
        readonly PlanningFrontier[] allFrontiers=System.Linq.Enumerable.Range(0,PlanetCountries.Maximum).Select(_=>new PlanningFrontier()).ToArray();
        readonly Dictionary<(int,string),int> planningCursors=new Dictionary<(int,string),int>();
        readonly List<RtsBuilding> planningAnchors=new List<RtsBuilding>(12);
        internal int LastPlanningCandidates {get;private set;}
        void RegisterPlanningSite(RtsBuilding b){var key=(b.team,b.definition.district);if(!planningFrontiers.TryGetValue(key,out var ring)){ring=new PlanningFrontier();planningFrontiers.Add(key,ring);}var area=Area(b);ring.Add(b,area);allFrontiers[b.team].Add(b,area);{var cropKey=(b.team,b.definition.id);if(!typeFrontiers.TryGetValue(cropKey,out var crop)){crop=new PlanningFrontier();typeFrontiers.Add(cropKey,crop);}crop.Add(b,area);}}
        int PlanningCursor(BuildingDefinition d,int team){var key=(team,d.id);planningCursors.TryGetValue(key,out int cursor);planningCursors[key]=(cursor+1)&int.MaxValue;return cursor;}
        void PlanningAnchors(BuildingDefinition d,int team,int cursor){
            planningAnchors.Clear();var district=d.district;
            if(PlanningSettlement!=null){if(RegionalAnchors!=null)foreach(var b in RegionalAnchors)if(InPlanningSettlement(b)&&b.Complete&&(b.definition.district==d.district||b.definition.id=="command")&&!planningAnchors.Contains(b))planningAnchors.Add(b);var hub=FindBuilding(PlanningSettlement.hub);if(hub!=null&&!planningAnchors.Contains(hub))planningAnchors.Add(hub);return;}
            PlanningFrontier preferred=null;if(d.id.StartsWith("civil_farm_"))typeFrontiers.TryGetValue((team,d.id),out preferred);if(preferred==null)planningFrontiers.TryGetValue((team,district),out preferred);if(preferred!=null)Add(preferred,6);
            if(d.district==SettlementDistrict.Agricultural&&planningFrontiers.TryGetValue((team,SettlementDistrict.Agricultural),out var fields)&&fields!=preferred)Add(fields,4);
            Add(allFrontiers[team],d.district==SettlementDistrict.Agricultural?2:4);
            // Headquarters remain military anchors after economic frontier rings roll over.
            if(planningAnchors.Count==0){var command=Buildings.Find(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.buildRadius>0);if(command!=null)planningAnchors.Add(command);}
            void Add(PlanningFrontier ring,int budget){for(int i=0;i<Math.Min(budget,ring.count);i++){var b=i<budget/2?ring.GetEdge((cursor%96)*(budget/2)+i):ring.Get((cursor%ring.count)*(budget/2)+i-budget/2);if(b==null||b.removed||!b.Complete&&!(d.id.StartsWith("civil_farm_")&&b.definition.id==d.id)||!CivilianExpansion(d)&&b.definition.buildRadius<=0||planningAnchors.Contains(b))continue;planningAnchors.Add(b);}}
        }
        internal bool CivilianExpansion(BuildingDefinition d)=>Civilisation!=null&&d.district!=SettlementDistrict.Military&&d.id!="command"&&d.id!="outpost";
        // Random local growth: no candidate ranking, city-wide search, or retry storm.
        // Persistent type/district frontier rings have fixed capacity regardless of city size.
        readonly System.Random districtRandom=new System.Random(72913);
        bool BuildDistrict(BuildingDefinition d,int team){
            using var timing=SettlementPerformance.Sample("DistrictPlacement");
            int cursor=PlanningCursor(d,team);PlanningAnchors(d,team,cursor);
            typeFrontiers.TryGetValue((team,d.id),out var sameType);
            var occupancy=CaptureConstructionOccupancy();LastPlanningCandidates=0;
            for(int attempt=0;attempt<8;attempt++){
                RtsBuilding anchor=null;
                // Most attempts grow a block of the same building; the remainder use its district.
                if(sameType!=null&&sameType.count>0&&districtRandom.Next(4)!=0)anchor=sameType.GetEdge(districtRandom.Next(48))??sameType.Get(districtRandom.Next(sameType.count));
                if(!InPlanningSettlement(anchor))anchor=planningAnchors.Count==0?null:planningAnchors[districtRandom.Next(planningAnchors.Count)];
                if(!InPlanningSettlement(anchor))continue;
                var area=Area(anchor);int rotation=anchor.definition.district==d.district?area.rotation:districtRandom.Next(4);
                int w=(rotation&1)==0?d.footprintWidth:d.footprintDepth,h=(rotation&1)==0?d.footprintDepth:d.footprintWidth;
                int side=districtRandom.Next(4),gap=1+attempt/4; // leave space for roads between blocks
                double x=area.x+area.width*.5,y=area.y+area.depth*.5;
                if(side<2)x+=(side==0?-1:1)*(area.width*.5+w*.5+gap);
                else y+=(side==2?-1:1)*(area.depth*.5+h*.5+gap);
                if(anchor.definition.district!=d.district){var offset=SettlementDistricts.Offset(d.district);x+=offset.x*(1+attempt/2);y+=offset.y*(1+attempt/2);}
                if(x<w*.5||y<h*.5||x+w*.5>Grid.Resolution||y+h*.5>Grid.Resolution)continue;
                var point=Grid.Point(area.face,x,y);LastPlanningCandidates++;if(!InPlanningRange(point))continue;
                if(!DistrictClear(d,point,team)||!GridClear(Grid.Area(d,point,rotation),out _,d,occupancy))continue;
                // At most one expensive final validation/GPU clearance per decision.
                return ConstructOriented(d,team,point,rotation);
            }
            return false;
        }
        internal bool BuildSettlementFrontier(string requested,int team){var d=Catalog.Buildings[requested];return CivilianExpansion(d)&&d.terrain!=TerrainRule.Node&&BuildDistrict(d,team);}
        readonly List<(Vector3 point,int rotation,float score)> previewStreetCandidates=new List<(Vector3,int,float)>();
        void AlignResidentialPreview(BuildingDefinition d,ref Vector3 point,ref int rotation){
            rotation=EntranceRotation(d,point,0);if(d.housingCapacity<=0)return;
            previewStreetCandidates.Clear();AddStreetCandidates(d,0,previewStreetCandidates,CaptureConstructionOccupancy());float closest=12;
            Vector3 selected=point;foreach(var candidate in previewStreetCandidates){float distance=Distance(point,candidate.point);if(distance<closest){closest=distance;selected=candidate.point;rotation=candidate.rotation;}}point=selected;
        }
        void AddStreetCandidates(BuildingDefinition d,int team,List<(Vector3 point,int rotation,float score)> candidates,ConstructionOccupancy occupancy){
            // Derive frontage from saved building footprints, so street growth survives revisiting a planet.
            if(!planningFrontiers.TryGetValue((team,SettlementDistrict.Residential),out var ring))return;
            int cursor=PlanningCursor(d,team);for(int i=0;i<Math.Min(8,ring.count);i++){var home=i<4?ring.GetEdge((cursor%96)*4+i):ring.Get((cursor%ring.count)*4+i-4);if(home==null||home.removed||home.definition.housingCapacity<=0)continue;
                var a=Area(home);bool horizontal=(a.rotation&1)==0;
                for(int side=-1;side<=1;side+=2){
                    int width=horizontal?d.footprintWidth:d.footprintDepth,depth=horizontal?d.footprintDepth:d.footprintWidth;
                    int x=a.x,y=a.y;
                    if(horizontal){x+=side<0?-width:a.width;if(a.rotation==0)y+=a.depth-depth;}
                    else{y+=side<0?-depth:a.depth;if(a.rotation==3)x+=a.width-width;}
                    Add(x,y,width,depth,a.rotation);
                }
                // The facing row leaves exactly one road module between the front doors.
                int opposite=(a.rotation+2)&3,ow=(opposite&1)==0?d.footprintWidth:d.footprintDepth,od=(opposite&1)==0?d.footprintDepth:d.footprintWidth;
                int ox=a.x,oy=a.y;switch(a.rotation){case 0:oy+=a.depth+1;break;case 1:ox-=ow+1;break;case 2:oy-=od+1;break;default:ox+=a.width+1;break;}Add(ox,oy,ow,od,opposite);
                void Add(int x,int y,int width,int depth,int rotation){
                    if(x<0||y<0||x+width>Grid.Resolution||y+depth>Grid.Resolution)return;
                    var p=Grid.Point(a.face,x+width*.5,y+depth*.5);var area=Grid.Area(d,p,rotation);
                    if(!DistrictClear(d,p,team)||!GridClear(area,out _,d,occupancy))return;
                    float score=-50000;
                    candidates.Add((p,rotation,score));
                }
            }
        }
        bool ConstructOriented(BuildingDefinition d,int team,Vector3 direction,int rotation){direction=SnapBuilding(d,direction,rotation);var valid=Validate(d,direction,rotation,true,team);if(!valid.valid||fixture.ResidentActive&&!fixture.IsFootprintClear(direction,FootprintRadius(d))||!ConstructionCost(team,direction,d.cost,true))return false;
            var b=Create(d,direction,rotation,false,team);b.node=ResolveResourceNode(valid.node);Civilisation?.SponsorProject(this,b);if(b.node>=0)Nodes[b.node].claimedBy=b.index;RefreshConstructionCosts();Rebase();return true;}
    }
}
