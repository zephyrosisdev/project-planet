#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MassiveArmy.Industrial;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
using Cursor=UnityEngine.Cursor;
namespace MassiveArmy.Planetary {
    public static class GovernmentUiValidation {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        [Serializable] sealed class Report {public string status="PASS",error="",scope="Isolated UI Toolkit element and real button-callback smoke test, without changing the live world or stopping Play mode. Does not verify rendered layout or keyboard focus.";public List<string> checks=new List<string>();}
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
        static void Click(VisualElement root,string text){Button found=null;root.Query<Button>().ForEach(b=>{if(found==null&&b.text==text)found=b;});if(found==null)throw new InvalidOperationException("Missing UI button: "+text);typeof(Clickable).GetMethod("Invoke",Flags,null,new[]{typeof(EventBase)},null).Invoke(found.clickable,new object[]{null});}
        [InitializeOnLoadMethod] static void Install(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
        static void Pump(){const string request="TestResults/Planetary/Government/ui-request.txt";if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(request))return;File.Delete(request);Run();}
        [MenuItem("Synthetic Front/Diagnostics/Validate tribe and empire UI")]
        public static void Run(){
            var report=new Report();GameObject host=null;PlanetDefinition planet=null;IndustrialAuthority authority=null;PersonalGameplayHud hud=null;PlanetFirstPersonController player=null;PlanetIndustry industry=null;var cursor=Cursor.lockState;bool visible=Cursor.visible;
            void Check(bool pass,string text){if(!pass)throw new InvalidOperationException(text);report.checks.Add(text);}
            try{
                host=new GameObject("Inactive government UI fixture");host.SetActive(false);var world=host.AddComponent<PlanetDevelopment>();world.sessionMode=PlanetGameMode.Survival;planet=ScriptableObject.CreateInstance<PlanetDefinition>();planet.bodyId=1;planet.radius=10000;planet.terrainHeight=0;world.definition=planet;
                var origin=new FloatingOrigin(default(UniversePosition));var surface=new PlanetSurface(planet);Set(world,"origin",origin);Set(world,"surface",surface);var cameraHost=new GameObject("Government fixture camera");cameraHost.transform.SetParent(host.transform);var camera=cameraHost.AddComponent<Camera>();camera.enabled=false;Set(world,"view",camera);player=new PlanetFirstPersonController(surface,origin,camera,host.transform){CaptureCursor=false};Set(world,"player",player);player.EnterBodyPosition(new Double3(0,10000,0),Vector3.forward);
                var modes=new PlayerControlModeManager();modes.Bind(PlayerControlMode.FirstPerson,_=>{});modes.Switch(PlayerControlMode.FirstPerson);Set(world,"modes",modes);
                var data=new IndustrialWorld{creativeMode=true};data.Register(1,world.RespawnPose(player.PreciseBodyPosition));authority=new IndustrialAuthority(data);industry=new PlanetIndustry(world,authority);Set(world,"industry",industry);var root=new VisualElement();hud=new PersonalGameplayHud(world,root);Set(world,"personalHud",hud);
                Check(!hud.Open&&!hud.OrganizationOpen,"Management starts closed on spawn");hud.ShowOrganization();Check(hud.OrganizationOpen&&world.PersonalMenuOpen,"Opening organization sets modal gameplay state");var create=root.Q<TextField>("tribe-create-name");Check(create!=null,"Independent players see the tribe founding form");create.value="UI Tribe";Click(root,"FOUND TRIBE");Check(data.government.ForActor(1)?.name=="UI Tribe","Found button creates a tribe through the real authority adapter");
                Click(root,"RULES");var policy=root.Q<Toggle>("tribe-rule-officer-construction");Check(policy!=null&&policy.enabledSelf,"Sovereign sees editable inherited policy controls");policy.value=true;Check(data.government.ForActor(1).rules.officerConstruction,"Rule toggle commits a real inherited policy change");
                Click(root,"DIPLOMACY");Click(root,"CREATE INDEPENDENT TEST TRIBE");Check(data.government.tribes.Count==2,"Creative test tribe button creates data only");Click(root,"OFFER EQUAL ALLIANCE");Check(data.government.invitations.Count==1,"Alliance offer button creates a pending invitation");Click(root,"CREATIVE: ACCEPT AS TEST LEADER");Check(data.government.alliances.Count==1&&!data.government.CanGovern(1,data.government.tribes[1].id),"Explicit test acceptance creates an equal ally without management rights");
                Click(root,"ORDERS");Click(root,"ISSUE ORDER AT MY CURRENT LOCATION");Check(data.government.orders.Count==1,"Order form dispatches a persistent location-based directive");Click(root,"ACKNOWLEDGE");Check(data.government.orders[0].recipients[0].status==TribeOrderStatus.Acknowledged,"Order acknowledgement button updates its receipt");Click(root,"COMPLETE");Click(root,"ARCHIVE RESOLVED");Check(data.government.orders.Count==0,"Completed orders can be explicitly archived");
                Click(root,"CLOSE");Check(!hud.Open&&!hud.OrganizationOpen&&hud.InventorySurface.style.display==DisplayStyle.None&&!world.PersonalMenuOpen,"Close hides the management panel and releases modal gameplay state");hud.ShowOrganization();Check(hud.OrganizationOpen,"Management can reopen after closing");hud.Close();
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            finally{hud?.Dispose();if(host!=null){var world=host.GetComponent<PlanetDevelopment>();Set(world,"personalHud",null);Set(world,"industry",null);Set(world,"player",null);Set(world,"view",null);}industry?.ReleaseUi();authority?.Dispose();player?.Dispose();if(host!=null)Object.DestroyImmediate(host);if(planet!=null)Object.DestroyImmediate(planet);Cursor.lockState=cursor;Cursor.visible=visible;Directory.CreateDirectory("TestResults/Planetary/Government");File.WriteAllText("TestResults/Planetary/Government/ui-validation.json",JsonUtility.ToJson(report,true));Debug.Log("Government UI validation "+report.status+": "+report.checks.Count+" checks");}
        }
    }
}
#endif
