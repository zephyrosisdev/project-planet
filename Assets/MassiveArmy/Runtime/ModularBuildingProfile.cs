using System;
using UnityEngine;
namespace MassiveArmy {
    public enum ModularUse {Home,Office,Workshop,Warehouse,Utility,Prison}
    public enum BuildingModuleKind {Wall,Door,Window,Floor,Stairs,GardenWall,Pillar}
    [Serializable]public sealed class BuildingModule {
        public string id;public BuildingModuleKind kind;public Vector3 size=Vector3.one;
        [Tooltip("Optional mesh, centred on its bounds. Modules share the kit's material/atlas.")]public Mesh mesh;
        public Color colour=Color.white;
    }
    [Serializable]public sealed class BuildingModulePlacement {
        public string module;public Vector3Int cell;[Range(0,3)]public int quarterTurns;public Vector3 size=Vector3.one;
        [Tooltip("-1 is the exterior shell; other indices attach to a generated room.")]public int room=-1;
        public bool collision=true;
    }
    [Serializable]public sealed class ModularRoomSettings {
        [Tooltip("Room index excludes the hall: floor * 4 + rear/front pair. Empty keeps the automatic room name.")]public string name;
        [Tooltip("Offset along the hall wall, in grid cells. Clamped to leave usable jambs.")]public int doorOffset;
        [Min(.9f)]public float doorWidth=1.2f;
    }
    [CreateAssetMenu(menuName="Massive Army/Modular building profile")]
    public sealed class ModularBuildingProfile:ScriptableObject {
        public string buildingId;public bool enabledForGeneration=true;public ModularUse use;
        public ModularBuildingKit kit;
        [Min(.5f)]public float moduleGrid=1;
        [Min(6)]public float width=14,depth=16;
        [Range(1,3)]public int floors=2;
        [Min(2.7f)]public float floorHeight=3.2f;
        [Min(2.6f)]public float hallWidth=3.6f;
        [Min(.12f)]public float wallThickness=.2f;
        [Min(.9f)]public float doorWidth=1.2f;[Min(2.05f)]public float doorHeight=2.3f;
        [Range(.25f,.65f),Tooltip("Fraction of depth occupied by the front room pair. Moves the dividing walls and keeps rooms connected to the hall.")]public float frontRoomFraction=.5f;
        public ModularRoomSettings[] rooms=Array.Empty<ModularRoomSettings>();
        public bool windows=true,pitchedRoof=true,gardenWalls,securePerimeter;
        [Min(.5f)]public float windowWidth=1.6f,windowHeight=1.3f;
        public Vector2 gardenSize=new Vector2(24,24);[Min(.5f)]public float gardenHeight=1.2f;
        [Range(30,150)]public float detailDistance=90;
        public Color exterior=new Color(.65f,.62f,.53f),interior=new Color(.8f,.78f,.71f),roof=new Color(.22f,.25f,.27f),floor=new Color(.4f,.35f,.28f);
        [Tooltip("Grid-aligned additions are combined at bake time; they do not become individual runtime objects.")]public BuildingModulePlacement[] additions=Array.Empty<BuildingModulePlacement>();
        [HideInInspector]public ModularBuildingLayout lastBake;
    }
}
