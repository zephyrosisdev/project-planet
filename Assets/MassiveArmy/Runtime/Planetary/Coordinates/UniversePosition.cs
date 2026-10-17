using System;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    [Serializable]
    public struct Double3
    {
        public double x,y,z;
        public Double3(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
        public Double3(Vector3 v):this(v.x,v.y,v.z){}
        public double Magnitude=>Math.Sqrt(x*x+y*y+z*z);
        public Vector3 Float=>new Vector3((float)x,(float)y,(float)z);
        public static Double3 operator +(Double3 a,Double3 b)=>new Double3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Double3 operator -(Double3 a,Double3 b)=>new Double3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Double3 operator *(Double3 a,double b)=>new Double3(a.x*b,a.y*b,a.z*b);
        public static Double3 Rotate(Quaternion q,Double3 v)
        {
            // Unity's float quaternion can be slightly non-unit, stretching large spatial offsets.
            // Normalize in double before rotating; never downcast the spatial value itself.
            double norm=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;
            if(norm<1e-30)return v;
            double inverse=1/Math.Sqrt(norm),x=q.x*inverse,y=q.y*inverse,z=q.z*inverse,w=q.w*inverse;
            double tx=2*(y*v.z-z*v.y),ty=2*(z*v.x-x*v.z),tz=2*(x*v.y-y*v.x);
            return new Double3(v.x+w*tx+y*tz-z*ty,v.y+w*ty+z*tx-x*tz,v.z+w*tz+x*ty-y*tx);
        }
        public static Double3 Cross(Double3 a,Double3 b)=>new Double3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }

    /// <summary>Metres, with half-open local offsets. Subtract positions before converting to floats.</summary>
    [Serializable]
    public struct UniversePosition
    {
        public const double SectorSize=1048576;
        public long sectorX,sectorY,sectorZ;
        public Double3 local;
        public UniversePosition(long x,long y,long z,Double3 offset)
        {
            sectorX=x;sectorY=y;sectorZ=z;local=offset;
            Normalize(ref sectorX,ref local.x);Normalize(ref sectorY,ref local.y);Normalize(ref sectorZ,ref local.z);
        }
        static void Normalize(ref long sector,ref double offset)
        {
            if(double.IsNaN(offset)||double.IsInfinity(offset))throw new ArgumentOutOfRangeException(nameof(offset));
            long carry=checked((long)Math.Floor((offset+SectorSize*.5)/SectorSize));
            sector=checked(sector+carry);offset-=carry*SectorSize;
        }
        public UniversePosition Add(Double3 delta)=>new UniversePosition(sectorX,sectorY,sectorZ,local+delta);
        public Double3 RelativeTo(UniversePosition origin)
        {
            // Integer subtraction first preserves close offsets even near long's limits.
            return new Double3(Difference(sectorX,origin.sectorX)*SectorSize,Difference(sectorY,origin.sectorY)*SectorSize,Difference(sectorZ,origin.sectorZ)*SectorSize)+local-origin.local;
        }
        static double Difference(long a,long b){try{return checked(a-b);}catch(OverflowException){return (double)a-(double)b;}}
    }
}
