using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsBase {
        public PlanetConstructionGrid Grid {get;private set;}
        sealed class ConstructionPose {public PlanetConstructionGrid.Footprint area;public int terrain;public float foundation;public Quaternion rotation;public Vector3 direction;public Renderer[] renderers;}
        readonly Dictionary<Transform,ConstructionPose> constructionPoses=new Dictionary<Transform,ConstructionPose>();
        public Vector3 SnapBuilding(BuildingDefinition definition,Vector3 direction,int rotation)=>Grid.Snap(definition,direction,rotation);
        internal PlanetConstructionGrid.Footprint Area(RtsBuilding building)=>Grid.Area(building.definition,CubeSphere.Decode(building.position),building.rotation);
        public float FoundationRadius(RtsBuilding building){var pose=PrepareConstruction(building.visual.transform,building.definition,CubeSphere.Decode(building.position),building.rotation);return pose.foundation;}
        ConstructionPose PrepareConstruction(Transform target,BuildingDefinition definition,Vector3 direction,int rotation){
            var area=Grid.Area(definition,direction,rotation);int terrain=fixture.definition.Edits.Revision;
            if(constructionPoses.TryGetValue(target,out var previous)&&previous.terrain==terrain&&previous.area.face==area.face&&previous.area.x==area.x&&previous.area.y==area.y&&previous.area.rotation==area.rotation)return previous;
            float height=surface.SampleHeight(Grid.Centre(area));for(int i=0;i<4;i++)height=Mathf.Max(height,surface.SampleHeight(Grid.Point(area,i&1,(i>>1)&1)));
            var pose=new ConstructionPose{area=area,terrain=terrain,foundation=fixture.definition.radius+height+.15f,rotation=Grid.Rotation(area),direction=Grid.Centre(area),renderers=previous?.renderers??target.GetComponentsInChildren<Renderer>()};
            Grid.WarpAxes(area,out var x,out var z,out float plane);var scale=ModelScale(definition);
            foreach(var renderer in pose.renderers){renderer.GetPropertyBlock(properties);properties.SetVector("_ConstructionPlane",new Vector4(plane,pose.foundation,1,0));properties.SetVector("_ConstructionX",x);properties.SetVector("_ConstructionZ",z);properties.SetVector("_ConstructionScale",scale);renderer.SetPropertyBlock(properties);
                var source=renderer.GetComponent<MeshFilter>().sharedMesh.bounds;var warpedBounds=WarpBounds(source,scale,x,z,plane,pose.foundation);var inverseScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);renderer.localBounds=new Bounds(Vector3.Scale(warpedBounds.center,inverseScale),Vector3.Scale(warpedBounds.size,inverseScale));}
            var collision=target.GetComponent<BoxCollider>();if(collision!=null){var model=art.Find(definition.id)??art.Find(definition.mesh);var warped=WarpBounds(model.near.bounds,scale,x,z,plane,pose.foundation);collision.center=warped.center;collision.size=Vector3.Max(warped.size,Vector3.one*.5f);}
            constructionPoses[target]=pose;return pose;
        }
        static Bounds WarpBounds(Bounds source,Vector3 scale,Vector3 x,Vector3 z,float plane,float foundation){
            var result=new Bounds();for(int i=0;i<8;i++){var corner=source.center+Vector3.Scale(source.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));var p=PlanetConstructionGrid.Warp(Vector3.Scale(corner,scale),x,z,plane,foundation);if(i==0)result=new Bounds(p,Vector3.zero);else result.Encapsulate(p);}
            result.Encapsulate(PlanetConstructionGrid.Warp(Vector3.Scale(source.center+Vector3.up*source.extents.y,scale),x,z,plane,foundation));result.Expand(.02f);return result;
        }
        void PlaceConstruction(Transform target,BuildingDefinition definition,Vector3 direction,int rotation){var pose=PrepareConstruction(target,definition,direction,rotation);target.SetPositionAndRotation(fixture.RenderBodyPosition(new Double3(pose.direction)*pose.foundation),pose.rotation);}
        internal bool GridClear(PlanetConstructionGrid.Footprint area,out string reason){
            var port=Grid.Port(area);foreach(var b in Buildings){if(b.removed)continue;var other=Area(b);if(area.Overlaps(other)){reason="Building grid footprints overlap";return false;}if(area.Contains(Grid.Port(other))||other.Contains(port)){reason="Keep the neighbouring building's road entrance clear";return false;}}
            if(Roads!=null)for(int y=0;y<area.depth;y++)for(int x=0;x<area.width;x++)if(Roads.HasRoad(new PlanetNavCoordinate(fixture.definition.bodyId,area.face,area.x+x,area.y+y))){reason="Building footprint overlaps a road module";return false;}
            reason="";return true;
        }
    }
}
