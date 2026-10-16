using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    /// <summary>Offline texture conditioning and GPU packing; never runs during a battle.</summary>
    public static class TerrainArtBuilder
    {
        public const string ArtFolder = "Assets/MassiveArmy/Art/Terrain";
        public const string ResourceFolder = "Assets/MassiveArmy/Resources/MassiveArmy/TerrainArt";
        public const string SourceFolder = "Tools/TerrainSources";
        public const int Size = 1024;
        // Physical tile width / relief amplitude in metres. Normals derive from these dimensions.
        static readonly float[] Tiles = {4,4,4,4,6,6,4,4,4};
        static readonly float[] Relief = {.055f,.045f,.045f,.055f,.16f,.06f,.07f,.10f,.045f};
        static readonly float[] Roughness = {.94f,.96f,.97f,.92f,.86f,.78f,.66f,.92f,.94f};

        [MenuItem("Massive Army/Art/Rebuild Terrain Textures")]
        public static void Rebuild()
        {
            Directory.CreateDirectory(ArtFolder);
            Directory.CreateDirectory(ResourceFolder);
            var format = TextureFormat.BC7;
            if (!SystemInfo.SupportsTextureFormat(format))
                throw new NotSupportedException("Terrain texture build requires BC7 support (DX11/DX12 desktop GPU).");
            int layers = TerrainSurfaceLibrary.LayerNames.Length;
            var albedo = new Texture2DArray(Size,Size,layers,format,true,false)
                {name="Terrain albedo (sRGB)",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            var data = new Texture2DArray(Size,Size,layers,format,true,true)
                {name="Terrain normal XY height roughness (linear)",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            var parameters = new Vector4[layers];
            try
            {
                for(int layer=0;layer<layers;layer++)
                {
                    string name=TerrainSurfaceLibrary.LayerNames[layer];
                    Debug.Log("Building terrain texture: "+name);
                    var colours=ReadSource(name+"_Albedo.png");
                    var heightPixels=ReadSource(name+"_Height.png");
                    MakePeriodic(colours);MakePeriodic(heightPixels);
                    var heights=new float[Size*Size];
                    for(int i=0;i<heights.Length;i++)heights[i]=heightPixels[i].grayscale;
                    // Height normalization is shared across the full image, never per tile or mip.
                    float min=1,max=0;foreach(float h in heights){min=Mathf.Min(min,h);max=Mathf.Max(max,h);}
                    if(max-min<.08f)throw new InvalidOperationException(name+": insufficient height detail.");
                    for(int i=0;i<heights.Length;i++)heights[i]=Mathf.Lerp(.12f,.88f,(heights[i]-min)/(max-min));
                    var normals=new Color[heights.Length];var packed=new Color[heights.Length];
                    // A small wrap-aware filter suppresses generation/8-bit quantization noise.
                    heights=SmoothHeight(heights);
                    float scale=Relief[layer]*Size/Tiles[layer];
                    for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                    {
                        int i=x+y*Size;
                        float dx=(Height(heights,x+1,y)-Height(heights,x-1,y))*.5f*scale;
                        float dy=(Height(heights,x,y+1)-Height(heights,x,y-1))*.5f*scale;
                        var n=new Vector3(-dx,-dy,1).normalized;
                        normals[i]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);
                        float h=heights[i];heightPixels[i]=new Color(h,h,h,1);
                        packed[i]=new Color(normals[i].r,normals[i].g,h,Roughness[layer]);
                    }
                    WriteMap(name+"_Albedo",colours,false,false);
                    WriteMap(name+"_Normal",normals,true,true);
                    WriteMap(name+"_Height",heightPixels,true,false);
                    PackLayer(albedo,layer,colours,false,false);
                    PackLayer(data,layer,packed,true,true);
                    parameters[layer]=new Vector4(Tiles[layer],1,Relief[layer],Roughness[layer]);
                }
                // CopyTexture fills GPU storage. Do not Apply here: that would replace it with
                // the array's uninitialized CPU copy. Asset serialization preserves GPU content.
                SaveAsset(albedo,ResourceFolder+"/AlbedoArray.asset");
                SaveAsset(data,ResourceFolder+"/SurfaceDataArray.asset");
                string path=ResourceFolder+"/SurfaceLibrary.asset";
                var library=AssetDatabase.LoadAssetAtPath<TerrainSurfaceLibrary>(path);
                if(library==null){library=ScriptableObject.CreateInstance<TerrainSurfaceLibrary>();AssetDatabase.CreateAsset(library,path);}
                library.albedo=AssetDatabase.LoadAssetAtPath<Texture2DArray>(ResourceFolder+"/AlbedoArray.asset");
                library.surfaceData=AssetDatabase.LoadAssetAtPath<Texture2DArray>(ResourceFolder+"/SurfaceDataArray.asset");
                library.layerParameters=parameters;library.NotifySettingsChanged();EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();AssetDatabase.Refresh();
                BuildPreview();
                Debug.Log("Terrain art ready: 27 1K maps, nine layers, two BC7 arrays with mipmaps.");
            }
            finally
            {
                if(!AssetDatabase.Contains(albedo))UnityEngine.Object.DestroyImmediate(albedo);
                if(!AssetDatabase.Contains(data))UnityEngine.Object.DestroyImmediate(data);
            }
        }

        static Color[] ReadSource(string filename)
        {
            string path=Path.Combine(SourceFolder,filename);
            if(!File.Exists(path))throw new FileNotFoundException("Missing generated terrain source",path);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                if(!texture.LoadImage(File.ReadAllBytes(path)))throw new InvalidOperationException("Cannot decode "+path);
                if(texture.width!=texture.height||texture.width<Size)throw new InvalidOperationException(path+": expected square source at least "+Size+" pixels.");
                if(texture.width==Size)return texture.GetPixels();
                // The image tool controls native output dimensions. Downsample to a power of two
                // for complete mip chains, preserving the originals alongside generation prompts.
                var pixels=new Color[Size*Size];
                for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)pixels[x+y*Size]=texture.GetPixelBilinear((x+.5f)/Size,(y+.5f)/Size);
                return pixels;
            }
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }

        // Blend opposing edge bands symmetrically, with zero change at the inner edge.
        // Albedo and height receive exactly the same transform so feature registration survives.
        static void MakePeriodic(Color[] pixels)
        {
            const int band=64;
            for(int y=0;y<Size;y++)for(int k=0;k<band;k++)
            {
                float mix=.5f*(1-Mathf.SmoothStep(0,1,(float)k/(band-1)));
                int a=k+y*Size,b=Size-1-k+y*Size;Color p=pixels[a],q=pixels[b];
                pixels[a]=Color.Lerp(p,q,mix);pixels[b]=Color.Lerp(q,p,mix);
            }
            for(int x=0;x<Size;x++)for(int k=0;k<band;k++)
            {
                float mix=.5f*(1-Mathf.SmoothStep(0,1,(float)k/(band-1)));
                int a=x+k*Size,b=x+(Size-1-k)*Size;Color p=pixels[a],q=pixels[b];
                pixels[a]=Color.Lerp(p,q,mix);pixels[b]=Color.Lerp(q,p,mix);
            }
        }
        static float Height(float[] pixels,int x,int y)=>pixels[((x+Size)%Size)+((y+Size)%Size)*Size];
        static float[] SmoothHeight(float[] pixels)
        {
            var temp=new float[pixels.Length];var result=new float[pixels.Length];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)temp[x+y*Size]=(Height(pixels,x-1,y)+2*Height(pixels,x,y)+Height(pixels,x+1,y))*.25f;
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)result[x+y*Size]=(Height(temp,x,y-1)+2*Height(temp,x,y)+Height(temp,x,y+1))*.25f;
            return result;
        }
        static void WriteMap(string name,Color[] pixels,bool linear,bool normal)
        {
            string path=ArtFolder+"/"+name+".png";
            var t=new Texture2D(Size,Size,TextureFormat.RGB24,false,linear);
            try{t.SetPixels(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());}
            finally{UnityEngine.Object.DestroyImmediate(t);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!linear;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;
            importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.maxTextureSize=Size;
            importer.isReadable=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        static void PackLayer(Texture2DArray array,int layer,Color[] pixels,bool linear,bool surface)
        {
            var t=new Texture2D(Size,Size,TextureFormat.RGBA32,true,linear);
            try
            {
                t.SetPixels(pixels);t.Apply(true,false);
                if(surface)
                {
                    // Renormalize averaged normals at every mip before compression.
                    for(int mip=1;mip<t.mipmapCount;mip++)
                    {
                        var colors=t.GetPixels(mip);
                        for(int i=0;i<colors.Length;i++)
                        {
                            float x=colors[i].r*2-1,y=colors[i].g*2-1;
                            var n=new Vector3(x,y,Mathf.Sqrt(Mathf.Max(1-x*x-y*y,.001f))).normalized;
                            colors[i].r=n.x*.5f+.5f;colors[i].g=n.y*.5f+.5f;
                        }
                        t.SetPixels(colors,mip);
                    }
                    t.Apply(false,false);
                }
                EditorUtility.CompressTexture(t,TextureFormat.BC7,TextureCompressionQuality.Best);
                for(int mip=0;mip<t.mipmapCount;mip++)Graphics.CopyTexture(t,0,mip,array,layer,mip);
            }
            finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        static void SaveAsset(UnityEngine.Object asset,string path)
        {
            var existing=AssetDatabase.LoadMainAssetAtPath(path);
            if(existing==null)AssetDatabase.CreateAsset(asset,path);
            else{EditorUtility.CopySerialized(asset,existing);EditorUtility.SetDirty(existing);}
        }
        public static void BuildAndValidate()
        {
            try{Rebuild();TerrainArtValidation.Run();}
            catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);else throw;}
        }
        static void BuildPreview()
        {
            const int tile=320;var preview=new Texture2D(tile*3,tile*3,TextureFormat.RGB24,false);
            try
            {
                for(int layer=0;layer<9;layer++)
                {
                    var source=new Texture2D(2,2,TextureFormat.RGB24,false);
                    try
                    {
                        source.LoadImage(File.ReadAllBytes(ArtFolder+"/"+TerrainSurfaceLibrary.LayerNames[layer]+"_Albedo.png"));
                        for(int y=0;y<tile;y++)for(int x=0;x<tile;x++)preview.SetPixel(layer%3*tile+x,(2-layer/3)*tile+y,source.GetPixelBilinear((x+.5f)/tile,(y+.5f)/tile));
                    }
                    finally{UnityEngine.Object.DestroyImmediate(source);}
                }
                preview.Apply();Directory.CreateDirectory("TestResults");File.WriteAllBytes("TestResults/terrain-materials.png",preview.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(preview);}
        }
    }
}
