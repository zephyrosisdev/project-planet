using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
namespace MassiveArmy.EditorTools {
    public static class PlanetGazeDetailValidation {
        const string Folder="TestResults/Planetary/GazeDetail";
        [Serializable]sealed class Report {public string status="PASS",error="",scope="Isolated Editor GPU residency generation at 2560x1440, fenced CPU+GPU latency. Not standalone frame-time or army acceptance.";public List<string> checks=new List<string>();public double cameraMedianMs,cameraP95Ms,gazeMedianMs,gazeP95Ms;public long bufferBytes;public int budget;}
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){string path=Folder+"/request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(path))return;File.Delete(path);Run();}
        static float Span(Vector4[] patches,Vector3 direction){CubeSphere.Project(direction,out var face,out var uv);float span=2;foreach(var p in patches)if((int)p.w==(int)face&&uv.x>=p.x-1e-6f&&uv.x<=p.x+p.z+1e-6f&&uv.y>=p.y-1e-6f&&uv.y<=p.y+p.z+1e-6f)span=Mathf.Min(span,p.z);return span;}
        [MenuItem("Synthetic Front/Planets/Validate camera gaze terrain detail")]
        public static void Run(){
            var report=new Report();var body=ScriptableObject.CreateInstance<PlanetDefinition>();body.terrainHeight=0;body.oceanHeight=-10;
            var host=new GameObject("Camera gaze terrain fixture");var camera=host.AddComponent<Camera>();camera.enabled=false;var target=new RenderTexture(2560,1440,24);camera.targetTexture=target;camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=10000000;
            void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                var surface=new PlanetSurface(body);int improved=0;
                foreach(float radius in new[]{120000f,1200000f})foreach(float degrees in new[]{12f,20f,27f}){
                    body.radius=radius;var eye=new Double3(0,0,radius*1.2);var axis=Quaternion.Euler(0,degrees,0)*Vector3.forward;var point=new Double3(axis)*radius;var forward=(point-eye).Float.normalized;
                    Check(PlanetTerrainGaze.TryFocus(surface,eye,forward,out var focus),"Gaze intersects the visible planet at "+radius+" / "+degrees+" degrees");
                    Check(Vector3.Dot(focus.normalized,axis)>.99999f&&Vector3.Dot(focus.normalized,Vector3.forward)<.99f,"Focus follows viewed latitude/longitude rather than nadir at "+degrees+" degrees");
                    Check(Math.Abs(focus.magnitude-radius-radius*.2)<radius*1e-5,"Focus preserves orbital altitude rather than requesting ground-scale triangles");
                    camera.transform.SetPositionAndRotation(eye.Float,Quaternion.LookRotation(forward,Vector3.up));
                    using(var control=new PlanetStreamedTerrain(body))using(var gaze=new PlanetStreamedTerrain(body)){
                        control.Update(camera,Vector3.zero,force:true);gaze.Update(camera,Vector3.zero,force:true,detailFocus:focus);var before=control.ReadPatchesForValidation();var after=gaze.ReadPatchesForValidation();float a=Span(before,axis),b=Span(after,axis);
                        Check(b<=a,"Viewed terrain retains or improves mesh LOD: "+a+" → "+b+" at "+degrees+" degrees");if(b<a)improved++;
                        Check(after.Length>0&&after.Length<=gaze.PatchBudget&&gaze.GpuLeafCount<=gaze.PatchBudget&&gaze.BufferBytes==control.BufferBytes,"Gaze refinement stays inside the unchanged GPU patch and memory budgets");
                    }
                }
                Check(improved>0,"Off-centre look targets receive finer GPU mesh patches than camera-position prioritisation");
                var observer=new Double3(0,0,body.radius*1.2);Check(!PlanetTerrainGaze.TryFocus(surface,observer,Vector3.forward,out _),"Looking into space uses the normal observer fallback");
                Check(!PlanetTerrainGaze.TryFocus(surface,observer,Vector3.zero,out _),"A zero view direction cannot produce a NaN focus");
                using(var adaptive=new PlanetCbtTerrain(body,8)){
                    Check(PlanetTerrainGaze.TryFocus(surface,observer,camera.transform.forward,out var adaptiveFocus),"Adaptive terrain receives the same camera gaze focus");
                    for(int i=0;i<8;i++)adaptive.Update(camera,Vector3.zero,detailFocus:adaptiveFocus);
                    var stats=new uint[8];adaptive.Statistics.GetData(stats);Check(stats[1]>0&&stats[0]<=12*(1<<8),"Adaptive CBT gaze refinement produces visible geometry within its unchanged tree capacity");
                }
                body.radius=1200000;body.terrainHeight=1800;var origin=new Double3(0,0,body.radius*1.2);var aim=new Double3((Quaternion.Euler(0,20,0)*Vector3.forward))*body.radius;var look=(aim-origin).Float.normalized;camera.transform.SetPositionAndRotation(origin.Float,Quaternion.LookRotation(look,Vector3.up));
                using(var terrain=new PlanetStreamedTerrain(body)){
                    report.bufferBytes=terrain.BufferBytes;report.budget=terrain.PatchBudget;var telemetry=new uint[4];
                    var before=new List<double>();var after=new List<double>();
                    void Measure(bool gaze,bool keep){var timer=Stopwatch.StartNew();Vector3? detail=null;if(gaze&&PlanetTerrainGaze.TryFocus(surface,origin,look,out var focus))detail=focus;terrain.Update(camera,Vector3.zero,force:true,detailFocus:detail);terrain.Statistics.GetData(telemetry);timer.Stop();if(keep)(gaze?after:before).Add(timer.Elapsed.TotalMilliseconds);}
                    // Interleave both orders after warming both paths, so Editor
                    // shader warm-up and GPU clock drift do not favour the second run.
                    for(int i=0;i<100;i++){Measure((i&1)!=0,i>=20);Measure((i&1)==0,i>=20);}before.Sort();after.Sort();report.cameraMedianMs=before[40];report.cameraP95Ms=before[75];report.gazeMedianMs=after[40];report.gazeP95Ms=after[75];
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);body.Edits.Dispose();UnityEngine.Object.DestroyImmediate(body);}
            Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Gaze detail validation "+report.status+": "+report.checks.Count+" checks");
        }
    }
}
