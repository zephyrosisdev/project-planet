using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        long haulSourceSelection,haulDestinationSelection,trackedWater,trackedTruck;double nextTruckReplan;
        void LocateWater(){SurfaceDeposit best=null;double distance=double.MaxValue;foreach(var d in Authority.World.deposits)if(d.resource=="water"&&d.remaining.litres>0&&actor.pose.Distance(d.pose)<distance){distance=actor.pose.Distance(d.pose);best=d;}if(best==null){message="No nonempty WATER deposit on this body";return;}trackedWater=best.id;message="Tracking WATER intake #"+best.id+". Place a pump within 8 m, then select Use water intake.";CloseMenu();}
        string TruckTracking(){var truck=Authority.World.Machine(trackedTruck);return truck?.haul==null||truck.pose.body!=actor.pose.body?"":$"\nVEHICLE #{truck.id} · {truck.haul.status}\nCargo {IndustrialWorld.HaulCargo(truck):N0} L · {actor.pose.Distance(truck.pose):0} m away";}
        string WaterDirection(){var water=Authority.World.Deposit(trackedWater);if(water==null||water.pose.body!=actor.pose.body)return "";var delta=(Body(water.pose)-Body(actor.pose)).Float;var up=PlanetPrecisionPolicy.Direction(Body(actor.pose));float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(world.View.transform.forward,up),Vector3.ProjectOnPlane(delta,up),up);return $"\nWATER INTAKE #{water.id} · {delta.magnitude:0} m · "+(Mathf.Abs(angle)<15?"ahead":angle<0?$"turn left {-angle:0}°":$"turn right {angle:0}°")+$" · {water.remaining.litres:N0} L remaining";}

        FramePose? ProjectTruckGround(FramePose pose){
            if(surface==null)return pose;var p=Body(pose);double height=surface.SampleHeight(p);if(height<world.definition.oceanHeight+.2)return null;
            var at=surface.GetSurfacePositionPrecise(PlanetPrecisionPolicy.Direction(p),.06);pose.x=at.x;pose.y=at.y;pose.z=at.z;return pose;
        }
        IndustrialCommand PlanAutomaticCollection(IndustrialMachine vehicle,long destination,out string error,bool remote=false,string roleOverride=null){
            var candidates=AutomaticCollectionSources(vehicle);error="No accessible nonempty pay-dirt node on this body";for(int i=0;i<Math.Min(3,candidates.Count);i++){var command=IndustrialHaulNavigation.Plan(Authority.World,vehicle.owner,vehicle.id,candidates[i],destination,ProjectTruckGround,out error,remote,remote?vehicle.haul?.role:roleOverride??DefaultVehicleRole(vehicle));if(command!=null)return command;}return null;
        }
        void RecoverTruckRoutes(){
            if(planningVehicle!=0||world.Paused||surface==null||Time.unscaledTimeAsDouble<nextTruckReplan)return;
            nextTruckReplan=Time.unscaledTimeAsDouble+.5;foreach(var truck in Authority.World.machines){var job=truck.haul;if(job?.active!=true||!truck.enabled||truck.integrity<=0||job.nextReplanTick>Authority.World.tick||truck.pose.body!=world.definition.bodyId)continue;
                bool switchWorksite=false;if(job.waitingForExcavator){var sites=Authority.World.ActiveMiningSites(truck.owner,truck.pose);if(sites.Contains(job.source)){if(Authority.World.MiningLoaderPose(job.source,truck.owner,truck.pose,out var loading)&&truck.pose.Distance(loading)<=12)continue;}else{if(!job.automaticSource||sites.Count==0)continue;switchWorksite=true;}}
                bool exhausted=switchWorksite||job.automaticSource&&truck.input.bulk.litres==0&&(job.role=="excavate"?Authority.World.RawSourceRemaining(job.source)==0:!Authority.World.PayDirtAvailable(job.source))&&(job.phase==1||job.blockedTicks>=20);if(!exhausted&&job.blockedTicks<20)continue;
                long source=exhausted?Authority.World.NearestPayDirt(truck.pose):job.source;
                nextTruckReplan=Time.unscaledTimeAsDouble+1;if(world.isActiveAndEnabled){BeginRoutePlanning(truck,source,job.destination,exhausted,true);break;}string error;var command=exhausted?PlanAutomaticCollection(truck,job.destination,out error,true):IndustrialHaulNavigation.Plan(Authority.World,truck.owner,truck.id,source,job.destination,ProjectTruckGround,out error,true,job.role);
                Authority.ReplanTruck(new IndustrialHaulReplan{truck=truck.id,source=command?.source??source,route=command?.route,pickupIndex=command?.routeSplit??0,error=error});break;
            }
        }
        void AssignTruckCollection(IndustrialMachine truck)=>AssignTruckCollectionRole(truck,null);
        void AssignTruckCollectionRole(IndustrialMachine truck,string roleOverride){
            if(Driving){message="Leave the vehicle before assigning autonomous collection";return;}
            if(surface!=null)world.PersistPlayerBuild();
            long source=haulSourceSelection==0?Authority.World.NearestPayDirt(truck.pose):haulSourceSelection;long destination=haulDestinationSelection==0?Authority.World.DefaultHaulDestination(actor.id,truck.pose):haulDestinationSelection;
            if(destination==0){message="Place a sluice, stockpile or store for this vehicle to deliver to.";return;}
            if(world.isActiveAndEnabled){BeginRoutePlanning(truck,source,destination,haulSourceSelection==0,false,roleOverride);return;}string error;var command=haulSourceSelection==0?PlanAutomaticCollection(truck,destination,out error,false,roleOverride):IndustrialHaulNavigation.Plan(Authority.World,actor.id,truck.id,source,destination,ProjectTruckGround,out error,false,roleOverride??DefaultVehicleRole(truck));
            if(command==null){message=error;return;}command.operationRole=roleOverride??DefaultVehicleRole(truck);command.sequence=actor.lastSequence+1;command.definition=haulSourceSelection==0?"auto-source":null;command.item=Authority.World.creativeMode?"creative-vehicle":"vehicle-operation";Execute(command);if(truck.haul?.active==true){trackedTruck=truck.id;CloseMenu();}
        }
        void BuildTruckCollection(VisualElement parent,IndustrialMachine truck){
            UiText(parent,"AUTONOMOUS PAY-DIRT COLLECTION","industry-section");if(truck.haul?.role=="tailings")UiText(parent,"Current job: periodic tailings cleanup. The mining controls below configure a new job if you press AUTO.");if(Authority.World.miningWorkflow)UiText(parent,truck.definition=="excavator"?"AUTO: excavate at the node and load nearby dump trucks. The excavator stays at its worksite. Assign a separate worker for periodic tailings cleanup.":truck.definition=="truck"?"AUTO: collect from an active excavator/loader worksite, then transport to processing. Trucks wait for a bucket loader instead of mining the ground themselves.":"AUTO: scoop and haul stockpiles. You can instead park here as a truck-loading worksite or assign periodic tailings cleanup.");
            UiText(parent,"Leave Collect from on AUTO to find the closest pay-dirt node and switch when depleted. Choose a delivery destination, or leave AUTO to use the nearest sluice. You can select a specific source below. The truck repeats loading, hauling and unloading. Survival needs Fuel in the input; newly assigned Creative jobs use free propulsion. Routes need clear outdoor access; steep slopes, water and buildings are avoided. Other vehicles can hold up a route.");
            var sources=new List<long>{0};var sourceNames=new List<string>{"AUTO · closest pay-dirt node (any grade)"};
            foreach(var d in Authority.World.deposits)if(d.resource=="paydirt"&&Authority.World.PayDirtAvailable(d.id)&&truck.pose.Distance(d.pose)<double.PositiveInfinity){sources.Add(d.id);sourceNames.Add((d.remaining.radioactive>0?"RADIOACTIVE":d.remaining.copper>0?"COPPER":"IRON")+" PAY DIRT #"+d.id+" · "+(int)truck.pose.Distance(d.pose)+" m");}
            foreach(var p in Authority.World.piles)if(!p.tailingsPile&&p.owner==actor.id&&truck.pose.Distance(p.pose)<double.PositiveInfinity){sources.Add(p.id);sourceNames.Add("STOCKPILE #"+p.id+" · "+p.Volume+" L");}
            var destinations=new List<long>{0};var destinationNames=new List<string>{"AUTO · nearest sluice, otherwise stockpile/store"};
            foreach(var m in Authority.World.machines)if(m.owner==actor.id&&!m.Definition.Vehicle&&(m.definition=="sluice"||m.Definition.storage)&&truck.pose.Distance(m.pose)<double.PositiveInfinity){destinations.Add(m.id);destinationNames.Add(Nice(m.definition)+" #"+m.id);}
            foreach(var p in Authority.World.piles)if(!p.tailingsPile&&p.owner==actor.id&&truck.pose.Distance(p.pose)<double.PositiveInfinity){destinations.Add(p.id);destinationNames.Add("Stockpile #"+p.id);}
            if(sources.Count>0&&destinations.Count>0){
                int sourceIndex=sources.IndexOf(haulSourceSelection);if(sourceIndex<0)sourceIndex=sources.IndexOf(truck.haul?.source??0);sourceIndex=Math.Max(0,sourceIndex);haulSourceSelection=sources[sourceIndex];
                int destinationIndex=destinations.IndexOf(haulDestinationSelection);if(destinationIndex<0)destinationIndex=destinations.IndexOf(truck.haul?.destination??0);destinationIndex=Math.Max(0,destinationIndex);haulDestinationSelection=destinations[destinationIndex];
                var sourcePicker=new DropdownField("Collect from",sourceNames,sourceIndex){name="haul-source"};parent.Add(sourcePicker);sourcePicker.RegisterValueChangedCallback(e=>haulSourceSelection=sources[sourcePicker.index]);
                var destinationPicker=new DropdownField("Deliver to",destinationNames,destinationIndex){name="haul-destination"};parent.Add(destinationPicker);destinationPicker.RegisterValueChangedCallback(e=>haulDestinationSelection=destinations[destinationPicker.index]);
                UiButton(parent,"AUTO OPERATION / REPLAN",()=>AssignTruckCollection(truck),"haul-start").SetEnabled(!Driving);
            }else UiText(parent,"Need a nonempty PAY DIRT deposit / stockpile and a placed sluice, store or destination stockpile on this body.");
            if(Authority.World.miningWorkflow&&(truck.definition=="excavator"||truck.definition=="loader")){if(truck.definition=="loader")UiButton(parent,"WORKSITE LOADER · LOAD DUMP TRUCKS",()=>AssignTruckCollectionRole(truck,"excavate"),"haul-worksite").SetEnabled(!Driving);UiButton(parent,"PERIODIC TAILINGS CLEANUP",()=>AssignTailingsCleanup(truck),"haul-tailings").SetEnabled(!Driving);}
            UiButton(parent,"STOP COLLECTION · KEEP CARGO",()=>{CancelRoutePlanning(truck.id);Execute(Command("haul-stop",to:truck.id));},"haul-stop");
        }
    }
}
