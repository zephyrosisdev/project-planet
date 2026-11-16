using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        bool inventoryOpen,industryOpen,networkMenu;Vector2 inventoryScroll,craftScroll;double nextGather;
        internal bool ClientMenuOpen=>inventoryOpen||industryOpen||networkMenu||latest?.self!=null&&latest.self.health<=0;
        internal bool NetworkMenuOpen=>networkMenu;
        PersonalItemCatalogue serverItems;
        FramePose? spawnPose;
        void SeedResources(FramePose spawn){
            var surface=surfaces[spawn.body];var focus=PlanetPrecisionPolicy.Direction(new Double3(spawn.x,spawn.y,spawn.z));
            for(int n=0;n<6;n++){
                string resource=n==3?"stone":n==4?"fuel":n==5?"water":"paydirt";
                if(authority.World.deposits.Exists(d=>d.pose.body==spawn.body&&(n<3?d.resource==resource&&((n==0&&d.remaining.iron>0)||(n==1&&d.remaining.copper>0)||(n==2&&d.remaining.radioactive>0)):d.resource==resource)))continue;
                for(int attempt=0;attempt<64;attempt++){
                    var p=PlanetIndustry.DepositPosition(surface,focus,n,Config.seed,attempt);var pose=new FramePose{body=spawn.body,x=p.x,y=p.y,z=p.z};
                    if(surface.SampleHeight(p)<surface.Definition.oceanHeight+.2||authority.World.deposits.Exists(d=>d.pose.Distance(pose)<80))continue;
                    authority.World.deposits.Add(new SurfaceDeposit{id=authority.World.AllocateId(),layoutVersion=1,pose=pose,resource=resource,remaining=new MaterialLot{litres=50000,iron=n==0?10000:0,copper=n==1?10000:0,radioactive=n==2?5000:0,activity=n==2?25000:0}});break;
                }
            }
            authority.Save();
        }
        string UseItem(Peer peer,string item){
            if(peer.record.nextUseTick>clock.Tick)return "Item is cooling down";
            serverItems??=PersonalItemCatalogue.Load();var definition=serverItems.Find(item);
            if(definition==null||definition.kind!=PersonalItemKind.Consumable)return "Choose a consumable";
            if(!authority.ChangePersonalItem(peer.record.actor,item,1,false))return "Item unavailable";
            var actor=authority.World.Actor(peer.record.actor);actor.health=Math.Min(100,actor.health+definition.health);peer.record.hunger=Math.Min(100,peer.record.hunger+definition.hunger);peer.record.thirst=Math.Min(100,peer.record.thirst+definition.thirst);
            if(item=="suit-oxygen")peer.record.oxygen=Math.Min(100,peer.record.oxygen+60);peer.record.nextUseTick=clock.Tick+(long)Math.Ceiling((definition.useSeconds+.25)*Config.tickRate);return null;
        }
        void GameplayInput(){
            if(!Online||latest?.self==null||ChatInputCaptured||!Application.isFocused)return;
            var keys=Keyboard.current;if(keys==null)return;
            if(latest.self.health<=0)return;
            if(CraftingTextCaptured){if(keys.escapeKey.wasPressedThisFrame){industryOpen=false;UpdateNetworkCursor();}return;}
            if(!ClientMenuOpen&&Config.allowTestTimeControls){if(keys.numpadPlusKey.wasPressedThisFrame)Submit(new SurvivalAction{kind="test-time-scale",count=SurvivalServerConfig.AdjacentTestTimeScale(Config.testTimeScale,1)});else if(keys.numpadMinusKey.wasPressedThisFrame)Submit(new SurvivalAction{kind="test-time-scale",count=SurvivalServerConfig.AdjacentTestTimeScale(Config.testTimeScale,-1)});}
            WeaponInput();
            if(keys.iKey.wasPressedThisFrame){inventoryOpen=!inventoryOpen;industryOpen=networkMenu=false;UpdateNetworkCursor();}
            if(keys.f4Key.wasPressedThisFrame){industryOpen=!industryOpen;industryTools=false;inventoryOpen=networkMenu=false;UpdateNetworkCursor();}
            if(keys.escapeKey.wasPressedThisFrame){if(placingMachine!=null){placingMachine=null;placementPose=null;return;}if(inventoryOpen||industryOpen){inventoryOpen=industryOpen=false;}else networkMenu=!networkMenu;UpdateNetworkCursor();}
            if(!ClientMenuOpen&&keys.fKey.wasPressedThisFrame){
                if(predicted?.vehicle!=null)Submit(new SurvivalAction{kind="exit-driver"});
                else if(predicted?.ship!=null){if(latest.vitals.controlledShip!=0)Submit(new SurvivalAction{kind="leave-pilot"});else {var interaction=InteriorInteraction();if(interaction!=null)Submit(interaction);}}
                else {IndustrialMachine nearest=null;double range=12;foreach(var m in clientMachines.Values)if(m.Definition.Vehicle&&m.owner==ActorId&&ClientDistance(m.pose)<range){nearest=m;range=ClientDistance(m.pose);}if(nearest!=null)Submit(new SurvivalAction{kind="enter-driver",entity=nearest.id});else {var exterior=ExteriorShipInteraction();if(exterior!=null){Submit(exterior);return;}SurvivalShipState ship=null;double shipRange=12;foreach(var candidate in clientShips.Values)if(candidate.entity!=0&&latest.self.pose.Distance(ClientShipEntry(candidate))<shipRange){ship=candidate;shipRange=latest.self.pose.Distance(ClientShipEntry(candidate));}if(ship!=null)Submit(new SurvivalAction{kind="board-passenger",entity=ship.entity});}}
            }
            // Disembark by walking down the ramp; no local teleport shortcut.
            if(!ClientMenuOpen&&predicted?.ship==null&&keys.gKey.isPressed&&Time.realtimeSinceStartupAsDouble>=nextGather){
                nextGather=Time.realtimeSinceStartupAsDouble+.4;SurvivalEntity nearest=null;double distance=5;
                foreach(var entity in visible.Values)if(entity.kind=="deposit"){double d=latest.self.pose.Distance(entity.pose);if(d<distance){distance=d;nearest=entity;}}
                if(nearest!=null)Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="dig",source=nearest.id/8,count=50}});
            }
        }
        void UpdateNetworkCursor(){Cursor.lockState=ClientMenuOpen?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=ClientMenuOpen;}
        void GameplayGUI(){
            if((inventoryOpen||industryOpen)&&!PrivateStateReady){networkUi.Box(new Rect(20,120,480,80),"Receiving inventory and world state…");return;}
            if(latest.self.health<=0){DeadRespawnGUI();return;}
            if(networkMenu){var panel=new Rect(networkUi.ViewWidth/2-250,networkUi.ViewHeight/2-230,500,460);networkUi.Box(panel,"MULTIPLAYER · server simulation continues");networkUi.BeginArea(new Rect(panel.x+14,panel.y+30,panel.width-28,panel.height-44));recallMenuScroll=networkUi.BeginScrollView(recallMenuScroll);ShipRecallGUI();SurvivalBindingsGUI();if(networkUi.Button("RESUME")){networkMenu=false;UpdateNetworkCursor();}if(networkUi.Button("DISCONNECT"))_ = Disconnect();networkUi.EndScrollView();networkUi.EndArea();}
            if(industryOpen&&industryTools){
                var panel=new Rect(20,210,600,Mathf.Max(200,networkUi.ViewHeight-310));networkUi.Box(panel,"CRAFTING · gather nearby resources with G");
                networkUi.BeginArea(new Rect(panel.x+12,panel.y+30,panel.width-24,panel.height-44));craftScroll=networkUi.BeginScrollView(craftScroll);
                if(networkUi.Button("BACK TO CRAFTING"))industryTools=false;
                StationGUI();BuildingGUI();ShipRecallGUI();ShipInvitationsGUI();ShipyardGUI();RespawnPointsGUI(false);FactoryRouteGUI();MachineControlsGUI();
                networkUi.EndScrollView();networkUi.EndArea();
            }
        }
    }
}
