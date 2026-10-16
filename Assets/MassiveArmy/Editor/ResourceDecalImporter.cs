using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public sealed class ResourceDecalImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if((!assetPath.StartsWith("Assets/MassiveArmy/Resources/MassiveArmy/ResourceDecals/")&&!assetPath.StartsWith("Assets/MassiveArmy/Resources/MassiveArmy/CombatArt/"))||!assetPath.EndsWith(".png"))return;
            var importer=(TextureImporter)assetImporter;
            bool normal=assetPath.EndsWith("Normal.png");
            // Keep RGB tangent normals linear; Unity's normal importer repacks channels.
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=!normal;importer.alphaSource=normal?TextureImporterAlphaSource.None:TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=!normal;importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=512;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=2;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Standalone",overridden=true,maxTextureSize=512,format=TextureImporterFormat.BC7});
        }
    }
}
