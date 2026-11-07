using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [Serializable]public sealed class RaiderCamp {
        public long id;public string name;public Vector2 position;public float health=1800;
        public bool deployed;public double nextRaid,stolen;public ResourceInventory loot=new ResourceInventory();
    }
    [Serializable]public sealed class FrontierIncident {public string description;public double time;public Vector2 position;}
    public sealed partial class CivilisationRuntime {
        readonly Dictionary<int,double> nextFrontierTick=new Dictionary<int,double>();
        internal void EnsureRaiderCamps(PlanetRtsBase local){
            if(local.World.ClassicCountryFixture||!Settings.raidersEnabled||local.Settlements==null||!Array.Exists(local.Settlements,x=>x!=null))return;var planet=Planet(local.World.definition.bodyId);if(planet.raidersInitialised)return;planet.raidersInitialised=true;
            for(int i=0;i<Settings.raiderCampCount;i++){
                var home=local.Homes[0];var other=local.Homes[1+i%Math.Max(1,local.Homes.Length-1)];if(other.sqrMagnitude<.5f)other=home;var preferred=Vector3.Slerp(home,other,.5f);
                foreach(var direction in PlanetStartingRegion.Candidates(local.World.definition,preferred)){
                    bool clear=true;foreach(var town in local.Homes)if(town.sqrMagnitude>.5f&&CubeSphere.ArcDistance(town,direction,local.World.definition.radius)<4000){clear=false;break;}
                    foreach(var camp in planet.raiders)if(CubeSphere.ArcDistance(CubeSphere.Decode(camp.position),direction,local.World.definition.radius)<2500){clear=false;break;}
                    if(!clear||PlanetTerrainEdits.BaseHeight(local.World.definition,direction)<local.World.definition.oceanHeight+10)continue;
                    var entry=new RaiderCamp{id=State.AllocateId(),name=new[]{"Dust Jackals","Iron Nomads","Ash Runners","Wasteland Reavers"}[i%4],position=CubeSphere.Encode(direction),nextRaid=State.time+90};entry.loot.Bind(Catalogue);planet.raiders.Add(entry);break;
                }
            }planet.pipelineRevision++;
        }
        internal void ResolveVisibleRaid(PlanetRtsBase local,CargoVehicleState vehicle,int campIndex){
            var planet=Planet(local.World.definition.bodyId);if(campIndex<0||campIndex>=planet.raiders.Count||vehicle.loaded<=0||SimulationTime-vehicle.lastRaided<60)return;var camp=planet.raiders[campIndex];if(camp.health<=0||!camp.deployed)return;vehicle.lastRaided=SimulationTime;
            var home=local.FindBuilding(vehicle.warehouse);var colony=home==null?null:local.Settlements[home.team];if(colony==null)return;int police=0;foreach(var p in colony.residents)if(p.alive&&p.patrolDuty)police++;
            if(PlanetResourceProfile.Random(camp.id+":"+vehicle.id+":"+vehicle.navigationVersion)>.6/(1+police*.35))return;
            camp.loot.Bind(Catalogue);double accepted=camp.loot.Deposit(vehicle.resource,vehicle.loaded*.35);vehicle.loaded-=accepted;camp.stolen+=accepted;
            planet.frontierIncidents.Add(new FrontierIncident{time=SimulationTime,position=camp.position,description=camp.name+" ambushed shipment #"+vehicle.id+": "+accepted.ToString("0.0")+" "+vehicle.resource});if(planet.frontierIncidents.Count>128)planet.frontierIncidents.RemoveAt(0);colony.security.requestedPatrols=Math.Min(32,colony.security.requestedPatrols+1);local.NotifyChanged();
        }
        internal void TickFrontier(PlanetRtsBase local,double seconds){
            if(seconds<=0||local.World.ClassicCountryFixture)return;var planet=Planet(local.World.definition.bodyId);TickOutbreaks(local,seconds);
            if(!Settings.raidersEnabled)return;EnsureRaiderCamps(local);
            if(nextFrontierTick.TryGetValue(planet.planetId,out double next)&&SimulationTime<next)return;
            // Fixed-rate strategic camp/mission decisions. No CPU army records or poses.
            nextFrontierTick[planet.planetId]=SimulationTime+Math.Max(1,Settings.frontierTickSeconds);
            foreach(var camp in planet.raiders){if(camp.health<=0)continue;camp.loot.Bind(Catalogue);var centre=CubeSphere.Decode(camp.position);
                if(!camp.deployed&&CubeSphere.ArcDistance(centre,local.World.InterestDirection,local.World.definition.radius)<Settings.frontierDetailRange){
                    int spawned=0;for(int i=0;i<4;i++){var east=Vector3.Cross(Mathf.Abs(centre.y)>.95f?Vector3.right:Vector3.up,centre).normalized;var point=(centre+east*((i-1.5f)*12/local.World.definition.radius)).normalized;if(local.World.TrySpawnHostileActor(0,point))spawned++;}camp.deployed=spawned>0;
                }
                if(SimulationTime<camp.nextRaid)continue;camp.nextRaid=SimulationTime+Math.Max(30,Settings.raiderRaidSeconds);
                CargoVehicleState best=null;ColonyState victim=null;Vector2 ambush=default;double value=0;
                foreach(var colony in local.Settlements)if(colony!=null)foreach(var truck in colony.cargoVehicles){
                    if(truck.funeral||truck.aerial||truck.seaborne||truck.phase!=CargoVehiclePhase.Delivering||truck.loaded<=0||truck.path.Count<2||SimulationTime-truck.lastRaided<60)continue;
                    // Search the planned corridor, never estimate/read a live GPU truck pose.
                    var from=CubeSphere.Decode(truck.path[0]);var to=CubeSphere.Decode(truck.path[truck.path.Count-1]);
                    for(int sample=1;sample<8;sample++){var point=Vector3.Slerp(from,to,sample/8f);if(CubeSphere.ArcDistance(point,centre,local.World.definition.radius)>Settings.raiderAmbushRange)continue;
                        bool town=false;foreach(var home in local.Homes)if(home.sqrMagnitude>.5f&&CubeSphere.ArcDistance(home,point,local.World.definition.radius)<2200){town=true;break;}if(town)continue;
                        double worth=truck.loaded*(truck.resource=="gold"||truck.resource=="currency"?10:truck.resource=="electronics"?3:1);if(worth<=value)continue;value=worth;best=truck;victim=colony;ambush=CubeSphere.Encode(point);
                    }
                }
                if(best==null)continue;
                // Local encounters resolve on the GPU; abstract theft is only allowed outside
                // the detailed area, so a visible lorry never loses cargo by a random roll.
                if(CubeSphere.ArcDistance(CubeSphere.Decode(ambush),local.World.InterestDirection,local.World.definition.radius)<Settings.frontierDetailRange){local.World.EncounterForValidation?.SetRaiderDestination(CubeSphere.Decode(ambush));continue;}
                best.lastRaided=SimulationTime;int patrols=0;foreach(var p in victim.residents)if(p.alive&&p.patrolDuty)patrols++;
                double chance=.45/(1+patrols*.35);if(PlanetResourceProfile.Random(camp.id+":"+best.id+":"+(long)SimulationTime)>chance)continue;
                double amount=Math.Min(best.loaded,Math.Max(1,best.loaded*.35));double accepted=camp.loot.Deposit(best.resource,amount);best.loaded-=accepted;camp.stolen+=accepted;
                var incident=new FrontierIncident{time=SimulationTime,position=ambush,description=camp.name+" intercepted "+accepted.ToString("0.0")+" "+best.resource+" from shipment #"+best.id};planet.frontierIncidents.Add(incident);if(planet.frontierIncidents.Count>128)planet.frontierIncidents.RemoveAt(0);
                victim.security.requestedPatrols=Math.Min(32,victim.security.requestedPatrols+1);local.NotifyChanged();
            }
        }
    }
}
