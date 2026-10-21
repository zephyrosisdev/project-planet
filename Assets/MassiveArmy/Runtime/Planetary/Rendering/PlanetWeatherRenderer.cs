using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Profiling;
namespace MassiveArmy.Planetary
{
    // View-only weather. One shared noise volume, bounded body draws and half-resolution integration;
    // unvisited planets receive no weather simulation, terrain allocations or army state.
    public sealed class PlanetWeatherRenderer : IDisposable
    {
        readonly PlanetDevelopment world;readonly Material material;readonly Texture3D noise;
        readonly PlanetDefinition[] bodies=new PlanetDefinition[17];readonly double[] distances=new double[17];
        readonly MaterialPropertyBlock[] blocks=new MaterialPropertyBlock[17];
        readonly CustomSampler sample=GpuSamples.Get("Planet.Weather");readonly Recorder recorder;
        RenderTexture localDepth,weather;int width,height;
        public bool Enabled=true;
        internal RenderTexture DepthForValidation=>localDepth;
        internal TextureDimension DepthDimensionForValidation {get;private set;}
        public int VisibleBodies {get;private set;}
        public int VolumetricBodies {get;private set;}
        public bool LocalAtmosphereDrawn {get;private set;}
        public long TextureBytes=>localDepth!=null?(long)width*height*4+(long)weather.width*weather.height*8+299593:299593;
        public double GpuMilliseconds=>recorder!=null&&recorder.gpuSampleBlockCount>0?recorder.gpuElapsedNanoseconds/1e6:0;
        public PlanetWeatherRenderer(PlanetDevelopment world){
            this.world=world;material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetWeather"));
            var data=new byte[64*64*64];for(int i=0;i<data.Length;i++)data[i]=(byte)PlanetWeather.Hash((uint)i+8317);
            noise=new Texture3D(64,64,64,TextureFormat.R8,true){name="Shared seamless weather density",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};noise.SetPixelData(data,0);noise.Apply(true,true);material.SetTexture("_WeatherNoise",noise);
            for(int i=0;i<blocks.Length;i++)blocks[i]=new MaterialPropertyBlock();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-weather-validation")>=0){recorder=sample.GetRecorder();recorder.enabled=true;}
        }
        void Allocate(int w,int h){if(localDepth!=null&&width==w&&height==h)return;ReleaseTargets();width=w;height=h;
            localDepth=new RenderTexture(w,h,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear){name="Local planet weather occlusion",filterMode=FilterMode.Point};localDepth.Create();
            weather=new RenderTexture((w+1)/2,(h+1)/2,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name="Half resolution planetary weather",filterMode=FilterMode.Bilinear};weather.Create();}
        static bool Visible(PlanetDefinition d)=>d!=null&&d.hasAtmosphere&&d.surfacePressure>.001f&&d.atmosphereHeight>0;
        void Insert(PlanetDefinition body,UniversePosition observer,Camera view){if(!Visible(body))return;double distance=body.position.RelativeTo(observer).Magnitude;
            if((body.radius+body.atmosphereHeight)/Math.Max(1,distance)*view.pixelHeight/Math.Tan(view.fieldOfView*Math.PI/360)<1)return;
            int at=0;while(at<VisibleBodies&&distances[at]<distance)at++;if(at==bodies.Length)return;VisibleBodies=Math.Min(bodies.Length,VisibleBodies+1);
            for(int i=VisibleBodies-1;i>at;i--){bodies[i]=bodies[i-1];distances[i]=distances[i-1];}bodies[at]=body;distances[at]=distance;}
        bool Properties(MaterialPropertyBlock block,PlanetDefinition body,UniversePosition observer,Camera view){
            var centre=body.position.RelativeTo(observer).Float;float outer=body.radius+body.atmosphereHeight,z=Vector3.Dot(centre,view.transform.forward),tan=Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f),aspect=(float)width/height;
            if(z+outer<=0)return false;Vector4 bounds=new Vector4(-1,-1,1,1);
            if(centre.magnitude>outer*1.02f&&z>outer){float x=Vector3.Dot(centre,view.transform.right)/(z*tan*aspect),y=Vector3.Dot(centre,view.transform.up)/(z*tan),extent=outer/Mathf.Max(1,z-outer)/tan;
                bounds=new Vector4(Mathf.Max(-1,x-extent/aspect),Mathf.Max(-1,y-extent),Mathf.Min(1,x+extent/aspect),Mathf.Min(1,y+extent));if(bounds.x>=bounds.z||bounds.y>=bounds.w)return false;}
            var settings=body.weather??(body.weather=new PlanetWeather());settings.Layer(body,out float bottom,out float thickness);float altitude=centre.magnitude-body.radius,near=settings.HasClouds?PlanetWeather.NearDetail(altitude,body.atmosphereHeight):0;
            if(near>0)VolumetricBodies++;if(body==world.definition)LocalAtmosphereDrawn=true;
            double clock=world.SystemRuntime?.Civilisation?.RenderTime??world.SystemRuntime?.WeatherTime??world.SimulationTicks*BattleSettings.Tick;float phase=(float)(clock*settings.windSpeed/body.radius%(Math.PI*2));uint seed=PlanetWeather.Hash(unchecked((uint)body.terrainSeed));
            block.Clear();block.SetVector("_WeatherBounds",bounds);block.SetVector("_WeatherCentre",centre/body.radius);block.SetFloat("_WeatherRadius",body.radius);block.SetFloat("_WeatherLocal",body==world.definition?1:0);
            block.SetVector("_WeatherAtmosphere",new Vector4(body.atmosphereHeight/body.radius,settings.haze*Mathf.Sqrt(Mathf.Clamp(body.surfacePressure,0,4)),bottom/body.radius,thickness/body.radius));
            float diameter=body.radius/Mathf.Max(1,centre.magnitude)*height/Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f);
            block.SetFloat("_WeatherNoiseLod",near>0?0:Mathf.Clamp(Mathf.Log(80/Mathf.Max(1,diameter),2),0,4));
            block.SetVector("_WeatherCloud",new Vector4(settings.HasClouds?settings.coverage:0,settings.cloudDensity*body.radius*.0018f,near,settings.HasClouds?1:0));
            block.SetColor("_WeatherAirColour",settings.atmosphereColour.linear);block.SetColor("_WeatherCloudColour",settings.cloudColour.linear);
            block.SetVector("_WeatherWind",new Vector4(Mathf.Sin(phase),Mathf.Cos(phase),seed%997*.037f,(seed>>16)%991*.041f));return true;
        }
        public void Draw(CustomPassContext ctx,ScaledSpaceRenderer distant){
            VisibleBodies=VolumetricBodies=0;LocalAtmosphereDrawn=false;
            if(!Enabled){distant?.Draw(ctx.cmd);return;}
            var camera=world.View;var observer=world.ObserverPosition;Insert(world.definition,observer,camera);if(distant!=null)for(int i=0;i<distant.VisibleCount;i++)Insert(distant.VisibleBody(i),observer,camera);
            if(VisibleBodies==0){distant?.Draw(ctx.cmd);return;}
            Allocate(ctx.hdCamera.actualWidth,ctx.hdCamera.actualHeight);var cmd=ctx.cmd;
            DepthDimensionForValidation=ctx.cameraDepthBuffer.rt.dimension;
            CoreUtils.SetKeyword(material,"WEATHER_DEPTH_ARRAY",DepthDimensionForValidation==TextureDimension.Tex2DArray);
            cmd.BeginSample(sample);cmd.SetGlobalVector("_WeatherFullSize",new Vector4(width,height,1f/width,1f/height));
            // Capture terrain/units before distant bodies write their compressed visual depths.
            cmd.SetRenderTarget(localDepth);cmd.SetViewport(new Rect(0,0,width,height));cmd.SetGlobalTexture("_WeatherSceneDepth",ctx.cameraDepthBuffer,RenderTextureSubElement.Depth);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
            cmd.EndSample(sample);CoreUtils.SetRenderTarget(cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);cmd.SetViewport(new Rect(0,0,width,height));distant?.Draw(cmd);cmd.BeginSample(sample);
            cmd.SetGlobalTexture("_WeatherLocalDepth",localDepth);
            cmd.SetGlobalVector("_WeatherRight",camera.transform.right);cmd.SetGlobalVector("_WeatherUp",camera.transform.up);cmd.SetGlobalVector("_WeatherForward",camera.transform.forward);
            var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);cmd.SetGlobalVector("_WeatherView",new Vector4((float)width/height,Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),vp.m11<0?-1:1,0));
            bool gl=SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLCore||SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLES3;
            cmd.SetGlobalVector("_WeatherDepthProjection",new Vector4(vp.m22,vp.m23,gl?2:1,gl?-1:0));
            var light=world.LightingForValidation?.Sun;cmd.SetGlobalVector("_WeatherSun",light!=null?-light.transform.forward:Vector3.up);
            bool cabin=world.TryWeatherCabin(out var cabinEye,out var cabinRotation);
            cmd.SetGlobalVector("_WeatherCabinEye",new Vector4(cabinEye.x,cabinEye.y,cabinEye.z,cabin?1:0));
            if(cabin){cmd.SetGlobalMatrix("_WeatherWorldToCabin",Matrix4x4.Rotate(cabinRotation));
                cmd.SetGlobalVector("_WeatherAftMin",PlanetSpacecraft.WeatherAft.min);cmd.SetGlobalVector("_WeatherAftMax",PlanetSpacecraft.WeatherAft.max);
                cmd.SetGlobalVector("_WeatherBridgeMin",PlanetSpacecraft.WeatherBridge.min);cmd.SetGlobalVector("_WeatherBridgeMax",PlanetSpacecraft.WeatherBridge.max);}
            cmd.SetRenderTarget(weather);cmd.SetViewport(new Rect(0,0,weather.width,weather.height));cmd.ClearRenderTarget(false,true,Color.clear);cmd.SetGlobalTexture("_WeatherSceneDepth",ctx.cameraDepthBuffer,RenderTextureSubElement.Depth);
            for(int i=VisibleBodies-1;i>=0;i--)if(Properties(blocks[i],bodies[i],observer,camera))cmd.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,6,1,blocks[i]);
            cmd.SetGlobalTexture("_WeatherIntegrated",weather);cmd.SetGlobalVector("_WeatherHalfSize",new Vector4(weather.width,weather.height,1f/weather.width,1f/weather.height));
            CoreUtils.SetRenderTarget(cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);cmd.SetViewport(new Rect(0,0,width,height));cmd.DrawProcedural(Matrix4x4.identity,material,2,MeshTopology.Triangles,3);cmd.EndSample(sample);
        }
        void ReleaseTargets(){if(localDepth!=null){localDepth.Release();ArmyLifetime.Destroy(localDepth);}if(weather!=null){weather.Release();ArmyLifetime.Destroy(weather);}localDepth=weather=null;}
        public void ReleaseView()=>ReleaseTargets();
        public void Dispose(){if(recorder!=null)recorder.enabled=false;ReleaseTargets();ArmyLifetime.Destroy(noise);ArmyLifetime.Destroy(material);}
    }
}
