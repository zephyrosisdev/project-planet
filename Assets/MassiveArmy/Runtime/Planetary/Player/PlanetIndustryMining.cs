using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        string DefaultVehicleRole(IndustrialMachine vehicle)=>!Authority.World.miningWorkflow?null:vehicle.definition=="excavator"?"excavate":vehicle.definition=="truck"?"transport":"haul";
        List<long> AutomaticCollectionSources(IndustrialMachine vehicle){
            var sources=new List<long>();if(Authority.World.miningWorkflow&&vehicle.definition=="truck")sources.AddRange(Authority.World.ActiveMiningSites(vehicle.owner,vehicle.pose));
            var candidates=new List<SurfaceDeposit>();foreach(var deposit in Authority.World.deposits)if(vehicle.pose.Distance(deposit.pose)<double.PositiveInfinity&&Authority.World.PayDirtAvailable(deposit.id)&&(vehicle.definition=="truck"||deposit.remaining.litres>0)&&ProjectTruckGround(deposit.pose).HasValue&&!sources.Contains(deposit.id))candidates.Add(deposit);candidates.Sort((a,b)=>vehicle.pose.Distance(a.pose).CompareTo(vehicle.pose.Distance(b.pose)));foreach(var deposit in candidates)sources.Add(deposit.id);return sources;
        }
        bool EnsureTailingsDump(IndustrialMachine sluice){
            if(sluice.owner!=actor.id||sluice.pose.body!=actor.pose.body||sluice.pose.frame!=actor.pose.frame){message="Choose your own sluice on this body";return false;}var existing=Authority.World.Pile(sluice.tailingsDump);if(existing?.tailingsPile==true&&existing.owner==sluice.owner)return true;if(surface!=null)world.PersistPlayerBuild();
            string failure="No clear, flat tailings dump site found 25 to 100 m from this sluice";
            foreach(double radius in new[]{32d,48d,64d,80d})for(int n=0;n<8;n++){
                double angle=-Math.PI*.5+n*Math.PI/4;var guess=IndustrialHaulNavigation.SurfaceOffset(sluice.pose,Math.Cos(angle)*radius,Math.Sin(angle)*radius);var ground=ProjectTruckGround(guess);if(!ground.HasValue)continue;var pose=ground.Value;
                if(surface!=null){var point=Body(pose);var fitted=ConstructionPlacement.FitRoomSurface(surface,point,Rotation(pose),new Bounds(Vector3.zero,new Vector3(16,.1f,16)));if((fitted-point).Magnitude>.5)continue;pose.x=fitted.x;pose.y=fitted.y;pose.z=fitted.z;}
                string error=Authority.CreateTailingsDump(sluice.id,pose);if(error==null){message="Tailings dump #"+sluice.tailingsDump+" created away from sluice #"+sluice.id;return true;}failure=error;
            }message=failure;return false;
        }
        void AssignTailingsCleanup(IndustrialMachine vehicle){
            if(Driving){message="Leave the vehicle before assigning cleanup";return;}if(vehicle.owner!=actor.id||vehicle.pose.Distance(actor.pose)>12){message="Move within 12 m of your cleanup vehicle";return;}IndustrialMachine sluice=null;double best=double.PositiveInfinity;foreach(var m in Authority.World.machines)if(m.definition=="sluice"&&m.owner==vehicle.owner&&vehicle.pose.Distance(m.pose)<best){best=vehicle.pose.Distance(m.pose);sluice=m;}
            if(sluice==null){message="Place an owned sluice on this body first";return;}if(!EnsureTailingsDump(sluice))return;
            if(world.isActiveAndEnabled){BeginRoutePlanning(vehicle,sluice.id,sluice.tailingsDump,false,false,"tailings");return;}
            var command=IndustrialHaulNavigation.Plan(Authority.World,vehicle.owner,vehicle.id,sluice.id,sluice.tailingsDump,ProjectTruckGround,out var error,false,"tailings");if(command==null){message=error;return;}command.sequence=actor.lastSequence+1;command.item=Authority.World.creativeMode?"creative-vehicle":"vehicle-operation";Execute(command);if(vehicle.haul?.active==true){trackedTruck=vehicle.id;CloseMenu();}
        }
    }
}
