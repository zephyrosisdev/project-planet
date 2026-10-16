using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public static class TransportProgression
    {
        // Read the real catalogue on the main thread. Generation and geographic queries remain managed.
        public static string Validate(WorldSetupResult world,RtsCatalog catalog)
        {
            int[] components=world.Starts.Select(p=>world.Deployment.Components[world.Map.Index(p)]).ToArray();
            if(components[0]==components[1])return "Connected ground starts";
            string failure="";
            foreach(var unit in catalog.units.Where(u=>u.transportCapacity>0&&(u.movement==MovementClass.Air||u.movement==MovementClass.Water)).OrderBy(u=>u.movement==MovementClass.Air?0:1))try{
                var buildings=new Dictionary<string,BuildingDefinition>();var technologies=new HashSet<string>();var visiting=new HashSet<string>();ResourceCost cost=unit.cost;
                void Tech(string id){if(string.IsNullOrEmpty(id)||technologies.Contains(id))return;if(!visiting.Add("t:"+id))throw new InvalidOperationException("Cyclic transport technology dependency");var t=catalog.Technologies[id];foreach(string parent in t.prerequisites)Tech(parent);Building(t.requiredBuilding);cost=Sum(cost,t.cost);technologies.Add(id);visiting.Remove("t:"+id);}
                void Building(string id){if(string.IsNullOrEmpty(id)||buildings.ContainsKey(id))return;if(!visiting.Add("b:"+id))throw new InvalidOperationException("Cyclic transport building dependency");var b=catalog.Buildings[id];Tech(b.requiredTech);buildings.Add(id,b);cost=Sum(cost,b.cost);visiting.Remove("b:"+id);}
                Building(unit.producer);Tech(unit.requiredTech);Building("mine");Building("oil");Building("rare");Building("wind");
                int tier=buildings.Values.Max(b=>b.tier);for(int level=1;level<tier;level++)cost=Sum(cost,catalog.Buildings["command"].cost*(.65f*level));
                float demand=buildings.Values.Sum(b=>b.consumption-b.generation);int extraWind=Mathf.Max(0,Mathf.CeilToInt(demand/Mathf.Max(1,catalog.Buildings["wind"].generation)));
                cost=Sum(cost,catalog.Buildings["wind"].cost*extraWind);
                for(int team=0;team<2;team++){
                    for(int resource=0;resource<4;resource++){
                        float available=world.Resources.Where(n=>(int)n.resource==resource&&world.Deployment.Components[world.Map.Index(n.position)]==components[team]).Sum(n=>n.capacity);
                        if(available<cost[resource])throw new InvalidOperationException("Island "+team+" lacks "+(ResourceKind)resource+" for "+unit.id);
                    }
                    var occupied=new List<(Vector2 p,float radius)>{(world.Starts[team],catalog.Buildings["command"].radius)};
                    foreach(var b in buildings.Values.Where(b=>b.terrain!=TerrainRule.Node).Concat(Enumerable.Repeat(catalog.Buildings["wind"],extraWind))){
                        bool found=false;
                        for(int ring=1;ring<=30&&!found;ring++)for(int k=0;k<24;k++){
                            float angle=k*Mathf.PI/12;Vector2 p=WorldTopology.Wrap(world.Starts[team]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*ring*20,world.Map.Size);
                            if(world.Deployment.Components[world.Map.Index(p)]!=components[team]||occupied.Any(o=>WorldTopology.Distance(o.p,p,world.Map.Size)<o.radius+b.radius+5)||world.Resources.Any(n=>WorldTopology.Distance(n.position,p,world.Map.Size)<n.radius+b.radius+4))continue;
                            bool safe=true;float lo=float.MaxValue,hi=float.MinValue;
                            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
                                Vector2 q=p+new Vector2(x,y)*b.radius;int i=world.Map.Index(q);float height=world.Map.SampleHeight(q);lo=Mathf.Min(lo,height);hi=Mathf.Max(hi,height);
                                if(world.Map.Water[i]!=0||world.Map.Slope[i]>.35f||!world.Deployment.Safe(q))safe=false;
                            }
                            if(!safe||hi-lo>b.radius*.5f)continue;
                            if(b.terrain==TerrainRule.Coast&&!HasWaterExit(world,p,b.radius))continue;
                            occupied.Add((p,b.radius));found=true;break;
                        }
                        if(!found)throw new InvalidOperationException("Island "+team+" lacks usable space for "+b.name);
                    }
                }
                return "Island progression validated: "+unit.id+" via "+unit.producer;
            }catch(Exception e)when(e is InvalidOperationException||e is KeyNotFoundException){failure=e.Message;}
            throw new InvalidOperationException("Separated islands cannot unlock naval or air transport: "+failure);
        }
        static ResourceCost Sum(ResourceCost a,ResourceCost b){for(int i=0;i<5;i++)a[i]+=b[i];return a;}
        static bool HasWaterExit(WorldSetupResult world,Vector2 p,float radius)
        {
            int n=world.Map.Height.Length;for(int k=0;k<32;k++){float a=k*Mathf.PI/16;Vector2 q=p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(radius+45);if(world.Deployment.Costs[4*n+world.Map.Index(q)]<DeploymentMap.Blocked*.5f)return true;}return false;
        }
    }
}
