using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MassiveArmy.Planetary {
    /// <summary>Pure arrival-coordinate regression checks; does not move a live ship or touch saves.</summary>
    public static class PlanetIndustryArrivalValidation {
        [Serializable] sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Synthetic Front/Diagnostics/Validate survival industry arrival")]
#endif
        public static void Run(){
            var report=new Report();PlanetDefinition definition=null;
            void Check(bool valid,string name){if(!valid)throw new InvalidOperationException(name);report.checks.Add(name);}
            try {
                var ship=new Double3(0,0,-300000000);var feet=new Double3(120000,0,0);
                Check((PlanetIndustry.SelectObserverPosition(true,default,ship)-ship).Magnitude==0,"Arriving pilot uses ship position when on-foot controller is uninitialized");
                Check((PlanetIndustry.SelectObserverPosition(false,feet,ship)-feet).Magnitude==0,"Walking observer keeps its actual feet position");
                definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=120000000;definition.terrainHeight=0;definition.stampTerrain=false;
                var surface=new PlanetSurface(definition);
                foreach(var focus in new[]{Vector3.up,Vector3.down,new Vector3(1,.12f,1.3f).normalized}){
                    var prior=new List<Double3>();
                    for(int i=0;i<6;i++){
                        var p=PlanetIndustry.DepositPosition(surface,focus,i);
                        Check(!double.IsNaN(p.Magnitude)&&Math.Abs(p.Magnitude-definition.radius)<.001,"Deposit "+i+" projects to a valid surface at "+focus);
                        foreach(var other in prior)Check((p-other).Magnitude>80,"Distributed deposits stay separated at large coordinates and polar tangent frames");
                        Check((p-PlanetIndustry.DepositPosition(surface,focus,i)).Magnitude<.001,"Deposit placement is repeatable without global random state");prior.Add(p);
                    }
                }
                definition.radius=12000000;surface=new PlanetSurface(definition);
                var legacyDeposit=new MassiveArmy.Industrial.FramePose{body=1,x=10247688.223895207,y=117539095.88485339,z=-21898713.967392869};
                var repaired=PlanetIndustry.SurfaceDepositPose(surface,legacyDeposit);double repairedRadius=Math.Sqrt(repaired.x*repaired.x+repaired.y*repaired.y+repaired.z*repaired.z);
                Check(Math.Abs(repairedRadius-definition.radius)<.001&&repaired.body==legacyDeposit.body&&repaired.frame==legacyDeposit.frame,"Old-scale saved deposit reprojects to the current planet surface with identity frame intact");
                Check(PlanetSpacecraft.CruiseSphereEntry(new Double3(0,0,-300000000),new Double3(0,0,1),1000000000,126000,out double entry)&&Math.Abs(entry-299874000)<.001,"Warp sweep catches a destination boundary inside a long step");
                Check(!PlanetSpacecraft.CruiseSphereEntry(new Double3(0,200000,-300000000),new Double3(0,0,1),1000000000,126000,out _),"Warp near miss does not report a planet collision");
            }catch(Exception error){report.status="FAIL";report.error=error.ToString();Debug.LogException(error);}
            finally {if(definition!=null)ArmyLifetime.Destroy(definition);}
            const string output="TestResults/Industrial/Arrival";Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));
            if(report.status=="PASS")Debug.Log("Survival industry arrival checks passed: "+report.checks.Count);
        }
    }
}
