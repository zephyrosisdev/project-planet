using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [CreateAssetMenu(menuName="Survival/World Preset")]
    public sealed class SurvivalWorldPreset:ScriptableObject {
        public string presetId="default-survival";
        [Min(1)] public int revision=1;
        public PlanetDefinition homePlanet;
        public PlanetSystemDefinition system;
        public SurvivalWorldPresetData Capture(){
            if(homePlanet==null||system==null)throw new InvalidOperationException("World preset needs a home planet and system");
            var copy=Instantiate(system);
            try{copy.galaxy=null;var json=JsonUtility.ToJson(copy).Replace("\"galaxy\":{\"instanceID\":0}","\"galaxy\":null");var data=new SurvivalWorldPresetData{presetId=presetId,revision=revision,homeJson=JsonUtility.ToJson(homePlanet),systemJson=json,galaxyJson=system.galaxy==null?null:JsonUtility.ToJson(system.galaxy)};data.Validate();return data;}
            finally{ArmyLifetime.Destroy(copy);}
        }
        public static SurvivalWorldPresetData Load(string path=null){
            if(!string.IsNullOrWhiteSpace(path))return SurvivalWorldPresetData.Parse(File.ReadAllText(path));
            var preset=Resources.Load<SurvivalWorldPreset>("MassiveArmy/Planets/DefaultWorldPreset");
            if(preset==null)throw new InvalidOperationException("Shared survival world preset is missing");return preset.Capture();
        }
#if UNITY_EDITOR
        [ContextMenu("Export world preset JSON")]
        void Export(){string path=UnityEditor.EditorUtility.SaveFilePanel("Export survival world preset","","world-preset.json","json");if(path.Length>0)File.WriteAllText(path,JsonUtility.ToJson(Capture(),true));}
#endif
    }
    // Self-contained public definitions, never Unity instance IDs or credentials.
    [Serializable] public sealed class SurvivalWorldPresetData {
        public int formatVersion=1,revision=1;public string presetId,homeJson,systemJson,galaxyJson;
        public string Fingerprint(){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(this)))).Replace("-","");}
        public static SurvivalWorldPresetData Parse(string json){if(string.IsNullOrWhiteSpace(json)||json.Length>1048576)throw new InvalidDataException("World preset is empty or oversized");var data=JsonUtility.FromJson<SurvivalWorldPresetData>(json);if(data==null)throw new InvalidDataException("Invalid world preset JSON");data.Validate();return data;}
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        public void Validate(){
            if(formatVersion!=1||revision<1||string.IsNullOrWhiteSpace(presetId)||presetId.Length>64||string.IsNullOrEmpty(homeJson)||homeJson.Length>16384||string.IsNullOrEmpty(systemJson)||systemJson.Length>16384||galaxyJson?.Length>16384)throw new InvalidDataException("Invalid world preset identity, version or definition size");
            if(homeJson.Contains("\"instanceID\"")||systemJson.Contains("\"instanceID\"")||galaxyJson?.Contains("\"instanceID\"")==true)throw new InvalidDataException("Preset files cannot contain Unity instance references");
            foreach(char c in presetId)if(!(char.IsLetterOrDigit(c)||c=='-'||c=='_'))throw new InvalidDataException("Preset ID must use letters, numbers, underscores or hyphens");
            PlanetDefinition home=null;PlanetSystemDefinition system=null;
            try{home=CreateHome(1);system=CreateSystem(0);
                if(!Finite(home.radius)||home.radius<10||!Finite(home.terrainHeight)||!Finite(home.oceanHeight)||!Finite(home.atmosphereHeight)||home.atmosphereHeight<0||!Finite(home.gravitationalParameter)||home.gravitationalParameter<=0)throw new InvalidDataException("Invalid planet geometry, atmosphere or gravity in world preset");
                if(system.planetCount<1||system.planetCount>8||!Finite(system.radiusRange.x)||!Finite(system.radiusRange.y)||system.radiusRange.x<10||system.radiusRange.y<system.radiusRange.x||!Finite(system.spacing)||system.spacing<=0)throw new InvalidDataException("Invalid planet distribution in world preset");
                var g=system.galaxy;if(g!=null&&(g.starSystems<2||g.starSystems>256||g.planetsPerSystem<1||g.planetsPerSystem>6||!Finite(g.galaxyRadius)||g.galaxyRadius<=0||!Finite(g.systemOrbitRadius)||g.systemOrbitRadius<=0||!Finite(g.stellarMu)||g.stellarMu<=0||!Finite(g.blackHoleMu)||g.blackHoleMu<=0||!Finite(g.binarySeparation)||g.binarySeparation<=0))throw new InvalidDataException("Invalid galaxy generation in world preset");
            }finally{ArmyLifetime.Destroy(home);if(system!=null){ArmyLifetime.Destroy(system.galaxy);ArmyLifetime.Destroy(system);}}
        }
        public PlanetDefinition CreateHome(int seed,float scale=1){
            if(!Finite(scale)||scale<1||scale>1000)throw new ArgumentException("World scale must be between 1 and 1000");
            var home=ScriptableObject.CreateInstance<PlanetDefinition>();try{JsonUtility.FromJsonOverwrite(homeJson,home);home.bodyId=1;home.terrainSeed=seed;home.radius*=scale;home.gravitationalParameter*=(double)scale*scale;return home;}catch{ArmyLifetime.Destroy(home);throw;}
        }
        public PlanetSystemDefinition CreateSystem(float scaleOverride=0){
            var template=ScriptableObject.CreateInstance<PlanetSystemDefinition>();GalaxyDefinition galaxy=null;
            try{JsonUtility.FromJsonOverwrite(systemJson,template);template.galaxy=null;if(!string.IsNullOrEmpty(galaxyJson)){galaxy=ScriptableObject.CreateInstance<GalaxyDefinition>();JsonUtility.FromJsonOverwrite(galaxyJson,galaxy);template.galaxy=galaxy;}
                float scale=scaleOverride==0?template.sizeMultiplier:scaleOverride;if(!Finite(scale)||scale<1||scale>1000)throw new InvalidDataException("World scale must be 1–1000, or 0 to inherit the preset");return template.CreateScaledCopy(scale);
            }finally{ArmyLifetime.Destroy(galaxy);ArmyLifetime.Destroy(template);}
        }
    }
}
