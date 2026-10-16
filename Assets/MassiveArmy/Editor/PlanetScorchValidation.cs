using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools
{
    public static class PlanetScorchValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public long atlasBytes=PlanetNuclearScorch.AtlasBytes;public int paintedPixels,retainedPixels,overlapPixels,impacts;}
        static Color32[] Read(RenderTexture texture,int face){var read=AsyncGPUReadback.Request(texture,0,0,1024,0,1024,face,1);read.WaitForCompletion();if(read.hasError)throw new InvalidOperationException("Atlas GPU readback failed");return read.GetData<Color32>().ToArray();}
        static Texture2DArray Source(string suffix,bool linear){var source=Resources.Load<Texture2D>("MassiveArmy/CombatArt/NukeCrater"+suffix);var array=new Texture2DArray(source.width,source.height,1,source.format,true,linear);array.Apply(false,true);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,array,0,mip);return array;}
        public static void Run(){var report=new Report();Directory.CreateDirectory("TestResults/Planetary/Scorch");var material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTerrain"));var colour=Source("Albedo",false);var normal=Source("Normal",true);PlanetNuclearScorch scorch=null;
            void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{
                scorch=new PlanetNuclearScorch(material,colour,normal,Resources.Load<CombatSurfaceLibrary>("MassiveArmy/CombatArt/SurfaceLibrary").normalNeutral[0]);
                var blank=Read(scorch.ColourForValidation,0);Check(Array.TrueForAll(blank,p=>p.r==255&&p.g==255&&p.b==255&&p.a==0),"Unpainted atlas is neutral and transparent");
                scorch.Paint(Vector3.right,120,6000);var first=Read(scorch.ColourForValidation,0);var firstNormals=Read(scorch.ReliefForValidation,0);foreach(var pixel in first)if(pixel.a>0)report.paintedPixels++;Check(report.paintedPixels>100,"Original nuke texture paints a surface footprint");
                scorch.Paint(new Vector3(1,0,.024f).normalized,120,6000);var second=Read(scorch.ColourForValidation,0);
                bool monotonic=true;for(int i=0;i<first.Length;i++){var a=first[i];var b=second[i];if(a.a>0){report.retainedPixels++;if(b.a<a.a||b.r>a.r||b.g>a.g||b.b>a.b)monotonic=false;if(b.r<a.r||b.g<a.g||b.b<a.b)report.overlapPixels++;}}
                Check(monotonic&&report.retainedPixels==report.paintedPixels,"Overlapping blast preserves every earlier painted texel without brightening or clearing");Check(report.overlapPixels>10,"Overlapping burns actually accumulate");
                scorch.Paint(Vector3.up,120,6000);var apart=Read(scorch.ColourForValidation,0);bool unchanged=true;for(int i=0;i<apart.Length;i++)if(!apart[i].Equals(second[i]))unchanged=false;Check(unchanged,"A separate blast cannot remove an earlier footprint");
                for(int i=0;i<520;i++)scorch.Paint(Vector3.left,120,6000);var wrapped=Read(scorch.ColourForValidation,0);unchanged=true;for(int i=0;i<wrapped.Length;i++)if(!wrapped[i].Equals(second[i]))unchanged=false;Check(unchanged,"More than 512 subsequent impacts cannot recycle an older nuclear mark");
                var beforeNormal=Read(scorch.ReliefForValidation,0);scorch.Paint(Vector3.down,120,6000);var afterNormal=Read(scorch.ReliefForValidation,0);unchanged=true;for(int i=0;i<beforeNormal.Length;i++)if(!beforeNormal[i].Equals(afterNormal[i]))unchanged=false;Check(unchanged,"Unrelated impacts preserve accumulated normal detail");
                var seam=new Vector3(1,.1f,1).normalized;scorch.Paint(seam,120,6000);var faceX=Read(scorch.ColourForValidation,0);var faceZ=Read(scorch.ColourForValidation,4);int xPaint=0,zPaint=0;for(int y=535;y<591;y++)for(int x=0;x<16;x++){if(faceX[y*1024+x].a>0)xPaint++;if(faceZ[y*1024+1023-x].a>0)zPaint++;}Check(xPaint>10&&zPaint>10,"A single impact paints both sides of a cube-face seam");
                var antipode=Read(scorch.ColourForValidation,5);Check(Array.TrueForAll(antipode,p=>p.a==0),"Projection does not stamp a second mark through the back of the planet");
                report.impacts=scorch.Count;Check(report.impacts==525,"Impact storage remains fixed without a mark-count eviction limit");Check(PlanetNuclearScorch.AtlasBytes==50331648,"Two fixed RGBA8 atlases use 48 MiB without growing with impact count");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{scorch?.Dispose();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(colour);UnityEngine.Object.DestroyImmediate(normal);File.WriteAllText("TestResults/Planetary/Scorch/atlas-validation.json",JsonUtility.ToJson(report,true));}
        }
    }
}
