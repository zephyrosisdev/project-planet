using System;
using System.Collections.Generic;
using System.Xml.Serialization;
namespace MassiveArmy.Industrial {
    public enum TribeRole {Member,Officer,Leader}
    public enum TribeInvitationKind {Member,Alliance,Subordination}
    public enum TribeOrderKind {Rally,Defend,Gather,Build,Patrol,Attack}
    public enum TribeOrderStatus {Pending,Acknowledged,Completed,Cancelled}
    [Serializable] public sealed class TribeMember {public long actor;public TribeRole role;}
    [Serializable] public sealed class TribeRules {public bool friendlyFire,officerConstruction,officerCombat,allowSecession;public string standingOrders="Respect your allies. Follow your chain of command.";}
    [Serializable] public sealed class TribeRecord {
        public long id,parent;public string name;public TribeRules rules=new TribeRules();public List<TribeMember> members=new List<TribeMember>();
        [XmlIgnore] public long Leader {get{foreach(var m in members)if(m.role==TribeRole.Leader)return m.actor;return 0;}}
        public TribeMember Member(long actor){foreach(var m in members)if(m.actor==actor)return m;return null;}
    }
    [Serializable] public sealed class TribeInvitation {public long id,from,to,player,createdAtTick;public TribeInvitationKind kind;}
    [Serializable] public sealed class TribeAlliance {public long first,second;}
    [Serializable] public sealed class TribeOrderReceipt {public long tribe,actor;public TribeOrderStatus status;}
    [Serializable] public sealed class TribeOrder {public long id,issuer,issuerTribe,targetTribe,hostileTribe,createdAtTick;public TribeOrderKind kind;public string text;public FramePose location;public bool cancelled;public List<TribeOrderReceipt> recipients=new List<TribeOrderReceipt>();}
    [Serializable] public sealed class TribeAudit {public long tick,actor;public string text;}
    [Serializable] public sealed class TribeGovernment {
        public long revision;public List<TribeRecord> tribes=new List<TribeRecord>();public List<TribeAlliance> alliances=new List<TribeAlliance>();public List<TribeInvitation> invitations=new List<TribeInvitation>();public List<TribeOrder> orders=new List<TribeOrder>();public List<TribeAudit> history=new List<TribeAudit>();
        public const int MaximumTribes=256,MaximumDepth=8,MaximumMembers=64,MaximumInvitations=256,MaximumOrders=256;
        public TribeRecord Tribe(long id){foreach(var t in tribes)if(t.id==id)return t;return null;}
        public TribeRecord ForActor(long actor){foreach(var t in tribes)if(t.Member(actor)!=null)return t;return null;}
        public TribeRecord Root(TribeRecord tribe){for(int depth=0;tribe!=null&&tribe.parent!=0&&depth<MaximumDepth;depth++)tribe=Tribe(tribe.parent);return tribe;}
        public bool Contains(long ancestor,long descendant){for(int depth=0;descendant!=0&&depth<=MaximumDepth;depth++){if(ancestor==descendant)return true;descendant=Tribe(descendant)?.parent??0;}return false;}
        public int Depth(TribeRecord t){int depth=1;for(;t!=null&&t.parent!=0&&depth<=MaximumDepth;depth++)t=Tribe(t.parent);return depth;}
        public int BranchHeight(long root){int height=1;foreach(var t in tribes)if(Contains(root,t.id))height=Math.Max(height,Depth(t)-Depth(Tribe(root))+1);return height;}
        public int RealmTribes(TribeRecord t){var root=Root(t);int total=0;foreach(var item in tribes)if(root!=null&&Contains(root.id,item.id))total++;return total;}
        public string Rank(TribeRecord t){int count=RealmTribes(t);return count>=5?"Empire":count>=3?"Federation":count>=2?"Clan":"Tribe";}
        public bool Allied(long a,long b){foreach(var treaty in alliances)if(treaty.first==a&&treaty.second==b||treaty.first==b&&treaty.second==a)return true;return false;}
        public bool CanGovern(long actor,long tribe){var mine=ForActor(actor);return mine!=null&&mine.Member(actor).role!=TribeRole.Member&&Contains(mine.id,tribe);}
        public bool LeaderOf(long actor,TribeRecord t)=>t?.Member(actor)?.role==TribeRole.Leader;
        public bool Sovereign(long actor){var t=ForActor(actor);return t!=null&&t.parent==0&&LeaderOf(actor,t);}
        public bool CanAttack(long attacker,long victim){
            if(attacker==victim)return false;var a=Root(ForActor(attacker));var b=Root(ForActor(victim));
            if(a==null||b==null)return true;if(a.id==b.id)return a.rules.friendlyFire;return !Allied(a.id,b.id);
        }
        public bool CanAttackTribe(long actor,long tribe){var target=Root(Tribe(tribe));if(target==null)return false;var mine=Root(ForActor(actor));if(mine==null)return true;return mine.id!=target.id&&!Allied(mine.id,target.id);}
        public bool OrderLive(TribeOrder order){if(order.cancelled||Tribe(order.issuerTribe)==null||ForActor(order.issuer)?.id!=order.issuerTribe||!CanGovern(order.issuer,order.targetTribe))return false;return order.kind!=TribeOrderKind.Attack||CanAttackTribe(order.issuer,order.hostileTribe);}
        public bool ReceiptLive(TribeOrder order,TribeOrderReceipt receipt)=>OrderLive(order)&&Contains(order.issuerTribe,receipt.tribe)&&Contains(order.targetTribe,receipt.tribe);
        public bool OrderVisible(long actor,TribeOrder order){var mine=ForActor(actor);return mine!=null&&(Contains(mine.id,order.issuerTribe)||order.recipients.Exists(r=>r.tribe==mine.id)&&Contains(order.issuerTribe,mine.id));}
        internal void Audit(long tick,long actor,string text){revision++;history.Add(new TribeAudit{tick=tick,actor=actor,text=text});if(history.Count>256)history.RemoveAt(0);}
        internal void InvalidateInvitations(long tribe){invitations.RemoveAll(i=>i.from==tribe||i.to==tribe);}
    }
    public sealed partial class IndustrialActor {public string displayName;public bool governmentTestActor;public long playerCombatSequence,playerCombatRevision;}
    public sealed partial class IndustrialWorld {
        public TribeGovernment government=new TribeGovernment();
        public string PlayerName(long actor){var who=Actor(actor);return string.IsNullOrWhiteSpace(who?.displayName)?"Player "+actor:who.displayName;}
        public string ConstructionPolicyError(long actor){var tribe=government.ForActor(actor);return tribe!=null&&government.Root(tribe).rules.officerConstruction&&tribe.Member(actor).role==TribeRole.Member?"Your ruler restricts construction to officers and leaders":null;}
        public string CombatPolicyError(long actor){var tribe=government.ForActor(actor);return tribe!=null&&government.Root(tribe).rules.officerCombat&&tribe.Member(actor).role==TribeRole.Member?"Your ruler restricts combat to officers and leaders":null;}
        static bool GovernmentPlainText(string text,bool multiline=false){if(text==null||text.IndexOfAny(new[]{'<','>'})>=0)return false;foreach(char value in text)if(char.IsControl(value)&&(!multiline||value!='\n'&&value!='\r'))return false;return true;}
        static bool GovernmentName(string text,int max)=>!string.IsNullOrWhiteSpace(text)&&text.Trim().Length<=max&&GovernmentPlainText(text);
        string PrepareGovernment(IndustrialActor actor,IndustrialCommand c,out Action apply){
            apply=null;var g=government;var mine=g.ForActor(actor.id);var target=g.Tribe(c.target);var root=g.Root(mine);string description=null;Action change=null;
            switch(c.kind){
                case "tribe-profile":
                    if(!GovernmentName(c.item,32))return "Use a player name of 1–32 characters";change=()=>actor.displayName=c.item.Trim();description="Updated their player name";break;
                case "tribe-create":case "tribe-test-create":
                    bool test=c.kind=="tribe-test-create";if(test&&(!creativeMode||actor.id!=1))return "Only the creative host can create test tribes";if(!test&&mine!=null)return "Leave your current tribe first";
                    if(g.tribes.Count>=TribeGovernment.MaximumTribes)return "Tribe budget reached (256)";if(!GovernmentName(c.item,48))return "Use a tribe name of 1–48 characters";if(g.tribes.Exists(t=>string.Equals(t.name,c.item.Trim(),StringComparison.OrdinalIgnoreCase)))return "That tribe name is already used";
                    if(test&&actors.FindAll(a=>a.governmentTestActor).Count>=16)return "Creative test leader limit reached (16)";
                    change=()=>{long leader=actor.id;if(test){leader=1000000000+AllocateId();while(Actor(leader)!=null)leader=1000000000+AllocateId();actors.Add(new IndustrialActor{id=leader,displayName=c.item.Trim()+" test leader",governmentTestActor=true,pose=actor.pose});}var tribe=new TribeRecord{id=AllocateId(),name=c.item.Trim()};tribe.members.Add(new TribeMember{actor=leader,role=TribeRole.Leader});g.tribes.Add(tribe);if(!test)g.invitations.RemoveAll(i=>i.kind==TribeInvitationKind.Member&&i.player==actor.id);};description="Founded "+c.item.Trim()+(test?" (creative test tribe)":"");break;
                case "tribe-rename":
                    if(!g.LeaderOf(actor.id,mine))return "Only your tribe leader can rename it";if(!GovernmentName(c.item,48)||g.tribes.Exists(t=>t.id!=mine.id&&string.Equals(t.name,c.item.Trim(),StringComparison.OrdinalIgnoreCase)))return "Choose an unused tribe name of 1–48 characters";change=()=>mine.name=c.item.Trim();description="Renamed tribe to "+c.item.Trim();break;
                case "tribe-invite-member":case "tribe-invite-alliance":case "tribe-invite-subject":
                    if(mine==null||mine.Member(actor.id).role==TribeRole.Member)return "An officer or leader must send invitations";if(g.invitations.Count>=TribeGovernment.MaximumInvitations)return "Invitation budget reached (256); cancel old invitations";
                    var kind=c.kind=="tribe-invite-member"?TribeInvitationKind.Member:c.kind=="tribe-invite-alliance"?TribeInvitationKind.Alliance:TribeInvitationKind.Subordination;
                    if(kind==TribeInvitationKind.Member){if(Actor(c.target)==null||g.ForActor(c.target)!=null||mine.members.Count>=TribeGovernment.MaximumMembers)return "Choose an unaligned registered player; tribe capacity is 64 members";}
                    else {if(!g.Sovereign(actor.id))return "Only an independent ruling tribe's leader manages foreign diplomacy";if(target==null||target.id==mine.id||target.parent!=0)return "Choose another independent tribe";if(g.Allied(mine.id,target.id)&&kind==TribeInvitationKind.Alliance)return "Already allied as equals";}
                    if(g.invitations.Exists(i=>i.kind==kind&&i.from==mine.id&&(kind==TribeInvitationKind.Member?i.player==c.target:i.to==c.target)))return "Invitation already pending";
                    change=()=>g.invitations.Add(new TribeInvitation{id=AllocateId(),from=mine.id,to=kind==TribeInvitationKind.Member?0:c.target,player=kind==TribeInvitationKind.Member?c.target:0,createdAtTick=tick,kind=kind});description=kind==TribeInvitationKind.Member?"Invited "+PlayerName(c.target)+" to "+mine.name:kind==TribeInvitationKind.Alliance?"Proposed an equal alliance to "+target.name:"Invited "+target.name+" to become subjects of "+mine.name;break;
                case "tribe-respond":case "tribe-test-respond":
                    var invite=g.invitations.Find(i=>i.id==c.source);if(invite==null||c.count<0||c.count>1)return "Invitation is no longer available";var sender=g.Tribe(invite.from);var recipient=g.Tribe(invite.to);var leader=invite.kind==TribeInvitationKind.Member?Actor(invite.player):Actor(recipient?.Leader??0);
                    if(c.kind=="tribe-test-respond"){if(actor.id!=1||!creativeMode||leader?.governmentTestActor!=true)return "Creative test responses apply only to test leaders";}
                    else if(invite.kind==TribeInvitationKind.Member?invite.player!=actor.id:!g.LeaderOf(actor.id,recipient))return "Only the invited player or receiving tribe leader can respond";
                    if(c.count==0){change=()=>g.invitations.Remove(invite);description="Declined an invitation";break;}
                    if(sender==null)return "Inviting tribe no longer exists";
                    if(invite.kind==TribeInvitationKind.Member){if(leader==null||g.ForActor(leader.id)!=null||sender.members.Count>=TribeGovernment.MaximumMembers)return "Player membership or tribe capacity changed";change=()=>{sender.members.Add(new TribeMember{actor=leader.id});g.invitations.RemoveAll(i=>i.kind==TribeInvitationKind.Member&&i.player==leader.id);};description=PlayerName(leader.id)+" joined "+sender.name;break;}
                    if(sender.parent!=0||recipient==null||recipient.parent!=0||sender.id==recipient.id)return "Diplomatic status changed; request a fresh invitation";
                    if(invite.kind==TribeInvitationKind.Alliance){if(g.Allied(sender.id,recipient.id))return "Already allied";change=()=>{g.alliances.Add(new TribeAlliance{first=sender.id,second=recipient.id});g.invitations.RemoveAll(i=>i.kind==TribeInvitationKind.Alliance&&(i.from==sender.id&&i.to==recipient.id||i.to==sender.id&&i.from==recipient.id));};description=sender.name+" and "+recipient.name+" formed an equal alliance";}
                    else {if(g.Contains(recipient.id,sender.id)||g.Depth(sender)+g.BranchHeight(recipient.id)>TribeGovernment.MaximumDepth)return "Hierarchy would cycle or exceed eight levels";change=()=>{recipient.parent=sender.id;g.alliances.RemoveAll(a=>a.first==recipient.id||a.second==recipient.id);g.InvalidateInvitations(recipient.id);g.invitations.Remove(invite);};description=recipient.name+" became subjects of "+sender.name+"; old independent alliances ended";}break;
                case "tribe-cancel-invite":
                    var pending=g.invitations.Find(i=>i.id==c.source);if(pending==null||mine==null||pending.from!=mine.id||mine.Member(actor.id).role==TribeRole.Member)return "Only the inviting tribe's leadership can cancel";change=()=>g.invitations.Remove(pending);description="Cancelled an invitation";break;
                case "tribe-role":case "tribe-transfer-leadership":case "tribe-kick":
                    if(!g.LeaderOf(actor.id,mine))return "Only the tribe leader manages ranks and membership";var member=mine.Member(c.target);if(member==null||member.actor==actor.id)return "Choose another member of your own tribe";
                    if(c.kind=="tribe-kick"){change=()=>mine.members.Remove(member);description="Removed "+PlayerName(member.actor)+" from "+mine.name;}
                    else if(c.kind=="tribe-transfer-leadership"){change=()=>{mine.Member(actor.id).role=TribeRole.Officer;member.role=TribeRole.Leader;};description="Transferred leadership to "+PlayerName(member.actor);}
                    else {if(!Enum.TryParse(c.item,false,out TribeRole role)||role==TribeRole.Leader||!Enum.IsDefined(typeof(TribeRole),role))return "Choose Member or Officer; leadership has a separate transfer action";change=()=>member.role=role;description="Assigned "+PlayerName(member.actor)+" rank "+role;}break;
                case "tribe-leave":
                    if(mine==null)return "You are independent";if(g.LeaderOf(actor.id,mine))return "Transfer leadership or explicitly disband your tribe first";change=()=>mine.members.RemoveAll(m=>m.actor==actor.id);description="Left "+mine.name;break;
                case "tribe-disband":
                    if(!g.LeaderOf(actor.id,mine)||mine.members.Count!=1||g.tribes.Exists(t=>t.parent==mine.id))return "Disband requires a leader-only tribe with no subjects";if(mine.parent!=0&&!root.rules.allowSecession)return "Your ruler must release this tribe before it can disband";if(g.orders.Exists(o=>o.issuerTribe==mine.id))return "Archive your issued orders before disbanding";change=()=>{g.alliances.RemoveAll(a=>a.first==mine.id||a.second==mine.id);g.InvalidateInvitations(mine.id);g.tribes.Remove(mine);};description="Disbanded "+mine.name;break;
                case "tribe-break-alliance":
                    if(!g.Sovereign(actor.id)||target==null||!g.Allied(mine.id,target.id))return "Only a sovereign leader can end their realm's alliance";change=()=>g.alliances.RemoveAll(a=>a.first==mine.id&&a.second==target.id||a.second==mine.id&&a.first==target.id);description="Ended alliance with "+target.name;break;
                case "tribe-release":case "tribe-reparent":
                    if(!g.Sovereign(actor.id))return "Only the sovereign leader can restructure the realm";var branch=g.Tribe(c.source);if(branch==null||branch.id==mine.id||!g.Contains(mine.id,branch.id))return "Choose a subordinate branch of your realm";
                    if(c.kind=="tribe-release"){change=()=>{branch.parent=0;g.InvalidateInvitations(branch.id);};description="Released "+branch.name+" as independent";}
                    else {if(target==null||!g.Contains(mine.id,target.id)||g.Contains(branch.id,target.id)||g.Depth(target)+g.BranchHeight(branch.id)>TribeGovernment.MaximumDepth)return "Choose a parent in your realm without creating a cycle or exceeding eight levels";change=()=>branch.parent=target.id;description="Placed "+branch.name+" under "+target.name;}break;
                case "tribe-secede":
                    if(!g.LeaderOf(actor.id,mine)||mine.parent==0||!root.rules.allowSecession)return "Your ruler must permit secession or release your tribe";change=()=>{mine.parent=0;g.InvalidateInvitations(mine.id);};description=mine.name+" declared permitted independence";break;
                case "tribe-rule":case "tribe-standing-orders":
                    if(!g.Sovereign(actor.id))return "Only the sovereign leader sets inherited realm policy";
                    if(c.kind=="tribe-standing-orders"){if(c.item==null||c.item.Length>280||!GovernmentPlainText(c.item,true))return "Standing orders must be at most 280 plain-text characters";change=()=>mine.rules.standingOrders=c.item;description="Updated standing orders for the realm";}
                    else {if(c.count<0||c.count>1)return "Invalid policy value";bool value=c.count==1;switch(c.item){case "friendly-fire":change=()=>mine.rules.friendlyFire=value;break;case "officer-construction":change=()=>mine.rules.officerConstruction=value;break;case "officer-combat":change=()=>mine.rules.officerCombat=value;break;case "allow-secession":change=()=>mine.rules.allowSecession=value;break;default:return "Unknown realm policy";}description="Changed realm policy "+c.item+" to "+value;}break;
                case "tribe-order":
                    var assigned=g.Tribe(c.source);if(assigned==null||!g.CanGovern(actor.id,assigned.id))return "You can order your own tribe and its subjects, never an equal ally";
                    if(!Enum.TryParse(c.operationRole,false,out TribeOrderKind orderKind)||!Enum.IsDefined(typeof(TribeOrderKind),orderKind)||!GovernmentName(c.item,120))return "Choose an order type and 1–120 character objective";
                    if(g.orders.Count>=TribeGovernment.MaximumOrders)return "Order board full (256); archive resolved orders";if(c.count<0||c.count>1)return "Invalid propagation option";if(!Valid(c.pose)||c.pose.frame!=0)return "Orders need a canonical planet-relative objective location";
                    if(orderKind==TribeOrderKind.Attack&&!g.CanAttackTribe(actor.id,c.target))return "Attack orders cannot target your realm or its allies";
                    change=()=>{var order=new TribeOrder{id=AllocateId(),issuer=actor.id,issuerTribe=mine.id,targetTribe=assigned.id,hostileTribe=orderKind==TribeOrderKind.Attack?c.target:0,kind=orderKind,text=c.item.Trim(),location=c.pose,createdAtTick=tick};foreach(var tribe in g.tribes)if(tribe.id==assigned.id||c.count==1&&g.Contains(assigned.id,tribe.id))order.recipients.Add(new TribeOrderReceipt{tribe=tribe.id});g.orders.Add(order);};description="Issued "+orderKind+" orders to "+assigned.name+(c.count==1?" and its subjects":"");break;
                case "tribe-order-status":case "tribe-order-cancel":case "tribe-order-archive":
                    var task=g.orders.Find(o=>o.id==c.source);if(task==null||!g.OrderVisible(actor.id,task))return "Order is unavailable to your tribe";
                    bool command=g.CanGovern(actor.id,task.issuerTribe);
                    if(c.kind=="tribe-order-cancel"){if(!command)return "Only the issuing chain of command can cancel";change=()=>task.cancelled=true;description="Cancelled order #"+task.id;}
                    else if(c.kind=="tribe-order-archive"){if(!command||g.OrderLive(task)&&task.recipients.Exists(r=>g.ReceiptLive(task,r)&&r.status!=TribeOrderStatus.Completed))return "Only command can archive a cancelled, revoked or fully completed order";change=()=>g.orders.Remove(task);description="Archived order #"+task.id;}
                    else {if(!g.OrderLive(task)||mine.Member(actor.id).role==TribeRole.Member||!Enum.TryParse(c.item,false,out TribeOrderStatus status)||(status!=TribeOrderStatus.Acknowledged&&status!=TribeOrderStatus.Completed))return "Your leadership can acknowledge or complete an active assigned order";var receipt=task.recipients.Find(r=>r.tribe==mine.id);if(receipt==null||!g.ReceiptLive(task,receipt)||receipt.status==TribeOrderStatus.Completed||status==TribeOrderStatus.Acknowledged&&receipt.status!=TribeOrderStatus.Pending)return "This receipt cannot move backwards or is not assigned to your tribe";change=()=>{receipt.status=status;receipt.actor=actor.id;};description="Marked order #"+task.id+" "+status;}break;
                default:return "Unknown tribe action";
            }
            apply=()=>{change();g.Audit(tick,actor.id,description);};return null;
        }
    }
    [Serializable] public sealed class TribeCombatDamage {public long attacker,victim,sequence;public double damage;}
    public sealed partial class IndustrialAuthority {
        // Trusted hit-resolution boundary; deliberately not exposed as a raw player command.
        public string ApplyPlayerCombatDamage(long attacker,long victim,long sequence,double damage,double maximumRange=4000){lock(gate){var from=World.Actor(attacker);var to=World.Actor(victim);if(from==null||to==null||from.health<=0||to.health<=0||sequence<=from.playerCombatSequence||double.IsNaN(damage)||double.IsInfinity(damage)||damage<=0||damage>1000||double.IsNaN(maximumRange)||double.IsInfinity(maximumRange)||maximumRange<0||maximumRange>4000||from.pose.Distance(to.pose)>maximumRange)return "Invalid or stale combat hit";string policy=World.CombatPolicyError(attacker);if(policy!=null)return policy;if(!World.government.CanAttack(attacker,victim))return "Friendly-fire protection: same realm or allied tribes";var hit=new TribeCombatDamage{attacker=attacker,victim=victim,sequence=sequence,damage=damage};Append(new IndustrialJournalEntry{playerCombat=hit});ApplyPlayerCombat(World,hit);return null;}}
        static void ApplyPlayerCombat(IndustrialWorld world,TribeCombatDamage hit){var attacker=world.Actor(hit.attacker);var victim=world.Actor(hit.victim);if(attacker==null||victim==null)throw new InvalidOperationException("Missing actor for combat journal");if(hit.sequence<=attacker.playerCombatSequence)return;victim.health=Math.Max(0,victim.health-hit.damage);victim.playerCombatRevision++;attacker.playerCombatSequence=hit.sequence;}
    }
}
