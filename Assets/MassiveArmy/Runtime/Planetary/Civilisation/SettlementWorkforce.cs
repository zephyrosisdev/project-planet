using System;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        // Strategic biographies assign job slots; live civilian and army movement stay on GPU.
        internal static bool Backbone(BuildingDefinition d)=>d.researchRate>0||d.id=="civil_launch_pad"||d.id=="civil_smelter"||d.id=="civil_electronics"||d.id=="light_depot"||d.id=="helipad"||d.id=="civil_port"||d.id=="civil_airport"||d.id=="civil_freight_station";
        internal static bool RecoveryEssential(BuildingDefinition definition)=>WorkerPriority(definition)==0||Backbone(definition)||definition.extraction>0||definition.generation>0||definition.id=="oil"||definition.id=="civil_oil_well"||definition.id=="civil_oil_refinery"||definition.id=="civil_mine_methane";
        internal static int ConstructionPriority(RtsBuilding building)=>!building.Complete?(RecoveryEssential(building.definition)?0:building.definition.housingCapacity>0?1:2):3;
        internal static int WorkerPriority(BuildingDefinition d){
            if(d.extractedResource=="water"||d.id.StartsWith("civil_farm_")||d.id=="civil_food_processing"||d.id=="civil_hydroponics"||d.id=="civil_beef_shed"||d.id=="civil_butcher")return 0;
            if(d.id==PoliceHeadquarters||d.id==Graveyard||d.extraction>0||Backbone(d))return 1;
            if(d.id.StartsWith("civil_"))return 2;
            return 3;
        }
        internal static void AllocateWorkers(PlanetRtsBase local,ColonyState colony,int team){
            int adults=0;foreach(var person in colony.residents)if(person.alive&&!Confined(person)&&person.age>=18&&person.age<75)adults++;
            colony.workforce=Math.Max(0,Math.Min(colony.population,colony.residents.Count==0?colony.population:adults));colony.employed=colony.workerDemand=colony.plannedWorkerDemand=0;
            foreach(var b in local.Buildings){if(b.team!=team||b.removed)continue;
                b.WorkersAssigned=0;b.WorkforceEfficiency=b.definition.workers==0?1:0;
                if(b.paused||!b.powered||b.roadSuspended||b.node>=0&&local.Nodes[b.node].remaining<=0)continue;
                int required=b.definition.workers*b.level;colony.plannedWorkerDemand+=required;
                if(b.Complete)colony.workerDemand+=required;
            }
            int projects=0;foreach(var b in local.Buildings)if(b.team==team&&!b.removed&&!b.paused&&!b.roadSuspended&&b.powered&&!b.awaitingProjectMaterials&&(!b.Complete||b.repairing&&b.health<b.MaxHealth))projects++;
            colony.workerDemand+=projects*2;int remaining=Math.Max(0,colony.workforce-(projects>0?Math.Min(4,Math.Max(2,projects/4)):0));
            // Share shortages fairly within a priority; food/water and extraction survive expansion.
            for(int priority=0;priority<4;priority++){
                int demand=0;foreach(var b in local.Buildings)if(Available(local,b,team,priority))demand+=b.definition.workers*b.level;
                int budget=Math.Min(remaining,demand),left=demand;
                foreach(var b in local.Buildings)if(Available(local,b,team,priority)){
                    int required=b.definition.workers*b.level;
                    int assigned=left==0?0:(int)((long)budget*required/left);
                    b.WorkersAssigned=assigned;b.WorkforceEfficiency=(float)assigned/required;
                    budget-=assigned;left-=required;remaining-=assigned;colony.employed+=assigned;
                }
            }
            MatchResidentJobs(local,colony);
        }
        static bool Available(PlanetRtsBase local,RtsBuilding b,int team,int priority)=>b.team==team&&b.Complete&&!b.paused&&!b.roadSuspended&&b.powered&&b.definition.workers>0&&(b.node<0||local.Nodes[b.node].remaining>0)&&WorkerPriority(b.definition)==priority;
        public static void ResourceTotals(ColonyState colony,string resource,out double stored,out double local,out double transit){
            stored=colony.inventory.Get(resource);local=transit=0;
            foreach(var item in colony.installations)if(!ReferenceEquals(item.inventory,colony.inventory)){if(item.definition=="civil_warehouse"||item.definition=="civil_market")stored+=item.inventory.Get(resource);else local+=item.inventory.Get(resource);}
            foreach(var cargo in colony.cargoVehicles)if(cargo.resource==resource)transit+=cargo.loaded+cargo.buffered+cargo.transferred;
        }
    }
}
