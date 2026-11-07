using System;
using System.Collections.Generic;
namespace MassiveArmy.Planetary {
    // Pure seeded generation: no saved appointments, daily arrays, strings or disk reads.
    internal readonly struct SettlementDailySchedule {
        public readonly double start,finish,errands,leisure,endLeisure;public readonly uint group;public readonly int parkPose;
        internal static uint Hash(ulong x){x^=x>>30;x*=0xbf58476d1ce4e5b9UL;x^=x>>27;x*=0x94d049bb133111ebUL;x^=x>>31;return (uint)(x^(x>>32));}
        internal static double Unit(ulong x)=>Hash(x)/4294967296.0;
        internal SettlementDailySchedule(long person,long job,long day){ulong seed=unchecked((ulong)person*0x9e3779b97f4a7c15UL^(ulong)job*397UL^(ulong)day*7919UL);start=6.5+Unit(seed)*2;finish=start+7+Unit(seed+1);errands=job==0?10+Unit(seed+2)*3:finish;leisure=Math.Min(19,errands+1+Unit(seed+3));endLeisure=Math.Min(21,leisure+.75+Unit(seed+4)*1.5);group=Hash(unchecked((ulong)day+(ulong)person/4));parkPose=(int)(Hash(seed+5)%3);}
        internal ResidentRoutine Routine(ResidentState p,double hour){if(hour<6||hour>=21)return ResidentRoutine.Home;if(p.age<18&&p.age>=5&&hour>=7.5&&hour<15)return ResidentRoutine.School;if(p.workplace!=0&&hour>=start&&hour<finish)return ResidentRoutine.Work;if(hour>=errands&&hour<errands+1)return ResidentRoutine.Shopping;if(hour>=leisure&&hour<endLeisure)return ResidentRoutine.Community;return ResidentRoutine.Home;}
    }
    internal sealed class SettlementOutdoorRoster {
        internal readonly List<ResidentState> People=new List<ResidentState>();readonly HashSet<long> ids=new HashSet<long>();long currentDay=long.MinValue;int budget=-1;
        internal void Refresh(ColonyState colony,int limit,long day){
            colony.outdoorPriority??=new List<ResidentState>();
            if(currentDay!=day||budget!=limit){People.Clear();currentDay=day;budget=limit;}
            People.RemoveAll(p=>!p.alive||p.recruited||CivilisationRuntime.Confined(p));while(People.Count>limit)People.RemoveAt(People.Count-1);ids.Clear();foreach(var p in People)ids.Add(p.id);
            foreach(var p in colony.outdoorPriority){if(limit==0)break;if(!p.alive||p.recruited||CivilisationRuntime.Confined(p)||ids.Contains(p.id))continue;if(People.Count>=limit){int last=People.FindLastIndex(x=>!colony.outdoorPriority.Contains(x));if(last<0)break;ids.Remove(People[last].id);People.RemoveAt(last);}ids.Add(p.id);People.Add(p);}
            int n=colony.residents.Count;if(n==0||limit==0)return;uint seed=SettlementDailySchedule.Hash(unchecked((ulong)colony.id*397UL+(ulong)day));int first=(int)(seed%(uint)n),stride=(int)((seed>>8)%(uint)n)+1;while(Gcd(stride,n)!=1)stride=stride%n+1;
            for(int probe=0;probe<Math.Min(n,limit*8+32)&&People.Count<limit;probe++){var p=colony.residents[(int)((first+(long)probe*stride)%n)];if(p.alive&&!p.recruited&&!CivilisationRuntime.Confined(p)&&ids.Add(p.id))People.Add(p);}
        }
        static int Gcd(int a,int b){while(b!=0){int r=a%b;a=b;b=r;}return a;}
    }

}
