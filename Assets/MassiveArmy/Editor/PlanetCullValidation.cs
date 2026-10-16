using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class PlanetCullValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public int visibleSamples;}
        public static void Run(){var report=new Report();const int count=4096;var cameraObject=new GameObject("Cull reference validation"){hideFlags=HideFlags.HideAndDontSave};var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.aspect=1.6f;camera.nearClipPlane=.1f;
            void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{var records=new UnitState[count];for(int i=0;i<count;i++){float z=1-2*(i+.5f)/count,a=i*2.39996323f,r=Mathf.Sqrt(1-z*z);records[i]=new UnitState{position=CubeSphere.Encode(new Vector3(r*Mathf.Cos(a),z,r*Mathf.Sin(a))),flags=1,health=1000,packed=(uint)(i%4)<<8};}
                var traits=new[]{new UnitTraits{scale=new Vector4(1,2,1,0)},new UnitTraits{scale=new Vector4(20,50,25,4)},new UnitTraits{scale=new Vector4(120,80,60,0),altitude=2000},new UnitTraits{scale=new Vector4(.25f,1,.25f,0),altitude=100}};
                using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,64))using(var types=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,64))using(var visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count*3,4))using(var args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,15,4)){
                    units.SetData(records);types.SetData(traits);
                    foreach(float radius in new[]{6000f,120000f})foreach(int edits in new[]{0,1,32}){var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=radius;definition.terrainHeight=240;definition.oceanHeight=-35;var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetArtCull"));
                        try{for(int i=0;i<edits;i++){float z=1-2*(i+.5f)/Mathf.Max(1,edits),a=i*2.39996323f,r=Mathf.Sqrt(1-z*z);definition.Edits.AddCrater(new Vector3(r*Mathf.Cos(a),z,r*Mathf.Sin(a)),120);}PlanetDevelopment.BindSurface(shader,definition);int kernel=shader.FindKernel("Cull");shader.SetBuffer(kernel,"_PlanetUnits",units);shader.SetBuffer(kernel,"_Traits",types);shader.SetBuffer(kernel,"_Visible",visible);shader.SetBuffer(kernel,"_Arguments",args);shader.SetInt("_Capacity",count);shader.SetInt("_Count",count);shader.SetFloat("_ProjectionScale",1000/(2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)));
                            for(int view=0;view<4;view++){var direction=view==0?Vector3.forward:view==1?Vector3.up:new Vector3(1,.2f,1).normalized;float distance=view<2?radius+400:view==2?radius*2:radius*8;camera.transform.position=direction*distance;var forward=view==1?Vector3.right:-direction;camera.transform.rotation=Quaternion.LookRotation(forward,Mathf.Abs(Vector3.Dot(forward,Vector3.up))>.95f?Vector3.forward:Vector3.up);camera.farClipPlane=radius*20;var planes=GeometryUtility.CalculateFrustumPlanes(camera);shader.SetVectorArray("_Frustum",planes.Select(p=>new Vector4(p.normal.x,p.normal.y,p.normal.z,p.distance)).ToArray());shader.SetVector("_CameraLocal",camera.transform.position);
                                List<uint> Read(int coarse){args.SetData(new uint[15]);shader.SetInt("_ConservativeCull",coarse);shader.Dispatch(kernel,count/128,1,1);var counters=new uint[15];args.GetData(counters);var values=new uint[count*3];visible.GetData(values);var result=new List<uint>();for(int mode=0;mode<3;mode++)for(int i=0;i<counters[mode*5+1];i++)result.Add(values[mode*count+i]|((uint)mode<<24));result.Sort();return result;}
                                var reference=Read(0);var optimized=Read(1);report.visibleSamples+=reference.Count;Check(reference.SequenceEqual(optimized),"Conservative GPU culling preserves every visible unit and LOD: R="+radius+", edits="+edits+", view="+view);
                            }
                        }finally{UnityEngine.Object.DestroyImmediate(shader);UnityEngine.Object.DestroyImmediate(definition);}
                    }
                }Check(report.visibleSamples>100,"Visibility comparison includes rendered units, naval surfaces and high-altitude large bounds");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}finally{UnityEngine.Object.DestroyImmediate(cameraObject);Directory.CreateDirectory("TestResults/Planetary/TerrainFlight");File.WriteAllText("TestResults/Planetary/TerrainFlight/culling.json",JsonUtility.ToJson(report,true));}
        }
    }
}
