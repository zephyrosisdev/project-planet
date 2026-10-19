using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal void PopulateCityBenchmark(int count){
            string[] types={"factory","barracks","solar","civil_housing","civil_food_processing","light_depot","civil_housing","wind"};
            var origin=Grid.Area(Catalog.Buildings["command"],Homes[0],0);int side=Mathf.CeilToInt(Mathf.Sqrt(count));
            for(int i=0;i<count;i++){var p=Grid.Point(origin.face,origin.x+48+(i%side)*12,origin.y+48+(i/side)*12);Create(Catalog.Buildings[types[i%types.Length]],p,i&3,true,0);}
            foreach(var faction in Civilisation.State.factions)faction.autonomous=false;Civilisation.Settings.autoExtendSettlementRoads=false;
            Civilisation.Advance(1);World.Rig.SetFocusDirection(Grid.Point(origin.face,origin.x+48+side*6,origin.y+48+side*6));World.RefreshBuildingCosts(Buildings);Rebase();
        }
    }
    [DefaultExecutionOrder(-215)]public sealed class PlanetCityValidation:MonoBehaviour {
        [Serializable]public sealed class Phase {public string name;public double medianMs,p95Ms,simulationHz;public long allocatedBytes,reservedBytes;public PlanetRtsBase.BuildingViewStatistics views;public int buildings;public List<double> samples=new List<double>();}
        [Serializable]public sealed class Report {public string status="PASS",error="";public int runtimeErrors,requestedBuildings;public List<Phase> phases=new List<Phase>();public List<string> checks=new List<string>();}
        readonly Report report=new Report();PlanetDevelopment world;string output;RenderTexture target;RenderPipeline.StandardRequest request;Phase phase;int stage=-1;double start,previous;uint tick;bool ready,finished,capture;Vector3 cityFocus;
        string Arg(string name,string fallback){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,name);return at>=0?args[at+1]:fallback;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-city-validation")>=0)new GameObject("Dense city benchmark").AddComponent<PlanetCityValidation>();}
        void Awake(){capture=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-city-capture")>=0;output=Arg("-planet-output",Application.persistentDataPath);Directory.CreateDirectory(output);report.requestedBuildings=int.Parse(Arg("-planet-city-count","1024"));world=FindAnyObjectByType<PlanetDevelopment>();world.currentRtsContent=true;world.configuredRadius=120000;world.ambientZombies=0;world.requestedCapacity=8192;world.Paused=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);}
        void BeginPhase(){stage++;if(stage>=4){ValidateViews();Check(report.runtimeErrors==0,"No dense-city runtime errors");Finish();return;}world.Rig.SetFocusDirection(cityFocus);world.Rig.Distance=stage==0||stage==3?2700:stage==1?18000:400000;world.Rig.RefreshPose();world.Base.Rebase();phase=new Phase{name=new[]{"Near","Distant","Orbit","Moving"}[stage],buildings=world.Base.Buildings.Count};report.phases.Add(phase);tick=world.SimulationTicks;start=previous=Time.realtimeSinceStartupAsDouble;}
        void LateUpdate(){if(finished)return;try{
            if(!ready){if(!world.Ready)return;ready=true;world.Base.PopulateCityBenchmark(report.requestedBuildings);cityFocus=world.Rig.FocusDirection;Check(world.Base.Buildings.Count>=report.requestedBuildings,"All requested building objects retained");world.Rig.InputEnabled=false;target=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;request=new RenderPipeline.StandardRequest{destination=target};world.Rig.Distance=2700;world.Rig.RefreshPose();world.Base.Rebase();return;}
            if(stage<0){if(!world.SettlementVisualsReadyForValidation)return;world.Paused=false;BeginPhase();return;}
            if(stage==3){var east=Vector3.Cross(Vector3.up,cityFocus).normalized;world.Rig.SetFocusDirection((cityFocus+east*Mathf.Sin((float)(Time.realtimeSinceStartupAsDouble-start))*.002f).normalized);world.Rig.RefreshPose();world.Base.Rebase();}
            RenderPipeline.SubmitRenderRequest(world.View,request);var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();double now=Time.realtimeSinceStartupAsDouble;if(now-start>=3)phase.samples.Add((now-previous)*1000);previous=now;phase.allocatedBytes=Math.Max(phase.allocatedBytes,Profiler.GetTotalAllocatedMemoryLong());phase.reservedBytes=Math.Max(phase.reservedBytes,Profiler.GetTotalReservedMemoryLong());
            if(now-start<12)return;phase.views=world.Base.BuildingViews;phase.simulationHz=(world.SimulationTicks-tick)/(now-start);var sorted=phase.samples.ToArray();Array.Sort(sorted);phase.medianMs=sorted[sorted.Length/2];phase.p95Ms=sorted[(int)((sorted.Length-1)*.95)];if(capture)Capture(phase.name);File.WriteAllText(Path.Combine(output,phase.name+".json"),JsonUtility.ToJson(phase,true));BeginPhase();
        }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);Finish();}}
        void Capture(string name){var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(2560,1440,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,2560,1440),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());Destroy(pixels);RenderTexture.active=old;}
        void ValidateViews(){if(world.Base.LegacyCityPresentation)return;var orbit=report.phases[2].views;Check(orbit.near+orbit.far+orbit.proxy==0,"Orbital sub-two-pixel building meshes are culled");Check(report.phases[1].views.proxy>0,"Distant buildings use shared simple silhouette meshes");
            var target=world.Base.Buildings[world.Base.Buildings.Count/2];var collider=target.visual.GetComponent<BoxCollider>();Check(target.visual.activeSelf&&collider.enabled,"Visual culling retains building objects and collision");world.Rig.SetFocusDirection(CubeSphere.Decode(target.position));world.Rig.Distance=80;world.Rig.RefreshPose();world.Base.Rebase();Physics.SyncTransforms();var mesh=target.visual.transform.Find("Current RTS shell").GetComponent<MeshFilter>();var art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");var model=art.Find(target.definition.id)??art.Find(target.definition.mesh);Check(mesh.sharedMesh==model.near&&mesh.GetComponent<MeshRenderer>().enabled,"Zooming back restores the original close mesh");
            Check(world.Base.Pick(world.View.WorldToScreenPoint(target.visual.transform.position)),"Previously culled buildings remain selectable");
        }
        void Finish(){finished=true;File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void OnDestroy(){Application.logMessageReceived-=Log;if(target!=null){target.Release();Destroy(target);}}
    }
}
