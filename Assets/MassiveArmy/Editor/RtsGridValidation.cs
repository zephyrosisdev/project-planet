using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class RtsGridValidation
    {
        [Serializable] sealed class Report{public string status,error,unity,gpu,timestamp;public string[] checks;}
        static readonly List<string> checks=new List<string>();
        static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);checks.Add(message);}
        [MenuItem("Massive Army/Validate Grid Placement and Gates")]
        public static void Run()
        {
            checks.Clear();string error="";var catalog=RtsCatalog.Load();
            try{
                GridPlacementChecks.Geometry(catalog,Check);
                foreach(int size in new[]{4096,16384}){
                    var root=new GameObject("Grid validation fixture"){hideFlags=HideFlags.DontSave};
                    try{
                        var settings=new BattleSettings{world=new WorldGenerationSettings{size=size,seed=73129},seed=73129,ambientZombies=0,baseBuilding=true,capacity=64,activeUnits=32,projectileCapacity=64,enemyAI=false};settings.ScaleWorld();
                        using(var field=new Battlefield(settings,root.transform))
                        using(var battle=new GpuBattle(settings,field,catalog.units.Select(u=>u.Gpu).ToArray()))
                        using(var world=new RtsWorld(battle,root.transform,catalog){EnemyEnabled=false}){
                            Check(field.Resolution==(size==4096?256:512),"Original navigation resolution preserved at "+size);
                            for(int rotation=0;rotation<2;rotation++){
                                var gate=GridPlacementChecks.World(world,Check,rotation);Check(gate!=null,"Gate fixture created");
                                QueryGate(field,gate,0);gate.powered=false;world.Update(.25f);QueryGate(field,gate,-1);
                                gate.team=1;gate.powered=true;world.Update(.25f);QueryGate(field,gate,1);
                                world.Sell(gate);QueryGate(field,gate,2);
                            }
                            Check(world.TryFindPlacement("wind",0,out var wind)&&world.SnapPlacement(catalog.Buildings["wind"],wind)==wind,"AI construction uses snapped foundations");
                            float before=world.Factions[0].wallet.Stock.materials;int live=world.Buildings.Count(b=>!b.removed);
                            var wall=catalog.Buildings["wall"];var a=BuildingGrid.Anchor(wall,world.BasePosition(0),field.Size);
                            Check(!world.ConstructWallChain(new[]{BuildingGrid.CellIndex(a.x,a.y,field.Size)},0,true)&&before==world.Factions[0].wallet.Stock.materials&&live==world.Buildings.Count(b=>!b.removed),"Rejected chain preserves wallet and structures");
                        }
                    }finally{UnityEngine.Object.DestroyImmediate(root);}
                }
            }catch(Exception e){error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/GridPlacement");File.WriteAllText("TestResults/GridPlacement/validation.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,timestamp=DateTime.UtcNow.ToString("O"),checks=checks.ToArray()},true));
            Debug.Log("Grid placement validation "+(error.Length==0?"PASS":"FAIL")+" · "+checks.Count+" checks");
            if(error.Length>0)throw new InvalidOperationException(error);
        }
        static void QueryGate(Battlefield field,RtsBuilding gate,int owner)
        {
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/GridPlacementValidation"));
            try{using(var results=new GraphicsBuffer(GraphicsBuffer.Target.Structured,8,4)){
                shader.SetBuffer(0,"_Results",results);shader.SetBuffer(0,"_Costs",field.Costs);shader.SetBuffer(0,"_StructureCells",field.StructureCells);
                shader.SetInt("_NavRes",field.Resolution);shader.SetInt("_StructureRes",field.StructureResolution);shader.SetFloat("_WorldSize",field.Size);
                shader.SetVector("_Gate",new Vector4(gate.position.x,gate.position.y,0,0));var across=BuildingGrid.Forward(gate.rotation);shader.SetVector("_Across",new Vector4(across.x,across.y,0,0));
                shader.Dispatch(0,1,1,1);var data=new uint[8];results.GetData(data);
                for(int team=0;team<2;team++)for(int query=0;query<4;query++)Check(data[team*4+query]==(owner==team||owner==2?1u:0u),"GPU gate query "+query+" team "+team+" access "+owner+" rotation "+gate.rotation+" world "+field.Size);
            }}finally{UnityEngine.Object.DestroyImmediate(shader);}
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch{EditorApplication.Exit(1);}}
    }
}
