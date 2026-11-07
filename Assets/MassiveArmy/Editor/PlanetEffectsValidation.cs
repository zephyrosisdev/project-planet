using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetEffectsValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        public static void Run(){var report=new Report();var previous=RenderTexture.active;var target=new RenderTexture(512,512,0,RenderTextureFormat.ARGBFloat);target.Create();var image=new Texture2D(512,512,TextureFormat.RGBAFloat,false);
            void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
            int Pixels(Material material,int vertices,Func<Color,bool> predicate){RenderTexture.active=target;GL.Clear(false,true,Color.clear);if(!material.SetPass(0))throw new InvalidOperationException("Effect shader failed to compile");Graphics.DrawProceduralNow(MeshTopology.Triangles,vertices);image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();int count=0;foreach(var pixel in image.GetPixels())if(predicate(pixel))count++;return count;}
            try{
                var gun=new Material(Resources.Load<Shader>("MassiveArmy/PlanetGunfire"));var shots=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2048,48);var data=new Vector4[2048*3];data[0]=new Vector4(-.03f,0,Mathf.Sqrt(1-.03f*.03f),2);data[1]=new Vector4(.03f,0,data[0].z,2);data[2]=new Vector4(9,0,1,0);shots.SetData(data);
                try{gun.SetBuffer("_Shots",shots);gun.SetFloat("_ShotTick",10);gun.SetFloat("_ShotRadius",100);gun.SetVector("_ShotCentre",new Vector3(0,0,-90));gun.SetVector("_ShotScreen",new Vector4(512,512,0,0));gun.SetMatrix("_ShotVP",GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,1,.1f,30000),true)*Matrix4x4.Scale(new Vector3(1,1,-1)));
                    Check(Pixels(gun,2048*6,p=>p.r>.01f)>100,"Live gunshot records draw visible tracer pixels");gun.SetFloat("_ShotTick",20);Check(Pixels(gun,2048*6,p=>p.r>.01f)==0,"Expired gunshot records disappear");
                }finally{shots.Dispose();UnityEngine.Object.DestroyImmediate(gun);}
                var source=Resources.Load<Texture2D>("MassiveArmy/CombatArt/NukeCraterAlbedo");var normalSource=Resources.Load<Texture2D>("MassiveArmy/CombatArt/NukeCraterNormal");var colour=new Texture2DArray(source.width,source.height,1,source.format,true,false);var normal=new Texture2DArray(normalSource.width,normalSource.height,1,normalSource.format,true,true);colour.Apply(false,true);normal.Apply(false,true);
                for(int mip=0;mip<source.mipmapCount;mip++){Graphics.CopyTexture(source,0,mip,colour,0,mip);Graphics.CopyTexture(normalSource,0,mip,normal,0,mip);}
                var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/MassiveArmy/Editor/PlanetScorchProbe.shader"));Planetary.PlanetNuclearScorch scorch=null;
                try{probe.SetTexture("_PlanetRemainsColours",colour);probe.SetFloat("_PlanetRadius",6371000);scorch=new Planetary.PlanetNuclearScorch(probe,colour,normal,Resources.Load<CombatSurfaceLibrary>("MassiveArmy/CombatArt/SurfaceLibrary").normalNeutral[0]);scorch.Paint(Vector3.right,120,6371000);
                    Check(Pixels(probe,3,p=>p.a>.5f&&p.r<.8f)>1000,"A 120 m nuclear mark remains visible on an Earth-size planet");
                }finally{scorch?.Dispose();UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(colour);UnityEngine.Object.DestroyImmediate(normal);}
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);Directory.CreateDirectory("TestResults/Planetary/RtsRestore");File.WriteAllText("TestResults/Planetary/RtsRestore/effects.json",JsonUtility.ToJson(report,true));}
        }
    }
}
