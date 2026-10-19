using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum CivilianRole {Survivor,Worker,Gatherer,Engineer}
    public enum HomeTenure {OwnerOccupied,PrivateRental,SocialHousing}
    public enum CivicPriority {Education,Infrastructure,Defence}
    public enum ResidentRoutine {Home,Work,Shopping,School,Community}
    [Serializable] public sealed class GatheringRegion {public string key;public double timber=3000,scrap=1500;}
    [Serializable] public sealed class ResidentState {
        public long id,household,workplace,parentA,parentB,partner;public string name,background,relationship;
        public double age,education,reputation=.5,savings,wage,lastPay;
        public bool alive=true;public CivilianRole role;public int workShift;
    }
    [Serializable] public sealed class HouseholdState {
        public long id,home,owner,shopper;public string surname;public HomeTenure tenure;
        public double rent=2,paidRent,foodAccess;public List<long> members=new List<long>();
    }
    [Serializable] public sealed class EnterpriseState {
        public long id,building,owner;public string name;public double balance,wagesPaid,sales,dividends;
    }
    [Serializable] public sealed class CouncilState {
        public bool initialised;public double treasury,taxRate=.2,taxesCollected,educationSpent,welfareSpent,infrastructureSpent;
        public CivicPriority priority;public long elections;public string decision="Establish family housing and essential services";
    }
    public sealed partial class CivilisationRuntime {
        static readonly string[] FirstNames={"Alex","Sam","Jordan","Morgan","Charlie","Jamie","Robin","Taylor","Avery","Riley","Amelia","Oliver","Sofia","Noah","Isla","Ethan","Maya","Leo","Zara","Theo","Freya","Arlo","Grace","Oscar","Fatima","Yusuf","Anika","Dev","Mei","Jin","Ada","Felix"};
        static readonly string[] Surnames={"Walker","Patel","Hughes","Chen","Wilson","Khan","Reed","Morgan","Singh","Clarke","Silva","Evans","Brooks","Ahmed","Park","Green","Carter","Costa","Reyes","Bell","Young","Okafor","Shaw","Bennett"};
        static double Random(long id,string salt)=>PlanetResourceProfile.Random(id+":"+salt);
        internal static ResidentState Person(ColonyState c,long id)=>c.residents.Find(p=>p.id==id);
        internal static HouseholdState Family(ColonyState c,long id)=>c.households.Find(h=>h.id==id);
        internal static RtsBuilding SocietyBuilding(PlanetRtsBase local,long id)=>local.FindBuilding(id);
        internal static double Credits(ColonyState c){double sum=c.council.treasury;foreach(var p in c.residents)sum+=p.savings;foreach(var e in c.enterprises)sum+=e.balance;return sum;}
        internal void EnsureResidents(ColonyState c){
            if(!c.council.initialised){c.council.initialised=true;c.council.treasury=500;}
            if(c.residents.Count==0&&c.population>0){
                int count=c.population;for(int i=0;i<count;i++){
                    if(i%4==0)c.households.Add(NewFamily(c,c.households.Count%3));
                    var h=c.households[c.households.Count-1];int member=i%4;bool young=i<16;double age=young?(member==3?8+Random(c.id+i,"child")*8:member==2?64+Random(c.id+i,"grandparent")*6:34+Random(c.id+i,"parent")*8):member<2?46+Random(c.id+i,"parent")*8:18+Random(c.id+i,"adultChild")*6;
                    AddPerson(c,h,age);var person=c.residents[c.residents.Count-1];person.relationship=member<2?"Partner":member==2&&young?"Grandparent":"Child";
                    if(member==1){var spouse=Person(c,h.members[0]);person.partner=spouse.id;spouse.partner=person.id;}
                    if(member>=2&&!(member==2&&young)){person.parentA=h.members[0];person.parentB=h.members[1];}
                    if(member==2&&young)Person(c,h.members[0]).parentA=person.id;
                }
            }
            // Migration is explicit family reunification, rather than an invisible aggregate increment.
            while(c.residents.Count<c.population){HouseholdState h=null;foreach(var family in c.households)if(family.members.Count<6){h=family;break;}if(h==null){h=NewFamily(c,c.households.Count%3);c.households.Add(h);}AddPerson(c,h,18+Random(State.nextId,"arrival")*22);}
        }
        HouseholdState NewFamily(ColonyState c,int tenure){long id=State.AllocateId();return new HouseholdState{id=id,surname=Surnames[(int)(PlanetResourceProfile.Hash(c.body+":"+id)%(uint)Surnames.Length)],tenure=(HomeTenure)tenure};}
        void AddPerson(ColonyState c,HouseholdState h,double age){
            long id=State.AllocateId();double wealth=Random(h.id,"wealth")*.32+(h.tenure==HomeTenure.OwnerOccupied?.68:h.tenure==HomeTenure.PrivateRental?.34:0);
            var p=new ResidentState{id=id,household=h.id,relationship=age<18?"Child":"Adult relative",name=FirstNames[(int)(Random(id,"first")*FirstNames.Length)]+" "+h.surname,age=age,education=age<18?Math.Clamp((age-5)/22+wealth*.2,0,1):.15+wealth*.65,background=wealth<.33?"Social housing family":wealth<.7?"Working family":"Affluent family",reputation=.35+wealth*.3,savings=age==0?0:age<18?5:25+wealth*125,role=CivilianRole.Survivor,workShift=(int)(Random(id,"shift")*3)};
            if(age==0){foreach(long member in h.members){var parent=Person(c,member);if(parent!=null&&parent.alive&&parent.partner!=0&&parent.age>=22&&parent.age<42){p.parentA=parent.id;p.parentB=parent.partner;break;}}}
            c.residents.Add(p);h.members.Add(id);if(h.owner==0&&age>=18)h.owner=id;if(h.shopper==0&&age>=18)h.shopper=id;c.societyRevision++;
        }
        internal static void BindHouseholds(PlanetRtsBase local,ColonyState c){
            var used=new HashSet<long>();foreach(var h in c.households){var home=SocietyBuilding(local,h.home);if(home==null||home.roadSuspended||!home.Complete||home.definition.housingCapacity<=0||home.team!=c.faction||!used.Add(h.home))h.home=0;}
            foreach(var h in c.households)if(h.home==0){RtsBuilding preferred=null;foreach(var candidate in local.Buildings)if(candidate.team==c.faction&&!candidate.removed&&!candidate.roadSuspended&&candidate.Complete&&candidate.definition.housingCapacity>=h.members.Count&&!used.Contains(candidate.persistentId)){if(preferred==null)preferred=candidate;var tenure=candidate.definition.id=="civil_social_home"?HomeTenure.SocialHousing:candidate.definition.id=="civil_rental_home"?HomeTenure.PrivateRental:HomeTenure.OwnerOccupied;if(tenure==h.tenure){preferred=candidate;break;}}
                if(preferred!=null){var b=preferred;h.home=b.persistentId;used.Add(h.home);h.tenure=b.definition.id=="civil_social_home"?HomeTenure.SocialHousing:b.definition.id=="civil_rental_home"?HomeTenure.PrivateRental:HomeTenure.OwnerOccupied;
                    if(h.tenure==HomeTenure.PrivateRental){foreach(var owner in c.residents)if(owner.alive&&owner.age>=18&&owner.household!=h.id&&(h.owner==0||owner.savings>(Person(c,h.owner)?.savings??0)))h.owner=owner.id;}
                    c.societyRevision++;}
            }
        }
        internal static CivilianRole JobRole(BuildingDefinition d){if(d.extraction>0||d.id=="civil_gathering_camp"||d.id.StartsWith("civil_farm_"))return CivilianRole.Gatherer;if(d.category==BuildingCategory.Research||d.id=="civil_fabricator"||d.id=="civil_council"||d.id=="civil_school")return CivilianRole.Engineer;return CivilianRole.Worker;}
        internal static void MatchResidentJobs(PlanetRtsBase local,ColonyState c){
            var slots=new Dictionary<long,int>();foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.WorkersAssigned>0)slots[b.persistentId]=b.WorkersAssigned;
            bool changed=false;int eligible=c.workforce;
            // Retain valid jobs so commuters are not reassigned whenever a new factory opens.
            foreach(var p in c.residents){if(!p.alive||p.age<18||p.age>=75||eligible--<=0){if(p.workplace!=0){p.workplace=0;changed=true;}continue;}
                if(p.workplace!=0&&slots.TryGetValue(p.workplace,out int remaining)&&remaining>0){slots[p.workplace]=remaining-1;var employer=SocietyBuilding(local,p.workplace);var role=JobRole(employer.definition);double wage=role==CivilianRole.Engineer?18:role==CivilianRole.Gatherer?10:8;if(p.role!=role||p.wage!=wage)changed=true;p.role=role;p.wage=wage;}
                else if(p.workplace!=0){p.workplace=0;changed=true;}
            }
            foreach(var b in local.Buildings){if(!slots.TryGetValue(b.persistentId,out int remaining)||remaining==0)continue;var role=JobRole(b.definition);
                // Open qualifications: education and experience improve selection; background never forbids a job.
                while(remaining-->0){ResidentState best=null;double score=double.MinValue;foreach(var p in c.residents)if(p.alive&&p.age>=18&&p.age<75&&p.workplace==0){double value=p.education*(role==CivilianRole.Engineer?3:1)+p.reputation+Random(p.id,b.definition.id)*.1;if(value>score){score=value;best=p;}}if(best==null)break;
                    best.workplace=b.persistentId;best.role=role;best.wage=role==CivilianRole.Engineer?18:role==CivilianRole.Gatherer?10:8;changed=true;
                }
            }
            var projects=new List<RtsBuilding>();foreach(var b in local.Buildings)if(b.team==c.faction){b.constructionWorkers=0;if(!b.removed&&!b.paused&&!b.roadSuspended&&b.powered&&!b.awaitingProjectMaterials&&(!b.Complete||b.repairing&&b.health<b.MaxHealth))projects.Add(b);}
            if(projects.Count>1){var ordinals=new Dictionary<int,int>();for(int i=0;i<projects.Count;i++)ordinals[projects[i].index]=i;int start=(int)(local.Civilisation.SimulationTime/15)%projects.Count;projects.Sort((a,b)=>{int rank=ConstructionPriority(a).CompareTo(ConstructionPriority(b));return rank!=0?rank:((ordinals[a.index]-start+projects.Count)%projects.Count).CompareTo((ordinals[b.index]-start+projects.Count)%projects.Count);});}
            // Share builders one per project before assigning a second builder to any project.
            for(int pass=0;pass<2;pass++)foreach(var b in projects){ResidentState builder=null;foreach(var p in c.residents)if(p.alive&&p.age>=18&&p.age<75&&p.workplace==0&&(builder==null||p.education>builder.education))builder=p;if(builder==null||c.employed>=c.workforce)break;builder.workplace=b.persistentId;builder.role=b.definition.id=="wall"||b.definition.id=="civil_fence"||b.definition.id=="gate"?CivilianRole.Worker:CivilianRole.Engineer;builder.wage=12;b.constructionWorkers++;c.employed++;changed=true;}
            foreach(var p in c.residents)if(p.workplace==0){p.wage=0;p.role=CivilianRole.Survivor;}
            if(changed)c.societyRevision++;
        }
        public double LocalHour=>((SimulationTime/Math.Max(60,Settings.settlementDaySeconds)*24+8)%24+24)%24;
        internal double WorkStart(ResidentState p)=>7+Math.Clamp(p.workShift,0,2)*.5+Random(p.id,"departure")*.4;
        internal ResidentRoutine Routine(ColonyState c,ResidentState p,double time){
            double hour=((time/Math.Max(60,Settings.settlementDaySeconds)*24+8)%24+24)%24;
            if(hour<6.5||hour>=21)return ResidentRoutine.Home;
            if(p.age<18)return p.age>=5&&hour>=7.25&&hour<15?ResidentRoutine.School:ResidentRoutine.Home;
            double start=WorkStart(p),finish=start+8;
            if(p.workplace!=0&&hour>=start&&hour<finish)return ResidentRoutine.Work;
            var h=Family(c,p.household);double errands=p.workplace!=0?finish:10+Random(p.id,"errand")*3;
            if(h!=null&&h.shopper==p.id&&hour>=errands&&hour<errands+2)return ResidentRoutine.Shopping;
            // Deterministic appointments vary by person and day, never reroll every frame.
            long day=(long)Math.Floor(time/Math.Max(60,Settings.settlementDaySeconds));
            if(hour>=18+Random(p.id,"visit")&&hour<20&&Random(p.id,"civic:"+day)<.3)return ResidentRoutine.Community;
            return ResidentRoutine.Home;
        }
        internal long Destination(PlanetRtsBase local,ColonyState c,ResidentState p,double time){
            var h=Family(c,p.household);long home=h?.home??0;var routine=Routine(c,p,time);var start=SocietyBuilding(local,home);if(start==null)return 0;
            if(routine==ResidentRoutine.Home)return home;
            if(routine==ResidentRoutine.Work){var work=SocietyBuilding(local,p.workplace);return work!=null&&!work.paused&&work.powered&&local.Roads.Connected(start,work)?p.workplace:home;}
            long day=(long)Math.Floor(time/Math.Max(60,Settings.settlementDaySeconds));
            int errand=(int)(Random(p.id,"shop:"+day)*5);string resource=errand==0?"medicine":errand==1?"drinks":"food";
            long destination=Nearest(routine,resource);if(destination==home&&routine==ResidentRoutine.Shopping&&resource!="food")destination=Nearest(routine,"food");return destination;
            long Nearest(ResidentRoutine activity,string goods){RtsBuilding best=null;double distance=double.MaxValue;
                foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.Complete&&!b.paused&&b.powered&&(activity==ResidentRoutine.School?b.definition.id=="civil_school":activity==ResidentRoutine.Community?b.definition.id=="civil_council":b.definition.retailResource==goods)){
                    double value=CubeSphere.ArcDistance(CubeSphere.Decode(b.position),CubeSphere.Decode(start.position),local.World.definition.radius);
                    if(value<distance&&local.Roads.Connected(start,b)){distance=value;best=b;}}
                return best?.persistentId??home;
            }
        }
        internal void TickSociety(PlanetRtsBase local,ColonyState c,double step){
            // Biographies/economics run once per local day, inside the existing bounded catch-up scheduler.
            if(step>0){c.civicServiceSeconds+=step;c.civicFoodService+=c.retailFoodCoverage*step;c.civicDrinksService+=c.retailDrinksCoverage*step;c.civicMedicineService+=c.retailMedicineCoverage*step;}
            long day=(long)Math.Floor(SimulationTime/Math.Max(60,Settings.settlementDaySeconds));
            foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.Complete&&b.definition.workers>0&&!c.enterprises.Exists(e=>e.building==b.persistentId)){
                ResidentState owner=null;foreach(var p in c.residents)if(p.alive&&p.age>=18&&(owner==null||p.savings>owner.savings))owner=p;
                double capital=owner==null?0:Math.Min(100,owner.savings*.5);if(owner!=null)owner.savings-=capital;c.enterprises.Add(new EnterpriseState{id=State.AllocateId(),building=b.persistentId,owner=owner?.id??0,name=(owner==null?"Community":owner.name)+" / "+b.definition.name,balance=capital});
            }
            if(c.lastCivicDay<0){c.lastCivicDay=day;return;}if(day<=c.lastCivicDay)return;long firstDay=c.lastCivicDay+1;long days=Math.Min(8,day-c.lastCivicDay);c.lastCivicDay+=days;
            double foodCoverage=c.civicServiceSeconds>0?c.civicFoodService/c.civicServiceSeconds:c.retailFoodCoverage,drinkCoverage=c.civicServiceSeconds>0?c.civicDrinksService/c.civicServiceSeconds:c.retailDrinksCoverage,medicineCoverage=c.civicServiceSeconds>0?c.civicMedicineService/c.civicServiceSeconds:c.retailMedicineCoverage;
            c.civicServiceSeconds=c.civicFoodService=c.civicDrinksService=c.civicMedicineService=0;
            bool schoolAvailable=local.Buildings.Exists(b=>b.team==c.faction&&!b.removed&&b.Complete&&b.powered&&!b.paused&&b.definition.id=="civil_school"&&b.WorkersAssigned>0);
            for(int d=0;d<days;d++){long civicDay=firstDay+d;
                foreach(var p in c.residents)if(p.alive){p.age+=1.0/Math.Max(1,Settings.calendarDaysPerYear);p.lastPay=0;
                    var family=Family(c,p.household);var schoolHome=SocietyBuilding(local,family?.home??0);var educationProvider=schoolAvailable&&schoolHome!=null?local.Buildings.Find(b=>b.team==c.faction&&!b.removed&&b.Complete&&b.powered&&!b.paused&&b.definition.id=="civil_school"&&local.Roads.Connected(schoolHome,b)):null;var schoolBusiness=educationProvider==null?null:c.enterprises.Find(e=>e.building==educationProvider.persistentId);
                    if(p.age<18&&p.age>=5&&schoolBusiness!=null){double tuition=Math.Min(c.council.treasury,c.council.priority==CivicPriority.Education?.35:.15);c.council.treasury-=tuition;c.council.educationSpent+=tuition;schoolBusiness.balance+=tuition;p.education=Math.Min(1,p.education+tuition*.003);}
                    if(p.workplace!=0){var business=c.enterprises.Find(e=>e.building==p.workplace);if(business!=null){double gross=Math.Min(business.balance,p.wage),tax=gross*Math.Clamp(c.council.taxRate,0,.6);business.balance-=gross;business.wagesPaid+=gross;p.savings+=gross-tax;p.lastPay=gross-tax;c.council.treasury+=tax;c.council.taxesCollected+=tax;p.reputation=Math.Min(1,p.reputation+.001);p.education=Math.Min(1,p.education+.0003);}}
                    else if(p.age>=18&&p.savings<15){double support=Math.Min(1,c.council.treasury);c.council.treasury-=support;p.savings+=support;c.council.welfareSpent+=support;}
                }
                foreach(var h in c.households){h.paidRent=0;h.foodAccess=foodCoverage;ResidentState shopper=Person(c,h.shopper);if(shopper==null||!shopper.alive||shopper.age<18){foreach(long member in h.members){var adult=Person(c,member);if(adult!=null&&adult.alive&&adult.age>=18){shopper=adult;h.shopper=adult.id;break;}}}
                    double rent=h.tenure==HomeTenure.OwnerOccupied?0:h.rent;
                    foreach(long member in h.members){var p=Person(c,member);if(p==null||!p.alive)continue;double paid=Math.Min(p.savings,rent);p.savings-=paid;rent-=paid;h.paidRent+=paid;}
                    var landlord=Person(c,h.owner);if(h.tenure==HomeTenure.PrivateRental&&landlord!=null)landlord.savings+=h.paidRent;else c.council.treasury+=h.paidRent;
                    foreach(string resource in new[]{"food","drinks","medicine"}){double coverage=resource=="food"?foodCoverage:resource=="drinks"?drinkCoverage:medicineCoverage;var shop=local.Buildings.Find(b=>b.team==c.faction&&!b.removed&&b.Complete&&b.powered&&!b.paused&&b.definition.retailResource==resource&&b.WorkersAssigned>0);var seller=shop==null?null:c.enterprises.Find(e=>e.building==shop.persistentId);if(seller==null)continue;
                        double cost=h.members.Count*coverage*(resource=="food"?1:resource=="drinks"?.4:.2),paid=0;foreach(long member in h.members){var p=Person(c,member);if(p==null||!p.alive)continue;double debit=Math.Min(cost-paid,p.savings);p.savings-=debit;paid+=debit;}
                        double tax=paid*.05;c.council.treasury+=tax;c.council.taxesCollected+=tax;seller.balance+=paid-tax;seller.sales+=paid;
                    }
                }
                // Retail businesses buy supplies from connected production enterprises; credits circulate, never duplicate materials.
                foreach(var retailer in c.enterprises){var shop=SocietyBuilding(local,retailer.building);if(shop==null||string.IsNullOrEmpty(shop.definition.retailResource)||retailer.balance<=10)continue;var suppliers=new List<EnterpriseState>();foreach(var supplier in c.enterprises)if(supplier!=retailer&&SocietyBuilding(local,supplier.building) is RtsBuilding b&&local.Roads.Connected(shop,b))suppliers.Add(supplier);if(suppliers.Count==0)continue;double procurement=(retailer.balance-10)*.5;retailer.balance-=procurement;foreach(var supplier in suppliers)supplier.balance+=procurement/suppliers.Count;}
                foreach(var h in c.households)if(h.home!=0&&h.members.Count>=2&&h.members.Count<6&&c.foodBalance>=-1e-6&&c.powerBalance>=0&&Random(h.id,"birth:"+civicDay)<.001){int parents=0;foreach(var id in h.members){var p=Person(c,id);if(p!=null&&p.alive&&p.age>=22&&p.age<42)parents++;}if(parents>=2){AddPerson(c,h,0);c.population++;}}
                // Adult children can establish a household only when a vacant, completed home actually exists.
                int moves=0;foreach(var vacant in local.Buildings){if(moves>=4)break;if(vacant.team!=c.faction||vacant.removed||!vacant.Complete||vacant.definition.housingCapacity<=0||c.households.Exists(x=>x.home==vacant.persistentId))continue;
                    ResidentState adult=null;HouseholdState previous=null;foreach(var h in c.households)for(int j=2;j<h.members.Count;j++){var candidate=Person(c,h.members[j]);if(candidate!=null&&candidate.alive&&candidate.age>=18&&(adult==null||candidate.age<adult.age)){adult=candidate;previous=h;}}
                    if(adult==null)break;var family=NewFamily(c,(int)previous.tenure);family.surname=previous.surname;family.home=vacant.persistentId;family.tenure=vacant.definition.id=="civil_social_home"?HomeTenure.SocialHousing:vacant.definition.id=="civil_rental_home"?HomeTenure.PrivateRental:HomeTenure.OwnerOccupied;family.owner=family.shopper=adult.id;if(family.tenure==HomeTenure.PrivateRental){ResidentState landlord=null;foreach(var p in c.residents)if(p.alive&&p.age>=18&&p.id!=adult.id&&(landlord==null||p.savings>landlord.savings))landlord=p;if(landlord!=null)family.owner=landlord.id;}family.members.Add(adult.id);adult.household=family.id;previous.members.Remove(adult.id);c.households.Add(family);moves++;
                }
                if(civicDay%7==0){c.council.elections++;c.council.priority=c.housedPopulation<c.population?CivicPriority.Infrastructure:schoolAvailable?CivicPriority.Defence:CivicPriority.Education;c.council.decision=c.council.priority==CivicPriority.Education?"Fund accessible education":c.council.priority==CivicPriority.Defence?"Fund local defences":"Fund serviced family homes";}
            }
            c.societyRevision++;
        }
    }
}
