using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetDetailValidation {
        const string Folder="TestResults/Planetary/EarlierDetail";
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [InitializeOnLoadMethod]static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){string path=Folder+"/request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(path))return;File.Delete(path);Run();}
        [MenuItem("Synthetic Front/Planets/Select approach detail settings")]
        public static void SelectSettings(){var settings=Resources.Load<PlanetSystemDefinition>("MassiveArmy/Planets/DefaultSystem");Selection.activeObject=settings;EditorGUIUtility.PingObject(settings);EditorApplication.ExecuteMenuItem("Window/General/Inspector");}
        [MenuItem("Synthetic Front/Planets/Validate approach detail distances")]
        public static void Run(){
            var report=new Report();var settings=ScriptableObject.CreateInstance<PlanetSystemDefinition>();var body=ScriptableObject.CreateInstance<PlanetDefinition>();
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);report.checks.Add(message);}
            try{
                foreach(float radius in new[]{120000f,1200000,12000000,120000000}){
                    body.radius=radius;double detail=settings.DetailAltitude(body),prepare=settings.PreparationAltitude(body);
                    Check(detail>=500000&&detail>=radius&&prepare>=detail+radius,"Detail and preparation scale with radius "+radius);
                    var approach=body.position.Add(new Double3(radius+detail-1,0,0));
                    Check(settings.ShouldUseFullDetail(body,approach)&&!settings.ShouldSimulate(body,approach,false),"Full detail is available while surface ticks sleep at radius "+radius);
                    Check(!settings.ShouldUseFullDetail(body,body.position.Add(new Double3(radius+detail+1,0,0))),"Detail uses surface altitude at radius "+radius);
                    var band=body.position.Add(new Double3(radius+settings.activationAltitude+settings.sleepHysteresis*.5,0,0));
                    Check(settings.ShouldSimulate(body,band,true)&&!settings.ShouldSimulate(body,band,false),"Surface simulation hysteresis is preserved at radius "+radius);
                }
                body.radius=1200000;Check(settings.DetailAltitude(body)==1200000&&settings.PreparationAltitude(body)==2400000,"Current 10x home approaches prepare at 2400 km and switch at 1200 km");
                settings.activationAltitude=2000000;Check(settings.DetailAltitude(body)>=2000000&&settings.PreparationAltitude(body)>settings.DetailAltitude(body),"A larger authored simulation range cannot exceed detail preparation");
                settings.activationAltitude=80000;settings.detailActivationRadius=0;settings.detailActivationAltitude=80000;settings.preparationRadius=0;
                Check(settings.DetailAltitude(body)==80000&&settings.PreparationAltitude(body)==280000,"Settings can reproduce the previous approach boundary");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{UnityEngine.Object.DestroyImmediate(body);UnityEngine.Object.DestroyImmediate(settings);}
            Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Planet detail validation "+report.status+": "+report.checks.Count+" checks");
        }
    }
}
