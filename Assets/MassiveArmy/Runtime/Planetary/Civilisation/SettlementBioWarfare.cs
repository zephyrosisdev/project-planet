using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [Serializable]public sealed class SettlementOutbreak {
        public int faction,residentCursor;public double infected,zombies,gpuZombies,lastUpdate;public Vector2 epicentre;
        // Strategic population estimates are kept separate from GPU actor counts.
        public double conversionRemainder;public List<Vector2> pendingConversions=new List<Vector2>();
    }
    public sealed partial class CivilisationRuntime {
        public bool BioWarfareAvailable(int faction)=>State.Faction(faction)?.technologies.Contains("civil_infection_weapon")==true;
        internal void StartOutbreak(PlanetRtsBase local,Vector3 target,float radius){
            var planet=Planet(local.World.definition.bodyId);planet.biowarfareReleased=true;
            foreach(var c in local.Settlements)if(c!=null){
                var hq=local.Buildings.Find(x=>!x.removed&&x.team==c.faction&&x.definition.id=="command");if(hq==null)continue;
                float distance=CubeSphere.ArcDistance(CubeSphere.Decode(hq.position),target,local.World.definition.radius);if(distance>radius+1500)continue;
                var outbreak=planet.outbreaks.Find(x=>x.faction==c.faction);if(outbreak==null){outbreak=new SettlementOutbreak{faction=c.faction,epicentre=CubeSphere.Encode(target),lastUpdate=SimulationTime};planet.outbreaks.Add(outbreak);}
                // Off-screen blast exposure uses the settlement footprint/population estimate.
                if(CubeSphere.ArcDistance(target,local.World.InterestDirection,local.World.definition.radius)>Settings.frontierDetailRange)outbreak.infected=Math.Min(c.population,outbreak.infected+Math.Max(1,c.population*Math.Min(1,radius*radius/(1500*1500))));
            }local.NotifyChanged();
        }
        internal void QueueInfectedCivilian(PlanetRtsBase local,ColonyState colony,Vector2 place){
            var planet=Planet(local.World.definition.bodyId);if(!planet.biowarfareReleased)return;
            var outbreak=planet.outbreaks.Find(x=>x.faction==colony.faction);if(outbreak==null){outbreak=new SettlementOutbreak{faction=colony.faction,epicentre=place,lastUpdate=SimulationTime};planet.outbreaks.Add(outbreak);}
            outbreak.pendingConversions.Add(place);
        }
        internal void TickOutbreaks(PlanetRtsBase local,double dt){
            var planet=Planet(local.World.definition.bodyId);if(!planet.biowarfareReleased)return;
            foreach(var outbreak in planet.outbreaks){var c=local.Settlements[outbreak.faction];if(c==null)continue;
                var centre=CubeSphere.Decode(outbreak.epicentre);bool detailed=CubeSphere.ArcDistance(centre,local.World.InterestDirection,local.World.definition.radius)<Settings.frontierDetailRange;
                // Conversion events are bounded; each dead citizen becomes one GPU actor.
                for(int i=outbreak.pendingConversions.Count-1,work=0;i>=0&&work<4;i--,work++)if(local.World.TrySpawnHostileActor(31,CubeSphere.Decode(outbreak.pendingConversions[i])))outbreak.pendingConversions.RemoveAt(i);
                if(detailed&&local.World.SimulationActive&&!ReplayingEconomy){for(int i=0;i<4&&outbreak.zombies>=1;i++)if(local.World.TrySpawnHostileActor(31,centre))outbreak.zombies--;else break;outbreak.lastUpdate=SimulationTime;continue;}
                double elapsed=SimulationTime-outbreak.lastUpdate;if(elapsed<Settings.distantOutbreakTickSeconds)continue;outbreak.lastUpdate=SimulationTime;
                double area=Math.Max(.1,c.installations.Count*.008),density=c.population/area;
                double susceptible=Math.Max(0,c.population-outbreak.infected);
                double exposure=(outbreak.infected+outbreak.zombies+outbreak.gpuZombies+outbreak.pendingConversions.Count)*Settings.infectionContactRate*Math.Min(4,density/100)*elapsed;
                outbreak.infected=Math.Min(c.population,outbreak.infected+Math.Min(susceptible,exposure));
                double converted=Math.Min(outbreak.infected,outbreak.infected*(1-Math.Exp(-elapsed/Math.Max(1,Settings.infectionConversionSeconds))));
                outbreak.conversionRemainder+=converted;int deaths=Math.Min(c.population,(int)outbreak.conversionRemainder);
                // Identity/death bookkeeping is bounded separately from the aggregate model.
                int workBudget=16;for(int scanned=0;scanned<64&&deaths>0&&workBudget>0&&c.residents.Count>0;scanned++){outbreak.residentCursor%=c.residents.Count;var person=c.residents[outbreak.residentCursor++];if(!person.alive)continue;workBudget--;
                    RecordDeath(c,person,DeathCause.Infection,SimulationTime,outbreak.epicentre);deaths--;outbreak.conversionRemainder--;outbreak.infected=Math.Max(0,outbreak.infected-1);outbreak.zombies++;
                }
            }
        }
    }
    public sealed partial class PlanetDevelopment {
        public bool BiologicalTargetArmed;
        public bool LaunchBiologicalWeapon(Vector3 target){BiologicalTargetArmed=false;var source=Base?.Selected;
            if(source==null||source.definition.id!="silo"||!source.Complete||source.paused||!source.powered||!Base.Factions[source.team].HasTech("civil_infection_weapon")||CubeSphere.ArcDistance(CubeSphere.Decode(source.position),target,definition.radius)>source.definition.weaponRange){Base.Notice="Biological launch unavailable: research, operational silo and range required";return false;}
            if(!Base.Factions[source.team].wallet.Spend(new ResourceCost{fuel=100,rareElements=50})){Base.Notice="Biological payload requires 100 fuel and 50 rare elements";return false;}
            encounter.ReleaseBiologicalWeapon(target,450);Base.Notice="BIOLOGICAL PAYLOAD RELEASED · infection outbreak";return true;
        }
        internal bool TrySpawnHostileActor(int type,Vector3 direction){
            if(Base==null||unitCount>=capacity-256||!SimulationActive)return false;
            var actor=type==31?ZombieDefinition.Archetype:Base.Catalog.units[type].Gpu;
            if(surface.SampleHeight(direction)<definition.oceanHeight||!IsFootprintClear(direction,actor.radius,false))return false;
            int kernel=movement.FindKernel("SpawnCatalog");movement.SetBuffer(kernel,"_PlanetUnits",units);movement.SetBuffer(kernel,"_Archetypes",archetypes);movement.SetInt("_PlanetSpawnSlot",unitCount);movement.SetInt("_PlanetSpawnType",type);movement.SetInt("_PlanetSpawnTeam",2);movement.SetInt("_PlanetSpawnGroup",17);movement.SetInt("_PlanetSpawnFlags",type==31?0:16777216);movement.SetVector("_PlanetStart",CubeSphere.Encode(direction));movement.Dispatch(kernel,1,1,1);encounter?.InitialiseActorOrigin(unitCount,direction);unitCount++;SetArmyCount(unitCount);return true;
        }
    }
    public sealed partial class PlanetEncounter {
        UnityEngine.GraphicsBuffer outbreakCounts;double nextOutbreakCount;bool outbreakReadPending;
        readonly Vector4[] outbreakRegions=new Vector4[8];
        void RefreshOutbreakCounts(){
            var sim=world.Base.Civilisation;var planet=sim?.Planet(world.definition.bodyId);if(planet==null||!planet.biowarfareReleased||planet.outbreaks.Count==0||outbreakReadPending||sim.RenderTime<nextOutbreakCount)return;
            nextOutbreakCount=sim.RenderTime+10;int count=Math.Min(8,planet.outbreaks.Count);var snapshot=planet.outbreaks.GetRange(0,count).ToArray();
            if(outbreakCounts==null)outbreakCounts=Buffer(8,4);outbreakCounts.SetData(new uint[8]);for(int i=0;i<count;i++){var d=CubeSphere.Decode(snapshot[i].epicentre);outbreakRegions[i]=new Vector4(d.x,d.y,d.z,3000);}
            int kernel=shader.FindKernel("CountOutbreakActors");shader.SetBuffer(kernel,"_UnitRead",world.ArmyForCivicCombat);shader.SetBuffer(kernel,"_BioCounts",outbreakCounts);shader.SetVectorArray("_BioRegions",outbreakRegions);shader.SetInt("_BioRegionCount",count);shader.SetInt("_Count",world.unitCount);shader.Dispatch(kernel,Math.Max(1,(world.unitCount+127)/128),1,1);outbreakReadPending=true;
            UnityEngine.Rendering.AsyncGPUReadback.Request(outbreakCounts,result=>{outbreakReadPending=false;if(disposed||result.hasError)return;var counts=result.GetData<uint>();for(int i=0;i<count;i++)snapshot[i].gpuZombies=counts[i];});
        }
        internal void InitialiseActorOrigin(int slot,Vector3 direction){int kernel=shader.FindKernel("InitialiseActorOrigin");shader.SetBuffer(kernel,"_RoamOrigins",roamOrigins);shader.SetInt("_OriginSlot",slot);shader.SetVector("_OriginPosition",CubeSphere.Encode(direction));shader.Dispatch(kernel,1,1,1);}
        internal void SetRaiderDestination(Vector3 direction)=>navigation.SetGoalQueued(17,direction,MovementClass.Infantry);
        internal void ReleaseBiologicalWeapon(Vector3 target,float radius){
            shader.EnableKeyword("PLANET_BIOWARFARE");shader.SetVector("_BioBlast",target);shader.SetFloat("_BioRadius",radius);shader.SetInt("_Count",world.unitCount);
            int kernel=shader.FindKernel("BiologicalBlast");shader.SetBuffer(kernel,"_RoamOrigins",roamOrigins);shader.SetBuffer(kernel,"_Units",world.ArmyForCivicCombat);shader.SetBuffer(kernel,"_Types",world.ArchetypesForCivicCombat);shader.Dispatch(kernel,Math.Max(1,(world.unitCount+127)/128),1,1);
            world.Base.Civilisation?.StartOutbreak(world.Base,target,radius);world.Base.Citizens?.BiologicalBlast(target,radius);nextOutbreakCount=0;RefreshOutbreakCounts();
        }
    }
}

namespace MassiveArmy.Planetary {
    public sealed partial class SettlementCitizens {
        internal void BiologicalBlast(Vector3 target,float radius){
            if(civicStates==null||Count==0)return;int kernel=shader.FindKernel("BiologicalExposure");shader.SetBuffer(kernel,"_CivicStates",civicStates);shader.SetVector("_BioBlast",target);shader.SetFloat("_BioRadius",radius);shader.SetFloat("_BioPlanetRadius",world.definition.radius);shader.SetFloat("_Clock",(float)world.SystemRuntime.Civilisation.RenderTime);shader.SetInt("_WalkerCount",Count);shader.Dispatch(kernel,(Count+127)/128,1,1);ReadDeaths(reportedDeaths+(uint)Count);
        }
    }
}
