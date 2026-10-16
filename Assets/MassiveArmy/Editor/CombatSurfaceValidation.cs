using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    public static class CombatSurfaceValidation
    {
        struct Stamp {public Vector4 positionSize,appearance,timing;}
        [Serializable] sealed class Report {public string result;public int checks;public string[] failures;}
        [MenuItem("Massive Army/Validate Combat Surface Blending")]
        public static void Run()
        {
            var failures=new List<string>();int checks=0;void Check(bool valid,string label){checks++;if(!valid)failures.Add(label);}
            var objects=new List<UnityEngine.Object>();var previous=RenderTexture.active;
            using(var stamps=new GraphicsBuffer(GraphicsBuffer.Target.Structured,512,48))
            using(var indices=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
            using(var coverage=new GraphicsBuffer(GraphicsBuffer.Target.Structured,192,4))
            using(var updates=new GraphicsBuffer(GraphicsBuffer.Target.Structured,16,8)){
                try{
                    var material=new Material(Resources.Load<Shader>("MassiveArmy/CombatSurfaceValidation"));objects.Add(material);
                    var map=new RenderTexture(new RenderTextureDescriptor(4,4){graphicsFormat=GraphicsFormat.R32_UInt,depthBufferBits=0,dimension=TextureDimension.Tex2DArray,volumeDepth=2,enableRandomWrite=true});objects.Add(map);map.Create();
                    var compute=Resources.Load<ComputeShader>("MassiveArmy/CombatPresentation");int clear=compute.FindKernel("ClearDecalMap"),write=compute.FindKernel("StampDecals");compute.SetInt("_IndexResolution",4);compute.SetTexture(clear,"_CombatIndexWrite",map);compute.Dispatch(clear,1,1,2);
                    var heads=new Vector2Int[16];for(int i=0;i<heads.Length;i++)heads[i]=new Vector2Int(i+16,1);updates.SetData(heads);compute.SetTexture(write,"_CombatIndexWrite",map);compute.SetBuffer(write,"_TileUpdates",updates);compute.SetInt("_StampCount",16);compute.Dispatch(write,1,1,1);
                    var colour=new Texture2DArray(8,8,2,TextureFormat.RGBAFloat,false,true);objects.Add(colour);colour.filterMode=FilterMode.Point;colour.wrapMode=TextureWrapMode.Clamp;
                    var red=new Color[64];var blue=new Color[64];for(int y=0;y<8;y++)for(int x=0;x<8;x++){red[x+y*8]=Color.red;blue[x+y*8]=new Color(0,0,1,x<4?0:.5f);}
                    colour.SetPixels(red,0);colour.SetPixels(blue,1);colour.Apply();
                    var normal=new Texture2DArray(8,8,2,TextureFormat.RGBAFloat,false,true);objects.Add(normal);normal.filterMode=FilterMode.Point;
                    var flat=new Color[64];var tilt=new Color[64];for(int i=0;i<64;i++){flat[i]=new Color(.5f,.5f,1,1);tilt[i]=new Color(.75f,.5f,1,1);}normal.SetPixels(flat,0);normal.SetPixels(tilt,1);normal.Apply();
                    var data=new Stamp[512];data[64]=new Stamp{positionSize=new Vector4(0,0,0,8),appearance=new Vector4(0,1,0,1),timing=new Vector4(0,100,0,1)};
                    data[65]=new Stamp{positionSize=new Vector4(0,0,0,8),appearance=new Vector4(1,1,0,1),timing=new Vector4(0,100,0,2)};stamps.SetData(data);indices.SetData(new uint[]{0,2,65,64});
                    material.SetTexture("_CombatIndexMap",map);material.SetTexture("_CombatTextures",colour);material.SetTexture("_CombatNormals",normal);material.SetBuffer("_CombatStamps",stamps);material.SetBuffer("_CombatTileIndices",indices);
                    material.SetFloat("_CombatIndexResolution",4);material.SetFloat("_CombatVisible",2);material.SetVector("_CombatLayersVisible",new Vector4(0,1,0,0));material.SetFloat("_WorldSize",64);
                    var mask=new uint[192];Array.Fill(mask,uint.MaxValue);coverage.SetData(mask);material.SetBuffer("_CombatAlphaCoverage",coverage);var neutral=new Vector4[6];for(int i=0;i<neutral.Length;i++)neutral[i]=new Vector4(.5f,.5f,1,0);material.SetVectorArray("_CombatNormalNeutral",neutral);
                    var target=new RenderTexture(64,64,0,RenderTextureFormat.ARGBFloat){filterMode=FilterMode.Point};objects.Add(target);target.Create();
                    var read=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);objects.Add(read);
                    void Render(){Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,64,64),0,0);read.Apply();}
                    Render();var left=read.GetPixel(8,32);var right=read.GetPixel(56,32);
                    Check(left.r>.99f&&left.b<.01f,"Transparent newer texels preserve the entire older decal");
                    Check(Mathf.Abs(right.r-.5f)<.02f&&Mathf.Abs(right.b-.5f)<.02f&&right.a>.99f,"Semi-transparent overlap blends both decals in linear colour");
                    Directory.CreateDirectory("TestResults/CombatSurface");File.WriteAllBytes("TestResults/CombatSurface/overlap-fixture.png",read.EncodeToPNG());
                    material.SetFloat("_TestNormal",1);Render();Check(Mathf.Abs(read.GetPixel(56,32).r-.25f)<.02f,"Decal normal contribution respects alpha coverage");
                    data[65].appearance.y=0;data[65].appearance.z=1;stamps.SetData(data);Render();
                    var rotated=read.GetPixel(32,56);Check(Mathf.Abs(rotated.r)<.02f&&rotated.g>.20f,"Rotating a decal also rotates its normal map");
                    data[65].appearance.w=0;stamps.SetData(data);material.SetFloat("_TestNormal",0);Render();Check(read.GetPixel(56,32).r>.99f,"Hidden or expired records cannot hide surviving older marks");
                    Check(!material.shader.isSupported?false:!ShaderUtil.ShaderHasError(material.shader),"Actual blending shader compiles on the editor GPU");
                    var atlas=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);objects.Add(atlas);atlas.SetPixels(new[]{new Color(0,.5f,0,.5f),new Color(0,.5f,0,.5f),new Color(0,.5f,0,.5f),new Color(0,.5f,0,.5f)});atlas.Apply();
                    var atlasNormal=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);objects.Add(atlasNormal);var flatAtlas=new Color(128f/255,128f/255,0,.5f);atlasNormal.SetPixels(new[]{flatAtlas,flatAtlas,flatAtlas,flatAtlas});atlasNormal.Apply();
                    material.SetTexture("_CraterAtlas",atlas);material.SetTexture("_CraterAtlasNormal",atlasNormal);material.SetFloat("_CraterAtlasVisible",1);material.SetFloat("_CombatVisible",0);material.SetVector("_CombatLayersVisible",Vector4.zero);Render();
                    var fallback=read.GetPixel(32,32);Check(fallback.g>.99f&&Mathf.Abs(fallback.a-.5f)<.01f,"Permanent crater atlas renders without any detailed decal slots");
                    material.SetFloat("_TestNormal",1);Render();Check(Mathf.Abs(read.GetPixel(32,32).r)<.001f&&Mathf.Abs(read.GetPixel(32,32).g)<.001f,"Flat atlas normals retain zero relief after encoding");
                    data[64].appearance.w=0;data[65].appearance=new Vector4(1,1,0,1);stamps.SetData(data);material.SetFloat("_TestNormal",0);material.SetFloat("_CombatVisible",1);material.SetVector("_CombatLayersVisible",new Vector4(0,1,0,0));Render();
                    var composite=read.GetPixel(56,32);Check(Mathf.Abs(composite.g-1f/3)<.02f&&Mathf.Abs(composite.b-2f/3)<.02f&&Mathf.Abs(composite.a-.75f)<.02f,"Transparent detailed decals blend over permanent crater coverage");
                    foreach(string name in new[]{"NukeCrater","ZombieSplatter","HumanRemains","TankWreck","VehicleWreck","BuildingRubble"}){
                        var albedo=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+name+"Albedo");var n=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+name+"Normal");
                        Check(albedo!=null&&n!=null&&albedo.width==512&&n.width==512&&albedo.height==512&&n.height==512,"Matching compressed albedo and normal dimensions for "+name);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(n));Check(importer!=null&&!importer.sRGBTexture&&!importer.alphaIsTransparency,"Normal map stays linear and retains RGB channels for "+name);
                    }
                    var height=Resources.Load<Texture2D>("MassiveArmy/CombatArt/NukeCraterHeight");Check(height!=null&&height.isReadable&&height.GetPixelBilinear(.5f,.5f).r<-.99f&&height.GetPixelBilinear(.99f,.5f).r==0,"Signed crater height map has a bowl and an unchanged outside boundary");
                    foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind))){var a=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+"Albedo");var n=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+"Normal");Check(a!=null&&n!=null&&a.width==512&&a.height==512&&n.width==512&&n.height==512,"Resource albedo and normal dimensions: "+kind);var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(n));Check(importer!=null&&!importer.sRGBTexture&&!importer.alphaIsTransparency,"Linear RGB resource normal: "+kind);}
                }catch(Exception e){failures.Add(e.ToString());}
                finally {RenderTexture.active=previous;foreach(var o in objects)UnityEngine.Object.DestroyImmediate(o);}
            }
            Directory.CreateDirectory("TestResults/CombatSurface");File.WriteAllText("TestResults/CombatSurface/blending-validation.json",JsonUtility.ToJson(new Report{result=failures.Count==0?"PASS":"FAIL",checks=checks,failures=failures.ToArray()},true));
        }
    }
}
