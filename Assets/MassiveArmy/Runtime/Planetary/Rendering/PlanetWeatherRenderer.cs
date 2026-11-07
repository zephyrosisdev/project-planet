using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Profiling;
namespace MassiveArmy.Planetary
{
    // View-only weather. One shared noise volume, bounded body draws and half-resolution integration;
    // unvisited planets receive no weather simulation, terrain allocations or army state.
    // Scheduling metadata only; density, shadows and cache storage stay on GPU.
    internal sealed class CloudLightRefresh {
        int signature,width,height,depth,nextRow;bool initialized;double nextRefresh;
        internal bool Valid {get;private set;}
        internal void Reset(){initialized=false;Valid=false;nextRow=0;nextRefresh=0;}
        internal int Take(int key,int w,int h,int d,double now,float interval,int budget,bool motion,out int offset,out bool capture){
            offset=0;capture=false;
            if(!initialized||key!=signature||width!=w||height!=h||depth!=d){signature=key;width=w;height=h;depth=d;initialized=true;Valid=false;nextRow=0;nextRefresh=0;}
            if(nextRow==0){if(Valid&&(now<nextRefresh||!motion))return 0;capture=true;}
            int rows=Math.Max(1,Math.Min(h,Math.Max(1,budget)/Math.Max(1,w*d)));offset=nextRow;rows=Math.Min(rows,h-nextRow);nextRow+=rows;
            if(nextRow>=h){nextRow=0;Valid=true;nextRefresh=now+Math.Max(0,interval);}
            return rows;
        }
    }
    public sealed partial class PlanetWeatherRenderer : IDisposable
    {
        readonly PlanetDevelopment world;readonly Material material;readonly Texture3D noise;readonly ComputeShader lightCompute;
        readonly PlanetDefinition[] bodies=new PlanetDefinition[17];readonly double[] distances=new double[17];
        readonly MaterialPropertyBlock[] blocks=new MaterialPropertyBlock[17];
        readonly CustomSampler sample=GpuSamples.Get("Planet.Weather"),lightSample=GpuSamples.Get("Planet.Weather.CloudLightCache");readonly Recorder recorder;
        static readonly string[] LightParameters={"_WeatherAtmosphere","_WeatherCloud","_WeatherWind","_WeatherDetailMotion","_WeatherNoiseTuning","_WeatherOctaves"};
        readonly CloudLightRefresh lightRefresh=new CloudLightRefresh();
        readonly Vector4[] cachedLightParameters=new Vector4[6];Vector3 cachedSun;float cachedRadius,cachedNoiseSize,cachedNoiseFrequency,cachedNativeDouble;
        RenderTexture localDepth,weather,lightCache;int width,height;
        public bool Enabled=true;
        internal bool ScatteringEnabled=true,ShadowsEnabled=true;
        internal RenderTexture LightForValidation=>lightCache;
        internal RenderTexture DepthForValidation=>localDepth;
        internal TextureDimension DepthDimensionForValidation {get;private set;}
        public int VisibleBodies {get;private set;}
        public int VolumetricBodies {get;private set;}
        public bool LocalAtmosphereDrawn {get;private set;}
        public long TextureBytes=>(localDepth!=null?(long)width*height*4+(long)weather.width*weather.height*8:0)+299593+(lightCache!=null?(long)lightCache.width*lightCache.height*lightCache.volumeDepth*4:0)+AirTextureBytes;
        public double GpuMilliseconds=>recorder!=null&&recorder.gpuSampleBlockCount>0?recorder.gpuElapsedNanoseconds/1e6:0;
        public PlanetWeatherRenderer(PlanetDevelopment world){
            this.world=world;material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetWeather"));
            lightCompute=Resources.Load<ComputeShader>("MassiveArmy/PlanetWeatherLight");
            var data=new byte[64*64*64];for(int i=0;i<data.Length;i++)data[i]=(byte)PlanetWeather.Hash((uint)i+8317);
            noise=new Texture3D(64,64,64,TextureFormat.R8,true){name="Shared seamless weather density",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};noise.SetPixelData(data,0);noise.Apply(true,true);material.SetTexture("_WeatherNoise",noise);
            for(int i=0;i<blocks.Length;i++)blocks[i]=new MaterialPropertyBlock();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-weather-validation")>=0){recorder=sample.GetRecorder();recorder.enabled=true;}
        }
        void Allocate(int w,int h){
            var settings=world.definition.weather;int rw=Mathf.CeilToInt(w*settings.EffectiveRenderScale),rh=Mathf.CeilToInt(h*settings.EffectiveRenderScale);
            // Camera/dynamic-resolution changes must not discard the planet-space cache.
            if(localDepth==null||width!=w||height!=h||weather.width!=rw||weather.height!=rh){
                if(localDepth!=null){localDepth.Release();ArmyLifetime.Destroy(localDepth);}if(weather!=null){weather.Release();ArmyLifetime.Destroy(weather);}width=w;height=h;
                localDepth=new RenderTexture(w,h,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear){name="Local planet weather occlusion",filterMode=FilterMode.Point};localDepth.Create();
                weather=new RenderTexture(rw,rh,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name="Scalable resolution planetary weather",filterMode=FilterMode.Bilinear};weather.Create();
            }
            if(lightCache==null||lightCache.width!=settings.ShadowWidth||lightCache.volumeDepth!=settings.ShadowLayers){
                if(lightCache!=null){lightCache.Release();ArmyLifetime.Destroy(lightCache);}lightRefresh.Reset();
                lightCache=new RenderTexture(settings.ShadowWidth,settings.ShadowWidth/2,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear){name="Planet cloud sunlight optical depth",dimension=TextureDimension.Tex3D,volumeDepth=settings.ShadowLayers,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,wrapModeW=TextureWrapMode.Clamp};lightCache.Create();
            }
        }
        static bool Visible(PlanetDefinition d)=>d!=null&&d.hasAtmosphere&&d.surfacePressure>.001f&&d.atmosphereHeight>0;
        void Insert(PlanetDefinition body,UniversePosition observer,Camera view){if(!Visible(body))return;double distance=body.position.RelativeTo(observer).Magnitude;
            if((body.radius+Mathf.Max(body.atmosphereHeight,body.weather!=null&&body.weather.HasClouds?body.weather.cloudAltitude+body.weather.cloudThickness:0))/Math.Max(1,distance)*view.pixelHeight/Math.Tan(view.fieldOfView*Math.PI/360)<1)return;
            int at=0;while(at<VisibleBodies&&distances[at]<distance)at++;if(at==bodies.Length)return;VisibleBodies=Math.Min(bodies.Length,VisibleBodies+1);
            for(int i=VisibleBodies-1;i>at;i--){bodies[i]=bodies[i-1];distances[i]=distances[i-1];}bodies[at]=body;distances[at]=distance;}
        bool Properties(MaterialPropertyBlock block,PlanetDefinition body,UniversePosition observer,Camera view){
            var settings=body.weather??(body.weather=new PlanetWeather());settings.Layer(body,out float bottom,out float thickness);
            var eye=observer.RelativeTo(body.position);var centre=body.position.RelativeTo(observer).Float;float outer=body.radius+Mathf.Max(body.atmosphereHeight,settings.HasClouds?bottom+thickness:0),z=Vector3.Dot(centre,view.transform.forward),tan=Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f),aspect=(float)width/height;
            var viewCentre=new Vector3(Vector3.Dot(centre,view.transform.right),Vector3.Dot(centre,view.transform.up),z);
            if(!PlanetWeatherProjection.Bounds(viewCentre,outer,tan,aspect,out var bounds))return false;
            float diameter=body.radius/Mathf.Max(1,centre.magnitude)*height/Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f);
            double altitude=eye.Magnitude-body.radius;
            float cloudDistance=(float)Math.Max(0,Math.Max(bottom-altitude,altitude-bottom-thickness));
            float near=settings.HasClouds?settings.Detail(cloudDistance,body.atmosphereHeight,diameter,height):0;
            // Large on-screen planets retain the low march tier regardless of
            // distance; non-resident worlds still cap their cost at four samples.
            if(near>0)VolumetricBodies++;if(body==world.definition)LocalAtmosphereDrawn=true;
            double clock=world.SystemRuntime?.Civilisation?.RenderTime??world.SystemRuntime?.WeatherTime??world.SimulationTicks*BattleSettings.Tick;double phase=clock*settings.windSpeed/body.radius%(Math.PI*2);uint seed=PlanetWeather.Hash(unchecked((uint)body.terrainSeed));
            block.Clear();block.SetFloat("_WeatherNativeDouble",world.NativePlanetDoublesFor(body)?1:0);block.SetFloat("_WeatherPrecisionEnabled",1);block.SetVector("_WeatherLightSize",new Vector4(lightCache.width,lightCache.height,lightCache.volumeDepth,0));block.SetFloat("_WeatherEyeAltitude",(float)((body.position.RelativeTo(observer).Magnitude-body.radius)/body.radius));block.SetVector("_WeatherBounds",bounds);block.SetVector("_WeatherCentre",centre/body.radius);block.SetFloat("_WeatherRadius",body.radius);block.SetFloat("_WeatherLocal",body==world.definition?1:0);
            block.SetVector("_WeatherAtmosphere",new Vector4(body.atmosphereHeight/body.radius,settings.haze*Mathf.Sqrt(Mathf.Clamp(body.surfacePressure,0,4)),bottom/body.radius,thickness/body.radius));
            SetAirProperties(block,body);
            // Preserve broad cloud coverage across detail changes. Only erosion
            // loses high-frequency noise as the ray-march budget decreases.
            block.SetFloat("_WeatherNoiseLod",Mathf.Clamp(Mathf.Log(80/Mathf.Max(1,diameter),2),0,4));
            block.SetFloat("_WeatherDetailNoiseLod",body==world.definition?(1-near)*2:Mathf.Max(1,(1-near)*2));
            block.SetFloat("_WeatherNoiseSizeScale",(world.SystemRuntime?.Settings.CloudNoiseSizeScale??PlanetSystemDefinition.AutomaticCloudNoiseScale(world.systemSizeMultiplier))*Mathf.Max(.05f,settings.noiseFeatureSize));
            block.SetVector("_WeatherCloudLod",new Vector4(body==world.definition?world.definition.weather.EffectiveMarchSteps:diameter>height*.2f?8:4,Mathf.Max(.5f,settings.internalShadowContrast),0,0));
            block.SetVector("_WeatherCloud",new Vector4(settings.HasClouds?settings.coverage:0,settings.cloudDensity*body.radius*.0018f,near,settings.HasClouds?1:0));
            var sun=world.LightingForValidation?.Sun;float star=sun!=null&&sun.enabled&&sun.gameObject.activeInHierarchy?Mathf.Clamp01(sun.intensity/10000):0;
            block.SetVector("_WeatherLighting",new Vector4(ScatteringEnabled?settings.lightScattering:0,ShadowsEnabled?Mathf.Min(.6f,settings.cloudShadowStrength*1.5f)*star:0,0,star));
            block.SetVector("_WeatherLightEffects",new Vector4(settings.forwardScattering,settings.multipleScattering,settings.interiorAmbient,settings.cloudBrightness));
            block.SetColor("_WeatherAirColour",settings.atmosphereColour.linear);block.SetColor("_WeatherCloudColour",settings.cloudColour.linear);
            double detailPhase=-clock*settings.windSpeed/body.radius*settings.detailCounterDrift%(Math.PI*2);
            double sn=Math.Sin(phase+detailPhase),cs=Math.Cos(phase+detailPhase);
            double tile=Mathf.Max(1,settings.localDetailFeatureSize)*64;
            float Wrap(double value)=>(float)(value-Math.Floor(value/tile)*tile);
            block.SetVector("_WeatherLocalAnchor",new Vector4(Wrap(eye.x*cs-eye.z*sn),Wrap(eye.y),Wrap(eye.x*sn+eye.z*cs),0));
            block.SetVector("_WeatherLocalRotation",new Vector4((float)sn,(float)cs,0,0));
            block.SetVector("_WeatherAdaptiveDetail",new Vector4(body==world.definition?settings.LocalDetailBlend(cloudDistance):0,Mathf.Max(1,settings.localDetailFeatureSize),0,0));
            block.SetVector("_WeatherOctaves",new Vector4(Mathf.Clamp(settings.detailNoiseOctaves,2,16),settings.octavePersistence,settings.octaveLacunarity,0));
            block.SetVector("_WeatherNoiseTuning",new Vector4(settings.detailErosion,settings.fineNoiseStrength,settings.fineNoiseFrequency,settings.noiseContrast));
            block.SetFloat("_WeatherNoiseFrequency",settings.detailNoiseFrequency);
            block.SetVector("_WeatherDetailMotion",new Vector4((float)Math.Sin(detailPhase),(float)Math.Cos(detailPhase),settings.interiorSoftness,settings.interiorExtinction));
            block.SetVector("_WeatherWind",new Vector4((float)Math.Sin(phase),(float)Math.Cos(phase),seed%997*.037f,(seed>>16)%991*.041f));return true;
        }
        int LightSignature(MaterialPropertyBlock block){unchecked{int hash=17;foreach(string name in LightParameters){var value=block.GetVector(name);if(name=="_WeatherWind"||name=="_WeatherDetailMotion")value.x=value.y=0;if(name=="_WeatherCloud")value.z=0;hash=hash*31+value.GetHashCode();}hash=hash*31+world.definition.radius.GetHashCode();hash=hash*31+block.GetFloat("_WeatherNativeDouble").GetHashCode();hash=hash*31+block.GetFloat("_WeatherNoiseSizeScale").GetHashCode();return hash*31+block.GetFloat("_WeatherNoiseFrequency").GetHashCode();}}
        void BuildLight(CommandBuffer cmd,MaterialPropertyBlock block){
            var lighting=block.GetVector("_WeatherLighting");if(lighting.x<=0&&lighting.y<=0)return;
            var sun=world.LightingForValidation?.Sun;var direction=sun!=null?-sun.transform.forward:Vector3.up;
            if(cachedSun.sqrMagnitude>.5f&&Vector3.Dot(direction,cachedSun)<.9961947f)lightRefresh.Reset();
            bool motion=block.GetVector("_WeatherWind")!=cachedLightParameters[2]||block.GetVector("_WeatherDetailMotion")!=cachedLightParameters[3]||direction!=cachedSun;
            var settings=world.definition.weather;
            int rows=lightRefresh.Take(LightSignature(block),lightCache.width,lightCache.height,lightCache.volumeDepth,Time.unscaledTimeAsDouble,settings.shadowRefreshInterval,settings.shadowUpdateVoxelsPerFrame,motion,out int offset,out bool capture);
            if(rows>0){
                // Hold one density/sun snapshot across the sweep. Only a bounded row
                // strip is rewritten; reuse the same texture, with no second large cache.
                if(capture){for(int i=0;i<LightParameters.Length;i++)cachedLightParameters[i]=block.GetVector(LightParameters[i]);cachedSun=direction;cachedRadius=world.definition.radius;cachedNoiseSize=block.GetFloat("_WeatherNoiseSizeScale");cachedNoiseFrequency=block.GetFloat("_WeatherNoiseFrequency");cachedNativeDouble=block.GetFloat("_WeatherNativeDouble");}
                cmd.SetComputeFloatParam(lightCompute,"_WeatherNativeDouble",cachedNativeDouble);
                for(int i=0;i<LightParameters.Length;i++)cmd.SetComputeVectorParam(lightCompute,LightParameters[i],cachedLightParameters[i]);
                cmd.SetComputeFloatParam(lightCompute,"_WeatherRadius",cachedRadius);cmd.SetComputeFloatParam(lightCompute,"_WeatherNoiseLod",0);cmd.SetComputeFloatParam(lightCompute,"_WeatherDetailNoiseLod",0);
                cmd.SetComputeFloatParam(lightCompute,"_WeatherNoiseSizeScale",cachedNoiseSize);cmd.SetComputeFloatParam(lightCompute,"_WeatherNoiseFrequency",cachedNoiseFrequency);cmd.SetComputeVectorParam(lightCompute,"_WeatherSun",cachedSun);
                cmd.SetComputeTextureParam(lightCompute,0,"_WeatherNoise",noise);cmd.SetComputeTextureParam(lightCompute,0,"_WeatherLightWrite",lightCache);cmd.SetComputeVectorParam(lightCompute,"_WeatherLightSize",block.GetVector("_WeatherLightSize"));
                cmd.SetComputeIntParam(lightCompute,"_WeatherLightRowOffset",offset);cmd.SetComputeIntParam(lightCompute,"_WeatherLightRowCount",rows);
                cmd.DispatchCompute(lightCompute,0,(lightCache.width+7)/8,(rows+7)/8,lightCache.volumeDepth);
            }
            // The analytic path supplies lighting until every row has been initialized.
            lighting.z=lightRefresh.Valid?1:0;block.SetVector("_WeatherLighting",lighting);
        }
        public void Draw(CustomPassContext ctx,ScaledSpaceRenderer distant){
            bool doubles=world.NativePlanetDoubles;var systemBodies=world.SystemRuntime?.Bodies;if(systemBodies!=null)foreach(var body in systemBodies)doubles|=world.NativePlanetDoublesFor(body);
            if(doubles){material.EnableKeyword("PLANET_GPU_DOUBLE");lightCompute.EnableKeyword("PLANET_GPU_DOUBLE");}else{material.DisableKeyword("PLANET_GPU_DOUBLE");lightCompute.DisableKeyword("PLANET_GPU_DOUBLE");}
            VisibleBodies=VolumetricBodies=0;LocalAtmosphereDrawn=false;
            if(!Enabled){distant?.Draw(ctx.cmd);return;}
            var camera=world.View;var observer=world.ObserverPosition;Insert(world.definition,observer,camera);if(distant!=null)for(int i=0;i<distant.VisibleCount;i++)Insert(distant.VisibleBody(i),observer,camera);
            if(VisibleBodies==0){distant?.Draw(ctx.cmd);return;}
            Allocate(ctx.hdCamera.actualWidth,ctx.hdCamera.actualHeight);var cmd=ctx.cmd;
            // Full-screen weather owns its viewport. Do not inherit a clipped
            // rectangle from a preceding pass or an Editor repaint.
            cmd.DisableScissorRect();
            PrepareAir(cmd);
            DepthDimensionForValidation=ctx.cameraDepthBuffer.rt.dimension;
            CoreUtils.SetKeyword(material,"WEATHER_DEPTH_ARRAY",DepthDimensionForValidation==TextureDimension.Tex2DArray);
            cmd.BeginSample(sample);cmd.SetGlobalVector("_WeatherHalfSize",new Vector4(weather.width,weather.height,1f/weather.width,1f/weather.height));cmd.SetGlobalVector("_WeatherFullSize",new Vector4(width,height,1f/width,1f/height));
            // Capture terrain/units before distant bodies write their compressed visual depths.
            cmd.SetRenderTarget(localDepth);cmd.SetViewport(new Rect(0,0,width,height));cmd.SetGlobalTexture("_WeatherSceneDepth",ctx.cameraDepthBuffer,RenderTextureSubElement.Depth);cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
            cmd.EndSample(sample);CoreUtils.SetRenderTarget(cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);cmd.SetViewport(new Rect(0,0,width,height));distant?.Draw(cmd);cmd.BeginSample(sample);
            cmd.SetGlobalTexture("_WeatherLocalDepth",localDepth);
            cmd.SetGlobalVector("_WeatherRight",camera.transform.right);cmd.SetGlobalVector("_WeatherUp",camera.transform.up);cmd.SetGlobalVector("_WeatherForward",camera.transform.forward);
            // Decode the depth actually written by HDRP, including render-request
            // and camera-history projection changes, rather than recalculating it.
            var vp=ctx.hdCamera.mainViewConstants.projMatrix;cmd.SetGlobalVector("_WeatherView",new Vector4((float)width/height,Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f),vp.m11<0?-1:1,0));
            bool gl=SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLCore||SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLES3;
            cmd.SetGlobalVector("_WeatherDepthProjection",new Vector4(vp.m22,vp.m23,gl?2:1,gl?-1:0));
            var light=world.LightingForValidation?.Sun;cmd.SetGlobalVector("_WeatherSun",light!=null?-light.transform.forward:Vector3.up);
            float power=light!=null&&light.enabled&&light.gameObject.activeInHierarchy?Mathf.Clamp01(light.intensity/10000):0;
            cmd.SetGlobalVector("_WeatherSunColour",light!=null?(Vector4)(light.color.linear*power):Vector4.zero);cmd.SetGlobalTexture("_WeatherLightCache",lightCache);
            bool cabin=world.TryWeatherCabin(out var cabinEye,out var cabinRotation);
            cmd.SetGlobalVector("_WeatherCabinEye",new Vector4(cabinEye.x,cabinEye.y,cabinEye.z,cabin?1:0));
            if(cabin){world.ShipForValidation.CabinBounds(cabinEye,out var aftBounds,out var bridgeBounds);cmd.SetGlobalMatrix("_WeatherWorldToCabin",Matrix4x4.Rotate(cabinRotation));
                cmd.SetGlobalVector("_WeatherAftMin",aftBounds.min);cmd.SetGlobalVector("_WeatherAftMax",aftBounds.max);
                cmd.SetGlobalVector("_WeatherBridgeMin",bridgeBounds.min);cmd.SetGlobalVector("_WeatherBridgeMax",bridgeBounds.max);}
            cmd.SetRenderTarget(weather);cmd.SetViewport(new Rect(0,0,weather.width,weather.height));cmd.ClearRenderTarget(false,true,Color.clear);
            if(airTransmission!=null){cmd.SetRenderTarget(airTransmission);cmd.ClearRenderTarget(false,true,Color.white);cmd.SetRenderTarget(airTargets,BuiltinRenderTextureType.None);}
            cmd.SetGlobalTexture("_WeatherSceneDepth",ctx.cameraDepthBuffer,RenderTextureSubElement.Depth);
            for(int i=VisibleBodies-1;i>=0;i--)if(Properties(blocks[i],bodies[i],observer,camera)){
                if(bodies[i]==world.definition&&bodies[i].weather.HasClouds){cmd.BeginSample(lightSample);BuildLight(cmd,blocks[i]);cmd.EndSample(lightSample);}
                cmd.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,6,1,blocks[i]);}
            cmd.SetGlobalTexture("_WeatherIntegrated",weather);cmd.SetGlobalVector("_WeatherHalfSize",new Vector4(weather.width,weather.height,1f/weather.width,1f/weather.height));
            CoreUtils.SetRenderTarget(cmd,ctx.cameraColorBuffer,ctx.cameraDepthBuffer,ClearFlag.None);cmd.SetViewport(new Rect(0,0,width,height));
            if(airTransmission!=null){cmd.SetGlobalTexture("_WeatherTransmission",airTransmission);cmd.DrawProcedural(Matrix4x4.identity,material,3,MeshTopology.Triangles,3);cmd.DrawProcedural(Matrix4x4.identity,material,4,MeshTopology.Triangles,3);}
            else cmd.DrawProcedural(Matrix4x4.identity,material,2,MeshTopology.Triangles,3);cmd.EndSample(sample);
#if UNITY_EDITOR
            TraceWeather(ctx,vp,cabin,cabinEye);
#endif
        }
        void ReleaseTargets(){lightRefresh.Reset();if(localDepth!=null){localDepth.Release();ArmyLifetime.Destroy(localDepth);}if(weather!=null){weather.Release();ArmyLifetime.Destroy(weather);}if(lightCache!=null){lightCache.Release();ArmyLifetime.Destroy(lightCache);}localDepth=weather=lightCache=null;}
        public void ReleaseView(){ReleaseTargets();ReleaseAir();}
        public void Dispose(){
#if UNITY_EDITOR
            traceDisposed=true;
#endif
            if(recorder!=null)recorder.enabled=false;ReleaseTargets();ReleaseAir();ArmyLifetime.Destroy(noise);ArmyLifetime.Destroy(material);}
    }
}
