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
        internal float DistrictScore(BuildingDefinition d,Vector3 p,int rotation,int team,Vector3 centre){
            var entrance=Grid.Direction(Grid.Port(Grid.Area(d,p,rotation)));float road=Distance(entrance,EntranceAnchor(team,p));
            foreach(var b in Buildings)if(b.team==team&&!b.removed){float link=Distance(entrance,Grid.Direction(Grid.Port(Area(b))));road=Mathf.Min(road,link);}
            float grouping=Distance(p,centre),separation=0;
            foreach(var b in Buildings)if(b.team==team&&!b.removed&&b.definition.district!=d.district){bool compatible=(d.district==SettlementDistrict.Residential&&b.definition.district==SettlementDistrict.Commercial)||(d.district==SettlementDistrict.Commercial&&b.definition.district==SettlementDistrict.Residential)||b.definition.district==SettlementDistrict.Civic;float gap=Distance(p,CubeSphere.Decode(b.position));if(!compatible&&gap<40)separation+=40-gap;}
            return road*.8f+grouping*1.2f+separation*2;
        }
        internal bool CivilianExpansion(BuildingDefinition d)=>Civilisation!=null&&d.district!=SettlementDistrict.Military&&d.id!="command"&&d.id!="outpost";
        bool BuildDistrict(BuildingDefinition d,int team){
            var candidates=new List<(Vector3 point,int rotation,float score)>();
            if(d.housingCapacity>0||!string.IsNullOrEmpty(d.retailResource))AddStreetCandidates(d,team,candidates);
            if(d.id.StartsWith("civil_farm_"))foreach(var field in Buildings){if(field.team!=team||field.removed||field.definition.id!=d.id)continue;var a=Area(field);
                for(int sign=-1;sign<=1;sign+=2){int dx=(a.rotation&1)==0?sign*a.width:0,dy=(a.rotation&1)!=0?sign*a.depth:0;var p=Grid.Point(a.face,a.x+a.width*.5+dx,a.y+a.depth*.5+dy);
                    if(GridClear(Grid.Area(d,p,a.rotation),out _))candidates.Add((p,a.rotation,-100000+Distance(p,EntranceAnchor(team,p))));}
            }
            int anchorCount=0;foreach(var anchorBuilding in Buildings)if(anchorBuilding.team==team&&!anchorBuilding.removed&&anchorBuilding.Complete&&(CivilianExpansion(d)||anchorBuilding.definition.buildRadius>0))anchorCount++;
            int ordinal=0;int start=anchorCount==0?0:(int)(Civilisation.SimulationTime/Math.Max(1,Civilisation.Settings.strategyTickSeconds))%anchorCount;
            for(int pass=0;pass<2&&candidates.Count<196;pass++)foreach(var anchorBuilding in Buildings){if(anchorBuilding.team!=team||anchorBuilding.removed||!anchorBuilding.Complete||!CivilianExpansion(d)&&anchorBuilding.definition.buildRadius<=0)continue;
                int index=ordinal++%Math.Max(1,anchorCount);if(pass==0?index<start:index>=start)continue;
                var origin=Area(anchorBuilding);var offset=SettlementDistricts.Offset(d.district);var centre=Grid.Point(origin.face,origin.x+origin.width*.5+offset.x,origin.y+origin.depth*.5+offset.y);
                int stride=Math.Max(d.footprintWidth,d.footprintDepth)+2;
                for(int y=-3;y<=3;y++)for(int x=-3;x<=3;x++){
                    var p=Grid.Point(origin.face,origin.x+origin.width*.5+offset.x+x*stride,origin.y+origin.depth*.5+offset.y+y*stride);int rotation=EntranceRotation(d,p,team);
                    if(!GridClear(Grid.Area(d,p,rotation),out _))continue;
                    candidates.Add((p,rotation,DistrictScore(d,p,rotation,team,centre)));
                }
                if(candidates.Count>=196)break;
            }
            candidates.Sort((a,b)=>a.score.CompareTo(b.score));int budget=Math.Min(24,candidates.Count);
            for(int i=0;i<budget;i++){var c=candidates[i];if(ConstructOriented(d,team,c.point,c.rotation))return true;}
            return false;
        }
        internal bool BuildSettlementFrontier(string requested,int team){
            var wanted=Catalog.Buildings[requested];if(!CivilianExpansion(wanted)||wanted.terrain==TerrainRule.Node)return false;
            // Roads supply civilian construction; forward bases do not unlock economic land.
            int count=0;foreach(var building in Buildings)if(building.team==team&&!building.removed&&building.Complete)count++;
            if(count==0)return false;int selected=(int)(Civilisation.SimulationTime/Math.Max(1,Civilisation.Settings.strategyTickSeconds))%count,index=0;
            foreach(var anchorBuilding in Buildings){if(anchorBuilding.team!=team||anchorBuilding.removed||!anchorBuilding.Complete)continue;if(index++!=selected)continue;
                var origin=CubeSphere.Decode(anchorBuilding.position);var east=Vector3.Cross(Mathf.Abs(origin.y)>.95f?Vector3.right:Vector3.up,origin).normalized;float range=48+Math.Max(wanted.footprintWidth,wanted.footprintDepth)*8;
                double phase=Math.Floor(Civilisation.SimulationTime/Math.Max(1,Civilisation.Settings.strategyTickSeconds)/count);
                for(int attempt=0;attempt<16;attempt++){float angle=(float)(phase*2.3999632+attempt*Math.PI/8);var target=(origin+(east*Mathf.Cos(angle)+Vector3.Cross(origin,east)*Mathf.Sin(angle))*range/fixture.definition.radius).normalized;
                    if(ConstructForTeam(requested,team,target))return true;
                }return false;
            }return false;
        }
        readonly List<(Vector3 point,int rotation,float score)> previewStreetCandidates=new List<(Vector3,int,float)>();
        void AlignResidentialPreview(BuildingDefinition d,ref Vector3 point,ref int rotation){
            rotation=EntranceRotation(d,point,0);if(d.housingCapacity<=0&&string.IsNullOrEmpty(d.retailResource))return;
            previewStreetCandidates.Clear();AddStreetCandidates(d,0,previewStreetCandidates);float closest=12;
            Vector3 selected=point;foreach(var candidate in previewStreetCandidates){float distance=Distance(point,candidate.point);if(distance<closest){closest=distance;selected=candidate.point;rotation=candidate.rotation;}}point=selected;
        }
        void AddStreetCandidates(BuildingDefinition d,int team,List<(Vector3 point,int rotation,float score)> candidates){
            // Derive frontage from saved building footprints, so street growth survives revisiting a planet.
            foreach(var home in Buildings){if(home.team!=team||home.removed||home.definition.housingCapacity<=0)continue;
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
                    if(!GridClear(area,out _,d))return;
                    float score=-50000+Distance(p,EntranceAnchor(team,p));
                    // Keep corner shops close to residents, but distribute services along the street.
                    if(!string.IsNullOrEmpty(d.retailResource))foreach(var shop in Buildings)if(shop.team==team&&!shop.removed&&!string.IsNullOrEmpty(shop.definition.retailResource))score+=Mathf.Max(0,40-Distance(p,CubeSphere.Decode(shop.position)))*2;
                    candidates.Add((p,rotation,score));
                }
            }
        }
        bool ConstructOriented(BuildingDefinition d,int team,Vector3 direction,int rotation){direction=SnapBuilding(d,direction,rotation);var valid=Validate(d,direction,rotation,true,team);if(!valid.valid||!fixture.IsFootprintClear(direction,FootprintRadius(d))||!ConstructionCost(team,direction,d.cost,true))return false;
            var b=Create(d,direction,rotation,false,team);b.node=valid.node;Civilisation?.SponsorProject(this,b);if(b.node>=0)Nodes[b.node].claimedBy=b.index;fixture.RefreshBuildingCosts(Buildings);Rebase();return true;}
    }
}
