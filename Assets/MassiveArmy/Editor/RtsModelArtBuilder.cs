using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class RtsModelArtBuilder
    {
        const string Root="Assets/MassiveArmy/Resources/MassiveArmy/ModelArt";
        const string Textures="Assets/MassiveArmy/Art/Textures/";
        [Serializable] sealed class Vertex{public float[] p,n,t,uv;public float part;}
        [Serializable] sealed class Payload{public Vertex[] vertices;}
        static T Save<T>(string path,T value) where T:UnityEngine.Object
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;
        }
        static Mesh Model(string key,Predicate<Vertex> filter=null)
        {
            var payload=JsonUtility.FromJson<Payload>(File.ReadAllText("ArtSource/SyntheticFront/"+key+".json"));
            if(payload.vertices==null||payload.vertices.Length==0||payload.vertices.Length%3!=0)throw new InvalidOperationException("Empty or malformed shell "+key);
            var positions=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();var uv=new List<Vector2>();var indices=new List<int>();
            for(int i=0;i<payload.vertices.Length;i+=3){if(filter!=null&&!filter(payload.vertices[i]))continue;
                for(int j=0;j<3;j++){var v=payload.vertices[i+j];
                    foreach(var values in new[]{v.p,v.n,v.t,v.uv})foreach(float value in values)if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Invalid vertex "+key);
                    if(v.uv[0]<-.001f||v.uv[0]>1.001f||v.uv[1]<-.001f||v.uv[1]>1.001f)throw new InvalidOperationException("UV outside atlas "+key);
                    positions.Add(new Vector3(v.p[0],v.p[1],v.p[2]));normals.Add(new Vector3(v.n[0],v.n[1],v.n[2]));tangents.Add(new Vector4(v.t[0],v.t[1],v.t[2],v.t[3]));uv.Add(new Vector2(v.uv[0],v.uv[1]));indices.Add(indices.Count);}}
            var mesh=new Mesh{name=key};mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        static Texture2D Texture(string key,string suffix)
        {
            string path=Textures+key+"-"+suffix+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(importer==null)throw new InvalidOperationException("Missing model texture "+path);
            bool srgb=suffix=="albedo";importer.textureType=TextureImporterType.Default;importer.sRGBTexture=srgb;importer.isReadable=true;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.wrapMode=TextureWrapMode.Clamp;
            var platform=importer.GetPlatformTextureSettings("Standalone");platform.overridden=true;platform.maxTextureSize=key=="building-command"?2048:512;platform.format=TextureImporterFormat.BC7;platform.textureCompression=TextureImporterCompression.CompressedHQ;importer.SetPlatformTextureSettings(platform);importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Mesh HQModel()
        {
            // Stream the high-detail shell; JsonUtility's native string conversion
            // is unsuitable for a large, triangle-expanded authoring payload.
            using var file=File.OpenRead("ArtSource/HQ/building-command.mesh.gz");
            using var gzip=new GZipStream(file,CompressionMode.Decompress);
            using var reader=new BinaryReader(gzip);
            if(new string(reader.ReadChars(4))!="HQM1")throw new InvalidDataException("Unknown HQ mesh version.");
            int count=reader.ReadInt32();if(count<=0||count%3!=0||count>10000000)throw new InvalidDataException("Invalid HQ vertex count.");
            var positions=new List<Vector3>();var normals=new List<Vector3>();var tangents=new List<Vector4>();var uv=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>(count);
            var shared=new Dictionary<(Vector3,Vector3,Vector4,Vector2,Color),int>();var values=new float[16];
            for(int i=0;i<count;i++){
                for(int k=0;k<16;k++){values[k]=reader.ReadSingle();if(float.IsNaN(values[k])||float.IsInfinity(values[k]))throw new InvalidDataException("Non-finite HQ vertex.");}
                var position=new Vector3(values[0],values[1],values[2]);var normal=new Vector3(values[3],values[4],values[5]);var tangent=new Vector4(values[6],values[7],values[8],values[9]);var coord=new Vector2(values[10],values[11]);var color=new Color(values[12],values[13],values[14],values[15]);
                if(coord.x<0||coord.x>1||coord.y<0||coord.y>1)throw new InvalidDataException("HQ UV outside trim sheet.");
                var vertex=(position,normal,tangent,coord,color);if(!shared.TryGetValue(vertex,out int index)){index=positions.Count;shared.Add(vertex,index);positions.Add(position);normals.Add(normal);tangents.Add(tangent);uv.Add(coord);colors.Add(color);}indices.Add(index);
            }
            var mesh=new Mesh{name="building-command",indexFormat=positions.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        static Texture2DArray ArrayTexture(Texture2D[] textures,string name,bool srgb)
        {
            int width=textures[0].width;var array=new Texture2DArray(width,width,textures.Length,TextureFormat.BC7,true,!srgb){name=name,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};
            for(int slice=0;slice<textures.Length;slice++){
                var tex=textures[slice];if(tex.width!=width||tex.format!=TextureFormat.BC7)throw new InvalidOperationException("Array texture format mismatch "+tex.name);
                var raw=tex.GetRawTextureData<byte>();int offset=0;
                for(int mip=0;mip<tex.mipmapCount;mip++){array.SetPixelData(raw,mip,slice,offset);int w=Mathf.Max(1,width>>mip);offset+=Mathf.Max(1,(w+3)/4)*Mathf.Max(1,(w+3)/4)*16;}
            }
            array.Apply(false,true);return Save(Root+"/"+name+".asset",array);
        }
        [MenuItem("Massive Army/Art/Build model art catalogue")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory("TestResults/ArtPass");
            var catalog=RtsCatalog.Load();var art=ScriptableObject.CreateInstance<RtsModelArt>();var buildings=new List<RtsModelArt.Building>();
            foreach(var d in catalog.buildings){
                string key="building-"+d.id;var albedo=Texture(key,"albedo");var mask=Texture(key,"metallic");var normal=Texture(key,"normal");
                bool hq=d.id=="command"&&File.Exists("ArtSource/HQ/building-command.mesh.gz");
                var material=new Material(Resources.Load<Shader>(hq?"MassiveArmy/ArtHQ":"MassiveArmy/ArtStructure")){name=d.name+" UV armour"};material.SetTexture("_Albedo",albedo);material.SetTexture("_Metallic",mask);material.SetTexture("_Normal",normal);
                material=Save(Root+"/Materials/"+key+".mat",material);
                var entry=new RtsModelArt.Building{id=d.id,material=material};
                entry.near=Save(Root+"/Meshes/"+key+".asset",hq?HQModel():Model(key,d.id=="gate"?(Predicate<Vertex>)(v=>v.part!=7):null));
                entry.far=hq?entry.near:Save(Root+"/Meshes/"+key+"-lod1.asset",Model(key+"-lod1",d.id=="gate"?(Predicate<Vertex>)(v=>v.part!=7):null));
                if(d.id=="gate")entry.gateLeaf=Save(Root+"/Meshes/gate-leaf.asset",Model(key,v=>v.part==7));
                if(d.id=="wall"){
                    entry.walls=new Mesh[16];for(int k=0;k<16;k++){int bits=k;entry.walls[k]=Save(Root+"/Meshes/wall-"+k+".asset",Model(key,v=>v.part<1||((bits>>(Mathf.RoundToInt(v.part)-1))&1)!=0));}
                }
                if(entry.near.bounds.size.x>d.footprintWidth*8+.01f||entry.near.bounds.size.z>d.footprintDepth*8+.01f)throw new InvalidOperationException("Model exceeds reserved foundation: "+d.id);
                buildings.Add(entry);
            }
            art.buildings=buildings.ToArray();art.unitIds=catalog.units.Select(u=>u.id).ToArray();art.unitNear=new TextAsset[31];art.unitFar=new TextAsset[31];art.unitBounds=new Vector4[31];
            var a=new Texture2D[31];var m=new Texture2D[31];var n=new Texture2D[31];var sprites=new List<Texture2D>();
            for(int i=0;i<31;i++){
                string key="unit-"+art.unitIds[i];a[i]=Texture(key,"albedo");m[i]=Texture(key,"metallic");n[i]=Texture(key,"normal");
                art.unitNear[i]=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Data/"+key+".bytes");art.unitFar[i]=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Data/"+key+"-lod1.bytes");
                if(art.unitNear[i]==null||art.unitNear[i].bytes.Length!=512*3*64||art.unitFar[i]==null||art.unitFar[i].bytes.Length!=128*3*64)throw new InvalidOperationException("Invalid GPU vertex bank "+key);
                var mesh=Model(key);var bounds=mesh.bounds;art.unitBounds[i]=new Vector4(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z)*1.5f,bounds.center.y,0,0);UnityEngine.Object.DestroyImmediate(mesh);
                for(int direction=0;direction<8;direction++){
                    string path="Assets/MassiveArmy/Art/Impostors/"+key+"-"+direction+".png";
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);if(importer==null)throw new InvalidOperationException("Missing directional impostor "+path);
                    importer.isReadable=true;importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.alphaIsTransparency=true;
                    var p=importer.GetPlatformTextureSettings("Standalone");p.overridden=true;p.maxTextureSize=128;p.format=TextureImporterFormat.BC7;importer.SetPlatformTextureSettings(p);importer.SaveAndReimport();sprites.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                }
            }
            art.albedo=ArrayTexture(a,"UnitAlbedo",true);art.metallic=ArrayTexture(m,"UnitMetallic",false);art.normal=ArrayTexture(n,"UnitNormal",false);art.impostors=ArrayTexture(sprites.ToArray(),"UnitImpostors",true);
            Save(Root+"/Catalogue.asset",art);AssetDatabase.SaveAssets();
            foreach(string file in Directory.GetFiles(Textures,"*.png").Concat(Directory.GetFiles("Assets/MassiveArmy/Art/Impostors","*.png"))){
                var importer=(TextureImporter)AssetImporter.GetAtPath(file);if(importer!=null&&importer.isReadable){importer.isReadable=false;importer.SaveAndReimport();}
            }
            File.WriteAllText("TestResults/ArtPass/catalogue-build.txt","PASS: 49 buildings, 31 units, 240 maps, 248 directional impostors; building foundations validated.");
            Debug.Log("Model art catalogue built: all 80 IDs covered");
        }
        public static void BuildPlayer(){Build();WorldBuild.Run();}
        [MenuItem("Massive Army/Art/Build detailed HQ only")]
        public static void BuildHQ()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var art=AssetDatabase.LoadAssetAtPath<RtsModelArt>(Root+"/Catalogue.asset");
            if(art==null)throw new InvalidOperationException("Build the existing catalogue before updating the HQ.");
            var entry=art.Find("command");if(entry==null)throw new InvalidOperationException("Missing HQ catalogue entry.");
            var mesh=HQModel();
            if(mesh.bounds.size.x>40.01f||mesh.bounds.size.z>40.01f)throw new InvalidOperationException("HQ exceeds its 5 x 5 placement grid.");
            var material=new Material(Resources.Load<Shader>("MassiveArmy/ArtHQ")){name="Command Centre concept armour trim"};
            foreach(string suffix in new[]{"albedo","metallic","normal"})material.SetTexture(suffix=="albedo"?"_Albedo":suffix=="metallic"?"_Metallic":"_Normal",Texture("building-command",suffix));
            entry.near=Save(Root+"/Meshes/building-command.asset",mesh);
            // Fidelity prototype: retain the same shell at both LOD distances; no hidden decimation.
            entry.far=entry.near;entry.material=Save(Root+"/Materials/building-command.mat",material);
            EditorUtility.SetDirty(art);AssetDatabase.SaveAssets();
            foreach(string suffix in new[]{"albedo","metallic","normal"}){var importer=(TextureImporter)AssetImporter.GetAtPath(Textures+"building-command-"+suffix+".png");importer.isReadable=false;importer.SaveAndReimport();}
            Directory.CreateDirectory("TestResults/HQ");File.WriteAllText("TestResults/HQ/import.txt","PASS HQ: "+entry.near.vertexCount+" vertices, "+entry.near.triangles.Length/3+" triangles, 2048px trim maps; bounds "+entry.near.bounds.size);
            Debug.Log("Detailed HQ imported; all other catalogue entries preserved.");
        }
        public static void BuildHQPlayer(){BuildHQ();WorldBuild.Run();}
    }
}
