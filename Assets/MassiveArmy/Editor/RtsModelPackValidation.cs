using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools
{
    public static class RtsModelPackValidation
    {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Art/Validate model packs")]
        public static void Run(){
            var report=new Report();var pack=ScriptableObject.CreateInstance<RtsModelPack>();GameObject prefab=null;string temporaryFolder=null;
            void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);report.checks.Add(message);}
            void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
            try{
                RtsModelPackBaker.Sync(pack);var original=RtsModelPackBaker.Original;var active=RtsModelArt.Load();
                Check(pack.units.Length==31&&pack.buildings.Length>49,"Catalogue includes all unit and civilisation building IDs");
                using(var unchanged=RtsModelPackBaker.Compile(pack))Check(unchanged.catalogue.unitNear[0]==original.unitNear[0]&&unchanged.catalogue.buildings.All(x=>!x.customLods),"Disabled overrides preserve original assets");
                var b=pack.buildings.First(x=>x.id=="command");b.enabled=true;b.size=new Vector3(8,6,8);b.lod0.kind=ModelSourceKind.Procedural;b.lod0.shape=ModelProceduralShape.StackedBuilding;b.lod1.kind=b.lod2.kind=ModelSourceKind.Cube;
                var u=pack.units[0];u.enabled=true;u.size=new Vector3(1,2,1);u.lod0.kind=ModelSourceKind.Procedural;u.lod0.shape=ModelProceduralShape.Cylinder;u.lod1.kind=ModelSourceKind.Cube;
                using(var baked=RtsModelPackBaker.Compile(pack)){
                    var art=baked.catalogue;var building=art.Find(b.id);
                    Check(building.customLods&&building.near.GetIndexCount(0)==72&&building.far.GetIndexCount(0)==36&&building.distant.GetIndexCount(0)==36,"Building has three independent baked LODs");
                    var definitions=RtsModelPackBaker.Catalogue();
                    Check(art.FindBuilding(definitions.Buildings["command"])==building,"Command override resolves by its own building ID");
                    foreach(var definition in definitions.buildings.Where(d=>original.Find(d.id)==null&&original.Find(d.mesh)!=null))
                        {var resolved=art.FindBuilding(definition);var baseline=original.Find(definition.mesh);Check(resolved.near==baseline.near&&resolved.far==baseline.far&&resolved.material==baseline.material&&!resolved.customLods,"Shared default mesh remains unchanged for "+definition.id);}
                    Check(art.FindBuilding(definitions.Buildings["civil_housing"]).near==original.FindBuilding(definitions.Buildings["civil_housing"]).near,"Command-only override leaves residential housing geometry unchanged");

                    Check(art.unitNear[0].bytes.Length==512*3*64&&art.unitFar[0].bytes.Length==128*3*64,"Custom unit banks retain fixed GPU allocation");
                    Check(art.unitNear[1]==original.unitNear[1]&&art.unitIds.SequenceEqual(original.unitIds),"Other unit banks and catalogue indices remain intact");
                    for(int view=0;view<8;view++)Check(Read(art.impostors,view).Any(x=>x.a>32),"Automatic impostor view "+view+" contains visible geometry");
                    Check(Read(art.albedo,1).SequenceEqual(Read(original.albedo,1)),"Untouched texture-array layer survives GPU copy");
                    using(var renderer=new ArtUnitRenderer(false,art))Check(renderer.NearMesh.vertexCount==512*3&&renderer.FarMesh.vertexCount==128*3,"Baked catalogue uploads through the shared GPU renderer");
                    temporaryFolder="Assets/__ModelPackValidation_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(temporaryFolder));RtsModelPackBaker.Persist(baked,temporaryFolder);
                    string bankPath=AssetDatabase.GetAssetPath(art.unitNear[0]),artPath=AssetDatabase.GetAssetPath(art);byte[] expected=art.unitNear[0].bytes;Resources.UnloadAsset(art.unitNear[0]);Resources.UnloadAsset(art.impostors);Resources.UnloadAsset(art);
                    var reload=AssetDatabase.LoadAssetAtPath<RtsModelArt>(artPath);Check(AssetDatabase.LoadAssetAtPath<TextAsset>(bankPath).bytes.SequenceEqual(expected),"Saved binary bank survives reload");Check(reload.Find(b.id).material.GetTexture("_Albedo")!=null,"Saved material texture reference survives reload");Check(reload.Find(b.id).distant!=null,"Saved distant LOD reference survives reload");Check(Read(reload.impostors,0).Any(x=>x.a>32),"Saved generated impostor survives texture-array reload");using(var renderer=new ArtUnitRenderer(false,reload))Check(renderer.NearMesh!=null,"Saved catalogue uploads to GPU after reload");
                    AssetDatabase.DeleteAsset(temporaryFolder);temporaryFolder=null;
                }
                u.enabled=false;var housing=pack.buildings.First(x=>x.id=="civil_housing");housing.enabled=true;housing.size=new Vector3(4,4,4);
                using(var explicitHousing=RtsModelPackBaker.Compile(pack)){
                    var definitions=RtsModelPackBaker.Catalogue();var art=explicitHousing.catalogue;
                    Check(art.FindBuilding(definitions.Buildings[housing.id])==art.Find(housing.id)&&art.Find(housing.id).customLods,"Explicit residential override still resolves by its own ID");
                    Check(art.Find(housing.id).near!=art.Find(b.id).near,"Command and residential overrides keep independent meshes");
                }housing.enabled=false;u.enabled=true;
                prefab=new GameObject("Model-pack multi-mesh fixture");
                for(int i=0;i<2;i++){var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.transform.SetParent(prefab.transform,false);child.transform.localPosition=new Vector3(i,0,0);}
                b.lod0.kind=ModelSourceKind.MeshOrPrefab;b.lod0.model=prefab;
                using(var baked=RtsModelPackBaker.Compile(pack))Check(baked.catalogue.Find(b.id).near.GetIndexCount(0)==72,"Prefab child meshes and transforms are combined");
                u.enabled=false;b.lod0.model=null;Reject(()=>{using var invalid=RtsModelPackBaker.Compile(pack);},"Missing mesh assignments are rejected before activation");
                b.enabled=false;u.enabled=true;u.impostors=new Texture2D[1];Reject(()=>{using var invalid=RtsModelPackBaker.Compile(pack);},"Incomplete custom impostor sets are rejected");
                u.impostors=Array.Empty<Texture2D>();u.size=new Vector3(1000,1000,1000);Reject(()=>{using var invalid=RtsModelPackBaker.Compile(pack);},"Models outside GPU culling bounds are rejected");
                Check(RtsModelArt.Load()==active&&!original.Find(b.id).customLods,"Validation leaves the active pack and original catalogue unchanged");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{if(temporaryFolder!=null)AssetDatabase.DeleteAsset(temporaryFolder);Object.DestroyImmediate(pack);if(prefab!=null)Object.DestroyImmediate(prefab);}
            Directory.CreateDirectory("TestResults/ArtPass/ModelPacks");File.WriteAllText("TestResults/ArtPass/ModelPacks/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Model pack validation "+report.status);if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
        static Color32[] Read(Texture2DArray texture,int layer){var copy=RtsModelPackBaker.Slice(texture,layer);try{return copy.GetPixels32();}finally{Object.DestroyImmediate(copy);}}
    }
}
