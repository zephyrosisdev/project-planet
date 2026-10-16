using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    // Opt-in player fixture; bounded building-pose readbacks, never a CPU army mirror.
    public sealed class BuildingPresentationValidation : MonoBehaviour
    {
        [Serializable] sealed class Report{public string status,error;public string[] checks;public float targetAngle;}
        readonly List<string> checks=new List<string>();string error="";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-building-presentation-validation")>=0)new GameObject("Building presentation fixture").AddComponent<BuildingPresentationValidation>();}
        void Check(bool valid,string label){if(!valid)throw new InvalidOperationException(label);checks.Add(label);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.Contains("Skipping dispatch"))error=message;}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;var app=FindAnyObjectByType<BattleApp>();app.settings.ambientZombies=0;app.settings.enemyAI=false;app.settings.shadows=true;app.settings.world.size=4096;app.settings.world.seed=app.settings.seed=73129;app.StartFlatValidationBattle(50000);
            double deadline=Time.realtimeSinceStartupAsDouble+120;while(app.State=="loading"&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            RtsBuilding turret=null;Quaternion placement=Quaternion.identity;var aim=new Vector4[1];
            try{
                Check(app.State=="battle","Base match loads");var d=app.Base.Catalog.Buildings["autocannon"];var faction=app.Base.Factions[0];faction.tier=4;if(!string.IsNullOrEmpty(d.requiredTech))faction.technologies.Add(d.requiredTech);faction.wallet.Stock=faction.wallet.Capacity;
                Vector2 home=app.Base.BasePosition(0);
                for(int ring=4;ring<=12&&turret==null;ring++)for(int side=0;side<8&&turret==null;side++){
                    float angle=side*Mathf.PI*.25f;var point=home+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*ring*8;
                    if(app.Base.ValidatePlacement(d,0,point,true,rotation:1).valid)turret=app.Base.Construct(d.id,0,point,true,rotation:1);
                }
                Check(turret!=null,"A rotated turret is constructed through the actual placement API");turret.construction=d.time;turret.health=turret.MaxHealth;placement=turret.visual.transform.rotation;
                var renderer=turret.visual.GetComponentsInChildren<MeshRenderer>().First();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Check(block.GetFloat("_HasTurret")==1&&Mathf.Abs(block.GetFloat("_BuildingYaw")-Mathf.PI*.5f)<.001f,"Presentation binds the weapon joint and placed yaw");
                var joints=renderer.GetComponent<MeshFilter>().sharedMesh.uv2;Check(joints.Any(j=>j.x==0)&&joints.Any(j=>j.x==1),"Runtime catalogue mesh retains fixed and moving shells");
                Vector2 prey=app.Battle.Field.TraversableDestination(turret.position+new Vector2(45,0));
                Check(app.Battle.RequestSpawn(app.Base.Catalog.TypeIndex("heavy_tank"),1,prey,prey,null),"Enemy target submitted through GPU spawn API");app.View.GetComponent<RtsCamera>().FocusBase(turret.position);
            }catch(Exception e){error=e.ToString();}
            deadline=Time.realtimeSinceStartupAsDouble+20;
            while(error.Length==0&&aim[0].w<.5f&&Time.realtimeSinceStartupAsDouble<deadline){yield return new WaitForSecondsRealtime(.25f);app.Battle.StructureAimBuffer.GetData(aim,0,turret.index,1);}
            if(error.Length==0)try{Check(aim[0].w==1,"Live building weapon publishes its firing target pose");Check(turret.visual.transform.rotation==placement&&turret.rotation==1,"Firing leaves the placed building transform and grid rotation unchanged");Check(Vector2.Distance(turret.position,new Vector2(aim[0].y,aim[0].z))>5,"Pose points to a target outside the foundation");}catch(Exception e){error=e.ToString();}
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-benchmark-output");string folder=at>=0?args[at+1]:Application.persistentDataPath;Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"player-validation.json"),JsonUtility.ToJson(new Report{status=error.Length==0?"PASS":"FAIL",error=error,checks=checks.ToArray(),targetAngle=aim[0].x},true));Application.logMessageReceived-=Log;Application.Quit(error.Length==0?0:1);
        }
    }
}
