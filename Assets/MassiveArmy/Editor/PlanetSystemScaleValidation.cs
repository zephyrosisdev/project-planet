using System;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class PlanetSystemScaleValidation
    {
        [Serializable] sealed class Report{public string status="PASS",error="";public int checks;}
        [MenuItem("Synthetic Front/Planets/Validate system size multipliers")]
        public static void Run(){
            var report=new Report();void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);report.checks++;}
            var source=ScriptableObject.CreateInstance<PlanetSystemDefinition>();var stars=ScriptableObject.CreateInstance<GalaxyDefinition>();source.galaxy=stars;stars.starSystems=2;stars.planetsPerSystem=2;
            var baselineHome=ScriptableObject.CreateInstance<PlanetDefinition>();baselineHome.radius=120000;baselineHome.gravitationalParameter=9.81*120000*120000;
            PlanetDefinition[] baselineBodies=null;
            try{
                Check(PlanetWeather.RenderScale(PlanetCloudQuality.Medium)==.5f&&PlanetWeather.MarchSteps(PlanetCloudQuality.Medium)==12,"Default cloud quality retains existing resolution and march budget");
                Check(PlanetWeather.RenderScale(PlanetCloudQuality.High)==.75f&&PlanetWeather.MarchSteps(PlanetCloudQuality.High)==16&&PlanetWeather.RenderScale(PlanetCloudQuality.Ultra)==1&&PlanetWeather.MarchSteps(PlanetCloudQuality.Ultra)==24,"High and Ultra cloud resolution and march budgets are explicit");
                Check(PlanetPrecisionPolicy.Select(500)==PlanetPrecisionMode.CameraRelative&&PlanetPrecisionPolicy.Select(120000)==PlanetPrecisionMode.Compensated&&PlanetPrecisionPolicy.Select(120000000)==PlanetPrecisionMode.ExtendedLocal,"Precision mode is selected independently from each body's radius");
                var preciseBody=ScriptableObject.CreateInstance<PlanetDefinition>();
                try{
                    preciseBody.radius=120000000;preciseBody.terrainHeight=0;preciseBody.oceanHeight=-1;var surface=new PlanetSurface(preciseBody);
                    var direction=new Double3(.371,.623,.691);direction=direction*(1/direction.Magnitude);var position=direction*preciseBody.radius;
                    var tangent=Double3.Cross(direction,new Double3(0,1,0));tangent=tangent*(1/tangent.Magnitude);
                    for(int frame=0;frame<1000;frame++){
                        var previous=position;position=surface.ProjectToSurface(position+tangent*.01,.025);
                        var change=position-previous;double travel=change.x*tangent.x+change.y*tangent.y+change.z*tangent.z;
                        if(Math.Abs(travel-.01)>.000001)throw new InvalidOperationException("Large-planet surface projection snapped a centimetre movement: "+travel);
                    }
                    Check(Math.Abs(position.Magnitude-preciseBody.radius-.025)<.000001,"1000x surface contact preserves centimetre movement for 1000 steps without radial jitter");
                    var normal=surface.SampleNormal(direction.Float);Check(float.IsFinite(normal.x)&&normal.sqrMagnitude>.99f,"Large-body terrain normal remains finite and normalised");
                }finally{UnityEngine.Object.DestroyImmediate(preciseBody);}
                var authored=Resources.Load<PlanetDefinition>("MassiveArmy/Planets/DefaultPlanet");var preset=Resources.Load<TextAsset>("MassiveArmy/Planets/LargePlanetClouds");
                var expected=JsonUtility.FromJson<PlanetWeather>(preset.text);
                foreach(var field in typeof(PlanetWeather).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
                    if(preset.text.Contains("\""+field.Name+"\""))Check(field.GetValue(authored.weather).Equals(field.GetValue(expected)),"Default planet uses supplied cloud setting: "+field.Name);
                var adaptive=new PlanetWeather{cloudAltitude=22000,cloudThickness=2872};
                adaptive.Layer(baselineHome,out float layerBottom,out float layerThickness);
                Check(layerBottom==22000&&layerThickness==2872,"Authored 22 km clouds are no longer silently clamped by atmosphere height");
                Check(adaptive.LocalDetailBlend(0)>0&&adaptive.LocalDetailBlend(100000)==0&&adaptive.LocalDetailBlend(30000)<adaptive.LocalDetailBlend(15000),"Local cloud detail blends smoothly by physical distance without changing the orbital profile");
                var weather=new PlanetWeather();
                Check(weather.Detail(1e9f,10000,400,1000)>.99f,"Large on-screen planets retain full cloud detail at astronomical distances");
                Check(weather.Detail(1e9f,10000,5,1000)==0,"Subpixel/small planets still use the cheap analytic cloud fallback");
                weather.quality=PlanetCloudQuality.Ultra;Check(weather.ShadowWidth==512&&weather.ShadowLayers==16,"Ultra increases cloud lighting/shadow cache resolution");
                weather.renderScaleOverride=.75f;weather.marchStepsOverride=48;weather.shadowResolutionOverride=256;weather.shadowLayersOverride=12;
                Check(weather.EffectiveRenderScale==.75f&&weather.EffectiveMarchSteps==48&&weather.ShadowWidth==256&&weather.ShadowLayers==12,"Live resolution and sampling overrides are bounded and respected");
                weather.shadowResolutionOverride=4096;Check(weather.ShadowWidth==4096,"Cloud shadow override supports 4096 without changing quality presets");
                weather.shadowResolutionOverride=8192;Check(weather.ShadowWidth==4096,"Cloud shadow override clamps to 4096");
                ValidatePrecision(Check);ValidateWeatherShaders(Check);
                var baseline=new GalaxyModel(stars,source,baselineHome,out baselineBodies);
                foreach(float scale in new[]{1f,10f,100f,1000f}){
                    var settings=source.CreateScaledCopy(scale);var home=UnityEngine.Object.Instantiate(baselineHome);home.radius*=scale;home.gravitationalParameter*=(double)scale*scale;PlanetDefinition[] bodies=null,legacy=null;
                    try{
                        Check(Mathf.Abs(settings.CloudNoiseSizeScale-scale*Mathf.Sqrt(scale))<.001f,"Cloud detail size scaling mismatch");
                        var model=new GalaxyModel(settings.galaxy,settings,home,out bodies);
                        Check(model.Nodes.Length==baseline.Nodes.Length,"Scale changed body count");
                        for(int i=0;i<model.Nodes.Length;i++){
                            var a=baseline.Nodes[i];var b=model.Nodes[i];
                            Check(Math.Abs(b.radius-a.radius*scale)<Math.Max(1,a.radius*scale)*1e-6,"Body radius scale mismatch");
                            Check(Math.Abs(b.semiMajor-a.semiMajor*scale)<Math.Max(1,a.semiMajor*scale)*1e-6,"Orbital spacing scale mismatch");
                            Check(Math.Abs(b.mu-a.mu*scale*scale)<Math.Max(1,a.mu*scale*scale)*1e-6,"Surface gravity scale mismatch");
                            Check(!double.IsNaN(model.Current[i].position.Magnitude)&&!double.IsInfinity(model.Current[i].velocity.Magnitude),"Invalid orbital state");
                        }
                        for(int i=0;i<bodies.Length;i++){
                            Check(Math.Abs(bodies[i].gravitationalParameter/(bodies[i].radius*bodies[i].radius)-9.81)<.00001,"Planet surface gravity changed");
                            Check(Math.Abs(bodies[i].terrainHeight-baselineBodies[i].terrainHeight)<.01,"Terrain relief changed");
                        }
                        legacy=settings.Generate(home);Check(legacy[1].position.RelativeTo(home.position).Magnitude>home.radius+legacy[1].radius,"Legacy planets overlap");
                        Check(Math.Abs(stars.galaxyRadius-8e9)<1&&source.radiusRange.x==80000,"Authored source settings mutated");
                    }finally{
                        if(bodies!=null)foreach(var body in bodies)if(body!=home)UnityEngine.Object.DestroyImmediate(body);
                        if(legacy!=null)foreach(var body in legacy)if(body!=home)UnityEngine.Object.DestroyImmediate(body);
                        UnityEngine.Object.DestroyImmediate(home);UnityEngine.Object.DestroyImmediate(settings.galaxy);UnityEngine.Object.DestroyImmediate(settings);
                    }
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{if(baselineBodies!=null)foreach(var body in baselineBodies)if(body!=baselineHome)UnityEngine.Object.DestroyImmediate(body);UnityEngine.Object.DestroyImmediate(baselineHome);UnityEngine.Object.DestroyImmediate(stars);UnityEngine.Object.DestroyImmediate(source);}
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/size-scale.json",JsonUtility.ToJson(report,true));Debug.Log("System size validation "+report.status+": "+report.checks+" checks");
        }
        public static void CaptureCurrentWeather(bool wider=false){
            const string folder="TestResults/Planetary/CloudInspection";Directory.CreateDirectory(folder);
            var system=UnityEngine.Object.FindFirstObjectByType<PlanetSystemRuntime>();PlanetDevelopment world=system?.ActiveWorld;
            if(world==null)foreach(var candidate in UnityEngine.Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None))if(candidate.Ready&&candidate.View.enabled){world=candidate;break;}
            if(world==null){File.WriteAllText(folder+"/state.txt","No active, ready planet session is available for capture.");return;}
            if(wider&&!world.IsPiloting&&!world.IsFirstPerson&&!world.IsDriving){world.Rig.Distance=Mathf.Min(world.definition.radius*12,world.Rig.Distance*3);world.Rig.RefreshPose();}
            var camera=world.View;File.WriteAllText(folder+"/state.txt",$"radius={world.definition.radius} scale={system?.Settings.RuntimeSizeMultiplier} camera={camera.transform.position} pixel={camera.pixelWidth}x{camera.pixelHeight} piloting={world.IsPiloting} body={world.RenderToBody(camera.transform.position)}");
            var target=new RenderTexture(1280,720,24);target.Create();var previous=RenderTexture.active;Texture2D image=null;
            try{UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(folder+"/current.png",image.EncodeToPNG());}
            finally{RenderTexture.active=previous;if(image!=null)UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static void ValidateWeatherShaders(Action<bool,string> check){
            var shader=Resources.Load<Shader>("MassiveArmy/PlanetWeather");var material=new Material(shader);
            var noise=new Texture3D(4,4,4,TextureFormat.RFloat,false);var pixels=new Color[64];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(.5f,0,0,1);noise.SetPixels(pixels);noise.Apply();
            var cache=new RenderTexture(256,128,0,RenderTextureFormat.RFloat){dimension=UnityEngine.Rendering.TextureDimension.Tex3D,volumeDepth=12,enableRandomWrite=true};cache.Create();
            try{
                for(int pass=0;pass<material.passCount;pass++)check(material.SetPass(pass),"Cloud shader pass failed: "+pass);
                check(!ShaderUtil.ShaderHasError(shader),"Cloud shader compile error");
                var compute=Resources.Load<ComputeShader>("MassiveArmy/PlanetWeatherLight");int kernel=compute.FindKernel("BuildLight");compute.SetInt("_WeatherLightRowOffset",0);compute.SetInt("_WeatherLightRowCount",0);
                compute.SetTexture(kernel,"_WeatherNoise",noise);compute.SetTexture(kernel,"_WeatherLightWrite",cache);compute.SetFloat("_WeatherRadius",120000);compute.SetFloat("_WeatherNoiseLod",0);compute.SetFloat("_WeatherDetailNoiseLod",0);compute.SetFloat("_WeatherNoiseSizeScale",1);compute.SetFloat("_WeatherNoiseFrequency",1);compute.SetVector("_WeatherNoiseTuning",new Vector4(.52f,.22f,8.4f,.65f));compute.SetVector("_WeatherOctaves",new Vector4(16,.5f,2,0));
                compute.SetVector("_WeatherAtmosphere",new Vector4(.08f,.4f,.018f,.013f));compute.SetVector("_WeatherCloud",new Vector4(.8f,216,1,1));compute.SetVector("_WeatherWind",new Vector4(0,1,0,0));compute.SetVector("_WeatherDetailMotion",new Vector4(0,1,.25f,.4f));compute.SetVector("_WeatherSun",Vector3.up);
                using(var profile=new ComputeBuffer(2,16)){
                    int profileKernel=compute.FindKernel("ValidateProfile");compute.SetBuffer(profileKernel,"_ProfileValidation",profile);compute.Dispatch(profileKernel,1,1,1);var values=new Vector4[2];profile.GetData(values);
                    check(Mathf.Abs(values[1].y-2000)<.01f,"1000x cloud-shell intersection retains centimetre-scale altitude precision");
                    check(Mathf.Abs(values[0].x-.575f/3)<.00001f&&values[0].y==0&&values[0].z==0&&values[0].w>.98f,"Cloud step integration preserves density across thin layers without sheet bands");
                }
                compute.SetVector("_WeatherLightSize",new Vector4(cache.width,cache.height,cache.volumeDepth,0));compute.Dispatch(kernel,(cache.width+7)/8,(cache.height+7)/8,cache.volumeDepth);
                var request=UnityEngine.Rendering.AsyncGPUReadback.Request(cache);request.WaitForCompletion();check(!request.hasError,"Cloud lighting GPU readback failed");
                var data=request.GetData<float>();bool finite=true,shadow=false;foreach(float value in data){finite&=!float.IsNaN(value)&&!float.IsInfinity(value);shadow|=value>0;}
                check(finite&&shadow,"Animated dense-cloud lighting produced invalid or empty optical depth");
            }finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(noise);cache.Release();UnityEngine.Object.DestroyImmediate(cache);}
        }
        static void ValidatePrecision(Action<bool,string> check){
            var shader=Resources.Load<ComputeShader>("MassiveArmy/PlanetTerrainValidation");int kernel=shader.FindKernel("Precision");
            var inputs=new Vector4[9];var results=new Vector4[9];var anchorUvs=new Vector4[6];var bases=new Vector4[6];
            using(var input=new ComputeBuffer(9,16))using(var output=new ComputeBuffer(9,16)){
                shader.SetBuffer(kernel,"_Directions",input);shader.SetBuffer(kernel,"_Samples",output);shader.SetInt("_SampleCount",9);
                foreach(float radius in new[]{120000f,1200000f,12000000f,120000000f})for(int face=0;face<6;face++){
                    float u=.371f,v=.623f;Double3 Cube(float x,float y)=>new Double3(CubeSphere.Normal[face])+new Double3(CubeSphere.Right[face])*x+new Double3(CubeSphere.Up[face])*y;
                    var cube=Cube(u,v);var observer=cube*(radius/cube.Magnitude)+new Double3(.31,1.73,-.19);
                    anchorUvs[face]=new Vector4(u,v,0,0);bases[face]=(cube*(radius/cube.Magnitude)-observer).Float;
                    for(int i=0;i<9;i++)inputs[i]=new Vector4(face,u+(i-4)/8388608f,v+(i-4)/16777216f,0);
                    input.SetData(inputs);shader.SetFloat("_PlanetRadius",radius);shader.SetVectorArray("_PlanetPrecisionUv",anchorUvs);shader.SetVectorArray("_PlanetPrecisionBase",bases);shader.Dispatch(kernel,1,1,1);output.GetData(results);
                    double error=0;for(int i=0;i<9;i++){var q=Cube(inputs[i].y,inputs[i].z);var expected=q*(radius/q.Magnitude)-observer;var actual=new Double3(results[i].x,results[i].y,results[i].z);error=Math.Max(error,(actual-expected).Magnitude);}
                    check(error<.001,"GPU terrain reference precision exceeds 1 mm at radius "+radius+" face "+face+": "+error);
                }
            }
            check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("MassiveArmy/PlanetTerrain")),"Terrain shader failed compilation");
        }
    }
}
