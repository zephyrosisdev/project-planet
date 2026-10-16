using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-220)]public sealed class PlanetScorchIntegration:MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public int retainedTexels,overlapTexels,scorchCount;public uint casualtyStamps;public double impactSubmissionMs;}
        readonly Report report=new Report();PlanetDevelopment world;string output;RenderTexture target;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-scorch-test")>=0)new GameObject("Nuclear scorch integration").AddComponent<PlanetScorchIntegration>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.currentRtsContent=true;world.deformTerrainOnNuke=false;world.configuredRadius=6000;world.ambientZombies=0;world.requestedCapacity=8192;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;}
        void Start(){StartCoroutine(Guard());}
        void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
        IEnumerator Guard(){var run=Run();while(true){object next;try{if(!run.MoveNext())break;next=run.Current;}catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);break;}yield return next;}File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        Color32[] Read(RenderTexture texture,int face){var request=AsyncGPUReadback.Request(texture,0,0,1024,0,1024,face,1);request.WaitForCompletion();if(request.hasError)throw new Exception("Atlas readback failed");return request.GetData<Color32>().ToArray();}
        Color32[] Capture(string name){RenderPipeline.SubmitRenderRequest(world.View,new RenderPipeline.StandardRequest{destination=target});var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;return pixels;}
        static void HideEffects(){foreach(var effect in FindObjectsByType<VisualEffect>(FindObjectsSortMode.None))effect.gameObject.SetActive(false);}
        IEnumerator Run(){double deadline=Time.realtimeSinceStartupAsDouble+90;while(world.EncounterForValidation==null||world.ShipForValidation==null){if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException();yield return null;}
            world.Paused=true;world.Rig.InputEnabled=false;var encounter=world.EncounterForValidation;var scorch=encounter.ScorchForValidation;var surface=new PlanetSurface(world.definition);Vector3 impact=world.Base.Offset(new Vector2(0,700));for(int i=0;surface.SampleHeight(impact)<world.definition.oceanHeight+40&&i<64;i++){float angle=i*2.39996323f;impact=world.Base.Offset(new Vector2(Mathf.Cos(angle)*700,Mathf.Sin(angle)*700));}Check(surface.SampleHeight(impact)>world.definition.oceanHeight,"Scorch test uses dry terrain");
            target=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;world.Rig.SetFocusDirection(impact);world.Rig.Distance=650;world.Rig.RefreshPose();for(int i=0;i<4;i++)yield return null;var before=Capture("before");
            encounter.Detonate(impact);HideEffects();for(int i=0;i<3;i++)yield return null;var after=Capture("first-impact");int darker=0;for(int i=0;i<before.Length;i++)if(after[i].r+after[i].g+after[i].b<before[i].r+before[i].g+before[i].b-15)darker++;Check(darker>100,"Persistent nuclear burn visibly darkens the rendered terrain");
            CubeSphere.Project(impact,out var face,out _);var first=Read(scorch.ColourForValidation,(int)face);var tangent=Vector3.Cross(Mathf.Abs(impact.y)>.95f?Vector3.right:Vector3.up,impact).normalized;var next=(impact+tangent*(140f/6000)).normalized;
            int firstVictim=world.unitCount;Check(encounter.Spawn(600,2,31,17)==600,"Regression fixture retains 600 GPU zombie records");var victims=new UnitState[600];for(int i=0;i<victims.Length;i++)victims[i]=new UnitState{position=CubeSphere.Encode(next),previous=CubeSphere.Encode(next),health=10,flags=1,packed=2u|(31u<<8),target=uint.MaxValue};world.UnitsForValidation.SetData(victims,0,firstVictim,600);
            var timer=System.Diagnostics.Stopwatch.StartNew();encounter.Detonate(next);timer.Stop();report.impactSubmissionMs=timer.Elapsed.TotalMilliseconds;HideEffects();world.UnitsForValidation.GetData(victims,0,firstVictim,600);Check(Array.TrueForAll(victims,u=>u.health==0),"Second blast kills all 600 test zombies and wraps the temporary remains ring");
            var second=Read(scorch.ColourForValidation,(int)face);bool retained=true;for(int i=0;i<first.Length;i++)if(first[i].a>0){report.retainedTexels++;if(second[i].a<first[i].a||second[i].r>first[i].r||second[i].g>first[i].g||second[i].b>first[i].b)retained=false;if(second[i].r<first[i].r||second[i].g<first[i].g||second[i].b<first[i].b)report.overlapTexels++;}
            report.scorchCount=scorch.Count;report.casualtyStamps=encounter.DecalCountForValidation-(uint)scorch.Count;Check(report.casualtyStamps>=600,"GPU casualty stamps exceeded the old 512-entry capacity");Check(retained&&report.retainedTexels>0,"Every original nuclear texel survives overlapping blast and casualty-ring recycling");Check(report.overlapTexels>0&&scorch.Count==2,"Both impacts accumulate independent nuclear paint");Check(world.definition.Edits.Count==0,"Scorch remains functional when geometric terrain deformation is disabled");for(int i=0;i<3;i++)yield return null;Capture("overlapping-impacts");
        }
        void OnDestroy(){if(target!=null){target.Release();Destroy(target);}}
    }
}
