using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    /// <summary>Session-owned strategic data. No per-unit records, physics objects or distant MonoBehaviours.</summary>
    public sealed partial class CivilisationRuntime {
        sealed class LocalColony {public PlanetRtsBase local;public ColonyState[] colonies;}
        public readonly CivilisationCatalogue Catalogue;public readonly CivilisationSettings Settings;
        public readonly SettlementLogistics Logistics;
        readonly SettlementDirector settlementDirector;
        public UniverseState State {get;private set;}
        readonly PlanetSystemRuntime system;readonly Dictionary<int,string> bodyIds=new Dictionary<int,string>();
        readonly Dictionary<int,LocalColony> locals=new Dictionary<int,LocalColony>();double accumulated,evaluationTime=-1;public long EconomyTicks {get;private set;}
        public int Revision {get;private set;}
        public double RenderTime=>State.time+Math.Min(accumulated,Math.Max(.25,Settings.economyTickSeconds));
        public double SimulationTime=>evaluationTime>=0?evaluationTime:State.time;
        public bool ReplayingEconomy {get;private set;}
        public double MaximumCatchupSliceMs {get;private set;}
        public bool CatchingUp(int planetId){var planet=Planet(planetId);return planet!=null&&State.time-planet.lastSimulatedTime>Math.Max(.25,Settings.economyTickSeconds)*1.5;}
        public CivilisationRuntime(PlanetSystemRuntime system,CivilisationSettings settings){
            this.system=system;Settings=settings;Catalogue=CivilisationCatalogue.Load();Logistics=new SettlementLogistics(this);settlementDirector=new SettlementDirector(this);State=new UniverseState{galaxySeed=system.Galaxy.Settings.seed};
            var galaxy=system.Galaxy;int[] ordinals=new int[galaxy.SystemNodes.Length];
            foreach(var node in galaxy.Nodes){
                if(node.kind==CelestialKind.Planet)node.stableId="g"+State.galaxySeed+"/s"+node.system+"/p"+(ordinals[node.system]++);
                else node.stableId="g"+State.galaxySeed+"/s"+node.system+"/"+node.kind+"/"+(node.kind==CelestialKind.Star?node.name:"");
            }
            for(int i=0;i<system.Bodies.Length;i++){
                var definition=system.Bodies[i];var node=galaxy.Nodes[galaxy.NodeFor(definition)];bodyIds.Add(definition.bodyId,node.stableId);
                var profile=PlanetResourceProfile.Generate(node.stableId,i==0,Catalogue,Settings);PlanetArchetype archetype=null;foreach(var item in Catalogue.archetypes)if(item.id==profile.archetype){archetype=item;break;}
                var environment=new CelestialEnvironment{atmosphere=i==0?definition.hasAtmosphere:archetype.pressure>0,breathable=i==0?definition.breathable:archetype.breathable,pressure=i==0?definition.surfacePressure:archetype.pressure,oxygen=i==0?definition.oxygenLevel:archetype.breathable?.21:0,temperature=archetype.temperature,habitability=archetype.breathable?1:.15};
                node.composition=profile;node.environment=environment;node.rotationRate=definition.rotationRate;
                State.planets.Add(new PlanetState{body=node.stableId,planetId=definition.bodyId,navigationContext=definition.bodyId,composition=profile,environment=environment});
            }
            for(int team=0;team<2;team++){var faction=new CivilisationFaction{id=team,name=team==0?"Cyan":"Red",autonomous=team==1||system.ActiveWorld.sessionMode==PlanetGameMode.AiTest};State.factions.Add(faction);var home=faction.knowledge.Record(bodyIds[system.Bodies[0].bodyId]);home.level=DiscoveryLevel.Surveyed;home.confidence=1;foreach(var abundance in State.planets[0].composition.abundance)home.resources.Add(new ResourceEstimate{resource=abundance.resource,estimate=abundance.amount,uncertainty=0});}
        }
        public string BodyId(int planetId)=>bodyIds.TryGetValue(planetId,out var id)?id:null;
        public bool Known(int faction,int planetId)=>State.Faction(faction)?.knowledge.Knows(BodyId(planetId))==true;
        public PlanetState Planet(int planetId)=>State.Planet(BodyId(planetId));
        public ColonyState Colony(string body,int faction){foreach(var colony in State.colonies)if(colony.body==body&&colony.faction==faction)return colony;return null;}
        public bool IsHome(int planetId)=>system.Bodies[0].bodyId==planetId;
        public void ObserveArrival(int planetId,int faction){var planet=Planet(planetId);var known=State.Faction(faction).knowledge.Record(planet.body);if(known.level<DiscoveryLevel.Environment)known.level=DiscoveryLevel.Environment;known.confidence=Math.Max(known.confidence,.95);known.lastObserved=State.time;Revision++;}
        public void Attach(PlanetRtsBase local,int planetId){
            var planet=Planet(planetId);var binding=new LocalColony{local=local,colonies=new ColonyState[2]};
            local.Roads=new SettlementRoadNetwork(local.World,local,planet.roads,Settings.roadTileSize);
            bool seedRoads=planet.roads.Count==0;
            for(int team=0;team<2;team++){
                var colony=Colony(planet.body,team);bool fresh=colony==null;
                if(fresh&&IsHome(planetId)&&(team==0||system.ActiveWorld.sessionMode!=PlanetGameMode.Swarm)){colony=new ColonyState{id=State.AllocateId(),faction=team,body=planet.body,name=State.Faction(team).name+" home base",stage=ColonyStage.Industrial,population=Settings.startingPopulation};State.colonies.Add(colony);planet.colonies.Add(colony.id);}
                if(colony==null)continue;colony.inventory.Bind(Catalogue,local.Factions[team].wallet);
                if(fresh){foreach(var resource in Catalogue.resources)if(resource.startingStock>0)colony.inventory.Deposit(resource.id,resource.startingStock);}else colony.inventory.RestoreWallet();
                foreach(var tech in State.Faction(team).technologies)local.Factions[team].technologies.Add(tech);binding.colonies[team]=colony;
            }local.Settlements=binding.colonies;locals.Add(planetId,binding);TickLocal(binding,0);if(planet.colonies.Count==0||State.time==0)planet.lastSimulatedTime=State.time;
            for(int team=0;team<2;team++){
                var colony=binding.colonies[team];if(colony==null)continue;RtsBuilding warehouse=null;foreach(var b in local.Buildings)if(b.team==team&&b.definition.id=="civil_warehouse"){warehouse=b;break;}
                if(warehouse!=null&&colony.cargoVehicles.Count==0)colony.cargoVehicles.Add(new CargoVehicleState{id=State.AllocateId(),warehouse=warehouse.persistentId});
                if(IsHome(planetId)&&seedRoads&&warehouse!=null)foreach(var b in local.Buildings)if(b.team==team&&b!=warehouse)local.Roads.Connect(b,warehouse,out _,true);
                if(seedRoads)foreach(var item in colony.installations){if(ReferenceEquals(item.inventory,colony.inventory))continue;double food=0;var definition=local.Catalog.Buildings[item.definition];food=definition.foodUse*Settings.buildingInputReserveSeconds;if(definition.housingCapacity>0)food+=Settings.startingPopulation*Settings.populationFoodPerSecond*Settings.buildingInputReserveSeconds;food=Math.Min(food,colony.inventory.Get("food"));colony.inventory.Spend("food",food);item.inventory.Deposit("food",food);}
            }Revision++;
        }
        public void Advance(double elapsed){
            if(elapsed<0||!double.IsFinite(elapsed))throw new ArgumentOutOfRangeException(nameof(elapsed));accumulated+=elapsed;
            int budget=Math.Max(1,Settings.maximumCatchupTicks);double step=Math.Max(.25,Settings.economyTickSeconds);
            while(accumulated+1e-9>=step&&budget-->0){accumulated-=step;State.time+=step;EconomyTicks++;Revision++;}
            var world=system.ActiveWorld;
            // No sweep over inactive planets, settlements, roads, factories or vehicles.
            if(world==null||!world.SimulationActive||!locals.TryGetValue(world.definition.bodyId,out var active))return;
            var planet=Planet(world.definition.bodyId);double missing=State.time-planet.lastSimulatedTime;if(missing<=1e-9)return;
            if(missing<=step*1.5){evaluationTime=State.time;TickLocal(active,missing);settlementDirector.Tick(active.local);planet.lastSimulatedTime=State.time;evaluationTime=-1;return;}
            ReplayingEconomy=true;var timer=System.Diagnostics.Stopwatch.StartNew();int work=Math.Max(1,Settings.catchupStepsPerFrame);
            while(planet.lastSimulatedTime<State.time-1e-9&&work-->0){
                double duration=Math.Min(State.time-planet.lastSimulatedTime,Math.Max(step,Settings.catchupMaximumStepSeconds));
                foreach(var colony in active.colonies)if(colony!=null){foreach(var vehicle in colony.cargoVehicles)if(vehicle.phase!=CargoVehiclePhase.Idle&&vehicle.arrival>planet.lastSimulatedTime+1e-6)duration=Math.Min(duration,vehicle.arrival-planet.lastSimulatedTime);foreach(var item in colony.installations)if(item.planted&&item.growth<1)foreach(var crop in Catalogue.crops)if(crop.id==item.crop)duration=Math.Min(duration,Math.Max(step,(1-item.growth)*crop.growthSeconds));}
                evaluationTime=planet.lastSimulatedTime+duration;TickLocal(active,duration);active.local.AdvanceDormantInfrastructure((float)duration);settlementDirector.Tick(active.local);planet.lastSimulatedTime=evaluationTime;
                if(timer.Elapsed.TotalMilliseconds>=Math.Max(.1,Settings.catchupBudgetMilliseconds))break;
            }
            evaluationTime=-1;ReplayingEconomy=false;timer.Stop();MaximumCatchupSliceMs=Math.Max(MaximumCatchupSliceMs,timer.Elapsed.TotalMilliseconds);
            // Excess time remains queued, rather than being silently discarded at accelerated speeds.
        }
        void TickLocal(LocalColony binding,double step){
            var local=binding.local;
            for(int team=0;team<2;team++){
                var colony=binding.colonies[team];if(colony==null)continue;var faction=State.Faction(team);foreach(var tech in local.Factions[team].technologies)if(!faction.technologies.Contains(tech))faction.technologies.Add(tech);
                AllocateWorkers(local,colony,team);
                local.RefreshSettlementPower(team);
                colony.powerBalance=local.Factions[team].generation-local.Factions[team].demand;
                colony.powerGeneration=local.Factions[team].generation;colony.powerDemand=local.Factions[team].demand;
                colony.housingCapacity=colony.housedPopulation=0;colony.foodDemand=colony.foodSupplied=0;int unhoused=colony.population;
                bool primaryWarehouse=false;
                foreach(var building in local.Buildings){
                    if(building.team!=team||building.removed)continue;
                    if(building.persistentId==0)building.persistentId=State.AllocateId();var installation=FindInstallation(colony,building.persistentId);
                    if(installation==null){installation=new SurfaceInstallation{id=building.persistentId,definition=building.definition.id};installation.inventory.Bind(Catalogue);colony.installations.Add(installation);State.Planet(colony.body).surfaceInstallations.Add(installation.id);}
                    if(building.definition.id=="civil_warehouse"&&!primaryWarehouse){installation.inventory=colony.inventory;primaryWarehouse=true;}
                    installation.position=building.position;installation.health=building.health;installation.construction=building.construction;installation.level=building.level;installation.paused=building.paused;installation.powered=building.powered;installation.workersAssigned=building.WorkersAssigned;installation.workersRequired=building.definition.workers*building.level;
                    if(!building.Complete||building.paused||!building.powered)continue;
                    int residents=Math.Min(unhoused,building.definition.housingCapacity*building.level);unhoused-=residents;
                    double demand=building.definition.foodUse*building.level+residents*Settings.populationFoodPerSecond;colony.foodDemand+=demand;
                    double needed=demand*step,available=Math.Min(needed,installation.inventory.Get("food"));if(available>0)installation.inventory.Spend("food",available);
                    building.FoodEfficiency=needed>0?(float)(available/needed):1;colony.foodSupplied+=step>0?available/step:demand;
                    double efficiency=local.Factions[team].efficiency*building.FoodEfficiency*building.WorkforceEfficiency*building.level;
                    building.Efficiency=(float)(efficiency/building.level);
                    if(building.definition.housingCapacity>0){colony.housingCapacity+=building.definition.housingCapacity*building.level;colony.housedPopulation+=residents;}
                    if(building.node>=0){var node=local.Nodes[building.node];string resource=node.elementId;if(string.IsNullOrEmpty(resource))foreach(var definition in Catalogue.resources)if(definition.legacy==(int)node.resource){resource=definition.id;break;}
                        double extracted=Math.Min(node.remaining,building.definition.extraction*node.rateMultiplier*efficiency*step);node.remaining-=(float)installation.inventory.Deposit(resource,extracted);
                    }
                    if(string.IsNullOrEmpty(installation.recipe)){foreach(var recipe in Catalogue.recipes)if(recipe.building==building.definition.id){installation.recipe=recipe.id;break;}}
                    foreach(var recipe in Catalogue.recipes)if(recipe.id==installation.recipe&&recipe.building==building.definition.id&&(string.IsNullOrEmpty(recipe.requiredTech)||faction.technologies.Contains(recipe.requiredTech)))installation.inventory.RunRecipe(recipe,step,efficiency);
                    if(building.definition.id.StartsWith("civil_farm_")&&efficiency>0)GrowCrop(installation,State.Planet(colony.body),step*efficiency);
                }
                for(int i=colony.installations.Count-1;i>=0;i--){bool alive=false;foreach(var b in local.Buildings)if(b.persistentId==colony.installations[i].id&&!b.removed){alive=true;break;}if(!alive){State.Planet(colony.body).surfaceInstallations.Remove(colony.installations[i].id);colony.installations.RemoveAt(i);}}
                ServeRetail(local,colony,step);
                colony.inventory.CaptureWallet();
                Logistics.Tick(local,colony,step);
                if(unhoused>0){double demand=unhoused*Settings.populationFoodPerSecond,food=Math.Min(colony.inventory.Get("food"),demand*step);colony.inventory.Spend("food",food);colony.foodDemand+=demand;colony.foodSupplied+=step>0?food/step:demand;}
                colony.foodBalance=colony.foodSupplied-colony.foodDemand;
                if(colony.housedPopulation>=colony.population&&colony.housingCapacity>colony.population&&colony.foodBalance>=-1e-6&&colony.powerBalance>=0){colony.growthProgress+=step;double interval=Math.Max(1,Settings.populationGrowthSeconds);int arrivals=Math.Min(colony.housingCapacity-colony.population,(int)Math.Min(int.MaxValue,colony.growthProgress/interval));colony.population+=arrivals;colony.growthProgress-=arrivals*interval;}
            }
        }
        void GrowCrop(SurfaceInstallation installation,PlanetState planet,double step){
            if(string.IsNullOrEmpty(installation.crop))installation.crop=installation.definition.Substring("civil_farm_".Length);
            CropDefinition crop=null;foreach(var item in Catalogue.crops)if(item.id==installation.crop){crop=item;break;}if(crop==null)return;
            installation.soilFertility=Math.Min(1,installation.soilFertility+Settings.soilRecoveryPerSecond*step);
            if(!planet.environment.atmosphere||Math.Abs(planet.environment.temperature-crop.temperature)>crop.tolerance)return;
            if(!installation.planted){if(!installation.inventory.Spend("seeds",crop.seedCost))return;installation.planted=true;}
            var climate=PlanetClimate.Sample(CubeSphere.Decode(installation.position),planet.planetId*.017f);double waterRate=crop.waterPerSecond*(1-climate.y*Settings.farmRainEfficiency);
            double growing=waterRate>0?Math.Min(step,installation.inventory.Get("water")/waterRate):step;if(growing<=0)return;installation.inventory.Spend("water",growing*waterRate);
            if(installation.soilFertility<.6&&installation.inventory.Get("fertiliser")>=.1*growing){installation.inventory.Spend("fertiliser",.1*growing);installation.soilFertility=Math.Min(1,installation.soilFertility+.01*growing);}
            installation.growth=Math.Min(1,installation.growth+growing/crop.growthSeconds);
            if(installation.growth<1)return;double yield=Math.Max(.2,installation.soilFertility);
            foreach(var output in crop.harvest)if(installation.inventory.Capacity(output.resource)-installation.inventory.Get(output.resource)<output.amount*yield)return;
            foreach(var output in crop.harvest)installation.inventory.Deposit(output.resource,output.amount*yield);
            installation.soilFertility=Math.Max(0,installation.soilFertility-crop.soilUse);installation.growth=0;installation.planted=false;installation.harvests++;
        }
        static SurfaceInstallation FindInstallation(ColonyState colony,long id){foreach(var item in colony.installations)if(item.id==id)return item;return null;}
        public string RecipeFor(int planetId,RtsBuilding building){var colony=Colony(BodyId(planetId),building.team);return colony==null?null:FindInstallation(colony,building.persistentId)?.recipe;}
        public bool SetRecipe(int planetId,RtsBuilding building,string recipeId,int commandingFaction){
            if(building.team!=commandingFaction||!building.Complete)return false;var colony=Colony(BodyId(planetId),building.team);var installation=colony==null?null:FindInstallation(colony,building.persistentId);if(installation==null)return false;
            foreach(var recipe in Catalogue.recipes)if(recipe.id==recipeId&&recipe.building==building.definition.id&&(string.IsNullOrEmpty(recipe.requiredTech)||State.Faction(commandingFaction).technologies.Contains(recipe.requiredTech))){installation.recipe=recipeId;Revision++;return true;}return false;
        }
        public string Serialize(){foreach(var colony in State.colonies)colony.inventory.CaptureWallet();return JsonUtility.ToJson(State,true);}
        public void Save(string path){string content=Serialize();string temporary=path+".tmp";File.WriteAllText(temporary,content);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
        public static UniverseState Deserialize(string json,int expectedSeed,CivilisationCatalogue catalogue){
            var state=JsonUtility.FromJson<UniverseState>(json);if(state==null||state.schemaVersion!=1||state.galaxySeed!=expectedSeed)throw new InvalidDataException("Civilisation save version or galaxy seed mismatch");
            var ids=new HashSet<long>();foreach(var colony in state.colonies){if(!ids.Add(colony.id)||state.Planet(colony.body)==null||state.Faction(colony.faction)==null)throw new InvalidDataException("Invalid saved colony identity");colony.inventory.Bind(catalogue);foreach(var value in colony.inventory.stock)if(!catalogue.Resources.ContainsKey(value.resource)||!double.IsFinite(value.amount)||value.amount<0)throw new InvalidDataException("Invalid saved stockpile");}return state;
        }
    }
}
