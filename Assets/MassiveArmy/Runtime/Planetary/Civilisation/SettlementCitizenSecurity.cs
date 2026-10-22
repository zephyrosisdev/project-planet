using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        struct DeathEvent {public uint slot,generation,cause,tick;public Vector4 body;}
        GraphicsBuffer civicStates,patrolGoals,deathEvents,acknowledgements;int civicVersion;uint generationSerial,reportedDeaths,handledDeaths;bool deathsPending;double nextDeaths;
        readonly HashSet<long> patrolling=new HashSet<long>();
        void RefreshPatrolRoster(PlanetRtsBase local,CivilisationRuntime simulation){patrolling.Clear();foreach(var c in local.Settlements)if(c!=null){foreach(var person in c.residents)person.patrolDuty=false;int remaining=c.security.requestedPatrols;foreach(var p in c.residents){if(remaining<=0)break;if(p.alive&&!CivilisationRuntime.Confined(p)&&p.role==CivilianRole.Officer&&simulation.FuneralDestination(c,p,simulation.RenderTime)==0&&CivilisationRuntime.CivicServiceReady(local.FindBuilding(p.workplace))){patrolling.Add(p.id);p.patrolDuty=true;remaining--;}}}}
        readonly List<uint> generations=new List<uint>();readonly Stack<int> vacant=new Stack<int>();
        internal GraphicsBuffer CivicStates=>civicStates;internal int CivicVersion=>civicVersion;
        GraphicsBuffer PatrolGoals {get{if(patrolGoals==null){patrolGoals=Buffer(PlanetCountries.Maximum*9,4);patrolGoals.SetData(new uint[PlanetCountries.Maximum*9]);}return patrolGoals;}}
        bool RegisterResidents(CivilisationRuntime simulation){bool changed=false;
            for(int i=0;i<residents.Count;i++)if(residents[i].person!=null&&residents[i].person.id>0&&!residents[i].person.alive&&!simulation.HoldCrimeBody(residents[i].colony,residents[i].person)){slots.Remove(residents[i].person.id);residents[i]=default;vacant.Push(i);changed=true;}
            foreach(var colony in world.Base.Settlements)if(colony!=null)foreach(var person in colony.residents)if(person.alive&&!slots.ContainsKey(person.id)&&(vacant.Count>0||residents.Count<Math.Min(65535,simulation.Settings.maximumCivilianAgents))){
                int slot=vacant.Count>0?vacant.Pop():residents.Count;if(slot==residents.Count){residents.Add(default);generations.Add(0);}residents[slot]=(colony,person);generations[slot]=generationSerial=(generationSerial%16777215)+1;slots[person.id]=slot;changed=true;
            }
            // Vacant slots retain their dead GPU state until reused, with an inactive order.
            for(int i=0;i<residents.Count;i++)if(residents[i].person==null){residents[i]=(new ColonyState(),new ResidentState{id=-i-1,alive=false});}
            return changed;
        }
        RtsBuilding SocietyBuildingForPatrol(PlanetRtsBase local,ColonyState c,int slot){foreach(long stop in c.security.patrolStops){var b=local.FindBuilding(stop);if(b!=null)return b;}return local.FindBuilding(residents[slot].person.workplace);}
        void UploadPatrolGoals(PlanetRtsBase local){var data=new uint[PlanetCountries.Maximum*9];for(int team=0;team<local.Factions.Length;team++){var c=local.Settlements[team];if(c==null)continue;int count=0;foreach(long stop in c.security.patrolStops){var b=local.FindBuilding(stop);if(count==8)break;if(b!=null&&localGoals.TryGetValue(b.index,out int flow))data[team*9+1+count++]=(uint)flow;}data[team*9]=(uint)count;}PatrolGoals.SetData(data);}
        // At most 256 named deaths per request. Unacknowledged overflow/error events remain on GPU for retry.
        internal void ReadDeaths(uint? totalDeaths=null){if(totalDeaths.HasValue)reportedDeaths=totalDeaths.Value;if(unchecked((int)(reportedDeaths-handledDeaths))<=0)return;if(disposed||civicStates==null||deathsPending||Time.realtimeSinceStartupAsDouble<nextDeaths)return;nextDeaths=Time.realtimeSinceStartupAsDouble+.25;
            if(deathEvents==null){deathEvents=Buffer(257,32);acknowledgements=Buffer(256,8);}deathEvents.SetData(new DeathEvent[1],0,0,1);
            int drain=shader.FindKernel("DrainDeaths");shader.SetInt("_WalkerCount",Count);shader.SetFloat("_Clock",(float)world.SystemRuntime.Civilisation.RenderTime);shader.SetBuffer(drain,"_CivicStates",civicStates);shader.SetBuffer(drain,"_DeathEvents",deathEvents);shader.Dispatch(drain,(Count+127)/128,1,1);deathsPending=true;
            AsyncGPUReadback.Request(deathEvents,result=>{deathsPending=false;if(disposed||result.hasError)return;var events=result.GetData<DeathEvent>();int count=(int)Math.Min(256,events[0].slot);if(count==0){handledDeaths=reportedDeaths;return;}var ack=new Vector2Int[count];
                for(int j=0;j<count;j++){var ev=events[j+1];ack[j]=new Vector2Int((int)ev.slot,(int)ev.generation);if(ev.slot>=residents.Count||generations[(int)ev.slot]!=ev.generation)continue;var entry=residents[(int)ev.slot];if(entry.person==null||!entry.person.alive)continue;world.SystemRuntime.Civilisation.RecordDeath(entry.colony,entry.person,DeathCause.ZombieAttack,ev.body.w,CubeSphere.Encode(new Vector3(ev.body.x,ev.body.y,ev.body.z).normalized));world.Base.NotifyChanged();}
                handledDeaths+=(uint)count;acknowledgements.SetData(ack);int apply=shader.FindKernel("AcknowledgeDeaths");shader.SetInt("_AcknowledgementCount",count);shader.SetBuffer(apply,"_Acknowledgements",acknowledgements);shader.SetBuffer(apply,"_CivicStates",civicStates);shader.Dispatch(apply,(count+127)/128,1,1);nextOrders=0;
            });
        }
    }
}
