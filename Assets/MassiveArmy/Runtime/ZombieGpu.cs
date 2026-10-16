using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy
{
    public static class ZombieDefinition
    {
        public static float StartingRefugeRadius(float worldSize)=>Mathf.Min(800,worldSize*.25f);
        public static UnitArchetype Archetype => new UnitArchetype {speed=16,attackRange=2.4f,acquisitionRange=75,cooldown=.7f,health=160,damage=24,movement=0,weapon=0,turnSpeed=8,radius=.45f,aimTolerance=1.2f,nearDistance=180,farDistance=650};
        public static UnitTraits Traits => new UnitTraits {vision=75,detection=75,signature=1,targetMask=31,ammunition=100000,fuel=100000,scale=new Vector4(1,1,1,0)};
    }
    public sealed partial class GpuBattle
    {
        public int ZombieType {get;private set;}=-1;
        public uint ZombieAlive=>Statistics!=null?Statistics[112]:0;
        public uint AmbientZombieRemaining {get;private set;}
        GraphicsBuffer noiseA,noiseB,noiseEvents,noiseCount,noiseSeeds,hearingSeeds,hearingField,ambientSites,weaponNoise;
        ComputeShader zombieNoise;
        int ambientSiteCount;
        const int NoiseEvents=1024;
        void InitializeZombieNoise()
        {
            if(!Settings.baseBuilding||Settings.ambientZombies==0&&!Settings.swarmMode)return;
            rtsShader.EnableKeyword("ZOMBIES");
            zombieNoise=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/ZombieNoise"));
            int cells=Field.Resolution*Field.Resolution;
            noiseA=Buffer(cells,16,"Persistent zombie hearing flow A");noiseB=Buffer(cells,16,"Persistent zombie hearing flow B");
            noiseSeeds=Buffer(cells,4,"Atomic explosion seed winners");noiseSeeds.SetData(new uint[cells]);
            hearingSeeds=Buffer(cells,4,"Atomic physical hearing winners");hearingSeeds.SetData(new uint[cells]);hearingField=Buffer(cells,16,"Physical zombie hearing independent of terrain");hearingField.SetData(new Vector4[cells]);
            noiseA.SetData(new Vector4[cells]);noiseB.SetData(new Vector4[cells]);
            noiseEvents=Buffer(NoiseEvents,16,"Bounded GPU explosion events");noiseCount=Buffer(2,4,"GPU explosion event count");noiseCount.SetData(new uint[2]);
        }
        public void ConfigureZombies(RtsWorld world)
        {
            if(noiseA==null||ambientSites!=null)return;
            if(Settings.ambientZombies>0&&Settings.ambientZombies>Settings.capacity-256)throw new InvalidOperationException("The ambient zombie population needs additional GPU slots beyond the reserved player-production slots.");
            var sites=new List<Vector2>();int cells=Field.Resolution*Field.Resolution;
            // SpawnSwarm jitters within a disk of 0.65 navigation cells. Reserve
            // that margin as well, so actual spawns remain outside the refuge.
            float refuge=ZombieDefinition.StartingRefugeRadius(Field.Size)+Field.Size/Field.Resolution*.65f;
            for(int i=0;i<cells;i++){
                if(Field.Deployment.Costs[i]>=DeploymentMap.Blocked*.5f||Field.Deployment.Clearance[i]<1)continue;
                Vector2 p=Field.Map.Position(i);bool near=false;
                foreach(var b in world.Buildings)if(Field.Distance(p,b.position)<refuge){near=true;break;}
                if(!near)sites.Add(p);
            }
            if(sites.Count==0&&Settings.ambientZombies>0)throw new InvalidOperationException("No safe land exists for the ambient zombie population outside the starting-base refuge.");
            if(sites.Count==0)sites.Add(Vector2.zero);
            ambientSites=Buffer(sites.Count,8,"Immutable ambient zombie land sites");ambientSites.SetData(sites);ambientSiteCount=sites.Count;
            // Ambient individuals inhabit any safe land; wave sites are later replaced
            // by the survival scheduler's reachable invasion perimeter.
            ConfigureSwarm(new[]{(uint)ZombieType},new[]{sites[0]});
            AmbientZombieRemaining=(uint)Settings.ambientZombies;
        }
        void BindZombieNoise(int kernel)
        {
            if(noiseEvents==null)return;
            cmd.SetComputeBufferParam(shader,kernel,"_WeaponNoise",weaponNoise);
            cmd.SetComputeBufferParam(shader,kernel,"_NoiseEvents",noiseEvents);cmd.SetComputeBufferParam(shader,kernel,"_NoiseCount",noiseCount);
        }
        void BindZombieNoiseRts(int kernel)
        {
            if(noiseEvents==null)return;
            cmd.SetComputeBufferParam(rtsShader,kernel,"_NoiseEvents",noiseEvents);cmd.SetComputeBufferParam(rtsShader,kernel,"_NoiseCount",noiseCount);
            cmd.SetComputeBufferParam(rtsShader,kernel,"_NoiseField",noiseA);
            cmd.SetComputeBufferParam(rtsShader,kernel,"_HearingField",hearingField);
            cmd.SetComputeFloatParam(rtsShader,"_NoiseTime",Tick*BattleSettings.Tick);
        }
        void RecordZombieNoise()
        {
            if(zombieNoise==null||Tick%3!=0)return;
            int cells=Field.Resolution*Field.Resolution;
            cmd.BeginSample(GpuSamples.Get("Army.ZombieHearing"));
            foreach(string name in new[]{"ClearNoiseSeeds","SeedExplosionNoise","UpdateZombieHearing","RelaxZombieNoise","ClearNoiseEvents"}){
                int k=zombieNoise.FindKernel(name);
                cmd.SetComputeIntParam(zombieNoise,"_NavRes",Field.Resolution);cmd.SetComputeIntParam(zombieNoise,"_EventCapacity",NoiseEvents);
                cmd.SetComputeFloatParam(zombieNoise,"_WorldSize",Field.Size);cmd.SetComputeFloatParam(zombieNoise,"_NoiseTime",Tick*BattleSettings.Tick);
                foreach(var pair in new[]{("_NoiseEvents",noiseEvents),("_NoiseCount",noiseCount),("_Seeds",noiseSeeds),("_HearingSeeds",hearingSeeds),("_HearingField",hearingField),("_Costs",Field.Costs)})cmd.SetComputeBufferParam(zombieNoise,k,pair.Item1,pair.Item2);
                if(name=="RelaxZombieNoise")for(int pass=0;pass<8;pass++){
                    cmd.SetComputeBufferParam(zombieNoise,k,"_Read",pass%2==0?noiseA:noiseB);cmd.SetComputeBufferParam(zombieNoise,k,"_Write",pass%2==0?noiseB:noiseA);
                    cmd.DispatchCompute(zombieNoise,k,(cells+127)/128,1,1);
                }else cmd.DispatchCompute(zombieNoise,k,name=="SeedExplosionNoise"?NoiseEvents:name=="ClearNoiseSeeds"||name=="UpdateZombieHearing"?(cells+127)/128:(NoiseEvents+127)/128,1,1);
            }
            cmd.EndSample(GpuSamples.Get("Army.ZombieHearing"));
        }
        void DisposeZombieNoise(){foreach(var buffer in new[]{noiseA,noiseB,noiseEvents,noiseCount,noiseSeeds,hearingSeeds,hearingField,ambientSites,weaponNoise})buffer?.Dispose();ArmyLifetime.Destroy(zombieNoise);}
#if UNITY_EDITOR
        public void InjectZombieExplosion(ComputeShader validation,Vector2 position,float damage,float radius)
        {
            int k=validation.FindKernel("Explosion");validation.SetFloat("_WorldSize",Field.Size);validation.SetVector("_Explosion",new Vector4(position.x,position.y,damage,radius));validation.SetBuffer(k,"_NoiseEvents",noiseEvents);validation.SetBuffer(k,"_NoiseCount",noiseCount);validation.Dispatch(k,1,1,1);
        }
#endif
    }
}
