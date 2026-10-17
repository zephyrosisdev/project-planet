using System;
namespace MassiveArmy.Planetary {
    public sealed partial class CivilisationRuntime {
        // Settlement workforce uses aggregate civilians only. No worker GameObjects or army readbacks.
        internal static int WorkerPriority(BuildingDefinition d){
            if(d.extractedResource=="water"||d.id.StartsWith("civil_farm_")||d.id=="civil_food_processing"||d.id=="civil_hydroponics"||d.id=="civil_beef_shed"||d.id=="civil_butcher")return 0;
            if(d.extraction>0)return 1;
            if(d.id.StartsWith("civil_"))return 2;
            return 3;
        }
        internal static void AllocateWorkers(PlanetRtsBase local,ColonyState colony,int team){
            colony.workforce=Math.Max(0,colony.population);colony.employed=colony.workerDemand=colony.plannedWorkerDemand=0;
            foreach(var b in local.Buildings){if(b.team!=team||b.removed)continue;
                b.WorkersAssigned=0;b.WorkforceEfficiency=b.definition.workers==0?1:0;
                if(b.paused||!b.powered||b.node>=0&&local.Nodes[b.node].remaining<=0)continue;
                int required=b.definition.workers*b.level;colony.plannedWorkerDemand+=required;
                if(b.Complete)colony.workerDemand+=required;
            }
            int remaining=colony.workforce;
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
        }
        static bool Available(PlanetRtsBase local,RtsBuilding b,int team,int priority)=>b.team==team&&b.Complete&&!b.paused&&b.powered&&b.definition.workers>0&&(b.node<0||local.Nodes[b.node].remaining>0)&&WorkerPriority(b.definition)==priority;
        public static void ResourceTotals(ColonyState colony,string resource,out double stored,out double local,out double transit){
            stored=colony.inventory.Get(resource);local=transit=0;
            foreach(var item in colony.installations)if(!ReferenceEquals(item.inventory,colony.inventory))local+=item.inventory.Get(resource);
            foreach(var cargo in colony.cargoVehicles)if(cargo.resource==resource)transit+=cargo.loaded;
        }
    }
}
