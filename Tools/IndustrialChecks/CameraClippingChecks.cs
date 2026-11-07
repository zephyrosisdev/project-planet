using System;
using MassiveArmy.Planetary;
static class CameraClippingChecks {
    static void Main(){
        int checks=0;
        void Check(bool pass,string label){if(!pass)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        foreach(double radius in new[]{1000d,120000,1200000,6371000})foreach(double altitude in new[]{0d,1000,30000,49300,120000,10000000}){
            double distance=radius+altitude,far=PlanetCameraClipping.FarPlane(distance);
            Check(far>altitude+radius*2&&far>=30000,"Surface and limb remain in range at radius "+radius+", altitude "+altitude);
        }
        Check(PlanetCameraClipping.FarPlane(1249300)==3747900,"Live quad case keeps the 49.3 km distant surface inside its frustum");
        Check(PlanetCameraClipping.FarPlane(0)==30000,"Small local scenes retain the minimum range");
        Check(float.IsFinite(PlanetCameraClipping.FarPlane(double.MaxValue)),"Extreme distances cannot overflow the camera projection");
        Check(PlanetCameraClipping.FarPlane(double.NaN)==30000&&PlanetCameraClipping.FarPlane(double.PositiveInfinity)==30000,"Invalid distances use a finite fallback");
        Check(PlanetCameraClipping.FarPlane(1249300,100000,6)==7495800,"Pilot profile doubles the previous planet-scale range");
        Check(PlanetCameraClipping.FarPlane(1000,500000,6)==500000,"Minimum far range is adjustable independently of body distance");
        Check(PlanetCameraClipping.FarPlane(1200000,double.NaN,double.NaN)==3600000,"Invalid profile numbers retain safe defaults");
        Check(PlanetCameraClipping.FarPlane(1200000,-1,-5)==1200000,"Invalid negative profile ranges cannot reverse the frustum");
        Console.WriteLine(checks+" camera clipping checks passed");
    }
}
