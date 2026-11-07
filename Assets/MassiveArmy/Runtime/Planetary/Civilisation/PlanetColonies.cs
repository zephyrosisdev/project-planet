using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    [Serializable] public sealed class ColonyDelivery {
        public long id,pad;public int source,destination,faction,rotation;public Vector2 position;
        public double arrival;public bool delivered;public string status;
        public ResourceCost paid;public double food;
    }
    public sealed partial class CivilisationRuntime {
        readonly List<int> backgroundWorlds=new List<int>();int backgroundCursor;
        public long BackgroundTicks {get;private set;}
        public double MaximumBackgroundSliceMs {get;private set;}
        internal bool ColonyPadBusy(long pad){foreach(var job in State.colonyDeliveries)if(!job.delivered&&job.pad==pad)return true;return false;}
        internal bool NeedsColony(PlanetRtsBase local,int team)=>!HasLocalCommand(local.Buildings,team);
        internal static bool HasLocalCommand(List<RtsBuilding> buildings,int team)=>buildings.Exists(b=>b.team==team&&!b.removed&&b.definition.id=="command");
        public bool CanDeliverColony(PlanetRtsBase target,int team,out string reason){
            reason="A colony delivery is already en route";
            foreach(var delivery in State.colonyDeliveries)if(!delivery.delivered&&delivery.destination==target.World.definition.bodyId&&delivery.faction==team)return false;
            reason="Requires an operational homeworld Launch Pad";
            if(!locals.TryGetValue(system.Bodies[0].bodyId,out var home))return false;
            foreach(var pad in home.local.Buildings)if(ColonyPad(home.local,pad,team)){
                var cost=ColonyCost(target);var colony=home.colonies[team];
                reason="Homeworld needs "+cost+" and "+Settings.colonySupplyFood.ToString("0")+" food for the colony kit";
                if(colony!=null&&home.local.Factions[team].wallet.CanAfford(cost)&&colony.inventory.Get("food")>=Settings.colonySupplyFood){reason="Homeworld colony delivery · "+Settings.colonyDeliverySeconds.ToString("0")+" seconds · "+cost;return true;}
            }return false;
        }
        bool ColonyPad(PlanetRtsBase local,RtsBuilding pad,int team){
            if(pad.team!=team||pad.removed||!pad.Complete||pad.definition.id!="civil_launch_pad"||!pad.powered||pad.paused||pad.roadSuspended||pad.Efficiency<=0||Orbitals.Job(pad.persistentId)!=null)return false;
            foreach(var delivery in State.colonyDeliveries)if(!delivery.delivered&&delivery.pad==pad.persistentId)return false;
            return Logistics.Connected(local,pad);
        }
        ResourceCost ColonyCost(PlanetRtsBase target){
            var cost=new ResourceCost{materials=Settings.colonySupplyMaterials,fuel=Settings.colonySupplyFuel};
            foreach(string id in PlanetRtsBase.ColonyKit)for(int i=0;i<5;i++)cost[i]+=target.Catalog.Buildings[id].cost[i];
            return cost;
        }
        internal bool RequestColony(PlanetRtsBase target,Vector3 point,int rotation,int team,out string reason){
            reason="Planet already settled";
            // Explicit launchpad deliveries may reinforce an existing planet; ordinary
            // command placement there uses the local economy instead.
            if(!CanDeliverColony(target,team,out reason))return false;
            foreach(var pending in State.colonyDeliveries)if(!pending.delivered&&pending.destination==target.World.definition.bodyId&&CubeSphere.ArcDistance(point,CubeSphere.Decode(pending.position),target.World.definition.radius)<1500*target.Grid.PresentationScale){reason="Another colony kit has reserved this landing region";return false;}
            if(target.SettlementTerritoryOwner(point)>=0){reason="Command Centres require neutral land; use a Forward Outpost in enemy territory";return false;}if(!target.PlanColonyKit(point,rotation,team,out _,out reason))return false;
            var home=locals[system.Bodies[0].bodyId];RtsBuilding launch=null;
            foreach(var pad in home.local.Buildings)if(ColonyPad(home.local,pad,team)){launch=pad;break;}
            if(launch==null){reason="Homeworld launch pad is unavailable";return false;}
            var cost=ColonyCost(target);if(!home.local.Factions[team].wallet.Spend(cost)){reason="Homeworld resources changed";return false;}
            home.colonies[team].inventory.Spend("food",Settings.colonySupplyFood);
            var job=new ColonyDelivery{id=State.AllocateId(),pad=launch.persistentId,source=home.local.World.definition.bodyId,destination=target.World.definition.bodyId,faction=team,position=CubeSphere.Encode(point),rotation=rotation,arrival=State.time+Math.Max(2,Settings.colonyDeliverySeconds),paid=cost,food=Settings.colonySupplyFood,status="In flight"};
            State.colonyDeliveries.Add(job);home.local.NotifyChanged();Revision++;
            reason="Colony kit launched from "+BodyName(BodyId(job.source))+" · arrival in "+Settings.colonyDeliverySeconds.ToString("0")+" seconds";return true;
        }
        void AdvanceColonyDeliveries(){
            foreach(var job in State.colonyDeliveries){if(job.delivered||State.time<job.arrival||!locals.TryGetValue(job.destination,out var binding)||!binding.local.World.Ready)continue;
                
                if(!binding.local.PlanColonyKit(CubeSphere.Decode(job.position),job.rotation,job.faction,out var sites,out string reason)){job.status="Landing delayed: "+reason;continue;}
                var planet=Planet(job.destination);bool fresh=binding.colonies[job.faction]==null;var colony=binding.colonies[job.faction]??new ColonyState{id=State.AllocateId(),faction=job.faction,body=planet.body,name=State.Faction(job.faction).name+" colony",stage=ColonyStage.InitialOutpost,population=Settings.startingPopulation};
                colony.autoBuild=true;colony.autoBuildOverride=true;if(fresh){colony.inventory.Bind(Catalogue,binding.local.Factions[job.faction].wallet);State.colonies.Add(colony);planet.colonies.Add(colony.id);binding.colonies[job.faction]=colony;}else colony.population+=Settings.startingPopulation;
                int firstBuilding=binding.local.Buildings.Count;binding.local.LandColonyKit(sites,job.rotation,job.faction);
                if(fresh){colony.inventory.Deposit("construction_materials",Settings.colonySupplyMaterials);colony.inventory.Deposit("propellant",Settings.colonySupplyFuel);colony.inventory.Deposit("food",job.food);}
                foreach(var tech in State.Faction(job.faction).technologies)binding.local.Factions[job.faction].technologies.Add(tech);
                TickLocal(binding,0);var warehouse=binding.local.Buildings.FindLast(b=>b.team==job.faction&&!b.removed&&b.definition.id=="civil_warehouse");
                if(!fresh){var inventory=FindInstallation(colony,warehouse.persistentId).inventory;inventory.Deposit("construction_materials",Settings.colonySupplyMaterials);inventory.Deposit("propellant",Settings.colonySupplyFuel);inventory.Deposit("food",job.food);}
                colony.cargoVehicles.Add(new CargoVehicleState{id=State.AllocateId(),warehouse=warehouse.persistentId});
                // Supplies arrive inside the kit, including food at its inhabited buildings.
                var supplies=FindInstallation(colony,warehouse.persistentId).inventory;
                for(int i=firstBuilding;i<binding.local.Buildings.Count;i++){var b=binding.local.Buildings[i];if(b.team!=job.faction||b.definition.housingCapacity<=0)continue;var site=FindInstallation(colony,b.persistentId);double food=Math.Min(20,supplies.Get("food"));supplies.Spend("food",food);site.inventory.Deposit("food",food);}
                planet.lastSimulatedTime=State.time;ObserveArrival(job.destination,job.faction);job.delivered=true;job.status="Colony operational";binding.local.Notice=job.status;Revision++;
            }
        }
        void AdvanceBackground(){
            if(backgroundWorlds.Count==0)return;
            int id=backgroundWorlds[backgroundCursor++%backgroundWorlds.Count];if(backgroundCursor==int.MaxValue)backgroundCursor=0;
            var binding=locals[id];var world=binding.local.World;
            if(!world.Ready||(world==system.ActiveWorld&&world.SimulationActive))return;
            var planet=Planet(id);double missing=State.time-planet.lastSimulatedTime;
            if(missing<Math.Max(1,Settings.backgroundTickSeconds))return;
            // A bounded round-robin visit: no army dispatch, civilian movement or planet preparation.
            var timer=System.Diagnostics.Stopwatch.StartNew();ReplayingEconomy=true;
            try {
                int budget=Math.Max(1,Settings.catchupStepsPerFrame);
                while(missing>1e-9&&budget-->0){double dt=Math.Min(missing,Math.Max(.25,Settings.catchupMaximumStepSeconds));
                    foreach(var colony in binding.colonies)if(colony!=null){
                        foreach(var vehicle in colony.cargoVehicles)if(vehicle.phase!=CargoVehiclePhase.Idle&&vehicle.arrival>planet.lastSimulatedTime+1e-6)dt=Math.Min(dt,vehicle.arrival-planet.lastSimulatedTime);
                        foreach(var item in colony.installations)if(item.planted&&item.growth<1)foreach(var crop in Catalogue.crops)if(crop.id==item.crop)dt=Math.Min(dt,Math.Max(.25,(1-item.growth)*crop.growthSeconds));
                    }
                    evaluationTime=planet.lastSimulatedTime+dt;
                    using(SettlementPerformance.Sample("BackgroundEconomy"))TickLocal(binding,dt,true);
                    binding.local.AdvanceDormantInfrastructure((float)dt,true);
                    using(SettlementPerformance.Sample("BackgroundPlanner"))settlementDirector.Tick(binding.local);
                    planet.lastSimulatedTime=evaluationTime;missing=State.time-planet.lastSimulatedTime;BackgroundTicks++;
                    if(timer.Elapsed.TotalMilliseconds>=Math.Max(.1,Settings.catchupBudgetMilliseconds))break;
                }
            }finally{evaluationTime=-1;ReplayingEconomy=false;MaximumBackgroundSliceMs=Math.Max(MaximumBackgroundSliceMs,timer.Elapsed.TotalMilliseconds);}
        }

    }
    public sealed partial class PlanetRtsBase {
        internal bool FreePrebuild;bool constructionCostsDirty;
        internal void RefreshConstructionCosts(){if(!fixture.ResidentActive||FreePrebuild){constructionCostsDirty=true;return;}fixture.RefreshBuildingCosts(Buildings);}
        internal void FlushConstructionCosts(){if(!constructionCostsDirty)return;constructionCostsDirty=false;fixture.RefreshBuildingCosts(Buildings);}
        internal static readonly string[] ColonyKit={"command","solar","civil_warehouse","civil_housing","civil_housing","civil_housing","civil_housing"};
        internal bool PlanColonyKit(Vector3 point,int rotation,int team,out Vector3[] sites,out string reason){
            sites=new Vector3[ColonyKit.Length];sites[0]=SnapBuilding(Catalog.Buildings["command"],point,rotation);reason="";
            var up=sites[0];var east=Vector3.Cross(Math.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);
            var valid=Validate(Catalog.Buildings["command"],sites[0],rotation,false,team);if(!valid.valid){reason=valid.reason;return false;}
            for(int i=1;i<sites.Length;i++){
                var d=Catalog.Buildings[ColonyKit[i]];bool found=false;
                for(int attempt=0;attempt<96;attempt++){double angle=attempt*2.39996323;float radius=(100+attempt/16*40)*Grid.PresentationScale;var candidate=SnapBuilding(d,(up+(east*(float)Math.Cos(angle)+north*(float)Math.Sin(angle))*radius/fixture.definition.radius).normalized,rotation);
                    if(!Validate(d,candidate,rotation,false,team).valid)continue;bool clear=true;
                    for(int j=0;j<i;j++)if(Distance(candidate,sites[j])<FootprintRadius(d)+FootprintRadius(Catalog.Buildings[ColonyKit[j]])+Grid.TileSize){clear=false;break;}
                    if(clear){sites[i]=candidate;found=true;break;}
                }if(!found){reason="Leave dry clear space for the colony's power, warehouse and four houses";return false;}
            }return true;
        }
        internal void LandColonyKit(Vector3[] sites,int rotation,int team){
            if(!Buildings.Exists(b=>b.team==team&&!b.removed&&b.definition.id=="command"))Homes[team]=sites[0];var built=new RtsBuilding[sites.Length];
            for(int i=0;i<sites.Length;i++)built[i]=Create(Catalog.Buildings[ColonyKit[i]],sites[i],rotation,true,team);
            for(int i=0;i<built.Length;i++)if(i!=2)Roads.Connect(built[i],built[2],out _,true);
            RefreshConstructionCosts();Rebase();
        }
        static readonly string[] PrebuildPlan={"mine","wind","civil_mine_water","civil_farm_wheat","civil_food_processing","civil_gathering_camp","civil_housing","civil_food_shop","civil_warehouse","civil_farm_potatoes","civil_housing","civil_launch_pad"};
        internal bool PrebuildStep(int step){
            int team=step%Factions.Length,index=(step/Factions.Length)%PrebuildPlan.Length;
            FreePrebuild=true;try {
                bool built=TryConstructInfrastructure(PrebuildPlan[index],team);
                if(built&&!(Civilisation.Settings.ambientSettlementLife&&PrebuildPlan[index].StartsWith("civil_farm_"))){var b=Buildings[Buildings.Count-1];var hq=Buildings.Find(x=>x.team==team&&x.definition.id=="command"&&!x.removed);if(hq!=null)Roads.Connect(b,hq,out _,true);}
                return built;
            }finally{FreePrebuild=false;}
        }
    }
    public sealed partial class PlanetDevelopment {
        public float PrebuildProgress {get;private set;}
        public bool Prebuilding {get;private set;}
        IEnumerator PrebuildSettlement(){
            var sim=SystemRuntime?.Civilisation;if(sim==null||DeferredInitialization||!StrategyEnabled||!sim.IsHome(definition.bodyId))yield break;
            var args=Environment.GetCommandLineArgs();bool validatePrebuild=Array.IndexOf(args,"-planet-prebuild-check")>=0;foreach(var arg in args)if(arg=="-planet-no-prebuild"||!validatePrebuild&&arg.StartsWith("-planet-")&&(arg.Contains("validation")||arg.Contains("check")))yield break;
            double duration=Math.Max(0,sim.Settings.startingPrebuildSeconds);if(duration<=0)yield break;
            Prebuilding=true;if(hud!=null)hud.Root.style.display=UnityEngine.UIElements.DisplayStyle.None;
            double started=Time.realtimeSinceStartupAsDouble;int cap=Math.Max(0,sim.Settings.startingPrebuildMaximumAdditions);
            try {
                for(int step=0;step<cap&&Time.realtimeSinceStartupAsDouble-started<duration;step++){
                    Base.PrebuildStep(step);PrebuildProgress=Math.Max((float)((Time.realtimeSinceStartupAsDouble-started)/duration),(step+1f)/Math.Max(1,cap));yield return null;
                }
            }finally{Prebuilding=false;PrebuildProgress=1;if(hud!=null)hud.Root.style.display=UnityEngine.UIElements.DisplayStyle.Flex;}
            Base.FlushConstructionCosts();Base.Notice="Starting settlement ready";
        }
        void DrawPrebuildProgress(){
            var box=new Rect(Screen.width*.25f,Screen.height*.45f,Screen.width*.5f,90);GUI.Box(box,"Preparing starting settlement");
            GUI.Box(new Rect(box.x+20,box.y+40,(box.width-40)*Mathf.Clamp01(PrebuildProgress),22),Mathf.RoundToInt(PrebuildProgress*100)+"%");
        }
    }
}
