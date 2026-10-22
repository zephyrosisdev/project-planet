using System;
using UnityEngine;
namespace MassiveArmy {
    [CreateAssetMenu(menuName="Massive Army/Modular building kit")]
    public sealed class ModularBuildingKit:ScriptableObject {
        public Material material,glassMaterial;public BuildingModule[] modules=Array.Empty<BuildingModule>();
    }
}
