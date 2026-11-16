using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Networking;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        [Header("Shared new-world generation")]
        public SurvivalWorldPreset worldPreset;
        [Tooltip("Optional separate offline save slot. Empty keeps the existing seed-based save location.")]
        public string offlineWorldId="";
        SurvivalUniverse pendingPresetUniverse;
        bool sharedPresetPrepared;
        internal string OfflineIndustryPath=>OfflineSavePath(terrainSeed,offlineWorldId);
        internal static string OfflineSavePath(int seed,string slot){
            string root=Path.Combine(Application.persistentDataPath,"Survival",seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(!string.IsNullOrEmpty(slot)){if(slot.Length>64)throw new ArgumentException("Offline world slot is too long");foreach(char c in slot)if(!(char.IsLetterOrDigit(c)||c=='-'||c=='_'))throw new ArgumentException("Offline world slot must use letters, numbers, underscores or hyphens");root=Path.Combine(root,"Worlds",slot);}return Path.Combine(root,"industry.xml");
        }
        void PrepareSharedWorld(string[] args){
            if(worldPreset==null||NetworkPresentation||SystemRuntime!=null||!IsSurvival)return;
            string Argument(string name){int at=Array.IndexOf(args,name);return at>=0&&at+1<args.Length?args[at+1]:null;}
            if(Argument("-survival-world") is string slot)offlineWorldId=slot;
            string worldId="offline-"+terrainSeed+(string.IsNullOrEmpty(offlineWorldId)?"":"-"+offlineWorldId);
            var saved=IndustrialAuthority.Load(OfflineIndustryPath);
            if(saved.universe!=null){pendingPresetUniverse=SurvivalUniverse.Restore(saved.universe,worldId,terrainSeed);Debug.Log("Loading saved offline planet definitions; new-world preset changes are not applied to this save.");}
            else {
                float scale=systemSizeMultiplier;string multiplier=Argument("-planet-size-multiplier");if(multiplier!=null&&!float.TryParse(multiplier,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out scale))throw new ArgumentException("Invalid planet size multiplier");
                var preset=Argument("-world-preset") is string path?SurvivalWorldPreset.Load(Path.GetFullPath(path)):worldPreset.Capture();
                pendingPresetUniverse=SurvivalUniverse.CreateFromPreset(worldId,"offline",terrainSeed,preset,scale);
                if(saved.actors.Count>0)Debug.LogWarning("This legacy offline save has no recorded universe definition. Capturing the current generation settings on first use; saved object coordinates are not rescaled.");
            }
            if(Array.IndexOf(args,"-planet-radius")>=0)throw new InvalidOperationException("Use a world preset and a separate offline save slot to change planet radius");
            if(ownsDefinition)ArmyLifetime.Destroy(definition);ConfigureDefinition(pendingPresetUniverse.Definition(1));configuredRadius=definition.radius;systemSizeMultiplier=pendingPresetUniverse.Settings.RuntimeSizeMultiplier;systemDefinition=pendingPresetUniverse.Settings;sharedPresetPrepared=true;
        }
        void InitializeSharedSystem(){SystemRuntime=gameObject.AddComponent<PlanetSystemRuntime>();SystemRuntime.InitializeShared(this,pendingPresetUniverse);pendingPresetUniverse=null;}
        void DisposePendingWorldPreset(){pendingPresetUniverse?.Dispose();pendingPresetUniverse=null;}
    }
}
