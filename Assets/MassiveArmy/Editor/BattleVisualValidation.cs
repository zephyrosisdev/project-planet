using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.EditorTools
{
    public static class BattleVisualValidation
    {
        [Serializable] sealed class Report{public string status="PASS",error;public string[] checks;public int fogFrames;public float enabledDifference;}
        [MenuItem("Massive Army/Art/Validate Battle Atmosphere and Fog")]
        public static void Run()
        {
            var report=new Report();var checks=new List<string>();var previous=RenderTexture.active;
            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128*128*2,4);
            var target=new RenderTexture(128,128,0,RenderTextureFormat.RFloat){enableRandomWrite=true};target.Create();
            var read=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/FogOfWar"));
            void Require(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
            try
            {
                shader.SetBuffer(0,"_Visibility",buffer);shader.SetTexture(0,"_DensityMask",target);shader.SetInt("_Resolution",128);shader.SetVector("_MaskSettings",new Vector4(.18f,1.5f,1,0));
                float Test(uint state){var data=new uint[128*128*2];for(int i=0;i<data.Length;i++)data[i]=state;buffer.SetData(data);shader.Dispatch(0,16,16,1);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();return read.GetPixel(64,64).r;}
                Require(Mathf.Abs(Test(0)-1)<.001f,"Unexplored cells generate full cloud density");
                Require(Mathf.Abs(Test(1)-.18f)<.001f,"Explored cells generate lighter persistent mist");
                Require(Test(2)<.001f,"Visible cells clear the volumetric fog");
                shader.SetBuffer(1,"_Visibility",buffer);shader.SetTexture(1,"_DarknessMask",target);shader.SetVector("_DarkSettings",new Vector4(.96f,.35f,0,0));
                float Dark(uint state){var data=new uint[128*128*2];for(int i=0;i<data.Length;i++)data[i]=state;buffer.SetData(data);shader.Dispatch(1,16,16,1);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();return read.GetPixel(64,64).r;}
                Require(Mathf.Abs(Dark(0)-.96f)<.001f,"Unexplored terrain has strong independent concealment");
                Require(Mathf.Abs(Dark(1)-.35f)<.001f,"Explored terrain is dimmer than visible ground");
                Require(Dark(2)<.001f,"Current vision removes concealment");
                var flow=new RenderTexture(128,128,0,RenderTextureFormat.ARGBHalf){enableRandomWrite=true};flow.Create();
                var ground=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);ground.SetPixel(0,0,Color.clear);ground.Apply();
                try{shader.SetTexture(2,"_FlowMap",flow);shader.SetTexture(2,"_TerrainHeight",ground);shader.SetVector("_FlowSettings",new Vector4(160,2048,19,0));shader.Dispatch(2,16,16,1);RenderTexture.active=flow;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();
                    var velocities=read.GetPixels();Require(velocities.All(v=>float.IsFinite(v.r)&&float.IsFinite(v.g)&&v.r*v.r+v.g*v.g<1.01f)&&velocities.Average(v=>v.r*v.r+v.g*v.g)>.01,"GPU flow map contains bounded spatially varying velocities");}
                finally{RenderTexture.active=previous;flow.Release();UnityEngine.Object.DestroyImmediate(flow);UnityEngine.Object.DestroyImmediate(ground);}
                var split=new uint[128*128*2];for(int y=0;y<128;y++)for(int x=0;x<64;x++)split[x+y*128]=2;buffer.SetData(split);shader.Dispatch(0,16,16,1);RenderTexture.active=target;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();
                Require(read.GetPixel(63,64).r>0&&read.GetPixel(64,64).r<1,"Visibility boundaries are smoothly filtered on the GPU");
                var preset=Resources.Load<BattleVisualSettings>("MassiveArmy/BattleVisualSettings");Require(preset!=null&&preset.sunLux>10000&&preset.exposure>10,"Shared daylight preset balances physical sun and camera exposure");
                var fogShader=Resources.Load<Shader>("MassiveArmy/VolumetricFogOfWar");Require(fogShader!=null&&fogShader.isSupported&&!ShaderUtil.GetShaderMessages(fogShader).Any(m=>m.severity.ToString()=="Error"),"Both volumetric fog shader passes compile");
                var component=UnityEngine.Object.FindFirstObjectByType<VolumetricFogOfWar>();
                if(EditorApplication.isPlaying)
                {
                    Require(component!=null&&component.RenderedFrames>0,"Fog volume executes in the generated battle");report.fogFrames=component.RenderedFrames;
                    var app=UnityEngine.Object.FindFirstObjectByType<BattleApp>();
                    int width=app.View.pixelWidth,height=app.View.pixelHeight;
                    var capture=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf);capture.Create();
                    var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
                    bool enabled=preset.fogOfWar;
                    Color[] Capture(bool fog,string name)
                    {
                        preset.fogOfWar=fog;
                        RenderTexture.active=previous;
                        var request=new RenderPipeline.StandardRequest{destination=capture};
                        Require(RenderPipeline.SupportsRenderRequest(app.View,request),"HDRP battle capture supported");
                        RenderPipeline.SubmitRenderRequest(app.View,request);RenderPipeline.SubmitRenderRequest(app.View,request);
                        RenderTexture.active=capture;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                        RenderTexture.active=previous;
                        File.WriteAllBytes("TestResults/"+name+".png",pixels.EncodeToPNG());return pixels.GetPixels();
                    }
                    try
                    {
                        var clear=Capture(false,"battle-daylight");var cloudy=Capture(true,"battle-volumetric-fog");
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                        var fogPass=typeof(VolumetricFogOfWar).GetField("pass",flags).GetValue(component);
                        var half=(RTHandle)fogPass.GetType().GetField("fog",flags).GetValue(fogPass);
                        RenderTexture.active=half.rt;
                        var alpha=new Texture2D(half.rt.width,half.rt.height,TextureFormat.RGBAFloat,false,true);
                        alpha.ReadPixels(new Rect(0,0,alpha.width,alpha.height),0,0);alpha.Apply();
                        var values=alpha.GetPixels();for(int i=0;i<values.Length;i++)values[i]=new Color(values[i].a,values[i].a,values[i].a,1);
                        alpha.SetPixels(values);alpha.Apply();File.WriteAllBytes("TestResults/fog-opacity.png",alpha.EncodeToPNG());UnityEngine.Object.DestroyImmediate(alpha);
                        report.enabledDifference=clear.Zip(cloudy,(a,b)=>(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/3).Average();
                        Require(report.enabledDifference>.002f,"Volumetric fog changes the actual HDRP battle image");
                    }
                    finally{preset.fogOfWar=enabled;RenderTexture.active=previous;capture.Release();UnityEngine.Object.DestroyImmediate(capture);UnityEngine.Object.DestroyImmediate(pixels);}
                }
            }
            catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{RenderTexture.active=previous;buffer.Dispose();target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(shader);}
            report.checks=checks.ToArray();Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/battle-visual-validation.json",JsonUtility.ToJson(report,true));Debug.Log("Battle visuals: "+report.status);
        }
    }
}
