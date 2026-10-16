using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>Osculating two-body orbit in double planet-local coordinates; no army state.</summary>
    public struct PlanetOrbit
    {
        public bool Valid,Bound;
        public double Radius,Speed,RadialSpeed,TangentialSpeed,Eccentricity,Periapsis,Apoapsis,Period,CircularSpeed,EscapeSpeed;
        public double Parameter,CurrentAnomaly;
        public Double3 PeriDirection,SideDirection;
        static double Dot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static PlanetOrbit Calculate(Double3 position,Double3 velocity,double mu)
        {
            var o=new PlanetOrbit();double r=position.Magnitude,v=velocity.Magnitude;
            if(!(r>1)||!(mu>0)||double.IsInfinity(r)||double.IsNaN(v))return o;
            o.Radius=r;o.Speed=v;o.RadialSpeed=Dot(position,velocity)/r;
            o.CircularSpeed=Math.Sqrt(mu/r);o.EscapeSpeed=Math.Sqrt(2*mu/r);
            var h=Double3.Cross(position,velocity);double hm=h.Magnitude;
            o.TangentialSpeed=hm/r;
            if(hm<1e-6)return o; // Radial/freefall flight has no defined orbital plane.
            var e=Double3.Cross(velocity,h)*(1/mu)-position*(1/r);double ecc=e.Magnitude;
            o.Eccentricity=ecc;o.Parameter=hm*hm/mu;
            o.PeriDirection=ecc>1e-8?e*(1/ecc):position*(1/r);
            o.SideDirection=Double3.Cross(h*(1/hm),o.PeriDirection);
            o.CurrentAnomaly=Math.Atan2(Dot(position,o.SideDirection),Dot(position,o.PeriDirection));
            o.Periapsis=o.Parameter/(1+ecc);double energy=v*v*.5-mu/r;
            o.Bound=energy<0&&ecc<1;
            o.Apoapsis=o.Bound?o.Parameter/(1-ecc):double.PositiveInfinity;
            double a=o.Bound?-mu/(2*energy):0;o.Period=o.Bound?2*Math.PI*Math.Sqrt(a*a*a/mu):double.PositiveInfinity;
            o.Valid=true;return o;
        }
        public bool SurfaceThreatAhead(double surfaceRadius)=>Valid&&Periapsis<=surfaceRadius&&(Bound||RadialSpeed<0||Radius<=surfaceRadius);
        public Double3 PositionAt(double anomaly)
        {
            double r=Parameter/(1+Eccentricity*Math.Cos(anomaly));
            return (PeriDirection*Math.Cos(anomaly)+SideDirection*Math.Sin(anomaly))*r;
        }
    }
}
