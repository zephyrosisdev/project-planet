using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary {
    public static class ColonyPrototypeValidation {
        [Serializable] sealed class Report {public string status="PASS",error="",scope="Imported Blender meshes, named sockets, collision apertures and construction compatibility. These checks do not establish whole-game frame rate or fleet scale.";public int assets,triangles;public List<string> checks=new List<string>();}
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Synthetic Front/Diagnostics/Validate colony prototypes")]
#endif
        static bool RenderInstancingProbe(Material material){
            const int size=768;var target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var texture=new Texture2D(size,size,TextureFormat.RGB24,false);var commands=new CommandBuffer{name="Colony prototype instancing smoke check"};
            material.SetInt("_ZTest",(int)(SystemInfo.usesReversedZBuffer?CompareFunction.GreaterEqual:CompareFunction.LessEqual));
            float ambient=Shader.GetGlobalFloat("_ArmyAmbientLight");var light=Shader.GetGlobalVector("_ArmyLightDirection");var direct=Shader.GetGlobalVector("_ArmyDirectLight");
            try{
                var eye=new Vector3(12,10,-17);var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye,Quaternion.LookRotation(new Vector3(0,1,0)-eye),Vector3.one).inverse;
                commands.SetRenderTarget(target);commands.SetInvertCulling(false);commands.ClearRenderTarget(true,true,new Color(.04f,.06f,.09f,1),SystemInfo.usesReversedZBuffer?0:1);commands.SetViewProjectionMatrices(view,GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(45,1,.1f,100),true));commands.SetGlobalFloat("_ArmyAmbientLight",1);commands.SetGlobalVector("_ArmyLightDirection",new Vector4(.3f,.8f,-.4f,0));commands.SetGlobalVector("_ArmyDirectLight",new Vector4(.9f,.85f,.8f,0));
                var transforms=new[]{Matrix4x4.TRS(new Vector3(-4,0,0),Quaternion.identity,Vector3.one),Matrix4x4.identity,Matrix4x4.TRS(new Vector3(4,0,0),Quaternion.Euler(0,40,0),Vector3.one)};
                commands.DrawMeshInstanced(ColonyPrototypeLibrary.Find("machine-foundry").Mesh("foundry probe"),0,material,0,transforms,transforms.Length);commands.SetInvertCulling(false);Graphics.ExecuteCommandBuffer(commands);RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,size,size),0,0);texture.Apply();
                int lit=0,cyan=0,warm=0;foreach(var pixel in texture.GetPixels32()){if(pixel.r>65||pixel.g>65||pixel.b>80)lit++;if(pixel.r<100&&pixel.g>140&&pixel.b>140)cyan++;if(pixel.r>160&&pixel.g>90&&pixel.b<120)warm++;}
                Directory.CreateDirectory("TestResults/Industrial/ColonyPrototypes");File.WriteAllBytes("TestResults/Industrial/ColonyPrototypes/UnityInstancing.png",texture.EncodeToPNG());return lit>5000&&cyan>30&&warm>30;
            }finally{material.SetInt("_ZTest",(int)CompareFunction.LessEqual);RenderTexture.active=previous;commands.Release();RenderTexture.ReleaseTemporary(target);ArmyLifetime.Destroy(texture);Shader.SetGlobalFloat("_ArmyAmbientLight",ambient);Shader.SetGlobalVector("_ArmyLightDirection",light);Shader.SetGlobalVector("_ArmyDirectLight",direct);}
        }
        public static void Run(){
            var report=new Report();GameObject collisionRoot=null;bool previous=Physics.queriesHitBackfaces;
            void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);report.checks.Add(name);}
            try{
                var source=Resources.Load<TextAsset>("MassiveArmy/ColonyPrototypes/Catalogue");Check(source!=null,"Blender catalogue imports into Resources");var data=JsonUtility.FromJson<ColonyPrototypeCatalogue>(source.text);Check(data.version==1&&data.assets.Length==38,"38 versioned prototypes retain stable asset IDs");
                var profiles=PlayerBuildingProfiles.Create();Check(profiles.Length==26&&(int)PlayerModuleKind.Stairs180==14,"Original serialized module IDs survive the expanded catalogue");
                collisionRoot=new GameObject("Isolated colony collision validation");collisionRoot.transform.position=new Vector3(100000,100000,100000);var collider=collisionRoot.AddComponent<MeshCollider>();Physics.queriesHitBackfaces=false;
                foreach(var asset in data.assets){
                    report.assets++;report.triangles+=asset.triangles.Length/3;var mesh=asset.Mesh(asset.id);
                    Check(mesh.vertexCount>0&&mesh.normals.Length==mesh.vertexCount&&mesh.colors.Length==mesh.vertexCount&&asset.lod.triangles.Length<=asset.triangles.Length,asset.id+": mesh, normals, colours and reduced-detail export valid");
                    var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;bool winding=true;for(int n=0;n<triangles.Length;n+=3){int a=triangles[n],b=triangles[n+1],c=triangles[n+2];var cross=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(cross.sqrMagnitude>.00000001f)winding&=Vector3.Dot(cross.normalized,normals[a])>.9f;}Check(winding,asset.id+": outward normals agree with triangle winding");
#if UNITY_EDITOR
                    var fbx=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MassiveArmy/Art/ColonyPrototypes/"+asset.id+".fbx");Check(fbx!=null,asset.id+": native FBX imported");foreach(var socket in asset.sockets){bool found=false;foreach(var transform in fbx.GetComponentsInChildren<Transform>(true))if(transform.name=="SOCKET_"+socket.name){found=true;break;}if(!found){var names=new List<string>();foreach(var transform in fbx.GetComponentsInChildren<Transform>(true))names.Add(transform.name);throw new InvalidOperationException(asset.id+": missing native socket "+socket.name+"; nodes: "+string.Join(",",names));}Check(found,asset.id+": native socket "+socket.name);}
#endif
                    if(asset.family=="room"){
                        collider.sharedMesh=asset.Collision();Physics.SyncTransforms();var at=collisionRoot.transform.position;
                        Check(collider.Raycast(new Ray(at+new Vector3(0,2.5f,0),Vector3.down),out var floor,3)&&floor.normal.y>.95f&&Mathf.Abs(floor.point.y-at.y-.2f)<.025f,asset.id+": upward walkable floor");
                        Check(!collider.Raycast(new Ray(at+new Vector3(0,1.5f,-asset.dimensions.z*.5f-1),Vector3.forward),out _,asset.dimensions.z+2),asset.id+": unobstructed entry and central interior aisle");
                        var profile=profiles[(int)Enum.Parse(typeof(PlayerModuleKind),asset.id.Substring(5))];Check(profile.Room&&profile.inputs.Length>=3,asset.id+": floor and room-port placement sockets loaded");
                    }
                    if(asset.family=="machine"){var definition=IndustrialCatalog.Machines[asset.id.Substring(8)];Check(Math.Abs(asset.dimensions.x-definition.Width)<.001&&Math.Abs(asset.dimensions.z-definition.Depth)<.001,asset.id+": simulation footprint matches authored metres");}
                }
                var habitat=profiles[(int)PlayerModuleKind.HabitatRoom];var corridor=profiles[(int)PlayerModuleKind.CorridorRoom];bool overlaps=false;foreach(var a in habitat.solids)foreach(var b in corridor.solids)if(PlayerBuildingProfiles.Overlap(a,Vector3.zero,Quaternion.identity,b,new Vector3(0,0,6),Quaternion.identity))overlaps=true;Check(!overlaps,"Adjacent room ports join without colliding floors or wall solids");
                Check(profiles[(int)PlayerModuleKind.WorkshopRoom].outputs.Length==4&&profiles[(int)PlayerModuleKind.WarehouseRoom].outputs.Length==6,"Workshops and warehouses expose designated machine mounts");
                var material=ColonyPrototypeLibrary.Material("Colony validation");Check(material.shader!=null&&material.shader.isSupported,"Prototype presentation shader supported");
#if UNITY_EDITOR
                Check(!UnityEditor.ShaderUtil.ShaderHasError(material.shader),"Prototype shader compiles without errors");
#endif
                Check(RenderInstancingProbe(material),"GPU-instanced machine meshes render visible pixels");ArmyLifetime.Destroy(material);Check(File.Exists("ArtSource/MartianColony/ColonyPrototypes.blend"),"Editable Blender source retained");
            }catch(Exception error){report.status="FAIL";report.error=error.ToString();Debug.LogException(error);}finally{Physics.queriesHitBackfaces=previous;if(collisionRoot!=null)ArmyLifetime.Destroy(collisionRoot);}
            Directory.CreateDirectory("TestResults/Industrial/ColonyPrototypes");File.WriteAllText("TestResults/Industrial/ColonyPrototypes/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Colony prototype validation "+report.status+": "+report.assets+" assets, "+report.triangles+" triangles, "+report.checks.Count+" checks");
        }
    }
}
