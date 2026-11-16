using System;
using System.IO;
using System.Xml.Serialization;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class ShipRecallLandingAnalysis {
        public static void Run(){
            var args=Environment.GetCommandLineArgs();string folder=args[Array.IndexOf(args,"-recall-capture")+1];
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(folder,"definition.json")),definition);
            var serializer=new XmlSerializer(typeof(SurvivalShipState),new XmlRootAttribute("SurvivalShipState"));SurvivalShipState ship;using(var stream=File.OpenRead(Path.Combine(folder,"ship.xml")))ship=(SurvivalShipState)serializer.Deserialize(stream);
            try{
                var surface=new PlanetSurface(definition);var at=new Double3(ship.pose.x,ship.pose.y,ship.pose.z);var rotation=new Quaternion(ship.rotationX,ship.rotationY,ship.rotationZ,ship.rotationW);
                var ramp=at+Double3.Rotate(rotation,new Double3(0,0,-52));var foot=surface.ProjectToSurface(ramp,.05);double footY=Double3.Rotate(Quaternion.Inverse(rotation),foot-at).y;
                var report=new LandingReport{radius=definition.radius,terrain=surface.SampleHeight(at),centreAltitude=at.Magnitude-definition.radius,rampFootY=footY,rampAngle=Math.Atan2(-4-footY,14)*180/Math.PI,position=at};
                report.minimumFootClearance=double.MaxValue;report.maximumFootClearance=double.MinValue;
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){var p=at+Double3.Rotate(rotation,new Double3(x*26,-8,z*50));double clearance=p.Magnitude-definition.radius-surface.SampleHeight(p);report.minimumFootClearance=Math.Min(report.minimumFootClearance,clearance);report.maximumFootClearance=Math.Max(report.maximumFootClearance,clearance);}
                File.WriteAllText(Path.Combine(folder,"analysis.json"),JsonUtility.ToJson(report,true));Debug.Log(JsonUtility.ToJson(report));
            }finally{UnityEngine.Object.DestroyImmediate(definition);}
        }
        [Serializable] sealed class LandingReport {public float radius,terrain;public double centreAltitude,rampFootY,rampAngle,minimumFootClearance,maximumFootClearance;public Double3 position;}
    }
}
