using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MassiveArmy.Planetary;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools {
    /// <summary>Bakes bounded derivatives; never imports the two-million-triangle source into a runtime rig.</summary>
    public static class ImportedTankAssets {
        const string Source="Assets/MassiveArmy/Art/Models/TankOptimized/";
        const string Root="Assets/MassiveArmy/Resources/MassiveArmy/Vehicles";
        static Mesh Read(string part,string lod){
            using var reader=new BinaryReader(File.OpenRead(Source+part+lod+".tankmesh"));int count=reader.ReadInt32();
            if(count<=0||count%3!=0)throw new InvalidOperationException(part+lod+": empty or malformed derivative");
            var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var tags=new List<Vector2>();var colours=new List<Color>();var indices=new int[count];
            var unique=new Dictionary<(Vector3,Vector3,Vector2),int>();
            for(int i=0;i<count;i++){
                var point=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());var normal=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());var coord=new Vector2(reader.ReadSingle(),reader.ReadSingle());
                var key=(point,normal,coord);if(!unique.TryGetValue(key,out int index)){index=p.Count;unique.Add(key,index);p.Add(point);n.Add(normal);uv.Add(coord);tags.Add(new Vector2(part=="Turret"?1:0,0));colours.Add(Color.white);}indices[i]=index;
            }
            var mesh=new Mesh{name="Imported tank "+part+" "+lod,indexFormat=p.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(p);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetUVs(1,tags);mesh.SetColors(colours);mesh.triangles=indices;mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
        public static void OptimizeExisting(){foreach(string lod in new[]{"Detail","Army","Far"})foreach(string part in new[]{"Hull","Turret"})Save(Read(part,lod),"SF-TK1-"+part+lod);AssetDatabase.SaveAssets();}
        static Mesh Save(Mesh mesh,string name){mesh.name=name;string path=Root+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old!=null){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
        static Mesh Combine(Mesh hull,Mesh turret)=>CombineParts(new[]{hull,turret});
        static Mesh CombineParts(Mesh[] parts){var combined=new Mesh{indexFormat=IndexFormat.UInt32};var instances=new CombineInstance[parts.Length];for(int i=0;i<parts.Length;i++)instances[i]=new CombineInstance{mesh=parts[i],transform=Matrix4x4.identity};combined.CombineMeshes(instances,true,true);return combined;}
        static MeshRenderer Add(Transform root,string name,Mesh mesh,Material material){var go=new GameObject(name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;return renderer;}
        [MenuItem("Synthetic Front/Validation/Check imported tank assets")]
        public static void ValidateAssets(){
            var d=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/SF-TK1");if(d==null||d.ValidateDefinition().Length>0)throw new InvalidOperationException("Invalid tank definition");
            var rig=d.prefab.GetComponent<VehicleRig>();if(rig.torso==null||rig.muzzle==null||rig.articulatedRenderers.Length!=2)throw new InvalidOperationException("Separate hull and turret rig missing");
            var art=RtsModelArt.Load();int type=Array.IndexOf(art.unitIds,"tank"),tags=0;
            using(var reader=new BinaryReader(new MemoryStream(art.unitNear[type].bytes)))for(int i=0;i<ArtUnitRenderer.NearTriangles*3;i++){reader.BaseStream.Position=i*64+12;if(reader.ReadSingle()==1)tags++;}
            if(tags<3)throw new InvalidOperationException("GPU tank turret articulation tags missing");
            var hull=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/SF-TK1-HullDetail.asset");var turret=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/SF-TK1-TurretDetail.asset");var size=hull.bounds;size.Encapsulate(turret.bounds);
            if(size.min.y<-.02f||size.size.z<7||size.size.y>6||size.size.x>6)throw new InvalidOperationException("Tank axes or metre scale incorrect");
            File.WriteAllText("TestResults/Tank/assets-validation.json","{\"status\":\"PASS\",\"turretTaggedVertices\":"+tags+",\"detailTriangles\":"+(hull.triangles.Length+turret.triangles.Length)/3+",\"indexedDetailVertices\":"+(hull.vertexCount+turret.vertexCount)+"}");
        }
        [MenuItem("Synthetic Front/Vehicles/Bake imported tank replacement")]
        public static void Generate(){
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before baking the tank assets.");
            Directory.CreateDirectory(Root);var meshes=new Dictionary<string,Mesh>();
            foreach(string lod in new[]{"Detail","Army","Far"})foreach(string part in new[]{"Hull","Turret"})meshes[part+lod]=Save(Read(part,lod),"SF-TK1-"+part+lod);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MassiveArmy/Art/Models/hi3d-military-tank-v1-texture-0.png");if(texture==null)throw new InvalidOperationException("Tank albedo texture missing");
            string matPath=Root+"/SF-TK1.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(material==null){material=new Material(Shader.Find("MassiveArmy/Modular structures"));AssetDatabase.CreateAsset(material,matPath);}material.SetTexture("_MainTex",texture);EditorUtility.SetDirty(material);
            var army=Save(Combine(meshes["HullArmy"],meshes["TurretArmy"]),"SF-TK1-Army");var far=Save(Combine(meshes["HullFar"],meshes["TurretFar"]),"SF-TK1-Far");
            if(army.triangles.Length/3>ArtUnitRenderer.NearTriangles||far.triangles.Length/3>ArtUnitRenderer.FarTriangles)throw new InvalidOperationException("Tank exceeds fixed army LOD budget");
            var path=Root+"/SF-TK1.asset";var d=AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);if(d==null){d=ScriptableObject.CreateInstance<VehicleDefinition>();AssetDatabase.CreateAsset(d,path);}
            d.id="SF-TK1";d.displayName="Tracked Tank";d.locomotion=VehicleLocomotion.Tracked;d.size=new Vector3(4.93f,5.23f,8);d.mass=26000;d.maximumSpeed=15;d.reverseSpeed=5;d.acceleration=2.5f;d.braking=8;d.turnSpeed=32;d.turnAcceleration=70;d.chassisCentre=new Vector3(0,1.35f,0);d.chassisSize=new Vector3(4.7f,2.6f,7.3f);d.centreOfMass=new Vector3(0,1,0);d.torsoYaw=180;d.traversal.maximumStep=.65f;d.traversal.clearance=.15f;
            // Four contact patches approximate the tracks; their visual wheels stay in the optimized shell.
            d.wheels=new VehicleWheelDefinition[4];for(int i=0;i<4;i++)d.wheels[i]=new VehicleWheelDefinition{name="Track support "+i,position=new Vector3(i%2==0?-1.9f:1.9f,.55f,i<2?2.6f:-2.6f),radius=.55f,suspension=.3f};
            d.seats=new[]{new VehicleSeatDefinition{name="Driver periscope",role=VehicleSeatRole.Driver,permissions=VehicleSeatPermissions.Movement,position=new Vector3(.9f,1.7f,1.7f),eye=new Vector3(.9f,2.9f,1.9f),entry=new Vector3(3,0,1),exit=new Vector3(3.4f,.1f,1)},new VehicleSeatDefinition{name="Gunner sight",role=VehicleSeatRole.Gunner,permissions=VehicleSeatPermissions.Aim|VehicleSeatPermissions.Fire,aiByDefault=true,position=new Vector3(0,2.8f,0),eye=new Vector3(0,3.8f,2.1f),entry=new Vector3(-3,0,0),exit=new Vector3(-3.4f,.1f,0)}};
            var root=new GameObject("SF-TK1 imported tank");try{
                var rig=root.AddComponent<VehicleRig>();rig.definition=d;var hull=Add(root.transform,"Hull",meshes["HullDetail"],material);
                rig.torso=new GameObject("Turret yaw pivot").transform;rig.torso.SetParent(root.transform,false);rig.torso.localPosition=new Vector3(0,2.82f,0);
                // Mesh coordinates retain the whole-vehicle bind pose; offset the mesh around its ring pivot.
                var turret=Add(rig.torso,"Turret",meshes["TurretDetail"],material);turret.transform.localPosition=-rig.torso.localPosition;
                rig.muzzle=new GameObject("Muzzle").transform;rig.muzzle.SetParent(rig.torso,false);rig.muzzle.localPosition=new Vector3(0,.63f,3.8f);
                var box=root.AddComponent<BoxCollider>();box.center=d.chassisCentre;box.size=d.chassisSize;rig.chassis=box;var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=d.mass;
                rig.articulatedRenderers=new Renderer[]{hull,turret};var proxy=Add(root.transform,"Rigid distant tank",far,material);rig.distantProxy=proxy.gameObject;
                var lod=root.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.12f,rig.articulatedRenderers),new LOD(.002f,new Renderer[]{proxy})});lod.RecalculateBounds();
                d.prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/SF-TK1.prefab");EditorUtility.SetDirty(d);
            }finally{Object.DestroyImmediate(root);}
            var packPath=Root+"/ImportedTankPack.asset";var pack=AssetDatabase.LoadAssetAtPath<RtsModelPack>(packPath);if(pack==null){pack=ScriptableObject.CreateInstance<RtsModelPack>();AssetDatabase.CreateAsset(pack,packPath);}
            var bounds=army.bounds;pack.units=new[]{new ModelPackUnit{id="tank",enabled=true,size=bounds.size,lod0=new ModelLodSource{kind=ModelSourceKind.MeshOrPrefab,model=army,material=material,preserveArticulation=true},lod1=new ModelLodSource{kind=ModelSourceKind.MeshOrPrefab,model=far,material=material,preserveArticulation=true}}};
            // Clone the currently selected catalogue so existing building and unit overrides survive.
            using(var result=RtsModelPackBaker.Compile(pack,RtsModelArt.Load())){var folder=AssetDatabase.GenerateUniqueAssetPath(Root+"/ImportedTankBaked");AssetDatabase.CreateFolder(Root,Path.GetFileName(folder));RtsModelPackBaker.Persist(result,folder);pack.bakedCatalogue=result.catalogue;pack.bakedAt=DateTime.Now.ToString("s");RtsModelPackBaker.Use(result.catalogue);}
            EditorUtility.SetDirty(pack);AssetDatabase.SaveAssets();Directory.CreateDirectory("TestResults/Tank");File.WriteAllText("TestResults/Tank/baked.json","{\"status\":\"PASS\",\"armyTriangles\":"+army.triangles.Length/3+",\"farTriangles\":"+far.triangles.Length/3+",\"seats\":2,\"turretVertices\":"+meshes["TurretDetail"].vertexCount+"}");
        }
    }
}
