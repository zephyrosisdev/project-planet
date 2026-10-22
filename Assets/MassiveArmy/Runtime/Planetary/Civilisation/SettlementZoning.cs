using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Infrastructure only. Body-space buckets work across cube-face seams and
    // floating-origin changes; no army records or per-frame queries are involved.
    public sealed class SettlementZoning {
        readonly struct Plot {
            internal readonly Vector3 centre;
            internal readonly float radius;
            internal readonly SettlementDistrict district;
            internal readonly RtsBuilding building;
            internal Plot(Vector3 centre,float radius,SettlementDistrict district,RtsBuilding building){this.centre=centre;this.radius=radius;this.district=district;this.building=building;}
        }
        readonly Dictionary<Vector3Int,List<Plot>> buckets=new Dictionary<Vector3Int,List<Plot>>();
        readonly float bucketSize;
        public readonly float IndustrialBuffer;
        public SettlementZoning(float tileSize){IndustrialBuffer=tileSize*6;bucketSize=Mathf.Max(32,IndustrialBuffer);}
        public static bool Incompatible(SettlementDistrict a,SettlementDistrict b)=>a==SettlementDistrict.Industrial&&Sensitive(b)||b==SettlementDistrict.Industrial&&Sensitive(a);
        public static bool Peripheral(SettlementDistrict district)=>district==SettlementDistrict.Industrial||district==SettlementDistrict.Agricultural;
        public static bool OutsideCentre(Vector3 direction,Vector3 home,float planetRadius,float plotRadius,float tileSize,SettlementDistrict district)=>!Peripheral(district)||CubeSphere.ArcDistance(direction,home,planetRadius)>=tileSize*12+plotRadius;
        static bool Sensitive(SettlementDistrict d)=>d==SettlementDistrict.Residential||d==SettlementDistrict.Commercial||d==SettlementDistrict.Agricultural;
        Vector3Int Bucket(Vector3 point)=>new Vector3Int(Mathf.FloorToInt(point.x/bucketSize),Mathf.FloorToInt(point.y/bucketSize),Mathf.FloorToInt(point.z/bucketSize));
        public void Clear()=>buckets.Clear();
        public void Add(Vector3 centre,float radius,SettlementDistrict district,RtsBuilding building=null){
            if(district!=SettlementDistrict.Industrial&&!Sensitive(district))return;
            var plot=new Plot(centre,radius,district,building);var min=Bucket(centre-Vector3.one*radius);var max=Bucket(centre+Vector3.one*radius);
            for(int z=min.z;z<=max.z;z++)for(int y=min.y;y<=max.y;y++)for(int x=min.x;x<=max.x;x++){var key=new Vector3Int(x,y,z);if(!buckets.TryGetValue(key,out var entries)){entries=new List<Plot>();buckets.Add(key,entries);}entries.Add(plot);}
        }
        public bool Allows(Vector3 centre,float radius,SettlementDistrict district){
            if(district!=SettlementDistrict.Industrial&&!Sensitive(district))return true;
            float reach=radius+IndustrialBuffer;var min=Bucket(centre-Vector3.one*reach);var max=Bucket(centre+Vector3.one*reach);
            for(int z=min.z;z<=max.z;z++)for(int y=min.y;y<=max.y;y++)for(int x=min.x;x<=max.x;x++)if(buckets.TryGetValue(new Vector3Int(x,y,z),out var entries))foreach(var plot in entries){if(plot.building!=null&&plot.building.removed||!Incompatible(district,plot.district))continue;float separation=radius+plot.radius+IndustrialBuffer;if((centre-plot.centre).sqrMagnitude<separation*separation)return false;}
            return true;
        }
    }
    public sealed partial class PlanetRtsBase {
        SettlementZoning zoning;int zoningIndexedCount;
        internal bool DistrictClear(BuildingDefinition definition,Vector3 direction,int team){
            if(!SettlementZoning.OutsideCentre(direction,Homes[team],Grid.Radius,Grid.FootprintRadius(definition),Grid.TileSize,definition.district))return false;
            if(zoning==null||zoningIndexedCount>Buildings.Count){zoning=new SettlementZoning(Grid.TileSize);zoningIndexedCount=0;}
            for(;zoningIndexedCount<Buildings.Count;zoningIndexedCount++){var building=Buildings[zoningIndexedCount];if(!building.removed)zoning.Add(Grid.Centre(Area(building))*Grid.Radius,Grid.FootprintRadius(building.definition),building.definition.district,building);}
            return zoning.Allows(direction.normalized*Grid.Radius,Grid.FootprintRadius(definition),definition.district);
        }
    }
}
