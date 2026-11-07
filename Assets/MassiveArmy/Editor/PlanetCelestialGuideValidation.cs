using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetCelestialGuideValidation {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate celestial orbit guides")]
        public static void Run(){var report=new Report();var owned=new List<UnityEngine.Object>();void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{var settings=ScriptableObject.CreateInstance<GalaxyDefinition>();owned.Add(settings);settings.starSystems=2;settings.planetsPerSystem=2;var local=ScriptableObject.CreateInstance<PlanetSystemDefinition>();owned.Add(local);var home=ScriptableObject.CreateInstance<PlanetDefinition>();owned.Add(home);home.radius=120000;home.bodyId=1;var model=new GalaxyModel(settings,local,home,out var planets);foreach(var planet in planets)if(planet!=home)owned.Add(planet);var states=new CelestialState[model.Nodes.Length];
                foreach(double time in new[]{0d,60,300,900,10000,100000}){model.Evaluate(time,states);for(int node=0;node<model.Nodes.Length;node++){double difference=(CelestialOrbitPath.Absolute(model.Nodes,node,time)-states[node].position).Magnitude;Check(difference<.01,"Displayed ephemeris matches actual moving body "+model.Nodes[node].name+" at "+time+" s within 1 cm");}}
                foreach(var body in model.Nodes)if(CelestialOrbitPath.HasOrbit(body)){double period=CelestialOrbitPath.Period(body);Check((CelestialOrbitPath.Relative(body,period)-CelestialOrbitPath.Relative(body,0)).Magnitude<.1,"Parent-frame orbit closes after one period for "+body.name);}
                var root=new CelestialBody{parent=-1};var planetBody=new CelestialBody{parent=0,kind=CelestialKind.Planet,semiMajor=5000000,meanMotion=.0001,phase=.3,eccentricity=.1,plane=Quaternion.Euler(30,70,0)};var moon=new CelestialBody{parent=1,kind=CelestialKind.Moon,semiMajor=120000,meanMotion=-.003,phase=.7,eccentricity=.2,plane=Quaternion.Euler(45,13,88)};var hierarchy=new[]{root,planetBody,moon};
                foreach(double time in new[]{0d,60,300,900})Check((CelestialOrbitPath.Absolute(hierarchy,2,time)-CelestialOrbitPath.Relative(planetBody,time)-CelestialOrbitPath.Relative(moon,time)).Magnitude<1e-8,"Moon forecast includes the moving parent planet at "+time+" s");
                Check((CelestialOrbitPath.Relative(moon,0)-CelestialOrbitPath.Relative(moon,CelestialOrbitPath.Period(moon))).Magnitude<1e-6,"Inclined retrograde moon has the correct orbit period");Check(!CelestialOrbitPath.HasOrbit(root),"Central bodies do not acquire invented orbit paths");
                var sweep=typeof(PlanetSpacecraft).GetMethod("CruiseSphereEntry",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                var sweepArgs=new object[]{new Double3(0,0,-200000),new Double3(0,0,1),5000000d,100000d,0d};
                Check((bool)sweep.Invoke(null,sweepArgs)&&Math.Abs((double)sweepArgs[4]-100000)<.001,"Warp sphere sweep catches a destination crossed entirely in one frame");
                sweepArgs=new object[]{new Double3(200000,0,-200000),new Double3(0,0,1),5000000d,100000d,0d};Check(!(bool)sweep.Invoke(null,sweepArgs),"Warp sweep permits a genuine flyby outside the destination shell");
                Gpu(Check);
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{foreach(var item in owned)UnityEngine.Object.DestroyImmediate(item);}
            Directory.CreateDirectory("TestResults/Planetary/CelestialGuides");File.WriteAllText("TestResults/Planetary/CelestialGuides/editor.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Celestial orbit guides PASS: "+report.checks.Count+" checks");
        }
        static void Gpu(Action<bool,string> check){var shader=Resources.Load<Shader>("MassiveArmy/PlanetCelestialOrbit");var material=new Material(shader);var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,193,16);var data=new Vector4[193];var target=new RenderTexture(512,512,0,RenderTextureFormat.ARGBFloat);target.Create();var image=new Texture2D(512,512,TextureFormat.RGBAFloat,false);var previous=RenderTexture.active;
            try{material.SetBuffer("_GuidePoints",buffer);material.SetVector("_GuideOrigin",Vector3.zero);material.SetVector("_GuideScreen",new Vector4(512,512,.1f,193));material.SetColor("_GuideColour",new Color(.2f,.7f,1,.6f));material.SetMatrix("_GuideVP",GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,1,.1f,30000),true)*Matrix4x4.Scale(new Vector3(1,1,-1)));
                foreach(float scale in new[]{1f,100000000f}){for(int i=0;i<data.Length;i++){float angle=i*Mathf.PI*2/(data.Length-1);data[i]=new Vector4(Mathf.Cos(angle)*8*scale,Mathf.Sin(angle)*3*scale,20*scale,1);}buffer.SetData(data);RenderTexture.active=target;GL.Clear(false,true,Color.clear);check(material.SetPass(0),"Celestial orbit shader compiles");Graphics.DrawProceduralNow(MeshTopology.Triangles,192*4*6);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();int pixels=0;foreach(var p in image.GetPixels())if(p.b>.005f)pixels++;check(pixels>500,"Celestial orbit ribbon remains visible at spatial scale "+scale+" without far-plane clipping");}
            }finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);buffer.Dispose();UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
