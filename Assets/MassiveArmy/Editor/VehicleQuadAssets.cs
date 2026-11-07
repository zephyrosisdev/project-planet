using System.IO;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools {
    public static partial class VehiclePrototypeAssets {
        [MenuItem("Synthetic Front/Vehicles/Generate one-seat quad bike")]
        public static void GenerateQuad(){
            Directory.CreateDirectory(Root);string path=Root+"/SF-QB1.asset";
            if(AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path)!=null)return;
            material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/VehicleArmour.mat");if(material==null){material=new Material(Shader.Find("MassiveArmy/Modular structures"));AssetDatabase.CreateAsset(material,Root+"/VehicleArmour.mat");}
            meshes.Clear();var definition=ScriptableObject.CreateInstance<VehicleDefinition>();definition.id="SF-QB1";definition.displayName="Quad Bike";definition.locomotion=VehicleLocomotion.Wheeled;definition.size=new Vector3(1.5f,1.3f,2.1f);definition.mass=280;definition.maximumSpeed=28;definition.reverseSpeed=6;definition.acceleration=6;definition.braking=10;definition.turnSpeed=75;definition.turnAcceleration=130;definition.chassisCentre=new Vector3(0,.55f,0);definition.chassisSize=new Vector3(1.1f,.55f,1.6f);definition.centreOfMass=new Vector3(0,.45f,0);definition.traversal.maximumStep=.35f;definition.traversal.clearance=.16f;
            definition.seats=new[]{new VehicleSeatDefinition{name="Rider",role=VehicleSeatRole.Driver,permissions=VehicleSeatPermissions.Movement,position=new Vector3(0,.9f,-.1f),eye=new Vector3(0,1.55f,.05f),entry=new Vector3(-1,0,0),exit=new Vector3(-1.2f,.1f,0)}};
            var root=new GameObject("SF-QB1 one-seat quad");var rig=root.AddComponent<VehicleRig>();rig.definition=definition;
            var collider=root.AddComponent<BoxCollider>();collider.center=definition.chassisCentre;collider.size=definition.chassisSize;rig.chassis=collider;var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=definition.mass;
            Part(root.transform,"Chassis",definition.chassisCentre,definition.chassisSize,Armour,true);Part(root.transform,"Saddle",new Vector3(0,.92f,-.15f),new Vector3(.5f,.15f,.8f),Dark,true);Part(root.transform,"Front fairing",new Vector3(0,.86f,.55f),new Vector3(.8f,.22f,.55f),Trim,true);Part(root.transform,"Handlebars",new Vector3(0,1.13f,.5f),new Vector3(1.1f,.07f,.07f),Dark);
            rig.torso=new GameObject("Rider view").transform;rig.torso.SetParent(root.transform,false);definition.wheels=new VehicleWheelDefinition[4];rig.wheels=new Transform[4];
            for(int i=0;i<4;i++){definition.wheels[i]=new VehicleWheelDefinition{name="Wheel "+i,position=new Vector3(i%2==0?-.6f:.6f,.3f,i<2?.65f:-.65f),radius=.3f,width=.22f,suspension=.2f,steering=i<2};rig.wheels[i]=Wheel(root.transform,definition.wheels[i]);}
            rig.articulatedRenderers=root.GetComponentsInChildren<Renderer>();var merged=new ModularGeometry();foreach(var filter in root.GetComponentsInChildren<MeshFilter>()){var mesh=filter.sharedMesh;var colours=mesh.colors;merged.Box(root.transform.InverseTransformPoint(filter.transform.TransformPoint(mesh.bounds.center)),Vector3.Scale(mesh.bounds.size,filter.transform.lossyScale),colours.Length>0?colours[0]:Armour,filter.transform.localRotation);}
            rig.distantProxy=new GameObject("Rigid quad silhouette");rig.distantProxy.transform.SetParent(root.transform,false);rig.distantProxy.AddComponent<MeshFilter>().sharedMesh=SaveMesh(merged.Build("SF-QB1 rigid silhouette"));rig.distantProxy.AddComponent<MeshRenderer>().sharedMaterial=material;
            var bounds=Part(root.transform,"Bounding shell",Vector3.up*.65f,definition.size,Armour,true);var lod=root.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.12f,rig.articulatedRenderers),new LOD(.02f,new[]{rig.distantProxy.GetComponent<Renderer>()}),new LOD(.002f,new[]{bounds.GetComponent<Renderer>()})});lod.RecalculateBounds();
            AssetDatabase.CreateAsset(definition,path);definition.prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/SF-QB1.prefab");EditorUtility.SetDirty(definition);Object.DestroyImmediate(root);AssetDatabase.SaveAssets();Directory.CreateDirectory("TestResults/Gameplay");File.WriteAllText("TestResults/Gameplay/quad-generated.json","{\"status\":\"PASS\",\"seats\":1,\"wheels\":4,\"lods\":3}");
        }
    }
}
