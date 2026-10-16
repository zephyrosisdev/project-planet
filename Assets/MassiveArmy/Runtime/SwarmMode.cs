using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MassiveArmy
{
    /// <summary>Wave scheduling only. Attackers and their identities remain GPU authoritative.</summary>
    public sealed class SwarmSchedule
    {
        public const double PreparationSeconds=600,WarningSeconds=30;
        public int Wave {get;private set;}=1;
        public ulong WaveSize {get;private set;}=1000;
        public ulong Remaining {get;private set;}
        public double Countdown {get;private set;}=PreparationSeconds+WarningSeconds;
        public bool Active {get;private set;}
        public bool Warning=>!Active&&Countdown<=WarningSeconds;
        public bool Exhausted {get;private set;}
        public void Advance(double seconds)
        {
            if(Active||Exhausted||seconds<=0)return;
            Countdown=Math.Max(0,Countdown-seconds);
            if(Countdown==0){Active=true;Remaining=WaveSize;}
        }
        public void ConfirmSpawn(ulong count)
        {
            if(count>Remaining)throw new InvalidOperationException("Swarm spawn acknowledgement exceeded the remaining wave.");
            Remaining-=count;
        }
        public bool TryClear(uint alive,bool pending)
        {
            if(!Active||Remaining!=0||alive!=0||pending)return false;
            Active=false;
            if(WaveSize>ulong.MaxValue/2){Exhausted=true;return true;}
            Wave++;WaveSize*=2;Countdown=PreparationSeconds+WarningSeconds;return true;
        }
    }

    public sealed class SwarmMode
    {
        readonly RtsWorld world;
        readonly GpuBattle battle;
        public readonly SwarmSchedule Schedule=new SwarmSchedule();
        public string Notice {get;private set;}="Build your base and defences. First swarm in 10 minutes, followed by a 30-second warning.";
        public SwarmMode(RtsWorld world)
        {
            this.world=world;battle=world.Battle;
            var types=new[]{(uint)battle.ZombieType};
            if(types.Length==0)throw new InvalidOperationException("Swarm mode needs a land combat unit in the roster.");
            battle.ConfigureSwarm(types,SpawnSites(world.Field,world.BasePosition(0)));
        }
        // Several entries around the canonical map perimeter, with an island-perimeter
        // fallback when the coastline does not meet an edge. No inaccessible attackers.
        public static Vector2[] SpawnSites(Battlefield field,Vector2 home)
        {
            int n=field.Resolution,total=n*n,component=field.Deployment.Components[field.Map.Index(home)];
            var result=new List<Vector2>();float half=field.Size*.5f,cell=field.Size/n;
            for(int side=0;side<4;side++)for(int spot=0;spot<2;spot++)
            {
                float along=(spot==0?-.26f:.26f)*field.Size;
                Vector2 desired=side==0?new Vector2(-half+cell,along):side==1?new Vector2(half-cell,along):side==2?new Vector2(along,-half+cell):new Vector2(along,half-cell);
                float best=float.MaxValue;Vector2 found=home;bool valid=false;
                for(int i=0;i<total;i++)
                {
                    if(field.Deployment.Components[i]!=component||field.Deployment.Clearance[i]<2)continue;
                    if(field.Deployment.Costs[total+i]>=DeploymentMap.Blocked*.5f)continue;
                    Vector2 p=field.Map.Position(i);if(field.Distance(home,p)<Mathf.Min(field.Size*.24f,650))continue;
                    bool duplicate=false;for(int k=0;k<result.Count;k++)if(field.Distance(result[k],p)<cell*3){duplicate=true;break;}if(duplicate)continue;
                    float score=(p-desired).sqrMagnitude;
                    if(score<best){best=score;found=p;valid=true;}
                }
                if(valid)result.Add(found);
            }
            if(result.Count<2)throw new InvalidOperationException("Swarm mode needs at least two distant, reachable land entry sites. Choose a larger island or an all-land world.");
            return result.ToArray();
        }
        public void Update(float seconds)
        {
            if(battle.SwarmError.Length>0){world.Outcome=battle.SwarmError;return;}
            bool wasWarning=Schedule.Warning,wasActive=Schedule.Active;Schedule.Advance(seconds);
            if(!wasWarning&&Schedule.Warning)Notice=$"WARNING · SWARM {Schedule.Wave} · {Schedule.WaveSize:N0} attackers approaching";
            if(!wasActive&&Schedule.Active)Notice=$"SWARM {Schedule.Wave} · Hold the line!";
            if(Schedule.Active&&Schedule.Remaining>0&&!battle.SwarmPending)
            {
                uint count=(uint)Math.Min(16384UL,Schedule.Remaining);
                battle.RequestSwarm(count,unchecked((uint)(battle.Settings.seed+Schedule.Wave*7919)),spawned=>Schedule.ConfirmSpawn(spawned),Schedule.WaveSize);
            }
            if(Schedule.TryClear(battle.SwarmAlive,battle.SwarmPending))
                Notice=Schedule.Exhausted?"All representable swarms survived.":$"SWARM {Schedule.Wave-1} SURVIVED · Next wave: {Schedule.WaveSize:N0}. Rebuild your defences.";
        }
        public string Status=>Schedule.Active?$"SWARM {Schedule.Wave} · {battle.SwarmAlive:N0} alive · {Schedule.Remaining:N0} reinforcements pending":
            Schedule.Exhausted?"SWARM CHALLENGE COMPLETE":Schedule.Warning?$"WARNING · SWARM {Schedule.Wave} IN {Math.Ceiling(Schedule.Countdown):00}s · {Schedule.WaveSize:N0} ENEMIES":
            $"SWARM {Schedule.Wave} · Prepare {TimeText(Schedule.Countdown-SwarmSchedule.WarningSeconds)} · {Schedule.WaveSize:N0} enemies";
        static string TimeText(double seconds)=>$"{(int)Math.Ceiling(seconds)/60:00}:{(int)Math.Ceiling(seconds)%60:00}";
    }
}
