using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MassiveArmy
{
    /// <summary>GPU texture arrays in the same order as Biomes.Layers, followed by the road.</summary>
    [CreateAssetMenu(menuName = "Massive Army/Terrain surface library")]
    public sealed class TerrainSurfaceLibrary : ScriptableObject
    {
        public static readonly string[] LayerNames =
            { "Grass", "Sand", "DryGrass", "ForestFloor", "Rock", "Snow", "Mud", "Gravel", "Road" };

        public Texture2DArray albedo;
        // Linear data: RG = tangent normal XY, B = height, A = roughness.
        public Texture2DArray surfaceData;
        public Vector4[] layerParameters;
        [Range(.01f, 1f)] public float heightBlendWidth = .22f;
        [Range(0, 1)] public float heightBlendStrength = .8f;
        [Range(0, 2)] public float normalStrength = .85f;
        [Range(0, .3f)] public float macroVariation = .12f;
        [Header("Natural biome transitions")]
        [Tooltip("Width of the filtered biome masks in world metres. Zero restores the original masks.")]
        [Range(0, 160)] public float biomeBlendWidth = 48;
        [Tooltip("Displacement amplitude of biome boundaries, in metres.")]
        [Range(0, 80)] public float boundaryWarp = 24;
        [Range(8, 256)] public float boundaryNoiseScale = 96;
        [Tooltip("Competition between locally present materials. Zero disables patchiness.")]
        [Range(0, 1)] public float patchStrength = .65f;
        [Range(1, 64)] public float patchScale = 12;
        [Tooltip("Break up road shoulders while keeping the road centre intact.")]
        [Range(0, 1)] public float roadEdgeNoise = .35f;

#if UNITY_EDITOR
        sealed class Binding { public TerrainSurfaceLibrary library; public int revision; }
        static readonly Dictionary<Material,Binding> bindings = new Dictionary<Material,Binding>();
        int revision;
        // Validation may run on an import thread. Apply shader changes on the editor thread.
        void OnValidate() => NotifySettingsChanged();
        public void NotifySettingsChanged() => Interlocked.Increment(ref revision);
        public static bool RefreshLiveMaterials()
        {
            bool changed=false;
            List<Material> expired=null;
            foreach(var pair in bindings)
            {
                var binding=pair.Value;
                if(pair.Key==null||binding.library==null){if(expired==null)expired=new List<Material>();expired.Add(pair.Key);continue;}
                if(binding.revision==binding.library.revision)continue;
                binding.library.Apply(pair.Key);binding.revision=binding.library.revision;changed=true;
            }
            if(expired!=null)foreach(var material in expired)bindings.Remove(material);
            return changed;
        }
#endif
        public void Bind(Material material)
        {
            Apply(material);
#if UNITY_EDITOR
            bindings[material]=new Binding{library=this,revision=revision};
#endif
        }
        public static void Unbind(Material material)
        {
#if UNITY_EDITOR
            if(!ReferenceEquals(material,null))bindings.Remove(material);
#endif
        }

        public void Apply(Material material)
        {
            if (albedo == null || surfaceData == null || layerParameters == null ||
                albedo.depth != LayerNames.Length || surfaceData.depth != LayerNames.Length ||
                layerParameters.Length != LayerNames.Length)
                throw new InvalidOperationException("Terrain surface library is incomplete. Run Massive Army/Art/Rebuild Terrain Textures.");

            material.SetTexture("_SurfaceAlbedo", albedo);
            material.SetTexture("_SurfaceData", surfaceData);
            material.SetVectorArray("_LayerParameters", layerParameters);
            material.SetFloat("_HeightBlendWidth", heightBlendWidth);
            material.SetFloat("_HeightBlendStrength", heightBlendStrength);
            material.SetFloat("_NormalStrength", normalStrength);
            material.SetFloat("_MacroVariation", macroVariation);
            material.SetVector("_BiomeBlend", new Vector4(biomeBlendWidth,boundaryWarp,boundaryNoiseScale,patchStrength));
            material.SetVector("_BiomeDetail", new Vector4(patchScale,roadEdgeNoise,0,0));
        }
    }
}
