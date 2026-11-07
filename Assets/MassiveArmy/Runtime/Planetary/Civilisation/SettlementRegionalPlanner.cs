using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // One fixed-size metadata survey page and at most one local construction action per tick.
    // Towns share their country's existing economy. No extra army, NPC poses or unit mirrors.
    public sealed class SettlementRegionalPlanner {
        internal sealed class Stats {
            internal int materialReady,gatherReady,fabricatorReady,waterReady,foodReady;
            internal bool materialProject,gatherProject,fabricatorProject,waterProject,foodProject;
            internal readonly Dictionary<string,int> types=new Dictionary<string,int>();internal readonly RtsBuilding[] anchors=new RtsBuilding[64];int next;
            internal RtsBuilding oilSource,oilSink,gasSource,gasSink;internal int count,housing,jobs,projects;internal float generation,demand;
            internal int Count(string id)=>types.TryGetValue(id,out int n)?n:0;
            internal void Add(RtsBuilding b){Pending(b);types[b.definition.id]=Count(b.definition.id)+1;count++;housing+=b.definition.housingCapacity*b.level;jobs+=b.definition.workers*b.level;generation+=b.definition.generation*b.level;demand+=b.definition.consumption*b.level;if(b.definition.extractedResource=="crude_oil")oilSource=b;if(CivilisationRuntime.DepotResource(b.definition.id)=="crude_oil"||b.definition.id=="civil_oil_refinery"||b.definition.id=="civil_port")oilSink=b;if(b.definition.extractedResource=="methane")gasSource=b;if(CivilisationRuntime.DepotResource(b.definition.id)=="methane")gasSink=b;if(!b.Complete)projects++;else if(!b.paused&&!b.roadSuspended){switch(b.definition.id){case "mine":materialReady++;break;case "civil_gathering_camp":gatherReady++;break;case "civil_fabricator":fabricatorReady++;break;case "civil_mine_water":waterReady++;break;case "civil_food_processing":case "civil_hydroponics":foodReady++;break;}}anchors[next++%anchors.Length]=b;}
            internal void Field(string id){types[id]=Count(id)+1;}
            internal void Pending(RtsBuilding b){if(b.Complete||b.paused)return;switch(b.definition.id){case "mine":materialProject=true;break;case "civil_gathering_camp":gatherProject=true;break;case "civil_fabricator":fabricatorProject=true;break;case "civil_mine_water":waterProject=true;break;case "civil_food_processing":case "civil_hydroponics":foodProject=true;break;}}
        }
        internal struct StartupSupply {
            internal int mines,gatherers,fabricators,water,food,fields,pendingMines,pendingGatherers,pendingFabricators,pendingWater,pendingFood;
            internal bool Materials=>mines>0||gatherers>0&&fabricators>0;
            internal bool Ready(bool fertile)=>Materials&&water>0&&food>0&&(!fertile||fields>0);
        }
        internal static string StartupNeed(StartupSupply s,bool fertile,string crop,int turn){
            if(!s.Materials){if(s.pendingMines>0)return null;if(s.pendingGatherers>0&&s.gatherers==0||s.pendingFabricators>0&&s.fabricators==0)return null;
                if(s.gatherers>0)return "civil_fabricator";
                return turn%3==0?"mine":"civil_gathering_camp";}
            if(s.water==0)return s.pendingWater>0?null:"civil_mine_water";
            if(fertile&&s.fields==0)return "civil_farm_"+crop;
            if(s.food==0)return s.pendingFood>0?null:fertile?"civil_food_processing":"civil_hydroponics";
            return null;
        }
        sealed class Work {
            internal int phase,cursor,buildingLimit,landLimit,turn,roadTurn,roadCursor,parentRoadCursor,prospectTurn,profileTurn,pipeTurn;internal bool ready;internal double nextAction,nextProspect;
            internal readonly int[] bootstrapTurns=new int[PlanetCountries.Maximum],startupTurns=new int[PlanetCountries.Maximum],failedProspects=new int[PlanetCountries.Maximum];internal readonly double[] blockedUntil=new double[PlanetCountries.Maximum];
            internal readonly bool[] startupReady=new bool[PlanetCountries.Maximum];
            internal readonly List<RegionalSettlement> towns=new List<RegionalSettlement>();internal Dictionary<long,Stats> current=new Dictionary<long,Stats>(),working=new Dictionary<long,Stats>();
        }
        readonly CivilisationRuntime simulation;readonly Dictionary<PlanetRtsBase,Work> worlds=new Dictionary<PlanetRtsBase,Work>();
        public SettlementRegionalPlanner(CivilisationRuntime simulation){this.simulation=simulation;}
        static List<RegionalSettlement> Towns(ColonyState c)=>c.towns??(c.towns=new List<RegionalSettlement>());
        public static RegionalSettlement Nearest(IReadOnlyList<RegionalSettlement> towns,Vector3 point){RegionalSettlement best=null;float score=-2;for(int i=0;i<towns.Count;i++){float d=Vector3.Dot(point,CubeSphere.Decode(towns[i].position));if(d>score){score=d;best=towns[i];}}return best;}
        internal static RegionalSettlement EarlierParent(IReadOnlyList<RegionalSettlement> towns,RegionalSettlement town){RegionalSettlement best=null;float score=-2;var point=CubeSphere.Decode(town.position);foreach(var candidate in towns){if(candidate==town)break;float d=Vector3.Dot(point,CubeSphere.Decode(candidate.position));if(d>score){score=d;best=candidate;}}return best;}
        internal Vector3 Anchor(PlanetRtsBase local,int team,Vector3 point){var c=local.Settlements?[team];return c==null?local.Homes[team]:CubeSphere.Decode(Nearest(Towns(c),point)?.position??CubeSphere.Encode(local.Homes[team]));}
        internal bool MilitaryAllowed(PlanetRtsBase local,RtsBuilding building,bool nuclear){var c=local.Settlements?[building.team];if(c==null)return false;var town=Nearest(Towns(c),CubeSphere.Decode(building.position));var p=SettlementProfiles.Get(town?.kind??SettlementKind.CivicTown);return nuclear?p.nuclear:p.military||p.kind==SettlementKind.Capital;}
        public void Tick(PlanetRtsBase local){
            if(!local.World.StrategyEnabled)return;if(!worlds.TryGetValue(local,out var w)){w=new Work();worlds.Add(local,w);}Survey(local,w);
            if(!w.ready||simulation.SimulationTime<w.nextAction||w.towns.Count==0)return;w.nextAction=simulation.SimulationTime+Math.Max(.25,simulation.Settings.strategyTickSeconds/Math.Max(1,w.towns.Count));
            ServiceRoad(local,w);
            var town=w.towns[w.turn++%w.towns.Count];var colony=local.Settlements[Owner(local,town)];if(colony==null)return;
            if(!w.current.TryGetValue(town.id,out var stats))return;var root=local.FindBuilding(town.hub);if(root==null||root.removed){town.status="Town centre lost; awaiting restoration";return;}
            town.buildingCount=stats.count;town.housing=stats.housing;town.jobs=stats.jobs;
            if(!simulation.Autonomous(local,colony.faction)){town.status="Auto Pilot off; manual control";return;}
            if(!root.Complete){town.status="Constructing town centre";return;}
            if(simulation.SimulationTime<town.nextGrowth)return;town.nextGrowth=simulation.SimulationTime+Math.Max(1,simulation.Settings.strategyTickSeconds);
            var supply=Supply(w,colony,out var crop);bool fertile=crop!=null;
            if(!supply.Ready(fertile)){
                string startup=stats.Count("civil_warehouse")==0?"civil_warehouse":stats.generation<stats.demand+5?"wind":StartupNeed(supply,fertile,crop?.id??"wheat",w.startupTurns[colony.faction]++);
                town.status=startup==null?"Completing the starter supply chain":"Establishing starter supply: "+startup;
                if(startup!=null)Build(local,w,colony,town,stats,startup,true);
                return; // Never spend the last startup funds on services, research or another HQ.
            }
            if(TryExpand(local,w,colony,town,root,stats))return;
            var profile=SettlementProfiles.Get(town.kind);
            if(Towns(colony).Count>0&&Towns(colony)[0]==town){string bootstrap=NationalNeed(w,colony);if(bootstrap!=null&&Build(local,w,colony,town,stats,bootstrap,true))return;}
            string required=BasicNeed(stats,town,profile,colony);
            if(required!=null){if(Build(local,w,colony,town,stats,required,true))return;town.failures++;}
            var unconnected=PendingConnection(local,w,colony);if(unconnected!=null){town.status="Connecting "+unconnected.name+" to the road network before further expansion";return;}
            if(!PlanetRtsBase.DevelopmentWorkforceReady(colony)){town.status="Restoring food and workforce before optional growth";return;}
            // A staffed town can deepen its specialist industries; lack of workers does not block homes/services.
            if(stats.projects>=Math.Max(2,Math.Min(6,colony.workforce/12))){town.status="Completing existing projects";return;}
            int stage=Math.Clamp(stats.count/24,0,4);town.developmentTier=stage;
            for(int probe=0;probe<profile.buildings.Length;probe++){string id=profile.buildings[town.focusCursor++%profile.buildings.Length];if(!local.Catalog.Buildings.TryGetValue(id,out var d)||local.Factions[colony.faction].tier<d.tier||!local.Factions[colony.faction].HasTech(d.requiredTech))continue;
                if(SettlementProfiles.IsNuclear(id)&&!profile.nuclear)continue;int target=SettlementProfiles.FocusTarget(id,stats.count);
                if(stats.Count(id)>=target)continue;if(Build(local,w,colony,town,stats,id,false))return;break;
            }
            // Grow housing and supporting commerce while specialists wait for technology or a usable deposit.
            if(stats.count<profile.target){string fallback=SettlementProfiles.Common[(town.focusCursor++)%SettlementProfiles.Common.Length];if(FallbackNeeded(fallback,stats)&&Build(local,w,colony,town,stats,fallback,false))return;}
            if(!town.status.StartsWith("Saving",StringComparison.Ordinal))town.status=stats.count>=profile.target?"Established; developing regional network":"Waiting for funds, workforce, technology or a suitable plot";
        }
        StartupSupply Supply(Work w,ColonyState c,out CropDefinition crop){var env=simulation.State.Planet(c.body).environment;crop=env.atmosphere?Array.Find(simulation.Catalogue.crops,x=>Math.Abs(env.temperature-x.temperature)<=x.tolerance):null;var s=new StartupSupply();foreach(var town in Towns(c))if(w.current.TryGetValue(town.id,out var stats)){s.mines+=stats.materialReady;s.gatherers+=stats.gatherReady;s.fabricators+=stats.fabricatorReady;s.water+=stats.waterReady;s.food+=stats.foodReady;s.pendingMines+=stats.materialProject?1:0;s.pendingGatherers+=stats.gatherProject?1:0;s.pendingFabricators+=stats.fabricatorProject?1:0;s.pendingWater+=stats.waterProject?1:0;s.pendingFood+=stats.foodProject?1:0;if(crop!=null)s.fields+=stats.Count("civil_farm_"+crop.id);}return s;}
        internal bool StartupReady(PlanetRtsBase local,int team){var c=local.Settlements?[team];if(c==null||!worlds.TryGetValue(local,out var w)||!w.ready)return false;return w.startupReady[team];}
        string NationalNeed(Work w,ColonyState c){int Count(string id){int n=0;foreach(var t in Towns(c))if(w.current.TryGetValue(t.id,out var stats))n+=stats.Count(id);return n;}
            int turn=w.bootstrapTurns[c.faction]++%5;string id=turn==0?"mine":turn==1?"civil_mine_water":turn==2?"civil_farm_wheat":turn==3?"civil_food_processing":"lab";
            int target=id=="mine"?simulation.Settings.aiMinimumMaterialMines:1;
            var env=simulation.State.Planet(c.body).environment;bool fertile=env.atmosphere&&Array.Exists(simulation.Catalogue.crops,crop=>Math.Abs(env.temperature-crop.temperature)<=crop.tolerance);if(turn==2){if(!fertile)id="civil_hydroponics";else {var crop=Array.Find(simulation.Catalogue.crops,x=>Math.Abs(env.temperature-x.temperature)<=x.tolerance);if(crop!=null){id="civil_farm_"+crop.id;double yield=0;foreach(var output in crop.harvest)if(output.resource!="seeds")yield+=output.amount;target=ProductionTarget(c.foodDemand*.75*crop.growthSeconds,Math.Max(1,yield*.2));}}}if(!fertile&&turn==3)return null;
            if(turn==3||id=="civil_hydroponics"){double rate=0;foreach(var recipe in simulation.Catalogue.recipes)if(recipe.building==id)foreach(var output in recipe.outputs)if(output.resource=="food")rate=Math.Max(rate,output.amount/Math.Max(1,recipe.seconds));target=ProductionTarget(c.foodDemand,rate);}
            return Count(id)<target?id:null;
        }
        internal static int ProductionTarget(double demand,double outputPerSecond)=>Math.Clamp((int)Math.Ceiling(Math.Max(0,demand)*1.2/Math.Max(.01,outputPerSecond)),1,64);
        internal static bool FallbackNeeded(string id,Stats stats)=>id!="civil_housing"&&stats.Count(id)<SettlementProfiles.FocusTarget(id,stats.count);
        internal static bool ExpansionReady(RegionalSettlement town,int count,double now,CivilisationSettings settings)=>now>=town.nextExpansion&&(count>=settings.regionalExpansionBuildings||count>=Math.Min(8,settings.regionalExpansionBuildings)&&now-town.founded>=settings.regionalExpansionSeconds*2);
        internal static RegionalSettlement FirstUnconnected(IReadOnlyList<RegionalSettlement> towns,Func<RegionalSettlement,bool> connected){foreach(var town in towns)if(town.parent!=0&&!connected(town))return town;return null;}
        RegionalSettlement PendingConnection(PlanetRtsBase local,Work w,ColonyState c)=>FirstUnconnected(Towns(c),town=>{
            var root=local.FindBuilding(town.hub);if(root==null||root.removed)return true;
            var parent=w.towns.Find(t=>t.id==town.parent);var other=parent==null?null:local.FindBuilding(parent.hub);
            // A successful short road extension is only progress. Require the two
            // completed HQ entrances to belong to the same actual road component.
            return root.Complete&&other!=null&&!other.removed&&other.Complete&&local.Roads.Connected(root,other);
        });
        bool SavingForExpansion(PlanetRtsBase local,Work w,ColonyState c){if(Towns(c).Count>=simulation.Settings.maximumRegionalSettlements||simulation.SimulationTime<w.blockedUntil[c.faction]||PendingConnection(local,w,c)!=null)return false;foreach(var town in Towns(c))if(ExpansionReady(town,town.buildingCount,simulation.SimulationTime,simulation.Settings))return true;return false;}
        internal static string BasicNeed(Stats s,RegionalSettlement town,SettlementProfile profile,ColonyState c){
            if(s.Count("civil_warehouse")==0)return "civil_warehouse";if(s.generation<s.demand+15)return "wind";
            int residentsPerTown=(int)Math.Ceiling(c.population/(double)Math.Max(1,c.towns?.Count??1));int desiredHousing=Math.Max(24,Math.Min(Math.Min(profile.target*4,(int)Math.Ceiling(s.jobs*1.35)+24),residentsPerTown*2+24));if(s.housing<desiredHousing)return "civil_housing";
            if(s.Count("civil_food_shop")==0)return "civil_food_shop";if(s.Count("civil_drinks_shop")==0)return "civil_drinks_shop";
            if(s.Count(CivilisationRuntime.PoliceHeadquarters)==0)return CivilisationRuntime.PoliceHeadquarters;
            if(s.Count("civil_school")==0)return "civil_school";if(s.Count("civil_council")==0)return "civil_council";
            if(s.Count("civil_park")==0)return "civil_park";if(c.deaths.Count>0&&s.Count(CivilisationRuntime.Graveyard)==0)return CivilisationRuntime.Graveyard;
            if(c.deaths.Count>0&&s.Count(CivilisationRuntime.FuneralDirectors)==0)return CivilisationRuntime.FuneralDirectors;return null;
        }
        bool Build(PlanetRtsBase local,Work w,ColonyState c,RegionalSettlement town,Stats stats,string id,bool essential){
            if(stats.count>=SettlementProfiles.Get(town.kind).target&&!essential)return false;if(!local.Catalog.Buildings.TryGetValue(id,out var d))return false;
            var faction=local.Factions[c.faction];if(faction.tier<d.tier||!faction.HasTech(d.requiredTech))return false;var cost=id.StartsWith("civil_farm_")||id=="civil_park"?new ResourceCost{materials=simulation.Settings.landTileMaterialCost}:d.cost;
            if(!essential&&SavingForExpansion(local,w,c)&&faction.wallet.Stock.materials-cost.materials<simulation.Settings.regionalFoundingReserve+local.Catalog.Buildings["command"].cost.materials){town.status="Saving the founding reserve for a new town";return false;}
            if(!(essential?faction.wallet.CanAfford(cost):local.AiCanSpend(c.faction,cost))){town.status="Saving resources for "+d.name;return false;}
            local.PlanningSettlement=town;local.RegionalAnchors=stats.anchors;int before=local.Buildings.Count;bool built=false;
            try{
                if(id=="civil_port"||id=="civil_freight_station")built=local.BuildTerminalNear(local.FindBuilding(town.hub),id=="civil_port",id=="civil_freight_station");else built=local.TryConstructInfrastructure(id,c.faction);
                if(!built&&d.terrain==TerrainRule.Node)local.SurveyFrontierDeposit(id,c.faction);
            }finally{local.PlanningSettlement=null;local.RegionalAnchors=null;}
            if(!built)return false;town.failures=0;town.status="Building "+d.name;
            if(local.Buildings.Count>before){var b=local.Buildings[local.Buildings.Count-1];b.settlementId=town.id;stats.Add(b);if(w.phase>0&&b.index>=w.buildingLimit)For(w.working,town.id).Add(b);}
            else stats.Field(id);
            var national=simulation.State.Faction(c.faction);national.priority=town.name+" / "+SettlementProfiles.Get(town.kind).label;national.reason=town.status;return true;
        }
        static Stats For(Dictionary<long,Stats> records,long id){if(!records.TryGetValue(id,out var s)){s=new Stats();records[id]=s;}return s;}
        void Survey(PlanetRtsBase local,Work w){
            if(w.phase==0&&w.cursor==0){w.buildingLimit=local.Buildings.Count;w.landLimit=local.LandUse.Tiles.Count;}
            int budget=64;
            while(budget-->0){
                if(w.phase==0){if(w.cursor<w.buildingLimit){var b=local.Buildings[w.cursor++];if(!b.removed&&b.definition.id=="command")Register(local,w,b);continue;}w.cursor=0;w.phase=1;}
                if(w.phase==1){if(w.cursor<w.buildingLimit){var b=local.Buildings[w.cursor++];if(b.removed)continue;var c=local.Settlements[b.team];if(c==null)continue;var town=Towns(c).Find(t=>t.id==b.settlementId)??Nearest(Towns(c),CubeSphere.Decode(b.position));if(town!=null){b.settlementId=town.id;For(w.working,town.id).Add(b);}continue;}w.cursor=0;w.phase=2;}
                if(w.phase==2){if(w.cursor<w.landLimit){var tile=local.LandUse.Tiles[w.cursor++];var c=local.Settlements[tile.faction];if(c!=null){var town=Towns(c).Find(t=>t.id==tile.settlementId)??Nearest(Towns(c),local.Grid.Direction(tile.cell));if(town!=null){tile.settlementId=town.id;For(w.working,town.id).Field(tile.kind==SettlementLandKind.Park?"civil_park":"civil_farm_"+tile.crop);}}continue;}
                    foreach(var c in local.Settlements)if(c!=null)foreach(var town in Towns(c))if(town.parent==0&&local.World.AlliedStart&&c.faction!=0){var capital=w.towns.Find(t=>t.kind==SettlementKind.Capital);if(capital!=null)town.parent=capital.id;}
                    w.current=w.working;w.working=new Dictionary<long,Stats>();w.cursor=w.phase=0;w.ready=true;foreach(var colony in local.Settlements)if(colony!=null){var supply=Supply(w,colony,out var crop);w.startupReady[colony.faction]=supply.Ready(crop!=null);}PlanPipe(local,w);return;
                }
            }
        }
        void PlanPipe(PlanetRtsBase local,Work w){if(w.towns.Count==0)return;var town=w.towns[(w.pipeTurn/2)%w.towns.Count];bool gas=(w.pipeTurn++&1)!=0;if(!w.current.TryGetValue(town.id,out var own))return;var source=gas?own.gasSource:own.oilSource;var sink=gas?own.gasSink:own.oilSink;
            if(sink==null&&w.current.TryGetValue(town.parent,out var parent))sink=gas?parent.gasSink:parent.oilSink;
            if(source!=null&&sink!=null&&source!=sink&&simulation.Autonomous(local,source.team)&&local.Roads.Connected(source,sink))simulation.ConnectPipeline(local,source,sink,gas?"methane":"crude_oil",out _);
        }
        void Register(PlanetRtsBase local,Work w,RtsBuilding b){var c=local.Settlements[b.team];if(c==null)return;if(b.persistentId==0)b.persistentId=simulation.State.AllocateId();var towns=Towns(c);var town=towns.Find(t=>t.hub==b.persistentId);
            if(town==null){var previous=Nearest(towns,CubeSphere.Decode(b.position));if(previous!=null&&local.FindBuilding(previous.hub)==null&&CubeSphere.ArcDistance(CubeSphere.Decode(previous.position),CubeSphere.Decode(b.position),local.World.definition.radius)<1000)town=previous;
                else {town=new RegionalSettlement{id=simulation.State.AllocateId(),position=b.position,founded=simulation.SimulationTime,kind=towns.Count==0?(simulation.IsHome(local.World.definition.bodyId)?(b.team==0?SettlementKind.Capital:InitialKind(b.team)):SettlementKind.CivicTown):SettlementProfiles.Choose(towns,c,towns.Count,true,false)};town.name=(towns.Count==0?c.name:SettlementProfiles.Get(town.kind).label+" "+(towns.Count+1));towns.Add(town);}}
            // Additional HQs placed manually or delivered by launchpad need a
            // parent link too, including settlements created before this fix.
            if(town.parent==0&&towns.Count>1&&towns[0]!=town)town.parent=EarlierParent(towns,town)?.id??0;
            town.hub=b.persistentId;b.settlementId=town.id;if(!w.towns.Contains(town))w.towns.Add(town);
        }
        static SettlementKind InitialKind(int team)=>new[]{SettlementKind.CivicTown,SettlementKind.ArableFarming,SettlementKind.MetalMining,SettlementKind.Manufacturing,SettlementKind.Energy,SettlementKind.Research}[team%6];
        int Owner(PlanetRtsBase local,RegionalSettlement town){foreach(var c in local.Settlements)if(c!=null&&Towns(c).Contains(town))return c.faction;return 0;}
        internal static int NextRoadIndex(ref int cursor,int count){if(count<=0)return -1;if(cursor<0||cursor>=count)cursor=0;return cursor++;}
        bool ServiceRoad(PlanetRtsBase local,Work w){
            if(w.towns.Count==0)return false;
            // Reserve every third maintenance turn for inter-town links, so new
            // building spurs cannot starve the regional road network.
            if(w.roadTurn++%3==0)for(int i=0;i<Math.Min(16,w.towns.Count);i++){
                var town=w.towns[NextRoadIndex(ref w.parentRoadCursor,w.towns.Count)];var root=local.FindBuilding(town.hub);var parent=w.towns.Find(t=>t.id==town.parent);var other=parent==null?null:local.FindBuilding(parent.hub);
                if(root==null||other==null||root.removed||other.removed||!root.Complete||!other.Complete||!simulation.Autonomous(local,root.team)||!StartupReady(local,root.team)||simulation.SimulationTime<root.roadRetryAfter||local.Roads.Connected(root,other))continue;
                if(!local.Roads.ConnectToNetwork(root,other,out _))root.roadRetryAfter=simulation.SimulationTime+10;return true;
            }
            // Visit the actual building list in bounded pages, independent of the
            // placement anchor ring. Old, manually placed and low-index sites count too.
            for(int i=0;i<Math.Min(32,local.Buildings.Count);i++){
                var site=local.Buildings[NextRoadIndex(ref w.roadCursor,local.Buildings.Count)];if(site.removed||simulation.SimulationTime<site.roadRetryAfter||!simulation.Autonomous(local,site.team))continue;
                if(!StartupReady(local,site.team)&&!StartupRoadRequired(site.definition))continue;
                var c=local.Settlements[site.team];if(c==null)continue;var town=Towns(c).Find(t=>t.id==site.settlementId)??Nearest(Towns(c),CubeSphere.Decode(site.position));var root=town==null?null:local.FindBuilding(town.hub);
                if(root==null||root.removed||!root.Complete||root==site)continue;
                if(local.Roads.Connected(site,root)){if(site.roadSuspended){site.roadSuspended=false;site.roadFailures=0;local.NotifyChanged();}continue;}
                if(!local.Roads.ConnectToNetwork(site,root,out _))site.roadRetryAfter=simulation.SimulationTime+10;return true;
            }return false;
        }
        internal static bool StartupRoadRequired(BuildingDefinition d)=>d.id=="mine"||d.id=="civil_gathering_camp"||d.id=="civil_fabricator"||d.id=="civil_mine_water"||d.id=="civil_hydroponics"||d.id=="civil_food_processing"||d.id=="civil_warehouse"||d.generation>0||d.housingCapacity>0;
        bool TryExpand(PlanetRtsBase local,Work w,ColonyState c,RegionalSettlement parent,RtsBuilding root,Stats stats){
            var settings=simulation.Settings;if(simulation.SimulationTime<w.blockedUntil[c.faction])return false;if(w.failedProspects[c.faction]>=32)w.failedProspects[c.faction]=0;
            if(simulation.SimulationTime<w.nextProspect||!ExpansionReady(parent,stats.count,simulation.SimulationTime,settings)||!root.Complete||Towns(c).Count>=settings.maximumRegionalSettlements)return false;
            var pending=PendingConnection(local,w,c);if(pending!=null){parent.status="Waiting for the road connection to "+pending.name;return false;}
            w.nextProspect=simulation.SimulationTime+5;var cost=local.Catalog.Buildings["command"].cost;if(local.Factions[c.faction].wallet.Stock.materials<cost.materials+settings.regionalFoundingReserve||!local.Factions[c.faction].wallet.CanAfford(cost)){parent.status="Saving resources for the next settlement HQ";return false;}
            if(++w.failedProspects[c.faction]>=32)w.blockedUntil[c.faction]=simulation.SimulationTime+30;
            float distance=Mathf.Lerp(local.World.settlementMinimumSeparation,Math.Max(local.World.settlementMinimumSeparation,local.World.settlementMaximumSeparation),((w.prospectTurn*7)%17)/16f);if(distance>local.World.definition.radius*Mathf.PI*.5f)return false;
            float angle=w.prospectTurn++*2.39996323f;var up=CubeSphere.Decode(parent.position);var east=Vector3.Cross(Mathf.Abs(up.y)>.95?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);float arc=distance/local.World.definition.radius;var point=(up*Mathf.Cos(arc)+(east*Mathf.Cos(angle)+north*Mathf.Sin(angle))*Mathf.Sin(arc)).normalized;
            if(!PlanetRtsBase.CommandLandAllowed(local.SettlementTerritoryOwner(point),c.faction))return false;
            foreach(var colony in local.Settlements)if(colony!=null)foreach(var town in Towns(colony))if(CubeSphere.ArcDistance(point,CubeSphere.Decode(town.position),local.World.definition.radius)<local.World.settlementMinimumSeparation)return false;
            if(!PlanetStartingRegion.RegionalSite(local.World.definition,point,up))return false;
            bool coast=false;var surface=new PlanetSurface(local.World.definition);for(int i=0;i<8;i++){float a=i*Mathf.PI*.25f;var sample=(point+(east*Mathf.Cos(a)+north*Mathf.Sin(a))*(3000/local.World.definition.radius)).normalized;if(surface.SampleHeight(sample)<local.World.definition.oceanHeight){coast=true;break;}}
            var env=simulation.Planet(local.World.definition.bodyId).environment;bool fertile=env.atmosphere&&Array.Exists(simulation.Catalogue.crops,crop=>Math.Abs(env.temperature-crop.temperature)<=crop.tolerance);var kind=SettlementProfiles.Choose(Towns(c),c,w.profileTurn++,fertile,coast);
            var next=new RegionalSettlement{id=simulation.State.AllocateId(),kind=kind,parent=parent.id,position=CubeSphere.Encode(point),founded=simulation.SimulationTime,name=SettlementProfiles.Get(kind).label+" "+(Towns(c).Count+1)};
            if(!local.FoundRegionalSettlement(next,c.faction,out string reason)){parent.status=reason;return false;}w.failedProspects[c.faction]=0;w.blockedUntil[c.faction]=0;Towns(c).Add(next);w.towns.Add(next);For(w.current,next.id).Add(local.FindBuilding(next.hub));parent.nextExpansion=simulation.SimulationTime+settings.regionalExpansionSeconds;next.nextExpansion=parent.nextExpansion;parent.status="Founded "+next.name;return true;
        }
    }
}
