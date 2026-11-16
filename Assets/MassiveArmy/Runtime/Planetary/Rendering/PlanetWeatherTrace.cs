#if UNITY_EDITOR || SURVIVAL_GRAPHICS_PROBE || SURVIVAL_WEATHER_TRACE
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetWeatherRenderer {
        [Serializable]public sealed class WeatherFrame {
            public int render,frame,width,height,bodies,stage;public double seconds,altitude;
            public bool piloting,cabin,localDrawn,gpuReady,depthReady,readbackError;
            public float near,far,cpuProjection22,cpuProjection23,renderProjection22,renderProjection23,centreRawDepth;
            public Vector3 cameraAngles,cabinEye;public Vector4 localBounds,centreWeather;
            public float terrainWeatherMean,terrainWeatherMissing,terrainWeatherLight;public bool gridReady,lightCacheReady;
            public Double3 bodyEye;
            public Vector4 centreRange;
        }
        [Serializable]public sealed class WeatherHistory {public string note="Bounded render history. Centre probes and a sparse 64x36 grid are diagnostic samples, not whole-image visibility or performance acceptance.";public string gpu,api,definition,ship;public WeatherFrame[] frames;}
        #if SURVIVAL_GRAPHICS_PROBE || SURVIVAL_WEATHER_TRACE
        readonly WeatherFrame[] weatherHistory=new WeatherFrame[4096];
#else
        readonly WeatherFrame[] weatherHistory=new WeatherFrame[128];
#endif
        int traceRender;bool traceDisposed;
#if SURVIVAL_GRAPHICS_PROBE
        readonly bool[] traceImageWritten=new bool[3];
#endif
#if SURVIVAL_GRAPHICS_PROBE || SURVIVAL_WEATHER_TRACE
        RenderTexture traceGrid,traceRay;double traceGridAt;
#endif
        RenderTexture traceWeatherPixel,traceDepthPixel;
        static string TraceArgument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        static readonly bool traceRequested=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-weather-trace")>=0;
        int traceSavedFrame=-1;
        internal void InitializeTrace(){TraceEnabled=traceRequested;}
        void DumpTrace(bool screenshot){
            if(!traceRequested||traceRender==0)return;
            try{string folder=TraceArgument("-planet-weather-trace-output")??Path.Combine(Application.persistentDataPath,"Diagnostics","Weather");Directory.CreateDirectory(folder);string file=Path.Combine(folder,"weather-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));File.WriteAllText(file+".json",TraceJson());if(screenshot)ScreenCapture.CaptureScreenshot(file+".png");Debug.Log("Saved weather trace: "+file+".json");}
            catch(Exception error){Debug.LogWarning("Weather trace could not be saved: "+error.Message);}
        }
        void ReleaseTrace(){DumpTrace(false);traceDisposed=true;foreach(var rt in new[]{traceWeatherPixel,traceDepthPixel})if(rt!=null){rt.Release();ArmyLifetime.Destroy(rt);}traceWeatherPixel=traceDepthPixel=null;
#if SURVIVAL_GRAPHICS_PROBE || SURVIVAL_WEATHER_TRACE
            foreach(var rt in new[]{traceGrid,traceRay})if(rt!=null){rt.Release();ArmyLifetime.Destroy(rt);}traceGrid=traceRay=null;
#endif
        }
        public bool TraceEnabled;
        public string TraceJson(){int count=Math.Min(traceRender,weatherHistory.Length);var result=new WeatherHistory{gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),definition=JsonUtility.ToJson(world.definition),ship=world.ShipForValidation!=null?JsonUtility.ToJson(world.ShipForValidation.CaptureSimulation()):null,frames=new WeatherFrame[count]};for(int i=0;i<count;i++)result.frames[i]=weatherHistory[(traceRender-count+i)%weatherHistory.Length];return JsonUtility.ToJson(result,true);}
        public void ClearTrace(){Array.Clear(weatherHistory,0,weatherHistory.Length);traceRender=0;}
        void TraceWeather(CustomPassContext ctx,Matrix4x4 projection,bool cabin,Vector3 eye){
            if(!TraceEnabled||traceDisposed)return;
            var keyboard=Keyboard.current;if(traceRequested&&keyboard!=null&&(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)&&(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed)&&keyboard.f10Key.wasPressedThisFrame&&traceSavedFrame!=Time.frameCount){traceSavedFrame=Time.frameCount;DumpTrace(true);}
            var camera=world.View;var cpu=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            var record=new WeatherFrame{render=++traceRender,frame=Time.frameCount,seconds=Time.realtimeSinceStartupAsDouble,width=width,height=height,bodies=VisibleBodies,
                bodyEye=world.ObserverPosition.RelativeTo(world.definition.position),lightCacheReady=lightRefresh.Valid,altitude=world.ObserverPosition.RelativeTo(world.definition.position).Magnitude-world.definition.radius,piloting=world.IsPiloting,cabin=cabin,cabinEye=eye,localDrawn=LocalAtmosphereDrawn,
                near=camera.nearClipPlane,far=camera.farClipPlane,cameraAngles=camera.transform.eulerAngles,cpuProjection22=cpu.m22,cpuProjection23=cpu.m23,renderProjection22=projection.m22,renderProjection23=projection.m23};
#if SURVIVAL_GRAPHICS_PROBE
            record.stage=DiagnosticStage;
#endif
            for(int i=0;i<VisibleBodies;i++)if(bodies[i]==world.definition){record.localBounds=blocks[i].GetVector("_WeatherBounds");break;}
            weatherHistory[(traceRender-1)%weatherHistory.Length]=record;
#if SURVIVAL_GRAPHICS_PROBE || SURVIVAL_WEATHER_TRACE
            if(record.seconds>=traceGridAt){traceGridAt=record.seconds;
                if(traceGrid==null){traceGrid=new RenderTexture(64,36,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);traceGrid.Create();}
                // Paired textures in one grid: weather opacity and local opaque depth.
                ctx.cmd.SetGlobalTexture("_WeatherIntegrated",weather);ctx.cmd.SetRenderTarget(traceGrid);ctx.cmd.SetViewport(new Rect(0,0,64,36));ctx.cmd.DrawProcedural(Matrix4x4.identity,material,5,MeshTopology.Triangles,3);
                ctx.cmd.RequestAsyncReadback(traceGrid,0,TextureFormat.RGBAFloat,r=>{if(traceDisposed||r.hasError){record.readbackError=true;return;}var data=r.GetData<Color>();int count=0,missing=0;float sum=0,light=0;foreach(var c in data)if(c.g>0){count++;sum+=c.r;light+=c.b;if(c.r<.001f)missing++;}record.terrainWeatherMean=sum/Math.Max(1,count);record.terrainWeatherLight=light/Math.Max(1,count);record.terrainWeatherMissing=(float)missing/Math.Max(1,count);record.gridReady=true;});
                if(traceRay==null){traceRay=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);traceRay.Create();}
                for(int body=0;body<VisibleBodies;body++)if(bodies[body]==world.definition){ctx.cmd.SetRenderTarget(traceRay);ctx.cmd.SetViewport(new Rect(0,0,1,1));ctx.cmd.DrawProcedural(Matrix4x4.identity,material,6,MeshTopology.Triangles,3,1,blocks[body]);ctx.cmd.RequestAsyncReadback(traceRay,0,TextureFormat.RGBAFloat,r=>{if(traceDisposed||r.hasError)return;var c=r.GetData<Color>()[0];record.centreRange=new Vector4(c.r,c.g,c.b,c.a);});break;}
                CoreUtils.SetRenderTarget(ctx.cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);ctx.cmd.SetViewport(new Rect(0,0,width,height));
            }
#endif
#if SURVIVAL_GRAPHICS_PROBE
            if(DiagnosticStage>0&&!traceImageWritten[DiagnosticStage]){int stage=DiagnosticStage,w=weather.width,h=weather.height;traceImageWritten[stage]=true;
                ctx.cmd.RequestAsyncReadback(weather,0,TextureFormat.RGBA32,r=>{if(r.hasError)return;var image=new Texture2D(w,h,TextureFormat.RGBA32,false,true);try{image.LoadRawTextureData(r.GetData<byte>());image.Apply();System.IO.Directory.CreateDirectory("TestResults/Planetary/WeatherDepth/OrbitDebug");System.IO.File.WriteAllBytes("TestResults/Planetary/WeatherDepth/OrbitDebug/stage-"+stage+".png",image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}});
            }
#endif
            // Two opt-in, ordered one-pixel probes. Keep the failed render in
            // history even if a later paused repaint restores the image.
            if(traceWeatherPixel==null){traceWeatherPixel=new RenderTexture(1,1,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);traceWeatherPixel.Create();traceDepthPixel=new RenderTexture(1,1,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);traceDepthPixel.Create();}
            ctx.cmd.CopyTexture(weather,0,0,weather.width/2,weather.height/2,1,1,traceWeatherPixel,0,0,0,0);ctx.cmd.CopyTexture(localDepth,0,0,width/2,height/2,1,1,traceDepthPixel,0,0,0,0);
            ctx.cmd.RequestAsyncReadback(traceWeatherPixel,0,TextureFormat.RGBAFloat,r=>{
                if(traceDisposed)return;if(r.hasError){record.readbackError=true;return;}var v=r.GetData<float>();record.centreWeather=new Vector4(v[0],v[1],v[2],v[3]);record.gpuReady=true;
            });
            ctx.cmd.RequestAsyncReadback(traceDepthPixel,0,TextureFormat.RFloat,r=>{
                if(traceDisposed)return;if(r.hasError){record.readbackError=true;return;}record.centreRawDepth=r.GetData<float>()[0];record.depthReady=true;
            });
        }
    }
}
#endif
