using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace MassiveArmy.EditorTools {
 public static class PlanetWeatherDepthValidation {
  [Serializable] sealed class Report {public string status="PASS",error="",gpu,scope="Isolated GPU weather compositing over perspective depth ramps and a thin foreground silhouette. Fenced draw timing is not full-scene FPS or GPU timestamp acceptance.";public int missingPixels,foregroundLeaks;public double medianMs,p95Ms;public List<string> checks=new List<string>();}
  public static void Run(){
   var report=new Report{gpu=SystemInfo.graphicsDeviceName};Material material=null;Texture2D depth=null;RenderTexture light=null,transmission=null,target=null;CommandBuffer commands=null;var prior=RenderTexture.active;
   var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"-weather-depth-output");string output=arg>=0?args[arg+1]:"TestResults/Planetary/WeatherDepth/Current";Directory.CreateDirectory(output);
   try{
    const int w=512,h=256;material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetWeather"));foreach(var message in ShaderUtil.GetShaderMessages(material.shader))UnityEngine.Debug.Log("Weather shader: "+message.message+" line "+message.line);if(ShaderUtil.ShaderHasError(material.shader))throw new InvalidOperationException("Weather shader compile failure");
    depth=new Texture2D(w,h,TextureFormat.RFloat,false,true){filterMode=FilterMode.Point};
    RenderTexture RT(int x,int y){var rt=new RenderTexture(x,y,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);rt.Create();return rt;}
    light=RT(w/4,h/4);transmission=RT(w/4,h/4);target=RT(w,h);commands=new CommandBuffer{name="Weather depth continuity"};
    material.SetTexture("_WeatherLocalDepth",depth);material.SetTexture("_WeatherIntegrated",light);material.SetTexture("_WeatherTransmission",transmission);material.SetVector("_WeatherFullSize",new Vector4(w,h,1f/w,1f/h));material.SetVector("_WeatherHalfSize",new Vector4(w/4,h/4,4f/w,4f/h));
    var vp=GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,2,.1f,10000000),true);material.SetVector("_WeatherDepthProjection",new Vector4(vp.m22,vp.m23,1,0));
    float Raw(float distance){var clip=vp*new Vector4(0,0,-distance,1);return clip.z/clip.w;}
    void Draw(){commands.Clear();commands.SetRenderTarget(light);commands.ClearRenderTarget(false,true,new Color(.08f,.12f,.18f,.4f));commands.SetRenderTarget(transmission);commands.ClearRenderTarget(false,true,new Color(.6f,.65f,.7f,1));commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,w,h));commands.ClearRenderTarget(false,true,new Color(.5f,.5f,.5f,1));commands.DrawProcedural(Matrix4x4.identity,material,3,MeshTopology.Triangles,3);commands.DrawProcedural(Matrix4x4.identity,material,4,MeshTopology.Triangles,3);Graphics.ExecuteCommandBuffer(commands);var read=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);read.WaitForCompletion();if(read.hasError)throw new InvalidOperationException("Weather fence failed");}
    Color[] Capture(string name){RenderTexture.active=target;var image=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);try{image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();var result=image.GetPixels();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());return result;}finally{Object.DestroyImmediate(image);}}
    var pixels=new float[w*h];var expected=new Color(.38f,.445f,.53f,1);
    foreach(float grazing in new[]{0f,.8f,.98f,.999f}){
     for(int y=0;y<h;y++)for(int x=0;x<w;x++){float reciprocal=Mathf.Lerp(1,1-grazing,(x+.5f)/w)/500;pixels[y*w+x]=Raw(1/reciprocal);}depth.SetPixelData(pixels,0);depth.Apply(false);Draw();var image=Capture("slope-"+grazing);
     int missing=0;for(int y=4;y<h-4;y++)for(int x=4;x<w-4;x++){var c=image[y*w+x];if(Math.Abs(c.r-expected.r)+Math.Abs(c.g-expected.g)+Math.Abs(c.b-expected.b)>.015f)missing++;}report.missingPixels+=missing;report.checks.Add("Perspective depth slope "+grazing+": "+missing+" missing composite pixels");
    }
    for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=x==253?Raw(1):0;depth.SetPixelData(pixels,0);depth.Apply(false);Draw();var silhouette=Capture("thin-foreground");for(int y=4;y<h-4;y++){var c=silhouette[y*w+253];if(Math.Abs(c.r-.5f)+Math.Abs(c.g-.5f)+Math.Abs(c.b-.5f)>.01f)report.foregroundLeaks++;}report.checks.Add("Thin foreground leaks: "+report.foregroundLeaks);
    for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=Raw(500+x*20);depth.SetPixelData(pixels,0);depth.Apply(false);for(int i=0;i<8;i++)Draw();var times=new double[40];for(int i=0;i<times.Length;i++){var watch=Stopwatch.StartNew();Draw();times[i]=watch.Elapsed.TotalMilliseconds;}Array.Sort(times);report.medianMs=times[20];report.p95Ms=times[38];
    if(report.missingPixels!=0||report.foregroundLeaks!=0){report.status="FAIL";report.error="Weather depth discontinuity or foreground leak";}
   }catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);}finally{RenderTexture.active=prior;commands?.Dispose();if(material!=null)Object.DestroyImmediate(material);if(depth!=null)Object.DestroyImmediate(depth);foreach(var rt in new[]{light,transmission,target})if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(report,true));UnityEngine.Debug.Log("Weather depth validation: "+report.status);}
  }
 }
}
