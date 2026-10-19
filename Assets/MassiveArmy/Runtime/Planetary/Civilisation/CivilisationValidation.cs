using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetRtsBase {
        internal RtsBuilding DisconnectedCropFixture(){
            var definition=Catalog.Buildings["civil_farm_wheat"];
            for(int attempt=0;attempt<96;attempt++){
                float angle=attempt*2.3999632f,range=120+Mathf.Sqrt(attempt)*12;
                var point=TeamOffset(0,new Vector2(Mathf.Cos(angle)*range,Mathf.Sin(angle)*range));
                int rotation=EntranceRotation(definition,point,0);point=SnapBuilding(definition,point,rotation);
                if(Roads.Node(Grid.Direction(Grid.Port(Grid.Area(definition,point,rotation))))>=0)continue;
                if(ConstructOriented(definition,0,point,rotation))return Buildings[Buildings.Count-1];
            }return null;
        }
    }
    [DefaultExecutionOrder(-230)]public sealed class CivilisationValidation:MonoBehaviour {
        [Serializable]sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();public int runtimeErrors,roadTiles,population;public double catchupMaxMs;public long allocatedBytes,reservedBytes;}
        readonly Report report=new Report();PlanetDevelopment world;CivilisationRuntime simulation;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-civilisation-validation")>=0)new GameObject("Civilisation milestone validation").AddComponent<CivilisationValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.Paused=true;world.requestedCapacity=8192;world.ambientZombies=0;Time.captureDeltaTime=1f/60;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)report.runtimeErrors++;}
        void OnDestroy(){Time.captureDeltaTime=0;Application.logMessageReceived-=Log;}
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);report.checks.Add(message);}
        void Start()=>StartCoroutine(Guard());
        IEnumerator Guard(){var run=Run();while(true){object current;try{if(!run.MoveNext())break;current=run.Current;}catch(Exception error){report.status="FAIL";report.error=error.ToString();Debug.LogException(error);break;}yield return current;}report.allocatedBytes=Profiler.GetTotalAllocatedMemoryLong();report.reservedBytes=Profiler.GetTotalReservedMemoryLong();if(simulation!=null)report.catchupMaxMs=simulation.MaximumCatchupSliceMs;File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void Step(double seconds){while(seconds>1e-9){double step=Math.Min(1,seconds);world.Base.CargoNavigation?.Step((float)step,true);simulation.Advance(step);seconds-=step;}int budget=10000;while(simulation.CatchingUp(world.definition.bodyId)&&budget-->0)simulation.Advance(0);if(budget<=0)throw new TimeoutException("Economy catch-up did not converge");}
        RtsBuilding Find(string id){foreach(var b in world.Base.Buildings)if(b.team==0&&!b.removed&&b.definition.id==id)return b;return null;}
        SurfaceInstallation Installation(ColonyState colony,RtsBuilding building)=>SettlementLogistics.Find(colony,building.persistentId);
        void Supply(ColonyState colony,SurfaceInstallation installation,string resource,double amount){Check(colony.inventory.Spend(resource,amount),"Fixture allocates finite "+resource+" supply");installation.inventory.Deposit(resource,amount);}
        IEnumerator Run(){
            double deadline=Time.realtimeSinceStartupAsDouble+120;while(!world.Ready&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Check(world.Ready,"Planetary session initialises with civilisation enabled");yield return null;
            simulation=world.SystemRuntime.Civilisation;simulation.Settings.autoExtendSettlementRoads=false;Check(simulation!=null,"Actual galaxy entry owns one civilisation runtime");foreach(var f in simulation.State.factions)f.autonomous=false;world.Rig.InputEnabled=false;
            var local=world.Base;var state=simulation.State;var home=simulation.Planet(world.definition.bodyId);var colony=simulation.Colony(home.body,0);Check(colony!=null,"Starter faction owns a persistent home settlement");
            var ids=new HashSet<string>();foreach(var body in world.SystemRuntime.Galaxy.Nodes)Check(ids.Add(body.stableId),"Stable celestial identity "+body.stableId);
            Check(state.planets.Count==world.SystemRuntime.Bodies.Length,"Every spherical planet has one composition/navigation state");
            Check(state.colonies.Count==2&&state.colonies[0].population==24&&state.colonies[1].population==24,"Each starting settlement owns its separate civilian population");
            foreach(var resource in simulation.Catalogue.resources)if(resource.bootstrap)Check(home.composition.Get(resource.id)>=simulation.Settings.bootstrapAbundanceFloor,"Homeworld bootstrap abundance: "+resource.id);
            bool rich=false;foreach(var planet in state.planets)if(planet.composition.Get("titanium")>.5)rich=true;Check(rich&&home.composition.Get("titanium")<.01,"Strategic scarcity differs from useful expansion worlds");
            Check(state.Faction(0).knowledge.bodies.Count==1&&state.Faction(1).knowledge.bodies.Count==1,"Both factions begin with home knowledge only");
            string unknown=state.planets[1].body;state.Faction(0).knowledge.Record(unknown).level=DiscoveryLevel.Detected;Check(!state.Faction(1).knowledge.Knows(unknown),"Faction discovery never mutates another faction's knowledge");
            int civilians=colony.population;
            colony.population=0;CivilisationRuntime.AllocateWorkers(local,colony,0);
            Check(colony.employed==0,"Zero population cannot staff buildings");
            foreach(var b in local.Buildings)if(b.team==0&&b.definition.workers>0)Check(b.WorkforceEfficiency==0,"Unstaffed production stops: "+b.definition.id);
            colony.population=civilians;CivilisationRuntime.AllocateWorkers(local,colony,0);
            Check(colony.employed<=colony.population&&colony.employed<=colony.workerDemand,"Worker allocation conserves civilian population and jobs");
            Check(local.Catalog.Buildings["civil_food_processing"].workers>0&&local.Catalog.Buildings["civil_farm_wheat"].workers>0,"Factories and farms require civilian workers");
            Check(simulation.Catalogue.Resources.ContainsKey("methane")&&local.Nodes.Exists(n=>n.elementId=="methane"&&n.kind==NodeKind.Gas),"Finite underground methane deposits and matching gas extractor are generated");
            var livestock=new ResourceInventory();livestock.Bind(simulation.Catalogue);IndustryRecipe beefRecipe=Array.Find(simulation.Catalogue.recipes,x=>x.id=="beef_and_methane");livestock.Deposit("grain",4);livestock.Deposit("water",3);livestock.RunRecipe(beefRecipe,30);
            Check(livestock.Get("beef")==6&&livestock.Get("methane")==1&&livestock.Get("grain")==0&&livestock.Get("water")==0,"Sealed beef sheds consume feed and water and produce finite beef plus captured methane");livestock.RunRecipe(beefRecipe,30);Check(livestock.Get("methane")==1,"Missing feed stops livestock methane production");
            var bottled=new ResourceInventory();bottled.Bind(simulation.Catalogue);bottled.Deposit("water",3);bottled.RunRecipe(Array.Find(simulation.Catalogue.recipes,x=>x.id=="bottle_drinks"),10);Check(bottled.Get("drinks")==4&&bottled.Get("water")==0,"Bottled drinks consume real water");
            Check(local.Catalog.Buildings["civil_mine_methane"].extraction*simulation.Settings.geologicalMinimumEfficiency>1.0/30,"Even low-yield geological extraction produces more methane per second than a beef shed");
            foreach(var b in local.Buildings)if(b.definition.id!="command"&&b.definition.id!="outpost"&&b.definition.housingCapacity<=0){var target=local.EntranceAnchor(b.team,CubeSphere.Decode(b.position));float actual=(local.Grid.Direction(local.Grid.Port(local.Area(b)))-target).sqrMagnitude;for(int rot=0;rot<4;rot++)Check(actual<=(local.Grid.Direction(local.Grid.Port(local.Grid.Area(b.definition,CubeSphere.Decode(b.position),rot)))-target).sqrMagnitude+1e-9f,"Starter entrance faces its nearest command anchor: "+b.definition.id);}
            double old=local.Faction.wallet.Stock.materials;colony.inventory.Deposit("construction_materials",10);Check(Math.Abs(local.Faction.wallet.Stock.materials-old-10)<.001,"Extended materials use the existing wallet balance");colony.inventory.Spend("construction_materials",10);
            var inventory=new ResourceInventory();inventory.Bind(simulation.Catalogue);inventory.Deposit("iron",8);inventory.Deposit("carbon",2);IndustryRecipe steel=null;foreach(var recipe in simulation.Catalogue.recipes)if(recipe.id=="steel")steel=recipe;
            inventory.RunRecipe(steel,steel.seconds*2);Check(inventory.Get("iron")==0&&inventory.Get("carbon")==0&&inventory.Get("steel")==8,"Recipes consume finite inputs and create configured outputs");double before=inventory.Get("steel");inventory.RunRecipe(steel,100);Check(inventory.Get("steel")==before,"Missing inputs stop manufacturing");
            Check(!inventory.Spend(new[]{new MaterialQuantity("steel",5),new MaterialQuantity("steel",5)}),"Duplicate manifest lines cannot overspend a stockpile");
            foreach(var definition in local.Catalog.buildings)if(definition.id!="civil_rail")Check(definition.foodUse>0&&definition.consumption>0,"Food and energy upkeep configured: "+definition.id);
            var warehouse=Find("civil_warehouse");Check(warehouse!=null&&ReferenceEquals(Installation(colony,warehouse).inventory,colony.inventory),"Warehouse holds the actual settlement inventory");
            Check(local.Roads.Connected(Find("barracks"),warehouse),"Starter production is connected through placed spherical road tiles");
            ValidateGrid(local);
            Check(local.CargoNavigation!=null,"Cargo uses a dedicated GPU road navigation subsystem");
            Check(RoadTile.Classify(3)==RoadShape.Straight&&RoadTile.Classify(5)==RoadShape.Corner&&RoadTile.Classify(7)==RoadShape.Tee&&RoadTile.Classify(15)==RoadShape.Cross,"One-cell road modules classify straights, corners, T-junctions and crossings");
            foreach(var tile in local.Roads.Tiles){int mask=tile.connections;Check(mask>=0&&mask<=15,"Road module has a cardinal connection mask");}
            Check(colony.cargoVehicles.Count==1,"Player and AI begin with an explicit starter cargo vehicle");
            Check(simulation.Logistics.Order(local,0,false),"Additional cargo transport is ordered through paid connected production");
            Check(local.TryConstructInfrastructure("civil_mine_iron",0),"New elemental extractor uses existing paid building placement");var mine=Find("civil_mine_iron");mine.construction=mine.definition.time;Step(1);var mineState=Installation(colony,mine);Supply(colony,mineState,"food",5);
            int savedPopulation=colony.population;double savedGrowth=colony.growthProgress;colony.population=0;colony.growthProgress=0;
            double oreBefore=mineState.inventory.Get("iron");Step(3);
            Check(mine.WorkersAssigned==0&&mineState.inventory.Get("iron")==oreBefore,"Actual extraction halts with no civilian workers");
            colony.population=savedPopulation;colony.growthProgress=savedGrowth;Step(1);
            Check(mine.WorkersAssigned>0&&mineState.inventory.Get("iron")>oreBefore,"Supplied extraction resumes when residents staff it");
            Check(!local.Roads.Connected(mine,warehouse),"GPU road graph excludes a disconnected production building");
            double warehouseIron=colony.inventory.Get("iron");Step(30);Check(mineState.inventory.Get("iron")>0&&Math.Abs(colony.inventory.Get("iron")-warehouseIron)<1e-6,"Disconnected extraction stores goods locally without teleporting them to the warehouse");
            int roads=local.Roads.Tiles.Count;double materials=colony.inventory.Get("construction_materials");Check(local.Roads.Connect(mine,warehouse,out var roadReason),"Player command constructs a valid road to the extractor: "+roadReason);Check(local.Roads.Tiles.Count>roads&&colony.inventory.Get("construction_materials")<materials,"Road construction consumes materials only for new tiles");
            Step(120);bool gpuRouted=false;foreach(var vehicle in colony.cargoVehicles)if(vehicle.gpuRouteMetres>0)gpuRouted=true;Check(gpuRouted,"GPU road search reports route distances without downloading cart positions");Check(colony.inventory.Get("iron")>warehouseIron,"A finite cargo trip collects ore and delivers it to connected storage");
            local.CargoNavigation.Step(0,true);var tracked=colony.cargoVehicles[0];uint mission=tracked.navigationVersion;double routeDistance=tracked.gpuRouteMetres;local.CargoNavigation.ValidateStorageGrowth();local.CargoNavigation.Step(0,true);
            Check(tracked.navigationVersion==mission&&Math.Abs(tracked.gpuRouteMetres-routeDistance)<.01,"Growing road search storage preserves GPU mission and route progress");
            var farm=local.DisconnectedCropFixture();Check(farm!=null,"A disconnected farm parcel uses paid original building construction");farm.construction=farm.definition.time;Step(1);var field=Installation(colony,farm);Supply(colony,field,"food",5);Supply(colony,field,"water",30);Supply(colony,field,"seeds",3);
            Step(150);Check(field.harvests>0&&field.inventory.Get("grain")>0,"Crop parcels consume supplied water and seeds, grow and retain a finite harvest locally: harvests="+field.harvests+", growth="+field.growth+", workers="+farm.WorkersAssigned+", power="+local.Faction.efficiency+", food="+field.inventory.Get("food")+", efficiency="+farm.Efficiency+", water="+field.inventory.Get("water")+", seeds="+field.inventory.Get("seeds")+", suspended="+farm.roadSuspended);
            Check(!local.Roads.Connected(farm,warehouse),"Manual-road fixture leaves the new farm disconnected");simulation.Settings.autoExtendSettlementRoads=true;double beforeExtension=colony.inventory.Get("construction_materials");Step(12);
            Check(local.Roads.Connected(farm,warehouse)&&colony.inventory.Get("construction_materials")<beforeExtension,"Settlement expansion automatically pays for a road to the existing warehouse network");simulation.Settings.autoExtendSettlementRoads=false;
            Check(local.Catalog.Buildings.ContainsKey("civil_oil_well")&&local.Catalog.Buildings.ContainsKey("civil_oil_refinery")&&simulation.Catalogue.crops.Length==7,"Oil industry, markets and seven crop types are data-driven placeable infrastructure");
            Step(1);Check(colony.housingCapacity>=colony.population&&colony.population>=24,"Settlement growth respects civilian housing capacity (capacity "+colony.housingCapacity+", population "+colony.population+", warehouse food "+colony.inventory.Get("food").ToString("F2")+")");
            int population=colony.population;foreach(var b in local.Buildings)if(b.team==0&&b.definition.generation>0)b.powered=false;Step(180);Check(colony.powerGeneration==0&&colony.population==population,"No electrical generation prevents industrial operation and population growth");foreach(var b in local.Buildings)if(b.team==0&&b.definition.generation>0)b.powered=true;
            double simulated=home.lastSimulatedTime;double stored=colony.inventory.Get("iron");int buildings=local.Buildings.Count;world.Rig.Distance=world.definition.radius*4;world.Rig.RefreshPose();yield return null;yield return null;Check(!world.SimulationActive,"Distant planet leaves the local simulation area of interest");
            for(int i=0;i<75;i++)simulation.Advance(8);Check(home.lastSimulatedTime==simulated&&colony.inventory.Get("iron")==stored&&local.Buildings.Count==buildings,"Absent settlement does not tick, produce, transport or construct while the global clock advances");
            world.Rig.Distance=300;world.Rig.RefreshPose();yield return null;yield return null;Check(world.SimulationActive,"Returning to the planet reactivates its local context");
            int frames=0;while(simulation.CatchingUp(world.definition.bodyId)&&frames++<10000){simulation.Advance(0);yield return null;}Check(!simulation.CatchingUp(world.definition.bodyId)&&home.lastSimulatedTime==state.time,"Bounded event batches catch up missed economic time on return");
            string saved=simulation.Serialize();var loaded=CivilisationRuntime.Deserialize(saved,state.galaxySeed,simulation.Catalogue);Check(loaded.colonies.Count==state.colonies.Count&&loaded.colonies[0].population==colony.population,"Strategic save roundtrip preserves settlement identities and population");
            Check(loaded.planets[0].roads.Count==home.roads.Count&&loaded.colonies[0].cargoVehicles.Count==colony.cargoVehicles.Count,"Strategic save roundtrip preserves roads and cargo vehicle state");
            var root=world.Hud.Root;var button=root.Q<Button>("civilisation-open");Check(button!=null,"Settlement UI is integrated into the original RTS front");((Action)button.userData)();yield return null;Check(root.Q("modal").style.display==DisplayStyle.Flex,"Settlement panel opens through the real HUD command");
            ((Action)root.Q<Button>("resources-open").userData)();yield return null;
            foreach(var resource in simulation.Catalogue.resources)Check(root.Q("resource-row-"+resource.id)!=null,"Resource overview includes zero and nonzero stocks: "+resource.id);
            Check(root.Q("resource-population")!=null,"Resource overview exposes population, housing and workforce");
            CivilisationRuntime.ResourceTotals(colony,"food",out double storedFood,out double localFood,out double cargoFood);
            Check(storedFood==colony.inventory.Get("food")&&localFood>=0&&cargoFood>=0,"Resource overview separates warehouse, local and loaded cargo without counting warehouse twice");
            ((Action)root.Q<Button>("tech").userData)();Check(world.Hud.ModalOpen,"Technology can replace the resource modal");
            ((Action)root.Q<Button>("tech").userData)();Check(!world.Hud.ModalOpen,"Closing technology after resources releases modal input capture");
            ((Action)root.Q<Button>("resources-open").userData)();yield return null;
            var shopDefinition=local.Catalog.Buildings["civil_food_shop"];Check(shopDefinition.district==SettlementDistrict.Commercial&&local.Catalog.Buildings["civil_beef_shed"].district==SettlementDistrict.Agricultural&&local.Catalog.Buildings["civil_housing"].district==SettlementDistrict.Residential,"Commercial, agricultural and residential types are explicit catalogue data");
            Check(local.TryConstructInfrastructure("civil_food_shop",0),"Compact district planner places a paid food shop");var shop=Find("civil_food_shop");shop.construction=shop.definition.time;Step(1);var retail=Installation(colony,shop);retail.inventory.Deposit("food",30);simulation.ServeRetail(local,colony,1);Check(colony.retailFoodCoverage==0,"Disconnected shops cannot serve residents without a cargo network");
            Check(local.Roads.ConnectToNetwork(shop,Find("command"),out var shopRoad),"Retail entrance connects to the nearest HQ road component: "+shopRoad);shop.Efficiency=1;double retailStock=retail.inventory.Get("food");simulation.ServeRetail(local,colony,10);Check(colony.retailFoodCoverage>0&&retail.inventory.Get("food")<retailStock,"Connected shops serve aggregate residents by consuming actual local stock");
            foreach(string id in new[]{"civil_drinks_shop","civil_pharmacy"}){Check(local.TryConstructInfrastructure(id,0),"Place commercial service: "+id);var service=Find(id);service.construction=service.definition.time;Step(1);Check(local.Roads.ConnectToNetwork(service,Find("command"),out _),"Commercial service is connected to the HQ network: "+id);Installation(colony,service).inventory.Deposit(service.definition.retailResource,10);Installation(colony,service).inventory.Deposit("food",10);}
            Step(1);simulation.ServeRetail(local,colony,10);Check(colony.retailDrinksCoverage>0&&colony.retailMedicineCoverage>0,"Drinks shops and pharmacies supply distinct finite resident needs (drinks "+colony.retailDrinksCoverage+", medical "+colony.retailMedicineCoverage+")");
            ((Action)root.Q<Button>("resources-open").userData)();yield return null;
            ((Action)root.Q<Button>("districts-toggle").userData)();Check(local.DistrictOverlay&&root.Q("district-legend").style.display==DisplayStyle.Flex,"District toggle enables world colours and their legend");
            double visualDeadline=Time.realtimeSinceStartupAsDouble+10;while(!world.SettlementVisualsReadyForValidation&&Time.realtimeSinceStartupAsDouble<visualDeadline)yield return null;Check(world.SettlementVisualsReadyForValidation,"Batched parcel and road rendering catches up with the newly expanded network");for(int i=0;i<4;i++)yield return null;Capture("settlement");Capture("districts");var uiTarget=new RenderTexture(1440,900,0,RenderTextureFormat.ARGB32);uiTarget.Create();world.Hud.SetRenderTarget(uiTarget);
            for(int frame=0;frame<6;frame++)yield return null;
            var previousTarget=RenderTexture.active;RenderTexture.active=uiTarget;var screen=new Texture2D(1440,900,TextureFormat.RGBA32,false);screen.ReadPixels(new Rect(0,0,1440,900),0,0);screen.Apply();File.WriteAllBytes(Path.Combine(output,"resources-screen.png"),screen.EncodeToPNG());Destroy(screen);((Action)root.Q<Button>("civilisation-open").userData)();for(int frame=0;frame<6;frame++)yield return null;
            var sceneTarget=new RenderTexture(1440,900,24,RenderTextureFormat.ARGBHalf);sceneTarget.Create();var combinedTarget=new RenderTexture(1440,900,0,RenderTextureFormat.ARGB32);combinedTarget.Create();var compositor=new Material(Resources.Load<Shader>("MassiveArmy/PlanetUiComposite"));compositor.SetTexture("_Ui",uiTarget);world.View.targetTexture=sceneTarget;RenderPipeline.SubmitRenderRequest(world.View,new RenderPipeline.StandardRequest{destination=sceneTarget});Graphics.Blit(sceneTarget,combinedTarget,compositor);
            RenderTexture.active=combinedTarget;var legendScreen=new Texture2D(1440,900,TextureFormat.RGBA32,false);legendScreen.ReadPixels(new Rect(0,0,1440,900),0,0);legendScreen.Apply();File.WriteAllBytes(Path.Combine(output,"district-key.png"),legendScreen.EncodeToPNG());Destroy(legendScreen);RenderTexture.active=previousTarget;world.View.targetTexture=null;sceneTarget.Release();combinedTarget.Release();Destroy(sceneTarget);Destroy(combinedTarget);Destroy(compositor);world.Hud.SetRenderTarget(null);uiTarget.Release();Destroy(uiTarget);report.roadTiles=home.roads.Count;report.population=colony.population;Check(report.runtimeErrors==0,"Integrated roads, economy, farms, populations and catch-up emit no runtime errors");
        }
        void ValidateGrid(PlanetRtsBase local){
            foreach(var building in local.Buildings){var up=CubeSphere.Decode(building.position);var area=local.Area(building);Check(CubeSphere.ArcDistance(up,local.Grid.Centre(area),world.definition.radius)<.04,"Building snaps to the shared road lattice: "+building.definition.id);Check(Vector3.Dot(building.visual.transform.up,up)>.99999,"Building frame follows the local planetary up direction");Check(!area.Contains(local.Grid.Port(area)),"Road entrance lies immediately outside its building grid footprint");}
            var definition=local.Catalog.Buildings["barracks"];
            for(int face=0;face<6;face++)for(int rotation=0;rotation<4;rotation++){
                var direction=CubeSphere.Direction((CubeFace)face,new Vector2(.42f,-.37f));var area=local.Grid.Area(definition,direction,rotation);var centre=local.Grid.Centre(area);var port=local.Grid.Port(area);var door=local.Grid.Door(area);bool adjacent=false;for(int edge=0;edge<4;edge++){var n=local.Grid.Neighbour(door,edge);adjacent|=n.face==port.face&&n.x==port.x&&n.y==port.y;}Check(adjacent,"Rotated building port is exactly one cardinal cell from its frontage");
                local.Grid.WarpAxes(area,out var x,out var z,out float plane);var q=local.Grid.Rotation(area);float foundation=world.definition.radius+10;var localCorner=new Vector3(-definition.footprintWidth*4,0,-definition.footprintDepth*4);var warped=PlanetConstructionGrid.Warp(localCorner,x,z,plane,foundation);var body=centre*foundation+q*warped;Check(Math.Abs(body.magnitude-foundation)<.05,"Building vertices follow constant planetary radius across cube faces and rotations");
                Vector3 edgeCorner=local.Grid.Point(area,rotation==0||rotation==3?0:1,rotation==0||rotation==1?0:1);Check(CubeSphere.ArcDistance(body.normalized,edgeCorner,world.definition.radius)<.08,"Curved building corner aligns with its road-grid footprint");
            }
            for(int face=0;face<6;face++)for(int edge=0;edge<4;edge++){
                int rotation=edge==0?1:edge==1?3:edge==2?2:0;var uv=edge<2?new Vector2(edge==0?-.999999f:.999999f,.3f):new Vector2(.3f,edge==2?-.999999f:.999999f);var area=local.Grid.Area(definition,CubeSphere.Direction((CubeFace)face,uv),rotation);var port=local.Grid.Port(area);Check(port.face!=area.face,"Building entrance crosses each cube seam through a valid road neighbour");
            }
            var tiny=PlanetConstructionGrid.Warp(new Vector3(8,0,0),Vector3.right/120000,Vector3.forward/120000,1,120000);Check(tiny.y<-.0001f&&Math.Abs(tiny.y+64f/240000)<.00001,"Sub-millimetre curvature survives float precision at a 120 km radius");
            foreach(var tile in local.Roads.Tiles)foreach(var b in local.Buildings)Check(!local.Area(b).Contains(tile.cell),"Road modules do not occupy building footprint cells");
        }
        void Capture(string name){var target=new RenderTexture(1440,900,24,RenderTextureFormat.ARGBHalf);target.Create();world.View.enabled=false;world.View.targetTexture=target;RenderPipeline.SubmitRenderRequest(world.View,new RenderPipeline.StandardRequest{destination=target});var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1440,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1440,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);RenderTexture.active=previous==target?null:previous;world.View.targetTexture=null;target.Release();Destroy(target);}
    }
}
