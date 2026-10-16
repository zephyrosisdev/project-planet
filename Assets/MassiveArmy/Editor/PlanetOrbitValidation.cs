using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public static class PlanetOrbitValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        public static void RunAndBuild(){Run();PlanetaryValidation.Build();}
        public static void Run()
        {
            var report=new Report();Directory.CreateDirectory("TestResults/Planetary/OrbitHud");
            void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{
                double mu=9.81*120000*120000d,r=150000,v=Math.Sqrt(mu/r);
                var circular=PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(0,v,0),mu);
                Check(circular.Valid&&circular.Bound&&circular.Eccentricity<1e-10,"Circular orbit is bound with zero eccentricity");
                Check(Math.Abs(circular.Periapsis-r)<1e-6&&Math.Abs(circular.Apoapsis-r)<1e-6,"Circular apsides match altitude");
                Check(Math.Abs(circular.Period-2*Math.PI*Math.Sqrt(r*r*r/mu))<1e-6,"Circular period matches Kepler law");
                double rp=140000,ra=260000,a=(rp+ra)/2;
                var ellipse=PlanetOrbit.Calculate(new Double3(rp,0,0),new Double3(0,Math.Sqrt(mu*(2/rp-1/a)),0),mu);
                Check(Math.Abs(ellipse.Periapsis-rp)<1e-6&&Math.Abs(ellipse.Apoapsis-ra)<1e-6,"Elliptical apsides match known orbit");
                var start=ellipse.PositionAt(ellipse.CurrentAnomaly);Check((start-new Double3(rp,0,0)).Magnitude<1e-6,"Path passes through current ship position");
                Check(Math.Abs(ellipse.PositionAt(Math.PI).Magnitude-ra)<1e-6,"Conic map reaches apoapsis");
                var q=Quaternion.Euler(32,71,-13);var rotated=PlanetOrbit.Calculate(Double3.Rotate(q,new Double3(rp,0,0)),Double3.Rotate(q,new Double3(0,Math.Sqrt(mu*(2/rp-1/a)),0)),mu);
                Check(Math.Abs(rotated.Periapsis-rp)<.1&&Math.Abs(rotated.Apoapsis-ra)<.1,"Orbit is independent of planet face and camera orientation");
                var escape=PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(0,v*1.6,0),mu);
                Check(escape.Valid&&!escape.Bound&&double.IsPositiveInfinity(escape.Apoapsis),"Hyperbolic escape has no false apoapsis");
                Check(!PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(0,0,0),mu).Valid,"Stationary ship does not report a stable orbit");
                Check(!PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(-100,0,0),mu).Valid,"Radial freefall has no artificial orbital plane");
                Check(!PlanetOrbit.Calculate(new Double3(),new Double3(),mu).Valid,"Invalid central position is rejected");
                var impact=PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(0,v*.5,0),mu);Check(impact.Periapsis<120000,"Suborbital trajectory intersects planet despite current altitude");
                var outbound=PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(1800,50,0),mu);
                var inbound=PlanetOrbit.Calculate(new Double3(r,0,0),new Double3(-1800,50,0),mu);
                Check(!outbound.Bound&&outbound.Periapsis<120000&&!outbound.SurfaceThreatAhead(120000),"Outbound escape does not warn about a surface crossing in its past");
                Check(inbound.SurfaceThreatAhead(120000),"Inbound escape conic warns about future surface impact");
                Check(impact.SurfaceThreatAhead(120000),"Bound suborbital path warns about its future surface impact");
                // Compare conic invariants to the production ship's semi-implicit gravity scheme.
                var pos=new Double3(r,0,0);var vel=new Double3(0,v,0);double dt=1d/60;
                for(int i=0;i<12000;i++){double radius=pos.Magnitude;var up=pos.Float.normalized;vel+=new Double3(-up*(float)(mu/(radius*radius)))*dt;pos+=vel*dt;}
                var coast=PlanetOrbit.Calculate(pos,vel,mu);Check(Math.Abs(coast.Periapsis-r)<40&&Math.Abs(coast.Apoapsis-r)<40,"200 seconds of production gravity integration agrees with circular prediction within 40 m");
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();throw;}finally{File.WriteAllText("TestResults/Planetary/OrbitHud/math.json",JsonUtility.ToJson(report,true));}
        }
    }
}
