using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MassiveArmy
{
    public enum Biome { Ocean, Lake, Pond, River, Beach, Rainforest, SeasonalForest, Savanna, HotDesert, Steppe,
        Grassland, TemperateForest, TemperateRainforest, Mediterranean, Wetland, BorealForest, Tundra, ColdDesert, Alpine, Glacier }

    public static class Biomes
    {
        public static readonly string[] Names = { "Ocean", "Lake", "Pond", "River", "Beach", "Tropical rainforest", "Tropical seasonal forest", "Savanna", "Hot desert", "Semi-arid steppe",
            "Temperate grassland", "Temperate forest", "Temperate rainforest", "Mediterranean scrub", "Wetland", "Boreal forest", "Tundra", "Cold desert", "Alpine rock", "Snow / glacier" };
        public static readonly Color[] Colours = {
            new Color(.025f,.15f,.29f),new Color(.07f,.38f,.54f),new Color(.14f,.48f,.47f),new Color(.18f,.58f,.78f),new Color(.85f,.75f,.48f),
            new Color(.06f,.34f,.17f),new Color(.24f,.48f,.18f),new Color(.65f,.62f,.25f),new Color(.88f,.69f,.38f),new Color(.63f,.56f,.34f),
            new Color(.46f,.64f,.28f),new Color(.17f,.43f,.24f),new Color(.07f,.34f,.32f),new Color(.52f,.57f,.32f),new Color(.26f,.46f,.35f),
            new Color(.19f,.33f,.34f),new Color(.52f,.59f,.54f),new Color(.63f,.62f,.57f),new Color(.43f,.45f,.48f),new Color(.9f,.95f,.98f) };
        // grass, sand, dry grass, forest floor, rock, snow, mud, gravel
        public static readonly Color[] Surfaces = { new Color(.19f,.29f,.085f),new Color(.63f,.45f,.22f),new Color(.38f,.31f,.12f),new Color(.085f,.15f,.065f),
            new Color(.24f,.26f,.28f),new Color(.79f,.86f,.9f),new Color(.16f,.14f,.075f),new Color(.34f,.31f,.26f) };
        public static float Forest(Biome b)
        {
            switch(b) { case Biome.Rainforest:return 1; case Biome.SeasonalForest:return .7f; case Biome.TemperateForest:return .75f;
                case Biome.TemperateRainforest:return .95f; case Biome.BorealForest:return .7f; case Biome.Savanna:return .12f;
                case Biome.Mediterranean:return .16f; case Biome.Wetland:return .25f; default:return 0; }
        }
        public static void Layers(Biome b,float slope,float altitude,out Color a,out Color c)
        {
            a=Color.clear;c=Color.clear;
            switch(b) {
                case Biome.HotDesert:case Biome.Beach:a.g=.88f;c.a=.12f;break;
                case Biome.Steppe:case Biome.Savanna:case Biome.Mediterranean:a.b=.8f;a.r=.2f;break;
                case Biome.Rainforest:case Biome.SeasonalForest:case Biome.TemperateForest:case Biome.TemperateRainforest:case Biome.BorealForest:a.a=.75f;a.r=.25f;break;
                case Biome.Wetland:c.b=.7f;a.r=.3f;break;
                case Biome.Tundra:a.r=.22f;c.a=.55f;c.r=.23f;break;
                case Biome.ColdDesert:c.a=.65f;c.r=.35f;break;
                case Biome.Alpine:c.r=.85f;c.a=.15f;break;
                case Biome.Glacier:c.g=.94f;c.r=.06f;break;
                case Biome.Ocean:case Biome.Lake:case Biome.Pond:case Biome.River:a.g=.25f;c.b=.75f;break;
                default:a.r=.85f;a.b=.15f;break;
            }
            float rock=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,1.15f,slope));
            a*=1-rock;c*=1-rock;c.r+=rock;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SceneryInstance { public Vector4 positionScale, tintYaw; }

    /// <summary>Immutable seed-generated landscape data. No individual army state is stored on the CPU.</summary>
    public sealed partial class WorldMap
    {
        public const int GeneratorVersion=4;
        public readonly int Resolution, Seed;
        public readonly float Size, Cell;
        public readonly float[] Height, Slope, Temperature, Moisture, WaterLevel, Road, Forest;
        public readonly float[] Sediment,Drainage;
        public readonly byte[] Water; // 0 dry, 1 ocean, 2 lake, 3 pond, 4 river
        public readonly Biome[] Biome;
        public readonly bool[] RockObstacle,RiverCentre;
        public readonly int[] Downstream;
        public readonly Vector2[] Sites=new Vector2[16];
        public readonly List<SceneryInstance>[] Scenery=new List<SceneryInstance>[7];
        readonly float offset;
        static readonly int[] DX={-1,0,1,-1,1,-1,0,1}, DY={-1,-1,-1,0,0,1,1,1};

        public WorldMap(int resolution,float size,int seed,WorldGenerationSettings options=null,System.Threading.CancellationToken cancellation=default)
        {
            Resolution=resolution;Size=size;Seed=seed;Cell=size/resolution;offset=(seed&65535)*.01731f;
            configuration=options?.Copy()??new WorldGenerationSettings();configuration.size=(int)size;configuration.seed=seed;cancel=cancellation;
            int n=resolution*resolution;
            Height=new float[n];Slope=new float[n];Temperature=new float[n];Moisture=new float[n];WaterLevel=new float[n];
            Road=new float[n];Forest=new float[n];Water=new byte[n];Biome=new Biome[n];RockObstacle=new bool[n];RiverCentre=new bool[n];Downstream=new int[n];
            Sediment=new float[n];Drainage=new float[n];
            for(int i=0;i<Scenery.Length;i++)Scenery[i]=new List<SceneryInstance>();
            Landforms();cancel.ThrowIfCancellationRequested();Erode();cancel.ThrowIfCancellationRequested();Hydrology();cancel.ThrowIfCancellationRequested();Climate();cancel.ThrowIfCancellationRequested();Roads();cancel.ThrowIfCancellationRequested();Decorate();
        }
        // Managed gradient noise makes generation independently testable and stable across editor/player platforms.
        float Noise(float x,float y,float scale,float salt=0)
        {
            int period=Math.Max(1,Mathf.RoundToInt(scale*Size));scale=period/Size;
            x=x*scale+offset+salt;y=y*scale+offset+71+salt*.37f;
            int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;
            float u=fx*fx*fx*(fx*(fx*6-15)+10),v=fy*fy*fy*(fy*(fy*6-15)+10);
            return Mathf.Clamp01(.5f+Mathf.Lerp(Mathf.Lerp(Gradient(WorldTopology.Cell(ix,period),WorldTopology.Cell(iy,period),fx,fy),Gradient(WorldTopology.Cell(ix+1,period),WorldTopology.Cell(iy,period),fx-1,fy),u),
                Mathf.Lerp(Gradient(WorldTopology.Cell(ix,period),WorldTopology.Cell(iy+1,period),fx,fy-1),Gradient(WorldTopology.Cell(ix+1,period),WorldTopology.Cell(iy+1,period),fx-1,fy-1),u),v)*.8f);
        }
        static float Gradient(int x,int y,float dx,float dy)
        {
            uint h=unchecked((uint)x*374761393u+(uint)y*668265263u);h=(h^(h>>13))*1274126177u;h^=h>>16;
            switch(h&7) { case 0:return dx;case 1:return -dx;case 2:return dy;case 3:return -dy;
                case 4:return (dx+dy)*.7071068f;case 5:return (dx-dy)*.7071068f;case 6:return (-dx+dy)*.7071068f;default:return (-dx-dy)*.7071068f; }
        }
        public Vector2 Position(int i) => new Vector2(((i%Resolution+.5f)/Resolution-.5f)*Size,((i/Resolution+.5f)/Resolution-.5f)*Size);
        public int Index(Vector2 p) => WorldTopology.Index(Mathf.FloorToInt((p.x/Size+.5f)*Resolution),Mathf.FloorToInt((p.y/Size+.5f)*Resolution),Resolution);
        float H(int x,int y) => Height[WorldTopology.Index(x,y,Resolution)];
        void Erode()
        {
            int n=Height.Length;
            // Talus relaxation conserves material locally rather than adding another noise octave.
            var delta=new float[n];
            for(int step=0;step<Mathf.RoundToInt(configuration.erosionStrength*.28f);step++)
            {
                cancel.ThrowIfCancellationRequested();Array.Clear(delta,0,n);
                for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
                {
                    int i=x+y*Resolution,best=-1;float excess=0;
                    for(int k=0;k<8;k++){int j=WorldTopology.Index(x+DX[k],y+DY[k],Resolution);float d=Height[i]-Height[j]-Cell*.62f*(DX[k]!=0&&DY[k]!=0?1.414f:1);if(d>excess){excess=d;best=j;}}
                    if(best<0)continue;float moved=excess*.18f;delta[i]-=moved;delta[best]+=moved;Sediment[best]+=moved;
                }
                for(int i=0;i<n;i++)Height[i]+=delta[i];
            }
            // Stream-power incision follows accumulated catchments down actual slopes.
            var order=new int[n];var receiver=new int[n];var flow=new float[n];
            for(int i=0;i<n;i++){order[i]=i;receiver[i]=-1;flow[i]=Cell*Cell;int x=i%Resolution,y=i/Resolution;float steepest=0;
                for(int k=0;k<8;k++){int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);int j=nx+ny*Resolution;
                    float slope=(Height[i]-Height[j])/(Cell*(DX[k]!=0&&DY[k]!=0?1.414f:1));if(slope>steepest){steepest=slope;receiver[i]=j;}}}
            Array.Sort(order,(a,b)=>Height[b].CompareTo(Height[a]));
            foreach(int i in order)if(receiver[i]>=0)flow[receiver[i]]+=flow[i];
            Array.Clear(delta,0,n);
            foreach(int i in order){int j=receiver[i];if(j<0||Height[i]<=0)continue;float slope=Mathf.Max(0,(Height[i]-Height[j])/Cell);
                float incision=Mathf.Min(9,Mathf.Pow(flow[i]/1600,.38f)*Mathf.Sqrt(slope)*3.6f*configuration.erosionStrength*.01f);
                delta[i]-=incision;float deposited=incision*.35f*(1-Mathf.Clamp01(slope*3));delta[j]+=deposited;Sediment[j]+=deposited;}
            for(int i=0;i<n;i++)Height[i]+=delta[i];
        }
        void Hydrology()
        {
            int n=Height.Length;var visited=new bool[n];var spill=new float[n];var order=new List<int>(n);var heap=new Heap();
            for(int i=0;i<n;i++)if(configuration.oceanCoverage>0&&Height[i]<=0){visited[i]=true;spill[i]=0;Downstream[i]=-1;heap.Push(i,0);}
            if(heap.Count==0){int sink=0;for(int i=1;i<n;i++)if(Height[i]<Height[sink])sink=i;visited[sink]=true;spill[sink]=Height[sink]+(configuration.lakeAbundance>0?3:0);Downstream[sink]=-1;heap.Push(sink,spill[sink]);if(configuration.lakeAbundance>0){Water[sink]=3;WaterLevel[sink]=spill[sink];}}
            // Priority flood gives every inland cell an acyclic downstream path and its basin's spill elevation.
            while(heap.Count>0)
            {
                if((order.Count&4095)==0)cancel.ThrowIfCancellationRequested();int i=heap.Pop(out _);order.Add(i);int x=i%Resolution,y=i/Resolution;
                for(int k=0;k<8;k++) { int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                    int j=nx+ny*Resolution;if(visited[j])continue;visited[j]=true;Downstream[j]=i;
                    spill[j]=Mathf.Max(Height[j],spill[i]);heap.Push(j,spill[j]); }
            }
            // Below-sea cells are ocean sinks; inland spill depressions are freshwater sinks.
            var queue=new Queue<int>();var ocean=new bool[n];
            for(int i=0;i<n;i++)if(Downstream[i]<0&&Height[i]<=0) { ocean[i]=true;queue.Enqueue(i); }
            while(queue.Count>0) { int i=queue.Dequeue(),x=i%Resolution,y=i/Resolution;Water[i]=1;WaterLevel[i]=0;
                for(int k=0;k<8;k++) { int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                    int j=nx+ny*Resolution;if(!ocean[j]&&Height[j]<=0) { ocean[j]=true;queue.Enqueue(j); } } }
            var seen=new bool[n];
            for(int start=0;start<n;start++)
            {
                if(configuration.lakeAbundance<=0||seen[start]||ocean[start]||spill[start]-Height[start]<.8f)continue;
                var component=new List<int>();seen[start]=true;queue.Enqueue(start);
                while(queue.Count>0) { int i=queue.Dequeue();component.Add(i);int x=i%Resolution,y=i/Resolution;
                    for(int k=0;k<8;k++) { int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                        int j=nx+ny*Resolution;if(!seen[j]&&!ocean[j]&&spill[j]-Height[j]>=.8f&&Mathf.Abs(spill[j]-spill[start])<.01f) { seen[j]=true;queue.Enqueue(j); } } }
                if(component.Count<3)continue;
                byte kind=(byte)(component.Count*Cell*Cell>1800?2:3);
                foreach(int i in component) { Water[i]=kind;WaterLevel[i]=spill[i]; }
            }
            var flow=new float[n];
            foreach(int i in order) { Vector2 p=Position(i);flow[i]=.4f+Noise(p.x,p.y,Mathf.Lerp(12,2,configuration.basinScale*.01f)/Size,102); }
            for(int a=order.Count-1;a>=0;a--) { int i=order[a],j=Downstream[i];if(j>=0)flow[j]+=flow[i]; }
            for(int i=0;i<n;i++)Drainage[i]=Mathf.Clamp01(Mathf.Log(1+flow[i])/Mathf.Log(1+n*.035f));
            for(int i=0;i<n;i++)
            {
                if(configuration.riverAbundance<=0||Water[i]!=0||flow[i]<n*Mathf.Lerp(.04f,.002f,configuration.riverAbundance*.01f))continue;
                float depth=Mathf.Lerp(.65f,3.4f,Mathf.Clamp01(flow[i]/(n*.09f)));
                if(Noise(Position(i).x,Position(i).y,15/Size,981)>.8f-configuration.fordFrequency*.006f)depth=.45f;
                RiverCentre[i]=true;Water[i]=4;WaterLevel[i]=Mathf.Max(Height[i],spill[i]);Height[i]=WaterLevel[i]-depth;
            }
            // Carve the requested physical channel width with wrapped neighbours. Keep centreline
            // metadata for ribbon geometry instead of turning every widened cell into a tributary.
            var levels=(float[])WaterLevel.Clone();var bed=(float[])Height.Clone();
            float radius=configuration.riverWidth*.5f;int reach=Mathf.CeilToInt(radius/Cell)+1;
            for(int i=0;i<n;i++)if(RiverCentre[i]){
                int downstream=Downstream[i];if(downstream<0)continue;Vector2 a=Position(i),edge=WorldTopology.Delta(a,Position(downstream),Size);
                float end=Water[downstream]>0?levels[downstream]:bed[downstream];float depth=levels[i]-bed[i];
                for(int y=-reach;y<=reach;y++)for(int x=-reach;x<=reach;x++){
                    int j=WorldTopology.Index(i%Resolution+x,i/Resolution+y,Resolution);if(Water[j]>0&&Water[j]!=4)continue;
                    Vector2 local=WorldTopology.Delta(a,Position(j),Size);float t=Mathf.Clamp01(Vector2.Dot(local,edge)/Mathf.Max(.001f,edge.sqrMagnitude));
                    if((local-edge*t).sqrMagnitude>radius*radius)continue;float level=Mathf.Lerp(levels[i],end,t);
                    if(Water[j]==4&&WaterLevel[j]-Height[j]>=depth)continue;Water[j]=4;WaterLevel[j]=level;Height[j]=Mathf.Min(Height[j],level-depth);
                }
            }
        }
        void Climate()
        {
            for(int i=0;i<Height.Length;i++)
            {
                Vector2 p=Position(i);int x=i%Resolution,y=i/Resolution;
                float dx=(H(x+1,y)-H(x-1,y))/(2*Cell),dz=(H(x,y+1)-H(x,y-1))/(2*Cell);
                Slope[i]=Mathf.Sqrt(dx*dx+dz*dz);
                // One regional climate: elevation, shelter and catchments create local habitats.
                float t=Mathf.Clamp01(configuration.temperature*.01f-Height[i]*.00065f+(Noise(p.x,p.y,2/Size,23)-.5f)*(configuration.climate==RegionalClimate.Mixed?Mathf.Max(75,configuration.climateVariation):configuration.climateVariation)*.006f);
                float barrier=0;for(int k=1;k<=12;k++)barrier=Mathf.Max(barrier,H(x-k*2,y)-Height[i]);
                float moisture=Mathf.Clamp01(configuration.rainfall*.01f+(Noise(p.x,p.y,2/Size,133)-.5f)*configuration.climateVariation*.0096f-barrier*.0024f*configuration.rainShadow*.01f+Drainage[i]*.15f+Mathf.Clamp01(Sediment[i])*.06f);
                Temperature[i]=t;Moisture[i]=moisture;
                Biome b;
                if(Water[i]>0)b=(Biome)(Water[i]-1);
                else if(configuration.oceanCoverage>0&&Height[i]<5)b=MassiveArmy.Biome.Beach;
                else if(t<.08f&&Height[i]>100)b=MassiveArmy.Biome.Glacier;
                else if((Height[i]>185&&Slope[i]>.3f)||Slope[i]>.7f)b=MassiveArmy.Biome.Alpine;
                else if(t<.14f)b=moisture<.34f?MassiveArmy.Biome.ColdDesert:MassiveArmy.Biome.Tundra;
                else if(t<.3f)b=moisture>.42f?MassiveArmy.Biome.BorealForest:MassiveArmy.Biome.Tundra;
                else if(t>.68f)b=moisture<.38f?MassiveArmy.Biome.HotDesert:moisture<.53f?MassiveArmy.Biome.Savanna:moisture<.68f?MassiveArmy.Biome.SeasonalForest:MassiveArmy.Biome.Rainforest;
                else if(moisture<.3f)b=MassiveArmy.Biome.Steppe;
                else if(t>.5f&&moisture<.43f)b=MassiveArmy.Biome.Mediterranean;
                else if(moisture<.5f)b=MassiveArmy.Biome.Grassland;
                else if(moisture>.7f)b=MassiveArmy.Biome.TemperateRainforest;
                else b=MassiveArmy.Biome.TemperateForest;
                if(Water[i]==0&&Slope[i]<.12f&&moisture>.65f&&NearWater(x,y,2)&&t>.3f)b=MassiveArmy.Biome.Wetland;
                Biome[i]=b;
                float cluster=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.39f,.64f,Noise(p.x,p.y,Mathf.Clamp(100/Mathf.Max(1,configuration.forestPatchSize),1,100)/Size,67)+Drainage[i]*.08f));
                cluster=Mathf.Lerp(.6f,cluster,configuration.forestClustering*.01f)*(1-Mathf.SmoothStep(.5f,1,Noise(p.x,p.y,12/Size,721))*configuration.clearingFrequency*.01f);
                Forest[i]=Mathf.Clamp01(Biomes.Forest(b)*configuration.forestCoverage*.02f)*cluster*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.4f,.8f,Slope[i])));
            }
        }
        bool NearWater(int x,int y,int radius)
        { for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++) { int nx=x+dx,ny=y+dy;if(Water[WorldTopology.Index(nx,ny,Resolution)]>0)return true; }return false; }
        public bool Ground(int i) => Water[i]==0 && Slope[i]<.55f && !RockObstacle[i];
        public void SurfaceLayers(int i,out Color a,out Color b)
        {
            Biomes.Layers(Biome[i],Slope[i],Height[i],out a,out b);
            if(Water[i]>0)return;
            // Open ground inside a woodland climate remains vegetated; litter follows actual canopy.
            if(Biomes.Forest(Biome[i])>.3f){a.r+=a.a;a.a=0;}
            int x=i%Resolution,y=i/Resolution;
            float concavity=((H(x-1,y)+H(x+1,y)+H(x,y-1)+H(x,y+1))*.25f-Height[i])/Cell;
            float wet=Moisture[i]*Drainage[i]*Mathf.Clamp01(1-Slope[i]*4);
            float deposit=Mathf.Clamp01(Sediment[i]*.3f+Mathf.Max(0,concavity)*2);
            float mud=Mathf.SmoothStep(0,.45f,Mathf.InverseLerp(.28f,.62f,wet))*deposit;
            float scree=Mathf.SmoothStep(0,.5f,Mathf.InverseLerp(.16f,.6f,Slope[i]))*Mathf.Clamp01(.3f+Sediment[i]*.2f);
            float soil=Mathf.Clamp01(Forest[i]*.65f)*(1-b.r);
            float sum=mud+scree+soil,scale=1/(1+sum);a*=scale;b*=scale;a.a+=soil*scale;b.b+=mud*scale;b.a+=scree*scale;
        }
        void Roads()
        {
            int n=Height.Length;var pass=new bool[n];var labels=new int[n];var queue=new Queue<int>();int label=0;
            for(int i=0;i<n;i++)pass[i]=Water[i]==0&&Slope[i]<.48f&&!NearWater(i%Resolution,i/Resolution,2);
            for(int s=0;s<n;s++)if(pass[s]&&labels[s]==0) { labels[s]=++label;queue.Enqueue(s);
                while(queue.Count>0) { int i=queue.Dequeue(),x=i%Resolution,y=i/Resolution;
                    for(int k=0;k<8;k++) { int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                        int j=nx+ny*Resolution;if(!pass[j]||labels[j]!=0||!pass[nx+y*Resolution]||!pass[x+ny*Resolution])continue;labels[j]=label;queue.Enqueue(j); } } }
            var anchors=new int[16];var random=new System.Random(Seed^12833);
            for(int s=0;s<16;s++) { Vector2 target=new Vector2((s%4-1.5f)*Size/4,(s/4-1.5f)*Size/4);
                target+=new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*Size*.075f;
                float best=float.MaxValue;int chosen=Index(Vector2.zero);
                for(int i=0;i<n;i++)if(pass[i]) { float distance=WorldTopology.Delta(target,Position(i),Size).sqrMagnitude; if(distance<best) { best=distance;chosen=i; } }
                anchors[s]=chosen;Sites[s]=Position(chosen); }
            var pairs=new List<Vector3>();
            for(int a=0;a<16;a++)for(int b=a+1;b<16;b++)if(labels[anchors[a]]==labels[anchors[b]])pairs.Add(new Vector3(a,b,WorldTopology.Delta(Sites[a],Sites[b],Size).sqrMagnitude));
            pairs.Sort((a,b)=>a.z.CompareTo(b.z));var sets=new int[16];for(int i=0;i<16;i++)sets[i]=i;
            int Root(int i) { while(sets[i]!=i)i=sets[i];return i; }
            int loops=0,connections=0;
            foreach(var pair in pairs) { cancel.ThrowIfCancellationRequested();int a=(int)pair.x,b=(int)pair.y,ra=Root(a),rb=Root(b);
                if(configuration.roadDensity<=0)break;bool connect=ra!=rb;if(!connect&&((a+b)%3!=0||loops>=Mathf.RoundToInt(configuration.roadLoops*.23f)))continue;
                if(connect&&connections>=Mathf.RoundToInt(configuration.roadDensity*.3f))continue;
                if(Route(anchors[a],anchors[b],pass)) { if(connect){sets[ra]=rb;connections++;}else loops++; } }
            for(int i=0;i<n;i++)Forest[i]*=1-Road[i];
        }
        bool Route(int start,int goal,bool[] pass)
        {
            int n=Height.Length;var cost=new float[n];var previous=new int[n];var closed=new bool[n];
            for(int i=0;i<n;i++) { cost[i]=float.MaxValue;previous[i]=-1; }
            var heap=new Heap();cost[start]=0;heap.Push(start,0);int visits=0;
            while(heap.Count>0) { if((visits++&4095)==0)cancel.ThrowIfCancellationRequested();int i=heap.Pop(out _);if(closed[i])continue;closed[i]=true;if(i==goal)break;
                int x=i%Resolution,y=i/Resolution;
                for(int k=0;k<8;k++) { int nx=x+DX[k],ny=y+DY[k];nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                    int j=nx+ny*Resolution;if(!pass[j]||closed[j]||!pass[nx+y*Resolution]||!pass[x+ny*Resolution])continue;
                    Vector2 p=Position(j);float step=(DX[k]!=0&&DY[k]!=0?1.414214f:1)*(1+Slope[j]*configuration.flatRoutePreference*.123f+Forest[j]*.4f+Noise(p.x,p.y,.006f,14)*configuration.roadCurvature*.04f);
                    float c=cost[i]+step;if(c>=cost[j])continue;cost[j]=c;previous[j]=i;
                    heap.Push(j,c+WorldTopology.Distance(Position(j),Position(goal),Size)/Cell); }
            }
            if(previous[goal]<0&&start!=goal)return false;
            for(int i=goal;i>=0;i=previous[i]) { int x=i%Resolution,y=i/Resolution;
                for(int dy=-Mathf.CeilToInt(configuration.roadWidth/Cell);dy<=Mathf.CeilToInt(configuration.roadWidth/Cell);dy++)for(int dx=-Mathf.CeilToInt(configuration.roadWidth/Cell);dx<=Mathf.CeilToInt(configuration.roadWidth/Cell);dx++) { int nx=x+dx,ny=y+dy;nx=WorldTopology.Cell(nx,Resolution);ny=WorldTopology.Cell(ny,Resolution);
                    int j=nx+ny*Resolution;if(!pass[j])continue;float brush=1-Mathf.SmoothStep(configuration.roadWidth*.3f,configuration.roadWidth*.65f,Mathf.Sqrt(dx*dx+dy*dy)*Cell);Road[j]=Mathf.Max(Road[j],brush); } }
            return true;
        }
        void Decorate()
        {
            var random=new System.Random(Seed^9193);
            for(int attempt=0;attempt<36000;attempt++)
            {
                if((attempt&1023)==0)cancel.ThrowIfCancellationRequested();
                Vector2 p=new Vector2(((float)random.NextDouble()-.5f)*Size,((float)random.NextDouble()-.5f)*Size);int i=Index(p);
                if(Water[i]!=0||Road[i]>.04f||Slope[i]>.85f||NearWater(i%Resolution,i/Resolution,1))continue;
                bool siteClearance=false;foreach(Vector2 site in Sites)if(WorldTopology.Delta(site,p,Size).sqrMagnitude<Mathf.Max(16,Cell*2)*Mathf.Max(16,Cell*2)) { siteClearance=true;break; }
                if(siteClearance)continue;
                float patch=Noise(p.x,p.y,.019f,251);int style;float scale;Color tint;
                if(random.NextDouble()<Forest[i]*.55f) { Biome b=Biome[i];style=b==MassiveArmy.Biome.BorealForest?0:b==MassiveArmy.Biome.Rainforest||b==MassiveArmy.Biome.SeasonalForest?2:1;
                    scale=2+(float)random.NextDouble()*2.8f;tint=b==MassiveArmy.Biome.BorealForest?new Color(.1f,.22f,.18f):new Color(.14f,.3f,.09f); }
                else if(patch>.59f&&random.NextDouble()<(.12+Slope[i]*.3)*configuration.rockAbundance*.025f) { style=5;scale=.8f+(float)random.NextDouble()*3.7f;
                    tint=Biome[i]==MassiveArmy.Biome.Glacier?new Color(.65f,.77f,.8f):new Color(.28f,.29f,.26f); }
                else if(patch>.56f&&random.NextDouble()<.18*configuration.shrubAbundance*.025f&&Biomes.Forest(Biome[i])<.3f) {
                    style=Biome[i]==MassiveArmy.Biome.HotDesert?4:Biome[i]==MassiveArmy.Biome.Savanna?3:6;scale=1+(float)random.NextDouble()*1.7f;tint=new Color(.28f,.31f,.12f);
                    if(Biome[i]==MassiveArmy.Biome.Glacier||Biome[i]==MassiveArmy.Biome.Alpine||Biome[i]==MassiveArmy.Biome.ColdDesert)continue;
                } else continue;
                tint*=.8f+(float)random.NextDouble()*.4f;
                // Start validation accounts for boulder obstacles on every island.
                if(style==5&&scale>3.3f)RockObstacle[i]=true;
                Scenery[style].Add(new SceneryInstance { positionScale=new Vector4(p.x,SampleHeight(p),p.y,scale),tintYaw=new Vector4(tint.r,tint.g,tint.b,(float)random.NextDouble()*Mathf.PI*2) });
            }
        }
        public float SampleHeight(Vector2 p) { Vector2 f=(p/Size+Vector2.one*.5f)*Resolution-Vector2.one*.5f;int x=Mathf.FloorToInt(f.x),y=Mathf.FloorToInt(f.y);
            return Mathf.Lerp(Mathf.Lerp(H(x,y),H(x+1,y),f.x-x),Mathf.Lerp(H(x,y+1),H(x+1,y+1),f.x-x),f.y-y); }
        public Color WaterData(int i) => new Color(WaterLevel[i],Water[i]==0?0:Mathf.Max(0,WaterLevel[i]-Height[i]),Water[i],0);
        public Color MapColour(int i) => Biomes.Colours[(int)Biome[i]];
        public void Export(string folder)
        {
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"world-settings.json"),JsonUtility.ToJson(configuration,true));var pixels=new Color[Height.Length];var counts=new int[Biomes.Names.Length];
            for(int i=0;i<pixels.Length;i++) { pixels[i]=MapColour(i);counts[(int)Biome[i]]++; }
            var texture=new Texture2D(Resolution,Resolution,TextureFormat.RGBA32,false);texture.SetPixels(pixels);texture.Apply();
            File.WriteAllBytes(Path.Combine(folder,$"biomes-{Seed}.png"),texture.EncodeToPNG());
            for(int i=0;i<pixels.Length;i++)pixels[i]=Water[i]>0?MapColour(i):new Color(.17f,.2f,.17f);texture.SetPixels(pixels);texture.Apply();
            File.WriteAllBytes(Path.Combine(folder,$"water-{Seed}.png"),texture.EncodeToPNG());
            float min=float.MaxValue,max=float.MinValue;foreach(float h in Height){min=Mathf.Min(min,h);max=Mathf.Max(max,h);}
            for(int i=0;i<pixels.Length;i++){float h=Mathf.InverseLerp(min,max,Height[i]);pixels[i]=new Color(h,h,h);}texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(Path.Combine(folder,$"height-{Seed}.png"),texture.EncodeToPNG());
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(Drainage[i],Mathf.Clamp01(Sediment[i]),Slope[i]);texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(Path.Combine(folder,$"geography-{Seed}.png"),texture.EncodeToPNG());ArmyLifetime.Destroy(texture);
            var lines=new List<string> { $"Seed: {Seed}; resolution: {Resolution}; size: {Size}; north: +Z", "Terrain layers: grass, sand, dry grass, forest floor, rock, snow, mud, gravel" };
            for(int b=0;b<counts.Length;b++)lines.Add($"{b}: {Biomes.Names[b]} | #{ColorUtility.ToHtmlStringRGB(Biomes.Colours[b])} | {counts[b]} cells");
            File.WriteAllLines(Path.Combine(folder,$"legend-{Seed}.txt"),lines);
        }
        sealed class Heap
        {
            readonly List<int> ids=new List<int>();readonly List<float> keys=new List<float>();public int Count=>ids.Count;
            public void Push(int id,float key) { int i=ids.Count;ids.Add(id);keys.Add(key);while(i>0) { int p=(i-1)/2;if(keys[p]<=key)break;ids[i]=ids[p];keys[i]=keys[p];i=p; }ids[i]=id;keys[i]=key; }
            public int Pop(out float key) { int result=ids[0];key=keys[0];int last=ids.Count-1,id=ids[last];float k=keys[last];ids.RemoveAt(last);keys.RemoveAt(last);
                if(last==0)return result;int i=0;while(i*2+1<last) { int c=i*2+1;if(c+1<last&&keys[c+1]<keys[c])c++;if(keys[c]>=k)break;ids[i]=ids[c];keys[i]=keys[c];i=c; }ids[i]=id;keys[i]=k;return result; }
        }
    }
}
