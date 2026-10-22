using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // A small reusable pool belongs to the base, never to army units or individual modules.
    internal sealed class ModularBuildingDetail:IDisposable {
        sealed class DoorView {public GameObject root;public MeshFilter mesh;public MeshRenderer renderer;public BoxCollider collision;}
        readonly GameObject root;readonly MeshCollider collision;readonly MeshRenderer glass;readonly MeshFilter glassMesh;
        readonly MeshFilter ramp;readonly MeshRenderer rampRenderer;readonly MeshCollider rampCollision;Mesh rampMesh;
        readonly MeshFilter number;readonly MeshRenderer numberRenderer;Mesh numberMesh;
        readonly List<MeshRenderer> roomRenderers=new List<MeshRenderer>();readonly List<MeshFilter> roomMeshes=new List<MeshFilter>();readonly List<DoorView> doorViews=new List<DoorView>();
        readonly ModularPortalVisibility visibility=new ModularPortalVisibility();uint shownDoors=uint.MaxValue;int rampTerrain=-1,rampRoad=-1;
        public RtsBuilding Building {get;private set;}public ModularBuildingLayout Layout {get;private set;}
        public int VisibleRooms {get;private set;}
        static MeshFilter MeshObject(Transform parent,string name,out MeshRenderer renderer){var go=new GameObject(name);go.transform.SetParent(parent,false);var mesh=go.AddComponent<MeshFilter>();renderer=go.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return mesh;}
        public ModularBuildingDetail(){root=new GameObject("Streamed modular interior");collision=root.AddComponent<MeshCollider>();glassMesh=MeshObject(root.transform,"Transparent windows",out glass);number=MeshObject(root.transform,"Building number",out numberRenderer);ramp=MeshObject(root.transform,"Terrain entrance ramp",out rampRenderer);rampCollision=ramp.gameObject.AddComponent<MeshCollider>();root.SetActive(false);}
        public void Bind(RtsBuilding b,ModularBuildingLayout layout,PlanetSurface surface,PlanetDevelopment world){Release();Building=b;Layout=layout;root.transform.SetParent(b.visual.transform,false);root.transform.localScale=Vector3.one;root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;collision.sharedMesh=layout.collision;
            while(roomRenderers.Count<layout.rooms.Length){var mesh=MeshObject(root.transform,"Portal room",out var renderer);roomMeshes.Add(mesh);roomRenderers.Add(renderer);}for(int i=0;i<roomRenderers.Count;i++){roomRenderers[i].gameObject.SetActive(i<layout.rooms.Length);if(i>=layout.rooms.Length)continue;roomMeshes[i].sharedMesh=layout.rooms[i].mesh;roomRenderers[i].sharedMaterial=layout.material;roomRenderers[i].name=layout.rooms[i].name;}
            while(doorViews.Count<layout.doors.Length){var doorRoot=new GameObject("Interactive module door");doorRoot.transform.SetParent(root.transform,false);var mesh=MeshObject(doorRoot.transform,"Door leaf",out var renderer);doorViews.Add(new DoorView{root=doorRoot,mesh=mesh,renderer=renderer,collision=mesh.gameObject.AddComponent<BoxCollider>()});}
            for(int i=0;i<doorViews.Count;i++){var view=doorViews[i];view.root.SetActive(i<layout.doors.Length);if(i>=layout.doors.Length)continue;var door=layout.doors[i];var q=Quaternion.Euler(0,door.yaw,0);view.root.transform.localPosition=door.centre-q*Vector3.right*(door.width*.5f)-Vector3.up*(door.height*.5f);view.root.transform.localRotation=q;view.mesh.sharedMesh=door.panel;view.mesh.transform.localPosition=new Vector3(door.width*.5f,door.height*.5f,0);view.renderer.sharedMaterial=layout.material;view.collision.center=Vector3.zero;view.collision.size=door.panel.bounds.size;}
            glassMesh.sharedMesh=layout.glass;glass.sharedMaterial=layout.glassMaterial;
            var g=new ModularGeometry();var qNumber=Quaternion.Euler(0,layout.numberYaw,0);string label=(b.index+1).ToString();g.Box(layout.numberPosition,new Vector3(Mathf.Max(.5f,label.Length*.22f),.42f,.045f),new Color(.08f,.1f,.12f),qNumber);g.Text(label,layout.numberPosition+qNumber*Vector3.forward*.035f,.3f,Color.white,qNumber);numberMesh=g.Build("Building address "+label);number.sharedMesh=numberMesh;numberRenderer.sharedMaterial=layout.material;
            RefreshTerrain(surface,world);b.visual.GetComponent<BoxCollider>().enabled=false;root.SetActive(true);shownDoors=uint.MaxValue;ApplyDoors();
        }
        public void RefreshTerrain(PlanetSurface surface,PlanetDevelopment world){if(rampTerrain==world.definition.Edits.Revision&&rampRoad==world.Base.Roads.Revision)return;rampTerrain=world.definition.Edits.Revision;rampRoad=world.Base.Roads.Revision;rampCollision.sharedMesh=null;if(rampMesh!=null)ArmyLifetime.Destroy(rampMesh);BuildEntranceRamp(surface,world);}
        void BuildEntranceRamp(PlanetSurface surface,PlanetDevelopment world){
            var g=new ModularGeometry();var area=world.Base.Area(Building);
            Vector3 Local(Vector3 p)=>root.transform.InverseTransformPoint(world.PlanetRenderCentre+p);
            var p0=Local(world.Base.PlotPoint(Building,world.Base.Grid.Point(area,0,0),-.04f));var p1=Local(world.Base.PlotPoint(Building,world.Base.Grid.Point(area,1,0),-.04f));var p2=Local(world.Base.PlotPoint(Building,world.Base.Grid.Point(area,1,1),-.04f));var p3=Local(world.Base.PlotPoint(Building,world.Base.Grid.Point(area,0,1),-.04f));
            g.Quad(p0,p1,p2,p3,Color.gray);
            if(world.Base.PlotEntrance(Building,out var a,out var b,out var c,out var d))g.Quad(Local(a),Local(d),Local(c),Local(b),Color.gray);
            // This mesh only supplies walking collision. The shared settlement mesh
            // draws the identical pad and driveway without a draw call per building.
            rampMesh=g.Build("Level plot and road access collision");ramp.sharedMesh=null;rampRenderer.enabled=false;rampCollision.sharedMesh=rampMesh;
        }

        void ApplyDoors(){if(Building==null||shownDoors==Building.modularDoors)return;shownDoors=Building.modularDoors;for(int i=0;i<Layout.doors.Length;i++){var door=Layout.doors[i];doorViews[i].root.transform.localRotation=Quaternion.Euler(0,door.yaw+((shownDoors&(1u<<i))!=0?95:0),0);}}
        public void Update(Camera camera){if(Building==null)return;ApplyDoors();visibility.Evaluate(Layout,camera,root.transform.localToWorldMatrix,Building.modularDoors);VisibleRooms=0;for(int i=0;i<Layout.rooms.Length;i++){roomRenderers[i].enabled=visibility.visible[i];if(visibility.visible[i])VisibleRooms++;}}
        public bool Interact(Ray ray){if(Building==null)return false;int selected=-1;float nearest=3.5f;for(int i=0;i<Layout.doors.Length;i++){var centre=doorViews[i].mesh.transform.position;float along=Vector3.Dot(centre-ray.origin,ray.direction);if(along<0||along>nearest||(ray.GetPoint(along)-centre).sqrMagnitude>1.5f*1.5f)continue;
                if(Physics.Raycast(ray,out var hit,along+.15f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&!hit.transform.IsChildOf(doorViews[i].root.transform)&&hit.distance<along-.3f)continue;nearest=along;selected=i;}
            if(selected<0)return false;Building.modularDoors^=1u<<selected;ApplyDoors();return true;}
        public void Release(){if(Building==null&&!root.activeSelf)return;if(Building!=null&&Building.visual!=null){var box=Building.visual.GetComponent<BoxCollider>();if(box!=null)box.enabled=!Building.removed;}root.SetActive(false);collision.sharedMesh=null;rampCollision.sharedMesh=null;if(rampMesh!=null)ArmyLifetime.Destroy(rampMesh);rampMesh=null;rampTerrain=-1;rampRoad=-1;if(numberMesh!=null)ArmyLifetime.Destroy(numberMesh);numberMesh=null;Building=null;Layout=null;}
        public void Dispose(){Release();ArmyLifetime.Destroy(root);}
    }
}
