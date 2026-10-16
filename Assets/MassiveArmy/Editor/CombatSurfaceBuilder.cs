using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class CombatSurfaceBuilder
    {
        [MenuItem("Massive Army/Art/Build Crater Height Map")]
        public static void Build()
        {
            const string path="Assets/MassiveArmy/Resources/MassiveArmy/CombatArt/NukeCraterHeight.asset";
            const int size=256;
            var pixels=new float[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float radius=new Vector2((x+.5f)/size-.5f,(y+.5f)/size-.5f).magnitude;
                float bowl=radius<.32f?-Mathf.Pow(1-radius*radius/(.32f*.32f),2):0;
                float rim=radius>.30f&&radius<.48f?.85f*Mathf.Pow(Mathf.Sin((radius-.30f)/.18f*Mathf.PI),2):0;
                pixels[x+y*size]=bowl+rim;
            }
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null){texture=new Texture2D(size,size,TextureFormat.RFloat,false,true);AssetDatabase.CreateAsset(texture,path);}
            texture.name="Signed crater bowl and rim displacement";texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            texture.SetPixelData(pixels,0);texture.Apply(false,false);EditorUtility.SetDirty(texture);AssetDatabase.SaveAssets();
            BuildLibrary("Assets/MassiveArmy/Resources/MassiveArmy/CombatArt/",new[]{"NukeCrater","ZombieSplatter","HumanRemains","TankWreck","VehicleWreck","BuildingRubble"},true);
            BuildLibrary("Assets/MassiveArmy/Resources/MassiveArmy/ResourceDecals/",new[]{"Metal","RichMetal","Oil","Gas","RareEarth","Uranium","Salvage"},false);
            Directory.CreateDirectory("TestResults/CombatSurface");File.WriteAllText("TestResults/CombatSurface/height-map.txt","Signed RFloat height map: centre -1, outer ground 0, broad rim +0.85. Readable 256x256, no mipmaps.\n");
        }
        static void BuildLibrary(string folder,string[] names,bool coverage)
        {
            string libraryPath=folder+"SurfaceLibrary.asset";
            var library=AssetDatabase.LoadAssetAtPath<CombatSurfaceLibrary>(libraryPath);
            if(library==null){library=ScriptableObject.CreateInstance<CombatSurfaceLibrary>();AssetDatabase.CreateAsset(library,libraryPath);}
            library.normalNeutral=new Vector4[names.Length];library.alphaCoverage=coverage?new uint[names.Length*32]:System.Array.Empty<uint>();
            for(int layer=0;layer<names.Length;layer++){
                var image=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                try{
                    image.LoadImage(File.ReadAllBytes(folder+names[layer]+"Normal.png"));
                    Color neutral=(image.GetPixel(1,1)+image.GetPixel(image.width-2,1)+image.GetPixel(1,image.height-2)+image.GetPixel(image.width-2,image.height-2))*.25f;
                    library.normalNeutral[layer]=new Vector4(neutral.r,neutral.g,neutral.b,0);
                    if(!coverage)continue;
                    image.LoadImage(File.ReadAllBytes(folder+names[layer]+"Albedo.png"));
                    var colours=image.GetPixels32();var occupied=new bool[1024];
                    for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)if(colours[x+y*image.width].a>0)occupied[x*32/image.width+y*32/image.height*32]=true;
                    // Conservative one-cell dilation accommodates filtering and edge mip levels.
                    for(int y=0;y<32;y++)for(int x=0;x<32;x++)if(occupied[x+y*32])for(int yy=Mathf.Max(0,y-1);yy<=Mathf.Min(31,y+1);yy++)for(int xx=Mathf.Max(0,x-1);xx<=Mathf.Min(31,x+1);xx++)library.alphaCoverage[layer*32+yy]|=1u<<xx;
                }finally{Object.DestroyImmediate(image);}
            }
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        }
    }
}
