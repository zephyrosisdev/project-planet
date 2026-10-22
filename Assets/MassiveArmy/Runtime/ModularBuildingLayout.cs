using System;
using UnityEngine;
namespace MassiveArmy {
    public sealed class ModularBuildingLayout:ScriptableObject {
        [Serializable]public sealed class Room {public string name;public Bounds bounds;public Mesh mesh;}
        [Serializable]public struct Portal {public int a,b,door;public bool window;public Bounds bounds;public Vector3 normal;}
        [Serializable]public struct Door {public Vector3 centre;public float yaw,width,height;public Mesh panel;}
        public int securityGate=-1;
        public string buildingId;public Mesh exterior,flat,proxy,collision,glass;public Material material,glassMaterial;
        public Room[] rooms;public Portal[] portals;public Door[] doors;public Bounds bounds;public float detailDistance;
        public Vector3 numberPosition;public float numberYaw;
    }
}
