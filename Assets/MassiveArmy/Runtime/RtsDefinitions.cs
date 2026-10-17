using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassiveArmy
{
    public enum ResourceKind { Materials, Fuel, RareElements, Isotopes, Research }
    public enum NodeKind { Metal, RichMetal, Oil, Gas, RareEarth, Uranium, Salvage }
    public enum BuildingCategory { Core, Economy, Power, Production, Research, Support, Defence, Strategic }
    public enum TerrainRule { Land, Node, Coast }
    public enum SearchPattern { None, Spiral, Grid, Sector, Perimeter, Route, AutoExplore, Observe, FollowArmy }
    public enum StrategicProfile { Rush, Armoured, AirPower, Turtle, Artillery, Economic, Naval, Balanced }
    public enum TargetPriority { Closest, Aircraft, Armour, Infantry, Artillery, Strategic }

    [Serializable]
    public struct ResourceCost
    {
        public float materials, fuel, rareElements, isotopes, research;
        public float this[int i] { get => i==0?materials:i==1?fuel:i==2?rareElements:i==3?isotopes:research;
            set { if(i==0)materials=value;else if(i==1)fuel=value;else if(i==2)rareElements=value;else if(i==3)isotopes=value;else research=value; } }
        public static ResourceCost operator *(ResourceCost a,float f) { for(int i=0;i<5;i++)a[i]*=f;return a; }
        public override string ToString() { var c=this;return string.Join("  ",Enumerable.Range(0,5).Where(i=>c[i]>0).Select(i=>$"{c[i]:0}{new[]{"M","F","R","I","RP"}[i]}")); }
    }

    [Serializable]
    public sealed class BuildingDefinition
    {
        public string id, name, description, requiredTech, mesh, icon;
        public string extractedResource;
        public float foodUse;
        public int housingCapacity;
        public BuildingCategory category;
        public TerrainRule terrain;
        public NodeKind[] nodes = Array.Empty<NodeKind>();
        public ResourceCost cost;
        public float time=12, radius=10, health=2400, armour=20, generation, consumption, buildRadius, vision=95, detection, extraction, researchRate, storage, battery, fuelUse;
        public int tier=1, maxLevel=3;
        public int footprintWidth,footprintDepth;
        public string[] production=Array.Empty<string>(), upgrades=Array.Empty<string>();
        public float weaponRange, weaponDamage, reload=1, shield,explosionRadius;
        public int targetMask=31;
    }

    [Serializable]
    public sealed class UnitDefinition
    {
        public string id, name, category, producer, requiredTech, icon;
        public int producerLevel=1, gpuModelIndex, spriteIndex, role, targetMask=23, transportCapacity;
        public MovementClass movement;
        public ResourceCost cost;
        public float time=5, speed=16, acceleration=8, turnRate=3, health=1000, armour=10, vision=100, detection=50, radarSignature=1, stealth, range=60, damage=180, reload=1.8f, radius=2, splash, altitude, ammunition=100, fuel=100, supplyConsumption=1,explosionRadius;
        public UnitArchetype Gpu => new UnitArchetype { speed=speed,attackRange=range,acquisitionRange=Mathf.Max(range,vision),cooldown=reload,health=(uint)health,damage=(uint)damage,movement=(uint)movement,weapon=splash>0?2u:0u,turnSpeed=turnRate,radius=radius,projectileSpeed=220,aimTolerance=.3f,model=(uint)gpuModelIndex,sprite=(uint)(spriteIndex&65535)|((uint)transportCapacity<<16),nearDistance=400,farDistance=1100 };
        public UnitTraits Traits => new UnitTraits { vision=vision,detection=detection,signature=radarSignature,stealth=stealth,armour=armour,splash=splash,altitude=altitude,fuel=fuel,targetMask=(uint)targetMask,role=(uint)role,ammunition=ammunition,supply=supplyConsumption,scale=new Vector4(radius/2, movement==MovementClass.Infantry?1.8f:1, movement==MovementClass.Water?radius/1.4f:radius/2,(float)movement) };
    }
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public struct UnitTraits
    {
        public float vision,detection,signature,stealth,armour,splash,altitude,fuel;
        public uint targetMask,role;
        public float ammunition,supply;
        public Vector4 scale;
        public const int Stride=64;
    }
    [Serializable]
    public sealed class TechnologyDefinition
    {
        public string id,name,description,icon,branch,requiredBuilding;
        public int tier=2,requiredBuildingLevel=1;
        public ResourceCost cost;
        public float time=30;
        public string[] prerequisites=Array.Empty<string>(),unlockUnits=Array.Empty<string>(),unlockBuildings=Array.Empty<string>(),unlockUpgrades=Array.Empty<string>(),unlockAbilities=Array.Empty<string>(),unlockModifiers=Array.Empty<string>();
    }
    [Serializable]
    public sealed class RtsCatalog
    {
        public BuildingDefinition[] buildings;
        public UnitDefinition[] units;
        public TechnologyDefinition[] technologies;
        public Dictionary<string,BuildingDefinition> Buildings {get;private set;}
        public Dictionary<string,UnitDefinition> Units {get;private set;}
        public Dictionary<string,TechnologyDefinition> Technologies {get;private set;}
        public static RtsCatalog Load()
        {
            var asset=Resources.Load<TextAsset>("MassiveArmy/RtsCatalog");
            if(asset==null)throw new InvalidOperationException("Missing RTS catalogue");
            var result=JsonUtility.FromJson<RtsCatalog>(asset.text);result.Validate();return result;
        }
        public void Validate()
        {
            Buildings=buildings.ToDictionary(d=>d.id);Units=units.ToDictionary(d=>d.id);Technologies=technologies.ToDictionary(d=>d.id);
            if(units.Length>256)throw new InvalidOperationException("Packed GPU type index exceeds 8 bits");
            foreach(var b in buildings) { ValidateCost(b.cost,b.id);ValidateTech(b.requiredTech);foreach(var id in b.production)if(!Units.ContainsKey(id))throw new InvalidOperationException("Unknown production "+id);if(b.radius<=0||b.time<=0||b.health<=0||b.tier<1||b.tier>5||b.maxLevel<1||b.footprintWidth<1||b.footprintDepth<1||b.footprintWidth>16||b.footprintDepth>16)throw new InvalidOperationException("Invalid building / footprint "+b.id); }
            foreach(var u in units) { ValidateCost(u.cost,u.id);ValidateTech(u.requiredTech);if(!Buildings.ContainsKey(u.producer)||u.time<=0||u.health<=0||u.transportCapacity<0||u.transportCapacity>65535)throw new InvalidOperationException("Invalid unit "+u.id); }
            var visiting=new HashSet<string>();var visited=new HashSet<string>();
            foreach(var t in technologies) { ValidateCost(t.cost,t.id);if(!Buildings.ContainsKey(t.requiredBuilding)||t.time<=0||t.tier<2||t.tier>5)throw new InvalidOperationException("Invalid technology "+t.id);foreach(var id in t.unlockUnits)if(!Units.ContainsKey(id))throw new InvalidOperationException("Unknown unit unlock "+id);foreach(var id in t.unlockBuildings)if(!Buildings.ContainsKey(id))throw new InvalidOperationException("Unknown building unlock "+id);Visit(t.id,visiting,visited); }
        }
        static void ValidateCost(ResourceCost cost,string id) {for(int i=0;i<5;i++)if(cost[i]<0||float.IsNaN(cost[i])||float.IsInfinity(cost[i]))throw new InvalidOperationException("Invalid resource cost "+id);}
        void ValidateTech(string id) { if(!string.IsNullOrEmpty(id)&&!Technologies.ContainsKey(id))throw new InvalidOperationException("Unknown technology "+id); }
        void Visit(string id,HashSet<string> visiting,HashSet<string> visited)
        {
            ValidateTech(id);if(visited.Contains(id))return;if(!visiting.Add(id))throw new InvalidOperationException("Cyclic technology "+id);
            foreach(var p in Technologies[id].prerequisites)Visit(p,visiting,visited);visiting.Remove(id);visited.Add(id);
        }
        public int TypeIndex(string id) => Array.FindIndex(units,u=>u.id==id);
    }

    public sealed class ResourceWallet
    {
        public ResourceCost Stock,Capacity;
        public bool CanAfford(ResourceCost cost) { for(int i=0;i<5;i++)if(Stock[i]+.001f<cost[i])return false;return true; }
        public bool Spend(ResourceCost cost) { if(!CanAfford(cost))return false;for(int i=0;i<5;i++)Stock[i]-=cost[i];return true; }
        public void Credit(ResourceCost amount) { for(int i=0;i<5;i++)Stock[i]=Mathf.Min(Capacity[i],Stock[i]+amount[i]); }
        public float Deposit(ResourceKind resource,float amount) { int i=(int)resource;float accepted=Mathf.Clamp(amount,0,Mathf.Max(0,Capacity[i]-Stock[i]));Stock[i]+=accepted;return accepted; }
    }
    [Serializable]
    public sealed class ResourceNode
    {
        public int id,claimedBy=-1;
        public NodeKind kind;
        public ResourceKind resource;
        public Vector2 position;
        public float capacity,remaining,rateMultiplier,quality,radius=12;
        public string elementId;
    }
    public sealed class QueueOrder
    {
        public string id;
        public ResourceCost paid;
        public float duration,progress;
        public int bay=-1;
        public bool research;
        public float Fraction => Mathf.Clamp01(progress/duration);
    }
}
