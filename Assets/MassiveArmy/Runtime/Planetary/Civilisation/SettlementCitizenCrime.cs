using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        struct CrimeOrder {public uint generation,flags,gang,facility;public float courage;public uint pad0,pad1,pad2;}
        struct CrimeAgent {public uint generation,status,target,pending;public float cooldown,time;public uint other,pad;}
        struct CrimeReply {public uint slot,generation,action,other;}
        struct CrimeEvent {public uint slot,generation,action,other;public Vector4 body;}
        struct Facility {public Vector4 position,right,up,forward,size;}
        ComputeShader crimeShader;GraphicsBuffer crimeOrders,crimeAgents,crimeHeads,crimeNext,crimeClaims,crimeEvents,crimeAcks,crimeReplies,crimeFacilities;
        CrimeOrder[] crimeOrderData;int crimeCapacity,crimeCivicVersion=-1,crimeCount;uint crimeFrame;bool crimeReadPending;double nextCrimeRead;
        readonly Dictionary<long,int> facilitySlots=new Dictionary<long,int>();readonly List<Vector4> pendingReplies=new List<Vector4>();
        readonly bool crimeControl=Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-crime-control")>=0;
        void UpdateCrimeOrders(){
            crimeCount=0;foreach(var entry in residents){var x=entry.person?.crime;if(x!=null&&(x.activity!=CrimeActivity.None||x.wanted>0||CivilisationRuntime.Confined(entry.person))||entry.person!=null&&!entry.person.alive&&world.Base.Civilisation.HoldCrimeBody(entry.colony,entry.person))crimeCount++;}
            if(crimeControl||crimeCount==0&&crimeShader==null)return;
            if(crimeShader==null)crimeShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/SettlementCrime"));
            if(crimeCapacity<capacity){crimeCapacity=capacity;crimeOrders?.Dispose();crimeAgents?.Dispose();crimeNext?.Dispose();crimeClaims?.Dispose();crimeOrders=Buffer(capacity,32);crimeAgents=Buffer(capacity,32);crimeAgents.SetData(new CrimeAgent[capacity]);crimeNext=Buffer(capacity,4);crimeClaims=Buffer(capacity,4);crimeClaims.SetData(new uint[capacity]);crimeOrderData=new CrimeOrder[capacity];crimeCivicVersion=-1;}
            if(crimeHeads==null){crimeHeads=Buffer(8192,4);crimeEvents=Buffer(257,32);crimeAcks=Buffer(256,8);crimeReplies=Buffer(256,16);crimeFacilities=Buffer(32,80);}
            var local=world.Base;var sim=local.Civilisation;facilitySlots.Clear();var facilities=new Facility[32];
            foreach(var b in local.Buildings){if(facilitySlots.Count==32)break;if(b.removed||!b.Complete||!(b.definition.id==CivilisationRuntime.CountyJail||b.definition.id==CivilisationRuntime.PoliceHeadquarters||b.definition.id==CivilisationRuntime.FederalPrison||b.definition.id==CivilisationRuntime.PrisonWing))continue;
                int index=facilitySlots.Count;facilitySlots[b.persistentId]=index;var direction=CubeSphere.Decode(b.position);var q=local.Grid.Rotation(local.Area(b));var p=direction*local.FoundationRadius(b);var layout=RtsModelArt.Load().FindBuilding(b.definition)?.modular;var size=layout?.bounds.size??new Vector3(18,6.4f,20);bool gate=layout!=null&&layout.securityGate>=0&&(b.modularDoors&(1u<<layout.securityGate))!=0;
                facilities[index]=new Facility{position=new Vector4(p.x,p.y,p.z,b.team),right=q*Vector3.right,up=q*Vector3.up,forward=new Vector4((q*Vector3.forward).x,(q*Vector3.forward).y,(q*Vector3.forward).z,gate?1:0),size=new Vector4(size.x,size.z,2,CivilisationRuntime.CivicServiceReady(b)?1:0)};
            }crimeFacilities.SetData(facilities);
            for(int i=0;i<Count;i++){var (c,p)=residents[i];var x=p.crime;uint flags=0;if(x!=null&&p.age>=18){if(x.contraband>0||x.wanted>0||x.activity==CrimeActivity.Exploiter||x.weapons>0&&p.role!=CivilianRole.Officer)flags|=1;if(p.patrolDuty)flags|=2;if(x.activity==CrimeActivity.Dealer&&x.contraband>0)flags|=4;if(x.activity==CrimeActivity.Mugger||x.activity==CrimeActivity.Burglar||x.activity==CrimeActivity.Robber)flags|=8;if(CivilisationRuntime.Confined(p))flags|=16;if(x.activity==CrimeActivity.IndependentSexWorker||x.activity==CrimeActivity.ExploitedSexWorker)flags|=64;}
                crimeOrderData[i]=new CrimeOrder{generation=generations[i],flags=flags,gang=unchecked((uint)(x?.gang??0)),facility=x!=null&&facilitySlots.TryGetValue(x.facility,out int f)?(uint)f:uint.MaxValue,courage=(float)(.15+PlanetResourceProfile.Random(p.id+":courage")*.5+(x?.gang>0?.25:0))};
            }crimeOrders.SetData(crimeOrderData);crimeCivicVersion=-1;
        }
        void StepCrime(float seconds,bool validation){
            if(crimeControl||crimeShader==null||crimeCount==0||seconds<=0||world.Paused&&!validation)return;
            if(crimeCivicVersion!=civicVersion){foreach(string name in new[]{"ClearCrimeGrid","BuildCrimeGrid","CrimeEncounters","ApplyCrimeShots","DrainCrimeEvents","AckCrimeEvents","ApplyCrimeReplies"}){int k=crimeShader.FindKernel(name);crimeShader.SetBuffer(k,"_CivicStates",civicStates);crimeShader.SetBuffer(k,"_CrimeOrders",crimeOrders);crimeShader.SetBuffer(k,"_CrimeAgents",crimeAgents);crimeShader.SetBuffer(k,"_CrimeHeads",crimeHeads);crimeShader.SetBuffer(k,"_CrimeNext",crimeNext);crimeShader.SetBuffer(k,"_CrimeClaims",crimeClaims);crimeShader.SetBuffer(k,"_CrimeEvents",crimeEvents);crimeShader.SetBuffer(k,"_CrimeAcks",crimeAcks);crimeShader.SetBuffer(k,"_CrimeReplies",crimeReplies);crimeShader.SetBuffer(k,"_CrimeFacilities",crimeFacilities);crimeShader.SetBuffer(k,"_CrimeWalkers",walkers);crimeShader.SetBuffer(k,"_CrimePoses",poses);}crimeCivicVersion=civicVersion;}
            crimeShader.SetInt("_CrimeCount",Count);crimeShader.SetInt("_CrimeFrame",unchecked((int)++crimeFrame));crimeShader.SetFloat("_CrimeClock",(float)world.Base.Civilisation.RenderTime);crimeShader.SetFloat("_CrimeDelta",Mathf.Min(.25f,seconds));crimeShader.Dispatch(crimeShader.FindKernel("ClearCrimeGrid"),64,1,1);crimeShader.Dispatch(crimeShader.FindKernel("BuildCrimeGrid"),(Count+127)/128,1,1);crimeShader.Dispatch(crimeShader.FindKernel("CrimeEncounters"),(Count+127)/128,1,1);crimeShader.Dispatch(crimeShader.FindKernel("ApplyCrimeShots"),(Count+127)/128,1,1);
            if(crimeReadPending||Time.realtimeSinceStartupAsDouble<nextCrimeRead)return;nextCrimeRead=Time.realtimeSinceStartupAsDouble+.5;crimeEvents.SetData(new CrimeEvent[1],0,0,1);crimeShader.Dispatch(crimeShader.FindKernel("DrainCrimeEvents"),(Count+127)/128,1,1);crimeReadPending=true;
            AsyncGPUReadback.Request(crimeEvents,result=>{crimeReadPending=false;if(disposed||result.hasError)return;var events=result.GetData<CrimeEvent>();int count=(int)Math.Min(256,events[0].slot);if(count==0)return;var ack=new Vector2Int[count];var replies=new CrimeReply[count];int replyCount=0;
                for(int j=0;j<count;j++){var e=events[j+1];ack[j]=new Vector2Int((int)e.slot,(int)e.generation);if(e.slot>=Count||generations[(int)e.slot]!=e.generation)continue;var (c,p)=residents[(int)e.slot];ResidentState other=e.other<Count?residents[(int)e.other].person:null;var sim=world.Base.Civilisation;var point=CubeSphere.Encode(new Vector3(e.body.x,e.body.y,e.body.z).normalized);double now=e.body.w;
                    void Reply(uint slot,uint action){if(slot>=Count||replyCount>=count)return;replies[replyCount++]=new CrimeReply{slot=slot,generation=generations[(int)slot],action=action,other=e.slot};}
                    if(e.action==1&&other!=null&&other.alive){c.crime.searches++;var x=other.crime;bool evidence=x.contraband>0||x.wanted>0||x.activity==CrimeActivity.Exploiter||x.weapons>0&&other.role!=CivilianRole.Officer;
                        if(evidence){double resistance=PlanetResourceProfile.Random(other.id+":resist:"+c.crime.searches);if(x.weapons>0&&resistance<.4+(x.gang!=0?.2:0)){sim.ReportCrime(c,other,p,p,Offence.Assault,point,now);c.security.requestedPatrols=Math.Min(32,c.security.requestedPatrols+4);Reply(e.other,2);}else if(resistance>.85){x.fleeUntil=now+60;Reply(e.other,3);}else if(sim.Arrest(world.Base,c,other,p,point,now))Reply(e.other,1);else Reply(e.other,3);}
                    }else if(e.action==3&&p.alive&&other!=null&&other.alive){double take=Math.Min(10,other.savings);other.savings-=take;p.savings+=take;sim.ReportCrime(c,p,other,null,Offence.Robbery,point,now);}
                    else if(e.action==4&&p.alive&&other!=null&&other.alive&&other.age>=18&&p.crime.contraband>=1&&other.savings>=6&&PlanetResourceProfile.Random(other.id+":buy:"+(int)(now/30))<.12){p.crime.contraband-=1;other.savings-=6;p.savings+=6;other.crime.drugAddiction=Math.Min(1,other.crime.drugAddiction+.12);}
                    else if(e.action==5)sim.DiscoverBody(c,p.id,other?.id??0,now);
                    else if(e.action==6&&p.alive&&other!=null&&other.alive&&p.age>=18&&other.age>=18&&other.savings>=4&&PlanetResourceProfile.Random(other.id+":service:"+(int)(now/60))<.05){other.savings-=4;p.savings+=4;var exploiter=CivilisationRuntime.Person(c,p.crime.exploiter);if(exploiter!=null&&exploiter.alive){p.savings-=2;exploiter.savings+=2;}}
                    else if(e.action==7&&other!=null&&p.role!=CivilianRole.Officer){sim.ReportCrime(c,p,other,null,Offence.Assault,point,now);c.security.requestedPatrols=Math.Min(32,c.security.requestedPatrols+3);}
                    else if(e.action==8&&p.alive){var cause=other?.role==CivilianRole.Officer?DeathCause.PoliceViolence:other?.crime.activity==CrimeActivity.Robber||other?.crime.activity==CrimeActivity.Mugger?DeathCause.Robbery:DeathCause.Murder;sim.RecordDeath(c,p,cause,now,point);if(cause!=DeathCause.PoliceViolence)sim.ReportCrime(c,other,p,null,p.role==CivilianRole.Officer?Offence.OfficerMurder:Offence.Murder,point,now,false);}
                    else if(e.action==9&&CivilisationRuntime.Confined(p)){p.crime.custody=CustodyStage.Free;p.crime.facility=0;p.crime.wanted+=2;sim.ReportCrime(c,p,null,null,Offence.Escape,point,now);Reply(e.slot,4);}
                }
                crimeAcks.SetData(ack);crimeShader.SetInt("_CrimeAckCount",count);crimeShader.Dispatch(crimeShader.FindKernel("AckCrimeEvents"),(count+127)/128,1,1);if(replyCount>0){crimeReplies.SetData(replies,0,0,replyCount);crimeShader.SetInt("_CrimeReplyCount",replyCount);crimeShader.Dispatch(crimeShader.FindKernel("ApplyCrimeReplies"),(replyCount+127)/128,1,1);}nextOrders=0;world.Base.NotifyChanged();
            });
        }
        void DisposeCrime(){crimeOrders?.Dispose();crimeAgents?.Dispose();crimeHeads?.Dispose();crimeNext?.Dispose();crimeClaims?.Dispose();crimeEvents?.Dispose();crimeAcks?.Dispose();crimeReplies?.Dispose();crimeFacilities?.Dispose();if(crimeShader!=null)UnityEngine.Object.Destroy(crimeShader);}
    }
}
