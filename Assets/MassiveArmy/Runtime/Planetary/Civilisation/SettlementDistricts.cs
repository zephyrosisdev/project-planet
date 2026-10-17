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
        bool BuildDistrict(BuildingDefinition d,int team){
            var candidates=new List<(Vector3 point,int rotation,float score)>();
            foreach(var anchorBuilding in Buildings){if(anchorBuilding.team!=team||anchorBuilding.removed||!anchorBuilding.Complete||anchorBuilding.definition.buildRadius<=0)continue;
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
        bool ConstructOriented(BuildingDefinition d,int team,Vector3 direction,int rotation){direction=SnapBuilding(d,direction,rotation);var valid=Validate(d,direction,rotation,true,team);if(!valid.valid||!fixture.IsFootprintClear(direction,FootprintRadius(d))||!Factions[team].wallet.Spend(d.cost))return false;
            var b=Create(d,direction,rotation,false,team);b.node=valid.node;if(b.node>=0)Nodes[b.node].claimedBy=b.index;fixture.RefreshBuildingCosts(Buildings);Rebase();return true;}
    }
}
