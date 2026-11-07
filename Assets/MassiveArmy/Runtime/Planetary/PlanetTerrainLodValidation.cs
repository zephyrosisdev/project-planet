using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed class PlanetTerrainLodValidation:MonoBehaviour {
        [Serializable]sealed class Report{public string status="PASS",error="";public int runtimeErrors;public long patchBufferBytes,allocatedBytes;public double renderMedianMs,renderP95Ms;public List<string> checks=new List<string>();}
        readonly Report report=new Report();PlanetDevelopment world;RenderTexture target;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-terrain-lod-check")>=0)new GameObject("First person terrain LOD validation").AddComponent<PlanetTerrainLodValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.currentRtsContent=true;world.sessionMode=PlanetGameMode.AiTest;world.requestedCapacity=8192;world.ambientZombies=0;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=Log;}
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
        IEnumerator Start(){var run=Run();while(true){object next;try{if(!run.MoveNext())break;next=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();break;}yield return next;}report.allocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void Render(){RenderPipeline.SubmitRenderRequest(world.View,new RenderPipeline.StandardRequest{destination=target});var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1,TextureFormat.RGBA32);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("Render fence");}
        void Capture(string name){Render();var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(2560,1440,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,2560,1440),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;}
        IEnumerator Run(){double deadline=Time.realtimeSinceStartupAsDouble+120;while(!world.Ready&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Check(world.Ready&&world.StreamedTerrain!=null,"Real game initializes streamed terrain");world.Paused=true;world.AutomaticPlayerStep=false;world.Rig.InputEnabled=false;world.SetFirstPerson(true);var player=world.PlayerForValidation;player.CaptureCursor=false;
            target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;
            var near=world.Base.Offset(new Vector2(1800,500));var mountain=near;float highest=float.MinValue;for(int i=0;i<128;i++){float angle=i*2.3999632f;var d=world.Base.Offset(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(1500+i*25));float h=world.Surface.SampleHeight(d);if(h>highest){highest=h;mountain=d;}}
            foreach(var direction in new[]{near,mountain}){var facing=Vector3.Cross(direction,Vector3.up).normalized;if(facing.sqrMagnitude<.1f)facing=Vector3.right;
                foreach(float eye in new[]{1000f,100,30,10,1.8f,5,1.8f}){player.EyeHeight=eye;player.EnterBodyPosition(world.Surface.GetSurfacePositionPrecise(direction,.05),facing);player.Step(0,Vector2.zero,new Vector2(0,-200));for(int frame=0;frame<4;frame++)yield return null;
                    CubeSphere.Project(direction,out var face,out var uv);float span=2;var patches=world.StreamedTerrain.ReadPatchesForValidation();foreach(var patch in patches)if((int)patch.w==(int)face&&uv.x>=patch.x-1e-6f&&uv.x<=patch.x+patch.z+1e-6f&&uv.y>=patch.y-1e-6f&&uv.y<=patch.y+patch.z+1e-6f)span=Mathf.Min(span,patch.z);float edge=world.definition.radius*span/PlanetStreamedTerrain.Grid;
                    Check(eye>5||edge<2,"Actual first-person mesh edge "+edge.ToString("F3")+" m at eye height "+eye+" m");Check(world.IsFirstPerson&&world.View.transform.position==Vector3.zero,"First person and floating origin remain active");Check(patches.Length<=384&&world.StreamedTerrain.BufferBytes==34848,"Terrain keeps 384 patches plus 16 KiB of GPU LOD history");
                    if(eye==1.8f)Capture(direction==near?"first-person-near-ground":"first-person-hillside");
                }
            }
            var samples=new List<double>();var timer=new System.Diagnostics.Stopwatch();for(int i=0;i<65;i++){timer.Restart();Render();timer.Stop();if(i>=15)samples.Add(timer.Elapsed.TotalMilliseconds);yield return null;}samples.Sort();report.renderMedianMs=samples[samples.Count/2];report.renderP95Ms=samples[(int)((samples.Count-1)*.95)];report.patchBufferBytes=world.StreamedTerrain.BufferBytes;Check(report.runtimeErrors==0,"No runtime or shader errors");
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(target!=null){target.Release();Destroy(target);}}
    }
}
