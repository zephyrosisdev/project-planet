using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [Serializable] public sealed class PipelineLink {
        public long source,destination;public int owner;
        public string resource;public double metres,delivered;
        public List<Vector2> route=new List<Vector2>();
    }
    public sealed partial class CivilisationRuntime {
        public const string OilDepot="civil_oil_depot",GasDepot="civil_gas_depot";
        readonly Dictionary<int,double> nextRegionalPlan=new Dictionary<int,double>();
        readonly Dictionary<int,int> regionalTurns=new Dictionary<int,int>();
        public static string DepotResource(string definition)=>definition==OilDepot?"crude_oil":definition==GasDepot?"methane":null;
        public static bool PipeEndpoint(RtsBuilding b,string resource)=>!b.removed&&b.Complete&&!b.paused&&b.powered&&(b.definition.extractedResource==resource||DepotResource(b.definition.id)==resource||b.definition.id=="civil_port"||b.definition.id=="civil_oil_refinery"&&resource=="crude_oil");
        internal SurfaceInstallation Installation(PlanetRtsBase local,long id){foreach(var colony in local.Settlements)if(colony!=null){var item=SettlementLogistics.Find(colony,id);if(item!=null)return item;}return null;}
        public static double TransferPipelineStock(ResourceInventory source,ResourceInventory destination,ResourceInventory payer,string resource,double maximum,double fuelPerUnit){
            if(!double.IsFinite(maximum)||!double.IsFinite(fuelPerUnit)||maximum<0||fuelPerUnit<0)throw new ArgumentOutOfRangeException(nameof(maximum));
            if(source==null||destination==null||payer==null||ReferenceEquals(source,destination))return 0;
            double amount=Math.Min(maximum,Math.Min(source.Get(resource),Math.Max(0,destination.Capacity(resource)-destination.Get(resource))));
            if(fuelPerUnit>0)amount=Math.Min(amount,payer.Get("propellant")/fuelPerUnit);if(amount<=1e-9)return 0;
            if(ReferenceEquals(source.Root,destination.Root))return 0;
            if(!payer.Spend("propellant",amount*fuelPerUnit))return 0;double accepted=destination.Deposit(resource,amount);source.Spend(resource,accepted);return accepted;
        }
        internal void TickPipelines(PlanetRtsBase local,double seconds){
            if(seconds<=0)return;var planet=Planet(local.World.definition.bodyId);
            foreach(var link in planet.pipelines){var a=local.FindBuilding(link.source);var b=local.FindBuilding(link.destination);
                if(a==null||b==null||!local.Allied(a.team,b.team)||!PipeEndpoint(a,link.resource)||!PipeEndpoint(b,link.resource))continue;
                var source=Installation(local,a.persistentId);var destination=Installation(local,b.persistentId);if(source==null||destination==null)continue;
                // Depots equalise stocks; wells/ports feed stores; stores feed consumers.
                bool stores=DepotResource(a.definition.id)==link.resource&&DepotResource(b.definition.id)==link.resource;
                if(stores&&source.inventory.Get(link.resource)<destination.inventory.Get(link.resource)){var swap=source;source=destination;destination=swap;}
                double available=source.inventory.Get(link.resource);if(stores)available=Math.Max(0,(available-destination.inventory.Get(link.resource))*.5);
                double amount=Math.Min(Settings.pipelineUnitsPerSecond*seconds,Math.Min(available,Math.Max(0,destination.inventory.Capacity(link.resource)-destination.inventory.Get(link.resource))));
                var payer=local.Settlements[link.owner]?.inventory;
                // Ten percent of the equivalent loaded lorry's round-trip fuel.
                double fuelPerUnit=link.metres*2*Settings.groundFuelPerMetre/Math.Max(1,Settings.cargoCapacity)*Settings.pipelineFuelFraction;
                link.delivered+=TransferPipelineStock(source.inventory,destination.inventory,payer,link.resource,amount,fuelPerUnit);
            }
        }
        public bool ConnectPipeline(PlanetRtsBase local,RtsBuilding source,RtsBuilding destination,string resource,out string reason){
            var planet=Planet(local.World.definition.bodyId);
            if(source==null||destination==null||source==destination||!local.Allied(source.team,destination.team)||!PipeEndpoint(source,resource)||!PipeEndpoint(destination,resource)){reason="Select two complete allied oil/gas facilities";return false;}
            foreach(var link in planet.pipelines)if(link.resource==resource&&(link.source==source.persistentId&&link.destination==destination.persistentId||link.source==destination.persistentId&&link.destination==source.persistentId)){reason="Pipeline already connected";return false;}
            if(planet.pipelines.Count>=Settings.maximumPipelineLinks){reason="Pipeline link capacity reached";return false;}
            var route=local.Roads.InfrastructureRoute(source,destination,Math.Max(64,Settings.roadSearchBudget));
            if(route==null){reason="A connected road corridor is required for pipeline construction";return false;}
            double metres=0;for(int i=1;i<route.Count;i++)metres+=CubeSphere.ArcDistance(CubeSphere.Decode(route[i-1]),CubeSphere.Decode(route[i]),local.World.definition.radius);
            double cost=metres*Settings.pipelineMaterialPerMetre;
            if(!local.FreePrebuild&&!local.Settlements[source.team].inventory.Spend("construction_materials",cost)){reason="Insufficient pipeline construction materials";return false;}
            planet.pipelines.Add(new PipelineLink{source=source.persistentId,destination=destination.persistentId,owner=source.team,resource=resource,metres=metres,route=route});planet.pipelineRevision++;local.NotifyChanged();reason="Pipeline connected: "+resource;return true;
        }
        internal void PlanRegionalConnections(PlanetRtsBase local){
            int id=local.World.definition.bodyId;if(local.World.ClassicCountryFixture||nextRegionalPlan.TryGetValue(id,out double next)&&SimulationTime<next)return;
            nextRegionalPlan[id]=SimulationTime+Math.Max(1,Settings.strategyTickSeconds);regionalTurns.TryGetValue(id,out int turn);regionalTurns[id]=turn+1;
            var commands=new List<RtsBuilding>();var endpoints=new List<RtsBuilding>();
            foreach(var b in local.Buildings)if(!b.removed&&b.Complete){if(b.definition.id=="command")commands.Add(b);if(PipeEndpoint(b,"crude_oil")||PipeEndpoint(b,"methane"))endpoints.Add(b);}
            // One inter-town extension per thought, round-robin. Existing roads and paid
            // section search handle terrain, crossings and curvature; never spawn roads as GOs.
            if(local.World.AlliedStart&&commands.Count>1){var town=commands[1+turn%(commands.Count-1)];var capital=commands.Find(x=>x.team==0);
                if(capital!=null&&Autonomous(local,capital.team)&&(!Settings.regionalSettlements||RegionalPlanner.StartupReady(local,capital.team))&&!local.Roads.Connected(town,capital)&&local.Settlements[capital.team].inventory.Get("construction_materials")>Settings.aiConstructionReserve*2)local.Roads.ConnectToNetwork(capital,town,out _);
            }
            if(endpoints.Count>1){int index=turn%endpoints.Count;var source=endpoints[index];
                foreach(string resource in new[]{"crude_oil","methane"})if(PipeEndpoint(source,resource)){
                    RtsBuilding best=null;float distance=float.MaxValue;var planet=Planet(id);
                    foreach(var target in endpoints){if(target==source||!local.Allied(source.team,target.team)||!PipeEndpoint(target,resource))continue;
                        bool sourceStore=DepotResource(source.definition.id)==resource,targetStore=DepotResource(target.definition.id)==resource;
                        if(!sourceStore&&!targetStore||!sourceStore&&source.definition.id=="civil_oil_refinery"||sourceStore&&(target.definition.extractedResource==resource||target.definition.id=="civil_port"))continue;
                        bool exists=false;foreach(var edge in planet.pipelines)if(edge.resource==resource&&(edge.source==source.persistentId&&edge.destination==target.persistentId||edge.source==target.persistentId&&edge.destination==source.persistentId)){exists=true;break;}if(exists)continue;
                        float gap=CubeSphere.ArcDistance(CubeSphere.Decode(source.position),CubeSphere.Decode(target.position),local.World.definition.radius);
                        if(gap<distance&&local.Roads.Connected(source,target)){distance=gap;best=target;}
                    }if(best!=null){ConnectPipeline(local,source,best,resource,out _);break;}
                }
            }
        }
    }
}
