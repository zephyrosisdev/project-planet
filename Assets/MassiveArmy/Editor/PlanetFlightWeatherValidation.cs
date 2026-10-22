using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetFlightWeatherValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate off-axis weather and flight instruments")]
        public static void Run(){var report=new Report();void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                foreach(float aspect in new[]{1f,16f/9,9f/16})foreach(float z in new[]{1.2f,3f,10f,100f})foreach(float x in new[]{-.8f,0,.8f})foreach(float y in new[]{-.7f,0,.7f}){
                    var centre=new Vector3(x*z,y*z,z);bool visible=PlanetWeatherProjection.Bounds(centre,1,1,aspect,out var bounds);
                    for(int latitude=-80;latitude<=80;latitude+=10)for(int longitude=0;longitude<360;longitude+=10){float lat=latitude*Mathf.Deg2Rad,lon=longitude*Mathf.Deg2Rad;var p=centre+new Vector3(Mathf.Cos(lat)*Mathf.Cos(lon),Mathf.Sin(lat),Mathf.Cos(lat)*Mathf.Sin(lon));if(p.z<=0)continue;float px=p.x/p.z/aspect,py=p.y/p.z;if(Mathf.Abs(px)>1||Mathf.Abs(py)>1)continue;if(!visible||px<bounds.x-1e-5||px>bounds.z+1e-5||py<bounds.y-1e-5||py>bounds.w+1e-5)throw new InvalidOperationException("Weather bounds clip a visible surface point");}
                    Check(true,"Off-axis spherical weather bounds contain all visible surface samples: "+centre+" aspect "+aspect);
                }
                PlanetWeatherProjection.Bounds(new Vector3(8,0,10),1.2f,1,1,out var edgeBounds);Check(edgeBounds.x<.8f-1.2f/8.8f,"Regression: approximate centre/extents rectangle omitted the off-axis silhouette");
                Check(!PlanetWeatherProjection.Bounds(new Vector3(0,0,-3),1,1,1,out _),"Bodies wholly behind the camera are culled");Check(PlanetWeatherProjection.Bounds(Vector3.zero,1,1,1,out var inside)&&inside==new Vector4(-1,-1,1,1),"Inside atmosphere uses the full viewport");
                foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(33,76,12),Quaternion.Euler(151,-57,94)}){
                    var up=rotation*Vector3.up;Check(PlanetFlightInstrumentMath.Bank(rotation,up,out float bank)&&Mathf.Abs(bank)<.001f,"Level roll is relative to the local planetary horizon");Check(PlanetFlightInstrumentMath.Bank(rotation*Quaternion.AngleAxis(45,Vector3.forward),up,out bank)&&Mathf.Abs(bank-45)<.001f,"Bank instrument measures 45 degree roll on any planetary face");Check(!PlanetFlightInstrumentMath.Bank(Quaternion.LookRotation(up,rotation*Vector3.forward),up,out _),"Vertical flight shows undefined bank instead of an unstable horizon");
                }
                Check(PlanetFlightInstrumentMath.TapeStep(1000)>0&&PlanetFlightInstrumentMath.TapeStep(-50)>0&&PlanetFlightInstrumentMath.TapeStep(1e12)>0,"Altitude tape remains finite below sea level and at astronomical distances");
                ValidateGpu(Check);
                ValidateTrajectory(Check);
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/Planetary/FlightWeather");File.WriteAllText("TestResults/Planetary/FlightWeather/editor.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Flight/weather PASS: "+report.checks.Count+" checks");
        }
        static void ValidateTrajectory(Action<bool,string> check){var material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTrajectory"));var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,16,16);var points=new Vector4[16];for(int i=0;i<16;i++){float t=i/15f;points[i]=new Vector4((t-.5f)*12,Mathf.Sin(t*Mathf.PI*2)*3,20,1);}buffer.SetData(points);var target=new RenderTexture(512,512,0,RenderTextureFormat.ARGBFloat);target.Create();var image=new Texture2D(512,512,TextureFormat.RGBAFloat,false);var previous=RenderTexture.active;
            try{material.SetBuffer("_ArcPoints",buffer);material.SetVector("_ArcOrigin",Vector3.zero);material.SetVector("_ArcScreen",new Vector4(512,512,.1f,16));material.SetColor("_ArcColour",Color.green);material.SetInteger("_ArcSubdivisions",4);material.SetMatrix("_ArcVP",GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,1,.1f,1000),true)*Matrix4x4.Scale(new Vector3(1,1,-1)));RenderTexture.active=target;GL.Clear(false,true,Color.clear);check(material.SetPass(0),"Procedural trajectory ribbon shader compiles");Graphics.DrawProceduralNow(MeshTopology.Triangles,15*4*6);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();int visible=0,soft=0;foreach(var pixel in image.GetPixels()){if(pixel.g>.005f)visible++;if(pixel.g>.01f&&pixel.g<.4f)soft++;}check(visible>200&&soft>50,"Smoothed GPU trajectory renders visible anti-aliased edges without VFX simulation");}
            finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);buffer.Dispose();UnityEngine.Object.DestroyImmediate(material);}
        }
        static void ValidateGpu(Action<bool,string> check){var shader=Resources.Load<Shader>("MassiveArmy/PlanetWeather");var material=new Material(shader);var noise=new Texture3D(2,2,2,TextureFormat.RFloat,false);var pixels=new Color[8];for(int i=0;i<8;i++)pixels[i]=Color.white;noise.SetPixels(pixels);noise.Apply();var depth=new Texture2D(256,256,TextureFormat.RFloat,false);depth.SetPixels(new Color[256*256]);depth.Apply();var target=new RenderTexture(128,128,0,RenderTextureFormat.ARGBFloat);target.Create();var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false);var previous=RenderTexture.active;
            try{
                material.SetTexture("_WeatherNoise",noise);material.SetTexture("_WeatherLocalDepth",depth);material.SetTexture("_WeatherSceneDepth",depth);material.SetVector("_WeatherFullSize",new Vector4(256,256,1f/256,1f/256));material.SetVector("_WeatherRight",Vector3.right);material.SetVector("_WeatherUp",Vector3.up);material.SetVector("_WeatherForward",Vector3.forward);material.SetVector("_WeatherSun",Vector3.forward*-1);material.SetVector("_WeatherCloudColour",Vector3.one);material.SetFloat("_WeatherRadius",120000);material.SetVector("_WeatherAtmosphere",new Vector4(.2f,0,.04f,.08f));material.SetVector("_WeatherCloud",new Vector4(1,50,0,1));material.SetVector("_WeatherWind",new Vector4(0,1,0,0));
                Color[] Draw(Vector4 bounds){RenderTexture.active=target;GL.Clear(false,true,Color.clear);material.SetVector("_WeatherBounds",bounds);material.SetPass(1);Graphics.DrawProceduralNow(MeshTopology.Triangles,6);image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();return image.GetPixels();}
                foreach(float flip in new[]{-1f,1f})foreach(var centre in new[]{new Vector3(8,0,10),new Vector3(-8,4,10),new Vector3(3,7,10),new Vector3(0,-8,10)}){
                    material.SetVector("_WeatherView",new Vector4(1,1,flip,0));material.SetVector("_WeatherCentre",centre);var full=Draw(new Vector4(-1,-1,1,1));PlanetWeatherProjection.Bounds(centre,1.2f,1,1,out var bounds);var bounded=Draw(bounds);int lit=0,dropped=0;float maximum=0;for(int i=0;i<full.Length;i++){if(full[i].a>.001f){lit++;maximum=Mathf.Max(maximum,Mathf.Abs(full[i].a-bounded[i].a));if(bounded[i].a<full[i].a-.01f)dropped++;}}
                    check(lit>5&&dropped==0&&maximum<.003f,"Off-axis GPU cloud draw matches full-screen reference at "+centre+" with projection Y "+flip+" (lit "+lit+", lost "+dropped+", max delta "+maximum+")");
                }
            }finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(depth);UnityEngine.Object.DestroyImmediate(noise);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
