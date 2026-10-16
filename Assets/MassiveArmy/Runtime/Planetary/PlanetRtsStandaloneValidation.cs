using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace MassiveArmy.Planetary
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlanetRtsStandaloneValidation : MonoBehaviour
    {
        [Serializable]sealed class Report{public string status="PASS",error="",gpu;public int width=2560,height=1440,unitsBefore,unitsAfter;public List<string> checks=new List<string>();}
        PlanetDevelopment fixture;RenderTexture worldTarget,hudTarget,composite;Material material;RenderPipeline.StandardRequest request;string output;int frame;double deadline;Report report=new Report();uint beforeFps;RtsBuilding producer;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-rts-validation")>=0)new GameObject("Planet RTS validation").AddComponent<PlanetRtsStandaloneValidation>();}
        void Start(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-planet-output");output=i>=0?args[i+1]:Application.persistentDataPath;Directory.CreateDirectory(output);fixture=FindAnyObjectByType<PlanetDevelopment>();deadline=Time.realtimeSinceStartupAsDouble+90;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;report.gpu=SystemInfo.graphicsDeviceName;}
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);report.checks.Add(message);}
        void Click(string name){var button=fixture.Hud.Root.Q<Button>(name);Check(button!=null&&button.enabledInHierarchy,"Existing HUD action available: "+name);if(button.userData is BuildingDefinition definition)fixture.Base.BeginPlacement(definition);else ((Action)button.userData)();}
        Vector3 Site(BuildingDefinition d){for(int y=-180;y<=180;y+=20)for(int x=-180;x<=180;x+=20){var direction=fixture.Base.Offset(new Vector2(x,y));if(fixture.Base.Validate(d,direction,0).valid)return direction;}throw new InvalidOperationException("No valid test construction site for "+d.id);}
        void LateUpdate()
        {
            try{
                if(Time.realtimeSinceStartupAsDouble>deadline)throw new InvalidOperationException("Planet RTS validation timed out waiting for scene / HUD");
                if(fixture?.Base==null||fixture.Hud?.Root==null)return;
                if(worldTarget==null){worldTarget=Target();hudTarget=Target();composite=Target();fixture.Hud.SetRenderTarget(hudTarget);fixture.View.enabled=false;fixture.View.targetTexture=worldTarget;fixture.Rig.InputEnabled=false;request=new RenderPipeline.StandardRequest{destination=worldTarget};material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUiComposite"));material.SetTexture("_Ui",hudTarget);report.unitsBefore=fixture.unitCount;}
                RenderPipeline.SubmitRenderRequest(fixture.View,request);var fence=AsyncGPUReadback.Request(worldTarget,0,0,1,0,1,0,1);fence.WaitForCompletion();if(fence.hasError)throw new InvalidOperationException("RTS render fence failed");frame++;
                if(frame==28)fixture.Hud.ShowBuildingCategory(BuildingCategory.Power);
                if(frame==58)fixture.Hud.ShowBuildingCategory(BuildingCategory.Research);
                if(frame==30){
                    Check(fixture.Base.Catalog.units.Length==31,"Current 31-type RTS catalogue is loaded");Check(fixture.Base.Buildings.Count==4,"Current command, barracks, depot and solar meshes seeded on the planet");
                    foreach(var b in fixture.Base.Buildings){var radial=CubeSphere.Decode(b.position);if(Vector3.Dot(b.visual.transform.up,radial)<.99999f)throw new InvalidOperationException("Building is not aligned to radial up");}Check(true,"All starter buildings align to radial gravity");
                    Capture("base-and-current-hud");Click("build-solar");var site=Site(fixture.Base.Catalog.Buildings["solar"]);Check(fixture.Base.Construct(site),"Construction action spends resources and places a valid footprint");fixture.Base.Tick(30);Check(fixture.Base.Selected.Complete,"Placed building completes construction");
                    producer=fixture.Base.Buildings.First(b=>b.definition.id=="barracks");var pixel=fixture.View.WorldToScreenPoint(producer.visual.transform.position);Check(fixture.Base.Pick(pixel),"Radial building selection updates the HUD context");
                }
                if(frame==45){Click("train-infantry");Check(producer.queue.Count==1,"Current infantry training enters the production queue");Capture("production-queue");fixture.Base.Tick(60);Check(fixture.unitCount==report.unitsBefore+1,"Production appends a GPU unit without a unit GameObject");
                    var record=new UnitState[1];fixture.UnitsForValidation.GetData(record,0,fixture.unitCount-1,1);Check((record[0].packed>>8&255)==(uint)fixture.Base.Catalog.TypeIndex("infantry")&&record[0].health==(uint)fixture.Base.Catalog.Units["infantry"].health,"Spawned GPU record uses current catalogue type and health");
                    Click("train-engineer");Check(producer.queue.Count==1,"A second current unit type can be queued");}
                if(frame==60){Click("cancel-queue-0");Check(producer.queue.Count==0,"HUD cancellation removes the bound queue order");Click("build-lab");var site=Site(fixture.Base.Catalog.Buildings["lab"]);Check(fixture.Base.Construct(site),"Current research lab can be constructed");fixture.Base.Tick(60);fixture.Base.Faction.wallet.Credit(new ResourceCost{research=500,materials=1000,fuel=500,rareElements=500});}
                if(frame==75){Click("research-mechanized");Check(fixture.Base.Selected.queue.Any(q=>q.research),"Technology enters the current building's research queue");fixture.Base.Tick(120);Check(fixture.Base.Faction.HasTech("mechanized"),"Research completion unlocks current catalogue technology");Capture("research-and-construction");
                    fixture.Rig.Distance=1700;}
                if(frame==90){Capture("orbit");beforeFps=fixture.SimulationTicks;fixture.SetFirstPerson(true);}
                if(frame==120){Capture("first-person");Check(fixture.SimulationTicks>beforeFps,"GPU army and base simulation continue during FPS control");fixture.SetFirstPerson(false);report.unitsAfter=fixture.unitCount;Finish();}
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);Finish();}
        }
        RenderTexture Target(){var texture=new RenderTexture(2560,1440,24,RenderTextureFormat.ARGB32);texture.Create();return texture;}
        void Capture(string name){Graphics.Blit(worldTarget,composite,material);var previous=RenderTexture.active;RenderTexture.active=composite;var image=new Texture2D(2560,1440,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,2560,1440),0,0);image.Apply();RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
        void Finish(){File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));enabled=false;Application.Quit(report.status=="PASS"?0:1);}
        void OnDestroy(){foreach(var target in new[]{worldTarget,hudTarget,composite})if(target!=null){target.Release();Destroy(target);}if(material!=null)Destroy(material);}
    }
}
