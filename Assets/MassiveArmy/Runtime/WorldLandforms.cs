using System;
using System.Threading;
using UnityEngine;
namespace MassiveArmy
{
    public sealed partial class WorldMap
    {
        readonly WorldGenerationSettings configuration;
        readonly CancellationToken cancel;
        public WorldGenerationSettings Configuration=>configuration.Copy();
        Vector3[] lakeBasins;
        float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {
            Vector2 edge=WorldTopology.Delta(a,b,Size),local=WorldTopology.Delta(a,p,Size);
            return (local-edge*Mathf.Clamp01(Vector2.Dot(local,edge)/Mathf.Max(.001f,edge.sqrMagnitude))).magnitude;
        }
        void Landforms()
        {
            var random=new System.Random(Seed^6713);var c=configuration;
            Vector2 Point()=>new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*Size;
            var centres=new Vector2[c.continents];for(int k=0;k<centres.Length;k++)centres[k]=Point();
            int islands=c.oceanCoverage>0?Mathf.RoundToInt(c.islandAbundance*.12f):0;
            var small=new Vector2[islands];for(int k=0;k<small.Length;k++){Vector2 a=Point(),b=centres[k%centres.Length]+Point()*.4f;small[k]=WorldTopology.Wrap(a+WorldTopology.Delta(a,b,Size)*c.islandClustering*.01f,Size);}
            int ranges=Mathf.Max(1,c.continents);var ridges=new Vector2[ranges,7];
            for(int k=0;k<ranges;k++){
                float angle=(float)random.NextDouble()*Mathf.PI*2;Vector2 axis=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),side=new Vector2(-axis.y,axis.x);
                for(int j=0;j<7;j++)ridges[k,j]=WorldTopology.Wrap(centres[k]+axis*(j/6f-.5f)*Size*c.ridgeLength*.01f+side*Mathf.Sin(j*.8f+k)*Size*.04f,Size);
            }
            int basins=c.lakeAbundance>0?Mathf.Max(1,Mathf.RoundToInt(c.lakeAbundance*.12f)):0;
            lakeBasins=new Vector3[basins];for(int k=0;k<basins;k++){Vector2 p=Point();lakeBasins[k]=new Vector3(p.x,p.y,Mathf.Max(Cell*2,Size*c.lakeSize*.005f*Mathf.Lerp(.6f,1.4f,c.basinScale*.01f)));}
            float landRadius=Size/Mathf.Sqrt(c.continents)*.32f;
            for(int i=0;i<Height.Length;i++){
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();Vector2 p=Position(i);float land=0;
                foreach(var centre in centres){float d=WorldTopology.Distance(p,centre,Size)/landRadius;land=Mathf.Max(land,Mathf.Exp(-d*d*1.5f));}
                foreach(var centre in small){float d=WorldTopology.Distance(p,centre,Size)/Mathf.Max(Cell,Size*c.islandSize*.005f);land=Mathf.Max(land,Mathf.Exp(-d*d*1.5f)*.75f);}
                float ridge=0,foothill=0,nearestRidge=float.MaxValue,width=Size*Mathf.Lerp(.018f,.07f,c.mountainCoverage*.01f);
                float foothillRadius=Mathf.Max(width,Size*c.foothillWidth*.005f),valleyRadius=Mathf.Max(Cell,Size*c.valleyWidth*.005f);
                for(int k=0;k<ranges;k++)for(int j=0;j<6;j++){
                    float metres=SegmentDistance(p,ridges[k,j],ridges[k,j+1]),d=metres/width;nearestRidge=Mathf.Min(nearestRidge,metres);
                    ridge=Mathf.Max(ridge,Mathf.Exp(-d*d));foothill=Mathf.Max(foothill,Mathf.Exp(-metres*metres/(foothillRadius*foothillRadius)));
                }
                float coast=(Noise(p.x,p.y,6/Size,18)-.5f)*c.coastRoughness*.007f;
                float h=35+land*75+coast*60+(Noise(p.x,p.y,3/Size,4)-.5f)*12;
                float valley=c.valleyWidth>0?Mathf.Exp(-Mathf.Pow((nearestRidge-width-valleyRadius)/valleyRadius,2)):0;
                if(c.mountainCoverage>0)h+=(ridge*c.reliefHeight+foothill*c.reliefHeight*.12f-valley*c.reliefHeight*.12f)*(c.oceanCoverage>0?Mathf.SmoothStep(0,1,land):1);
                h+=(Noise(p.x,p.y,24/Size,87)-.5f)*(1+ridge*5);
                foreach(var b in lakeBasins){float d=WorldTopology.Distance(p,new Vector2(b.x,b.y),Size)/b.z;h-=Mathf.Exp(-d*d*2.7f)*Mathf.Min(60,b.z*.55f);}
                Height[i]=h;
            }
            // Sea coverage is a quantile of the periodic field, never an artificial edge coast.
            if(c.oceanCoverage>0){var sorted=(float[])Height.Clone();Array.Sort(sorted);float sea=sorted[Mathf.Clamp((int)(Height.Length*c.oceanCoverage*.01f),0,sorted.Length-1)];for(int i=0;i<Height.Length;i++)Height[i]-=sea;}
            else {float min=float.MaxValue;foreach(float h in Height)min=Mathf.Min(min,h);for(int i=0;i<Height.Length;i++)Height[i]+=12-min;}
        }
    }
}
