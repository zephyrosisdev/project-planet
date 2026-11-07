#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetWeatherRenderer {
        [Serializable]public sealed class WeatherFrame {
            public int render,frame,width,height,bodies;public double seconds,altitude;
            public bool piloting,cabin,localDrawn,gpuReady,depthReady,readbackError;
            public float near,far,cpuProjection22,cpuProjection23,renderProjection22,renderProjection23,centreRawDepth;
            public Vector3 cameraAngles,cabinEye;public Vector4 localBounds,centreWeather;
        }
        [Serializable]public sealed class WeatherHistory {public string note="Last 128 renders, including paused repaints. Centre-pixel probes are diagnostic samples, not whole-image visibility or performance acceptance.";public WeatherFrame[] frames;}
        readonly WeatherFrame[] weatherHistory=new WeatherFrame[128];int traceRender;bool traceDisposed;
        public bool TraceEnabled;
        public string TraceJson(){int count=Math.Min(traceRender,weatherHistory.Length);var result=new WeatherHistory{frames=new WeatherFrame[count]};for(int i=0;i<count;i++)result.frames[i]=weatherHistory[(traceRender-count+i)%weatherHistory.Length];return JsonUtility.ToJson(result,true);}
        public void ClearTrace(){Array.Clear(weatherHistory,0,weatherHistory.Length);traceRender=0;}
        void TraceWeather(CustomPassContext ctx,Matrix4x4 projection,bool cabin,Vector3 eye){
            if(!TraceEnabled||traceDisposed)return;
            var camera=world.View;var cpu=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            var record=new WeatherFrame{render=++traceRender,frame=Time.frameCount,seconds=Time.realtimeSinceStartupAsDouble,width=width,height=height,bodies=VisibleBodies,
                altitude=world.ObserverPosition.RelativeTo(world.definition.position).Magnitude-world.definition.radius,piloting=world.IsPiloting,cabin=cabin,cabinEye=eye,localDrawn=LocalAtmosphereDrawn,
                near=camera.nearClipPlane,far=camera.farClipPlane,cameraAngles=camera.transform.eulerAngles,cpuProjection22=cpu.m22,cpuProjection23=cpu.m23,renderProjection22=projection.m22,renderProjection23=projection.m23};
            for(int i=0;i<VisibleBodies;i++)if(bodies[i]==world.definition){record.localBounds=blocks[i].GetVector("_WeatherBounds");break;}
            weatherHistory[(traceRender-1)%weatherHistory.Length]=record;
            // Two opt-in, ordered one-pixel probes. Keep the failed render in
            // history even if a later paused repaint restores the image.
            ctx.cmd.RequestAsyncReadback(weather,0,weather.width/2,1,weather.height/2,1,0,1,TextureFormat.RGBAFloat,r=>{
                if(traceDisposed)return;if(r.hasError){record.readbackError=true;return;}var v=r.GetData<float>();record.centreWeather=new Vector4(v[0],v[1],v[2],v[3]);record.gpuReady=true;
            });
            ctx.cmd.RequestAsyncReadback(localDepth,0,width/2,1,height/2,1,0,1,TextureFormat.RFloat,r=>{
                if(traceDisposed)return;if(r.hasError){record.readbackError=true;return;}record.centreRawDepth=r.GetData<float>()[0];record.depthReady=true;
            });
        }
    }
}
#endif
