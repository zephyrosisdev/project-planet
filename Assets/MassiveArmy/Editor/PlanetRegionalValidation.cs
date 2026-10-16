using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    public static class PlanetRegionalValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        public static void Run(){var report=new Report();void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);report.checks.Add(message);}
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.radius=PlanetDefinition.TargetRadius;definition.terrainHeight=0;definition.oceanHeight=-8;
            var movement=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));var recorder=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassiveArmy/Editor/PlanetRegionalValidation.compute"));
            try{const int n=32,cells=6*n*n,steps=600;
                using(var costs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells*5,4))using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,64))
                using(var obstacles=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16))using(var trace=new GraphicsBuffer(GraphicsBuffer.Target.Structured,steps,16))using(var nav=new PlanetPathfindingAdapter(n,2,definition.radius,costs)){
                    PlanetDevelopment.BindSurface(movement,definition);movement.SetInt("_PlanetNavRes",n);movement.SetInt("_PlanetUnitCount",1);movement.SetFloat("_PlanetDt",BattleSettings.Tick);movement.SetFloat("_PlanetSpeed",32);
                    int generate=movement.FindKernel("GenerateCosts"),move=movement.FindKernel("Move");movement.SetBuffer(generate,"_PlanetCosts",costs);movement.Dispatch(generate,(cells+127)/128,1,1);
                    nav.EnableRegional(definition);nav.SetRegionalBuildings(obstacles,1);movement.EnableKeyword("PLANET_REGIONAL_NAV");movement.EnableKeyword("PLANET_GROUP_COMMANDS");
                    movement.SetBuffer(move,"_PlanetCosts",costs);movement.SetBuffer(move,"_PlanetUnits",units);movement.SetBuffer(move,"_Groups",nav.Groups);movement.SetBuffer(move,"_PlanetField",nav.Global);nav.Regional.Bind(movement,move);
                    int record=recorder.FindKernel("Record");recorder.SetBuffer(record,"_Units",units);recorder.SetBuffer(record,"_Trace",trace);
                    long budget=nav.Regional.BufferBytes;Check(budget<1024*1024,"Two tactical patches use less than 1 MiB, independent of 120 km planet area");
                    foreach(var centre in new[]{Vector3.forward,Vector3.up,new Vector3(1,0,1).normalized,new Vector3(1,1,1).normalized}){
                        var right=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre).normalized;var up=Vector3.Cross(centre,right);
                        Vector3 At(float x,float y)=>(centre+(right*x+up*y)/definition.radius).normalized;
                        obstacles.SetData(new[]{new Vector4(centre.x,centre.y,centre.z,32)});var start=At(0,-200);var goal=At(0,200);
                        nav.SetGoal(0,goal,MovementClass.Infantry,true);nav.Regional.RelaxPending(256);
                        units.SetData(new[]{new UnitState{position=CubeSphere.Encode(start),previous=CubeSphere.Encode(start),destination=CubeSphere.Encode(goal),flags=1,health=1000,target=uint.MaxValue,group=0}});
                        using(var cmd=new CommandBuffer()){for(int i=0;i<steps;i++){cmd.DispatchCompute(movement,move,1,1,1);cmd.SetComputeIntParam(recorder,"_Step",i);cmd.DispatchCompute(recorder,record,1,1,1);}Graphics.ExecuteCommandBuffer(cmd);}
                        var path=new Vector4[steps];trace.GetData(path);float minimum=float.MaxValue,detour=0;foreach(var sample in path){var d=CubeSphere.Decode(new Vector2(sample.x,sample.y));minimum=Mathf.Min(minimum,CubeSphere.ArcDistance(centre,d,definition.radius));detour=Mathf.Max(detour,Mathf.Abs(Vector3.Dot(d-centre,right)*definition.radius));}
                        var end=CubeSphere.Decode(new Vector2(path[steps-1].x,path[steps-1].y));Check(CubeSphere.ArcDistance(end,goal,definition.radius)<20,"Tactical flow reaches a nearby order at "+centre);
                        Check(minimum>=31.75f&&detour>32,"Shared GPU integration routes around a physical 32 m obstacle at "+centre);
                        Check(nav.Regional.BufferBytes==budget,"Retargeting across the planet reuses the same patch storage at "+centre);
                    }
                    nav.SetGoal(1,Vector3.forward,MovementClass.Air,true);var picks=new uint[2];nav.Regional.Pick.GetData(picks);Check((picks[1]&65535)==64+64*128,"Air navigation retains its own traversable tactical layer");
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{UnityEngine.Object.DestroyImmediate(movement);UnityEngine.Object.DestroyImmediate(recorder);UnityEngine.Object.DestroyImmediate(definition);}
            Directory.CreateDirectory("TestResults/Planetary/LargePlanet");File.WriteAllText("TestResults/Planetary/LargePlanet/navigation.json",JsonUtility.ToJson(report,true));if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
    }
}
