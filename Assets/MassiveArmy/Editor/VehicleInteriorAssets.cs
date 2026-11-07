using System.Collections.Generic;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class VehicleInteriorAssets
    {
        const string Root="Assets/MassiveArmy/Resources/MassiveArmy/Vehicles/";
        static readonly Color Shell=new Color(.2f,.26f,.24f), Dark=new Color(.045f,.055f,.065f), Trim=new Color(.55f,.6f,.56f), Screen=new Color(.12f,.65f,.72f);
        public static void Upgrade(){
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"VehicleArmour.mat");
            var glass=AssetDatabase.LoadAssetAtPath<Material>(Root+"VehicleWindows.mat");if(glass==null){glass=new Material(Shader.Find("MassiveArmy/Vehicle windows"));AssetDatabase.CreateAsset(glass,Root+"VehicleWindows.mat");}
            foreach(string id in new[]{"SF-MW1","SF-LUV1"}){
                string path=Root+id+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var rig=root.GetComponent<VehicleRig>();var d=rig.definition;bool car=d.locomotion==VehicleLocomotion.Wheeled;
                    foreach(var name in new[]{"Cabin","Windshield","Side glazing","Upper command pod","Inset pilot visor"})foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())if(renderer.name==name)Object.DestroyImmediate(renderer.gameObject);
                    var previous=root.transform.Find("Crew interior");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
                    var interior=new GameObject("Crew interior");interior.transform.SetParent(root.transform,false);
                    float width=car?1.95f:d.size.x*.58f,front=car?1.4f:d.size.z*.24f,rear=car?-1.2f:-d.size.z*.24f,floor=car?.94f:d.seats[0].position.y-.12f,roof=car?2.32f:d.seats[0].eye.y+.85f;
                    float sill=car?1.55f:d.seats[0].eye.y-.38f;
                    var g=new ModularGeometry();var windows=new ModularGeometry();
                    void Box(Vector3 p,Vector3 size,Color c){g.Box(p,size,c);}
                    Box(new Vector3(0,floor,(front+rear)*.5f),new Vector3(width,.1f,front-rear),Dark);
                    Box(new Vector3(0,roof,(front+rear)*.5f),new Vector3(width+.12f,.12f,front-rear+.1f),Shell);
                    foreach(float side in new[]{-1f,1f}){
                        float x=side*width*.5f;
                        Box(new Vector3(x,(floor+sill)*.5f,(front+rear)*.5f),new Vector3(.09f,sill-floor,front-rear),Shell);
                        foreach(float z in new[]{front,rear,(front+rear)*.5f})Box(new Vector3(x,(sill+roof)*.5f,z),new Vector3(.075f,roof-sill,.08f),Trim);
                        float y=(sill+roof)*.5f;
                        windows.Quad(new Vector3(x,sill,rear),new Vector3(x,sill,front),new Vector3(x,roof,front),new Vector3(x,roof,rear),Color.white);
                        Box(new Vector3(x-side*.1f,sill-.08f,.15f),new Vector3(.12f,.06f,.32f),Trim);
                    }
                    Box(new Vector3(0,(floor+sill)*.5f,front),new Vector3(width,sill-floor,.1f),Shell);
                    Box(new Vector3(0,(floor+roof)*.5f,rear),new Vector3(width,roof-floor,.1f),Shell);
                    Box(new Vector3(0,(sill+roof)*.5f,front),new Vector3(.07f,roof-sill,.07f),Trim);
                    windows.Quad(new Vector3(-width*.5f,sill,front),new Vector3(width*.5f,sill,front),new Vector3(width*.5f,roof,front),new Vector3(-width*.5f,roof,front),Color.white);
                    float dash=front-.25f;
                    Box(new Vector3(0,sill-.15f,dash),new Vector3(width-.15f,.3f,.4f),Dark);
                    foreach(var seat in d.seats){
                        if(!car&&seat.role==VehicleSeatRole.Commander){seat.position.z=-.7f;seat.eye=seat.position+new Vector3(0,.65f,.3f);EditorUtility.SetDirty(d);}
                        var cushion=root.transform.Find(seat.name+" cushion");var back=root.transform.Find(seat.name+" back");if(cushion!=null)cushion.localPosition=seat.position;if(back!=null)back.localPosition=seat.position+new Vector3(0,.3f,-.25f);
                        Box(seat.position-new Vector3(0,.2f,0),new Vector3(.3f,.3f,.3f),Trim);
                        if(seat.role==VehicleSeatRole.Driver||seat.role==VehicleSeatRole.Gunner){
                            Box(new Vector3(seat.position.x,sill+.02f,dash-.225f),new Vector3(.38f,.19f,.025f),Screen);
                            for(int b=0;b<4;b++)Box(new Vector3(seat.position.x-.14f+b*.09f,sill-.15f,dash-.22f),new Vector3(.035f,.025f,.02f),b%2==0?Screen:new Color(.9f,.45f,.08f));
                            if(car){for(int segment=0;segment<16;segment++){float a=segment*Mathf.PI*2/16;Box(new Vector3(seat.position.x+Mathf.Cos(a)*.17f,sill-.03f+Mathf.Sin(a)*.17f,dash-.42f),new Vector3(.065f,.065f,.04f),Dark);}Box(new Vector3(seat.position.x,sill-.03f,dash-.42f),new Vector3(.3f,.035f,.04f),Trim);}
                            else foreach(float side in new[]{-1f,1f})Box(seat.position+new Vector3(side*.28f,.27f,.25f),new Vector3(.06f,.24f,.06f),Dark);
                        }
                    }
                    MakeMesh(interior.transform,"Cockpit walls, dashboard and controls",id+"_Interior",g,material);
                    MakeMesh(interior.transform,"Clear windows",id+"_Windows",windows,glass);
                    var renderers=new List<Renderer>();foreach(var renderer in root.GetComponentsInChildren<Renderer>())if(renderer.enabled&&!renderer.transform.IsChildOf(rig.distantProxy.transform)&&!renderer.name.StartsWith("LOD 2"))renderers.Add(renderer);
                    rig.articulatedRenderers=renderers.ToArray();var group=root.GetComponent<LODGroup>();var levels=group.GetLODs();levels[0]=new LOD(levels[0].screenRelativeTransitionHeight,rig.articulatedRenderers);group.SetLODs(levels);group.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
        }
        static void MakeMesh(Transform parent,string name,string file,ModularGeometry geometry,Material material){
            string path=Root+file+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);var fresh=geometry.Build(name);
            if(mesh==null){mesh=fresh;AssetDatabase.CreateAsset(mesh,path);}else{EditorUtility.CopySerialized(fresh,mesh);Object.DestroyImmediate(fresh);EditorUtility.SetDirty(mesh);}
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
    }
}
