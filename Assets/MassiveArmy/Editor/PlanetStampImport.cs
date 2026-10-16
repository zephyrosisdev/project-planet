using System.IO;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools
{
    public static class PlanetStampImport
    {
        [InitializeOnLoadMethod]static void Install(){EditorApplication.delayCall+=Create;}
        [MenuItem("Massive Army/Planetary/Create Stamp Definitions")]
        public static void Create(){const string path="Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Library.asset";if(File.Exists(path))return;
            string[] names={"mountain01","mountain03","mountain05","mountain08","ridge01","ridge03","plateau01","plateau03","flat_mountain01","rolling_hills01","island01","canyon01"};
            var library=ScriptableObject.CreateInstance<PlanetStampLibrary>();library.definitions=new PlanetStampLibrary.StampDefinition[names.Length];
            for(int i=0;i<names.Length;i++)library.definitions[i]=new PlanetStampLibrary.StampDefinition{name=names[i],sourceAsset="Assets/TerrainSampleAssets/TerrainBrushes/Heightmaps/"+names[i]+".tif",heightGain=1,footprint=1.5f,heightOffset=0};
            AssetDatabase.CreateAsset(library,path);AssetDatabase.SaveAssets();
        }
        [MenuItem("Massive Army/Planetary/Bake Stamp Heightmaps")]
        public static void Bake(){
            Create();var library=AssetDatabase.LoadAssetAtPath<PlanetStampLibrary>("Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Library.asset");
            if(library.definitions.Length<1||library.definitions.Length>16)throw new System.InvalidOperationException("Use between one and sixteen stamp definitions.");
            const int size=PlanetStampLibrary.Resolution;var data=new ushort[size*size*library.Count];var old=RenderTexture.active;var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);var image=new Texture2D(size,size,TextureFormat.RFloat,false,true);
            try{for(int i=0;i<library.Count;i++){
                var source=AssetDatabase.LoadAssetAtPath<Texture2D>(library.definitions[i].sourceAsset);if(source==null)throw new System.InvalidOperationException("Missing source stamp: "+library.definitions[i].sourceAsset);
                Graphics.Blit(source,target);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();var pixels=image.GetPixelData<float>(0);
                for(int p=0;p<pixels.Length;p++)data[i*size*size+p]=(ushort)Mathf.RoundToInt(Mathf.Clamp01(pixels[p])*65535);
            }}finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(image);}
            var bytes=new byte[data.Length*2];System.Buffer.BlockCopy(data,0,bytes,0,bytes.Length);File.WriteAllBytes("Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Heights.bytes",bytes);var kernels=new System.Text.StringBuilder();foreach(var guid in AssetDatabase.FindAssets("t:ComputeShader",new[]{"Assets/MassiveArmy"})){string shaderPath=AssetDatabase.GUIDToAssetPath(guid);int count=System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(shaderPath),@"(?m)^#pragma kernel ").Count;kernels.AppendLine(Path.GetFileNameWithoutExtension(shaderPath)+":"+count);}File.WriteAllText("Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Kernels.txt",kernels.ToString());AssetDatabase.Refresh();Debug.Log("Baked "+library.Count+" deterministic 16-bit planet stamps.");
        }
    }
}
