using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        struct DeathEvent {public uint slot,generation,cause,tick;public Vector4 body;}
        GraphicsBuffer civicStates,patrolGoals,deathEvents,acknowledgements;int civicVersion;uint generationSerial,reportedDeaths,handledDeaths;bool deathsPending;double nextDeaths;
        readonly HashSet<long> patrolling=new HashSet<long>();
        void RefreshPatrolRoster(PlanetRtsBase local,CivilisationRuntime simulation){patrolling.Clear();foreach(var entry in residents)if(entry.person!=null)entry.person.patrolDuty=false;foreach(var c in local.Settlements)if(c!=null){int remaining=c.security.requestedPatrols;foreach(var entry in residents){var p=entry.person;if(p==null||entry.colony!=c||remaining<=0)continue;if(p.alive&&!CivilisationRuntime.Confined(p)&&p.role==CivilianRole.Officer&&simulation.FuneralDestination(c,p,simulation.RenderTime)==0&&CivilisationRuntime.CivicServiceReady(local.FindBuilding(p.workplace))){patrolling.Add(p.id);p.patrolDuty=true;remaining--;}}}}
        readonly List<uint> generations=new List<uint>();readonly Stack<int> vacant=new Stack<int>();
        internal GraphicsBuffer CivicStates=>civicStates;internal int CivicVersion=>civicVersion;
        GraphicsBuffer PatrolGoals {get{if(patrolGoals==null){patrolGoals=Buffer(PlanetCountries.Maximum*9,4);patrolGoals.SetData(new uint[PlanetCountries.Maximum*9]);}return patrolGoals;}}
        readonly Dictionary<long,SettlementOutdoorRoster> dailyOutdoorRosters=new Dictionary<long,SettlementOutdoorRoster>();
        readonly HashSet<long> outdoorRoster=new HashSet<long>();double nextRoster;
        bool RegisterResidents(CivilisationRuntime simulation){
            bool ambient=simulation.Settings.ambientSettlementLife,changed=false;
            if(Time.realtimeSinceStartupAsDouble<nextRoster)return false;nextRoster=Time.realtimeSinceStartupAsDouble+.5;
            outdoorRoster.Clear();rosterPeople.Clear();int limit=Math.Clamp(ambient?simulation.Settings.outdoorCitizenLimit:simulation.Settings.maximumCivilianAgents,0,65535);
            if(ambient){int populated=0;foreach(var c in world.Base.Settlements)if(c!=null&&c.residents.Count>0)populated++;int share=limit/Math.Max(1,populated);long day=(long)Math.Floor(simulation.RenderTime/Math.Max(60,simulation.Settings.settlementDaySeconds));
                // Existing discovered/unacknowledged bodies keep their GPU identity until services release them.
                foreach(var entry in residents)if(entry.person!=null&&simulation.HoldCrimeBody(entry.colony,entry.person)&&outdoorRoster.Count<limit){outdoorRoster.Add(entry.person.id);rosterPeople[entry.person.id]=entry;}
                foreach(var colony in world.Base.Settlements)if(colony!=null){int n=colony.residents.Count,selected=0;if(n==0)continue;int cap=Math.Min(limit-outdoorRoster.Count,Math.Max(0,Math.Min(share,simulation.Settings.outdoorCitizensPerSettlement)));
                    if(!dailyOutdoorRosters.TryGetValue(colony.id,out var roster)){roster=new SettlementOutdoorRoster();dailyOutdoorRosters[colony.id]=roster;}roster.Refresh(colony,cap,day);
                    foreach(var p in roster.People)if(outdoorRoster.Add(p.id))rosterPeople[p.id]=(colony,p);
                }
            }
            for(int i=0;i<residents.Count;i++){var entry=residents[i];if(entry.person==null)continue;bool keep=ambient?outdoorRoster.Contains(entry.person.id):entry.person.alive&&!entry.person.recruited||simulation.HoldCrimeBody(entry.colony,entry.person);if(!keep){slots.Remove(entry.person.id);residents[i]=default;vacant.Push(i);changed=true;}}
            foreach(var colony in world.Base.Settlements)if(colony!=null)foreach(var person in Selected(colony,ambient))if(!slots.ContainsKey(person.id)&&(vacant.Count>0||residents.Count<limit)){
                int slot=vacant.Count>0?vacant.Pop():residents.Count;if(slot==residents.Count){residents.Add(default);generations.Add(0);}residents[slot]=(colony,person);generations[slot]=generationSerial=(generationSerial%16777215)+1;slots[person.id]=slot;changed=true;
            }
            int previous=residents.Count;while(residents.Count>0&&residents[residents.Count-1].person==null){residents.RemoveAt(residents.Count-1);generations.RemoveAt(generations.Count-1);}
            if(previous!=residents.Count){vacant.Clear();for(int i=0;i<residents.Count;i++)if(residents[i].person==null)vacant.Push(i);}return changed;
        }
        readonly Dictionary<long,(ColonyState colony,ResidentState person)> rosterPeople=new Dictionary<long,(ColonyState,ResidentState)>();
        System.Collections.Generic.IEnumerable<ResidentState> Selected(ColonyState colony,bool ambient){if(ambient){foreach(var entry in rosterPeople.Values)if(entry.colony==colony)yield return entry.person;}else foreach(var p in colony.residents)if(p.alive&&!p.recruited)yield return p;}
        static int Gcd(int a,int b){while(b!=0){int r=a%b;a=b;b=r;}return a;}
        RtsBuilding SocietyBuildingForPatrol(PlanetRtsBase local,ColonyState c,int slot){foreach(long stop in c.security.patrolStops){var b=local.FindBuilding(stop);if(b!=null)return b;}return local.FindBuilding(residents[slot].person.workplace);}
        void UploadPatrolGoals(PlanetRtsBase local){var data=new uint[PlanetCountries.Maximum*9];for(int team=0;team<local.Factions.Length;team++){var c=local.Settlements[team];if(c==null)continue;int count=0;foreach(long stop in c.security.patrolStops){var b=local.FindBuilding(stop);if(count==8)break;if(b!=null&&localGoals.TryGetValue(b.index,out int flow))data[team*9+1+count++]=(uint)flow;}data[team*9]=(uint)count;}PatrolGoals.SetData(data);}
        // At most 256 named deaths per request. Unacknowledged overflow/error events remain on GPU for retry.
        internal void ReadDeaths(uint? totalDeaths=null){if(totalDeaths.HasValue)reportedDeaths=totalDeaths.Value;if(unchecked((int)(reportedDeaths-handledDeaths))<=0)return;if(disposed||civicStates==null||deathsPending||Time.realtimeSinceStartupAsDouble<nextDeaths)return;nextDeaths=Time.realtimeSinceStartupAsDouble+.25;
            if(deathEvents==null){deathEvents=Buffer(257,32);acknowledgements=Buffer(256,8);}deathEvents.SetData(new DeathEvent[1],0,0,1);
            int drain=shader.FindKernel("DrainDeaths");shader.SetInt("_WalkerCount",Count);shader.SetFloat("_Clock",(float)world.SystemRuntime.Civilisation.RenderTime);shader.SetBuffer(drain,"_CivicStates",civicStates);shader.SetBuffer(drain,"_DeathEvents",deathEvents);shader.Dispatch(drain,(Count+127)/128,1,1);deathsPending=true;
            AsyncGPUReadback.Request(deathEvents,result=>{deathsPending=false;if(disposed||result.hasError)return;var events=result.GetData<DeathEvent>();int count=(int)Math.Min(256,events[0].slot);if(count==0){handledDeaths=reportedDeaths;return;}var ack=new Vector2Int[count];
                for(int j=0;j<count;j++){var ev=events[j+1];ack[j]=new Vector2Int((int)ev.slot,(int)ev.generation);if(ev.slot>=residents.Count||generations[(int)ev.slot]!=ev.generation)continue;var entry=residents[(int)ev.slot];if(entry.person==null||!entry.person.alive)continue;world.SystemRuntime.Civilisation.RecordDeath(entry.colony,entry.person,(ev.cause==7?DeathCause.Murder:ev.cause==5?DeathCause.Infection:ev.cause==6?DeathCause.RaiderAttack:DeathCause.ZombieAttack),ev.body.w,CubeSphere.Encode(new Vector3(ev.body.x,ev.body.y,ev.body.z).normalized));if(ev.cause==7){var record=new CrimeCase{id=world.SystemRuntime.Civilisation.State.AllocateId(),suspect=-1,victim=entry.person.id,offence=Offence.Murder,time=ev.body.w,place=CubeSphere.Encode(new Vector3(ev.body.x,ev.body.y,ev.body.z).normalized),reported=true,evidence=.8,outcome="Player weapon attack"};entry.colony.crime.cases.Add(record);entry.colony.crime.reportedCrimes++;var inventory=world.PlayerForValidation.Inventory;inventory.WantedHeat+=40;if(inventory.Equipped!=null){if(!inventory.Provenance.TryGetValue(inventory.Equipped,out var history)){history=new ItemProvenance();inventory.Provenance[inventory.Equipped]=history;}history.crime=record.id;}}
                    if(ev.cause==5)world.SystemRuntime.Civilisation.QueueInfectedCivilian(world.Base,entry.colony,CubeSphere.Encode(new Vector3(ev.body.x,ev.body.y,ev.body.z).normalized));world.Base.NotifyChanged();}
                handledDeaths+=(uint)count;acknowledgements.SetData(ack);int apply=shader.FindKernel("AcknowledgeDeaths");shader.SetInt("_AcknowledgementCount",count);shader.SetBuffer(apply,"_Acknowledgements",acknowledgements);shader.SetBuffer(apply,"_CivicStates",civicStates);shader.Dispatch(apply,(count+127)/128,1,1);nextOrders=0;
            });
        }
    }
}
