using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace MassiveArmy.Planetary
{
    public sealed class PlanetWeatherValidation:MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="",gpu,measurement="2560x1440; paired HDRP render requests with explicit pixel readback fence. Weather delta includes CPU submission and GPU work; not a GPU timestamp.";public int runtimeErrors,orbitPixels,insidePixels;public long weatherTextureBytes;public double nearWeatherMedianMs,nearWeatherP95Ms,weatherGpuMs;public List<string> checks=new List<string>();}
        readonly Report report=new Report();PlanetDevelopment world;RenderTexture target,small;string output;bool logging;readonly Stopwatch timer=new Stopwatch();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-weather-validation")>=0)new GameObject("Planet weather validation").AddComponent<PlanetWeatherValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.currentRtsContent=true;world.configuredRadius=120000;world.requestedCapacity=8192;world.ambientZombies=0;world.sessionMode=PlanetGameMode.AiTest;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;report.gpu=SystemInfo.graphicsDeviceName;Application.logMessageReceived+=Log;logging=true;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);report.checks.Add(text);}
        IEnumerator Start(){var run=Run();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();UnityEngine.Debug.LogException(e);break;}if(!more)break;yield return next;}File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void Render(){RenderPipeline.SubmitRenderRequest(world.View,new RenderPipeline.StandardRequest{destination=target});var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1,TextureFormat.RGBA32);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("Weather render fence failed");}
        Color32[] Capture(string name){Render();var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);Graphics.Blit(target,small);RenderTexture.active=small;var preview=new Texture2D(small.width,small.height,TextureFormat.RGBA32,false);preview.ReadPixels(new Rect(0,0,small.width,small.height),0,0);preview.Apply();var data=preview.GetPixels32();Destroy(preview);RenderTexture.active=previous;return data;}
        static int Difference(Color32[] a,Color32[] b){int count=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>12)count++;return count;}
        Color32[] ReadColour(){var read=AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32);read.WaitForCompletion();if(read.hasError)throw new InvalidOperationException("First-person colour readback failed");return read.GetData<Color32>().ToArray();}
        void CheckNearSolids(string name,PlanetWeatherRenderer renderer,float limit){
            // Compare geometry occlusion without bloom spreading exterior cloud light
            // over an otherwise correctly occluded cockpit silhouette.
            var hd=world.View.GetComponent<HDAdditionalCameraData>();var savedSettings=hd.renderingPathCustomFrameSettings;var savedMask=hd.renderingPathCustomFrameSettingsOverrideMask;
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess]=true;hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess,false);
            renderer.Enabled=false;Capture(name+"-control");var control=ReadColour();renderer.Enabled=true;Capture(name+"-weather");var weather=ReadColour();
            var read=AsyncGPUReadback.Request(renderer.DepthForValidation,0);read.WaitForCompletion();Check(!read.hasError,"First-person depth readback succeeds");var depth=read.GetData<float>();var projection=GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true);int samples=0,changed=0;
            for(int y=4;y<1436;y+=4)for(int x=4;x<2556;x+=4){int i=y*2560+x;float raw=depth[i];if(raw<=0)continue;float metres=Mathf.Abs(projection.m23/(raw+projection.m22));if(metres>limit)continue;
                if(Mathf.Abs(depth[i-2]-raw)>raw*.01f||Mathf.Abs(depth[i+2]-raw)>raw*.01f||Mathf.Abs(depth[i-5120]-raw)>raw*.01f||Mathf.Abs(depth[i+5120]-raw)>raw*.01f)continue;
                samples++;if(Math.Abs(control[i].r-weather[i].r)+Math.Abs(control[i].g-weather[i].g)+Math.Abs(control[i].b-weather[i].b)>6)changed++;
            }
            hd.renderingPathCustomFrameSettings=savedSettings;hd.renderingPathCustomFrameSettingsOverrideMask=savedMask;
            Check(samples>1000,name+" renders nearby opaque surfaces");Check(changed==0,name+" keeps weather behind solid surfaces (changed "+changed+" / "+samples+")");
        }
        IEnumerator CharacterOcclusion(PlanetWeatherRenderer renderer){
            var player=world.PlayerForValidation;var ship=world.ShipForValidation;float haze=world.definition.weather.haze;world.definition.weather.haze=0;world.AutomaticPlayerStep=false;player.CaptureCursor=false;
            world.SetFirstPerson(true);var building=world.Base.Buildings[0];var up=CubeSphere.Decode(building.position);var side=Vector3.Cross(up,Vector3.up).normalized;if(side.sqrMagnitude<.1f)side=Vector3.Cross(up,Vector3.right).normalized;
            player.Enter((up+side*(60/world.definition.radius)).normalized,-side);world.Base.Rebase();yield return null;
            var targetBuilding=building.visual.GetComponentInChildren<Renderer>().bounds.center;world.View.transform.rotation=Quaternion.LookRotation(targetBuilding-world.View.transform.position,up);yield return null;
            Check(world.IsFirstPerson&&world.View.nearClipPlane==.05f,"Building weather check uses the real first-person camera");CheckNearSolids("first-person-building",renderer,200);
            world.SetShipControl(true);for(int i=0;i<8;i++)yield return null;Check(world.IsPiloting,"Cockpit weather check uses pilot control");CheckNearSolids("pilot-cockpit",renderer,20);
            world.SetFirstPerson(true);player.EnterShip(ship,new Vector3(0,-3.2f,31),Vector3.right);ship.Rebase();for(int i=0;i<4;i++)yield return null;CheckNearSolids("first-person-ship-wall",renderer,20);
            world.definition.weather.Layer(world.definition,out float bottom,out float thickness);var pose=ship.Pose;pose.position=new Double3(pose.position.Float.normalized)*(world.definition.radius+bottom+thickness*.5f);ship.ResetForSystemValidation(pose);player.Resume();ship.Rebase();for(int i=0;i<4;i++)yield return null;CheckNearSolids("ship-wall-inside-cloud",renderer,20);
            world.SetShipControl(true);for(int i=0;i<4;i++)yield return null;CheckNearSolids("pilot-inside-cloud",renderer,20);
            renderer.Enabled=false;var windowControl=Capture("cloud-window-control");renderer.Enabled=true;var windowWeather=Capture("cloud-window-weather");Check(Difference(windowControl,windowWeather)>1000,"Clouds remain visible outside the cockpit windshield while the cabin stays clear");
            world.SetFirstPerson(false);world.AutomaticPlayerStep=true;world.definition.weather.haze=haze;world.Rig.RefreshPose();yield return null;
        }
        IEnumerator ForegroundOcclusion(PlanetWeatherRenderer renderer){
            // Exercise the opaque mesh path used by RTS buildings, ahead of weather.
            float pitch=world.Rig.Pitch,distance=world.Rig.Distance;world.Rig.Pitch=25;world.Rig.Distance=1000;world.Rig.RefreshPose();yield return null;
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Weather foreground occlusion fixture";
            var material=new Material(Resources.Load<Shader>("MassiveArmy/Structure"));material.SetColor("_Tint",new Color(.8f,.05f,.03f));cube.GetComponent<Renderer>().sharedMaterial=material;
            cube.transform.SetPositionAndRotation(world.View.transform.position+world.View.transform.forward*50,world.View.transform.rotation);cube.transform.localScale=new Vector3(20,20,2);
            float haze=world.definition.weather.haze;world.definition.weather.haze=0;
            renderer.Enabled=false;var control=Capture("foreground-control");renderer.Enabled=true;var weather=Capture("foreground-weather");int changed=0;
            var depth=AsyncGPUReadback.Request(renderer.DepthForValidation,0);depth.WaitForCompletion();bool depthReadSucceeded=!depth.hasError;var values=depth.GetData<float>();var projection=GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true);
            float raw=values[720*2560+1280],metres=Mathf.Abs(projection.m23/(raw+projection.m22));
            File.WriteAllText(Path.Combine(output,"foreground-depth.txt"),"Source dimension: "+renderer.DepthDimensionForValidation+"\nForeground depth: "+metres+" metres\nRaw depth: "+raw);
            for(int y=65;y<115;y++)for(int x=135;x<185;x++){int i=y*320+x;if(Math.Abs(control[i].r-weather[i].r)+Math.Abs(control[i].g-weather[i].g)+Math.Abs(control[i].b-weather[i].b)>6)changed++;}
            cube.SetActive(false);Destroy(cube);Destroy(material);world.definition.weather.haze=haze;
            world.Rig.Pitch=pitch;world.Rig.Distance=distance;world.Rig.RefreshPose();yield return null;
            Check(depthReadSucceeded&&Mathf.Abs(metres-49)<1,"Weather snapshot contains the foreground object's depth rather than an empty texture");
            Check(changed==0,"Clouds behind a foreground opaque object do not tint or cover its interior (changed pixels: "+changed+")");
        }
        IEnumerator Run(){
            float deadline=Time.realtimeSinceStartup+120;while(!world.Ready){Check(Time.realtimeSinceStartup<deadline,"Planet initialization remains bounded");yield return null;}world.Paused=true;world.Rig.InputEnabled=false;
            target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();small=new RenderTexture(320,180,0,RenderTextureFormat.ARGB32);small.Create();world.View.enabled=false;world.View.targetTexture=target;
            var definition=world.definition;var renderer=world.WeatherForValidation;Check(renderer!=null,"Viewed world owns a bounded weather renderer");
            var clouds=new HashSet<PlanetCloudSystem>();int airless=0;var probe=ScriptableObject.CreateInstance<PlanetDefinition>();
            for(int i=0;i<96;i++){probe.terrainSeed=i*7919;PlanetWeather.Generate(probe);string first=JsonUtility.ToJson(probe.weather);PlanetWeather.Generate(probe);Check(first==JsonUtility.ToJson(probe.weather),"Planet weather seed is repeatable: "+i);clouds.Add(probe.weather.clouds);if(!probe.hasAtmosphere){airless++;Check(!probe.weather.HasClouds&&probe.surfacePressure==0,"Airless planets have no cloud layer or pressure");}}
            Destroy(probe);Check(airless>0&&clouds.Count==4,"Generation includes airless, clear, scattered, broken and overcast worlds");
            Check(PlanetWeather.NearDetail(0,10000)==1&&PlanetWeather.NearDetail(40000,10000)==0&&PlanetWeather.NearDetail(22500,10000)>.4f&&PlanetWeather.NearDetail(22500,10000)<.6f,"Volumetric detail blends continuously into distant clouds");
            definition.hasAtmosphere=true;definition.surfacePressure=1;definition.weather=new PlanetWeather{clouds=PlanetCloudSystem.Broken,coverage=.65f};
            world.Rig.Pitch=90;world.Rig.Distance=definition.radius*2;world.Rig.RefreshPose();for(int i=0;i<20;i++)yield return null;
            var orbit=Capture("orbit-weather");renderer.Enabled=false;var bare=Capture("orbit-control");report.orbitPixels=Difference(orbit,bare);Check(report.orbitPixels>1000,"Atmospheric limb and cloud cover are visible from orbit");renderer.Enabled=true;
            definition.weather.clouds=PlanetCloudSystem.Scattered;definition.weather.coverage=.52f;world.Rig.Pitch=0;world.Rig.Distance=5000;world.Rig.RefreshPose();for(int i=0;i<20;i++)yield return null;Capture("below-scattered-clouds");
            definition.weather.Layer(definition,out float cloudBottom,out float cloudDepth);world.Rig.Pitch=25;world.Rig.Distance=Mathf.Max(10,(cloudBottom+cloudDepth+1000-world.Rig.Surface.SampleHeight(world.Rig.FocusDirection))/Mathf.Sin(25*Mathf.Deg2Rad));world.Rig.RefreshPose();for(int i=0;i<20;i++)yield return null;Capture("above-scattered-clouds");
            definition.weather.clouds=PlanetCloudSystem.Overcast;definition.weather.coverage=1;definition.weather.Layer(definition,out float bottom,out float thickness);float altitude=bottom+thickness*.5f;
            world.Rig.Pitch=25;world.Rig.Distance=Mathf.Max(10,(altitude-world.Rig.Surface.SampleHeight(world.Rig.FocusDirection))/Mathf.Sin(25*Mathf.Deg2Rad));world.Rig.RefreshPose();for(int i=0;i<20;i++)yield return null;
            var inside=Capture("inside-volumetric-clouds");Check(renderer.VolumetricBodies>0,"Flying into a cloud layer uses volumetric integration");renderer.Enabled=false;var without=Capture("inside-control");report.insidePixels=Difference(inside,without);Check(report.insidePixels>3000,"Cloud volume surrounds the camera and attenuates the ground view");renderer.Enabled=true;
            var occlusion=ForegroundOcclusion(renderer);while(occlusion.MoveNext())yield return occlusion.Current;
            var character=CharacterOcclusion(renderer);while(character.MoveNext())yield return character.Current;
            var deltas=new List<double>();for(int i=0;i<80;i++){renderer.Enabled=false;timer.Restart();Render();double off=timer.Elapsed.TotalMilliseconds;renderer.Enabled=true;timer.Restart();Render();double on=timer.Elapsed.TotalMilliseconds;if(i>=20)deltas.Add(Math.Max(0,on-off));yield return null;}
            deltas.Sort();report.nearWeatherMedianMs=deltas[deltas.Count/2];report.nearWeatherP95Ms=deltas[(int)((deltas.Count-1)*.95)];report.weatherGpuMs=renderer.GpuMilliseconds;report.weatherTextureBytes=renderer.TextureBytes;
            Check(report.nearWeatherP95Ms<=3,"Near-cloud weather incremental fenced render cost stays within 3 ms p95");Check(renderer.VisibleBodies<=17&&renderer.TextureBytes<32*1024*1024,"Weather draw count and render memory remain bounded");
            definition.hasAtmosphere=false;Capture("airless-local");Check(!renderer.LocalAtmosphereDrawn,"Airless worlds skip local atmospheric and cloud rendering");definition.hasAtmosphere=true;definition.weather.clouds=PlanetCloudSystem.None;Capture("clear-atmosphere");Check(renderer.VolumetricBodies==0,"A clear atmosphere does not march cloud volumes");foreach(var body in world.SystemRuntime.Bodies)if(body!=definition){Check(body.edits==null&&body.stampField==null,"Distant weather does not allocate gameplay terrain for "+body.displayName);var environment=world.SystemRuntime.Civilisation.Planet(body.bodyId).environment;Check(body.hasAtmosphere==environment.atmosphere&&Math.Abs(body.surfacePressure-environment.pressure)<.0001,"Rendered atmosphere matches strategic environment for "+body.displayName);}
            Check(report.runtimeErrors==0,"Weather rendering produces no runtime or shader errors");
        }
        void OnDestroy(){if(logging)Application.logMessageReceived-=Log;if(target!=null){target.Release();Destroy(target);}if(small!=null){small.Release();Destroy(small);}}
    }
}
