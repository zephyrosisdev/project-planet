using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MassiveArmy.Planetary;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools
{
    public static class RtsModelPackBaker
    {
        public const string SelectionPath="Assets/MassiveArmy/Resources/MassiveArmy/ModelArt/Selection.asset";
        public const int BuildingNearBudget=4096,BuildingFarBudget=512,BuildingProxyBudget=128;
        const int AtlasSize=512;
        public static RtsCatalog Catalogue(){var c=RtsCatalog.Load();CivilisationCatalogue.Load().Extend(c);return c;}
        public static RtsModelArt Original=>Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");
        public static void Sync(RtsModelPack pack){
            var c=Catalogue();var art=Original;var oldB=pack.buildings.ToDictionary(x=>x.id);var oldU=pack.units.ToDictionary(x=>x.id);
            pack.buildings=c.buildings.Where(d=>d.id!="civil_road"&&d.id!="civil_rail").Select(d=>oldB.TryGetValue(d.id,out var entry)?entry:new ModelPackBuilding{id=d.id,size=(art.Find(d.id)??art.Find(d.mesh))?.near.bounds.size??new Vector3(d.footprintWidth*8,8,d.footprintDepth*8)}).ToArray();
            pack.units=c.units.Select((d,i)=>oldU.TryGetValue(d.id,out var entry)?entry:new ModelPackUnit{id=d.id,size=BankBounds(art.unitNear[i]).size}).ToArray();EditorUtility.SetDirty(pack);
        }
        public static Bounds BankBounds(TextAsset data){using var r=new BinaryReader(new MemoryStream(data.bytes));var b=new Bounds();for(int i=0;i<data.bytes.Length/64;i++){var p=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());if(i==0)b=new Bounds(p,Vector3.zero);else b.Encapsulate(p);r.BaseStream.Position=(i+1)*64;}return b;}
        public sealed class Result:IDisposable {
            public RtsModelArt catalogue;public readonly List<Object> owned=new List<Object>();public T Own<T>(T value)where T:Object{owned.Add(value);return value;}
            public void Dispose(){foreach(var value in owned)if(value!=null&&!AssetDatabase.Contains(value))Object.DestroyImmediate(value);}
        }
        sealed class Surface {public Material material;public Texture2D albedo,mask,normal;public Color tint=Color.white;public bool original;}
        sealed class Part {public Mesh mesh;public Matrix4x4 matrix;public int submesh,surface;public bool original;}
        sealed class Shape:IDisposable {
            public readonly List<Surface> surfaces=new List<Surface>();public readonly List<Mesh> temporary=new List<Mesh>();
            public readonly List<Part>[] lods;public Shape(int count){lods=new List<Part>[count];}
            public void Dispose(){foreach(var mesh in temporary)Object.DestroyImmediate(mesh);}
        }
        static Texture2D Map(Material material,params string[] keys){if(material==null)return null;foreach(string key in keys){int i=material.shader.FindPropertyIndex(key);if(i>=0&&material.shader.GetPropertyType(i)==ShaderPropertyType.Texture&&material.GetTexture(key) is Texture2D tex)return tex;}return null;}
        static float Number(Material m,float fallback,params string[] keys){if(m!=null)foreach(string key in keys)if(m.HasProperty(key)){int i=m.shader.FindPropertyIndex(key);var type=m.shader.GetPropertyType(i);if(type==ShaderPropertyType.Float||type==ShaderPropertyType.Range)return m.GetFloat(key);}return fallback;}
        static Color Tint(Material m){foreach(string key in new[]{"_BaseColor","_Color","_Tint"})if(m!=null&&m.HasProperty(key))return m.GetColor(key);return Color.white;}
        static Material BakeMaterial()=>new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/MassiveArmy/Editor/ModelPackBake.shader"));
        static Texture2D Read(Texture2D input,int size,bool linear,bool normal=false,Color? tint=null){
            var rt=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB);var saved=RenderTexture.active;var material=BakeMaterial();
            try{material.SetFloat("_NormalMode",normal?1:0);material.SetColor("_Tint",tint??Color.white);Graphics.Blit(input??Texture2D.whiteTexture,rt,material,0);RenderTexture.active=rt;var t=new Texture2D(size,size,TextureFormat.RGBA32,true,linear);t.ReadPixels(new Rect(0,0,size,size),0,0);t.Apply(true);return t;}finally{RenderTexture.active=saved;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(material);}
        }
        static bool NormalImport(Texture2D t)=>t!=null&&AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter importer&&importer.textureType==TextureImporterType.NormalMap;
        internal static Texture2D Slice(Texture2DArray array,int slice){
            var target=RenderTexture.GetTemporary(array.width,array.height,0,RenderTextureFormat.ARGB32,array.isDataSRGB?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);var saved=RenderTexture.active;var material=BakeMaterial();
            try{material.SetTexture("_Array",array);material.SetFloat("_Layer",slice);Graphics.Blit(Texture2D.whiteTexture,target,material,2);RenderTexture.active=target;var texture=new Texture2D(array.width,array.height,TextureFormat.RGBA32,false,!array.isDataSRGB);texture.ReadPixels(new Rect(0,0,array.width,array.height),0,0);texture.Apply();return texture;}
            finally{RenderTexture.active=saved;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(material);}
        }
        static Texture2D[] Atlas(Shape shape,Surface original){
            if(shape.surfaces.Count>16)throw new InvalidOperationException("A model can use at most 16 material tiles across its LODs. Combine materials into an atlas first.");
            int cells=Mathf.CeilToInt(Mathf.Sqrt(Math.Max(1,shape.surfaces.Count))),tile=AtlasSize/cells;var textures=new Texture2D[3];
            for(int channel=0;channel<3;channel++){
                var texture=new Texture2D(AtlasSize,AtlasSize,TextureFormat.RGBA32,true,channel!=0);var pixels=Enumerable.Repeat(channel==2?new Color(.5f,.5f,1,1):channel==1?new Color(0,1,0,.3f):Color.white,AtlasSize*AtlasSize).ToArray();
                for(int k=0;k<shape.surfaces.Count;k++){
                    var s=shape.surfaces[k];var m=s.material;Texture2D source=channel==0?s.albedo??Map(m,"_BaseColorMap","_BaseMap","_MainTex","_Albedo"):channel==1?s.mask??Map(m,"_Metallic","_MetallicGlossMap","_MaskMap"):s.normal??Map(m,"_Normal","_NormalMap","_BumpMap");
                    Color tint=channel==0?s.tint*Tint(m):Color.white;var read=Read(source,tile,channel!=0,channel==2&&NormalImport(source),tint);var data=read.GetPixels();Object.DestroyImmediate(read);
                    bool directMask=s.original||Map(m,"_Metallic")!=null;
                    for(int y=0;y<tile;y++)for(int x=0;x<tile;x++){
                        Color colour=data[y*tile+x];if(channel==1&&!directMask)colour=new Color(source==null?Number(m,0,"_Metallic"):colour.r,1,0,source==null?Number(m,.3f,"_Smoothness","_Glossiness"):colour.a);
                        if(channel==2&&source==null)colour=new Color(.5f,.5f,1,1);
                        pixels[((k/cells)*tile+y)*AtlasSize+(k%cells)*tile+x]=colour;
                    }
                }
                texture.SetPixels(pixels);texture.Apply(true);textures[channel]=texture;
            }return textures;
        }
        static Mesh Primitive(ModelLodSource source,int lod){
            if(source.kind==ModelSourceKind.Procedural&&source.shape==ModelProceduralShape.Vehicle)return ArmyMeshes.Tank(lod==0);
            if(source.kind==ModelSourceKind.Procedural&&source.shape==ModelProceduralShape.Pyramid){var m=new Mesh();m.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f),Vector3.up};m.triangles=new[]{0,2,1,0,3,2,0,1,4,1,2,4,2,3,4,3,0,4};m.RecalculateNormals();m.uv=m.vertices.Select(v=>new Vector2(v.x+.5f,v.z+.5f)).ToArray();m.RecalculateBounds();return m;}
            var obj=GameObject.CreatePrimitive(source.kind==ModelSourceKind.Procedural&&source.shape==ModelProceduralShape.Cylinder?PrimitiveType.Cylinder:PrimitiveType.Cube);var mesh=Object.Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);Object.DestroyImmediate(obj);
            if(source.kind==ModelSourceKind.Procedural&&source.shape==ModelProceduralShape.StackedBuilding){var small=Object.Instantiate(mesh);mesh.CombineMeshes(new[]{new CombineInstance{mesh=small,transform=Matrix4x4.TRS(new Vector3(0,.25f,0),Quaternion.identity,new Vector3(1,.5f,1))},new CombineInstance{mesh=small,transform=Matrix4x4.TRS(new Vector3(0,.75f,0),Quaternion.identity,new Vector3(.65f,.5f,.65f))}});Object.DestroyImmediate(small);}return mesh;
        }
        static Mesh FromBank(TextAsset bank){
            var positions=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();var uv=new List<Vector2>();var parts=new List<Vector2>();using var r=new BinaryReader(new MemoryStream(bank.bytes));
            for(int i=0;i<bank.bytes.Length/64;i++){positions.Add(new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()));float part=r.ReadSingle();normals.Add(new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()));r.ReadSingle();tangents.Add(new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle()));uv.Add(new Vector2(r.ReadSingle(),r.ReadSingle()));r.ReadSingle();r.ReadSingle();parts.Add(new Vector2(part,0));}
            var m=new Mesh();m.SetVertices(positions);m.SetNormals(normals);m.SetTangents(tangents);m.SetUVs(0,uv);m.SetUVs(1,parts);m.SetTriangles(Enumerable.Range(0,positions.Count).ToArray(),0);m.RecalculateBounds();return m;
        }
        static void Collect(Shape shape,ModelLodSource source,int lod,Mesh original,Surface defaultSurface){
            var parts=new List<Part>();shape.lods[lod]=parts;
            void Add(Mesh mesh,Matrix4x4 matrix,Material material,bool old){
                if(mesh==null)throw new InvalidOperationException("Missing mesh in LOD "+lod);if(!mesh.isReadable)throw new InvalidOperationException(mesh.name+": enable Read/Write on this model's import settings before baking.");
                for(int sub=0;sub<mesh.subMeshCount;sub++){
                    var surface=old?defaultSurface:new Surface{material=source.material??material,albedo=source.albedo,tint=source.tint};
                    int at=shape.surfaces.FindIndex(s=>ReferenceEquals(s,surface)||!old&&!s.original&&s.material==surface.material&&s.albedo==surface.albedo&&s.tint==surface.tint);if(at<0){at=shape.surfaces.Count;shape.surfaces.Add(surface);}
                    parts.Add(new Part{mesh=mesh,matrix=Matrix4x4.Rotate(Quaternion.Euler(source.rotation))*matrix,submesh=sub,surface=at,original=old});
                }
            }
            if(source.kind==ModelSourceKind.Original){if(original==null)throw new InvalidOperationException("No original mesh for LOD "+lod);Add(original,Matrix4x4.identity,defaultSurface.material,true);return;}
            if(source.kind!=ModelSourceKind.MeshOrPrefab){var mesh=Primitive(source,lod);shape.temporary.Add(mesh);Add(mesh,Matrix4x4.identity,null,false);return;}
            if(source.model is Mesh direct){Add(direct,Matrix4x4.identity,null,false);return;}
            if(!(source.model is GameObject prefab))throw new InvalidOperationException("Assign a mesh or prefab for LOD "+lod);
            var group=prefab.GetComponentInChildren<LODGroup>();var renderers=group!=null&&group.GetLODs().Length>0?group.GetLODs()[Math.Min(lod,group.GetLODs().Length-1)].renderers:prefab.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in renderers){if(renderer==null)continue;Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
                var materials=renderer.sharedMaterials;for(int sub=0;sub<mesh.subMeshCount;sub++){int before=parts.Count;Add(mesh,prefab.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix,materials.Length>sub?materials[sub]:null,false);for(int i=parts.Count-1;i>=before;i--)if(parts[i].submesh!=sub)parts.RemoveAt(i);}}
            if(parts.Count==0)throw new InvalidOperationException("Prefab has no mesh renderers: "+prefab.name);
        }
        static Mesh Geometry(Shape shape,int lod,Vector3 size,int budget){
            if(size.x<=0||size.y<=0||size.z<=0||!float.IsFinite(size.x+size.y+size.z))throw new InvalidOperationException("Model dimensions must be positive and finite.");
            var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var joint=new List<Vector2>();int cells=Mathf.CeilToInt(Mathf.Sqrt(shape.surfaces.Count)),tile=AtlasSize/cells;
            foreach(var part in shape.lods[lod]){var mesh=part.mesh;var v=mesh.vertices;var normals=mesh.normals;var coords=mesh.uv;var oldJoints=mesh.uv2;var normalMatrix=part.matrix.inverse.transpose;
                foreach(int index in mesh.GetTriangles(part.submesh)){
                    var tex=coords.Length==v.Length?coords[index]:Vector2.zero;if(!float.IsFinite(tex.x+tex.y)||tex.x<-.001f||tex.y<-.001f||tex.x>1.001f||tex.y>1.001f)throw new InvalidOperationException(mesh.name+": UVs must be in 0..1 for safe material atlasing.");
                    var point=part.matrix.MultiplyPoint3x4(v[index]);var normal=normals.Length==v.Length?normalMatrix.MultiplyVector(normals[index]).normalized:Vector3.up;if(!float.IsFinite(point.x+point.y+point.z+normal.x+normal.y+normal.z))throw new InvalidOperationException(mesh.name+": non-finite mesh data.");p.Add(point);n.Add(normal);
                    uv.Add(new Vector2(((part.surface%cells)*tile+2+Mathf.Clamp01(tex.x)*(tile-4))/AtlasSize,((part.surface/cells)*tile+2+Mathf.Clamp01(tex.y)*(tile-4))/AtlasSize));joint.Add(part.original&&oldJoints.Length==v.Length?oldJoints[index]:Vector2.zero);
                }}
            if(p.Count==0||p.Count/3>budget)throw new InvalidOperationException("LOD "+lod+" has "+p.Count/3+" triangles; budget is "+budget+". Assign a lower-detail LOD or a cube/procedural source.");
            var bounds=new Bounds(p[0],Vector3.zero);foreach(var point in p)bounds.Encapsulate(point);var scale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));
            for(int i=0;i<p.Count;i++){var point=p[i]-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);p[i]=Vector3.Scale(point,scale);n[i]=Vector3.Scale(n[i],new Vector3(1/scale.x,1/scale.y,1/scale.z)).normalized;}
            var result=new Mesh{indexFormat=p.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};result.SetVertices(p);result.SetNormals(n);result.SetUVs(0,uv);result.SetUVs(1,joint);result.SetTriangles(Enumerable.Range(0,p.Count).ToArray(),0);result.RecalculateTangents();result.RecalculateBounds();return result;
        }
        public static byte[] Pack(Mesh mesh,int triangles){
            using var stream=new MemoryStream();using var w=new BinaryWriter(stream);var p=mesh.vertices;var n=mesh.normals;var t=mesh.tangents;var uv=mesh.uv;var joints=mesh.uv2;var indices=mesh.triangles;
            if(indices.Length>triangles*3)throw new InvalidOperationException("Unit vertex bank budget exceeded.");
            for(int i=0;i<triangles*3;i++){bool real=i<indices.Length;int at=real?indices[i]:0;Vector3 position=real?p[at]:Vector3.zero,normal=real?n[at]:Vector3.up;Vector4 tangent=real&&t.Length>at?t[at]:new Vector4(1,0,0,1);Vector2 coord=real?uv[at]:Vector2.zero;
                foreach(float value in new[]{position.x,position.y,position.z,real&&joints.Length>at?joints[at].x:0,normal.x,normal.y,normal.z,0,tangent.x,tangent.y,tangent.z,tangent.w,coord.x,coord.y,0,0})w.Write(value);}
            return stream.ToArray();
        }
        static Texture2D Impostor(Mesh mesh,Texture2D atlas,Vector4 bounds,int view){
            var result=new Texture2D(128,128,TextureFormat.RGBA32,true,false);var target=RenderTexture.GetTemporary(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var saved=RenderTexture.active;var material=BakeMaterial();var cmd=new CommandBuffer();
            try{float angle=view*Mathf.PI/4;var centre=new Vector3(0,bounds.y,0);var eye=centre+new Vector3(Mathf.Sin(angle),.55f,Mathf.Cos(angle))*bounds.x*3;var rotation=Quaternion.LookRotation(centre-eye,Vector3.up);
                var matrix=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye,rotation,Vector3.one).inverse;var projection=GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-bounds.x*.5f,bounds.x*.5f,-bounds.x*.5f,bounds.x*.5f,.01f,bounds.x*8),true);
                material.SetMatrix("_BakeVP",projection*matrix);material.SetTexture("_MainTex",atlas);cmd.SetRenderTarget(target);cmd.ClearRenderTarget(true,true,Color.clear);cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,1);Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active=target;result.ReadPixels(new Rect(0,0,128,128),0,0);result.Apply(true);return result;
            }finally{cmd.Dispose();Object.DestroyImmediate(material);RenderTexture.active=saved;RenderTexture.ReleaseTemporary(target);}
        }
        static void Compress(Texture2D texture){EditorUtility.CompressTexture(texture,TextureFormat.BC7,TextureCompressionQuality.Normal);texture.Apply(false,false);}
        static Texture2DArray CloneArray(Texture2DArray source){
            // Native texture assets retain their serialized pixel payload in the editor.
            // Enable CPU access on an isolated copy; never alter the source catalogue.
            var result=Object.Instantiate(source);var serialized=new SerializedObject(result);serialized.FindProperty("m_IsReadable").boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
            try{if(result.GetPixelData<byte>(0,0).Length==0)throw new InvalidOperationException("Original texture array has no editable pixel payload.");return result;}catch{Object.DestroyImmediate(result);throw;}
        }
        static void SetSlice(Texture2DArray array,int slice,Texture2D texture){Compress(texture);var data=texture.GetRawTextureData<byte>();int offset=0;for(int mip=0;mip<texture.mipmapCount;mip++){array.SetPixelData(data,mip,slice,offset);int w=Mathf.Max(1,texture.width>>mip),h=Mathf.Max(1,texture.height>>mip);offset+=Mathf.Max(1,(w+3)/4)*Mathf.Max(1,(h+3)/4)*16;}}
        public static Result Compile(RtsModelPack pack){
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before baking model packs.");var original=Original;var c=Catalogue();var result=new Result();result.catalogue=result.Own(Object.Instantiate(original));result.catalogue.name=pack.name+" catalogue";
            try{
                var entries=original.buildings.ToDictionary(x=>x.id,x=>new RtsModelArt.Building{id=x.id,near=x.near,far=x.far,distant=x.distant,customLods=x.customLods,material=x.material,walls=x.walls,gateLeaf=x.gateLeaf});
                var ids=new HashSet<string>();foreach(var b in pack.buildings.Where(x=>x.enabled)){
                    if(!ids.Add(b.id)||!c.Buildings.TryGetValue(b.id,out var d))throw new InvalidOperationException("Unknown or duplicate building: "+b.id);
                    if(b.size.x>d.footprintWidth*8||b.size.z>d.footprintDepth*8)throw new InvalidOperationException(b.id+": model dimensions exceed its reserved construction footprint.");
                    var old=original.Find(b.id)??original.Find(d.mesh);using var shape=new Shape(3);var surface=new Surface{material=old?.material,original=true};var sources=new[]{b.lod0,b.lod1,b.lod2};
                    for(int lod=0;lod<3;lod++)Collect(shape,sources[lod],lod,lod==0?old?.near:old?.far,surface);
                    var near=result.Own(Geometry(shape,0,b.size,BuildingNearBudget));var far=result.Own(Geometry(shape,1,b.size,BuildingFarBudget));var proxy=result.Own(Geometry(shape,2,b.size,BuildingProxyBudget));near.name=b.id+" LOD0";far.name=b.id+" LOD1";proxy.name=b.id+" LOD2";
                    var maps=Atlas(shape,surface);for(int i=0;i<3;i++){maps[i].name=b.id+" map "+i;Compress(maps[i]);result.Own(maps[i]);}
                    var material=result.Own(new Material(Resources.Load<Shader>("MassiveArmy/ArtStructure")){name=b.id+" atlas material"});material.SetTexture("_Albedo",maps[0]);material.SetTexture("_Metallic",maps[1]);material.SetTexture("_Normal",maps[2]);
                    entries[b.id]=new RtsModelArt.Building{id=b.id,near=near,far=far,distant=proxy,material=material,customLods=true};
                }result.catalogue.buildings=entries.Values.ToArray();
                var units=pack.units.Where(x=>x.enabled).ToArray();if(units.Length>0){result.catalogue.unitNear=(TextAsset[])original.unitNear.Clone();result.catalogue.unitFar=(TextAsset[])original.unitFar.Clone();result.catalogue.unitBounds=(Vector4[])original.unitBounds.Clone();
                    var arrays=new[]{result.Own(CloneArray(original.albedo)),result.Own(CloneArray(original.metallic)),result.Own(CloneArray(original.normal)),result.Own(CloneArray(original.impostors))};ids.Clear();
                    foreach(var u in units){int index=Array.IndexOf(original.unitIds,u.id);if(!ids.Add(u.id)||index<0)throw new InvalidOperationException("Unknown or duplicate unit: "+u.id);
                        using var shape=new Shape(2);var oldNear=FromBank(original.unitNear[index]);var oldFar=FromBank(original.unitFar[index]);shape.temporary.Add(oldNear);shape.temporary.Add(oldFar);
                        var oldMaps=new[]{Slice(original.albedo,index),Slice(original.metallic,index),Slice(original.normal,index)};try{
                            var surface=new Surface{albedo=oldMaps[0],mask=oldMaps[1],normal=oldMaps[2],original=true};Collect(shape,u.lod0,0,oldNear,surface);Collect(shape,u.lod1,1,oldFar,surface);
                            var near=result.Own(Geometry(shape,0,u.size,ArtUnitRenderer.NearTriangles));var far=result.Own(Geometry(shape,1,u.size,ArtUnitRenderer.FarTriangles));
                            var combined=near.bounds;combined.Encapsulate(far.bounds);float radius=Mathf.Max(4,Mathf.Max(c.units[index].Traits.scale.x,c.units[index].Traits.scale.z)*5);if(combined.extents.magnitude>radius||combined.max.y>radius*1.5f)throw new InvalidOperationException(u.id+": model exceeds the existing GPU culling bound; reduce its dimensions.");
                            var maps=Atlas(shape,surface);try{
                                var bounds=near.bounds;bounds.Encapsulate(far.bounds);var packedBounds=new Vector4(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*1.5f,bounds.center.y,0,0);result.catalogue.unitBounds[index]=packedBounds;
                                result.catalogue.unitNear[index]=Bytes(result,Pack(near,ArtUnitRenderer.NearTriangles),u.id+" near");result.catalogue.unitFar[index]=Bytes(result,Pack(far,ArtUnitRenderer.FarTriangles),u.id+" far");
                                if(u.impostors.Length!=0&&(u.impostors.Length!=8||u.impostors.Any(x=>x==null)))throw new InvalidOperationException(u.id+": supply all eight impostor views, or leave the list empty for automatic baking.");
                                for(int view=0;view<8;view++){var image=u.impostors.Length==8?Read(u.impostors[view],128,false):Impostor(far,maps[0],packedBounds,view);try{SetSlice(arrays[3],index*8+view,image);}finally{Object.DestroyImmediate(image);}}
                                for(int channel=0;channel<3;channel++)SetSlice(arrays[channel],index,maps[channel]);
                            }finally{foreach(var map in maps)Object.DestroyImmediate(map);}
                        }finally{foreach(var map in oldMaps)Object.DestroyImmediate(map);}
                    }
                    foreach(var array in arrays)array.Apply(false,true);result.catalogue.albedo=arrays[0];result.catalogue.metallic=arrays[1];result.catalogue.normal=arrays[2];result.catalogue.impostors=arrays[3];
                }return result;
            }catch{result.Dispose();throw;}
        }
        static TextAsset Bytes(Result result,byte[] bytes,string name)=>result.Own(new TextAsset(bytes){name=name});
        public static void BakeAndUse(RtsModelPack pack){
            using var result=Compile(pack);string parent=Path.GetDirectoryName(AssetDatabase.GetAssetPath(pack)).Replace('\\','/');string folder=AssetDatabase.GenerateUniqueAssetPath(parent+"/"+pack.name+" Baked");AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
            Persist(result,folder);
            pack.bakedCatalogue=result.catalogue;pack.bakedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");EditorUtility.SetDirty(pack);Use(result.catalogue);AssetDatabase.SaveAssets();
        }
        internal static void Persist(Result result,string folder){
            for(int i=0;i<result.owned.Count;i++){
                var asset=result.owned[i];string path=folder+"/"+i.ToString("D3");
                if(asset is TextAsset bank){
                    path+=".bytes";File.WriteAllBytes(path,bank.bytes);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var imported=AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    for(int unit=0;unit<result.catalogue.unitNear.Length;unit++){if(result.catalogue.unitNear[unit]==bank)result.catalogue.unitNear[unit]=imported;if(result.catalogue.unitFar[unit]==bank)result.catalogue.unitFar[unit]=imported;}
                    result.owned[i]=imported;Object.DestroyImmediate(bank);
                }else AssetDatabase.CreateAsset(asset,path+(asset is Material?".mat":".asset"));
            }
            foreach(var asset in result.owned)EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
        }
        public static void Use(RtsModelArt catalogue){if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before switching model packs.");var selector=AssetDatabase.LoadAssetAtPath<RtsModelSelection>(SelectionPath);if(selector==null){selector=ScriptableObject.CreateInstance<RtsModelSelection>();AssetDatabase.CreateAsset(selector,SelectionPath);}Undo.RecordObject(selector,"Select RTS model pack");selector.catalogue=catalogue;EditorUtility.SetDirty(selector);AssetDatabase.SaveAssets();}
    }
}

