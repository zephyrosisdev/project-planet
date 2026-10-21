using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class PlanetStampCacheValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate Stamp Cache Reload")]
        public static void Run()
        {
            var report=new Report();PlanetStampLibrary library=null;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);report.checks.Add(message);}
            try{
                library=UnityEngine.Object.Instantiate(PlanetStampLibrary.Shared);
                var cache=typeof(PlanetStampLibrary).GetField("heights",BindingFlags.Instance|BindingFlags.NonPublic);
                Check(cache.IsDefined(typeof(NonSerializedAttribute),false),"Height cache is excluded from editor hot-reload serialization");
                var points=new[]{Vector3.up,Vector3.down,Vector3.right,new Vector3(.3f,.4f,.5f).normalized};var expected=new float[points.Length];
                for(int i=0;i<points.Length;i++)expected[i]=library.Height(points[i],73129);
                foreach(int length in new[]{0,1,PlanetStampLibrary.Resolution*PlanetStampLibrary.Resolution-1}){
                    cache.SetValue(library,new ushort[length]);
                    for(int i=0;i<points.Length;i++)Check(library.Height(points[i],73129)==expected[i],"Invalid cache length "+length+" reloads the original terrain at sample "+i);
                    Check(((ushort[])cache.GetValue(library)).Length==PlanetStampLibrary.Resolution*PlanetStampLibrary.Resolution*library.Count,"Recovered cache contains every stamp slice");
                }
                var retained=cache.GetValue(library);library.Height(points[0],73129);Check(ReferenceEquals(retained,cache.GetValue(library)),"Valid terrain cache is reused without per-query allocation");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}
            finally{if(library!=null)UnityEngine.Object.DestroyImmediate(library);Directory.CreateDirectory("TestResults/Planetary/StampCache");File.WriteAllText("TestResults/Planetary/StampCache/validation.json",JsonUtility.ToJson(report,true));}
            Debug.Log("Planet stamp cache reload validation PASS");
        }
    }
}
