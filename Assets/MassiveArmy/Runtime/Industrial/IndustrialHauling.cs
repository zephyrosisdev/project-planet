using System;
using System.Collections.Generic;

namespace MassiveArmy.Industrial {
    [Serializable] public sealed class IndustrialHaulJob {
        public string role;[System.ComponentModel.DefaultValue(false)] public bool waitingForExcavator;public long lastServiceTick,lastLoadTick;public long source,destination,reserved,nextReplanTick;public bool active=true,automaticSource,freeFuel;
        public List<FramePose> route=new List<FramePose>();public int pickupIndex,waypoint=1,phase,blockedTicks;
        public string status="Travelling to collection";
    }
    [Serializable] public sealed class IndustrialHaulReplan {public long truck,source;[System.Xml.Serialization.XmlArray(IsNullable=true)]public List<FramePose> route;public int pickupIndex;public string error;}
    [Serializable] public sealed class IndustrialHaulObstacle {public long module;public FramePose pose;public double radius,halfWidth,halfDepth,height;}
    public sealed partial class IndustrialWorld {
        public const int MaximumHaulJobs=100,MaximumHaulRoutePoints=65536;
        public List<IndustrialHaulObstacle> haulObstacles=new List<IndustrialHaulObstacle>();
        bool MiningBlocked(FramePose point){foreach(var m in machines)if(!m.integratedLogistics&&!m.Definition.Vehicle&&HaulNear(m.pose,point,6))return true;foreach(var obstacle in haulObstacles)if(IndustrialHaulNavigation.BuildingContains(obstacle,point,.5))return true;return false;}
        public bool PayDirtAvailable(long id){var deposit=Deposit(id);return deposit!=null&&deposit.resource=="paydirt"&&(deposit.remaining.litres>0&&!MiningBlocked(deposit.pose)||SiteBucketStock(id)>0);}
        public long NearestPayDirt(FramePose point){long id=0;double best=double.PositiveInfinity;foreach(var deposit in deposits)if(deposit.resource=="paydirt"&&PayDirtAvailable(deposit.id)){double distance=point.Distance(deposit.pose);if(distance<best){best=distance;id=deposit.id;}}return id;}
        public long DefaultHaulDestination(long owner,FramePose point){long id=0;double best=double.PositiveInfinity;foreach(var m in machines)if(m.owner==owner&&m.definition=="sluice"){double d=point.Distance(m.pose);if(d<best){best=d;id=m.id;}}if(id!=0)return id;foreach(var pile in piles)if(pile.owner==owner&&!pile.tailingsPile){double d=point.Distance(pile.pose);if(d<best){best=d;id=pile.id;}}if(id!=0)return id;foreach(var m in machines)if(m.owner==owner&&!m.integratedLogistics&&!m.Definition.Vehicle&&m.Definition.storage){double d=point.Distance(m.pose);if(d<best){best=d;id=m.id;}}return id;}
        public long PileHaulReserved(long pile){long n=0;foreach(var m in machines)if(m.haul!=null&&m.haul.active&&m.haul.destination==pile)n+=m.haul.reserved;return n;}
        public bool HaulEndpointInUse(long id){foreach(var m in machines)if(m.haul!=null&&m.haul.active&&(m.id==id||m.haul.destination==id||m.haul.source==id))return true;return false;}
        public bool HaulSource(long owner,long id,out FramePose pose,out long stock){
            var pile=Pile(id);if(pile!=null&&pile.owner==owner){pose=pile.pose;stock=pile.Volume+(pile.tailingsPile?0:SiteBucketStock(id,owner));return true;}
            var sourceMachine=miningWorkflow?Machine(id):null;if(miningWorkflow&&sourceMachine?.definition=="sluice"&&sourceMachine.owner==owner){pose=TailingsPose(sourceMachine);stock=TailingsCount(sourceMachine.frontTailings);return true;}
            foreach(var d in deposits)if(d.id==id&&d.resource=="paydirt"){pose=d.pose;stock=d.remaining.litres+SiteBucketStock(id,owner);return true;}
            pose=default;stock=0;return false;
        }
        public bool HaulDestination(long owner,long id,out FramePose pose,out double clearance){
            var m=Machine(id);if(m!=null&&m.owner==owner&&!m.integratedLogistics&&!m.Definition.Vehicle&&(m.definition=="sluice"||m.Definition.storage)){pose=m.pose;clearance=m.Definition.Radius+3.6;return true;}
            var pile=Pile(id);if(pile!=null&&pile.owner==owner){pose=pile.pose;clearance=15;return true;}pose=default;clearance=0;return false;
        }
        static bool HaulNear(FramePose a,FramePose b,double radius){if(a.body!=b.body||a.frame!=b.frame)return false;double x=a.x-b.x,y=a.y-b.y,z=a.z-b.z;return Math.Abs(x)<radius&&Math.Abs(y)<radius&&Math.Abs(z)<radius&&x*x+y*y+z*z<radius*radius;}
        public string HaulBlocked(FramePose point,long truck,double planningPadding=0){
            double playerClearance=Machine(truck)?.definition=="truck"?2.8:6;foreach(var actor in actors){if(HaulNear(actor.pose,point,playerClearance+actor.collisionRadius+planningPadding))return "Player in route";if(actor.vehicleObstacleRadius>0&&HaulNear(actor.vehicleObstacle,point,actor.vehicleObstacleRadius+2.8)&&IndustrialHaulNavigation.ShipClearanceContains(actor.vehicleObstacle,point))return "Ship blocks route";}
            if(!miningWorkflow){foreach(var m in machines)if(!m.integratedLogistics&&m.id!=truck&&HaulNear(m.pose,point,(m.Definition.Vehicle?2.8:m.Definition.Radius)+2.8+planningPadding))return m.Definition.Vehicle?"Vehicle in route":"Construction blocks route";}
            else foreach(var m in machines){if(m.integratedLogistics||m.id==truck)continue;if(HaulNear(m.pose,point,(m.Definition.Vehicle?2.8:m.Definition.Radius)+2.8+planningPadding))return m.Definition.Vehicle?"Vehicle in route":"Construction blocks route";var parked=m.haul;if(parked?.active==true&&parked.role=="excavate"&&parked.route!=null&&parked.pickupIndex<parked.route.Count&&HaulNear(parked.route[parked.pickupIndex],point,5.6+planningPadding))return "Reserved excavator loading berth";}
            if(miningWorkflow)foreach(var sluice in machines)if(sluice.definition=="sluice"&&HaulNear(sluice.pose,point,12)){if(HaulNear(TailingsPose(sluice),point,4.8+planningPadding))return "Sluice front discharge area blocks route";}
            foreach(var pile in piles)if(HaulNear(pile.pose,point,14.5+planningPadding))return "Stockpile blocks route";
            foreach(var obstacle in haulObstacles)if(IndustrialHaulNavigation.BuildingContains(obstacle,point,2.8+planningPadding))return "Building #"+obstacle.module+" blocks route";
            return null;
        }
        public string HaulCommandError(long owner,IndustrialCommand c,bool requireReach=true){
            var a=Actor(owner);var truck=Machine(c.target);if(a==null||truck==null||truck.owner!=owner||requireReach&&!Own(a,truck)||!truck.Definition.Vehicle)return "Move within 12 m of your truck";
            int active=0;foreach(var m in machines)if(m.haul?.active==true&&m!=truck)active++;if(active>=MaximumHaulJobs)return "Autonomous vehicle limit reached (100); stop a job before assigning another";
            if(c.source==c.count||!HaulSource(owner,c.source,out var pickup,out var stock)||stock==0&&truck.input.bulk.litres==0&&TailingsCount(truck.input)==0&&!CanWaitForHaulSource(c.source))return "Choose a nonempty pay-dirt deposit or your stockpile";
            if(Deposit(c.source)!=null&&Deposit(c.source).remaining.litres>0&&MiningBlocked(pickup))return "Collection would undermine construction; move the building away from the deposit";
            if(!HaulDestination(owner,c.count,out var destination,out double clearance))return "Choose your sluice, store, or stockpile destination";
            if(c.operationRole!=null&&!miningWorkflow)return "Enable the industrial mining workflow before assigning this role";
            bool waste=IsTailingsSource(c.source);var targetPile=Pile(c.count);
            if(waste!=(targetPile?.tailingsPile==true))return "Keep tailings in their own dump pile and pay dirt in the processing chain";
            if(c.operationRole!=null&&c.operationRole!="excavate"&&c.operationRole!="transport"&&c.operationRole!="tailings"&&c.operationRole!="haul")return "Unknown vehicle operation";
            if(c.operationRole=="excavate"&&(truck.definition!="excavator"&&truck.definition!="loader"||waste)||c.operationRole=="transport"&&(truck.definition!="truck"||waste)||c.operationRole=="tailings"&&(!waste||truck.definition=="truck"))return "Choose a compatible excavator, truck or tailings-cleanup role";
            if(waste&&c.operationRole!="tailings")return "Use the tailings cleanup operation";
            if(c.operationRole=="tailings"&&truck.input.bulk.litres>0||c.operationRole!="tailings"&&TailingsCount(truck.input)>0)return "Unload the existing cargo before changing material type";
            if(c.route==null||c.route.Count<3||c.route.Count>MaximumHaulRoutePoints||c.routeSplit<1||c.routeSplit>=c.route.Count-1)return "No valid truck route";
            if(c.route[0].Distance(truck.pose)>.5)return "Vehicle moved while planning; stop it and replan";
            if(c.route[c.routeSplit].Distance(pickup)>(Pile(c.source)!=null?16:8))return "Pickup berth is too far from the resource; replan collection";
            if(c.operationRole!="excavate"&&c.route[c.route.Count-1].Distance(destination)>clearance+1)return "Delivery berth is too far from storage; replan delivery";
            for(int i=0;i<c.route.Count;i++){var p=c.route[i];if(!Valid(p)||p.body!=truck.pose.body||p.frame!=truck.pose.frame)return "Route must stay in the truck's reference frame";if(HaulBlocked(p,truck.id)!=null)return "A route sample is obstructed";if(i>0){double segment=p.Distance(c.route[i-1]);if(segment>5.1)return "Route samples must be within 5 m";}}
            return null;
        }
        public string PrepareHaulReplan(IndustrialHaulReplan request,out Action apply){
            apply=null;var truck=Machine(request.truck);if(truck?.haul?.active!=true)return "Collection job no longer active";var job=truck.haul;
            // Legacy XML omitted null routes; XmlSerializer restores those as
            // empty lists. A recorded planning failure is a cooldown/status
            // update, not a replacement route to validate or cargo to discard.
            if(request.route==null||request.route.Count==0&&!string.IsNullOrEmpty(request.error)){apply=()=>{job.nextReplanTick=tick+100;job.status="Route blocked · "+request.error;};return null;}
            var command=new IndustrialCommand{kind="haul-start",source=request.source!=0?request.source:job.source,target=truck.id,count=job.destination,route=request.route,routeSplit=request.pickupIndex,operationRole=job.role};string error=HaulCommandError(truck.owner,command,false);if(error!=null)return error;
            if(command.source!=job.source&&(!job.automaticSource||truck.input.bulk.litres>0))return "Cannot change this job source during delivery";
            var points=new List<FramePose>(request.route);apply=()=>{ReleaseHaul(truck);truck.haul=new IndustrialHaulJob{source=command.source,destination=job.destination,role=job.role,lastServiceTick=job.lastServiceTick,lastLoadTick=job.lastLoadTick,automaticSource=job.automaticSource,freeFuel=job.freeFuel,route=points,pickupIndex=request.pickupIndex,nextReplanTick=tick+100,status="Route replanned"};miningSiteRevision++;ReserveHaul(truck.haul,Math.Min(HaulCargo(truck),HaulFree(truck.haul)));};return null;
        }
        void ReleaseHaul(IndustrialMachine truck){var job=truck.haul;if(job==null)return;if(job.reserved>0){var dest=Machine(job.destination);if(dest!=null){if(dest.input.reserved<job.reserved)throw new InvalidOperationException("Truck reservation accounting mismatch");dest.input.reserved-=job.reserved;}job.reserved=0;}job.active=false;miningSiteRevision++;job.status="Stopped · cargo retained";}
        string HaulCapacityReason(IndustrialHaulJob job){var destination=Machine(job.destination);if(destination==null)return "Destination stockpile full · waiting";return "Destination #"+destination.id+" full/reserved · "+(!destination.enabled?"switch it on":destination.status??"clear its input")+" · waiting";}
        long HaulFree(IndustrialHaulJob job){var m=Machine(job.destination);return m!=null?m.input.Free:Math.Max(0,100000-Pile(job.destination).Volume-PileHaulReserved(job.destination));}
        void ReserveHaul(IndustrialHaulJob job,long amount){if(amount<=0)return;var dest=Machine(job.destination);if(dest!=null)dest.input.reserved+=amount;job.reserved+=amount;}
        void UnreserveHaul(IndustrialHaulJob job,long amount){var dest=Machine(job.destination);if(dest!=null)dest.input.reserved-=amount;job.reserved-=amount;}
        void StepHaul(IndustrialMachine truck){
            var job=truck.haul;if(!job.active)return;
            if(!truck.enabled||truck.integrity<=0){job.status="Disabled";return;}
            if(!HaulSource(truck.owner,job.source,out var sourcePose,out long stock)||!HaulDestination(truck.owner,job.destination,out var destination,out var clearance)){ReleaseHaul(truck);job.status="Endpoint unavailable · cargo retained";return;}
            if(job.route[job.pickupIndex].Distance(sourcePose)>(Pile(job.source)!=null?16:8)||job.role!="excavate"&&job.route[job.route.Count-1].Distance(destination)>clearance+1){job.status="Endpoint moved · reassign route";return;}
            if(job.phase==1){
                if(job.role=="excavate"){truck.pose.yaw=IndustrialHaulNavigation.Yaw(truck.pose,sourcePose);StepExcavation(truck);return;}
                bool waste=job.role=="tailings";long cargo=HaulCargo(truck);if(!waste&&job.role!="transport")stock=RawSourceRemaining(job.source);
                if(waste&&cargo==0&&stock<CleanupBatchThreshold&&(stock==0||tick-job.lastServiceTick<CleanupIntervalTicks)){job.status="Waiting for tailings batch · collects at 500 L or every 20 s";return;}
                long room=truck.input.Free,incoming=Math.Min(stock,room),want=cargo+incoming;if(job.reserved<want)ReserveHaul(job,Math.Min(want-job.reserved,HaulFree(job)));
                long amount=Math.Min(50,Math.Min(incoming,Math.Max(0,job.reserved-cargo)));
                if(amount>0){
                    if(waste){if(!UseJobFuel(truck,1))return;var source=TailingsSourceInventory(job.source);long moved=MoveTailings(source,truck.input,amount);Pile(job.source)?.RemoveCells(moved);if(cargo==0)job.lastServiceTick=tick;job.status="Scooping front tailings";return;}
                    if(job.role=="transport"){
                        if(LoadTruckFromExcavator(truck,amount)>0)return;
                        if(cargo==0||tick-job.lastLoadTick<20)return;
                        if(job.reserved>cargo)UnreserveHaul(job,job.reserved-cargo);job.waitingForExcavator=false;job.phase=2;job.waypoint=job.pickupIndex+1;job.status="Delivering partial excavator load";return;
                    }
                    if(Pile(job.source)==null&&MiningBlocked(sourcePose)){job.status="Collection would undermine construction";return;}
                    var pile=Pile(job.source);MaterialLot lot;if(pile!=null)lot=pile.Scoop(amount);else{var d=Deposit(job.source);lot=d.remaining.Take(amount);d.removed+=amount;}truck.input.bulk.Add(lot);job.status="Loading pay dirt";return;
                }
                if(cargo>0&&job.reserved>=cargo){job.phase=2;job.waypoint=job.pickupIndex+1;job.status=waste?"Taking tailings to dump pile":"Delivering";return;}
                if(job.reserved>0)UnreserveHaul(job,job.reserved);job.status=stock==0?(waste?"Waiting for tailings":Pile(job.source)!=null?"Waiting for stockpile deliveries":"Collection source depleted"):HaulCapacityReason(job);return;
            }
            if(job.phase==3){
                long cargo=HaulCargo(truck);if(job.reserved<cargo)ReserveHaul(job,Math.Min(cargo-job.reserved,HaulFree(job)));
                long amount=Math.Min(50,Math.Min(cargo,job.reserved));if(amount>0){
                    if(job.role=="tailings"){if(!UseJobFuel(truck,1))return;var dump=Pile(job.destination);dump.tailings??=new Inventory{capacity=100000};long moved=MoveTailings(truck.input,dump.tailings,amount);dump.AddCells(moved);UnreserveHaul(job,moved);job.status="Building tailings dump pile";return;}
                    var lot=truck.input.bulk.Take(amount);UnreserveHaul(job,amount);var dest=Machine(job.destination);if(dest!=null)dest.input.bulk.Add(lot);else Pile(job.destination).Add(lot);job.status="Unloading pay dirt";return;
                }
                if(cargo>0){job.status=HaulCapacityReason(job);return;}if(job.reserved>0)UnreserveHaul(job,job.reserved);job.phase=4;job.waypoint=job.route.Count-2;job.status="Returning to collection";return;
            }
            job.waitingForExcavator=false;var next=job.route[job.waypoint];double distance=truck.pose.Distance(next);double step=Math.Min(.5,distance);var point=truck.pose;
            if(distance>.001){point.x+=(next.x-point.x)*step/distance;point.y+=(next.y-point.y)*step/distance;point.z+=(next.z-point.z)*step/distance;point.yaw=IndustrialHaulNavigation.Yaw(truck.pose,next);}
            string obstruction=HaulBlocked(point,truck.id);if(obstruction!=null){job.blockedTicks++;if(job.blockedTicks==1)job.status=obstruction+" · waiting for a detour";return;}
            if(!UseJobFuel(truck,DrivingEnergy(truck)))return;truck.pose=point;job.blockedTicks=0;job.status=job.phase==2?"Delivering":job.phase==4?"Returning to collection":"Travelling to collection";
            if(distance<=.501){next.yaw=point.yaw;truck.pose=next;if(job.phase==0&&job.waypoint==job.pickupIndex||job.phase==4&&job.waypoint==job.pickupIndex)job.phase=1;else if(job.phase==2&&job.waypoint==job.route.Count-1)job.phase=3;else job.waypoint+=job.phase==4?-1:1;}
        }
    }
}
