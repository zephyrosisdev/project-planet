using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum DeathCause {ZombieAttack,OldAge,Murder,Robbery,PoliceViolence}
    [Serializable] public sealed class DeathRecord {
        public long id,resident,household,graveyard,hearse;public string name;public double age,time,ceremonyDue,ceremonyAt,burialAt;
        public bool discovered=true;public double discoveredAt;public long discoveredBy;
        public DeathCause cause;public bool policeHonours,collected,delivered,interred;public Vector2 place;
    }
    [Serializable] public sealed class CivicSecurityState {
        public int requestedPatrols=2,civilianDeathsToday,civilianDeathsYesterday;public bool automaticPatrols=true;
        public long day=-1,activeFuneral;public double funeralUntil;
        // Ordered building entrances are route waypoints, never live pedestrian positions.
        public List<long> patrolStops=new List<long>();
    }
    public sealed partial class CivilisationRuntime {
        public const int MaximumFuneralVehicles=8;
        public const string PoliceHeadquarters="civil_police_hq",Graveyard="civil_graveyard";
        internal long CivicDay(double time)=>(long)Math.Floor(time/Math.Max(60,Settings.settlementDaySeconds)+1.0/3);
        internal double NextFuneralMorning(double time)=>(CivicDay(time)+1)*Math.Max(60,Settings.settlementDaySeconds);
        public string CivicDate(double time){double daySeconds=Math.Max(60,Settings.settlementDaySeconds);long day=CivicDay(time);int yearDays=Math.Max(1,Settings.calendarDaysPerYear);double hour=((time/daySeconds*24+8)%24+24)%24;return $"Year {day/yearDays+1}, day {day%yearDays+1} · {(int)hour:00}:{(int)((hour%1)*60):00}";}
        public DeathRecord RecordDeath(ColonyState colony,ResidentState person,DeathCause cause,double time,Vector2 place){
            var previous=colony.deaths.Find(x=>x.resident==person.id);if(previous!=null)return previous;if(!person.alive)return null;
            var record=new DeathRecord{id=State.AllocateId(),resident=person.id,household=person.household,name=person.name,age=person.age,time=time,cause=cause,discovered=cause!=DeathCause.Murder&&cause!=DeathCause.Robbery,place=place,policeHonours=person.servedPolice||person.role==CivilianRole.Officer,ceremonyDue=NextFuneralMorning(time)};
            person.alive=false;person.workplace=0;person.wage=person.lastPay=0;colony.population=Math.Max(0,colony.population-1);
            var family=Family(colony,person.household);ResidentState heir=Person(colony,person.partner);if(heir==null||!heir.alive)heir=null;
            if(family!=null){family.members.Remove(person.id);foreach(long id in family.members){var p=Person(colony,id);if(heir==null&&p!=null&&p.alive)heir=p;}if(family.shopper==person.id)family.shopper=heir?.id??0;}
            if(heir!=null)heir.savings+=person.savings;else colony.council.treasury+=person.savings;person.savings=0;
            foreach(var h in colony.households)if(h.owner==person.id)h.owner=heir?.id??0;
            foreach(var e in colony.enterprises)if(e.owner==person.id)e.owner=heir?.id??0;
            if(cause==DeathCause.ZombieAttack&&!record.policeHonours){RollSecurityDay(colony,Math.Max(SimulationTime,time));long died=CivicDay(time);if(died==colony.security.day)colony.security.civilianDeathsToday++;else if(died==colony.security.day-1)colony.security.civilianDeathsYesterday++;}
            colony.deaths.Add(record);colony.societyRevision++;return record;
        }
        void RollSecurityDay(ColonyState c,double now){long day=CivicDay(now);if(day<=c.security.day)return;c.security.civilianDeathsYesterday=c.security.day==day-1?c.security.civilianDeathsToday:0;c.security.civilianDeathsToday=0;c.security.day=day;}
        internal long FuneralDestination(ColonyState c,ResidentState p,double time){if(c.security.activeFuneral==0||time>=c.security.funeralUntil)return 0;var death=c.deaths.Find(d=>d.id==c.security.activeFuneral);return death!=null&&(p.household==death.household||death.policeHonours&&p.role==CivilianRole.Officer)?death.graveyard:0;}
        internal static bool CivicServiceReady(RtsBuilding b)=>b!=null&&!b.removed&&b.Complete&&!b.paused&&b.powered&&!b.roadSuspended&&b.WorkersAssigned>0;
        internal bool OnPatrol(PlanetRtsBase local,ColonyState c,ResidentState p){if(!p.alive||Confined(p)||p.role!=CivilianRole.Officer||FuneralDestination(c,p,RenderTime)!=0||!CivicServiceReady(SocietyBuilding(local,p.workplace)))return false;int rank=0;foreach(var officer in c.residents){if(officer==p)break;if(officer.alive&&officer.role==CivilianRole.Officer&&CivicServiceReady(SocietyBuilding(local,officer.workplace)))rank++;}return rank<c.security.requestedPatrols;}
        internal void TickCivicServices(PlanetRtsBase local,ColonyState c){
            c.security??=new CivicSecurityState();c.deaths??=new List<DeathRecord>();double now=SimulationTime;TickCrime(local,c);RollSecurityDay(c,now);
            if(c.security.automaticPatrols)c.security.requestedPatrols=Math.Clamp(Math.Max(c.security.requestedPatrols,2+c.security.civilianDeathsToday+c.security.civilianDeathsYesterday/2),0,32);
            c.security.patrolStops.RemoveAll(id=>SocietyBuilding(local,id)==null);
            if(c.security.patrolStops.Count==0){var hq=local.Buildings.Find(b=>b.team==c.faction&&!b.removed&&b.Complete&&b.definition.id==PoliceHeadquarters);if(hq!=null){c.security.patrolStops.Add(hq.persistentId);foreach(var b in local.Buildings)if(c.security.patrolStops.Count<4&&b.team==c.faction&&!b.removed&&b.Complete&&b!=hq&&b.definition.housingCapacity>0&&local.Roads.Connected(hq,b))c.security.patrolStops.Add(b.persistentId);}}
            int hearses=0;foreach(var vehicle in c.cargoVehicles)if(vehicle.funeral)hearses++;
            foreach(var graveyard in local.Buildings)if(hearses<MaximumFuneralVehicles&&graveyard.team==c.faction&&graveyard.definition.id==Graveyard&&CivicServiceReady(graveyard)&&!c.cargoVehicles.Exists(v=>v.funeral&&v.warehouse==graveyard.persistentId)){c.cargoVehicles.Add(new CargoVehicleState{id=State.AllocateId(),warehouse=graveyard.persistentId,funeral=true});hearses++;}
            foreach(var hearse in c.cargoVehicles)if(hearse.funeral){
                var home=SocietyBuilding(local,hearse.warehouse);if(!CivicServiceReady(home))continue;
                var death=c.deaths.Find(d=>d.id==hearse.funeralDeath);
                if(hearse.phase!=CargoVehiclePhase.Idle&&local.CargoNavigation?.Arrived(hearse)==true&&death!=null){
                    if(hearse.phase==CargoVehiclePhase.Collecting){death.collected=true;hearse.phase=CargoVehiclePhase.Delivering;}
                    else{death.delivered=true;hearse.phase=CargoVehiclePhase.Idle;}
                    hearse.navigationVersion=0;local.NotifyChanged();
                }
                if(hearse.phase!=CargoVehiclePhase.Idle)continue;
                var pending=c.deaths.Find(d=>d.discovered&&!d.delivered&&(d.hearse==0||d.hearse==hearse.id));if(pending==null)continue;
                pending.hearse=hearse.id;pending.graveyard=home.persistentId;hearse.funeralDeath=pending.id;hearse.funeralPickup=pending.place;hearse.source=hearse.destination=home.persistentId;hearse.phase=pending.collected?CargoVehiclePhase.Delivering:CargoVehiclePhase.Collecting;hearse.departure=now;hearse.navigationVersion=0;local.NotifyChanged();
            }
            if(c.security.activeFuneral!=0){if(now<c.security.funeralUntil)return;var ended=c.deaths.Find(d=>d.id==c.security.activeFuneral);if(ended!=null){ended.interred=true;ended.burialAt=now;}c.security.activeFuneral=0;local.NotifyChanged();}
            var next=c.deaths.Find(d=>d.delivered&&!d.interred&&d.ceremonyDue<=now&&CivicServiceReady(SocietyBuilding(local,d.graveyard)));
            if(next!=null){next.ceremonyAt=now;c.security.activeFuneral=next.id;c.security.funeralUntil=now+Math.Max(60,Settings.settlementDaySeconds)/24;local.Notice=(next.policeHonours?"POLICE HONOURS · ":"FUNERAL · ")+next.name;local.NotifyChanged();}
        }
    }
}
