using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    // Isolated graphics fixture: no planets, army simulation or gameplay objects are allocated.
    public sealed class PlanetAirValidation:MonoBehaviour {
        [Serializable]public sealed class Report {
            public string status="PASS",error="",mode,gpu,scope="Standalone 2560x1440 atmosphere graphics fixture; no army or simulation. Fenced CPU+GPU draw time is not a GPU timestamp.";
            public List<string> checks=new List<string>();public double medianMs,p95Ms,frameMedianMs,frameP95Ms,renderFramesPerSecond,gpuMs;
            public long privateBytes,workingSetBytes,managedBytes,unityAllocatedBytes,unityReservedBytes,graphicsDriverBytes,targetBytes;
            public int samples,runtimeErrors;
        }
        Report report;string output;bool legacy;Material material;Texture2D depth;RenderTexture light,transmission,target,columns;CommandBuffer commands;Recorder recorder;
        readonly CustomSampler sampler=CustomSampler.Create("Atmosphere.Validation",true);
        readonly RenderTargetIdentifier[] drawTargets=new RenderTargetIdentifier[2];
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-atmosphere-validation")>=0)new GameObject("Atmosphere graphics validation").AddComponent<PlanetAirValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-planet-output");output=index>=0?args[index+1]:"TestResults/Planetary/Atmosphere/Current";legacy=Array.IndexOf(args,"-atmosphere-legacy")>=0;report=new Report{mode=legacy?"legacy":"Rayleigh + Mie",gpu=SystemInfo.graphicsDeviceName};Directory.CreateDirectory(output);Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=Log;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
        IEnumerator Start(){var run=Run();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);break;}if(!more)break;yield return next;}File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        RenderTexture RT(int w,int h,string name){var rt=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name=name,filterMode=FilterMode.Point};rt.Create();return rt;}
        void Setup(int w,int h){
            if(light!=null){light.Release();Destroy(light);if(transmission!=null){transmission.Release();Destroy(transmission);}target.Release();Destroy(target);Destroy(depth);}
            light=RT(w/2,h/2,"Validation radiance");if(!legacy)transmission=RT(w/2,h/2,"Validation spectral transmission");target=RT(w,h,"Validation background");drawTargets[0]=light;drawTargets[1]=transmission;
            depth=new Texture2D(w,h,TextureFormat.RFloat,false,true);depth.SetPixelData(new float[w*h],0);depth.Apply(false,true);
            material.SetTexture("_WeatherLocalDepth",depth);material.SetTexture("_WeatherSceneDepth",depth);material.SetVector("_WeatherFullSize",new Vector4(w,h,1f/w,1f/h));material.SetVector("_WeatherHalfSize",new Vector4(w/2,h/2,2f/w,2f/h));
            material.SetTexture("_WeatherIntegrated",light);material.SetTexture("_WeatherTransmission",transmission);
        }
        void Pose(Vector3 eye,Vector3 forward,Vector3 sun){
            forward.Normalize();var right=Vector3.Cross(Mathf.Abs(forward.y)>.99f?Vector3.forward:Vector3.up,forward).normalized;var up=Vector3.Cross(forward,right);
            material.SetVector("_WeatherCentre",-eye);material.SetFloat("_WeatherEyeAltitude",eye.magnitude-1);material.SetVector("_WeatherForward",forward);material.SetVector("_WeatherRight",right);material.SetVector("_WeatherUp",up);material.SetVector("_WeatherSun",sun.normalized);
        }
        void Draw(bool fence=true){
            commands.Clear();commands.BeginSample(sampler);commands.SetRenderTarget(light);commands.ClearRenderTarget(false,true,Color.clear);
            if(!legacy){commands.SetRenderTarget(transmission);commands.ClearRenderTarget(false,true,Color.white);commands.SetRenderTarget(drawTargets,BuiltinRenderTextureType.None);}
            commands.SetViewport(new Rect(0,0,light.width,light.height));commands.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,6);
            commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,target.width,target.height));commands.ClearRenderTarget(false,true,new Color(.02f,.02f,.02f,1));
            if(!legacy){commands.DrawProcedural(Matrix4x4.identity,material,3,MeshTopology.Triangles,3);commands.DrawProcedural(Matrix4x4.identity,material,4,MeshTopology.Triangles,3);}else commands.DrawProcedural(Matrix4x4.identity,material,2,MeshTopology.Triangles,3);
            commands.EndSample(sampler);Graphics.ExecuteCommandBuffer(commands);
            if(fence){var read=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1,TextureFormat.RGBAFloat);read.WaitForCompletion();CheckRead(read);}
        }
        static void CheckRead(AsyncGPUReadbackRequest read){if(read.hasError)throw new InvalidOperationException("Atmosphere GPU readback failed");}
        Color Centre(RenderTexture texture){var read=AsyncGPUReadback.Request(texture,0,texture.width/2,1,texture.height/2,1,0,1,TextureFormat.RGBAFloat);read.WaitForCompletion();CheckRead(read);return read.GetData<Color>()[0];}
        void Capture(string name){var read=AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32);read.WaitForCompletion();CheckRead(read);var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);image.LoadRawTextureData(read.GetData<byte>());image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
        void BuildColumns(){
            columns=new RenderTexture(128,64,0,RenderTextureFormat.RGFloat,RenderTextureReadWrite.Linear){dimension=TextureDimension.Tex2DArray,volumeDepth=17,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};columns.Create();
            var compute=Resources.Load<ComputeShader>("MassiveArmy/PlanetAirOpticalDepth");compute.SetFloat("_AirHeight",.08333333f);compute.SetInt("_AirSlice",0);compute.SetTexture(0,"_AirColumnsWrite",columns);compute.Dispatch(0,16,8,1);material.SetTexture("_AirColumns",columns);
            var read=AsyncGPUReadback.Request(columns,0);read.WaitForCompletion();CheckRead(read);var values=read.GetData<Vector2>();
            foreach(int y in new[]{0,4,16,40,63})foreach(int x in new[]{0,4,16,64,127}){
                double height=.08333333,alt=height*y*y/(63d*63),r=1+alt,horizon=-Math.Sqrt(alt*(2+alt))/r,mu=horizon+(1-horizon)*x*x/(127d*127),b=r*mu,c=(alt-height)*(2+alt+height),distance=Math.Max(0,-b+Math.Sqrt(Math.Max(0,b*b-c)));
                var actual=values[y*128+x];double[] expected=new double[2];const int count=8192;
                for(int i=0;i<count;i++){double t=(i+.5)*distance/count,h=Math.Max(0,Math.Sqrt(r*r+2*r*mu*t+t*t)-1);for(int j=0;j<2;j++){double scale=height*(j==0?.125:.01875);expected[j]+=Math.Exp(-h/scale)*distance/count/scale;}}
                Check(Math.Abs(actual.x-expected[0])<.08*Math.Max(.02,expected[0])&&Math.Abs(actual.y-expected[1])<.08*Math.Max(.02,expected[1]),"GPU sunlight density columns agree with independent 8192-step reference at "+x+","+y);
            }
        }
        void Configure(){
            material.SetVector("_WeatherBounds",new Vector4(-1,-1,1,1));material.SetVector("_WeatherView",new Vector4(16f/9,Mathf.Tan(30*Mathf.Deg2Rad),1,0));
            material.SetVector("_WeatherAtmosphere",new Vector4(.08333333f,.45f,.018f,.013f));material.SetFloat("_WeatherRadius",120000);material.SetFloat("_WeatherPrecisionEnabled",1);
            material.SetVector("_AirScattering",legacy?Vector4.zero:new Vector4(1,0,1,1));material.SetVector("_WeatherSunColour",Vector3.one);material.SetColor("_WeatherAirColour",new Color(.18f,.4f,.85f).linear);
            material.SetVector("_WeatherCloud",Vector4.zero);material.SetVector("_WeatherCloudColour",Vector3.one);material.SetVector("_WeatherLighting",new Vector4(.85f,0,0,1));material.SetVector("_WeatherLightEffects",new Vector4(.65f,.55f,.28f,1));material.SetVector("_WeatherCloudLod",new Vector4(12,1.65f,0,0));material.SetVector("_WeatherWind",new Vector4(0,1,1,1));material.SetVector("_WeatherDetailMotion",new Vector4(0,1,.25f,.4f));material.SetVector("_WeatherNoiseTuning",new Vector4(.45f,0,2,.3f));
        }
        void CheckPhysics(){
            var eye=new Vector3(0,1.0001f,0);Pose(eye,Vector3.up,new Vector3(.8f,.6f,0));Draw();var day=Centre(light);var clear=Centre(transmission);Check(day.b>day.r*1.4f&&day.b>day.g,"Daytime zenith is blue from wavelength-dependent scattering");Check(clear.b<clear.g&&clear.g<clear.r,"Blue light is extinguished more strongly than red along the view ray");var composed=Centre(target);Check(Mathf.Abs(composed.r-day.r-clear.r*.02f)<.005f&&Mathf.Abs(composed.g-day.g-clear.g*.02f)<.005f&&Mathf.Abs(composed.b-day.b-clear.b*.02f)<.005f,"RGB extinction and in-scattering composite onto the scene in the correct order");Capture("daytime-zenith");
            Pose(eye,new Vector3(1,.025f,0),new Vector3(1,.001f,0));Draw();var sunset=Centre(light);Check(sunset.r>sunset.b&&sunset.r>.001f,"Long sunset sunlight paths redden the horizon");Capture("sunset-horizon");
            Pose(eye,Vector3.up,Vector3.down);Draw();var night=Centre(light);Check(night.r+night.g+night.b<.0001f,"Planet occlusion removes direct atmospheric scattering on the night side");Capture("night");
            Pose(eye,Vector3.up,Vector3.up);material.SetVector("_AirScattering",new Vector4(1,0,4,4));Draw();var dense=Centre(transmission);Check(dense.r<clear.r&&dense.b<clear.b,"Higher atmospheric density reduces transmission");
            material.SetVector("_AirScattering",new Vector4(1,0,0,0));Draw();var vacuum=Centre(light);var transparent=Centre(transmission);Check(vacuum.r+vacuum.g+vacuum.b<.0001f&&transparent.r>.999f&&transparent.g>.999f&&transparent.b>.999f,"Zero scattering leaves a transparent atmosphere without created light");
            material.SetVector("_AirScattering",new Vector4(1,0,1,0));Draw();var noAerosol=Centre(light);material.SetVector("_AirScattering",new Vector4(1,0,1,1));Draw();Check(Centre(light).r>noAerosol.r,"Aerosols produce forward-scattered sunlight around the star");
            // Sealed foreground: the exterior integration starts after the wall depth.
            material.SetVector("_WeatherCabinEye",new Vector4(0,0,0,1));material.SetMatrix("_WeatherWorldToCabin",Matrix4x4.identity);material.SetVector("_WeatherAftMin",new Vector3(-200000,-200000,-200000));material.SetVector("_WeatherAftMax",new Vector3(200000,200000,200000));material.SetVector("_WeatherBridgeMin",Vector3.one*-200000);material.SetVector("_WeatherBridgeMax",Vector3.one*200000);Draw();Check(Centre(light).r<.0001f&&Centre(transmission).r>.999f,"Sealed cabin exclusion prevents atmospheric scattering inside the hull");material.SetVector("_WeatherCabinEye",Vector4.zero);
            Pose(new Vector3(0,0,-2.2f),new Vector3(.45f,0,1),Vector3.right);Draw();var orbit=Centre(light);Check(float.IsFinite(orbit.r)&&float.IsFinite(orbit.b),"Orbital limb integration remains finite");Capture("orbital-limb");
        }
        IEnumerator Run(){
            commands=new CommandBuffer{name="Atmosphere validation"};var shader=Resources.Load<Shader>(legacy?"MassiveArmy/PlanetWeatherLegacyValidation":"MassiveArmy/PlanetWeather");Check(shader!=null,"Atmosphere shader asset is available");material=new Material(shader);Configure();Setup(512,288);if(!legacy){BuildColumns();CheckPhysics();}
            // Same camera, density, cloud field, resolution and draw work in each mode.
            Setup(2560,1440);Configure();Pose(new Vector3(0,1.0001f,0),new Vector3(1,.12f,0),new Vector3(1,.3f,0));
            var noise=new Texture3D(2,2,2,TextureFormat.RFloat,false);noise.SetPixels(Enumerable.Repeat(Color.white,8).ToArray());noise.Apply();material.SetTexture("_WeatherNoise",noise);material.SetFloat("_WeatherNoiseSizeScale",1);material.SetFloat("_WeatherNoiseFrequency",1);material.SetVector("_WeatherCloud",new Vector4(.65f,216,1,1));
            recorder=sampler.GetRecorder();recorder.enabled=true;var times=new List<double>();var frames=new List<double>();var watch=new Stopwatch();
            for(int i=0;i<160;i++){watch.Restart();Draw();double elapsed=watch.Elapsed.TotalMilliseconds;if(i>=40){times.Add(elapsed);frames.Add(Time.unscaledDeltaTime*1000);}yield return null;}
            times.Sort();frames.Sort();report.samples=times.Count;report.medianMs=times[times.Count/2];report.p95Ms=times[(int)((times.Count-1)*.95)];report.frameMedianMs=frames[frames.Count/2];report.frameP95Ms=frames[(int)((frames.Count-1)*.95)];report.renderFramesPerSecond=1000/frames.Average();report.gpuMs=recorder.gpuSampleBlockCount>0?recorder.gpuElapsedNanoseconds/1e6:0;
            var process=Process.GetCurrentProcess();report.privateBytes=process.PrivateMemorySize64;report.workingSetBytes=process.WorkingSet64;report.managedBytes=GC.GetTotalMemory(false);report.unityAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.unityReservedBytes=Profiler.GetTotalReservedMemoryLong();report.graphicsDriverBytes=Profiler.GetAllocatedMemoryForGraphicsDriver();report.targetBytes=2560L*1440*8+1280L*720*8+2560L*1440*4+(legacy?0:1280L*720*8+128L*64*17*8);
            Check(report.runtimeErrors==0,"No runtime or shader errors");Capture("near-cloud-benchmark");Destroy(noise);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(recorder!=null)recorder.enabled=false;commands?.Dispose();foreach(var rt in new[]{light,transmission,target,columns})if(rt!=null){rt.Release();Destroy(rt);}Destroy(depth);Destroy(material);}
    }
}



