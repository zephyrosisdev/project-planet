using System;
using System.Collections;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        Coroutine routePlanning;long planningVehicle;int planningGeneration;
        void CancelRoutePlanning(long vehicle=0){if(vehicle!=0&&planningVehicle!=vehicle)return;planningGeneration++;planningVehicle=0;if(routePlanning!=null&&world!=null)world.StopCoroutine(routePlanning);routePlanning=null;}
        void BeginRoutePlanning(IndustrialMachine vehicle,long source,long destination,bool automatic,bool recovery,string roleOverride=null){
            if(planningVehicle!=0){message="Route planner is finishing vehicle #"+planningVehicle;return;}
            planningVehicle=vehicle.id;int generation=++planningGeneration;var coroutine=world.StartCoroutine(PlanLongRoute(vehicle,source,destination,automatic,recovery,generation,roleOverride));if(planningVehicle!=0&&generation==planningGeneration)routePlanning=coroutine;
        }
        IEnumerator PlanLongRoute(IndustrialMachine vehicle,long source,long destination,bool automatic,bool recovery,int generation,string roleOverride){
            try{
                var sources=automatic?AutomaticCollectionSources(vehicle):new List<long>{source};if(sources.Count>3)sources.RemoveRange(3,sources.Count-3);string role=roleOverride??(recovery?vehicle.haul?.role:DefaultVehicleRole(vehicle));
                IndustrialCommand command=null;string error="No accessible nonempty pay-dirt node on this body";
                foreach(long candidate in sources){var result=new IndustrialHaulNavigation.PlanningResult();var iterator=IndustrialHaulNavigation.PlanIncremental(Authority.World,vehicle.owner,vehicle.id,candidate,destination,ProjectTruckGround,result,recovery,role);
                    while(iterator.MoveNext()){
                        if(generation!=planningGeneration||vehicle.pose.body!=world.definition.bodyId)yield break;
                        message=$"Planning vehicle #{vehicle.id} · {result.plannedMetres:N0} m checked";yield return null;
                    }
                    command=result.command;error=result.error;if(command!=null)break;
                }
                if(generation!=planningGeneration)yield break;
                if(recovery){Authority.ReplanTruck(new IndustrialHaulReplan{truck=vehicle.id,source=command?.source??source,route=command?.route,pickupIndex=command?.routeSplit??0,error=error});}
                else if(command==null)message=error;
                else{command.operationRole=role;command.sequence=actor.lastSequence+1;command.definition=automatic?"auto-source":null;command.item=Authority.World.creativeMode?"creative-vehicle":"vehicle-operation";Execute(command);if(vehicle.haul?.active==true){trackedTruck=vehicle.id;CloseMenu();}}
            }finally{if(generation==planningGeneration){planningVehicle=0;routePlanning=null;}}
        }
    }
}
