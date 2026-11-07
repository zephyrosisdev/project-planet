using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MassiveArmy.Planetary;
namespace MassiveArmy.EditorTools {
    public static class PlanetEconomyDiagnostics {
        // Revisit the user-reported black frame after recreating shader bindings.
        public static void RestoreBlackView(){
            var world=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();if(world==null||!world.Ready)throw new InvalidOperationException("Start a planetary match first.");
            world.SetFirstPerson(false);world.Rig.SetFocusDirection(new Vector3(.05813847f,.9090089f,-.4127018f));world.Rig.Distance=104170.5f;world.Rig.Pitch=55;
            var heading=Quaternion.Euler(32.807f,-29.199f,15.22f)*Quaternion.Euler(-55,0,0)*Vector3.forward;
            typeof(PlanetRtsCamera).GetField("tangent",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(world.Rig,heading);world.Rig.RefreshPose();
            double after=EditorApplication.timeSinceStartup+1;
            void CaptureView(){if(EditorApplication.timeSinceStartup<after)return;EditorApplication.update-=CaptureView;if(world==null||!world.Ready)return;
                const string folder="TestResults/Planetary/BlackView";Directory.CreateDirectory(folder);
                var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGBFloat);target.Create();var previous=RenderTexture.active;Texture2D image=null;
                try{UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(world.View,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBAFloat,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();int invalid=0,lit=0;float maximum=0;
                    foreach(var c in image.GetPixels()){if(float.IsNaN(c.r)||float.IsInfinity(c.r)||float.IsNaN(c.g)||float.IsInfinity(c.g)||float.IsNaN(c.b)||float.IsInfinity(c.b)){invalid++;continue;}float brightness=Mathf.Max(c.r,Mathf.Max(c.g,c.b));maximum=Mathf.Max(maximum,brightness);if(brightness>.02f)lit++;}
                    var png=new Texture2D(1280,720,TextureFormat.RGB24,false);png.SetPixels(image.GetPixels());png.Apply();File.WriteAllBytes(folder+"/after.png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
                    File.WriteAllText(folder+"/check.txt",$"{(invalid==0&&lit>1000?"PASS":"FAIL")} · original camera restored\ninvalid pixels={invalid}; visible pixels={lit}/921600; maximum brightness={maximum}\nfocus={world.Rig.FocusDirection}; distance={world.Rig.Distance}; seed={world.definition.terrainSeed}");
                }finally{RenderTexture.active=previous;if(image!=null)UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);}
            }
            EditorApplication.update+=CaptureView;
        }
        public static void TestTakeoff(){
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var world=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();
            if(world==null||!world.Ready)throw new InvalidOperationException("Start a planetary match before validating takeoff.");
            var ship=(PlanetSpacecraft)typeof(PlanetDevelopment).GetField("spacecraft",flags).GetValue(world);
            var original=ship.Pose;var checks=new System.Collections.Generic.List<string>();GameObject blocker=null;
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message+"; "+ship.LandingStatus);checks.Add(message);}
            void Park(SpacePose pose){typeof(PlanetSpacecraft).GetField("pose",flags).SetValue(ship,pose);typeof(PlanetSpacecraft).GetField("<Landed>k__BackingField",flags).SetValue(ship,true);ship.Rebase();Physics.SyncTransforms();}
            void Finish(){for(int i=0;i<240&&ship.AutoFlightActive;i++){ship.Integrate(1f/60,Vector3.zero,Vector3.zero);ship.Rebase();}Check(!ship.Landed&&!ship.AutoFlightActive,"Takeoff finishes airborne");}
            world.SetShipControl(true);world.Paused=false;
            try{
                Park(original);blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="Takeoff overhead regression fixture";blocker.transform.SetPositionAndRotation(world.RenderBodyPosition(original.position+new Double3(ship.Direction)*40),original.rotation);blocker.transform.localScale=new Vector3(15,1,15);Physics.SyncTransforms();
                Check(!ship.TryLandingOrTakeoff()&&ship.Landed,"Overhead obstacle rejects takeoff and preserves ground lock");UnityEngine.Object.DestroyImmediate(blocker);blocker=null;
                Check(ship.TryLandingOrTakeoff(),"Takeoff can be retried after overhead obstruction is removed");Finish();Check(Math.Abs((ship.Pose.position-original.position).Magnitude-80)<.01,"Normal launch reaches 80 metres");
                var contact=original;contact.position-=new Double3(ship.Direction)*2;Park(contact);
                Check(ship.TryLandingOrTakeoff(),"Takeoff starts with the hull touching raised terrain");Finish();Check(Math.Abs((ship.Pose.position-contact.position).Magnitude-80)<.01,"Terrain contact clears smoothly through the full 80 metre launch");
                Park(original);Check(ship.TryLandingOrTakeoff(),"Dynamic obstruction fixture starts takeoff");blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.SetPositionAndRotation(world.RenderBodyPosition(original.position),original.rotation);blocker.transform.localScale=new Vector3(10,10,10);Physics.SyncTransforms();ship.Integrate(1f/60,Vector3.zero,Vector3.zero);
                Check(!ship.AutoFlightActive&&ship.Landed,"A blocked first lift-off frame remains landed for retry");UnityEngine.Object.DestroyImmediate(blocker);blocker=null;
                Check(ship.TryLandingOrTakeoff(),"Space action can retry an interrupted first lift-off frame");Finish();
                Park(original);var keyboard=UnityEngine.InputSystem.Keyboard.current;
                try{
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Space));UnityEngine.InputSystem.InputSystem.Update();ship.Step(1f/60);
                    Check(ship.AutoFlightActive,"Space key reaches the actual pilot input path");Finish();
                }finally{UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();}
                File.WriteAllText("TestResults/Planetary/takeoff-test.txt","PASS\n"+string.Join("\n",checks));
            }finally{if(blocker!=null)UnityEngine.Object.DestroyImmediate(blocker);world.Paused=false;CaptureShip();}
        }
        public static void CaptureShip(){
            var world=UnityEngine.Object.FindObjectsByType<PlanetDevelopment>(FindObjectsSortMode.None).FirstOrDefault(w=>w.IsPiloting)??UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();
            if(world==null||!world.Ready)throw new InvalidOperationException("Start a planetary match before inspecting the ship.");
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var ship=(PlanetSpacecraft)typeof(PlanetDevelopment).GetField("spacecraft",flags).GetValue(world);
            var pose=ship.Pose;var centre=world.RenderBodyPosition(pose.position+Double3.Rotate(pose.rotation,new Double3(0,-2,-7)));
            var hits=Physics.BoxCastAll(centre,new Vector3(27,5.5f,48),ship.Direction,pose.rotation,80,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            var overlaps=Physics.OverlapBox(centre,new Vector3(27,5.5f,48),pose.rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            File.WriteAllText("TestResults/Planetary/ship-live.txt",$"piloting={world.IsPiloting} paused={world.Paused} active={typeof(PlanetSpacecraft).GetField("active",flags).GetValue(ship)} landed={ship.Landed} auto={ship.AutoFlightActive} status={ship.LandingStatus}\n"+string.Join("\n",hits.Select(h=>$"hit {h.collider.name} parent={h.collider.transform.parent?.name} owns={ship.Owns(h.collider)} distance={h.distance} point={h.point}"))+"\nOverlaps:\n"+string.Join("\n",overlaps.Select(c=>$"{c.name} parent={c.transform.parent?.name} owns={ship.Owns(c)}")));
        }
        [Serializable]sealed class Spending {public double seconds,materials;public int transactions;public string stack;}
        [Serializable]sealed class Audit {public double started,ended;public float before,after;public System.Collections.Generic.List<Spending> spending=new System.Collections.Generic.List<Spending>();}
        static Audit audit;static ResourceWallet auditedWallet;static double auditUntil;
        [MenuItem("Massive Army/Diagnostics/Trace Material Spending (10 seconds)")]
        public static void Trace(){
            var world=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();if(world?.Base==null)return;
            StopTrace();auditedWallet=world.Base.Faction.wallet;audit=new Audit{started=EditorApplication.timeSinceStartup,before=auditedWallet.Stock.materials};auditUntil=audit.started+10;
            ResourceWallet.SpendObserver+=OnSpend;EditorApplication.update+=FinishTrace;
        }
        static void OnSpend(ResourceWallet wallet,ResourceCost cost){
            if(!ReferenceEquals(wallet,auditedWallet)||cost.materials<=0)return;
            string stack=new System.Diagnostics.StackTrace(2,true).ToString();var entry=audit.spending.Find(s=>s.stack==stack);
            if(entry==null){entry=new Spending{stack=stack};audit.spending.Add(entry);}entry.materials+=cost.materials;entry.transactions++;
        }
        static void StopTrace(){ResourceWallet.SpendObserver-=OnSpend;EditorApplication.update-=FinishTrace;}
        static void FinishTrace(){if(EditorApplication.timeSinceStartup<auditUntil)return;StopTrace();audit.ended=EditorApplication.timeSinceStartup;audit.after=auditedWallet.Stock.materials;foreach(var entry in audit.spending)entry.seconds=audit.ended-audit.started;
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/material-spending.json",JsonUtility.ToJson(audit,true));auditedWallet=null;audit=null;}
        public static void ValidateSurveys(){
            var local=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>()?.Base;if(local?.Natural==null)throw new InvalidOperationException("Start a planetary match before validating surveys");
            var method=typeof(PlanetProceduralResources).GetMethod("Survey",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);int checks=0;
            foreach(int team in new[]{-1,0,1,2,3,7,8}){method.Invoke(local.Natural,new object[]{local.Catalog.Buildings["mine"],team,new FrontierSurvey{team=team,resource="mine"}});checks++;}
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/survey-validation.txt","PASS: "+checks+" survey calls, including third/fourth countries and invalid indexes; countries="+local.Factions.Length);
        }
        [Serializable]sealed class Snapshot {public string mode;public int seed;public double clock;public float speed;public uint tick;public Building[] buildings;public ColonyState[] colonies;}
        [Serializable]sealed class Building {public long id;public int team,node,builders,workers;public string type;public bool removed,complete,pending,roadSuspended;public float progress,food,efficiency;}
        public static void Capture(){var world=UnityEngine.Object.FindAnyObjectByType<PlanetDevelopment>();if(world?.Base==null)return;var local=world.Base;
            var snapshot=new Snapshot{mode=world.sessionMode.ToString(),seed=world.definition.terrainSeed,clock=local.Civilisation?.SimulationTime??0,speed=world.SimulationSpeed,tick=world.SimulationTicks,colonies=local.Settlements,buildings=local.Buildings.Select(b=>new Building{id=b.persistentId,team=b.team,node=b.node,type=b.definition.id,removed=b.removed,complete=b.Complete,pending=b.awaitingProjectMaterials,roadSuspended=b.roadSuspended,progress=b.construction,food=b.FoodEfficiency,efficiency=b.Efficiency,builders=b.constructionWorkers,workers=b.WorkersAssigned}).ToArray()};
            Directory.CreateDirectory("TestResults/Planetary");File.WriteAllText("TestResults/Planetary/live-economy.json",JsonUtility.ToJson(snapshot,true));
        }
    }
}
