using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        long trackedTribeOrder;
        internal TribeGovernment Government=>IndustrialAuthority?.World.government;
        internal string BuildingPolicyError=>IsSurvival?IndustrialAuthority?.World.ConstructionPolicyError(1):null;
        internal string MilitaryPolicyError=>IsSurvival?IndustrialAuthority?.World.CombatPolicyError(1):null;
        internal void CloseShipyardMenu(){shipyard?.CloseForOtherMenu();}
        internal void SendTribeCommand(string kind,long source=0,long target=0,string text=null,string definition=null,long count=0,string operation=null){
            var authority=IndustrialAuthority;if(authority==null)return;var actor=authority.World.Actor(1);actor.pose=RespawnPose(player.PreciseBodyPosition);
            var result=authority.Execute(1,new IndustrialCommand{sequence=actor.lastSequence+1,kind=kind,source=source,target=target,item=text,definition=definition,count=count,operationRole=operation,pose=actor.pose});
            SurvivalNotice=result.accepted?Government.history[Government.history.Count-1].text:result.message;if(result.accepted)authority.Save();personalHud?.RefreshPanel();
        }
        internal string TribeHudHint(){
            var data=IndustrialAuthority?.World;var g=data?.government;var tribe=g?.ForActor(1);if(tribe==null)return "O tribe / empire management";
            var sovereign=g.Root(tribe);string hint=tribe.name+(tribe.parent!=0?" · under "+sovereign.name:" · "+g.Rank(tribe))+" · O management";
            if(trackedTribeOrder!=0){var task=g.orders.Find(o=>o.id==trackedTribeOrder);if(task==null||!g.OrderLive(task)||!g.OrderVisible(1,task)){trackedTribeOrder=0;return hint;}var at=RespawnPose(player.PreciseBodyPosition);double distance=at.Distance(task.location);hint+="\n"+task.kind+": "+task.text+(double.IsInfinity(distance)?" · planet "+task.location.body:$" · {distance:0} m");}
            return hint;
        }
        internal void TrackTribeOrder(long id){trackedTribeOrder=id;SurvivalNotice=id==0?"Order waypoint cleared":"Tracking order #"+id;}
    }
    public sealed partial class PersonalGameplayHud {
        string governmentTab="Realm",governmentOrderKind="Rally";long governmentRevision=-1,governmentSelection;bool governmentDestructive;
        public bool OrganizationOpen=>Open&&mode=="organization";
        IndustrialWorld GovernmentWorld=>world.IndustrialAuthority?.World;
        TribeGovernment Gov=>GovernmentWorld?.government;
        void GovAction(VisualElement parent,string label,string kind,long source=0,long target=0,string text=null,long count=0,string operation=null,bool enabled=true){Action(parent,label,()=>world.SendTribeCommand(kind,source,target,text,count:count,operation:operation)).SetEnabled(enabled);}
        void GovSection(VisualElement parent,string label){var t=Text(parent,label,"heading");t.style.marginTop=14;t.style.marginBottom=6;}
        void GovLine(VisualElement parent,string text){var label=Text(parent,text);label.style.whiteSpace=WhiteSpace.Normal;label.enableRichText=false;}
        void BuildGovernment(){
            title.text="TRIBES / RULERSHIP";var g=Gov;if(g==null){GovLine(content,"The authoritative survival world is still loading.");return;}governmentRevision=g.revision;
            var data=GovernmentWorld;var mine=g.ForActor(1);var rootTribe=g.Root(mine);GovLine(content,world.SurvivalNotice);GovLine(content,"Alliances are equal non-aggression treaties. Becoming a subject puts your tribe and its branches under another ruler's inherited policies and command.");
            var tabs=new VisualElement();tabs.style.flexDirection=FlexDirection.Row;tabs.style.flexWrap=Wrap.Wrap;content.Add(tabs);
            foreach(string name in new[]{"Realm","People","Diplomacy","Rules","Orders","History"}){string value=name;Action(tabs,name.ToUpperInvariant(),()=>{governmentTab=value;RefreshPanel();});}
            if(mine==null){GovSection(content,"FOUND A TRIBE");var name=new TextField("Tribe name"){name="tribe-create-name",maxLength=48};name.SetValueWithoutNotify("New Tribe");content.Add(name);Action(content,"FOUND TRIBE",()=>world.SendTribeCommand("tribe-create",text:name.value)).name="tribe-create";}
            else GovLine(content,$"{mine.name} · your rank: {mine.Member(1).role} · sovereign: {rootTribe.name} · {g.Rank(rootTribe)} · {g.RealmTribes(mine)} tribes · {g.Depth(mine)}/{TribeGovernment.MaximumDepth} hierarchy depth");
            if(governmentTab=="Realm")BuildRealm(mine,rootTribe);
            else if(governmentTab=="People")BuildTribePeople(mine);
            else if(governmentTab=="Diplomacy")BuildDiplomacy(mine,rootTribe);
            else if(governmentTab=="Rules")BuildTribeRules(mine,rootTribe);
            else if(governmentTab=="Orders")BuildTribeOrders(mine);
            else {GovSection(content,"RECENT REALM EVENTS (LATEST 256)");for(int i=g.history.Count-1;i>=0;i--){var entry=g.history[i];GovLine(content,$"Tick {entry.tick} · {data.PlayerName(entry.actor)} · {entry.text}");}}
            if(data.creativeMode){GovSection(content,"CREATIVE DIPLOMACY TESTING");GovLine(content,"Test tribes have data-only test leaders: no civilians, units or simulated armies. The creative host can explicitly accept invitations on their behalf.");var name=new TextField("Independent test tribe"){maxLength=48,value="Test Tribe "+(g.tribes.Count+1)};content.Add(name);Action(content,"CREATE INDEPENDENT TEST TRIBE",()=>world.SendTribeCommand("tribe-test-create",text:name.value));}
        }
        void BuildRealm(TribeRecord mine,TribeRecord ruler){
            GovSection(content,"CHAIN OF COMMAND");if(mine!=null)RenderBranch(ruler,0,new HashSet<long>());else GovLine(content,"Join or found a tribe to enter its command hierarchy.");
            GovSection(content,"INDEPENDENT ORGANIZATIONS");foreach(var tribe in Gov.tribes)if(tribe.parent==0&&(ruler==null||tribe.id!=ruler.id))GovLine(content,$"#{tribe.id} {tribe.name} · {Gov.Rank(tribe)} · {Gov.RealmTribes(tribe)} tribes · leader {GovernmentWorld.PlayerName(tribe.Leader)}"+(ruler!=null&&Gov.Allied(ruler.id,tribe.id)?" · EQUAL ALLY":""));
            if(mine==null)return;
            GovSection(content,"SELECTED BRANCH");var selected=Gov.Tribe(governmentSelection)??mine;GovLine(content,$"#{selected.id} {selected.name} · leader {GovernmentWorld.PlayerName(selected.Leader)} · {selected.members.Count}/64 members");
            if(selected.parent!=0&&Gov.Sovereign(1)&&Gov.Contains(mine.id,selected.id)){
                GovAction(content,"RELEASE AS INDEPENDENT", "tribe-release",source:selected.id,enabled:governmentDestructive);
                var ids=new List<long>();var names=new List<string>();foreach(var t in Gov.tribes)if(Gov.Contains(mine.id,t.id)&&!Gov.Contains(selected.id,t.id)){ids.Add(t.id);names.Add(t.name+" #"+t.id);}if(ids.Count>0){var parents=new DropdownField("New superior tribe",names,0);content.Add(parents);Action(content,"MOVE BRANCH UNDER SELECTED SUPERIOR",()=>world.SendTribeCommand("tribe-reparent",selected.id,ids[parents.index]));}
            }
            if(Gov.LeaderOf(1,mine)){var name=new TextField("Your tribe's name"){value=mine.name,maxLength=48};content.Add(name);Action(content,"RENAME TRIBE",()=>world.SendTribeCommand("tribe-rename",text:name.value));if(mine.parent!=0)GovAction(content,"DECLARE INDEPENDENCE (IF RULER PERMITS)","tribe-secede",enabled:ruler.rules.allowSecession&&governmentDestructive);GovAction(content,"DISBAND LEADER-ONLY TRIBE", "tribe-disband",enabled:mine.members.Count==1&&!Gov.tribes.Exists(t=>t.parent==mine.id)&&(mine.parent==0||ruler.rules.allowSecession)&&!Gov.orders.Exists(o=>o.issuerTribe==mine.id)&&governmentDestructive);}
            var unlock=new Toggle("Enable release / secession / disband actions"){value=governmentDestructive};content.Add(unlock);unlock.RegisterValueChangedCallback(e=>{governmentDestructive=e.newValue;RefreshPanel();});
        }
        void RenderBranch(TribeRecord tribe,int depth,HashSet<long> visited){if(tribe==null||depth>=TribeGovernment.MaximumDepth||!visited.Add(tribe.id))return;var row=new VisualElement();row.style.marginLeft=depth*20;content.Add(row);Action(row,(depth==0?"SOVEREIGN · ":"SUBJECT · ")+tribe.name+" · "+tribe.members.Count+" members",()=>{governmentSelection=tribe.id;RefreshPanel();});foreach(var child in Gov.tribes)if(child.parent==tribe.id)RenderBranch(child,depth+1,visited);}
        void BuildTribePeople(TribeRecord mine){
            GovSection(content,"YOUR PLAYER PROFILE");var profile=new TextField("Display name"){value=GovernmentWorld.PlayerName(1),maxLength=32};content.Add(profile);Action(content,"UPDATE PLAYER NAME",()=>world.SendTribeCommand("tribe-profile",text:profile.value));
            if(mine==null)return;GovSection(content,"MEMBERS AND RANKS");bool leader=Gov.LeaderOf(1,mine);
            foreach(var member in mine.members){long actor=member.actor;GovLine(content,GovernmentWorld.PlayerName(actor)+" #"+actor+" · "+member.role);if(actor==1)continue;var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexWrap=Wrap.Wrap;content.Add(row);GovAction(row,"PROMOTE TO OFFICER","tribe-role",target:actor,text:"Officer",enabled:leader&&member.role==TribeRole.Member);GovAction(row,"MAKE MEMBER","tribe-role",target:actor,text:"Member",enabled:leader&&member.role==TribeRole.Officer);GovAction(row,"TRANSFER LEADERSHIP","tribe-transfer-leadership",target:actor,enabled:leader&&governmentDestructive);GovAction(row,"REMOVE MEMBER","tribe-kick",target:actor,enabled:leader&&governmentDestructive);}
            GovSection(content,"INVITE AN UNALIGNED PLAYER");bool found=false;foreach(var actor in GovernmentWorld.actors)if(!actor.governmentTestActor&&actor.id!=1&&Gov.ForActor(actor.id)==null){found=true;GovAction(content,"INVITE "+GovernmentWorld.PlayerName(actor.id),"tribe-invite-member",target:actor.id,enabled:mine.Member(1).role!=TribeRole.Member);}if(!found)GovLine(content,"No other unaligned players are registered in this world.");
            GovAction(content,"LEAVE TRIBE","tribe-leave",enabled:!leader);var unlock=new Toggle("Enable membership removal and leadership transfer"){value=governmentDestructive};content.Add(unlock);unlock.RegisterValueChangedCallback(e=>{governmentDestructive=e.newValue;RefreshPanel();});
        }
        void BuildDiplomacy(TribeRecord mine,TribeRecord ruler){
            GovSection(content,"INVITATIONS");if(Gov.invitations.Count==0)GovLine(content,"No pending invitations.");foreach(var invite in Gov.invitations){var from=Gov.Tribe(invite.from);var to=Gov.Tribe(invite.to);bool received=invite.kind==TribeInvitationKind.Member?invite.player==1:Gov.LeaderOf(1,to);bool test=GovernmentWorld.creativeMode&&GovernmentWorld.Actor(to?.Leader??0)?.governmentTestActor==true;bool sent=mine!=null&&invite.from==mine.id;if(!received&&!sent&&!test)continue;
                GovLine(content,$"#{invite.id} {invite.kind} · {from?.name} → "+(invite.kind==TribeInvitationKind.Member?GovernmentWorld.PlayerName(invite.player):to?.name));
                if(invite.kind==TribeInvitationKind.Subordination)GovLine(content,"ACCEPTING MEANS SUBORDINATION: your entire branch inherits the new ruler's policies and command. Your previous independent alliances end. Member inventories and asset ownership stay with their current owners.");
                if(received||test){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;content.Add(row);string action=test&&!received?"tribe-test-respond":"tribe-respond";GovAction(row,test&&!received?"CREATIVE: ACCEPT AS TEST LEADER":"ACCEPT",action,source:invite.id,count:1);GovAction(row,"DECLINE",action,source:invite.id);}
                if(sent)GovAction(content,"CANCEL INVITATION","tribe-cancel-invite",source:invite.id,enabled:mine.Member(1).role!=TribeRole.Member);
            }
            if(mine==null)return;GovSection(content,"INDEPENDENT TRIBES / EQUAL ALLIANCES");foreach(var t in Gov.tribes){if(t.parent!=0||t.id==ruler.id)continue;GovLine(content,t.name+" · "+Gov.Rank(t)+" · "+(Gov.Allied(ruler.id,t.id)?"ALLIED AS EQUALS":"NEUTRAL"));bool sovereign=Gov.Sovereign(1);var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexWrap=Wrap.Wrap;content.Add(row);GovAction(row,"OFFER EQUAL ALLIANCE","tribe-invite-alliance",target:t.id,enabled:sovereign&&!Gov.Allied(ruler.id,t.id));GovAction(row,"INVITE TO BECOME SUBJECTS","tribe-invite-subject",target:t.id,enabled:sovereign);if(Gov.Allied(ruler.id,t.id))GovAction(row,"END ALLIANCE","tribe-break-alliance",target:t.id,enabled:sovereign&&governmentDestructive);}
            if(mine.parent!=0)GovLine(content,"Foreign diplomacy is managed by your sovereign ruler. Your branch inherits the realm's allies.");var unlock=new Toggle("Enable ending an existing alliance"){value=governmentDestructive};content.Add(unlock);unlock.RegisterValueChangedCallback(e=>{governmentDestructive=e.newValue;RefreshPanel();});
        }
        void BuildTribeRules(TribeRecord mine,TribeRecord ruler){
            if(mine==null)return;GovSection(content,"INHERITED REALM POLICIES · "+ruler.name);GovLine(content,"Only the sovereign leader can change these rules. Officers and leaders can issue orders down their own branch; an equal ally never gains that authority. Policies do not transfer private inventories or building ownership.");bool editable=Gov.Sovereign(1);
            void Rule(string title,string key,bool value){var toggle=new Toggle(title){value=value,name="tribe-rule-"+key};toggle.SetEnabled(editable);content.Add(toggle);toggle.RegisterValueChangedCallback(e=>world.SendTribeCommand("tribe-rule",text:key,count:e.newValue?1:0));}
            Rule("Allow friendly fire inside this realm (equal alliances always remain protected)","friendly-fire",ruler.rules.friendlyFire);Rule("Restrict base / industrial / ship construction to officers and leaders","officer-construction",ruler.rules.officerConstruction);Rule("Restrict weapon use to officers and leaders","officer-combat",ruler.rules.officerCombat);Rule("Allow subject leaders to declare independence","allow-secession",ruler.rules.allowSecession);
            var orders=new TextField("Standing orders"){value=ruler.rules.standingOrders,multiline=true,maxLength=280};orders.SetEnabled(editable);content.Add(orders);Action(content,"PUBLISH STANDING ORDERS",()=>world.SendTribeCommand("tribe-standing-orders",text:orders.value)).SetEnabled(editable);
        }
        void BuildTribeOrders(TribeRecord mine){
            if(mine==null)return;GovSection(content,"COMMAND ORDERS");GovLine(content,"These are persistent player task directives with acknowledgements and objective locations. They do not spawn or take control of RTS units.");
            if(mine.Member(1).role!=TribeRole.Member){var ids=new List<long>();var names=new List<string>();foreach(var tribe in Gov.tribes)if(Gov.CanGovern(1,tribe.id)){ids.Add(tribe.id);names.Add(tribe.name+" #"+tribe.id);}if(ids.Count>0){var recipient=new DropdownField("Recipient tribe",names,0);var types=new List<string>(Enum.GetNames(typeof(TribeOrderKind)));var kind=new DropdownField("Order type",types,Math.Max(0,types.IndexOf(governmentOrderKind)));kind.RegisterValueChangedCallback(e=>governmentOrderKind=e.newValue);var objective=new TextField("Objective"){maxLength=120,value="Rally at my current location"};var cascade=new Toggle("Include all subordinate tribes"){value=true};content.Add(recipient);content.Add(kind);content.Add(objective);content.Add(cascade);
                    var enemies=new List<long>{0};var enemyNames=new List<string>{"Choose a hostile independent realm for Attack"};foreach(var t in Gov.tribes)if(t.parent==0&&Gov.CanAttackTribe(1,t.id)){enemies.Add(t.id);enemyNames.Add(t.name+" #"+t.id);}var hostile=new DropdownField("Attack target",enemyNames,0);content.Add(hostile);Action(content,"ISSUE ORDER AT MY CURRENT LOCATION",()=>world.SendTribeCommand("tribe-order",ids[recipient.index],enemies[hostile.index],objective.value,count:cascade.value?1:0,operation:kind.value)).name="tribe-issue-order";}}
            GovSection(content,$"ORDER BOARD · {Gov.orders.Count}/{TribeGovernment.MaximumOrders}");foreach(var task in Gov.orders){if(!Gov.OrderVisible(1,task))continue;bool live=Gov.OrderLive(task);GovLine(content,$"#{task.id} {task.kind} · {task.text} · "+(live?"ACTIVE":"CANCELLED / REVOKED")+$" · planet {task.location.body}");foreach(var receipt in task.recipients)GovLine(content,"  "+(Gov.Tribe(receipt.tribe)?.name??"Former tribe")+" · "+(Gov.ReceiptLive(task,receipt)?receipt.status.ToString():"REVOKED"));var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexWrap=Wrap.Wrap;content.Add(row);
                Action(row,"TRACK WAYPOINT",()=>world.TrackTribeOrder(task.id)).SetEnabled(live);bool assigned=task.recipients.Exists(r=>r.tribe==mine.id&&Gov.ReceiptLive(task,r)&&r.status!=TribeOrderStatus.Completed);bool officer=mine.Member(1).role!=TribeRole.Member;GovAction(row,"ACKNOWLEDGE","tribe-order-status",source:task.id,text:"Acknowledged",enabled:live&&assigned&&officer);GovAction(row,"COMPLETE","tribe-order-status",source:task.id,text:"Completed",enabled:live&&assigned&&officer);bool command=Gov.CanGovern(1,task.issuerTribe);GovAction(row,"CANCEL","tribe-order-cancel",source:task.id,enabled:command&&live);GovAction(row,"ARCHIVE RESOLVED","tribe-order-archive",source:task.id,enabled:command&&(!live||!task.recipients.Exists(r=>Gov.ReceiptLive(task,r)&&r.status!=TribeOrderStatus.Completed)));
            }
            Action(content,"CLEAR TRACKED WAYPOINT",()=>world.TrackTribeOrder(0));
        }
    }
}
