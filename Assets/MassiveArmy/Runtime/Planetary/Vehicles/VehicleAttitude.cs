using UnityEngine;
namespace MassiveArmy.Planetary
{
    public static class VehicleAttitude
    {
        public static Vector2 SuspensionAngles(Quaternion heading,Vector3 normal){
            var local=Quaternion.Inverse(heading)*normal.normalized;
            // Unity positive X pitch points the nose DOWN. Uphill normals have negative local Z.
            return new Vector2(Mathf.Clamp(Mathf.Atan2(local.z,local.y)*Mathf.Rad2Deg,-38,38),Mathf.Clamp(Mathf.Atan2(local.x,Mathf.Sqrt(local.y*local.y+local.z*local.z))*Mathf.Rad2Deg,-28,28));
        }
    }
}
