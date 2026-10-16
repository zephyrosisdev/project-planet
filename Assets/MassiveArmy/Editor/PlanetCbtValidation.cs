using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class PlanetCbtValidation
    {
        [Serializable]sealed class Report{public string status,error;public List<string> checks=new List<string>();public long bufferBytes;public uint active,visible,saturated;}
        [MenuItem("Massive Army/Planetary/Validate Adaptive CBT Terrain")]
        public static void Run()
        {
            var report=new Report{status="PASS",error=""};var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.terrainHeight=12;
            var root=new GameObject("GPU CBT validation"){hideFlags=HideFlags.DontSave};var camera=root.AddComponent<Camera>();var target=new RenderTexture(2560,1440,24);camera.targetTexture=target;camera.fieldOfView=48;camera.nearClipPlane=.05f;camera.farClipPlane=30000;
            void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                using(var terrain=new PlanetCbtTerrain(definition,8)){
                    report.bufferBytes=terrain.BufferBytes;
                    var positions=new[]{Vector3.forward,Vector3.up,Vector3.down,Vector3.right,new Vector3(1,1,1).normalized};
                    foreach(var direction in positions){camera.transform.position=direction*(definition.radius+25);CubeSphere.Project(direction,out var face,out _);camera.transform.rotation=Quaternion.LookRotation(-direction,CubeSphere.Right[(int)face]);
                        for(int frame=0;frame<24;frame++){terrain.Update(camera,Vector3.zero,2);ValidateForest(terrain);}
                        Check(true,"GPU split/merge stays watertight through refinement and camera transition at "+direction);
                    }
                    var stats=new uint[8];terrain.Statistics.GetData(stats);Check(stats[0]>12&&stats[1]>0&&stats[1]<stats[0],"Adaptive refinement and frustum/horizon culling reduce visible geometry");
                    var draw=new uint[4];terrain.DrawArguments.GetData(draw);Check(draw[0]==stats[1]*3&&draw[1]==1,"GPU indirect draw count exactly matches compacted visible triangles");
                    Check(stats[5]>0,"Insufficient depth is explicitly reported as saturated geometry, not a false error-target pass");
                    report.active=stats[0];report.visible=stats[1];report.saturated=stats[5];
                    camera.transform.position=Vector3.forward*10000;camera.transform.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
                    terrain.Update(camera,Vector3.zero,2,refine:false);terrain.Statistics.GetData(stats);Check(stats[1]==0,"Culling responds immediately when refinement is skipped between simulation ticks");ValidateForest(terrain);
                    for(int frame=0;frame<24;frame++){terrain.Update(camera,Vector3.zero,2);ValidateForest(terrain);}
                    terrain.Statistics.GetData(stats);Check(stats[0]==12&&stats[1]==0,"Off-screen terrain merges to twelve roots and produces zero draw vertices");
                    // Exact integer seam keys, not float hashing, remain injective for each bisection level.
                    ValidateMidpoints(terrain.Grid,terrain.MaximumDepth);ValidateMidpoints(128,14);Check(true,"All midpoint keys identify exactly one edge through depth 14, including cube seams and corner-adjacent triangles");
                }
                using(var terrain=new PlanetCbtTerrain(definition)){
                    var direction=new Vector3(1,1,1).normalized;var surface=new PlanetSurface(definition);camera.transform.position=surface.GetSurfacePosition(direction,1.7f);camera.transform.rotation=Quaternion.LookRotation(-direction,Vector3.right);
                    for(int frame=0;frame<18;frame++){terrain.Update(camera,Vector3.zero,2);ValidateForest(terrain);}
                    var stats=new uint[8];terrain.Statistics.GetData(stats);Check(stats[0]>12&&stats[1]>0,"Depth-14 terrain refines around a near-surface observer");Check(terrain.BufferBytes<16*1048576L,"Default depth-14 buffers remain bounded below 16 MiB");
                    report.bufferBytes=terrain.BufferBytes;report.active=stats[0];report.visible=stats[1];report.saturated=stats[5];Check(true,"Depth-14 cube-corner refinement remains watertight with complete binary-tree coverage");
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition);}
            Directory.CreateDirectory("TestResults/Planetary/Terrain");File.WriteAllText("TestResults/Planetary/Terrain/validation.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Planet CBT validation PASS");
        }
        static ulong Edge(int a,int b)=>((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
        static void ValidateForest(PlanetCbtTerrain terrain)
        {
            var states=new uint[12*terrain.TreeWords];var counts=new uint[states.Length];terrain.States.GetData(states);terrain.Counts.GetData(counts);var edges=new Dictionary<ulong,int>();uint total=0;
            for(int root=0;root<12;root++){
                double coverage=0;uint leaves=0;
                for(uint node=1;node<terrain.TreeWords;node++){
                    int index=root*terrain.TreeWords+(int)node;uint state=states[index];if(state==0){if(counts[index]!=0)throw new InvalidOperationException("Inactive CBT node has nonzero count");continue;}
                    if(state==2){if(node*2+1>=terrain.TreeWords||states[root*terrain.TreeWords+node*2]==0||states[root*terrain.TreeWords+node*2+1]==0)throw new InvalidOperationException("Split node is missing children");continue;}
                    if(state!=1||counts[index]!=1)throw new InvalidOperationException("Invalid CBT leaf/count");leaves++;coverage+=1.0/(1<<PlanetCbtTopology.Depth(node));
                    PlanetCbtTopology.Decode(root,node,terrain.Grid,out var a,out var b,out var c);
                    int ia=PlanetCbtTopology.VertexIndex(root,a,terrain.Grid),ib=PlanetCbtTopology.VertexIndex(root,b,terrain.Grid),ic=PlanetCbtTopology.VertexIndex(root,c,terrain.Grid);
                    foreach(var edge in new[]{Edge(ia,ib),Edge(ib,ic),Edge(ic,ia)}){edges.TryGetValue(edge,out int n);edges[edge]=n+1;}
                }
                if(Math.Abs(coverage-1)>1e-9||counts[root*terrain.TreeWords+1]!=leaves)throw new InvalidOperationException("CBT root coverage or leaf population is incorrect");total+=leaves;
            }
            foreach(var pair in edges)if(pair.Value!=2)throw new InvalidOperationException("Crack / T-junction / nonmanifold edge: "+pair.Key+" has "+pair.Value+" incident triangles");
            var stats=new uint[8];terrain.Statistics.GetData(stats);if(stats[0]!=total)throw new InvalidOperationException("GPU active count does not match binary tree population");
        }
        static void ValidateMidpoints(int grid,int depth)
        {
            var keys=new Dictionary<long,ulong>();int vertices=6*(grid+1)*(grid+1);
            for(int root=0;root<12;root++)for(uint node=1;node<(1<<depth);node++){
                PlanetCbtTopology.Decode(root,node,grid,out var a,out _,out var c);long key=(long)PlanetCbtTopology.Depth(node)*vertices+PlanetCbtTopology.VertexIndex(root,(a+c)/2,grid);ulong edge=Edge(PlanetCbtTopology.VertexIndex(root,a,grid),PlanetCbtTopology.VertexIndex(root,c,grid));
                if(keys.TryGetValue(key,out ulong previous)&&previous!=edge)throw new InvalidOperationException("Midpoint key aliases different bisection edges");keys[key]=edge;
            }
        }
    }
}
