using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace MassiveArmy.EditorTools {
    public static class ModularBuildingValidation {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        [MenuItem("Massive Army/Art/Validate modular buildings")]
        public static void Run(){var report=new Report();var objects=new List<UnityEngine.Object>();
            void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);report.checks.Add(name);}
            try {
                var kit=ScriptableObject.CreateInstance<ModularBuildingKit>();objects.Add(kit);kit.material=new Material(Resources.Load<Shader>("MassiveArmy/ModularStructure"));kit.glassMaterial=new Material(Resources.Load<Shader>("MassiveArmy/ModularGlass"));objects.Add(kit.material);objects.Add(kit.glassMaterial);
                Check(!ShaderUtil.ShaderHasError(kit.material.shader)&&!ShaderUtil.ShaderHasError(kit.glassMaterial.shader),"Opaque and transparent shaders compile");
                var cameraObject=new GameObject("Modular visibility validation");objects.Add(cameraObject);var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=70;camera.aspect=16f/9;camera.nearClipPlane=.05f;camera.farClipPlane=200;
                foreach(var use in new[]{ModularUse.Home,ModularUse.Office,ModularUse.Workshop,ModularUse.Warehouse}){
                    var p=ScriptableObject.CreateInstance<ModularBuildingProfile>();objects.Add(p);p.buildingId="test-"+use;p.kit=kit;p.use=use;p.gardenWalls=use==ModularUse.Home;p.windows=false;p.floors=use==ModularUse.Warehouse?1:2;p.doorWidth=use==ModularUse.Warehouse?2.4f:1.2f;
                    using(var result=ModularBuildingBaker.Bake(p)){var l=result.layout;
                        Check(l.proxy.GetIndexCount(0)==36&&l.proxy.vertexCount==24,use+": whole-building LOD2 is twelve triangles");
                        Check(l.flat.GetIndexCount(0)/3<=512&&l.exterior.GetIndexCount(0)/3<=4096,use+": exterior LOD budgets");
                        Check(l.rooms.Length==p.floors*5&&l.rooms.All(r=>r.bounds.size.x>=2.5f&&r.bounds.size.z>=3),use+": usable rooms on every floor");
                        var reached=new HashSet<int>{0};bool changed=true;while(changed){changed=false;foreach(var portal in l.portals)if(portal.b>=0){if(reached.Contains(portal.a))changed|=reached.Add(portal.b);if(reached.Contains(portal.b))changed|=reached.Add(portal.a);}}
                        Check(reached.Count==l.rooms.Length,use+": every room reachable through doors and stairs");
                        var visibility=new ModularPortalVisibility();camera.transform.position=new Vector3(0,1.5f,p.depth*.5f+5);camera.transform.rotation=Quaternion.LookRotation(Vector3.back);visibility.Evaluate(l,camera,Matrix4x4.identity,0);
                        Check(!visibility.visible.Any(v=>v),use+": closed opaque exterior hides every room");
                        var entry=l.portals.First(x=>x.b<0&&!x.window);visibility.Evaluate(l,camera,Matrix4x4.identity,1u<<entry.door);
                        Check(visibility.visible[entry.a]&&!visibility.visible[1]&&visibility.Visits<=128,use+": open entrance reveals hall; closed internal doors hide side rooms");
                        camera.transform.position=l.rooms[1].bounds.center;camera.transform.rotation=Quaternion.LookRotation(Vector3.right);visibility.Evaluate(l,camera,Matrix4x4.identity,0);
                        Check(visibility.visible[1]&&!visibility.visible[0],use+": occupied room is visible behind a closed door");
                        var colliderObject=new GameObject("Modular collision validation");objects.Add(colliderObject);var collider=colliderObject.AddComponent<MeshCollider>();collider.sharedMesh=l.collision;Physics.SyncTransforms();
                        var observer=new GameObject("Modular capsule validation");objects.Add(observer);observer.layer=2;observer.transform.position=Vector3.one*100000;var capsule=observer.AddComponent<CapsuleCollider>();capsule.height=1.8f;capsule.radius=.35f;capsule.center=Vector3.up*.9f;var movement=new MassiveArmy.Planetary.PlanetPlayerCollision(capsule);
                        var feet=new Vector3(0,.02f,p.depth*.5f+1);for(int step=0;step<30;step++){feet=movement.Move(feet,Vector3.up,new Vector3(0,-.04f,-.08f),true,out _);feet.y=Mathf.Max(.02f,feet.y);}Check(feet.z<p.depth*.5f-.8f,use+": player capsule walks through the generated opening (feet="+feet+", contacts="+string.Join(",",Physics.OverlapCapsule(feet+Vector3.up*.4f,feet+Vector3.up*1.45f,.36f).Select(c=>c.name))+")");
                        if(p.floors>1){float stairX=-p.hallWidth*.5f+1,stairZ=p.depth*.5f-p.floorHeight*1.5f-1.75f;feet=new Vector3(stairX,.02f,stairZ-.5f);for(int step=0;step<110;step++)feet=movement.Move(feet,Vector3.up,new Vector3(0,-.03f,.09f),true,out _);Check(feet.y>p.floorHeight-.2f,use+": player capsule climbs stairs and reaches the upper landing ("+feet+")");}
                        UnityEngine.Object.DestroyImmediate(observer);objects.Remove(observer);
                        Check(!collider.Raycast(new Ray(new Vector3(0,1.5f,p.depth*.5f+1),Vector3.back),out _,2),use+": exterior doorway is physically open");
                        Check(collider.Raycast(new Ray(new Vector3(p.width*.45f,2.5f,p.depth*.5f+1),Vector3.back),out _,2),use+": solid facade blocks entry");
                        if(p.floors>1){float z=p.depth*.5f-p.floorHeight*.75f-1.75f;Check(collider.Raycast(new Ray(new Vector3(-p.hallWidth*.5f+1, p.floorHeight+1,z),Vector3.down),out var hit,p.floorHeight+2)&&hit.point.y>.5f&&hit.point.y<p.floorHeight,use+": stairwell has an ascending physical ramp");}
                        UnityEngine.Object.DestroyImmediate(colliderObject);objects.Remove(colliderObject);
                        var vertices=l.collision.vertices;var indices=l.collision.triangles;bool nondegenerate=true;for(int i=0;i<indices.Length;i+=3)nondegenerate&=Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude>1e-10f;
                        Check(nondegenerate,use+": collision mesh has no degenerate triangles");
                    }
                    p.windows=true;using(var result=ModularBuildingBaker.Bake(p)){camera.transform.position=new Vector3(0,1.5f,p.depth*.5f+5);camera.transform.rotation=Quaternion.LookRotation(Vector3.back);var visibility=new ModularPortalVisibility();visibility.Evaluate(result.layout,camera,Matrix4x4.identity,0);Check(visibility.visible.Any(v=>v),use+": transparent windows expose rooms without opening doors");}
                    p.frontRoomFraction=.35f;p.rooms=new[]{new ModularRoomSettings{name="Custom room",doorOffset=1,doorWidth=1.5f}};using(var result=ModularBuildingBaker.Bake(p)){Check(result.layout.rooms[1].name=="Custom room"&&Mathf.Abs(result.layout.rooms[1].bounds.size.z-p.depth*.35f)<.001f,use+": profile changes room layout, names and door positions");}
                    p.width=2;bool rejected=false;try{using(var invalid=ModularBuildingBaker.Bake(p)){} }catch(InvalidOperationException){rejected=true;}Check(rejected,use+": unusable layout rejected before activation");
                }
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}finally{foreach(var o in objects)if(o!=null)UnityEngine.Object.DestroyImmediate(o);}
            Directory.CreateDirectory("TestResults/Planetary/ModularBuildings");File.WriteAllText("TestResults/Planetary/ModularBuildings/editor.json",JsonUtility.ToJson(report,true));Debug.Log("Modular buildings validation "+report.status+" ("+report.checks.Count+" checks)");if(report.status!="PASS")throw new InvalidOperationException(report.error);
        }
    }
}
