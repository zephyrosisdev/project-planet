using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class DeploymentValidation
    {
        [Serializable] sealed class Scale { public int seed,units,resolution,connectedCells;public float size,hullGap; }
        [Serializable] sealed class Report { public string status,error,timestamp;public string[] checks;public Scale[] scales; }
        [MenuItem("Massive Army/Validate Army Deployment")]
        public static void Run()
        {
            var report=new Report{status="PASS",timestamp=DateTime.UtcNow.ToString("O")};
            var checks=new List<string>();var scales=new List<Scale>();
            try {
                foreach(int seed in new[]{73129,332385012,99881})foreach(int count in new[]{10000,50000,100000,250000,500000,1000000}) {
                    if(seed!=73129&&count!=1000000)continue;
                    var settings=new BattleSettings{seed=seed,activeUnits=count,capacity=count};settings.ScaleWorld();
                    var deployment=DeploymentMap.Create(settings,out var slots);
                    var occupied=new HashSet<long>();int blue=(count+1)/2;
                    for(int i=0;i<slots.Length;i++) {
                        Vector2 p=slots[i];
                        int x=Mathf.RoundToInt((Mathf.Abs(p.x)-24)/BattleSettings.FormationSpacing),y=Mathf.RoundToInt(p.y/BattleSettings.FormationSpacing);
                        long key=((long)(x*(i<blue?-1:1)+(i<blue?-1:1))<<32)|(uint)y;
                        if(!occupied.Add(key)||!deployment.Safe(p)||((i<blue)!=(p.x<0)) ||
                            Mathf.Abs(Mathf.Abs(p.x)-(24+x*BattleSettings.FormationSpacing))>.002f ||
                            Mathf.Abs(p.y-y*BattleSettings.FormationSpacing)>.002f)throw new InvalidOperationException("Unsafe or overlapping deployment at "+seed+" / "+count+" / "+i);
                    }
                    if(slots.Length!=count)throw new InvalidOperationException("Incomplete deployment");
                    checks.Add(seed+" / "+count+": every spawn has hull clearance on the main connected ground component, unique 5.2m lattice, balanced sides");
                    scales.Add(new Scale{seed=seed,units=count,size=settings.worldSize,resolution=settings.navigationResolution,connectedCells=deployment.ConnectedCells,hullGap=BattleSettings.FormationSpacing-3.4f});
                    File.WriteAllLines("TestResults/deployment-progress.txt",checks);
                }
                // Deliberately isolate a walkable summit from the main plain and ensure it is excluded.
                var map=new WorldMap(64,2048,73129);
                for(int i=0;i<map.Height.Length;i++) { map.Water[i]=0;map.WaterLevel[i]=map.Height[i];map.Slope[i]=0;map.RockObstacle[i]=false; }
                for(int y=10;y<=16;y++)for(int x=10;x<=16;x++)if(x==10||x==16||y==10||y==16)map.Slope[x+y*64]=2;
                var isolated=new DeploymentMap(map);
                if(isolated.Costs[2*4096+13+13*64]<DeploymentMap.Blocked*.5f)throw new InvalidOperationException("Isolated summit remains available");
                checks.Add("Isolated walkable mountain summit excluded; terrain connectivity forbids diagonal corner leaks");
                if(new DeploymentMap(new WorldMap(64,2048,73129)).Slots(1000000)!=null)throw new InvalidOperationException("Undersized map accepted");
                checks.Add("Undersized terrain refuses deployment instead of shrinking gaps or random retry placement");
            } catch(Exception e) { report.status="FAIL";report.error=e.ToString();Debug.LogException(e); }
            report.checks=checks.ToArray();report.scales=scales.ToArray();File.WriteAllText("TestResults/deployment-validation.json",JsonUtility.ToJson(report,true));
            Debug.Log("Army deployment validation: "+report.status+" ("+checks.Count+" checks)");
        }
    }
}
