using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum CrimeActivity {None,Begging,Dealer,Mugger,Burglar,Robber,IndependentSexWorker,ExploitedSexWorker,Exploiter}
    public enum CustodyStage {Free,CountyJail,AwaitingCourt,AwaitingPrison,FederalPrison}
    public enum Offence {Possession,Dealing,Theft,Robbery,Assault,OfficerMurder,Murder,Exploitation,Escape}
    public enum SocialTieKind {Friend,Rival,Gang}
    [Serializable] public sealed class SocialTie {public long resident;public SocialTieKind kind;public float strength;}
    [Serializable] public sealed class ResidentCrime {
        public CrimeActivity activity;public CustodyStage custody;public double alcoholAddiction,drugAddiction,hardship,unemployedDays,rehabilitation;
        public long gang,exploiter,facility;public int weapons,convictions,ammoPurchased;public double contraband,wanted,courtDue,releaseAt,fleeUntil;public string reason="";
        public List<SocialTie> ties=new List<SocialTie>();
    }
    [Serializable] public sealed class CrimeCase {
        public long id,suspect,victim,officer;public Offence offence;public Vector2 place;public double time,evidence,hearingAt,sentence;public bool reported,arrested,heard,convicted,closed;public string outcome="";
    }
    [Serializable] public sealed class GangState {public long id,leader;public string name;public List<long> members=new List<long>();public double reputation;}
    [Serializable] public sealed class BlackMarketBusiness {
        public long id,owner,building,gang;public bool owned,shutDown;public string goods="illicit_compounds";public double rent=2,sales,produced;public ResourceInventory stock=new ResourceInventory();
    }
    [Serializable] public sealed class PrisonPlan {
        public long building,yard;public List<long> wings=new List<long>();public string decision="Assess demand";public double nextThought;public int capacity=16;
    }
    [Serializable] public sealed class CrimeState {
        [NonSerialized]public int socialCount=-1;
        public long lastDay=-1;public double nextJustice,nextPrisonThought;public bool prisonPlanner=true;public int searches,arrests,reportedCrimes,discoveries;
        public List<CrimeCase> cases=new List<CrimeCase>();public List<GangState> gangs=new List<GangState>();public List<BlackMarketBusiness> businesses=new List<BlackMarketBusiness>();public List<PrisonPlan> prisons=new List<PrisonPlan>();
    }
    public sealed partial class CivilisationRuntime {
        public const string CountyJail="civil_county_jail",Court="civil_courthouse",FederalPrison="civil_federal_prison",PrisonWing="civil_prison_wing",PrisonYard="civil_prison_yard";
        internal static bool Confined(ResidentState p)=>p.crime!=null&&p.crime.custody!=CustodyStage.Free;
        // A probability, never a class label. Affluent opportunity remains possible;
        // deprivation, addiction and recruitment increase risk independently.
        internal static double CriminalRisk(ColonyState c,ResidentState p){
            if(p.age<18||!p.alive||Confined(p))return 0;var x=p.crime??new ResidentCrime();double maturity=Math.Clamp((c.population-60)/180.0,0,1);
            return maturity*Math.Clamp(.002+(1-Math.Clamp(p.savings/120,0,1))*.018+x.hardship*.025+x.alcoholAddiction*.018+x.drugAddiction*.045+(x.gang!=0?.025:0)+(1-p.reputation)*.008,0,.18);
        }
        internal void EnsureSocialTies(ColonyState c){
            if(c.crime.socialCount==c.residents.Count)return;c.crime.socialCount=c.residents.Count;
            for(int i=0;i<c.residents.Count;i++){var p=c.residents[i];p.crime??=new ResidentCrime();if(p.crime.ties.Count>0||c.residents.Count<2)continue;
                // Bounded named links supplement existing family/partner/parent ids.
                for(int k=1;k<=3;k++){int index=(i+k*17)%c.residents.Count;var other=c.residents[index];if(other==p)continue;p.crime.ties.Add(new SocialTie{resident=other.id,kind=k==3?SocialTieKind.Rival:SocialTieKind.Friend,strength=.3f+(float)Random(p.id,"tie:"+other.id)*.6f});}
            }
        }
        internal void TickCrime(PlanetRtsBase local,ColonyState c){
            c.crime??=new CrimeState();EnsureSocialTies(c);long day=CivicDay(SimulationTime);if(c.crime.lastDay<0)c.crime.lastDay=day;
            int days=(int)Math.Min(8,Math.Max(0,day-c.crime.lastDay));for(int n=0;n<days;n++){long current=++c.crime.lastDay;CrimeDay(local,c,current);}
            if(SimulationTime>=c.crime.nextJustice){c.crime.nextJustice=SimulationTime+10;IssuePoliceEquipment(local,c);TickJustice(local,c);PlanPrisons(local,c);}
        }
        void CrimeDay(PlanetRtsBase local,ColonyState c,long day){
            double daySeconds=Math.Max(60,Settings.settlementDaySeconds);
            foreach(var p in c.residents){if(!p.alive||p.age<18)continue;var x=p.crime;x.unemployedDays=p.workplace==0?x.unemployedDays+1:0;
                var h=Family(c,p.household);x.hardship=Math.Clamp((p.savings<8?.4:0)+(h==null||h.home==0?.25:0)+(h!=null&&h.foodAccess<.5?.25:0)+Math.Min(.2,x.unemployedDays*.025),0,1);
                if(Confined(p)){x.rehabilitation=Math.Min(1,x.rehabilitation+.015);x.alcoholAddiction=Math.Max(0,x.alcoholAddiction-.025);x.drugAddiction=Math.Max(0,x.drugAddiction-.02);continue;}
                // Adult legal consumption requires available goods and a conserved payment.
                if(Random(p.id,"alcohol:"+day)<.12&&TryLegalPurchase(c,p,"alcohol",.2,1.5)){x.alcoholAddiction=Math.Min(1,x.alcoholAddiction+.04);}else x.alcoholAddiction=Math.Max(0,x.alcoholAddiction-.008);
                x.drugAddiction=Math.Max(0,x.drugAddiction-.004*(1+x.rehabilitation));
                if(x.alcoholAddiction+x.drugAddiction>1.25&&Random(p.id,"absence:"+day)<.25){p.workplace=0;p.wage=0;x.reason="Lost employment after repeated substance-related absence";}
                // Friends can help through hardship; credits move between real accounts.
                if(p.savings<3)foreach(var tie in x.ties){var friend=Person(c,tie.resident);if(tie.kind!=SocialTieKind.Friend||friend==null||!friend.alive||friend.savings<40)continue;friend.savings-=1;p.savings+=1;break;}
                if(x.activity==CrimeActivity.None&&Random(p.id,"crime:"+day)<CriminalRisk(c,p)){
                    double choice=Random(p.id,"offence:"+day);x.activity=choice<.18?CrimeActivity.Begging:choice<.40?CrimeActivity.Dealer:choice<.65?CrimeActivity.Mugger:choice<.85?CrimeActivity.Burglar:CrimeActivity.Robber;
                    x.reason="Financial pressure, personal choices and local opportunity";
                }
                // Independent adult sex work is lawful by default. Exploitation is an offence.
                if(x.activity==CrimeActivity.None&&x.hardship>.35&&Random(p.id,"adult-work:"+day)<.012)x.activity=CrimeActivity.IndependentSexWorker;
                if(x.activity==CrimeActivity.None&&x.hardship<.2&&p.savings>100&&Random(p.id,"organiser:"+day)<CriminalRisk(c,p)*.2)x.activity=CrimeActivity.Exploiter;
                if(x.activity==CrimeActivity.Exploiter)foreach(var other in c.residents){if(other.age<18||!other.alive||other==p||other.crime.activity!=CrimeActivity.IndependentSexWorker||other.crime.hardship<.6)continue;if(Random(other.id,"coercion:"+day)<.08){other.crime.activity=CrimeActivity.ExploitedSexWorker;other.crime.exploiter=p.id;break;}}
                if(x.activity==CrimeActivity.Dealer&&c.crime.businesses.Find(b=>b.owner==p.id&&!b.shutDown)==null)LeaseBlackMarket(local,c,p);
                if(x.activity!=CrimeActivity.None&&x.activity!=CrimeActivity.Begging&&x.activity!=CrimeActivity.IndependentSexWorker&&x.activity!=CrimeActivity.ExploitedSexWorker&&x.gang==0&&Random(p.id,"gang:"+day)<.12)RecruitGang(c,p,day);
                if(x.activity!=CrimeActivity.None&&x.activity!=CrimeActivity.Exploiter&&p.workplace!=0&&x.hardship<.25&&x.alcoholAddiction+x.drugAddiction<.35&&Random(p.id,"desist:"+day)<.12){x.activity=CrimeActivity.None;x.reason="Returned to stable work";}
            }
            foreach(var b in c.crime.businesses){b.stock.Bind(Catalogue);var owner=Person(c,b.owner);var site=SocietyBuilding(local,b.building);if(owner==null||!owner.alive||Confined(owner)||site==null||!site.Complete||!site.powered||site.paused||b.shutDown)continue;
                var inventory=SettlementLogistics.Find(c,b.building)?.inventory;if(inventory==null)continue;var enterprise=c.enterprises.Find(e=>e.building==b.building);double rent=b.owned?0:Math.Min(owner.savings,b.rent);owner.savings-=rent;if(enterprise!=null)enterprise.balance+=rent;else c.council.treasury+=rent;if(!b.owned&&rent+1e-6<b.rent)continue;
                // Fictional, aggregate game commodities; no real chemical process.
                double batch=Math.Min(4,Math.Min(inventory.Get("industrial_chemicals"),owner.savings/4));if(batch>0&&inventory.Spend("industrial_chemicals",batch)){owner.savings-=batch*4;c.council.treasury+=batch*4;double made=b.stock.Deposit("illicit_compounds",batch);b.produced+=made;}
                double carry=Math.Min(4-owner.crime.contraband,b.stock.Get("illicit_compounds"));if(carry>0&&b.stock.Spend("illicit_compounds",carry))owner.crime.contraband+=carry;
                if(owner.savings>=12&&inventory.Get("steel")>=1&&inventory.Get("machine_parts")>=1){inventory.Spend("steel",1);inventory.Spend("machine_parts",1);owner.savings-=12;c.council.treasury+=12;b.stock.Deposit("illegal_weapons",1);b.stock.Deposit("ammunition",12);}
                if(owner.crime.weapons==0&&owner.savings>=20&&b.stock.Get("illegal_weapons")>=1&&b.stock.Spend("illegal_weapons",1)){owner.savings-=20;c.council.treasury+=20;owner.crime.weapons=1;}
            }
            foreach(var p in c.residents)if(p.alive&&p.age>=18&&!Confined(p)&&p.crime.activity!=CrimeActivity.None&&p.crime.gang!=0){if(p.crime.weapons==0)TryBlackMarketPurchase(c,p,"illegal_weapons",1);if(p.crime.ammoPurchased<65500)TryBlackMarketPurchase(c,p,"ammunition",6);}
            c.societyRevision++;
        }
        void IssuePoliceEquipment(PlanetRtsBase local,ColonyState c){
            foreach(var b in local.Buildings)if(b.team==c.faction&&b.definition.id==PoliceHeadquarters&&b.Complete&&!b.removed){var item=SettlementLogistics.Find(c,b.persistentId);if(item==null)continue;if(!item.serviceEquipmentIssued){item.inventory.Deposit("service_weapons",8);item.inventory.Deposit("ammunition",240);item.serviceEquipmentIssued=true;}
                foreach(var p in c.residents)if(p.alive&&p.role==CivilianRole.Officer&&p.workplace==b.persistentId&&!Confined(p)){if(p.crime.weapons==0&&item.inventory.Spend("service_weapons",1))p.crime.weapons=1;if(p.crime.ammoPurchased<65500&&item.inventory.Spend("ammunition",12))p.crime.ammoPurchased+=12;}
            }
        }
        internal bool TryBlackMarketPurchase(ColonyState c,ResidentState buyer,string resource,double amount){
            if(!buyer.alive||buyer.age<18||Confined(buyer)||amount<=0)return false;double price=resource=="illegal_weapons"?20:resource=="ammunition"?1:6;
            foreach(var b in c.crime.businesses){var seller=Person(c,b.owner);if(b.shutDown||seller==null||!seller.alive||Confined(seller)||buyer.savings<price*amount)continue;b.stock.Bind(Catalogue);if(!b.stock.Spend(resource,amount))continue;buyer.savings-=price*amount;seller.savings+=price*amount;b.sales+=price*amount;if(resource=="illegal_weapons")buyer.crime.weapons+=(int)amount;else if(resource=="ammunition")buyer.crime.ammoPurchased+=(int)amount;else if(resource=="illicit_compounds")buyer.crime.contraband+=amount;return true;}
            return false;
        }
        internal bool TryLegalPurchase(ColonyState c,ResidentState buyer,string resource,double amount,double price){if(amount<=0||price<0||buyer.savings<amount*price||!c.inventory.Spend(resource,amount))return false;buyer.savings-=amount*price;c.council.treasury+=amount*price;return true;}
        internal bool LeaseBlackMarket(PlanetRtsBase local,ColonyState c,ResidentState p){
            if(p.age<18||!p.alive||Confined(p)||p.savings<5)return false;RtsBuilding site=null;
            foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.Complete&&b.powered&&(b.definition.id=="civil_warehouse"||b.definition.id=="factory")&&!c.crime.businesses.Exists(x=>x.building==b.persistentId&&!x.shutDown)){site=b;break;}
            if(site==null){var home=SocietyBuilding(local,Family(c,p.household)?.home??0);if(home!=null&&!c.crime.businesses.Exists(x=>x.building==home.persistentId&&!x.shutDown))site=home;}
            if(site==null)return false;var enterprise=c.enterprises.Find(e=>e.building==site.persistentId);var h=Family(c,p.household);var business=new BlackMarketBusiness{id=State.AllocateId(),owner=p.id,building=site.persistentId,gang=p.crime.gang,owned=enterprise?.owner==p.id||h?.home==site.persistentId&&h.owner==p.id};business.stock.Bind(Catalogue);c.crime.businesses.Add(business);return true;
        }
        void RecruitGang(ColonyState c,ResidentState p,long day){
            GangState gang=null;foreach(var tie in p.crime.ties){var friend=Person(c,tie.resident);if(friend?.crime.gang>0){gang=c.crime.gangs.Find(g=>g.id==friend.crime.gang);if(gang!=null)break;}}
            if(gang==null){gang=new GangState{id=State.AllocateId(),leader=p.id,name="The "+(new[]{"Ash","Dock","Iron","Night","Orion"})[(int)(Random(p.id,"gang-name")*5)]+" Circle"};c.crime.gangs.Add(gang);}p.crime.gang=gang.id;if(!gang.members.Contains(p.id))gang.members.Add(p.id);
            foreach(long id in gang.members)if(id!=p.id){var friend=Person(c,id);if(friend!=null&&!p.crime.ties.Exists(t=>t.resident==id&&t.kind==SocialTieKind.Gang)&&p.crime.ties.Count<12)p.crime.ties.Add(new SocialTie{resident=id,kind=SocialTieKind.Gang,strength=.8f});}
        }
        internal long CrimeDestination(PlanetRtsBase local,ColonyState c,ResidentState p,double time){
            var x=p.crime;if(x==null)return 0;if(x.fleeUntil>time)return Family(c,p.household)?.home??0;if(Confined(p))return x.facility;if(p.age<18||x.activity==CrimeActivity.None)return 0;
            double hour=((time/Math.Max(60,Settings.settlementDaySeconds)*24+8)%24+24)%24;if(hour<16&&x.activity!=CrimeActivity.Begging||hour>=23)return 0;
            var home=SocietyBuilding(local,Family(c,p.household)?.home??0);if(home==null)return 0;RtsBuilding best=null;double distance=double.MaxValue;
            foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.Complete&&!string.IsNullOrEmpty(b.definition.retailResource)&&local.Roads.Connected(home,b)){double d=(CubeSphere.Decode(b.position)-CubeSphere.Decode(home.position)).sqrMagnitude;if(d<distance){distance=d;best=b;}}
            return best?.persistentId??0;
        }
        internal CrimeCase ReportCrime(ColonyState c,ResidentState suspect,ResidentState victim,ResidentState officer,Offence offence,Vector2 place,double time,bool reported=true){
            var record=new CrimeCase{id=State.AllocateId(),suspect=suspect?.id??0,victim=victim?.id??0,officer=officer?.id??0,offence=offence,place=place,time=time,evidence=officer!=null?.95:.65,reported=reported};c.crime.cases.Add(record);if(reported){c.crime.reportedCrimes++;if(suspect!=null)suspect.crime.wanted+=1;}c.societyRevision++;return record;
        }
        internal bool Arrest(PlanetRtsBase local,ColonyState c,ResidentState suspect,ResidentState officer,Vector2 place,double time){
            if(suspect==null||!suspect.alive||suspect.age<18||Confined(suspect))return false;RtsBuilding jail=null;foreach(var b in local.Buildings)if(b.team==c.faction&&(b.definition.id==CountyJail||b.definition.id==PoliceHeadquarters)&&CivicServiceReady(b)){int held=0;foreach(var p in c.residents)if(p.alive&&Confined(p)&&p.crime.facility==b.persistentId)held++;if(held<(b.definition.id==CountyJail?24:8)*b.level){jail=b;break;}}
            if(jail==null){suspect.crime.reason="Wanted; holding cells are full";return false;}var x=suspect.crime;var record=c.crime.cases.FindLast(q=>q.suspect==suspect.id&&q.reported&&!q.closed);if(record==null)record=ReportCrime(c,suspect,null,officer,x.activity==CrimeActivity.Exploiter?Offence.Exploitation:x.contraband>0?Offence.Dealing:Offence.Possession,place,time);
            record.arrested=true;record.officer=officer?.id??0;x.custody=CustodyStage.AwaitingCourt;x.facility=jail.persistentId;x.courtDue=time+Math.Max(60,Settings.settlementDaySeconds)*.25;x.contraband=0;x.weapons=0;suspect.workplace=0;suspect.wage=0;c.crime.arrests++;
            foreach(var business in c.crime.businesses)if(business.owner==suspect.id)business.shutDown=true;
            foreach(var p in c.residents)if(p.crime.exploiter==suspect.id){p.crime.exploiter=0;p.crime.activity=CrimeActivity.IndependentSexWorker;p.crime.rehabilitation=Math.Min(1,p.crime.rehabilitation+.2);}
            return true;
        }
        internal bool HoldCrimeBody(ColonyState c,ResidentState p){var d=c.deaths.Find(x=>x.resident==p.id);return d!=null&&(d.cause==DeathCause.Murder||d.cause==DeathCause.Robbery||d.cause==DeathCause.PoliceViolence)&&!d.collected;}
        internal void DiscoverBody(ColonyState c,long resident,long witness,double time){var d=c.deaths.Find(x=>x.resident==resident);if(d==null||d.discovered)return;d.discovered=true;d.discoveredAt=time;d.discoveredBy=witness;c.crime.discoveries++;foreach(var q in c.crime.cases)if(q.victim==resident&&!q.reported){q.reported=true;c.crime.reportedCrimes++;var criminal=Person(c,q.suspect);if(criminal!=null)criminal.crime.wanted+=2;}c.security.requestedPatrols=Math.Min(32,c.security.requestedPatrols+2);c.societyRevision++;}
        void TickJustice(PlanetRtsBase local,ColonyState c){
            var court=local.Buildings.Find(b=>b.team==c.faction&&b.definition.id==Court&&CivicServiceReady(b));
            foreach(var p in c.residents){if(!p.alive||!Confined(p))continue;var x=p.crime;var facility=SocietyBuilding(local,x.facility);if(facility==null){x.custody=CustodyStage.Free;x.facility=0;x.wanted+=1;x.reason="Escaped a destroyed detention facility";continue;}
                if(x.custody==CustodyStage.AwaitingCourt&&court!=null&&SimulationTime>=x.courtDue){var q=c.crime.cases.FindLast(v=>v.suspect==p.id&&v.arrested&&!v.heard);if(q==null)continue;q.heard=true;q.hearingAt=SimulationTime;q.convicted=Random(q.id,"judgment")<Math.Clamp(q.evidence,.1,.99);q.closed=true;
                    if(!q.convicted){x.custody=CustodyStage.Free;x.facility=0;x.wanted=0;q.outcome="Acquitted";continue;}
                    int days=q.offence==Offence.Murder||q.offence==Offence.OfficerMurder?12:q.offence==Offence.Robbery||q.offence==Offence.Exploitation?6:q.offence==Offence.Dealing?4:2;q.sentence=days;x.releaseAt=SimulationTime+days*Math.Max(60,Settings.settlementDaySeconds);x.custody=CustodyStage.AwaitingPrison;x.convictions++;x.wanted=0;q.outcome="Convicted; "+days+" days";
                }
                if(x.custody==CustodyStage.AwaitingPrison)foreach(var prison in local.Buildings)if(prison.team==c.faction&&(prison.definition.id==FederalPrison||prison.definition.id==PrisonWing)&&CivicServiceReady(prison)){int held=0;foreach(var inmate in c.residents)if(inmate.alive&&Confined(inmate)&&inmate.crime.facility==prison.persistentId)held++;if(held>=16*prison.level)continue;x.facility=prison.persistentId;x.custody=CustodyStage.FederalPrison;break;}
                if((x.custody==CustodyStage.FederalPrison||x.custody==CustodyStage.AwaitingPrison)&&SimulationTime>=x.releaseAt){x.custody=CustodyStage.Free;x.facility=0;x.activity=CrimeActivity.None;x.reason="Released; employment and support reduce reoffending";double help=Math.Min(5,c.council.treasury);c.council.treasury-=help;p.savings+=help;}
            }
            c.societyRevision++;
        }
    }
}
