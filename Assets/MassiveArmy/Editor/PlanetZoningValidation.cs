using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class PlanetZoningValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate settlement zoning")]
        public static void Run(){var report=new Report();void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                foreach(float tile in new[]{8f,32f})foreach(var sensitive in new[]{SettlementDistrict.Residential,SettlementDistrict.Commercial,SettlementDistrict.Agricultural}){
                    var zoning=new SettlementZoning(tile);float radius=tile*2,gap=tile*6,minimum=radius*2+gap;
                    foreach(var origin in new[]{Vector3.zero,new Vector3(-500,-700,-300),new Vector3(200000,0,200000)}){
                        zoning.Clear();zoning.Add(origin,radius,sensitive);
                        Check(!zoning.Allows(origin+Vector3.right*(minimum-.5f),radius,SettlementDistrict.Industrial),"Industry respects buffer from "+sensitive+" across body-space buckets at tile size "+tile);
                        Check(zoning.Allows(origin+Vector3.right*(minimum+.5f),radius,SettlementDistrict.Industrial),"Industry may build beyond the buffer");
                        zoning.Clear();zoning.Add(origin,radius,SettlementDistrict.Industrial);
                        Check(!zoning.Allows(origin+Vector3.forward*(minimum-.5f),radius,sensitive),"Separation also applies when sensitive plot is placed second");
                        Check(zoning.Allows(origin+Vector3.right*(radius*2),radius,SettlementDistrict.Industrial),"Industrial sites can cluster together");
                    }
                }
                var farms=new SettlementZoning(32);farms.Add(Vector3.zero,64,SettlementDistrict.Agricultural);Check(farms.Allows(new Vector3(128,0,0),64,SettlementDistrict.Agricultural),"Different farms can share an agricultural cluster");
                var removed=new RtsBuilding{removed=true};farms.Add(new Vector3(1000,0,0),64,SettlementDistrict.Industrial,removed);Check(farms.Allows(new Vector3(1100,0,0),32,SettlementDistrict.Residential),"Demolished industrial sites release their buffer");
                foreach(var district in new[]{SettlementDistrict.Agricultural,SettlementDistrict.Industrial}){
                    Check(!SettlementZoning.OutsideCentre(Vector3.up,Vector3.up,10000,64,32,district),district+" cannot occupy the town centre");
                    Check(SettlementZoning.OutsideCentre(Quaternion.AngleAxis(4,Vector3.forward)*Vector3.up,Vector3.up,10000,64,32,district),district+" can occupy outskirts");
                }
                Check(SettlementZoning.OutsideCentre(Vector3.up,Vector3.up,10000,64,32,SettlementDistrict.Commercial),"Commercial centre remains available");
                var catalogue=JsonUtility.FromJson<CivilisationCatalogue>(File.ReadAllText("Assets/MassiveArmy/Resources/MassiveArmy/Civilisation/Catalogue.json"));
                // Catalogue extension has its own validation; spacing has no dependency on live army buffers.
                Check(catalogue!=null,"Civilisation catalogue remains readable");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/Planetary/Zoning");File.WriteAllText("TestResults/Planetary/Zoning/editor.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Settlement zoning PASS: "+report.checks.Count+" checks");
        }
    }
}
