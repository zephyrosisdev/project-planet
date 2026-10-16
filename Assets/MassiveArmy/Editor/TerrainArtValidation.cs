using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    public static class TerrainArtValidation
    {
        [Serializable] sealed class Report
        {
            public string status="PASS",error,device,timestamp;
            public int resolution,layers,maps;
            public float heightBlendPixelDifference,normalPixelDifference;
            public string[] checks;
        }
        [MenuItem("Massive Army/Art/Validate Terrain Textures")]
        public static void Run()
        {
            var checks=new List<string>();
            var report=new Report{device=SystemInfo.graphicsDeviceName,timestamp=DateTime.UtcNow.ToString("O"),resolution=TerrainArtBuilder.Size,layers=9,maps=27};
            void Require(bool ok,string check){if(!ok)throw new InvalidOperationException(check);checks.Add(check);}
            try
            {
                var library=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");
                Require(library!=null,"Surface library loads through the battlefield Resources path");
                Require(library.albedo.depth==9&&library.surfaceData.depth==9,"Nine slices match all eight biome channels and the road");
                Require(library.albedo.mipmapCount==11&&library.surfaceData.mipmapCount==11,"Full 1K mip chains in both arrays");
                Require(library.albedo.format==TextureFormat.BC7&&library.surfaceData.format==TextureFormat.BC7,"GPU arrays use BC7 compression");
                foreach(string name in TerrainSurfaceLibrary.LayerNames)
                {
                    foreach(string map in new[]{"Albedo","Normal","Height"})
                    {
                        string path=TerrainArtBuilder.ArtFolder+"/"+name+"_"+map+".png";
                        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                        Require(texture!=null&&texture.width==1024&&texture.height==1024,name+" "+map+": 1K asset imported");
                        Require(importer.wrapMode==TextureWrapMode.Repeat&&importer.mipmapEnabled&&importer.anisoLevel==8,name+" "+map+": repeat, mipmaps, anisotropic filtering");
                        Require(importer.sRGBTexture==(map=="Albedo")&&(! (map=="Normal")||importer.textureType==TextureImporterType.NormalMap),name+" "+map+": correct colour space and texture type");
                        if(map!="Normal")ValidateEdges(path,Require);
                    }
                }
                var shader=Resources.Load<Shader>("MassiveArmy/Terrain");
                Require(shader!=null&&shader.isSupported,"Terrain shader supported on the active graphics device");
                var material=new Material(shader);library.Apply(material);
                // The eight-metre fixture needs the same periodic-world metadata as
                // runtime terrain; zero world size produces undefined sampling.
                material.SetFloat("_WorldSize",8);
                var mesh=Plane();var overview=new Texture2D(768,768,TextureFormat.RGB24,false);
                Texture2D a=Solid(Color.clear),b=Solid(Color.clear),s=Solid(Color.clear);
                try
                {
                    material.SetFloat("_MacroVariation",0);
                    material.SetTexture("_LayersA",a);material.SetTexture("_LayersB",b);material.SetTexture("_Splat",s);
                    var means=new List<float>();
                    for(int layer=0;layer<9;layer++)
                    {
                        SetLayer(a,b,s,layer);
                        var image=Render(mesh,material,256);
                        try
                        {
                            var pixels=image.GetPixels();ValidatePixels(pixels,Require,TerrainSurfaceLibrary.LayerNames[layer]);
                            means.Add(pixels.Average(p=>p.grayscale));
                            overview.SetPixels(layer%3*256,(2-layer/3)*256,256,256,pixels);
                        }
                        finally{UnityEngine.Object.DestroyImmediate(image);}
                    }
                    Require(means.Max()-means.Min()>.15f,"Saved texture array slices retain distinct surface colours on the GPU");
                    overview.Apply();File.WriteAllBytes("TestResults/terrain-rendered-surfaces.png",overview.EncodeToPNG());
                    Set(a,new Color(.5f,.5f,0,0));Set(b,Color.clear);Set(s,Color.clear);
                    material.SetFloat("_HeightBlendStrength",0);var flat=Render(mesh,material,256);
                    material.SetFloat("_HeightBlendStrength",1);var blended=Render(mesh,material,256);
                    report.heightBlendPixelDifference=Difference(flat,blended);
                    Require(report.heightBlendPixelDifference>.001f,"Height maps measurably change grass/sand transitions on the GPU");
                    File.WriteAllBytes("TestResults/terrain-height-blend.png",blended.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(flat);UnityEngine.Object.DestroyImmediate(blended);
                    SetLayer(a,b,s,4);material.SetFloat("_NormalStrength",0);flat=Render(mesh,material,256);
                    material.SetFloat("_NormalStrength",1);blended=Render(mesh,material,256);
                    report.normalPixelDifference=Difference(flat,blended);
                    Require(report.normalPixelDifference>.0001f,"Generated normal maps measurably change surface lighting on the GPU");
                    UnityEngine.Object.DestroyImmediate(flat);UnityEngine.Object.DestroyImmediate(blended);
                    SetLayer(a,b,s,8);flat=Render(mesh,material,256);Set(a,Color.red);Set(b,Color.blue);blended=Render(mesh,material,256);
                    Require(Difference(flat,blended)<.0001f,"Road interiors fully replace underlying biome surfaces");
                    UnityEngine.Object.DestroyImmediate(flat);UnityEngine.Object.DestroyImmediate(blended);
                    SetLayer(a,b,s,4);var cliff=Render(mesh,material,256,Matrix4x4.Rotate(Quaternion.Euler(0,0,65)));
                    ValidatePixels(cliff.GetPixels(),Require,"Steep rock projection");UnityEngine.Object.DestroyImmediate(cliff);
                    Require(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"Both terrain shader passes compile without errors");
                    ValidateTransitions(material,Require);
                    RenderWorld(material,Require);
                }
                finally
                {
                    foreach(var obj in new UnityEngine.Object[]{material,mesh,overview,a,b,s})UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            report.checks=checks.ToArray();Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/terrain-art-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Terrain art validation: "+report.status+" ("+checks.Count+" checks)");
            if(Application.isBatchMode)EditorApplication.Exit(report.status=="PASS"?0:1);
        }
        static void ValidateEdges(string path,Action<bool,string> require)
        {
            var t=new Texture2D(2,2,TextureFormat.RGB24,false,true);
            try
            {
                t.LoadImage(File.ReadAllBytes(path));var p=t.GetPixels();float error=0;
                for(int i=0;i<t.width;i++)
                {
                    error+=Delta(p[i*t.width],p[i*t.width+t.width-1]);
                    error+=Delta(p[i],p[i+(t.height-1)*t.width]);
                }
                require(error/(t.width*2)<.006f,Path.GetFileName(path)+": opposite edges match for seamless repeat");
            }
            finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        static float Delta(Color a,Color b)=>(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/3;
        static float Difference(Texture2D a,Texture2D b)
        {
            var x=a.GetPixels();var y=b.GetPixels();float difference=0;
            for(int i=0;i<x.Length;i++)difference+=Delta(x[i],y[i]);return difference/x.Length;
        }
        static void ValidatePixels(Color[] pixels,Action<bool,string> require,string label)
        {
            require(pixels.All(c=>!float.IsNaN(c.r)&&!float.IsInfinity(c.r)),label+": finite rendered pixels");
            require(pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.2f)<pixels.Length*.01f,label+": no shader-error magenta");
            require(pixels.Average(c=>c.grayscale)>.015f,label+": visible textured output");
        }
        static Texture2D Solid(Color colour){var t=new Texture2D(1,1,TextureFormat.RGBA32,false,true);Set(t,colour);return t;}
        static void Set(Texture2D t,Color colour){t.SetPixel(0,0,colour);t.Apply();}
        static void SetLayer(Texture2D a,Texture2D b,Texture2D s,int layer)
        {
            Color x=Color.clear,y=Color.clear,z=Color.clear;
            if(layer<4)x[layer]=1;else if(layer<8)y[layer-4]=1;else z.b=1;
            Set(a,x);Set(b,y);Set(s,z);
        }
        static Mesh Plane()
        {
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-4,0,-4),new Vector3(-4,0,4),new Vector3(4,0,4),new Vector3(4,0,-4)};
            mesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();return mesh;
        }
        static Texture2D Render(Mesh mesh,Material material,int size,Matrix4x4? transform=null,float extent=4,Vector3? cameraPosition=null,Quaternion? rotation=null)
        {
            var target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;var cmd=new CommandBuffer();
            Vector3 position=cameraPosition??new Vector3(0,10,0);Quaternion orientation=rotation??Quaternion.Euler(90,0,0);
            try
            {
                cmd.SetRenderTarget(target);cmd.ClearRenderTarget(true,true,new Color(.04f,.05f,.07f));
                Matrix4x4 view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(position,orientation,Vector3.one).inverse;
                cmd.SetViewProjectionMatrices(view,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-extent,extent,-extent,extent,.1f,10000),true));
                cmd.SetGlobalVector("_WorldSpaceCameraPos",position);cmd.DrawMesh(mesh,transform??Matrix4x4.identity,material,0,material.FindPass("TerrainValidation"));
                Graphics.ExecuteCommandBuffer(cmd);RenderTexture.active=target;
                var image=new Texture2D(size,size,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();return image;
            }
            finally{cmd.Dispose();RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        static void RenderWorld(Material material,Action<bool,string> require)
        {
            var map=new WorldMap(256,2048,73129);var a=new Color[map.Height.Length];var b=new Color[a.Length];var s=new Color[a.Length];
            for(int i=0;i<a.Length;i++){map.SurfaceLayers(i,out a[i],out b[i]);s[i]=new Color(0,0,map.Road[i],map.Water[i]>0?1:0);}
            Texture2D Mask(Color[] pixels,bool mips){var t=new Texture2D(256,256,TextureFormat.RGBA32,mips,true){wrapMode=TextureWrapMode.Clamp,filterMode=mips?FilterMode.Trilinear:FilterMode.Bilinear};t.SetPixels(pixels);t.Apply();return t;}
            var ta=Mask(a,true);var tb=Mask(b,true);var ts=Mask(s,false);var mesh=new Mesh{indexFormat=IndexFormat.UInt32};
            try
            {
                const int n=128;var vertices=new Vector3[(n+1)*(n+1)];var uv=new Vector2[vertices.Length];var indices=new int[n*n*6];int at=0;
                for(int y=0;y<=n;y++)for(int x=0;x<=n;x++){int i=x+y*(n+1);uv[i]=new Vector2((float)x/n,(float)y/n);Vector2 p=(uv[i]-Vector2.one*.5f)*2048;vertices[i]=new Vector3(p.x,map.SampleHeight(p),p.y);}
                for(int y=0;y<n;y++)for(int x=0;x<n;x++){int i=x+y*(n+1);indices[at++]=i;indices[at++]=i+n+1;indices[at++]=i+1;indices[at++]=i+1;indices[at++]=i+n+1;indices[at++]=i+n+2;}
                mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();
                material.SetTexture("_LayersA",ta);material.SetTexture("_LayersB",tb);material.SetTexture("_Splat",ts);material.SetFloat("_MacroVariation",.12f);
                material.SetFloat("_GeographyGuided",1);
                material.SetFloat("_WorldSize",map.Size);material.SetFloat("_BlendSeed",map.Seed % 100003);
                var image=Render(mesh,material,1024,null,1150,new Vector3(0,2000,-1500),Quaternion.Euler(58,0,0));
                try{ValidatePixels(image.GetPixels(),require,"Seed 73129 textured terrain");File.WriteAllBytes("TestResults/terrain-world.png",image.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(image);}
            }
            finally{foreach(var obj in new UnityEngine.Object[]{ta,tb,ts,mesh})UnityEngine.Object.DestroyImmediate(obj);}
        }
        static void ValidateTransitions(Material material,Action<bool,string> require)
        {
            const int n=128;
            var pixels=new Color[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++)pixels[x+y*n]=x<n/2?new Color(1,0,0,0):new Color(0,1,0,0);
            var mask=new Texture2D(n,n,TextureFormat.RGBA32,true,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};mask.SetPixels(pixels);mask.Apply();
            var b=Solid(Color.clear);var s=Solid(Color.clear);var mesh=Plane();var images=new List<Texture2D>();
            Vector4 blend=material.GetVector("_BiomeBlend"),detail=material.GetVector("_BiomeDetail");
            Texture2D Draw(){var image=Render(mesh,material,256,Matrix4x4.Scale(Vector3.one*16),64,new Vector3(0,200,0));images.Add(image);return image;}
            try
            {
                material.SetTexture("_LayersA",mask);material.SetTexture("_LayersB",b);material.SetTexture("_Splat",s);material.SetFloat("_WorldSize",128);material.SetFloat("_BlendSeed",73129);
                material.SetFloat("_BlendDebug",1);material.SetVector("_BiomeBlend",new Vector4(0,0,96,0));var original=Draw();
                material.SetVector("_BiomeBlend",new Vector4(32,0,96,0));var smooth=Draw();
                int Mixed(Texture2D image)=>image.GetPixels().Count(c=>c.r>.08f&&c.r<.92f&&c.g>.08f);
                require(Mixed(smooth)>Mixed(original)*4,"Prefiltered splats broaden a categorical biome border");
                material.SetVector("_BiomeBlend",new Vector4(32,20,64,.8f));var noisy=Draw();
                require(Difference(smooth,noisy)>.01f,"Warp and material patches break up a straight transition on the GPU");
                require(Difference(noisy,Draw())<.00001f,"Same seed and settings reproduce identical transitions");
                material.SetFloat("_BlendSeed",91827);require(Difference(noisy,Draw())>.005f,"A different world seed produces a different transition pattern");
                require(noisy.GetPixels().All(c=>c.b<.001f),"Transition noise never introduces an absent rock layer");
                require(noisy.GetPixels().All(c=>Mathf.Abs(c.linear.r+c.linear.g-1)<.02f),"Noisy material weights stay normalized throughout the transition");
                Set(s,new Color(0,0,0,1));var submerged=Draw();require(Difference(submerged,original)<.01f,"Submerged beds retain the local material assignments");Set(s,Color.clear);
                material.SetFloat("_BlendDebug",0);material.SetFloat("_BlendSeed",73129);
                material.SetVector("_BiomeBlend",new Vector4(0,0,96,0));var before=Draw();
                material.SetVector("_BiomeBlend",new Vector4(32,20,64,.8f));var after=Draw();
                require(Difference(before,after)>.01f,"Natural masks visibly affect textured height-blended output");
                File.WriteAllBytes("TestResults/terrain-transition-before.png",before.EncodeToPNG());File.WriteAllBytes("TestResults/terrain-transition-after.png",after.EncodeToPNG());
                File.WriteAllBytes("TestResults/terrain-transition-masks.png",noisy.EncodeToPNG());
                Set(s,new Color(0,0,.5f,0));material.SetVector("_BiomeDetail",new Vector4(12,0,0,0));var smoothRoad=Draw();
                material.SetVector("_BiomeDetail",new Vector4(12,.7f,0,0));require(Difference(smoothRoad,Draw())>.001f,"Road shoulder noise visibly breaks up partial road coverage");
            }
            finally
            {
                material.SetFloat("_BlendDebug",0);material.SetVector("_BiomeBlend",blend);material.SetVector("_BiomeDetail",detail);
                foreach(var image in images)UnityEngine.Object.DestroyImmediate(image);
                foreach(var obj in new UnityEngine.Object[]{mask,b,s,mesh})UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
