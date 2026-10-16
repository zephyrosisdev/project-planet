using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
namespace MassiveArmy
{
    public sealed class WorldSetupResult
    {
        public const int ResourceLayoutVersion=2;
        readonly WorldGenerationSettings settings;
        public WorldGenerationSettings Settings=>settings.Copy();
        public readonly WorldMap Map;
        public readonly DeploymentMap Deployment;
        public readonly Vector2[] Starts;
        public readonly ResourceNode[] Resources;
        public readonly float[] BiomePercentages;
        public readonly int Attempt;
        public readonly float OceanPercent,LakePercent,ForestPercent,RiverLength,RoadLength;
        public readonly int LandComponents;
        public readonly string InputKey;
        WorldSetupResult(WorldGenerationSettings input,WorldMap map,DeploymentMap deployment,Vector2[] starts,ResourceNode[] resources,int attempt)
        {
            settings=input.Copy();Map=map;Deployment=deployment;Starts=starts;Resources=resources;Attempt=attempt;InputKey=input.Key;
            BiomePercentages=new float[20];int dry=0;foreach(var b in map.Biome)if((int)b>=4){BiomePercentages[(int)b]++;dry++;}
            for(int i=0;i<20;i++)BiomePercentages[i]*=100f/Math.Max(1,dry);
            var components=new HashSet<int>();int ocean=0,lakes=0;float forest=0,rivers=0,roads=0;
            for(int i=0;i<map.Height.Length;i++){if(map.Water[i]==1)ocean++;if(map.Water[i]==2||map.Water[i]==3)lakes++;if(map.Water[i]==0)forest+=map.Forest[i];if(map.RiverCentre[i]&&map.Downstream[i]>=0)rivers+=WorldTopology.Distance(map.Position(i),map.Position(map.Downstream[i]),map.Size);roads+=map.Road[i]*map.Cell;if(deployment.Components[i]>=0)components.Add(deployment.Components[i]);}
            components.Remove(0);OceanPercent=ocean*100f/map.Height.Length;LakePercent=lakes*100f/map.Height.Length;ForestPercent=forest*100f/Math.Max(1,dry);RiverLength=rivers;RoadLength=roads;LandComponents=components.Count;
        }
        public static WorldSetupResult Generate(WorldGenerationSettings input,CancellationToken cancel=default,RtsCatalog catalog=null)
        {
            input=input.Copy();input.Validate();string error="";
            for(int attempt=0;attempt<8;attempt++){
                cancel.ThrowIfCancellationRequested();int seed=unchecked(input.seed+attempt*104729);
                var map=new WorldMap(input.Resolution,input.size,seed,input,cancel);var deployment=new DeploymentMap(map);
                try{var starts=FindStarts(map,deployment,input,cancel,catalog);var resources=BuildResources(map,deployment,starts,input,cancel,catalog);var result=new WorldSetupResult(input,map,deployment,starts,resources,attempt);if(catalog!=null)TransportProgression.Validate(result,catalog);return result;}
                catch(InvalidOperationException e){error=e.Message;}
            }
            throw new InvalidOperationException("No valid balanced starts after eight attempts: "+error+". Increase land/island size or reduce relief.");
        }
        public static WorldSetupResult FromMap(WorldMap map,DeploymentMap deployment,CancellationToken cancel=default,RtsCatalog catalog=null)
        {
            var config=map.Configuration;var starts=FindStarts(map,deployment,config,cancel,catalog);return new WorldSetupResult(config,map,deployment,starts,BuildResources(map,deployment,starts,config,cancel,catalog),0);
        }
        static bool Foundation(WorldMap map,DeploymentMap deployment,Vector2 p,float radius)
        {
            float min=float.MaxValue,max=float.MinValue;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
                Vector2 q=p+new Vector2(x,y)*radius;int i=map.Index(q);float h=map.SampleHeight(q);min=Mathf.Min(min,h);max=Mathf.Max(max,h);
                if(map.Water[i]!=0||map.RockObstacle[i]||map.Slope[i]>.35f||!deployment.Safe(q))return false;
            }return max-min<radius*.5f;
        }
        static Vector2[] FindStarts(WorldMap map,DeploymentMap deployment,WorldGenerationSettings config,CancellationToken cancel,RtsCatalog catalog)
        {
            var random=new System.Random(map.Seed^90213);var candidates=new List<Vector2>();
            for(int j=0;j<3000&&candidates.Count<128;j++){
                if((j&127)==0)cancel.ThrowIfCancellationRequested();Vector2 p=map.Position(random.Next(map.Height.Length));
                if(Foundation(map,deployment,p,28)&&!candidates.Exists(q=>WorldTopology.Distance(p,q,map.Size)<100))candidates.Add(p);
            }
            var usable=new List<Vector2>();var access=new List<float>();
            foreach(var candidate in candidates){cancel.ThrowIfCancellationRequested();try{
                var nodes=BuildStarterNodes(map,deployment,new[]{candidate},config,catalog);float value=0;foreach(var node in nodes)value+=node.capacity*node.quality/(50+WorldTopology.Distance(candidate,node.position,map.Size));usable.Add(candidate);access.Add(value);
            }catch(InvalidOperationException){}}
            for(int a=0;a<usable.Count;a++)for(int b=a+1;b<usable.Count;b++){
                if(WorldTopology.Distance(usable[a],usable[b],map.Size)<map.Size*config.startSeparation*.01f)continue;
                if(Mathf.Abs(access[a]-access[b])/Mathf.Max(access[a],access[b])>Mathf.Min(.10f,Mathf.Max(.005f,1-config.startBalance*.01f)))continue;
                return new[]{usable[a],usable[b]};
            }
            throw new InvalidOperationException("Insufficient safe starting land with the requested separation.");
        }
        static List<ResourceNode> BuildStarterNodes(WorldMap map,DeploymentMap deployment,Vector2[] starts,WorldGenerationSettings config,RtsCatalog catalog)
        {
            var list=new List<ResourceNode>();NodeKind[] kinds={NodeKind.Metal,NodeKind.Oil,NodeKind.RareEarth,NodeKind.Gas,NodeKind.Uranium};
            for(int team=0;team<starts.Length;team++)for(int k=0;k<kinds.Length;k++){
                Vector2 best=Vector2.zero;float score=float.MaxValue;int component=deployment.Components[map.Index(starts[team])];
                float angle=k*Mathf.PI*2/kinds.Length;Vector2 desired=starts[team]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*110;
                for(int y=-18;y<=18;y++)for(int x=-18;x<=18;x++){
                    Vector2 p=map.Position(map.Index(desired+new Vector2(x,y)*Mathf.Max(map.Cell,6)));
                    p=BuildingGrid.SnapResource(kinds[k],p,map.Size,catalog);
                    float d=WorldTopology.Distance(desired,p,map.Size);if(d>=score||WorldTopology.Distance(p,starts[team],map.Size)>Mathf.Max(160,map.Size*.08f)||WorldTopology.Distance(p,starts[team],map.Size)<55)continue;
                    if(deployment.Components[map.Index(p)]!=component||!Foundation(map,deployment,p,12)||list.Exists(n=>WorldTopology.Distance(n.position,p,map.Size)<38))continue;
                    best=p;score=d;
                }
                if(score==float.MaxValue)throw new InvalidOperationException("Starting islands lack accessible starter and transport-unlock resource space.");
                list.Add(Node(kinds[k],best,list.Count,config,1));
            }
            float[] access=new float[2];
            for(int team=0;team<starts.Length;team++)for(int k=0;k<5;k++){var node=list[team*5+k];access[team]+=node.capacity*node.quality/(50+WorldTopology.Distance(starts[team],node.position,map.Size));}
            float difference=Mathf.Abs(access[0]-access[1])/Mathf.Max(access[0],access[1]);
            if(starts.Length==2&&difference>Mathf.Min(.10f,Mathf.Max(.005f,1-config.startBalance*.01f)))throw new InvalidOperationException("Starting resource access differs by more than the allowed balance tolerance.");
            return list;
        }
        static ResourceNode[] BuildResources(WorldMap map,DeploymentMap deployment,Vector2[] starts,WorldGenerationSettings config,CancellationToken cancel,RtsCatalog catalog)
        {
            var list=BuildStarterNodes(map,deployment,starts,config,catalog);var random=new System.Random(map.Seed^5187);
            int count=Mathf.RoundToInt(config.resourceAbundance*.96f);
            for(int k=0;k<count;k++){
                cancel.ThrowIfCancellationRequested();Vector2 desired=map.Position(random.Next(map.Height.Length));
                if(config.resourceClustering>0&&list.Count>10)desired=WorldTopology.Wrap(desired+WorldTopology.Delta(desired,list[random.Next(list.Count)].position+new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*map.Size*.15f,map.Size)*config.resourceClustering*.007f,map.Size);
                if(!Foundation(map,deployment,desired,12)||list.Exists(n=>WorldTopology.Distance(n.position,desired,map.Size)<40)||Array.Exists(starts,p=>WorldTopology.Distance(p,desired,map.Size)<map.Size*.1f))continue;
                var kind=(NodeKind)random.Next(7);float quality=.7f+(float)random.NextDouble()*.8f;
                desired=BuildingGrid.SnapResource(kind,desired,map.Size,catalog);
                // Validate the final snapped position, including shorelines and neighbour spacing.
                if(!Foundation(map,deployment,desired,12)||list.Exists(n=>WorldTopology.Distance(n.position,desired,map.Size)<40)||Array.Exists(starts,p=>WorldTopology.Distance(p,desired,map.Size)<map.Size*.1f))continue;
                list.Add(Node(kind,desired,list.Count,config,quality));
            }return list.ToArray();
        }
        [Serializable] sealed class Manifest {public int generatorVersion,resourceLayoutVersion,seed,resolvedSeed,attempt;public WorldGenerationSettings settings;public Vector2[] starts;public ResourceNode[] resources;public float[] biomePercentages;}
        public void Export(string folder){Map.Export(folder);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"world-manifest.json"),JsonUtility.ToJson(new Manifest{generatorVersion=WorldMap.GeneratorVersion,resourceLayoutVersion=ResourceLayoutVersion,seed=settings.seed,resolvedSeed=Map.Seed,attempt=Attempt,settings=settings,starts=Starts,resources=Resources,biomePercentages=BiomePercentages},true));}
        static ResourceNode Node(NodeKind kind,Vector2 p,int id,WorldGenerationSettings config,float quality)
        {
            float capacity=(kind==NodeKind.RichMetal?65000:28000)*Mathf.Lerp(.25f,2,config.resourceRichness*.01f);
            return new ResourceNode{id=id,kind=kind,position=p,resource=kind==NodeKind.Oil||kind==NodeKind.Gas?ResourceKind.Fuel:kind==NodeKind.RareEarth?ResourceKind.RareElements:kind==NodeKind.Uranium?ResourceKind.Isotopes:ResourceKind.Materials,capacity=capacity,remaining=capacity,quality=quality,rateMultiplier=quality*(kind==NodeKind.RichMetal?2:1)};
        }
    }
}
