using System;
using System.Collections.Generic;
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
        readonly Dictionary<(int,string),PlanningFrontier> cropFrontiers=new Dictionary<(int,string),PlanningFrontier>();
        readonly PlanningFrontier[] allFrontiers={new PlanningFrontier(),new PlanningFrontier()};
        readonly Dictionary<(int,string),int> failedPlanningOffsets=new Dictionary<(int,string),int>();
        readonly Dictionary<(int,string),int> planningCursors=new Dictionary<(int,string),int>();
        readonly List<RtsBuilding> planningAnchors=new List<RtsBuilding>(12);
        readonly List<(Vector3 point,int rotation,float score)> districtCandidates=new List<(Vector3,int,float)>();
        internal int LastPlanningCandidates {get;private set;}
        void RegisterPlanningSite(RtsBuilding b){var key=(b.team,b.definition.district);if(!planningFrontiers.TryGetValue(key,out var ring)){ring=new PlanningFrontier();planningFrontiers.Add(key,ring);}var area=Area(b);ring.Add(b,area);allFrontiers[b.team].Add(b,area);if(b.definition.id.StartsWith("civil_farm_")){var cropKey=(b.team,b.definition.id);if(!cropFrontiers.TryGetValue(cropKey,out var crop)){crop=new PlanningFrontier();cropFrontiers.Add(cropKey,crop);}crop.Add(b,area);}}
        int PlanningCursor(BuildingDefinition d,int team){var key=(team,d.id);planningCursors.TryGetValue(key,out int cursor);planningCursors[key]=(cursor+1)&int.MaxValue;return cursor;}
        void PlanningAnchors(BuildingDefinition d,int team,int cursor){
            planningAnchors.Clear();var district=!string.IsNullOrEmpty(d.retailResource)?SettlementDistrict.Residential:d.district;
            PlanningFrontier preferred=null;if(d.id.StartsWith("civil_farm_"))cropFrontiers.TryGetValue((team,d.id),out preferred);if(preferred==null)planningFrontiers.TryGetValue((team,district),out preferred);if(preferred!=null)Add(preferred,8);
            Add(allFrontiers[team],4);
            // Headquarters remain military anchors after economic frontier rings roll over.
            if(planningAnchors.Count==0){var command=Buildings.Find(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.buildRadius>0);if(command!=null)planningAnchors.Add(command);}
            void Add(PlanningFrontier ring,int budget){for(int i=0;i<Math.Min(budget,ring.count);i++){var b=i<budget/2?ring.GetEdge((cursor%96)*(budget/2)+i):ring.Get((cursor%ring.count)*(budget/2)+i-budget/2);if(b==null||b.removed||!b.Complete&&!(d.id.StartsWith("civil_farm_")&&b.definition.id==d.id)||!CivilianExpansion(d)&&b.definition.buildRadius<=0||planningAnchors.Contains(b))continue;planningAnchors.Add(b);}}
        }
        internal bool CivilianExpansion(BuildingDefinition d)=>Civilisation!=null&&d.district!=SettlementDistrict.Military&&d.id!="command"&&d.id!="outpost";
        bool BuildDistrict(BuildingDefinition d,int team){
            using var timing=SettlementPerformance.Sample("DistrictPlacement");int cursor=PlanningCursor(d,team);PlanningAnchors(d,team,cursor);var occupancy=CaptureConstructionOccupancy();bool seedField=d.id.StartsWith("civil_farm_")&&!planningAnchors.Exists(b=>b.definition.id==d.id);var candidates=districtCandidates;candidates.Clear();LastPlanningCandidates=0;
            if(d.housingCapacity>0||!string.IsNullOrEmpty(d.retailResource))AddStreetCandidates(d,team,candidates,occupancy);
            foreach(var anchorBuilding in planningAnchors){var a=Area(anchorBuilding);
                // Farms grow as contiguous blocks; housing grows by shared frontage.
                if(d.id.StartsWith("civil_farm_")&&anchorBuilding.definition.id==d.id)for(int side=-1;side<=1;side+=2){int dx=(a.rotation&1)==0?side*a.width:0,dy=(a.rotation&1)!=0?side*a.depth:0;Add(a.x+a.width*.5+dx,a.y+a.depth*.5+dy,a.rotation,-100000);}
                var offset=anchorBuilding.definition.district==d.district?Vector2.zero:SettlementDistricts.Offset(d.district);int stride=Math.Max(d.footprintWidth,d.footprintDepth)+2;
                for(int edge=0;edge<16;edge++){int side=edge/4,along=edge%4-1;int x=side==0?-2:side==1?2:along,y=side==2?-2:side==3?2:along;
                    double px=a.x+a.width*.5+offset.x+x*stride,py=a.y+a.depth*.5+offset.y+y*stride;var p=Grid.Point(a.face,px,py);
                    int turn=0;float best=float.MaxValue;for(int rotation=0;rotation<4;rotation++){float score=(Grid.Direction(Grid.Port(Grid.Area(d,p,rotation)))-Homes[team]).sqrMagnitude;if(score<best){best=score;turn=rotation;}}
                    Add(px,py,turn,Mathf.Abs(x)+Mathf.Abs(y));
                }
                void Add(double x,double y,int rotation,float priority){LastPlanningCandidates++;var p=Grid.Point(a.face,x,y);if(!GridClear(Grid.Area(d,p,rotation),out _,d,occupancy))return;
                    if(seedField){var field=Grid.Area(d,p,rotation);bool space=false;for(int sign=-1;sign<=1;sign+=2){int dx=(rotation&1)==0?sign*field.width:0,dy=(rotation&1)!=0?sign*field.depth:0;var next=Grid.Point(field.face,field.x+field.width*.5+dx,field.y+field.depth*.5+dy);if(GridClear(Grid.Area(d,next,rotation),out _,d,occupancy))space=true;}if(!space)return;}
                    // Local road length and district compatibility are constant-work rules.
                    var port=Grid.Port(Grid.Area(d,p,rotation));var anchorPort=Grid.Port(a);float road=port.face==anchorPort.face?Mathf.Abs(port.x-anchorPort.x)+Mathf.Abs(port.y-anchorPort.y):100;
                    candidates.Add((p,rotation,priority+road*.8f));}
            }
            candidates.Sort((a,b)=>a.score.CompareTo(b.score));var key=(team,d.id);failedPlanningOffsets.TryGetValue(key,out int attemptOffset);for(int i=0;i<Math.Min(3,candidates.Count);i++){var c=candidates[(attemptOffset+i)%candidates.Count];if(ConstructOriented(d,team,c.point,c.rotation)){failedPlanningOffsets[key]=0;return true;}}failedPlanningOffsets[key]=candidates.Count==0?0:(attemptOffset+3)%candidates.Count;return false;
        }
        internal bool BuildSettlementFrontier(string requested,int team){var d=Catalog.Buildings[requested];return CivilianExpansion(d)&&d.terrain!=TerrainRule.Node&&BuildDistrict(d,team);}
        readonly List<(Vector3 point,int rotation,float score)> previewStreetCandidates=new List<(Vector3,int,float)>();
        void AlignResidentialPreview(BuildingDefinition d,ref Vector3 point,ref int rotation){
            rotation=EntranceRotation(d,point,0);if(d.housingCapacity<=0&&string.IsNullOrEmpty(d.retailResource))return;
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
                    if(!GridClear(area,out _,d,occupancy))return;
                    float score=-50000;
                    candidates.Add((p,rotation,score));
                }
            }
        }
        bool ConstructOriented(BuildingDefinition d,int team,Vector3 direction,int rotation){direction=SnapBuilding(d,direction,rotation);var valid=Validate(d,direction,rotation,true,team);if(!valid.valid||!fixture.IsFootprintClear(direction,FootprintRadius(d))||!ConstructionCost(team,direction,d.cost,true))return false;
            var b=Create(d,direction,rotation,false,team);b.node=ResolveResourceNode(valid.node);Civilisation?.SponsorProject(this,b);if(b.node>=0)Nodes[b.node].claimedBy=b.index;fixture.RefreshBuildingCosts(Buildings);Rebase();return true;}
    }
}
