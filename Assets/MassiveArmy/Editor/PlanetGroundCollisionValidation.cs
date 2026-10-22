using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools {
    public static class PlanetGroundCollisionValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Planetary/Validate walking floors and ramps")]
        public static void Run(){var report=new Report();var owned=new List<UnityEngine.Object>();
            void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{var avatar=new GameObject("Collision validation observer");owned.Add(avatar);avatar.layer=2;var capsule=avatar.AddComponent<CapsuleCollider>();capsule.radius=PlanetPlayerCollision.Radius;capsule.height=PlanetPlayerCollision.Height;capsule.center=Vector3.up*.9f;var solver=new PlanetPlayerCollision(capsule);
                var floor=new GameObject("Collision validation surface");owned.Add(floor);var collider=floor.AddComponent<MeshCollider>();
                foreach(float tilt in new[]{0f,12f,24f,32f})foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(33,71,14),Quaternion.Euler(150,-27,88)}){
                    float rise=Mathf.Tan(tilt*Mathf.Deg2Rad)*12;var geometry=new ModularGeometry();geometry.Quad(new Vector3(-5,0,0),new Vector3(-5,rise,12),new Vector3(5,rise,12),new Vector3(5,0,0),Color.white);var mesh=geometry.Build("Walkable ramp");owned.Add(mesh);collider.sharedMesh=mesh;floor.transform.SetPositionAndRotation(new Vector3(1000,2000,-3000),rotation);Physics.SyncTransforms();var up=rotation*Vector3.up;var feet=floor.transform.TransformPoint(new Vector3(0,Mathf.Tan(tilt*Mathf.Deg2Rad)*2+.2f,2));
                    bool grounded=false;float velocity=0;for(int step=0;step<120;step++){velocity-=9.81f/60;feet=solver.Move(feet,up,up*(velocity/60),true,out var support);grounded=support!=null;if(grounded)velocity=0;}
                    Check(grounded,"Observer settles on "+tilt+" degree ramp with rotated planetary up");var hold=feet;
                    for(int step=0;step<600;step++){feet=solver.Move(feet,up,-up*(9.81f/3600),true,out var support);Check(support!=null,"Idle ramp contact remains supported");}
                    Check(Vector3.Distance(feet,hold)<.025f,"Standing still does not slide down the ramp");
                    for(int step=0;step<60;step++)feet=solver.Move(feet,up,(rotation*Vector3.forward*4-up*(9.81f/60))/60,true,out _);
                    var walked=floor.transform.InverseTransformPoint(feet);Check(walked.z>4.5f&&walked.y>=Mathf.Tan(tilt*Mathf.Deg2Rad)*walked.z-.04f,"Observer walks uphill without passing through the ramp");
                    var stopped=feet;for(int step=0;step<300;step++)feet=solver.Move(feet,up,-up*(9.81f/3600),true,out _);Check(Vector3.Distance(feet,stopped)<.025f,"Stopping after walking uphill remains stable");
                }
                foreach(var entry in RtsModelArt.Load().buildings){var layout=entry.modular;if(layout==null)continue;collider.sharedMesh=layout.collision;floor.transform.SetPositionAndRotation(new Vector3(1000,2000,-3000),Quaternion.Euler(23,78,41));Physics.SyncTransforms();var up=floor.transform.up;
                    foreach(var room in layout.rooms){var local=room.bounds.center;local.y=room.bounds.min.y+.3f;var point=floor.transform.TransformPoint(local);Check(Physics.Raycast(point,-up,out var hit,.8f)&&hit.collider==collider&&Vector3.Dot(hit.normal,up)>.55f,entry.id+" / "+room.name+" has a supporting interior floor");}
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);}
            Directory.CreateDirectory("TestResults/Planetary/Crime/Collision");File.WriteAllText("TestResults/Planetary/Crime/Collision/editor.json",JsonUtility.ToJson(report,true));Debug.Log("Walking collision validation "+report.status);if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
    }
}
