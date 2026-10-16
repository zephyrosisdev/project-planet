using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy.EditorTools
{
    public static class RtsGridMovementValidation
    {
        static IEnumerator routine;
        static string error="";
        static readonly List<string> checks=new List<string>();
        static GameObject root;
        static Battlefield field;
        static GpuBattle battle;
        static RtsWorld world;
        static ComputeShader fixture;
        static double deadline;
        [Serializable]sealed class Report{public string status,error,timestamp;public string[] checks;}
        [MenuItem("Massive Army/Validate GPU Movement Through Owned Gates")]
        public static void Start(){checks.Clear();error="";routine=Run();deadline=EditorApplication.timeSinceStartup+180;EditorApplication.update+=Advance;}
        public static void Batch(){Start();EditorApplication.update+=Exit;}
        static void Exit(){if(routine==null){EditorApplication.update-=Exit;EditorApplication.Exit(error.Length==0?0:1);}}
        static void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);checks.Add(text);File.WriteAllLines("TestResults/GridPlacement/movement-progress.txt",checks);}
        static void Advance(){try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Gate movement validation timed out");if(routine.MoveNext())return;}catch(Exception e){error=e.ToString();Debug.LogException(e);}Finish();}
        static void Cleanup(){world?.Dispose();battle?.Dispose();field?.Dispose();world=null;battle=null;field=null;if(root!=null)UnityEngine.Object.DestroyImmediate(root);root=null;if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);fixture=null;}
        static void Finish(){EditorApplication.update-=Advance;routine=null;Cleanup();Directory.CreateDirectory("TestResults/GridPlacement");File.WriteAllText("TestResults/GridPlacement/movement.json",JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,timestamp=DateTime.UtcNow.ToString("O"),checks=checks.ToArray()},true));}
        static IEnumerator Run(){
            Directory.CreateDirectory("TestResults/GridPlacement");var catalog=RtsCatalog.Load();uint[] draws={3,3,6,6};
            foreach(int size in new[]{4096,16384}){
                root=new GameObject("GPU gate movement fixture"){hideFlags=HideFlags.DontSave};var camera=new GameObject("Fixture camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.transform.position=new Vector3(0,1700,-1000);camera.transform.LookAt(Vector3.zero);
                var settings=new BattleSettings{world=new WorldGenerationSettings{size=size,seed=73129},seed=73129,ambientZombies=0,baseBuilding=true,capacity=64,activeUnits=32,projectileCapacity=64,enemyAI=false};settings.ScaleWorld();
                field=new Battlefield(settings,root.transform);battle=new GpuBattle(settings,field,catalog.units.Select(u=>u.Gpu).ToArray());world=new RtsWorld(battle,root.transform,catalog){EnemyEnabled=false};
                fixture=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/GridPlacementValidation"));
                // Complete the normal initial GPU spawn commands before replacing a bounded test sample.
                while(battle.ProducedUnits<8){world.Update(.25f);battle.AdvanceRtsValidation();battle.Update(camera,BattleSettings.Tick,draws,false);yield return null;}
                for(int rotation=0;rotation<2;rotation++){
                    var gate=GridPlacementChecks.World(world,Check,rotation);Vector2 across=BuildingGrid.Forward(rotation);
                    int kernel=fixture.FindKernel("SeedProbes");fixture.SetBuffer(kernel,"_ProbeUnits",battle.Units);fixture.SetInt("_ProbeType",catalog.TypeIndex("engineer"));fixture.SetVector("_Gate",new Vector4(gate.position.x,gate.position.y,0,0));fixture.SetVector("_Across",new Vector4(across.x,across.y,0,0));fixture.Dispatch(kernel,1,1,1);
                    using(var commands=new CommandBuffer()){
                        battle.Navigation.UploadFormation(commands,0,new FormationLayout(gate.position+across*16,Vector2.right,8),0,ArmyOrder.Move,0);
                        battle.Navigation.UploadFormation(commands,1,new FormationLayout(gate.position-across*16,Vector2.right,8),1,ArmyOrder.Move,0);
                        battle.Navigation.Reset(commands,0);battle.Navigation.Reset(commands,1);
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    float owner=-16,enemy=16;bool enemyEntered=false;
                    for(int i=0;i<600&&owner<=1;i++){
                        world.Update(BattleSettings.Tick);battle.AdvanceRtsValidation();battle.Update(camera,BattleSettings.Tick,draws,false);yield return null;
                        if(i%10!=9)continue;UnitState[] sample=null;battle.ReadUnitSample(0,2,data=>sample=data);while(sample==null)yield return null;
                        owner=Vector2.Dot(field.Delta(gate.position,sample[0].position),across);enemy=Vector2.Dot(field.Delta(gate.position,sample[1].position),across);
                        enemyEntered|=BuildingGrid.Contains(gate.definition,gate.position,sample[1].position,field.Size,gate.rotation);
                    }
                    Check(owner>1,"Owner crosses gate with existing GPU flow and movement · rotation "+rotation+" world "+size+" progress "+owner+" start cost "+world.NavigationCost(gate.position-across*16,0));
                    Check(!enemyEntered,"Enemy never enters owned gate · rotation "+rotation+" world "+size+" progress "+enemy);
                    world.Sell(gate);
                }
                Cleanup();
            }
        }
    }
}
