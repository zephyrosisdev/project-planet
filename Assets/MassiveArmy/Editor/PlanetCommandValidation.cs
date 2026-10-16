using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    public static class PlanetCommandValidation
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate GPU Selection and Commands")]
        public static void Run()
        {
            var report=new Report();void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);report.checks.Add(message);}
            var definition=ScriptableObject.CreateInstance<PlanetDefinition>();definition.terrainHeight=0;definition.oceanHeight=-1;
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDevelopment"));
            try{
                const int n=16,cells=6*n*n,count=6;
                using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,UnitState.Stride))
                using(var costs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells*5,4))
                using(var nav=new PlanetPathfindingAdapter(n,3,definition.radius,costs))
                using(var commands=new PlanetArmyCommands(definition,units,count)){
                    // Bounded test fixtures only; gameplay never reads or mirrors these records on the CPU.
                    var records=new UnitState[count];var directions=new[]{new Vector3(-.03f,0,1).normalized,new Vector3(.03f,0,1).normalized,new Vector3(0,.03f,1).normalized,Vector3.back,Vector3.forward,Vector3.forward};
                    for(int i=0;i<count;i++)records[i]=new UnitState{position=CubeSphere.Encode(directions[i]),previous=CubeSphere.Encode(directions[i]),flags=i==4?0u:1u,health=1000,packed=i==5?1u:0u};units.SetData(records);
                    var camera=new Vector3(0,0,700);var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(camera,Quaternion.LookRotation(Vector3.back),Vector3.one).inverse;
                    var matrix=Matrix4x4.Perspective(48,16f/9,.1f,3000)*view;var size=new Vector2(2560,1440);
                    Vector2 Pixel(int i){var d=directions[i];var p=matrix*new Vector4(d.x*501,d.y*501,d.z*501,1);return new Vector2((p.x/p.w*.5f+.5f)*size.x,(p.y/p.w*.5f+.5f)*size.y);}
                    void Click(int i,int operation=0){commands.Select(matrix,camera,size,Pixel(i),Pixel(i),operation,true);}
                    commands.Select(matrix,camera,size,Vector2.zero,size);Check(commands.SelectedCount==3,"Rectangle selection excludes far-side, dead and enemy tanks");
                    Click(0);units.GetData(records);Check(commands.SelectedCount==1&&(records[0].flags&2)!=0,"Click selection preserves stable GPU unit ID");
                    Click(1,1);Check(commands.SelectedCount==2,"Shift selection adds without clearing existing selection");
                    Click(0,2);units.GetData(records);Check(commands.SelectedCount==1&&(records[1].flags&2)!=0,"Control selection removes only the chosen tank");
                    commands.Select(matrix,camera,size,new Vector2(30,30),new Vector2(30,30),0,true);Check(commands.SelectedCount==0,"Empty click clears selection");
                    Check(!commands.Move(nav,Vector3.right),"An empty selection cannot overwrite a group order");
                    PlanetDevelopment.BindSurface(shader,definition);shader.SetInt("_PlanetNavRes",n);shader.SetInt("_PlanetUnitCount",count);
                    int generate=shader.FindKernel("GenerateCosts");shader.SetBuffer(generate,"_PlanetCosts",costs);shader.Dispatch(generate,(cells+127)/128,1,1);
                    var originalGoal=new Vector3(.3f,0,1).normalized;nav.SetGoal(0,originalGoal);nav.Relax(0,n*12);
                    var fieldBefore=new float[cells*3];nav.Global.GetData(fieldBefore);
                    Click(0);var newGoal=new Vector3(-.3f,0,1).normalized;Check(commands.Move(nav,newGoal),"A selected subset allocates an independent shared navigation field");
                    nav.Relax(1,3);var fieldAfter=new float[cells*3];nav.Global.GetData(fieldAfter);
                    for(int i=0;i<cells;i++)if(fieldAfter[i]!=fieldBefore[i])throw new InvalidOperationException("Relaxation overwrote another group's field");
                    Check(true,"Odd requested relaxation count preserves all other group slices");nav.Relax(1,n*12);
                    shader.EnableKeyword("PLANET_GROUP_COMMANDS");int move=shader.FindKernel("Move");shader.SetBuffer(move,"_PlanetUnits",units);shader.SetBuffer(move,"_PlanetCosts",costs);shader.SetBuffer(move,"_PlanetField",nav.Global);shader.SetBuffer(move,"_Groups",nav.Groups);shader.SetFloat("_PlanetSpeed",16);shader.SetFloat("_PlanetDt",BattleSettings.Tick);
                    for(int i=0;i<10;i++)shader.Dispatch(move,1,1,1);units.GetData(records);
                    Check(records[0].group==1&&records[1].group==0&&(records[0].destination-CubeSphere.Encode(newGoal)).sqrMagnitude<1e-10&&(records[1].destination-CubeSphere.Encode(originalGoal)).sqrMagnitude<1e-10,"GPU movement uses each group's destination; unselected tanks retain prior orders");
                    // Stop and resume without replacing the record or its stable slot.
                    commands.Stop();units.GetData(records);var stopped=records[0].position;
                    for(int i=0;i<10;i++)shader.Dispatch(move,1,1,1);units.GetData(records);
                    Check(records[0].position==stopped&&records[0].velocity==Vector2.zero,"Stop persists across simulation ticks and clears velocity");
                    Check(commands.Move(nav,newGoal),"Stopped selection resumes using an available field");nav.Relax(1,n*12);shader.SetBuffer(move,"_PlanetField",nav.Global);shader.Dispatch(move,1,1,1);units.GetData(records);Check((records[0].flags&4)==0&&records[0].position!=stopped,"A new move order releases the persistent stop flag");
                    // Use original fixture positions again to exercise the group capacity rule deterministically.
                    for(int i=0;i<count;i++){records[i].position=CubeSphere.Encode(directions[i]);records[i].flags=i==4?0u:1u;records[i].group=i==0?1u:i==1?2u:0u;}units.SetData(records);
                    nav.SetGoal(2,Vector3.up);Click(2);Check(!commands.Move(nav,newGoal),"Exhausted group slots reject an order without stealing an unselected field");
                    Click(0);Check(commands.Move(nav,Vector3.up),"A fully selected existing group can be retargeted when all slots are occupied");
                    units.GetData(records);Check(records[0].group==1&&records[1].group==2&&records[2].group==0&&records[0].health==1000,"Group reuse preserves other memberships and stable unit health");
                    var offset=new Vector3(10000,-2000,3000);var rebased=matrix*Matrix4x4.Translate(-offset)*Matrix4x4.Translate(offset);
                    commands.Select(rebased,camera,size,Vector2.zero,size);Check(commands.SelectedCount==3,"Planet-local selection is invariant under a render-origin translation");
                    foreach(var pole in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back,new Vector3(1,1,1).normalized,new Vector3(-1,1,1).normalized,new Vector3(1,-1,1).normalized,new Vector3(1,1,-1).normalized,new Vector3(-1,-1,1).normalized,new Vector3(-1,1,-1).normalized,new Vector3(1,-1,-1).normalized,new Vector3(-1,-1,-1).normalized}){
                        var rotation=Quaternion.FromToRotation(Vector3.forward,pole);for(int i=0;i<count;i++){records[i].position=CubeSphere.Encode(rotation*directions[i]);records[i].flags=i==4?0u:1u;}units.SetData(records);
                        var eye=pole*700;var localView=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye,Quaternion.LookRotation(-pole,rotation*Vector3.up),Vector3.one).inverse;
                        commands.Select(Matrix4x4.Perspective(48,16f/9,.1f,3000)*localView,eye,size,Vector2.zero,size);
                        if(commands.SelectedCount!=3)throw new InvalidOperationException("Selection failed pole / corner "+pole);
                    }
                    Check(true,"Visible-side selection remains correct on all six face centres and eight cube corners");
                    using(var types=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,64))
                    using(var traits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,UnitTraits.Stride))
                    using(var mixedNav=new PlanetPathfindingAdapter(n,4,definition.radius,costs))
                    using(var mixedCommands=new PlanetArmyCommands(definition,units,count)){
                        types.SetData(new[]{new UnitArchetype{movement=0},new UnitArchetype{movement=2},new UnitArchetype{movement=3}});traits.SetData(new[]{new UnitTraits{scale=new Vector4(1,1,1,0)},new UnitTraits{scale=new Vector4(1,1,1,2)},new UnitTraits{scale=new Vector4(1,1,1,3)}});mixedCommands.BindCatalog(types,traits);
                        for(int i=0;i<count;i++){records[i].position=CubeSphere.Encode(directions[i]);records[i].flags=i==4?0u:1u;records[i].group=0;records[i].packed=i<3?(uint)i<<8:i==5?1u:0u;}units.SetData(records);
                        mixedNav.SetGoal(0,originalGoal);mixedCommands.Select(matrix,camera,size,Vector2.zero,size);Check(mixedCommands.SelectedCount==3,"Mixed current roster selection remains GPU authoritative");
                        Check(mixedCommands.Move(mixedNav,newGoal),"A mixed infantry / armour / air order allocates one shared field per movement class");units.GetData(records);var orders=new GroupOrder[4];mixedNav.Groups.GetData(orders);
                        Check(records[0].group!=records[1].group&&records[1].group!=records[2].group&&records[0].group!=records[2].group&&orders[records[0].group].movement==0&&orders[records[1].group].movement==2&&orders[records[2].group].movement==3,"Mixed command assignment preserves each current unit's traversal class");
                        var beforeGroups=new[]{records[0].group,records[1].group,records[2].group};Check(!mixedCommands.Move(mixedNav,Vector3.back,m=>m==3),"Incompatible mixed terrain rejects the complete order transaction");units.GetData(records);Check(records[0].group==beforeGroups[0]&&records[1].group==beforeGroups[1]&&records[2].group==beforeGroups[2],"Rejected mixed order leaves every group unchanged");
                    }
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{UnityEngine.Object.DestroyImmediate(shader);UnityEngine.Object.DestroyImmediate(definition);}
            Directory.CreateDirectory("TestResults/Planetary/Commands");File.WriteAllText("TestResults/Planetary/Commands/validation.json",JsonUtility.ToJson(report,true));
            if(report.status!="PASS")throw new InvalidOperationException(report.error);Debug.Log("Planet command validation PASS");
        }
    }
}
