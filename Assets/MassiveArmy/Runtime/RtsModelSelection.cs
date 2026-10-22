using UnityEngine;
namespace MassiveArmy
{
    // A single catalogue choice at view creation; never a per-unit lookup or object.
    public sealed class RtsModelSelection:ScriptableObject
    {
        public RtsModelArt catalogue;
    }
}
