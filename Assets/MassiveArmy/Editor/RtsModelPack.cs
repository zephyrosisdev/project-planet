using System;
using UnityEngine;
namespace MassiveArmy.EditorTools
{
    public enum ModelSourceKind { Original, MeshOrPrefab, Cube, Procedural }
    public enum ModelProceduralShape { Cylinder, Pyramid, Vehicle, StackedBuilding }
    [Serializable]public sealed class ModelLodSource
    {
        public ModelSourceKind kind=ModelSourceKind.Cube;
        [Tooltip("A mesh, imported model or prefab. LODGroup levels are selected automatically.")]public UnityEngine.Object model;
        public Material material;
        public Texture2D albedo;
        public Color tint=Color.white;
        public ModelProceduralShape shape;
        public Vector3 rotation;
    }
    [Serializable]public sealed class ModelPackBuilding
    {
        public string id;public bool enabled;public Vector3 size;
        public ModelLodSource lod0=new ModelLodSource(),lod1=new ModelLodSource(),lod2=new ModelLodSource();
    }
    [Serializable]public sealed class ModelPackUnit
    {
        public string id;public bool enabled;public Vector3 size;
        public ModelLodSource lod0=new ModelLodSource(),lod1=new ModelLodSource();
        [Tooltip("Leave empty to bake all eight views from LOD1. Otherwise supply exactly eight views, clockwise from the front.")]public Texture2D[] impostors=Array.Empty<Texture2D>();
    }
    [CreateAssetMenu(menuName="Massive Army/Model pack")]
    public sealed class RtsModelPack:ScriptableObject
    {
        public ModelPackBuilding[] buildings=Array.Empty<ModelPackBuilding>();
        public ModelPackUnit[] units=Array.Empty<ModelPackUnit>();
        [HideInInspector]public RtsModelArt bakedCatalogue;
        [HideInInspector]public string bakedAt;
    }
}
