using System;
namespace MassiveArmy.Planetary {
    /// <summary>Planet-scale visibility for observers, including vehicles falling from orbit.</summary>
    public static class PlanetCameraClipping {
        public static float FarPlane(double distanceFromBodyCentre,double minimum=30000,double multiplier=3) {
            if(double.IsNaN(minimum)||double.IsInfinity(minimum))minimum=30000;
            if(double.IsNaN(multiplier)||double.IsInfinity(multiplier))multiplier=3;
            minimum=Math.Max(1,minimum);multiplier=Math.Max(1,multiplier);
            if(double.IsNaN(distanceFromBodyCentre)||double.IsInfinity(distanceFromBodyCentre))return (float)Math.Min(float.MaxValue/4,minimum);
            // Match the spacecraft camera. Keep both the limb and surface within
            // the frustum without increasing terrain residency or detail budgets.
            return (float)Math.Min(float.MaxValue/4,Math.Max(minimum,Math.Abs(distanceFromBodyCentre)*multiplier));
        }
    }
}
