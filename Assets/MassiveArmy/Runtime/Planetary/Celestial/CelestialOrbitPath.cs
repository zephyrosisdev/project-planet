using System;
namespace MassiveArmy.Planetary {
    // View-only ephemeris sampling. Uses the same prescribed Kepler elements as
    // GalaxyModel.Evaluate; never changes celestial or army simulation state.
    public static class CelestialOrbitPath {
        public static bool HasOrbit(CelestialBody body)=>body.parent>=0&&body.semiMajor>0&&Math.Abs(body.meanMotion)>1e-20&&body.eccentricity>=0&&body.eccentricity<1;
        public static Double3 Relative(CelestialBody body,double time){
            if(body.semiMajor<=0)return new Double3();double e=body.eccentricity,m=Math.IEEERemainder(body.phase+body.meanMotion*time,Math.PI*2),E=m;
            if(e>0)for(int k=0;k<8;k++){double correction=(E-e*Math.Sin(E)-m)/(1-e*Math.Cos(E));E-=correction;if(Math.Abs(correction)<1e-13)break;}
            return Double3.Rotate(body.plane,new Double3(body.semiMajor*(Math.Cos(E)-e),0,body.semiMajor*Math.Sqrt(1-e*e)*Math.Sin(E)));
        }
        public static Double3 Absolute(CelestialBody[] nodes,int node,double time){var result=new Double3();int depth=0;while(node>=0&&node<nodes.Length){if(++depth>64)throw new InvalidOperationException("Invalid celestial parent hierarchy");var body=nodes[node];result+=Relative(body,time);node=body.parent;}return result;}
        public static double Period(CelestialBody body)=>HasOrbit(body)?Math.PI*2/Math.Abs(body.meanMotion):0;
    }
}
