using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class PlanetNamesValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();public List<string> examples=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate Planet Names")]
        public static void Run(){
            var report=new Report();void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);report.checks.Add(message);}
            try{
                foreach(int seed in new[]{0,73129,-1,int.MinValue}){
                    var first=new HashSet<string>();var second=new HashSet<string>();bool deterministic=true,readable=true;
                    for(int ordinal=0;ordinal<1536;ordinal++){
                        string name=PlanetNames.Unique(seed,ordinal,first);deterministic&=name==PlanetNames.Unique(seed,ordinal,second);
                        readable&=name.Length>=4&&name.Length<=13&&name[0]>='A'&&name[0]<='Z';foreach(char c in name)readable&=c>='A'&&c<='Z'||c>='a'&&c<='z';
                        if(seed==73129&&ordinal<16)report.examples.Add(name);
                    }
                    Check(deterministic,"Names repeat deterministically for seed "+seed);Check(readable,"Names use English letters, an initial capital and readable lengths");Check(first.Count==1536,"Maximum supported galaxy has unique names");
                }
                Check(PlanetNames.Placeholder("Home planet")&&!PlanetNames.Placeholder("Tarlon"),"Generated names replace placeholders and preserve authored names");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}
            finally{Directory.CreateDirectory("TestResults/Planetary/Names");File.WriteAllText("TestResults/Planetary/Names/validation.json",JsonUtility.ToJson(report,true));}
            Debug.Log("Planet names validation PASS");
        }
    }
}
