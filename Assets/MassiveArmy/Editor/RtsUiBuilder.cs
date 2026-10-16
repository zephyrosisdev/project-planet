using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace MassiveArmy.EditorTools
{
    public static class RtsUiBuilder
    {
        const string Folder="Assets/MassiveArmy/Resources/MassiveArmy/UI/";
        [MenuItem("Massive Army/UI/Build UI Assets")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            string glowPath=Folder+"ControlHighlight.png";
            if(!File.Exists(glowPath)){
                var glow=new Texture2D(64,16,TextureFormat.RGBA32,false);
                for(int y=0;y<16;y++)for(int x=0;x<64;x++)glow.SetPixel(x,y,new Color(.08f,.85f,1,Mathf.Pow(y/15f,3)*Mathf.Sin(x*Mathf.PI/63)*.32f));
                glow.Apply();File.WriteAllBytes(glowPath,glow.EncodeToPNG());UnityEngine.Object.DestroyImmediate(glow);AssetDatabase.ImportAsset(glowPath);
            }
            MakeFont("Rajdhani-SemiBold.ttf","Rajdhani SDF.asset");
            MakeFont("Rajdhani-Bold.ttf","Rajdhani Bold SDF.asset");
            MakeFont("Inter-Regular.ttf","Inter SDF.asset");
            MakeFont("Inter-Medium.ttf","Inter Medium SDF.asset");
            var text=LoadOrCreate<PanelTextSettings>("FrontText.asset");
            text.defaultFontAsset=AssetDatabase.LoadAssetAtPath<FontAsset>(Folder+"Fonts/Inter SDF.asset");
            var panel=LoadOrCreate<PanelSettings>("FrontPanel.asset");
            panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1920,1080);panel.screenMatchMode=PanelScreenMatchMode.Expand;panel.scale=1;
            panel.textSettings=text;panel.sortingOrder=100;
            string themePath=Folder+"FrontTheme.tss";
            if(!File.Exists(themePath))File.WriteAllText(themePath,"@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(themePath,ImportAssetOptions.ForceUpdate);
            var theme=AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            if(theme==null)throw new InvalidOperationException("Missing default runtime theme");
            panel.themeStyleSheet=theme;
            var art=LoadOrCreate<RtsUiArt>("FrontArt.asset");var entries=new List<RtsUiArt.Entry>();var catalog=RtsCatalog.Load();
            foreach(var b in catalog.buildings)entries.Add(new RtsUiArt.Entry{kind="building",id=b.id,portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Portraits/building-"+b.id+".png")});
            foreach(var u in catalog.units)entries.Add(new RtsUiArt.Entry{kind="unit",id=u.id,portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"Portraits/unit-"+u.id+".png")});
            foreach(var t in catalog.technologies)entries.Add(new RtsUiArt.Entry{kind="technology",id=t.id,icon=AssetDatabase.LoadAssetAtPath<VectorImage>(Folder+"Icons/tech-"+t.id+".svg")});
            art.entries=entries.ToArray();EditorUtility.SetDirty(art);EditorUtility.SetDirty(panel);EditorUtility.SetDirty(text);AssetDatabase.SaveAssets();
            Validate();
        }
        static T LoadOrCreate<T>(string name) where T:ScriptableObject
        {
            var asset=AssetDatabase.LoadAssetAtPath<T>(Folder+name);if(asset!=null)return asset;asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,Folder+name);return asset;
        }
        static void MakeFont(string source,string name)
        {
            if(AssetDatabase.LoadAssetAtPath<FontAsset>(Folder+"Fonts/"+name)!=null)return;
            var font=AssetDatabase.LoadAssetAtPath<Font>(Folder+"Fonts/"+source);if(font==null)throw new InvalidOperationException("Missing font "+source);
            var asset=FontAsset.CreateFontAsset(font,48,6,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
            AssetDatabase.CreateAsset(asset,Folder+"Fonts/"+name);asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?/%×²↑↓→←−+·—…()[]{}&'\"=<>_");
            AssetDatabase.AddObjectToAsset(asset.material,asset);foreach(var atlas in asset.atlasTextures)AssetDatabase.AddObjectToAsset(atlas,asset);EditorUtility.SetDirty(asset);
        }
        [MenuItem("Massive Army/UI/Validate UI Assets")]
        public static void Validate()
        {
            var art=AssetDatabase.LoadAssetAtPath<RtsUiArt>(Folder+"FrontArt.asset");var missing=new List<string>();
            foreach(var entry in art.entries)if(entry.kind=="technology"?entry.icon==null:entry.portrait==null)missing.Add(entry.kind+":"+entry.id);
            var report=new AssetReport{result=missing.Count==0?"PASS":"INCOMPLETE",entries=art.entries.Length,missing=missing.ToArray()};Directory.CreateDirectory("TestResults/UiRedesign");File.WriteAllText("TestResults/UiRedesign/assets.json",JsonUtility.ToJson(report,true));
            Debug.Log("UI art coverage: "+report.result+"; "+missing.Count+" missing");
        }
        [Serializable] sealed class AssetReport{public string result;public int entries;public string[] missing;}
    }
    public sealed class RtsUiTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.Contains("/UI/Portraits/"))return;var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Compressed;importer.isReadable=false;importer.sRGBTexture=true;
        }
    }
}
