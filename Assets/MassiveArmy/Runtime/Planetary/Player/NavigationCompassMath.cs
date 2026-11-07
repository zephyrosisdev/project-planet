using System;
namespace MassiveArmy.Planetary {
    public readonly struct CompassProjection {
        public readonly double bearing,pitchError,scale;public readonly bool valid;
        public CompassProjection(double bearing,double pitchError,double scale,bool valid){this.bearing=bearing;this.pitchError=pitchError;this.scale=scale;this.valid=valid;}
    }
    /// <summary>Pure direction math: the compass wraps yaw independently from pitch.</summary>
    public static class NavigationCompassMath {
        const double Degrees=180/Math.PI;
        public static double Wrap(double angle){if(double.IsNaN(angle)||double.IsInfinity(angle))return 0;angle%=360;if(angle>180)angle-=360;if(angle<=-180)angle+=360;return angle;}
        public static CompassProjection Project(double right,double forward,double vertical,double viewPitch){
            if(!Finite(right)||!Finite(forward)||!Finite(vertical)||!Finite(viewPitch))return new CompassProjection(0,0,.36,false);
            double length=Math.Max(Math.Abs(right),Math.Max(Math.Abs(forward),Math.Abs(vertical)));if(length<1e-12)return new CompassProjection(0,0,.36,false);
            right/=length;forward/=length;vertical/=length;double flat=Math.Sqrt(right*right+forward*forward);
            // Directly above/below has no azimuth: keep it centered and use the pitch cue.
            // Float tangent axes leave ~1e-7 residuals for a planet directly below.
            // Such directions have no usable azimuth; do not feed noise into atan2.
            double bearing=flat<1e-4?0:Math.Atan2(right,forward)*Degrees;
            double error=Wrap(Math.Atan2(vertical,flat)*Degrees-viewPitch);
            double alignment=Math.Cos(Math.Min(90,Math.Abs(error))/Degrees);
            return new CompassProjection(Wrap(bearing),error,.36+.64*alignment*alignment,true);
        }
        public static double StableBearing(double current,double previous){
            current=Wrap(current);previous=Wrap(previous);
            // Near the rear seam, keep the previous side until the target clearly
            // leaves the seam region instead of jumping between opposite bar ends.
            if(Math.Abs(current)>179.5&&Math.Abs(previous)>179.5)return Math.Abs(current)*(previous<0?-1:1);
            return current;
        }
        public static double BarPosition(double bearing)=>Wrap(bearing)/180;
        public static double ArcHeight(double normalized)=>12*normalized*normalized;
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
