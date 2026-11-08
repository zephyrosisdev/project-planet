#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        SurvivalCombatFixtureSpec combatSpec;int combatStep,combatShots;long combatDrop,combatReplayAfter;bool combatSubscribed,combatReplyDone;SurvivalAction combatRequest;SurvivalActionReply combatReply;
        string CombatMarker(string name)=>Path.Combine(Path.GetDirectoryName(path),"combat-"+name+".ack");
        bool StartCombatProbe(){string source=Argument("-survival-probe-combat");if(source==null)return true;try{if(report.chatRequired||report.factoryRequired||report.interiorRequired||!Path.GetFullPath(source).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid isolated combat probe");combatSpec=JsonUtility.FromJson<SurvivalCombatFixtureSpec>(File.ReadAllText(source));if(combatSpec?.seeded!=true||combatSpec.version!=1)throw new InvalidOperationException("Combat fixture was not seeded offline");report.combatRequired=true;return true;}catch(Exception error){_ = Finish(false,error.Message);return false;}}
        void CombatReply(SurvivalActionReply reply){if(combatRequest?.sequence==reply.sequence)combatReply=reply;}
        void CombatSend(SurvivalAction request){combatReplyDone=false;combatReply=null;combatRequest=request;session.Submit(request);}
        bool CombatReplied(bool expected){if(combatReplyDone)return true;if(combatReply==null)return false;if(combatReply.accepted!=expected){_ = Finish(false,"Unexpected combat reply: "+combatReply.message);return false;}combatReply=null;combatReplyDone=true;return true;}
        void UpdateCombatProbe(){
            if(!session.PrivateStateReady)return;if(!combatSubscribed){session.ActionReplied+=CombatReply;combatSubscribed=true;startPose=session.Latest.self.pose;}
            report.combatStepNow=combatStep;if(report.actor==combatSpec.shooter)ShooterTick();else if(report.actor==combatSpec.victim)VictimTick();else _ = Finish(false,"Unexpected combat identity");
        }
        void ShooterTick(){
            var snapshot=session.Latest;
            switch(combatStep){
                case 0:if(!report.remotePlayerSeen)return;CombatSend(new SurvivalAction{kind="shoot"});combatStep=1;break;
                case 1:if(!CombatReplied(true))return;combatShots++;report.combatShots=combatShots;combatStep=2;break;
                case 2:if(snapshot.self.inventory.Count("ammo-9mm")!=6-combatShots)return;if(combatShots<4){if(snapshot.tick<snapshot.vitals.nextAttackTick)return;CombatSend(new SurvivalAction{kind="shoot"});combatStep=1;}else{combatReplyDone=false;combatReply=null;combatReplayAfter=snapshot.tick+session.Config.tickRate;session.RepeatProbeAction(combatRequest);combatStep=3;}break;
                case 3:if(snapshot.tick<combatReplayAfter||snapshot.self.inventory.Count("ammo-9mm")!=2)return;report.combatReplay=true;combatStep=4;break;
                case 4:if(!File.Exists(CombatMarker("victim-done"))||snapshot.self.inventory.Count("ammo-9mm")!=2)return;report.combatKilled=true;phase=3;combatStep=5;_ = Reconnect();break;
                case 5:if(phase!=4||snapshot.self.id!=report.actor||snapshot.self.inventory.Count("ammo-9mm")!=2)return;report.reconnected=report.combatReconnect=true;report.movementMetres=startPose.Distance(snapshot.self.pose);_ = Finish(true,null);break;
            }
        }
        void VictimTick(){
            var snapshot=session.Latest;
            switch(combatStep){
                case 0:if(snapshot.self.health>0||snapshot.self.inventory.Used!=0)return;foreach(var entity in session.Visible.Values)if(entity.kind=="loot"){combatDrop=entity.id/8;break;}if(combatDrop==0)return;report.combatKilled=true;CombatSend(new SurvivalAction{kind="respawn",entity=combatSpec.point});combatStep=1;break;
                case 1:if(!CombatReplied(true))return;if(snapshot.self.health<=0||snapshot.self.inventory.Used!=0)return;report.combatRevived=true;if(!session.Visible.TryGetValue(combatDrop*8,out var loot))return;var drop=JsonUtility.FromJson<DroppedInventory>(loot.data);var stack=drop.inventory.items.Find(s=>s.item=="copper-bar");if(stack==null||stack.count!=3)return;CombatSend(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{section="ground",entity=combatDrop,slot=stack.slot},to=new InventoryAddress{slot=0},count=3});combatStep=2;break;
                case 2:if(!CombatReplied(true))return;if(snapshot.self.inventory.Count("copper-bar")!=3)return;combatReplyDone=false;combatReply=null;combatReplayAfter=snapshot.tick+session.Config.tickRate;session.RepeatProbeAction(combatRequest);combatStep=3;break;
                case 3:if(snapshot.tick<combatReplayAfter)return;if(snapshot.self.inventory.Count("copper-bar")!=3)return;if(session.Visible.TryGetValue(combatDrop*8,out var remaining)&&JsonUtility.FromJson<DroppedInventory>(remaining.data).inventory.Count("copper-bar")!=0)return;report.combatReplay=report.combatRecovered=true;File.WriteAllText(CombatMarker("victim-done"),"death drop, revival and recovery conserved");phase=3;combatStep=4;_ = Reconnect();break;
                case 4:if(phase!=4||snapshot.self.id!=report.actor||snapshot.self.health<=0||snapshot.self.inventory.Count("copper-bar")!=3)return;report.reconnected=report.combatReconnect=true;report.movementMetres=startPose.Distance(snapshot.self.pose);_ = Finish(true,null);break;
            }
        }
        void StopCombatProbe(){if(combatSubscribed&&session!=null)session.ActionReplied-=CombatReply;combatSubscribed=false;}
    }
}
#endif
