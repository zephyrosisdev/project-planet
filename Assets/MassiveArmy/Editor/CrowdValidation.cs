using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class CrowdValidation
    {
        static IEnumerator routine;static GameObject root;static Battlefield field;static GpuBattle battle;
        static readonly uint[] Indices={3,3,6,6};static Report report;static double deadline;
        [Serializable] sealed class Metrics { public bool pressure;public float movingPeakDensity,movingMinimumGap,forwardProgress,queueMinimumGap,queueStep,queueJitter,queueJitter95,queueJitterWorst; }
        [Serializable] sealed class Report { public string status,error,timestamp,gpu;public Metrics[] comparisons;public string[] checks; }
        static readonly List<Metrics> comparisons=new List<Metrics>();static readonly List<string> checks=new List<string>();
        [MenuItem("Massive Army/Validate Crowd Density and Stability")]
        public static void Start() {
            if(routine!=null)throw new InvalidOperationException("Crowd validation already running");
            comparisons.Clear();checks.Clear();report=new Report{status="PASS",timestamp=DateTime.UtcNow.ToString("O"),gpu=SystemInfo.graphicsDeviceName};
            routine=Run();deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Advance;
        }
        static void Require(bool condition,string text) { if(!condition)throw new InvalidOperationException(text);checks.Add(text); }
        static void Advance() {
            try { if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Crowd validation timed out");if(routine.MoveNext())return; }
            catch(Exception e) { report.status="FAIL";report.error=e.ToString();Debug.LogException(e); }
            EditorApplication.update-=Advance;routine=null;battle?.Dispose();field?.Dispose();battle=null;field=null;
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            report.comparisons=comparisons.ToArray();report.checks=checks.ToArray();File.WriteAllText("TestResults/crowd-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Crowd density/stability validation: "+report.status);
        }
        static UnitState Tank(Vector2 p,Vector2 goal,uint team) => new UnitState {position=p,previous=p,destination=goal,packed=team,flags=1,health=1000,target=uint.MaxValue,group=uint.MaxValue};
        static float Gap(UnitState[] sample) { float gap=float.MaxValue;for(int i=0;i<sample.Length;i++)for(int j=0;j<i;j++)gap=Mathf.Min(gap,Vector2.Distance(sample[i].position,sample[j].position));return gap; }
        static float Peak(UnitState[] sample) {
            const float h=12;float peak=0;
            foreach(var u in sample) {float density=0;foreach(var v in sample) {float q=(u.position-v.position).sqrMagnitude/(h*h);if(q<1)density+=(1-q)*(1-q)*3/(Mathf.PI*h*h)*5.2f*5.2f;}peak=Mathf.Max(peak,density);}
            return peak;
        }
        static IEnumerator Run() {
            root=new GameObject("Crowd pressure validation");var settings=new BattleSettings{activeUnits=128,capacity=128,enemyAI=false,combat=false,targeting=false,simulationLod=false,projectileCapacity=256};
            field=new Battlefield(settings,root.transform);int r=field.Resolution,n=r*r;
            var camera=new GameObject("Crowd camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;
            camera.transform.position=new Vector3(0,1800,0);camera.transform.rotation=Quaternion.Euler(90,0,0);camera.farClipPlane=5000;
            foreach(bool pressure in new[]{false,true}) {
                settings.crowdPressure=pressure;var costs=new float[n*5];for(int i=0;i<costs.Length;i++)costs[i]=1;field.Costs.SetData(costs);
                battle=new GpuBattle(settings,field);var units=new UnitState[128];
                for(int i=0;i<128;i++) {Vector2 p=new Vector2(-400+(i%16)*3.4f,(i/16-3.5f)*3.4f);units[i]=Tank(p,p+Vector2.right*1000,(uint)(i&1));}
                battle.SetValidationUnits(units);var metrics=new Metrics{pressure=pressure};comparisons.Add(metrics);
                for(int tick=0;tick<400;tick++) {battle.Update(camera,BattleSettings.Tick,Indices,false);if(tick%8==0)yield return null;}
                bool done=false;UnitState[] sample=null;battle.ReadUnitSample(0,128,s=>{sample=s;done=true;});while(!done)yield return null;
                if(sample==null)throw new InvalidOperationException("Dense crowd readback failed");
                metrics.movingPeakDensity=Peak(sample);metrics.movingMinimumGap=Gap(sample);float progress=0;
                for(int i=0;i<128;i++) {progress+=sample[i].position.x-units[i].position.x;if(float.IsNaN(sample[i].position.x)||float.IsNaN(sample[i].position.y))throw new InvalidOperationException("Nonfinite fluid position");}
                metrics.forwardProgress=progress/128;
                // Closed gate is a stability control: a full queue must wait rather than vibrate.
                for(int y=0;y<r;y++)for(int x=0;x<r;x++)if(x>=r/2 || y<r/2-4 || y>=r/2+4)costs[2*n+x+y*r]=1e20f;
                field.Costs.SetData(costs);
                for(int i=0;i<128;i++) {Vector2 p=new Vector2(-30-(i/8)*5.2f,(i%8-3.5f)*5.2f);units[i]=Tank(p,new Vector2(600,p.y),(uint)(i&1));}
                battle.SetValidationUnits(units);UnitState[] previous=null;Vector2[] lastStep=null;float movement=0,jitter=0,minGap=100;int measurements=0;var perUnitJitter=new float[128];
                for(int tick=0;tick<1500;tick++) {
                    battle.Update(camera,BattleSettings.Tick,Indices,false);if(tick%8==0)yield return null;if(tick<1260 || tick%4!=0)continue;
                    done=false;battle.ReadUnitSample(0,128,s=>{sample=s;done=true;});while(!done)yield return null;
                    minGap=Mathf.Min(minGap,Gap(sample));
                    if(previous!=null) {
                        var steps=new Vector2[128];for(int i=0;i<128;i++) {steps[i]=sample[i].position-previous[i].position;movement+=steps[i].magnitude;if(lastStep!=null) { float change=(steps[i]-lastStep[i]).magnitude;jitter+=change;perUnitJitter[i]+=change; }}
                        measurements++;lastStep=steps;
                    }
                    previous=sample;
                }
                var rows=new List<string>();foreach(var u in sample)rows.Add($"{u.position.x},{u.position.y},{u.velocity.x},{u.velocity.y}");File.WriteAllLines("TestResults/queue-"+pressure+".csv",rows);
                metrics.queueMinimumGap=minGap;metrics.queueStep=movement/Mathf.Max(1,measurements*128);metrics.queueJitter=jitter/Mathf.Max(1,(measurements-1)*128);
                for(int i=0;i<128;i++)perUnitJitter[i]/=Mathf.Max(1,measurements-1);Array.Sort(perUnitJitter);metrics.queueJitter95=perUnitJitter[121];metrics.queueJitterWorst=perUnitJitter[127];
                File.WriteAllText("TestResults/crowd-progress.json",JsonUtility.ToJson(metrics,true));
                battle.Dispose();battle=null;
            }
            var fluid=comparisons[1];
            Require(fluid.forwardProgress>140,"Dense mixed-team crowd keeps forward progress");
            Require(fluid.movingMinimumGap>=4.15f && fluid.queueMinimumGap>=4.15f,"Dense moving and waiting crowds retain visible hull clearance");
            Require(fluid.movingPeakDensity<1.2f,"Mixed-team crowd decompresses a deliberately overcrowded column");
            Require(fluid.queueJitter95<.05f && fluid.queueJitterWorst<.15f,"Queue vibration stays bounded for individual tanks, including the worst case");
            Require(fluid.queueJitter<.03f,"Settled queue has less than three centimetres of average displacement jitter per four ticks");
        }
    }
}
