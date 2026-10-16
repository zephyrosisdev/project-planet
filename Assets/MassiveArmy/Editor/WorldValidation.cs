using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class WorldValidation
    {
        [Serializable] sealed class Report { public int generatorVersion;public string status,error,timestamp;public string[] checks;public SeedResult[] seeds; }
        [Serializable] sealed class SeedResult { public int seed,roads,trees,rocks;public float minHeight,maxHeight;public int[] biomeCells,waterCells; }
        [MenuItem("Massive Army/Validate Biome Generation")]
        public static void Run()
        {
            var checks=new List<string>();var seeds=new List<SeedResult>();var report=new Report{generatorVersion=WorldMap.GeneratorVersion,status="PASS",timestamp=DateTime.UtcNow.ToString("O")};
            void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message);checks.Add(message); }
            try
            {
                foreach(int seed in new[]{73129,14351,99881})
                {
                    var map=new WorldMap(256,2048,seed);int n=map.Height.Length;
                    var stats=new SeedResult{seed=seed,biomeCells=new int[Biomes.Names.Length],waterCells=new int[5],minHeight=map.Height.Min(),maxHeight=map.Height.Max()};
                    for(int i=0;i<n;i++) { stats.biomeCells[(int)map.Biome[i]]++;stats.waterCells[map.Water[i]]++;if(map.Road[i]>.01f)stats.roads++; }
                    stats.trees=map.Scenery.Take(5).Sum(a=>a.Count);stats.rocks=map.Scenery[5].Count;seeds.Add(stats);
                    Require(map.Height.All(h=>!float.IsNaN(h)&&!float.IsInfinity(h)),seed+": finite elevation");
                    Require(stats.maxHeight>110&&stats.minHeight<0,seed+": mountains and ocean bed");
                    Require(Enumerable.Range(0,n).Count(i=>map.Water[i]==0&&map.Slope[i]<.10f)>n*.1,seed+": substantial flat plains");
                    Require(stats.biomeCells.Count(c=>c>0)>=5&&stats.biomeCells.Count(c=>c>0)<=12,seed+": coherent regional habitats instead of every global biome");
                    Require(stats.biomeCells[(int)Biome.HotDesert]==0&&stats.biomeCells[(int)Biome.Rainforest]==0&&stats.biomeCells[(int)Biome.Glacier]==0,seed+": temperate region excludes tropical desert and polar glacier");
                    Require(map.Drainage.All(f=>f>=0&&f<=1)&&map.Sediment.All(f=>f>=0&&float.IsFinite(f))&&map.Sediment.Max()>.1f,seed+": finite catchment and deposited sediment fields");
                    Require(stats.waterCells.Skip(1).All(c=>c>0),seed+": ocean, lakes, ponds and rivers present");
                    Require(stats.roads>200,seed+": sprawling road network");
                    Require(Enumerable.Range(0,n).All(i=>map.Road[i]==0||map.Water[i]==0),seed+": no road over water");
                    bool buffer=true;
                    for(int i=0;i<n;i++)if(map.Road[i]>0)for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++) {
                        int x=i%256+dx,y=i/256+dy;if(x>=0&&y>=0&&x<256&&y<256&&map.Water[x+y*256]>0)buffer=false;
                    }
                    Require(buffer,seed+": road footprint has a two-cell shoreline buffer");
                    Require(Enumerable.Range(0,n).All(i=>map.Water[i]==0||map.WaterLevel[i]>map.Height[i]),seed+": all water surfaces are above their beds");
                    bool drainage=true;var state=new byte[n];
                    for(int i=0;i<n;i++) { int j=i;while(j>=0&&state[j]==0) { state[j]=1;j=map.Downstream[j]; }
                        if(j>=0&&state[j]==1)drainage=false;j=i;while(j>=0&&state[j]==1) { state[j]=2;j=map.Downstream[j]; } }
                    Require(drainage,seed+": drainage graph has no cycles");
                    Require(map.Sites.All(p=>map.Ground(map.Index(p))),seed+": all strategic sites lie on traversable dry ground");
                    Require(map.Scenery.SelectMany(a=>a).All(p=>map.Water[map.Index(new Vector2(p.positionScale.x,p.positionScale.z))]==0&&map.Road[map.Index(new Vector2(p.positionScale.x,p.positionScale.z))]<.05f),seed+": scenery avoids water and roads");
                    bool layers=true;for(int i=0;i<n;i++) { map.SurfaceLayers(i,out var a,out var b);float total=a.r+a.g+a.b+a.a+b.r+b.g+b.b+b.a;if(Mathf.Abs(total-1)>.001f)layers=false; }
                    Require(layers,seed+": eight surface weights sum to one");
                    Require(map.Forest.Count(f=>f>.4f)>100&&map.Forest.Count(f=>f==0)>n*.25,seed+": forest patches and broad clear regions");
                    if(seed==73129) {
                        var repeat=new WorldMap(256,2048,seed);
                        Require(map.Height.SequenceEqual(repeat.Height)&&map.Biome.SequenceEqual(repeat.Biome)&&map.Road.SequenceEqual(repeat.Road),"Same seed regenerates identical heights, biomes and roads");
                        map.Export("TestResults/BiomeMaps");
                    }
                }
                Require(BattleSettings.FormationSpacing>=UnitArchetype.Tank.radius*2+.8f,"Formation slots include a margin around the full visible hull radius");
            }
            catch(Exception e) { report.status="FAIL";report.error=e.ToString();Debug.LogException(e); }
            report.checks=checks.ToArray();report.seeds=seeds.ToArray();Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/world-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Biome generation validation: "+report.status+" ("+checks.Count+" checks)");
            if(Application.isBatchMode)EditorApplication.Exit(report.status=="PASS"?0:1);
        }
    }
}
