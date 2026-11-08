using System;
using MassiveArmy.Networking;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy {
    public sealed partial class BattleApp {
        VisualElement multiplayerPage;Label multiplayerStatus;ScrollView serverRows;TextField relayInput;int serverQueryGeneration;
        void ShowMultiplayer(){
            if(multiplayerPage!=null){multiplayerPage.RemoveFromHierarchy();multiplayerPage=null;}
            multiplayerPage=new VisualElement();multiplayerPage.style.position=Position.Absolute;multiplayerPage.style.left=40;multiplayerPage.style.top=40;multiplayerPage.style.right=40;multiplayerPage.style.bottom=40;multiplayerPage.style.backgroundColor=new Color(.025f,.04f,.065f,.99f);multiplayerPage.style.paddingLeft=24;multiplayerPage.style.paddingTop=24;menuUi.Add(multiplayerPage);
            L(multiplayerPage,"MULTIPLAYER SERVERS","heading");multiplayerStatus=L(multiplayerPage,"Signing in…","body");
            var controls=Box(multiplayerPage,"row");B(controls,"REFRESH",()=>RefreshServers(),null);relayInput=new TextField("Relay join code"){maxLength=16};controls.Add(relayInput);B(controls,"JOIN CODE",()=>JoinSurvival(relayInput.value,null),null);
            B(controls,"BACK",()=>{++serverQueryGeneration;multiplayerPage?.RemoveFromHierarchy();multiplayerPage=null;_ = SurvivalNetworkSession.Active?.Disconnect(false);},null);
            serverRows=new ScrollView();multiplayerPage.Add(serverRows);RefreshServers();
        }
        async void RefreshServers(){
            int generation=++serverQueryGeneration;try{
                var discovery=new SurvivalServices();await discovery.Initialize("production","survival-client");var lobbies=await discovery.Query();
                if(generation!=serverQueryGeneration||multiplayerPage==null)return;serverRows.Clear();multiplayerStatus.text=lobbies.Count==0?"No registered servers found.":"Select a server to join.";
                foreach(var lobby in lobbies){var selected=lobby;string Read(string key)=>lobby.Data!=null&&lobby.Data.TryGetValue(key,out var value)?value.Value:"?";
                    var row=Box(serverRows,"row");L(row,lobby.Name+" · "+Read("players")+"/"+Read("capacity")+" · "+Read("region")+" · "+Read("relay"),"body");B(row,"JOIN",()=>JoinSurvival("",selected.Id),null);
                }
            }catch(Exception e){if(generation==serverQueryGeneration&&multiplayerStatus!=null)multiplayerStatus.text="Server list unavailable: "+e.Message;}
        }
        async void JoinSurvival(string code,string lobby){
            if(string.IsNullOrWhiteSpace(code)&&lobby==null)return;multiplayerStatus.text="Connecting…";var session=SurvivalNetworkSession.Ensure();await session.Connect(this,code,lobby);if(multiplayerStatus!=null)multiplayerStatus.text=session.Status;
        }
        internal void StartNetworkSurvival(string definitionJson,SurvivalNetworkSession session){
            StartSurvivalGame();planetSession.NetworkPresentation=true;
            if(planetSession.definition!=null)ArmyLifetime.Destroy(planetSession.definition);
            var template=ScriptableObject.CreateInstance<Planetary.PlanetDefinition>();JsonUtility.FromJsonOverwrite(definitionJson,template);planetSession.ConfigureDefinition(template,template.terrainSeed);ArmyLifetime.Destroy(template);
            planetSession.configuredRadius=planetSession.definition.radius;planetSession.terrainSeed=planetSession.definition.terrainSeed;planetSession.returnToMenu=()=>{_ = session.Disconnect();};session.BindWorld(planetSession);
        }
        internal void ReturnFromNetwork(){ReturnMenu();View.enabled=true;ShowMultiplayer();}
    }
}
