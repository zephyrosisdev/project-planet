using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsBase {
        public PlanetConstructionGrid Grid {get;private set;}
        sealed class ConstructionPose {public PlanetConstructionGrid.Footprint area;public int terrain;public float foundation;public Quaternion rotation;public Vector3 direction;public Renderer[] renderers;public bool positioned;public Vector3 renderedPosition;}
        readonly Dictionary<Transform,ConstructionPose> constructionPoses=new Dictionary<Transform,ConstructionPose>();
        public Vector3 SnapBuilding(BuildingDefinition definition,Vector3 direction,int rotation)=>Grid.Snap(definition,direction,rotation);
        readonly struct CachedBuildingArea {
            public readonly Vector2 position;public readonly int rotation,width,depth;public readonly PlanetConstructionGrid.Footprint area;
            public CachedBuildingArea(RtsBuilding b,PlanetConstructionGrid.Footprint area){position=b.position;rotation=b.rotation;width=b.definition.footprintWidth;depth=b.definition.footprintDepth;this.area=area;}
            public bool Matches(RtsBuilding b)=>position.Equals(b.position)&&rotation==b.rotation&&width==b.definition.footprintWidth&&depth==b.definition.footprintDepth;
        }
        readonly Dictionary<RtsBuilding,CachedBuildingArea> buildingAreas=new Dictionary<RtsBuilding,CachedBuildingArea>();
        internal PlanetConstructionGrid.Footprint Area(RtsBuilding building){if(buildingAreas.TryGetValue(building,out var cached)&&cached.Matches(building))return cached.area;var area=Grid.Area(building.definition,CubeSphere.Decode(building.position),building.rotation);buildingAreas[building]=new CachedBuildingArea(building,area);return area;}
        public float FoundationRadius(RtsBuilding building){var pose=PrepareConstruction(building.visual.transform,building.definition,CubeSphere.Decode(building.position),building.rotation);return pose.foundation;}
        internal Vector3 PlotPoint(RtsBuilding building,Vector3 direction,float offset=0){
            float radius=FoundationRadius(building)+offset;
            return direction*(IsModularBuilding(building)?radius/Mathf.Max(.1f,Vector3.Dot(direction,Grid.Centre(Area(building)))):radius);
        }
        // Called only when entering planet first-person mode. Normal walking uses
        // the bounded nearby collision pool; no infrastructure scan per frame.
        internal double PlayerEntryRadius(Vector3 direction){
            double radius=0;var cell=Grid.Cell(direction);foreach(var building in Buildings)if(!building.removed&&Area(building).Contains(cell))radius=System.Math.Max(radius,PlotPoint(building,direction,.02f).magnitude+.05);return radius;
        }
        // The driveway runs from the lot boundary to the adjoining road's centre.
        // Both the renderer and the bounded interior collision pool use these vertices.
        internal bool PlotEntrance(RtsBuilding building,out Vector3 a,out Vector3 b,out Vector3 c,out Vector3 d){
            var area=Area(building);var port=Grid.Port(area);a=b=c=d=default;
            if(Roads==null||!Roads.HasRoad(port))return false;
            var door=Grid.Door(area);float x=door.x+.5f,y=door.y+.5f,half=Grid.CarriagewayWidth*.5f;
            var centre=Grid.Centre(area);var road=Grid.Direction(port);var across=Vector3.Cross(centre,road).normalized;
            Vector3 edge=area.rotation==0?Grid.Point(area.face,x,area.y+area.depth):area.rotation==1?Grid.Point(area.face,area.x,y):area.rotation==2?Grid.Point(area.face,x,area.y):Grid.Point(area.face,area.x+area.width,y);
            a=PlotPoint(building,(edge-across*(half/fixture.definition.radius)).normalized,-.02f);b=PlotPoint(building,(edge+across*(half/fixture.definition.radius)).normalized,-.02f);
            c=surface.GetSurfacePositionPrecise((road+across*(half/fixture.definition.radius)).normalized,.13).Float;d=surface.GetSurfacePositionPrecise((road-across*(half/fixture.definition.radius)).normalized,.13).Float;return true;
        }
        ConstructionPose PrepareConstruction(Transform target,BuildingDefinition definition,Vector3 direction,int rotation){
            var area=Grid.Area(definition,direction,rotation);int terrain=fixture.definition.Edits.Revision;
            if(constructionPoses.TryGetValue(target,out var previous)&&previous.terrain==terrain&&previous.area.face==area.face&&previous.area.x==area.x&&previous.area.y==area.y&&previous.area.rotation==area.rotation)return previous;
            // A rigid modular floor needs a tangent plane above the entire plot, not a
            // constant-radius sphere which falls away beneath its corners.
            bool planar=art.FindBuilding(definition)?.modular!=null;var centre=Grid.Centre(area);
            float radius=fixture.definition.radius+surface.SampleHeight(centre);
            int samplesX=Mathf.Max(2,Mathf.CeilToInt(area.width*Grid.TileSize/8)),samplesY=Mathf.Max(2,Mathf.CeilToInt(area.depth*Grid.TileSize/8));
            for(int sy=0;sy<=samplesY;sy++)for(int sx=0;sx<=samplesX;sx++){var d=Grid.Point(area,sx/(float)samplesX,sy/(float)samplesY);float ground=fixture.definition.radius+surface.SampleHeight(d);radius=Mathf.Max(radius,planar?ground*Vector3.Dot(d,centre):ground);}
            float height=radius-fixture.definition.radius;
            var pose=new ConstructionPose{area=area,terrain=terrain,foundation=fixture.definition.radius+height+.15f,rotation=Grid.Rotation(area),direction=Grid.Centre(area),renderers=previous?.renderers??target.GetComponentsInChildren<Renderer>()};
            Grid.WarpAxes(area,out var x,out var z,out float plane);var scale=ModelScale(definition);bool modular=art.FindBuilding(definition)?.modular!=null;
            foreach(var renderer in pose.renderers){renderer.GetPropertyBlock(properties);properties.SetVector("_ConstructionPlane",new Vector4(plane,pose.foundation,modular?0:1,0));properties.SetVector("_ConstructionX",x);properties.SetVector("_ConstructionZ",z);properties.SetVector("_ConstructionScale",scale);renderer.SetPropertyBlock(properties);
                var source=renderer.GetComponent<MeshFilter>().sharedMesh.bounds;var warpedBounds=modular?source:WarpBounds(source,scale,x,z,plane,pose.foundation);var inverseScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);renderer.localBounds=new Bounds(Vector3.Scale(warpedBounds.center,inverseScale),Vector3.Scale(warpedBounds.size,inverseScale));}
            var collision=target.GetComponent<BoxCollider>();if(collision!=null){var model=art.FindBuilding(definition);var warped=modular?model.near.bounds:WarpBounds(model.near.bounds,scale,x,z,plane,pose.foundation);collision.center=warped.center;collision.size=Vector3.Max(warped.size,Vector3.one*.5f);}
            constructionPoses[target]=pose;return pose;
        }
        static Bounds WarpBounds(Bounds source,Vector3 scale,Vector3 x,Vector3 z,float plane,float foundation){
            var result=new Bounds();for(int i=0;i<8;i++){var corner=source.center+Vector3.Scale(source.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var p=PlanetConstructionGrid.Warp(Vector3.Scale(corner,scale),x,z,plane,foundation);if(i==0)result=new Bounds(p,Vector3.zero);else result.Encapsulate(p);}
            result.Encapsulate(PlanetConstructionGrid.Warp(Vector3.Scale(source.center+Vector3.up*source.extents.y,scale),x,z,plane,foundation));result.Expand(.02f);return result;
        }
        void PlaceConstruction(Transform target,BuildingDefinition definition,Vector3 direction,int rotation){var pose=PrepareConstruction(target,definition,direction,rotation);PlacePreparedConstruction(target,pose);}
        void PlacePreparedConstruction(Transform target,ConstructionPose pose){var position=fixture.RenderBodyPosition(new Double3(pose.direction)*pose.foundation);if(LegacyCityPresentation||!pose.positioned||position!=pose.renderedPosition){target.SetPositionAndRotation(position,pose.rotation);pose.positioned=true;pose.renderedPosition=position;}}
        // Sparse infrastructure broad phase, never GPU army records. Placement extends
        // it on construction and invalidates it on destruction; road searches own snapshots.
        internal sealed class ConstructionOccupancy {
            const int BucketSize=16;
            readonly struct Occupant {internal readonly PlanetConstructionGrid.Footprint area;internal readonly PlanetNavCoordinate port;internal readonly bool gate;internal Occupant(PlanetConstructionGrid.Footprint area,PlanetNavCoordinate port,bool gate){this.area=area;this.port=port;this.gate=gate;}}
            readonly Dictionary<(CubeFace,int,int),List<Occupant>> buckets=new Dictionary<(CubeFace,int,int),List<Occupant>>();readonly Stack<List<Occupant>> spareBuckets=new Stack<List<Occupant>>();
            readonly PlanetConstructionGrid grid;
            internal ConstructionOccupancy(PlanetConstructionGrid grid){this.grid=grid;}
            internal void Clear(){foreach(var entries in buckets.Values){entries.Clear();spareBuckets.Push(entries);}buckets.Clear();}
            internal void Add(PlanetConstructionGrid.Footprint area,bool gate=false){var port=grid.Port(area);var entry=new Occupant(area,port,gate);
                for(int y=area.y/BucketSize;y<=(area.y+area.depth-1)/BucketSize;y++)for(int x=area.x/BucketSize;x<=(area.x+area.width-1)/BucketSize;x++)AddBucket((area.face,x,y));
                AddBucket((port.face,port.x/BucketSize,port.y/BucketSize));
                void AddBucket((CubeFace,int,int) key){if(!buckets.TryGetValue(key,out var entries)){entries=spareBuckets.Count>0?spareBuckets.Pop():new List<Occupant>();buckets.Add(key,entries);}entries.Add(entry);}
            }
            internal bool RoadCellClear(PlanetNavCoordinate cell){if(buckets.TryGetValue((cell.face,cell.x/BucketSize,cell.y/BucketSize),out var entries))foreach(var entry in entries)if(entry.area.Contains(cell)&&!(entry.gate&&GatePassage(entry.area,cell)))return false;return true;}
            internal bool Clear(PlanetConstructionGrid.Footprint area){var port=grid.Port(area);
                for(int y=area.y/BucketSize;y<=(area.y+area.depth-1)/BucketSize;y++)for(int x=area.x/BucketSize;x<=(area.x+area.width-1)/BucketSize;x++)if(Blocked((area.face,x,y)))return false;
                return !Blocked((port.face,port.x/BucketSize,port.y/BucketSize));
                bool Blocked((CubeFace,int,int) key){if(!buckets.TryGetValue(key,out var entries))return false;foreach(var entry in entries)if(area.Overlaps(entry.area)||area.Contains(entry.port)||entry.area.Contains(port))return true;return false;}
            }
        }
        ConstructionOccupancy placementOccupancy;int placementIndexedCount;bool placementDirty;
        void InvalidatePlacementOccupancy(){placementDirty=true;}
        ConstructionOccupancy CaptureConstructionOccupancy(){var occupancy=placementOccupancy??(placementOccupancy=new ConstructionOccupancy(Grid));if(placementDirty||placementIndexedCount>Buildings.Count){occupancy.Clear();placementIndexedCount=0;placementDirty=false;}for(;placementIndexedCount<Buildings.Count;placementIndexedCount++){var b=Buildings[placementIndexedCount];if(!b.removed)occupancy.Add(Area(b));}return occupancy;}
        internal bool GridClear(PlanetConstructionGrid.Footprint area,out string reason,BuildingDefinition candidate=null,ConstructionOccupancy occupancy=null){
            if(occupancy!=null){if(!occupancy.Clear(area)){reason="Building grid footprint or road entrance is occupied";return false;}}
            else {var port=Grid.Port(area);foreach(var b in Buildings){if(b.removed)continue;var other=Area(b);if(area.Overlaps(other)){reason="Building grid footprints overlap";return false;}if(area.Contains(Grid.Port(other))||other.Contains(port)){reason="Keep the neighbouring building's road entrance clear";return false;}}}
            var entrance=Grid.Port(area);if(Roads!=null&&Roads.OtherPortReserved(entrance)||Rails!=null&&Rails.HasRoad(entrance)){reason="Keep road and rail entrances separate";return false;}
            if(candidate?.id=="civil_freight_station"){var trackEntrance=Grid.Port(new PlanetConstructionGrid.Footprint(area.face,area.x,area.y,area.width,area.depth,(area.rotation+1)&3));if(Roads!=null&&Roads.HasRoad(trackEntrance)||Rails!=null&&(Rails.OtherPortReserved(trackEntrance)||!Rails.FootprintCellClear(trackEntrance))){reason="Freight station requires a separate clear rail entrance";return false;}}
            if(Roads!=null)for(int y=0;y<area.depth;y++)for(int x=0;x<area.width;x++)if((Roads.HasRoad(new PlanetNavCoordinate(fixture.definition.bodyId,area.face,area.x+x,area.y+y))||Roads.OtherPortReserved(new PlanetNavCoordinate(fixture.definition.bodyId,area.face,area.x+x,area.y+y)))&&!(candidate?.id=="gate"&&GatePassage(area,new PlanetNavCoordinate(fixture.definition.bodyId,area.face,area.x+x,area.y+y)))){reason="Building footprint overlaps a road module";return false;}
            if(Rails!=null)for(int y=0;y<area.depth;y++)for(int x=0;x<area.width;x++)if(Rails.HasRoad(new PlanetNavCoordinate(fixture.definition.bodyId,area.face,area.x+x,area.y+y))){reason="Building footprint overlaps a railway module";return false;}
            reason="";return true;
        }
    }
}
