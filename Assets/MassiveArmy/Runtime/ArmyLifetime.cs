using UnityEngine;

namespace MassiveArmy
{
    static class ArmyLifetime
    {
        public static void Destroy(Object asset)
        {
            if(asset==null)return;
            if(Application.isPlaying)Object.Destroy(asset);
            else Object.DestroyImmediate(asset);
        }
    }
}
