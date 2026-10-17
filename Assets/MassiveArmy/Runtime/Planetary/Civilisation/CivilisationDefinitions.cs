using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    [Serializable] public sealed class ElementDefinition {
        public string id,name;public bool bootstrap,element;public int legacy=-1;
        public double capacity=20000,mass=1,startingStock;
    }
    [Serializable] public sealed class MaterialQuantity {public string resource;public double amount;public MaterialQuantity(){}public MaterialQuantity(string resource,double amount){this.resource=resource;this.amount=amount;}}
    [Serializable] public sealed class IndustryRecipe {public string id,name,building,requiredTech;public double seconds=10;public MaterialQuantity[] inputs,outputs;}
    [Serializable] public sealed class PlanetArchetype {public string id;public bool breathable;public double temperature=288,pressure=1;public MaterialQuantity[] abundances;}
    [Serializable] public sealed class CropDefinition {public string id,name;public double growthSeconds=120,waterPerSecond=.1,temperature=288,tolerance=30,seedCost=1,soilUse=.05;public MaterialQuantity[] harvest;public Color colour=Color.green;}
    [Serializable] public sealed class CivilisationCatalogue {
        public ElementDefinition[] resources;public IndustryRecipe[] recipes;public PlanetArchetype[] archetypes;
        public BuildingDefinition[] buildings;public TechnologyDefinition[] technologies;
        public CropDefinition[] crops;
        [NonSerialized]public Dictionary<string,ElementDefinition> Resources;
        public static CivilisationCatalogue Load(){var text=UnityEngine.Resources.Load<TextAsset>("MassiveArmy/Civilisation/Catalogue");if(text==null)throw new InvalidOperationException("Missing civilisation catalogue");var result=JsonUtility.FromJson<CivilisationCatalogue>(text.text);result.Validate();return result;}
        public void Validate(){
            Resources=resources.ToDictionary(x=>x.id);var legacy=new HashSet<int>();
            foreach(var resource in resources)if(string.IsNullOrWhiteSpace(resource.id)||resource.capacity<=0||resource.mass<=0||resource.startingStock<0||resource.legacy>4||resource.legacy< -1||resource.legacy>=0&&!legacy.Add(resource.legacy))throw new InvalidOperationException("Invalid resource "+resource.id);
            var ids=new HashSet<string>();foreach(var recipe in recipes){if(!ids.Add(recipe.id)||recipe.seconds<=0||recipe.outputs==null||recipe.outputs.Length==0)throw new InvalidOperationException("Invalid recipe "+recipe.id);ValidateQuantities(recipe.inputs);ValidateQuantities(recipe.outputs);}
            ids.Clear();foreach(var archetype in archetypes){if(!ids.Add(archetype.id)||archetype.temperature<=0||archetype.pressure<0)throw new InvalidOperationException("Invalid archetype");ValidateQuantities(archetype.abundances);foreach(var abundance in archetype.abundances)if(abundance.amount>1)throw new InvalidOperationException("Abundance exceeds one");}
            ids.Clear();foreach(var crop in crops){if(!ids.Add(crop.id)||crop.growthSeconds<=0||crop.waterPerSecond<0||crop.tolerance<=0||crop.seedCost<0||crop.soilUse<0)throw new InvalidOperationException("Invalid crop "+crop.id);ValidateQuantities(crop.harvest);}
        }
        void ValidateQuantities(MaterialQuantity[] values){if(values==null)return;var ids=new HashSet<string>();foreach(var value in values)if(!Resources.ContainsKey(value.resource)||!ids.Add(value.resource)||value.amount<0||double.IsNaN(value.amount)||double.IsInfinity(value.amount))throw new InvalidOperationException("Invalid resource quantity "+value.resource);}
        public void Extend(RtsCatalog catalogue){catalogue.buildings=catalogue.buildings.Concat(buildings).ToArray();catalogue.technologies=catalogue.technologies.Concat(technologies).ToArray();catalogue.Validate();foreach(var recipe in recipes)if(!catalogue.Buildings.ContainsKey(recipe.building)||!string.IsNullOrEmpty(recipe.requiredTech)&&!catalogue.Technologies.ContainsKey(recipe.requiredTech))throw new InvalidOperationException("Invalid recipe infrastructure "+recipe.id);}
    }
}
