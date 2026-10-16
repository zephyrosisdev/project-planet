using System;
using UnityEngine;
namespace MassiveArmy
{
    public enum WorldPreset { AllLand,CoastalContinent,Continents,Archipelago,InlandLakes,Custom }
    public enum RegionalClimate { Temperate,Arid,Tropical,Boreal,Alpine,Mixed }
    [Serializable]
    public sealed class WorldGenerationSettings
    {
        public int version=4,seed=73129,size=4096;
        public WorldPreset preset=WorldPreset.CoastalContinent;
        public RegionalClimate climate=RegionalClimate.Temperate;
        [Range(0,100)] public float oceanCoverage=25;
        [Range(1,8)] public int continents=1;
        [Range(0,100)] public float islandAbundance=10,islandSize=8,islandClustering=50,coastRoughness=35;
        [Range(0,100)] public float mountainCoverage=35,ridgeLength=65,foothillWidth=8,valleyWidth=12,erosionStrength=50;
        [Range(0,350)] public float reliefHeight=210;
        [Range(0,100)] public float temperature=61,rainfall=51,climateVariation=25,rainShadow=50;
        [Range(0,100)] public float riverAbundance=40,lakeAbundance=40,lakeSize=4,basinScale=50,fordFrequency=25;
        [Range(1,30)] public float riverWidth=8;
        [Range(0,100)] public float forestCoverage=50,forestPatchSize=25,forestClustering=65,clearingFrequency=20,shrubAbundance=40,rockAbundance=40;
        [Range(0,100)] public float roadDensity=50,roadLoops=35,roadCurvature=40,flatRoutePreference=65;
        [Range(1,20)] public float roadWidth=7;
        [Range(0,100)] public float resourceAbundance=50,resourceRichness=50,resourceClustering=50;
        [Range(90,100)] public float startBalance=90;
        [Range(30,60)] public float startSeparation=30;
        public int Resolution=>size>4096?512:256;
        public WorldGenerationSettings Copy()=>JsonUtility.FromJson<WorldGenerationSettings>(JsonUtility.ToJson(this));
        public string Key=>JsonUtility.ToJson(this);
        public void ApplyPreset(WorldPreset selected)
        {
            preset=selected;if(selected==WorldPreset.Custom)return;
            oceanCoverage=selected==WorldPreset.AllLand||selected==WorldPreset.InlandLakes?0:selected==WorldPreset.Archipelago?65:selected==WorldPreset.Continents?45:25;
            continents=selected==WorldPreset.Continents?3:selected==WorldPreset.Archipelago?6:1;
            islandAbundance=selected==WorldPreset.Archipelago?80:selected==WorldPreset.Continents?35:10;
            islandSize=selected==WorldPreset.Archipelago?12:8;lakeAbundance=selected==WorldPreset.InlandLakes?75:selected==WorldPreset.AllLand?20:40;
        }
        public void ApplyClimate(RegionalClimate selected)
        {
            climate=selected;temperature=selected==RegionalClimate.Arid?80:selected==RegionalClimate.Tropical?85:selected==RegionalClimate.Boreal?25:selected==RegionalClimate.Alpine?18:61;
            rainfall=selected==RegionalClimate.Arid?20:selected==RegionalClimate.Tropical?80:selected==RegionalClimate.Boreal?55:51;
        }
        public void Validate()
        {
            if(version!=4)throw new ArgumentException("Unsupported world settings version.");
            if(size!=2048&&size!=4096&&size!=8192&&size!=16384)throw new ArgumentException("Choose a supported map size.");
            if(!Enum.IsDefined(typeof(WorldPreset),preset)||!Enum.IsDefined(typeof(RegionalClimate),climate))throw new ArgumentException("Unknown world preset or climate.");
            foreach(var f in typeof(WorldGenerationSettings).GetFields())if(f.FieldType==typeof(float)){
                float value=(float)f.GetValue(this);var range=(RangeAttribute)Attribute.GetCustomAttribute(f,typeof(RangeAttribute));
                if(!float.IsFinite(value)||(range!=null&&(value<range.min||value>range.max)))throw new ArgumentException("Invalid "+f.Name);
            }
            if(continents<1||continents>8)throw new ArgumentException("Continent count must be 1–8.");
            if(oceanCoverage>=100)throw new ArgumentException("A playable world needs dry land for both starts; reduce ocean coverage below 100%. ");
            if(riverAbundance>0&&oceanCoverage==0&&lakeAbundance==0)throw new ArgumentException("Rivers need ocean or lake sinks.");
        }
    }
}
