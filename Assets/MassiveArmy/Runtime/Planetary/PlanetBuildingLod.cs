using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        [Serializable]public struct BuildingViewStatistics {public int near,far,proxy,subpixel,hidden;}
        public BuildingViewStatistics BuildingViews {get;private set;}
        internal readonly bool LegacyCityPresentation=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-city-legacy-presentation")>=0;
        sealed class BuildingView {
            public RtsBuilding building;public Transform root;public MeshFilter shell;public MeshRenderer renderer;public Renderer[] accessories;public Mesh near,far,proxy;public Bounds bounds,warpedBounds;public Vector3 modelScale;public ShadowCastingMode shadows=(ShadowCastingMode)(-1);public int lod=-2;public bool visible=true;public float scale=1;public ConstructionPose pose;
        }
        readonly List<BuildingView> buildingViews=new List<BuildingView>(),pendingConstructionViews=new List<BuildingView>();
        readonly Dictionary<RtsModelArt.Building,Mesh> proxyMeshes=new Dictionary<RtsModelArt.Building,Mesh>();
        readonly Dictionary<long,RtsBuilding> buildingIndex=new Dictionary<long,RtsBuilding>();
        readonly Plane[] buildingPlanes=new Plane[6];
        Matrix4x4 buildingCamera;Vector3 buildingCentre;int buildingHeight=-1,viewTerrain=-1,viewCount=-1,lastViewRevision=-1,lastViewScaleRevision=-1,scaleRevision;
        Vector3 rebasedCentre;int rebasedTerrain=-1,rebasedCount=-1,rebasedRevision=-1,rebasedMarkers=-1;
        readonly List<Double3> markerBodies=new List<Double3>();int markerTerrain=-1;
        internal RtsBuilding FindBuilding(long id,bool includeRemoved=false){
            if(id<=0)return null;if(buildingIndex.TryGetValue(id,out var cached)&&cached.persistentId==id)return includeRemoved||!cached.removed?cached:null;
            // Stable infrastructure identities only; no army/civilian pose cache.
            buildingIndex.Clear();foreach(var b in Buildings)if(b.persistentId>0)buildingIndex[b.persistentId]=b;
            return buildingIndex.TryGetValue(id,out cached)&&(includeRemoved||!cached.removed)?cached:null;
        }
        void RegisterBuildingView(RtsBuilding b,RtsModelArt.Building model){
            if(LegacyCityPresentation)return;var state=new BuildingView{building=b,root=b.visual.transform,near=model.near,far=model.far??model.near};var shell=state.root.Find("Current RTS shell");if(shell!=null){state.shell=shell.GetComponent<MeshFilter>();state.renderer=shell.GetComponent<MeshRenderer>();state.shell.sharedMesh=model.near;}
            state.accessories=state.root.GetComponentsInChildren<Renderer>(true);state.bounds=model.near.bounds;state.bounds.Encapsulate(state.far.bounds);if(model.gateLeaf!=null)state.bounds.Encapsulate(model.gateLeaf.bounds);
            if(state.shell!=null){if(!proxyMeshes.TryGetValue(model,out state.proxy)){state.proxy=BuildingProxy(state.bounds);proxyMeshes.Add(model,state.proxy);}if(state.far==state.near)state.far=state.proxy;}
            state.pose=PrepareConstruction(state.root,b.definition,CubeSphere.Decode(b.position),b.rotation);CacheBuildingBounds(state);buildingViews.Add(state);if(!b.Complete)pendingConstructionViews.Add(state);viewCount=-1;
        }
        static Mesh BuildingProxy(Bounds bounds){
            var vertices=new List<Vector3>(24);var uv=new List<Vector2>(24);var indices=new List<int>(36);
            Vector3[] normals={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            foreach(var n in normals){var u=Mathf.Abs(n.y)>.5f?Vector3.right:Vector3.Cross(Vector3.up,n);var v=Vector3.Cross(n,u);int start=vertices.Count;
                for(int y=0;y<2;y++)for(int x=0;x<2;x++){var corner=n+u*(x*2-1)+v*(y*2-1);vertices.Add(bounds.center+Vector3.Scale(corner,bounds.extents));uv.Add(new Vector2(x,y));}
                indices.AddRange(new[]{start,start+1,start+2,start+1,start+3,start+2});}
            var mesh=new Mesh{name="Shared distant building silhouette"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        void LegacyRebaseInfrastructure(){foreach(var b in Buildings){PlaceTransform(b.visual.transform,b.definition,CubeSphere.Decode(b.position),b.rotation);b.visual.transform.localScale=Vector3.one*(b.Complete?1:Mathf.Lerp(.1f,1,b.construction/b.definition.time));}
            for(int i=0;i<markers.Count;i++){var up=CubeSphere.Decode(Nodes[i].position);markers[i].transform.SetPositionAndRotation(fixture.RenderBodyPosition(surface.GetSurfacePositionPrecise(up)),Quaternion.FromToRotation(Vector3.up,up));}if(ghost!=null)PlaceTransform(ghost.transform,Placing,ghostDirection,PlacementRotation);}
        void RebaseInfrastructure(){
            if(LegacyCityPresentation){LegacyRebaseInfrastructure();return;}
            for(int i=pendingConstructionViews.Count-1;i>=0;i--){var state=pendingConstructionViews[i];var b=state.building;if(b.removed){pendingConstructionViews.RemoveAt(i);continue;}float size=b.Complete?1:Mathf.Lerp(.1f,1,b.construction/b.definition.time);if(size!=state.scale){state.root.localScale=Vector3.one*size;state.scale=size;scaleRevision++;}if(b.Complete)pendingConstructionViews.RemoveAt(i);}
            int terrain=fixture.definition.Edits.Revision;var centre=fixture.PlanetRenderCentre;
            if(rebasedMarkers!=markers.Count||rebasedCount!=Buildings.Count||rebasedCentre!=centre||rebasedTerrain!=terrain||rebasedRevision!=Revision){
                foreach(var state in buildingViews){var b=state.building;if(b.removed)continue;if(state.pose.terrain!=terrain){state.pose=PrepareConstruction(state.root,b.definition,CubeSphere.Decode(b.position),b.rotation);CacheBuildingBounds(state);}
                    PlacePreparedConstruction(state.root,state.pose);
                }
                if(markerTerrain!=terrain){markerBodies.Clear();markerTerrain=terrain;}for(int i=markerBodies.Count;i<markers.Count;i++)markerBodies.Add(surface.GetSurfacePositionPrecise(CubeSphere.Decode(Nodes[i].position)));
                for(int i=0;i<markers.Count;i++){var position=fixture.RenderBodyPosition(markerBodies[i]);if(markers[i].transform.position!=position)markers[i].transform.SetPositionAndRotation(position,Quaternion.FromToRotation(Vector3.up,CubeSphere.Decode(Nodes[i].position)));}
                rebasedMarkers=markers.Count;rebasedCount=Buildings.Count;rebasedCentre=centre;rebasedTerrain=terrain;rebasedRevision=Revision;
            }
            if(ghost!=null)PlaceTransform(ghost.transform,Placing,ghostDirection,PlacementRotation);UpdateBuildingViews();
        }
        void UpdateBuildingViews(){
            var camera=fixture.View;if(camera==null)return;var matrix=camera.projectionMatrix*camera.worldToCameraMatrix;var centre=fixture.PlanetRenderCentre;int height=camera.pixelHeight;
            if(viewCount==buildingViews.Count&&viewTerrain==fixture.definition.Edits.Revision&&buildingCamera==matrix&&buildingCentre==centre&&buildingHeight==height&&lastViewRevision==Revision&&lastViewScaleRevision==scaleRevision)return;
            lastViewScaleRevision=scaleRevision;lastViewRevision=Revision;buildingCamera=matrix;buildingCentre=centre;buildingHeight=height;viewCount=buildingViews.Count;viewTerrain=fixture.definition.Edits.Revision;
            GeometryUtility.CalculateFrustumPlanes(camera,buildingPlanes);float focal=height*.5f/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);var eye=camera.transform.position;var forward=camera.transform.forward;var planetEye=eye-centre;float eyeDistance=planetEye.magnitude;
            float opaqueRadius=Mathf.Max(1,fixture.definition.radius-fixture.definition.ReliefBound);float sine=opaqueRadius/Mathf.Max(opaqueRadius,eyeDistance),cosine=Mathf.Sqrt(Mathf.Max(0,1-sine*sine));var stats=new BuildingViewStatistics();
            foreach(var state in buildingViews){var b=state.building;if(b.removed){stats.hidden++;continue;}var pose=state.pose;var scale=state.modelScale;var bounds=state.warpedBounds;
                var position=state.root.position+pose.rotation*(bounds.center*state.scale);float radius=bounds.extents.magnitude*state.scale;var delta=position-eye;float distance=delta.magnitude,depth=Vector3.Dot(delta,forward);
                float pixels=2*radius*focal/Mathf.Max(.01f,depth-radius);bool visible=depth+radius>camera.nearClipPlane&&depth-radius<camera.farClipPlane;
                if(visible)for(int i=0;i<6;i++)if(buildingPlanes[i].GetDistanceToPoint(position)<-radius){visible=false;break;}
                if(visible&&eyeDistance>opaqueRadius&&distance>radius&&Vector3.Dot(position-centre,planetEye)<opaqueRadius*opaqueRadius){float projection=Vector3.Dot(-planetEye,delta)/distance,perpendicular=planetEye.sqrMagnitude-projection*projection;
                    if(projection>0&&perpendicular<opaqueRadius*opaqueRadius&&distance-radius>projection+Mathf.Sqrt(Mathf.Max(0,opaqueRadius*opaqueRadius-perpendicular))){float angular=radius/distance;if(angular<sine&&projection/eyeDistance>cosine*Mathf.Sqrt(Mathf.Max(0,1-angular*angular))+sine*angular)visible=false;}}
                if(visible&&pixels<2){stats.subpixel++;visible=false;}else if(!visible)stats.hidden++;
                int lod=!visible?-1:pixels>=64?0:pixels>=12?1:2;
                if(visible&&state.lod==0&&pixels>=56)lod=0;else if(visible&&state.lod==1&&pixels>=10&&pixels<72)lod=1;else if(visible&&state.lod==2&&pixels<14)lod=2;
                if(visible){if(lod==0)stats.near++;else if(lod==1)stats.far++;else stats.proxy++;}
                if(state.shell!=null&&lod>=0&&lod!=state.lod){state.shell.sharedMesh=lod==0?state.near:lod==1?state.far:state.proxy;var inv=new Vector3(1/scale.x,1/scale.y,1/scale.z);state.renderer.localBounds=new Bounds(Vector3.Scale(bounds.center,inv),Vector3.Scale(bounds.size,inv));}
                if(state.visible!=visible||state.lod!=lod){foreach(var renderer in state.accessories)renderer.enabled=visible&&(renderer==state.renderer||lod!=2);state.visible=visible;}
                if(state.renderer!=null){var shadows=visible&&pixels>=32&&b.definition.housingCapacity==0?ShadowCastingMode.On:ShadowCastingMode.Off;if(state.shadows!=shadows){foreach(var renderer in state.accessories)renderer.shadowCastingMode=shadows;state.shadows=shadows;}}
                state.lod=lod;
            }BuildingViews=stats;
        }
        void CacheBuildingBounds(BuildingView state){Grid.WarpAxes(state.pose.area,out var x,out var z,out float plane);state.modelScale=ModelScale(state.building.definition);state.warpedBounds=WarpBounds(state.bounds,state.modelScale,x,z,plane,state.pose.foundation);}
        void DisposeBuildingViews(){foreach(var mesh in proxyMeshes.Values)ArmyLifetime.Destroy(mesh);proxyMeshes.Clear();buildingViews.Clear();pendingConstructionViews.Clear();buildingIndex.Clear();}
    }
}
