using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    public enum DiscoveryLevel {Unknown,Detected,OrbitEstimate,Environment,Spectroscopy,Surveyed}
    [Serializable] public sealed class ResourceEstimate {public string resource;public double estimate,uncertainty;}
    [Serializable] public sealed class BodyKnowledge {
        public string body;public DiscoveryLevel level;public double confidence,lastObserved,orbitUncertainty;
        public Double3 estimatedDirection;public double estimatedDistance;public List<ResourceEstimate> resources=new List<ResourceEstimate>();
    }
    [Serializable] public sealed class FactionKnowledgeBase {
        public List<BodyKnowledge> bodies=new List<BodyKnowledge>();
        public BodyKnowledge Find(string body){foreach(var item in bodies)if(item.body==body)return item;return null;}
        public bool Knows(string body,DiscoveryLevel minimum=DiscoveryLevel.Detected)=>Find(body)?.level>=minimum;
        internal BodyKnowledge Record(string body){var known=Find(body);if(known==null){known=new BodyKnowledge{body=body};bodies.Add(known);}return known;}
    }
    [Serializable] public sealed class PlanetResourceProfile {
        public string archetype;public List<MaterialQuantity> abundance=new List<MaterialQuantity>();
        public double Get(string resource){foreach(var value in abundance)if(value.resource==resource)return value.amount;return 0;}
        internal static uint Hash(string key){uint value=2166136261;foreach(char c in key){value^=c;value*=16777619;}value^=value>>16;value*=0x7feb352d;return value^(value>>15);}
        internal static double Random(string key)=>Hash(key)/4294967296.0;
        internal static PlanetResourceProfile Generate(string stableId,bool home,CivilisationCatalogue catalogue,CivilisationSettings settings){
            var archetype=catalogue.archetypes[home?0:(int)(Hash(stableId+":archetype")%(uint)catalogue.archetypes.Length)];var result=new PlanetResourceProfile{archetype=archetype.id};
            foreach(var resource in catalogue.resources){if(!resource.element)continue;double basis=0;foreach(var value in archetype.abundances)if(value.resource==resource.id){basis=value.amount;break;}if(basis==0&&!resource.bootstrap)continue;
                double amount=Math.Min(1,basis*(.65+Random(stableId+":"+resource.id)*.7));if(home&&resource.bootstrap)amount=Math.Max(settings.bootstrapAbundanceFloor,amount);if(home&&resource.id=="titanium")amount=settings.homeStrategicAbundance;
                if(amount>0)result.abundance.Add(new MaterialQuantity(resource.id,amount));
            }return result;
        }
        // Correlated 3D geology, sampled in body space. No latitude bands or cube-face seams.
        public double LocalAbundance(string resource,Vector3 direction,int seed){double baseValue=Get(resource);double phase=Random(seed+":"+resource)*100;double broad=.5+.5*Math.Sin(direction.x*13+phase)*Math.Sin(direction.y*11-phase)*Math.Cos(direction.z*17+phase);return Math.Min(1,baseValue*(.35+1.3*broad));}
    }
    [Serializable] public sealed class CelestialEnvironment {
        public bool atmosphere,breathable;public double pressure,oxygen,temperature=288,habitability;
    }
    [Serializable] public sealed class PlanetState {
        public string body;public int planetId,navigationContext;public PlanetResourceProfile composition;
        public CelestialEnvironment environment;public List<long> colonies=new List<long>(),orbitalAssets=new List<long>(),surfaceInstallations=new List<long>();
        public List<RoadTile> roads=new List<RoadTile>();
        public double lastSimulatedTime;
    }
    [Serializable] public sealed class CivilisationFaction {
        public int id;public string name;public bool autonomous;public FactionKnowledgeBase knowledge=new FactionKnowledgeBase();
        public List<string> technologies=new List<string>();public string priority="Develop planetary industry",reason="Bootstrap resources available";
    }
    public enum ColonyStage {InitialOutpost,Dependent,Industrial,SelfSufficient,Developed}
    [Serializable] public sealed class ColonyState {
        public long id;public int faction;public string body,name;public ColonyStage stage;public double selfSufficiency,powerBalance,waterBalance,foodBalance;
        public int workforce,employed,workerDemand,plannedWorkerDemand;
        public int population,housingCapacity,housedPopulation;public double growthProgress,foodDemand,foodSupplied,powerGeneration,powerDemand;
        public ResourceInventory inventory=new ResourceInventory();public List<SurfaceInstallation> installations=new List<SurfaceInstallation>();
        public List<CargoVehicleState> cargoVehicles=new List<CargoVehicleState>();public List<CargoVehicleOrder> cargoOrders=new List<CargoVehicleOrder>();
    }
    [Serializable] public sealed class SurfaceInstallation {
        public int workersAssigned,workersRequired;
        public long id;public string definition;public Vector2 position;public int level=1;public double construction,health;public bool powered=true,paused;public string recipe;
        public ResourceInventory inventory=new ResourceInventory();public string crop;public double growth,soilFertility=.8;public int harvests;public bool planted;
    }
    [Serializable] public sealed class UniverseState {
        public int schemaVersion=1,galaxySeed;public long nextId=1;public double time;
        public List<PlanetState> planets=new List<PlanetState>();public List<CivilisationFaction> factions=new List<CivilisationFaction>();public List<ColonyState> colonies=new List<ColonyState>();
        public PlanetState Planet(string id){foreach(var planet in planets)if(planet.body==id)return planet;return null;}
        public CivilisationFaction Faction(int id){foreach(var faction in factions)if(faction.id==id)return faction;return null;}
        public long AllocateId()=>nextId++;
    }
}
