using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Explicit-render standalone smoke test; opt-in and bounded.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PlanetStandaloneValidation : MonoBehaviour
    {
        PlanetDevelopment fixture;RenderTexture target;RenderPipeline.StandardRequest request;string output;
        int frames;uint beforeFps;double deadline;long allocated,reserved;bool commandSmoke;uint selected;
        [Serializable]sealed class Report {public string status,gpu,api,limitation;public int frames,width,height;public uint ticksBeforeFps,ticksAfterFps,selectedTanks;public long peakAllocatedBytes,peakReservedBytes;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-smoke")>=0)new GameObject("Planet standalone validation").AddComponent<PlanetStandaloneValidation>();}
        void Start()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i>=0&&i+1<args.Length?args[i+1]:Application.persistentDataPath;
            commandSmoke=Array.IndexOf(args,"-planet-command-smoke")>=0;
            Directory.CreateDirectory(output);deadline=Time.realtimeSinceStartupAsDouble+90;fixture=FindAnyObjectByType<PlanetDevelopment>();Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
        }
        void LateUpdate()
        {
            if(Time.realtimeSinceStartupAsDouble>deadline){File.WriteAllText(Path.Combine(output,"error.txt"),"Planet smoke test timed out");Application.Quit(2);return;}
            if(fixture==null||!fixture.Ready)return;
            if(target==null){target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();fixture.View.enabled=false;fixture.View.targetTexture=target;request=new RenderPipeline.StandardRequest{destination=target};}
            try{
                RenderPipeline.SubmitRenderRequest(fixture.View,request);var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("Planet render fence failed");
                frames++;allocated=Math.Max(allocated,Profiler.GetTotalAllocatedMemoryLong());reserved=Math.Max(reserved,Profiler.GetTotalReservedMemoryLong());
                if(frames==15&&commandSmoke){fixture.Rig.InputEnabled=false;var centre=fixture.PlanetRenderCentre;
                    fixture.Commands.Select(fixture.View.projectionMatrix*fixture.View.worldToCameraMatrix*Matrix4x4.Translate(centre),fixture.View.transform.position-centre,new Vector2(2560,1440),Vector2.zero,new Vector2(1280,1440));
                    selected=fixture.Commands.SelectedCount;if(selected==0||selected>=fixture.unitCount)throw new InvalidOperationException("Standalone rectangle did not select a proper subset");
                    fixture.Commands.Stop();if(!fixture.OrderSelected(fixture.Rig.FocusDirection))throw new InvalidOperationException("Standalone selected move was rejected");}
                if(frames==30){Capture("surface");fixture.Rig.Distance=1700;}
                if(frames==60){Capture("orbit");beforeFps=fixture.SimulationTicks;fixture.SetFirstPerson(true);}
                if(frames==90){Capture("first-person");if(fixture.SimulationTicks<=beforeFps)throw new InvalidOperationException("Army simulation stopped during FPS mode");
                    File.WriteAllText(Path.Combine(output,"smoke.json"),JsonUtility.ToJson(new Report{status="PASS",gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),frames=frames,width=2560,height=1440,ticksBeforeFps=beforeFps,ticksAfterFps=fixture.SimulationTicks,selectedTanks=selected,peakAllocatedBytes=allocated,peakReservedBytes=reserved,limitation="Development scene smoke only; does not certify planetary million-unit performance, Earth-scale terrain, vehicle/ship gameplay or fog."},true));fixture.SetFirstPerson(false);Application.Quit();}
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);}
        }
        void Capture(string name)
        {
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);
        }
        void OnDestroy(){if(target!=null){target.Release();Destroy(target);}}
    }
}
