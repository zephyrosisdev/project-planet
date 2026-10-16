using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    // Opt-in bounded GPU fixtures. Production never reads or mirrors unit records.
    [DefaultExecutionOrder(-220)]
    public sealed class PlanetCrowdValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="";public List<string> checks=new List<string>();}
        readonly Report report=new Report();PlanetDevelopment world;string output;bool done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-crowd-validation")>=0)new GameObject("Planet crowd regression").AddComponent<PlanetCrowdValidation>();}
        void Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i<0?Application.persistentDataPath:args[i+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();}
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks.Add(message);}
        UnitState Unit(Vector3 d,uint group=0,bool stopped=false){var p=CubeSphere.Encode(d);return new UnitState{position=p,previous=p,destination=p,flags=stopped?5u:1u,packed=0,health=1000000000,cooldown=1000000,target=uint.MaxValue,group=group};}
        Vector4[] Run(UnitState[] records,int iterations=1,bool reset=true){world.SetArmyCount(records.Length);world.UnitsForValidation.SetData(records);if(reset)world.EncounterForValidation.ResetCrowdForValidation();for(int j=0;j<iterations;j++)world.EncounterForValidation.BeforeTick();var result=new Vector4[records.Length];world.EncounterForValidation.SteeringForValidation.GetData(result);return result;}
        void LateUpdate(){if(done||world?.EncounterForValidation==null)return;done=true;world.Paused=true;
            try{
                Check(world.sessionMode==PlanetGameMode.Rts&&world.ambientZombies==0,"RTS with zero zombies still creates the GPU steering system");
                Check(world.EncounterForValidation.CrowdEnabled,"Restored crowd behaviour is enabled");
                var centre=world.Base.Offset(new Vector2(220,220));var right=Vector3.Cross(Vector3.up,centre).normalized;var forward=Vector3.Cross(centre,right);
                Vector3 At(float x,float y)=> (centre+(right*x+forward*y)/world.definition.radius).normalized;
                world.NavigationForValidation.SetGoal(0,At(0,80),MovementClass.Infantry);world.NavigationForValidation.SetGoal(1,At(0,-80),MovementClass.Infantry);world.NavigationForValidation.RelaxPending(512);
                Run(new[]{Unit(At(0,0),0,true)},8);var pressure=new float[world.NavigationForValidation.CellCount];world.EncounterForValidation.PressureForValidation.GetData(pressure);
                Check(pressure.All(p=>Mathf.Abs(p)<1e-5f),"An isolated unit generates no pressure or long-distance attraction");
                var pair=new[]{Unit(At(0,-2)),Unit(At(0,2),1)};pair[0].destination=CubeSphere.Encode(At(0,80));pair[1].destination=CubeSphere.Encode(At(0,-80));
                Run(pair);world.UnitsForValidation.GetData(pair);Check((pair[0].flags&(1024u|2048u))!=0&&(pair[1].flags&(1024u|2048u))!=0,"Opposing traffic chooses a passing side");
                uint firstSide=pair[0].flags&3072u,secondSide=pair[1].flags&3072u;Run(pair);world.UnitsForValidation.GetData(pair);Check((pair[0].flags&3072u)==firstSide&&(pair[1].flags&3072u)==secondSide,"Passing sides persist while opposing traffic remains nearby");
                var stopped=new[]{Unit(At(0,-1)),Unit(At(0,1),0,true)};var velocities=Run(stopped);
                var towards=(At(0,1)-At(0,-1)).normalized;Check(Vector3.Dot((Vector3)velocities[0],towards)<.2f,"A stopped leader brakes the follower before it drives into the queue");
                Check(((Vector3)velocities[1]).sqrMagnitude<1e-8f,"Manual Stop remains immovable under avoidance");
                foreach(var d in new[]{Vector3.forward,new Vector3(1,0,1).normalized,Vector3.up,new Vector3(1,1,1).normalized}){
                    var dense=new UnitState[4096];var axis=Vector3.Cross(Mathf.Abs(d.y)<.9f?Vector3.up:Vector3.right,d).normalized;var vertical=Vector3.Cross(d,axis);
                    for(int j=0;j<dense.Length;j++)dense[j]=Unit((d+(axis*((j%64-31.5f)*.1f)+vertical*((j/64-31.5f)*.1f))/world.definition.radius).normalized,0,true);
                    Run(dense,8);world.EncounterForValidation.PressureForValidation.GetData(pressure);var mass=new uint[pressure.Length];world.EncounterForValidation.MassForValidation.GetData(mass);var grid=new Vector4[pressure.Length];world.EncounterForValidation.CrowdForValidation.GetData(grid);
                    Check(Math.Abs(mass.Sum(v=>(long)v)-4096L*1024)<4096*2,"Predicted density conserves mass at surface/seam/pole/corner "+d);
                    Check(pressure.All(p=>!float.IsNaN(p)&&!float.IsInfinity(p)&&p>=0)&&pressure.Max()>0,"Compression creates finite nonnegative pressure at "+d);
                    var tiledPressure=(float[])pressure.Clone();world.EncounterForValidation.UseTiledCrowdForValidation(false);Run(dense,8);world.EncounterForValidation.PressureForValidation.GetData(pressure);float error=0;for(int j=0;j<pressure.Length;j++)error=Mathf.Max(error,Mathf.Abs(pressure[j]-tiledPressure[j]));world.EncounterForValidation.UseTiledCrowdForValidation(true);
                    Check(error<=Mathf.Max(1e-4f,pressure.Max()*1e-5f),"Compacted pressure tiles match the full-grid reference at "+d);
                    Check(grid.All(v=>float.IsFinite(v.y)&&float.IsFinite(v.z)&&float.IsFinite(v.w))&&grid.Any(v=>v.y*v.y+v.z*v.z+v.w*v.w>.001f),"Pressure produces a finite world-tangent relief gradient at "+d);
                    Run(new[]{Unit(-d,0,true)},32,false);world.EncounterForValidation.PressureForValidation.GetData(pressure);Check(pressure.All(p=>p>=0&&p<1e-4f),"Pressure clears after a compressed crowd leaves at "+d);
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);
        }
    }
}
