using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum OrbitalPayloadKind { SurveySatellite, ResearchStation, Warehouse, DockingStation }
    [Serializable] public sealed class OrbitalAssetState {
        public long id,launchPad; public string body; public int faction; public OrbitalPayloadKind kind;
        public double radius,mu,epoch,insertion,lastAccounted,pendingResearch; public Double3 u,v,launchOrigin;
        public ResourceInventory inventory=new ResourceInventory(); public bool airlockOpen;
        public bool Ready(double time)=>time>=insertion;
        public SpacePose Pose(double time){
            double rate=Math.Sqrt(mu/(radius*radius*radius)),angle=Math.IEEERemainder(rate*Math.Max(0,time-insertion),Math.PI*2);
            var radial=u*Math.Cos(angle)+v*Math.Sin(angle);var tangent=v*Math.Cos(angle)-u*Math.Sin(angle);
            var position=radial*radius;var velocity=tangent*(radius*rate);
            if(time<insertion){double t=Math.Clamp((time-epoch)/Math.Max(.001,insertion-epoch),0,1),s=t*t*(3-2*t);position=launchOrigin+(u*radius-launchOrigin)*s;velocity=(u*radius-launchOrigin)*(6*t*(1-t)/Math.Max(.001,insertion-epoch));}
            return new SpacePose{position=position,linearVelocity=velocity,rotation=Quaternion.LookRotation(tangent.Float,radial.Float),angularVelocity=Double3.Cross(u,v).Float*(float)rate};
        }
        public string Name=>kind==OrbitalPayloadKind.SurveySatellite?"Survey satellite":kind==OrbitalPayloadKind.ResearchStation?"Orbital research station":kind==OrbitalPayloadKind.Warehouse?"Orbital warehouse":"Docking station";
        public double Capacity=>kind==OrbitalPayloadKind.Warehouse?5000:kind==OrbitalPayloadKind.DockingStation?1000:0;
    }
    [Serializable] public sealed class OrbitalLaunchJob {public long id,pad;public string body;public int faction;public OrbitalPayloadKind kind;public double progress,seconds;}
    [Serializable] public sealed class OrbitalShipment {public long id,source,destination,pad;public string body,resource;public int faction;public double amount,arrival;public bool down;}
    /// <summary>Timestamped infrastructure, not a galaxy-wide Update loop. Only the observed body's ledger is settled.</summary>
    public sealed class OrbitalEconomy {
        readonly CivilisationRuntime sim; readonly PlanetSystemRuntime system;
        readonly List<OrbitalAssetState> warehouses=new List<OrbitalAssetState>();readonly Dictionary<(string,int),int> freightCursors=new Dictionary<(string,int),int>();
        readonly Dictionary<long,OrbitalLaunchJob> padJobs=new Dictionary<long,OrbitalLaunchJob>();
        readonly Dictionary<long,OrbitalAssetState> assets=new Dictionary<long,OrbitalAssetState>();
        readonly Dictionary<string,List<OrbitalAssetState>> bodies=new Dictionary<string,List<OrbitalAssetState>>();
        readonly Dictionary<string,List<OrbitalLaunchJob>> jobs=new Dictionary<string,List<OrbitalLaunchJob>>();
        readonly Dictionary<string,List<OrbitalShipment>> shipments=new Dictionary<string,List<OrbitalShipment>>();
        static readonly List<OrbitalAssetState> empty=new List<OrbitalAssetState>();
        public OrbitalEconomy(CivilisationRuntime sim,PlanetSystemRuntime system){this.sim=sim;this.system=system;}
        public IReadOnlyList<OrbitalAssetState> Assets(string body)=>body!=null&&bodies.TryGetValue(body,out var list)?list:empty;
        public OrbitalAssetState Find(long id)=>assets.TryGetValue(id,out var a)?a:null;
        public int Revision {get;private set;}
        static List<T> Bucket<T>(Dictionary<string,List<T>> map,string body){if(!map.TryGetValue(body,out var list)){list=new List<T>();map.Add(body,list);}return list;}
        public OrbitalLaunchJob Job(long pad)=>padJobs.TryGetValue(pad,out var job)?job:null;
        public static string RequiredTech(OrbitalPayloadKind kind)=>kind==OrbitalPayloadKind.SurveySatellite||kind==OrbitalPayloadKind.ResearchStation?"civil_satellites":"civil_orbital_construction";
        public static double Kits(OrbitalPayloadKind kind)=>kind==OrbitalPayloadKind.SurveySatellite?2:kind==OrbitalPayloadKind.ResearchStation?8:kind==OrbitalPayloadKind.Warehouse?6:10;
        public static double Fuel(OrbitalPayloadKind kind)=>kind==OrbitalPayloadKind.SurveySatellite?30:kind==OrbitalPayloadKind.ResearchStation?80:kind==OrbitalPayloadKind.Warehouse?80:100;
        static bool Storage(OrbitalAssetState a)=>a!=null&&a.Capacity>0;
        public bool HasResearchStation(string body,int faction){foreach(var a in Assets(body))if(a.faction==faction&&a.kind==OrbitalPayloadKind.ResearchStation&&a.Ready(sim.State.time))return true;return false;}
        public bool CanResearch(string body,int faction,string tech)=>tech!="civil_orbital_science"||HasResearchStation(body,faction);
        public bool Launch(PlanetRtsBase local,RtsBuilding pad,OrbitalPayloadKind kind,int faction,out string reason){
            reason="Requires your completed, road-connected, powered and staffed launch pad";
            if(!Enum.IsDefined(typeof(OrbitalPayloadKind),kind)||pad==null||pad.team!=faction||pad.removed||pad.definition.id!="civil_launch_pad"||!pad.Complete||pad.paused||!pad.powered||pad.roadSuspended||pad.Efficiency<=0||!sim.Logistics.Connected(local,pad))return false;
            string body=sim.BodyId(local.World.definition.bodyId);var colony=sim.Colony(body,faction);var item=colony?.installations.Find(i=>i.id==pad.persistentId);if(item==null)return false;
            reason="Requires "+RequiredTech(kind);if(!local.Factions[faction].HasTech(RequiredTech(kind)))return false;
            reason="Launch already in preparation";if(Job(pad.persistentId)!=null)return false;
            int count=0;foreach(var a in Assets(body))if(a.faction==faction)count++;if(jobs.TryGetValue(body,out var orders))foreach(var j in orders)if(j.faction==faction)count++;
            reason="Orbital capacity reached (32 payloads per faction per planet)";if(count>=32)return false;
            var cost=new[]{new MaterialQuantity("orbital_kit",Kits(kind)),new MaterialQuantity("propellant",Fuel(kind))};
            reason=$"Deliver {Kits(kind):0} orbital kits and {Fuel(kind):0} propellant to this pad";if(!item.inventory.Spend(cost))return false;
            var job=new OrbitalLaunchJob{id=sim.State.AllocateId(),pad=pad.persistentId,body=body,faction=faction,kind=kind,seconds=kind==OrbitalPayloadKind.SurveySatellite?60:180};
            sim.State.orbitalLaunches.Add(job);padJobs.Add(job.pad,job);Bucket(jobs,body).Add(job);Revision++;local.NotifyChanged();reason="Paid launch preparation started";return true;
        }
        public bool Cancel(PlanetRtsBase local,long padId,int faction,out string reason){
            var j=Job(padId);reason="No friendly launch order on this planet";if(j==null||j.faction!=faction||j.body!=sim.BodyId(local.World.definition.bodyId))return false;
            var pad=local.FindBuilding(padId);var item=sim.Colony(j.body,faction)?.installations.Find(v=>v.id==padId);
            if(pad!=null&&item!=null){reason="Make room at the pad for returned hardware and fuel";if(item.inventory.Capacity("orbital_kit")-item.inventory.Get("orbital_kit")<Kits(j.kind)||item.inventory.Capacity("propellant")-item.inventory.Get("propellant")<Fuel(j.kind))return false;item.inventory.Deposit("orbital_kit",Kits(j.kind));item.inventory.Deposit("propellant",Fuel(j.kind));reason="Launch cancelled; unlaunched hardware and fuel returned to the pad";}
            else reason="Destroyed pad launch cancelled; its hardware was lost with the pad";
            jobs[j.body].Remove(j);padJobs.Remove(padId);sim.State.orbitalLaunches.Remove(j);Revision++;local.NotifyChanged();return true;
        }
        internal void Tick(PlanetRtsBase local,ColonyState colony,double seconds){
            if(!jobs.TryGetValue(colony.body,out var list))return;
            for(int i=list.Count-1;i>=0;i--){var j=list[i];if(j.faction!=colony.faction)continue;var pad=local.FindBuilding(j.pad);
                // Paid hardware survives as a paused order if its pad is destroyed; there is no free relaunch.
                if(pad==null||pad.removed||!pad.Complete||pad.paused||!pad.powered||pad.roadSuspended||pad.Efficiency<=0)continue;
                j.progress=Math.Min(j.seconds,j.progress+seconds*pad.Efficiency);if(j.progress+1e-9<j.seconds)continue;
                var d=local.World.definition;var up=CubeSphere.Decode(pad.position).normalized;var normal=new Double3(up);normal=normal*(1/normal.Magnitude);var east=Double3.Cross(new Double3(Mathf.Abs(up.y)>.95f?Vector3.forward:Vector3.up),normal);east=east*(1/east.Magnitude);
                var a=new OrbitalAssetState{id=sim.State.AllocateId(),launchPad=j.pad,body=j.body,faction=j.faction,kind=j.kind,radius=d.radius+Math.Max(20000,d.ReliefBound+5000),mu=d.gravitationalParameter,u=normal,v=east,launchOrigin=new Double3(local.World.Surface.GetSurfacePosition(up,20)),epoch=sim.SimulationTime,insertion=sim.SimulationTime+20,lastAccounted=sim.SimulationTime+20};
                Register(a);list.RemoveAt(i);sim.State.orbitalLaunches.Remove(j);padJobs.Remove(j.pad);Revision++;local.NotifyChanged();
            }
        }
        internal void Register(OrbitalAssetState a){if(a.kind==OrbitalPayloadKind.Warehouse)warehouses.Add(a);a.inventory.Bind(sim.Catalogue);assets.Add(a.id,a);Bucket(bodies,a.body).Add(a);sim.State.orbitals.Add(a);sim.State.Planet(a.body).orbitalAssets.Add(a.id);}
        internal void Observe(PlanetDevelopment world)=>ObserveBody(sim.BodyId(world.definition.bodyId),world.Base);
        internal void ObserveBody(string body,PlanetRtsBase local){
            if(!bodies.TryGetValue(body,out var list))return;double now=sim.State.time;
            foreach(var a in list){if(!a.Ready(now)||now<=a.lastAccounted)continue;double elapsed=now-a.lastAccounted;a.lastAccounted=now;
                double rate=a.kind==OrbitalPayloadKind.ResearchStation?.6:a.kind==OrbitalPayloadKind.SurveySatellite?.03:0;
                if(sim.State.Faction(a.faction).technologies.Contains("civil_orbital_science"))rate*=1.5;
                a.pendingResearch+=elapsed*rate;var colony=sim.Colony(body,a.faction);if(colony!=null&&a.pendingResearch>0)a.pendingResearch-=colony.inventory.Deposit("research",a.pendingResearch);
                if(a.kind==OrbitalPayloadKind.SurveySatellite){var known=sim.State.Faction(a.faction).knowledge.Record(body);bool needsSurvey=known.level<DiscoveryLevel.Surveyed;known.level=DiscoveryLevel.Surveyed;known.confidence=1;known.lastObserved=now;if(needsSurvey||known.resources.Count==0){known.resources.Clear();foreach(var value in sim.State.Planet(body).composition.abundance)known.resources.Add(new ResourceEstimate{resource=value.resource,estimate=value.amount,uncertainty=0});}}
            }
            if(!shipments.TryGetValue(body,out var incoming))return;
            for(int i=incoming.Count-1;i>=0;i--){var m=incoming[i];if(m.arrival>now)continue;ResourceInventory inventory=null;double capacity=m.amount;
                if(m.down){var colony=sim.Colony(body,m.faction);var pad=local?.FindBuilding(m.pad);var installation=colony?.installations.Find(v=>v.id==m.pad);if(pad==null||pad.removed||!pad.Complete||installation==null||!pad.powered)continue;inventory=installation.inventory;}
                else {var destination=Find(m.destination);if(!Storage(destination)||!destination.Ready(now))continue;inventory=destination.inventory;capacity=Math.Min(capacity,Math.Max(0,destination.Capacity-Mass(destination))/sim.Catalogue.Resources[m.resource].mass);}
                double accepted=inventory.Deposit(m.resource,Math.Max(0,capacity));m.amount-=accepted;
                if(m.amount>1e-7)continue;incoming.RemoveAt(i);sim.State.orbitalShipments.Remove(m);Revision++;
            }
        }
        public double Mass(OrbitalAssetState a){double mass=0;foreach(var s in a.inventory.stock)mass+=s.amount*sim.Catalogue.Resources[s.resource].mass;return mass;}
        double Reserved(OrbitalAssetState a){double mass=0;if(shipments.TryGetValue(a.body,out var list))foreach(var m in list)if(!m.down&&m.destination==a.id)mass+=m.amount*sim.Catalogue.Resources[m.resource].mass;return mass;}
        bool Cargo(string resource,double amount,out double mass){mass=0;if(!double.IsFinite(amount)||amount<=0||!sim.Catalogue.Resources.TryGetValue(resource,out var d)||d.mass<=0)return false;mass=amount*d.mass;return double.IsFinite(mass);}
        bool Room(OrbitalAssetState a,double mass)=>Storage(a)&&a.Ready(sim.State.time)&&Mass(a)+Reserved(a)+mass<=a.Capacity+1e-7;
        void Send(OrbitalShipment m){m.id=sim.State.AllocateId();sim.State.orbitalShipments.Add(m);Bucket(shipments,m.body).Add(m);Revision++;}
        public bool Shuttle(PlanetRtsBase local,RtsBuilding pad,long station,string resource,double amount,bool down,int faction,out string reason){
            reason="Requires an operational friendly launch pad and orbital warehouse on this planet";var a=Find(station);string body=sim.BodyId(local.World.definition.bodyId);
            if(sim.State.orbitalShipments.Count>=256||pad==null||pad.team!=faction||pad.removed||pad.definition.id!="civil_launch_pad"||!pad.Complete||pad.paused||!pad.powered||pad.roadSuspended||!sim.Logistics.Connected(local,pad)||!Storage(a)||a.body!=body||a.faction!=faction||!a.Ready(sim.State.time))return false;
            var colony=sim.Colony(body,faction);var item=colony?.installations.Find(i=>i.id==pad.persistentId);if(item==null)return false;
            reason="Surface shuttle capacity is 80 cargo mass per flight";if(!Cargo(resource,amount,out double mass)||mass>80)return false;
            var source=down?a.inventory:item.inventory;
            reason="Insufficient source cargo or destination capacity";if(source.Get(resource)+1e-9<amount||(!down&&!Room(a,mass)))return false;
            // Account for cargo propellant and flight propellant together before mutating either balance.
            reason="Deliver 10 propellant to the launch pad for the shuttle";if(item.inventory.Get("propellant")+1e-9<10+(!down&&resource=="propellant"?amount:0))return false;
            item.inventory.Spend("propellant",10);source.Spend(resource,amount);
            Send(new OrbitalShipment{source=down?a.id:0,destination=down?0:a.id,pad=pad.persistentId,body=body,faction=faction,resource=resource,amount=amount,down=down,arrival=sim.State.time+60});reason="Finite cargo shuttle dispatched";return true;
        }
        public bool Transfer(long sourceId,long destinationId,string resource,double amount,int faction,out string reason){
            var a=Find(sourceId);var b=Find(destinationId);reason="Select two ready friendly space warehouses in different planetary orbits";
            if(sim.State.orbitalShipments.Count>=256||!Storage(a)||!Storage(b)||a==b||a.body==b.body||a.faction!=faction||b.faction!=faction||!a.Ready(sim.State.time)||!b.Ready(sim.State.time))return false;
            if(!sim.State.Faction(faction).knowledge.Knows(b.body))return false;
            double limit=sim.State.Faction(faction).technologies.Contains("civil_orbital_science")?400:200;
            reason=$"Orbital freighter capacity is {limit:0} cargo mass";if(!Cargo(resource,amount,out double mass)||mass>limit)return false;
            reason="Insufficient cargo, 20 onboard propellant, or reserved destination space";
            if(a.inventory.Get(resource)+1e-9<amount||a.inventory.Get("propellant")+1e-9<20+(resource=="propellant"?amount:0)||!Room(b,mass))return false;
            var da=Definition(a.body);var db=Definition(b.body);if(da==null||db==null)return false;
            double distance=db.position.RelativeTo(da.position).Magnitude;a.inventory.Spend("propellant",20);a.inventory.Spend(resource,amount);
            Send(new OrbitalShipment{source=a.id,destination=b.id,body=b.body,faction=faction,resource=resource,amount=amount,arrival=sim.State.time+Math.Max(120,distance/30000)});reason="Interplanetary freight dispatched; unloaded when destination is observed";return true;
        }
        PlanetDefinition Definition(string body){var state=sim.State.Planet(body);if(state==null)return null;foreach(var d in system.Bodies)if(d.bodyId==state.planetId)return d;return null;}
        internal void Plan(PlanetRtsBase local,int team){
            if(!local.Factions[team].HasTech("civil_satellites")||!local.AiEconomyReady(team))return;RtsBuilding pad=null;foreach(var b in local.Buildings)if(b.team==team&&!b.removed&&b.definition.id=="civil_launch_pad"){pad=b;break;}
            if(pad==null){if(local.AiCanSpend(team,local.Catalog.Buildings["civil_launch_pad"].cost))local.TryConstructInfrastructure("civil_launch_pad",team);return;}
            if(!pad.Complete||Job(pad.persistentId)!=null)return;if(HasResearchStation(sim.BodyId(local.World.definition.bodyId),team)&&!local.Factions[team].HasTech("civil_orbital_science"))local.Research(pad,"civil_orbital_science");string body=sim.BodyId(local.World.definition.bodyId);bool survey=false,research=false,warehouse=false,dock=false;
            foreach(var a in Assets(body))if(a.faction==team){survey|=a.kind==OrbitalPayloadKind.SurveySatellite;research|=a.kind==OrbitalPayloadKind.ResearchStation;warehouse|=a.kind==OrbitalPayloadKind.Warehouse;dock|=a.kind==OrbitalPayloadKind.DockingStation;}
            if(!survey)Launch(local,pad,OrbitalPayloadKind.SurveySatellite,team,out _);else if(!research)Launch(local,pad,OrbitalPayloadKind.ResearchStation,team,out _);else if(local.Factions[team].HasTech("civil_orbital_construction")){if(!warehouse)Launch(local,pad,OrbitalPayloadKind.Warehouse,team,out _);else if(!dock)Launch(local,pad,OrbitalPayloadKind.DockingStation,team,out _);else foreach(var a in Assets(body))if(a.faction==team&&a.kind==OrbitalPayloadKind.Warehouse){PlanFreight(local,pad,a,team);break;}}
        }
        internal bool PlanFreight(PlanetRtsBase local,RtsBuilding pad,OrbitalAssetState warehouse,int team){
            if(!warehouse.Ready(sim.State.time)||pad==null||pad.team!=team||pad.removed||!pad.Complete)return false;
            string body=warehouse.body;var colony=sim.Colony(body,team);var item=colony?.installations.Find(i=>i.id==pad.persistentId);if(item==null)return false;
            if(shipments.TryGetValue(body,out var localMissions))foreach(var mission in localMissions)if(mission.pad==pad.persistentId)return false;
            var region=SettlementLogistics.RegionalWarehouse(local,colony,pad);
            if(region!=null&&region.inventory.Get("construction_materials")<sim.Settings.aiConstructionReserve&&warehouse.inventory.Get("construction_materials")>=80&&item.inventory.Get("construction_materials")<160)
                return Shuttle(local,pad,warehouse.id,"construction_materials",80,true,team,out _);
            if(warehouse.inventory.Get("propellant")<40&&item.inventory.Get("propellant")>=190)
                return Shuttle(local,pad,warehouse.id,"propellant",80,false,team,out _);
            if(warehouses.Count==0)return false;var key=(body,team);freightCursors.TryGetValue(key,out int cursor);
            for(int scan=0;scan<Math.Min(16,warehouses.Count);scan++){var destination=warehouses[(cursor+scan)%warehouses.Count];freightCursors[key]=(cursor+scan+1)%warehouses.Count;
                if(destination.faction!=team||destination.body==body||!destination.Ready(sim.State.time)||!sim.State.Faction(team).knowledge.Knows(destination.body)||!Room(destination,80))continue;
                double incoming=0;if(shipments.TryGetValue(destination.body,out var orders))foreach(var m in orders)if(!m.down&&m.destination==destination.id&&m.resource=="construction_materials")incoming+=m.amount;
                if(destination.inventory.Get("construction_materials")+incoming>=160)continue;
                if(warehouse.inventory.Get("construction_materials")>=80&&warehouse.inventory.Get("propellant")>=20)
                    return Transfer(warehouse.id,destination.id,"construction_materials",80,team,out _);
                if(item.inventory.Get("construction_materials")>=140)
                    return Shuttle(local,pad,warehouse.id,"construction_materials",80,false,team,out _);
            }return false;
        }
        internal static void Validate(UniverseState state,CivilisationCatalogue catalogue){
            state.orbitals??=new List<OrbitalAssetState>();state.orbitalLaunches??=new List<OrbitalLaunchJob>();state.orbitalShipments??=new List<OrbitalShipment>();var ids=new HashSet<long>();
            foreach(var a in state.orbitals){if(a.id<=0||!ids.Add(a.id)||state.Planet(a.body)==null||state.Faction(a.faction)==null||!Enum.IsDefined(typeof(OrbitalPayloadKind),a.kind)||!double.IsFinite(a.epoch)||!double.IsFinite(a.launchOrigin.Magnitude)||!double.IsFinite(a.u.Magnitude)||!double.IsFinite(a.v.Magnitude)||!double.IsFinite(a.radius)||a.radius<=0||!double.IsFinite(a.mu)||a.mu<=0||!double.IsFinite(a.insertion)||!double.IsFinite(a.lastAccounted)||!double.IsFinite(a.pendingResearch)||a.pendingResearch<0||Math.Abs(a.u.Magnitude-1)>.001||Math.Abs(a.v.Magnitude-1)>.001||Math.Abs(a.u.x*a.v.x+a.u.y*a.v.y+a.u.z*a.v.z)>.001)throw new InvalidDataException("Invalid orbital asset");a.inventory.Bind(catalogue);double mass=0;var resources=new HashSet<string>();foreach(var s in a.inventory.stock){if(!resources.Add(s.resource)||!catalogue.Resources.ContainsKey(s.resource)||!double.IsFinite(s.amount)||s.amount<0)throw new InvalidDataException("Invalid orbital cargo");mass+=s.amount*catalogue.Resources[s.resource].mass;}if(mass>a.Capacity+1e-6)throw new InvalidDataException("Orbital cargo exceeds capacity");}
            foreach(var j in state.orbitalLaunches)if(j.id<=0||!ids.Add(j.id)||state.Planet(j.body)==null||state.Faction(j.faction)==null||!Enum.IsDefined(typeof(OrbitalPayloadKind),j.kind)||!double.IsFinite(j.progress)||!double.IsFinite(j.seconds)||j.progress<0||j.seconds<=0||j.progress>j.seconds)throw new InvalidDataException("Invalid launch order");
            foreach(var m in state.orbitalShipments)if(m.id<=0||!ids.Add(m.id)||state.Planet(m.body)==null||state.Faction(m.faction)==null||!catalogue.Resources.ContainsKey(m.resource)||!double.IsFinite(m.amount)||m.amount<=0||!double.IsFinite(m.arrival))throw new InvalidDataException("Invalid orbital shipment");
        }
    }
}
