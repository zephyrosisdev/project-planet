using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Bounded CPU infrastructure; army records remain exclusively on the GPU.</summary>
    public sealed partial class PlanetRtsBase : IDisposable
    {
        public readonly RtsCatalog Catalog=RtsCatalog.Load();
        public CivilisationRuntime Civilisation=>fixture.SystemRuntime?.Civilisation;
        public PlanetDevelopment World=>fixture;
        public SettlementCitizens Citizens {get;internal set;}
        public SettlementRoadNetwork Roads {get;internal set;}
        public SettlementRoadNetwork Rails {get;internal set;}
        public ColonyState[] Settlements {get;internal set;}
        public readonly RtsFaction[] Factions={new RtsFaction(),new RtsFaction()};
        public RtsFaction Faction=>Factions[0];
        public readonly Vector3[] Homes=new Vector3[2];
        public readonly List<RtsBuilding> Buildings=new List<RtsBuilding>();
        public PlanetProceduralResources Natural {get;private set;}
        internal int ResolveResourceNode(int id)=>id>=-1?id:Natural.Materialize(-id-1)?.id??-1;
        public readonly List<ResourceNode> Nodes=new List<ResourceNode>();
        public RtsBuilding Selected {get;private set;}
        public BuildingDefinition Placing {get;private set;}
        public string Outcome {get;private set;}="";
        public string Notice="Select a building to train units, construct or research.";
        public int Revision {get;private set;}
        public int PlacementRotation {get;private set;}
        public void ClearSelection(){if(Selected!=null){Selected=null;Revision++;}}
        readonly PlanetDevelopment fixture;readonly PlanetSurface surface;readonly RtsModelArt art;
        readonly GameObject root;readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly GraphicsBuffer aim;readonly Material nodeMaterial,ghostMaterial;
        readonly List<GameObject> markers=new List<GameObject>();GameObject ghost;Vector3 ghostDirection;
        readonly Vector3 anchor,right,forward;bool manualPlacementRotation;
        public PlanetRtsBase(PlanetDevelopment fixture,PlanetSurface surface)
        {
            this.fixture=fixture;this.surface=surface;Grid=new PlanetConstructionGrid(fixture.definition.bodyId,fixture.definition.radius,Civilisation?.Settings.roadTileSize??8);anchor=fixture.Rig.FocusDirection;right=Vector3.Cross(Vector3.up,anchor).normalized;forward=Vector3.Cross(anchor,right);
            Civilisation?.Catalogue.Extend(Catalog);
            art=RtsModelArt.Load();if(art==null)throw new InvalidOperationException("Missing current RTS model art");
            root=new GameObject("Planet RTS infrastructure");root.transform.SetParent(fixture.transform,false);aim=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128,16);aim.SetData(new Vector4[128]);
            ghostMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Ghost"));nodeMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Structure"));nodeMaterial.SetColor("_Tint",new Color(.65f,.48f,.04f));
            Factions[0].team=0;Factions[1].team=1;foreach(var faction in Factions){faction.wallet.Capacity=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};faction.wallet.Stock=new ResourceCost{materials=1800,fuel=350,rareElements=160,research=100};}
            Homes[0]=anchor;Homes[1]=fixture.OpposingHome.sqrMagnitude>.5f?fixture.OpposingHome:Offset(new Vector2(Mathf.Min(4500,fixture.definition.radius*.9f),0));
            bool starter=Civilisation==null||Civilisation.IsHome(fixture.definition.bodyId);if(starter){Seed("command",new Vector2(-65,-55));Seed("barracks",new Vector2(30,-75));Seed("light_depot",new Vector2(90,-30));Seed("solar",new Vector2(-90,15));if(Civilisation!=null){Seed("civil_warehouse",new Vector2(-10,40));SeedFamilyHomes(0);}}else foreach(var faction in Factions)faction.wallet.Stock=new ResourceCost();
            SeedResourceNodes(0);
            if(starter&&fixture.StrategyEnabled&&fixture.sessionMode!=PlanetGameMode.Swarm){FindEnemyHome();Seed("command",new Vector2(-65,-55),1);Seed("barracks",new Vector2(30,-75),1);Seed("light_depot",new Vector2(90,-30),1);Seed("solar",new Vector2(-90,15),1);if(Civilisation!=null){Seed("civil_warehouse",new Vector2(-10,40),1);SeedFamilyHomes(1);}SeedResourceNodes(1);}
            InitializeGeology();Natural=new PlanetProceduralResources(this,fixture.definition);Civilisation?.Attach(this,fixture.definition.bodyId);
            Selected=Buildings.FirstOrDefault();fixture.RefreshBuildingCosts(Buildings);Rebase();
        }
        public Vector3 Offset(Vector2 metres)=> (anchor+(right*metres.x+forward*metres.y)/fixture.definition.radius).normalized;
        float Distance(Vector3 a,Vector3 b)=>CubeSphere.ArcDistance(a,b,fixture.definition.radius);
        void SeedFamilyHomes(int team){
            // Validate the whole block before committing: two rows share a single one-cell street.
            var d=Catalog.Buildings["civil_housing"];int width=d.footprintWidth,depth=d.footprintDepth;
            for(int attempt=0;attempt<32;attempt++){
                var origin=Grid.Area(d,TeamOffset(team,new Vector2(55+(attempt%4)*24,55-(attempt/4)*16)),0);
                var points=new Vector3[6];bool valid=true;
                for(int i=0;i<6;i++){int row=i/3;points[i]=Grid.Point(origin.face,origin.x+(i%3)*width+width*.5,origin.y+row*(depth+1)+depth*.5);if(!Validate(d,points[i],row==0?0:2,false,team).valid){valid=false;break;}}
                if(!valid)continue;
                for(int i=0;i<6;i++)Create(Catalog.Buildings[i%3==0?"civil_social_home":i%3==1?"civil_rental_home":"civil_housing"],points[i],i<3?0:2,true,team);
                return;
            }
            throw new InvalidOperationException("No valid starter residential street");
        }
        internal void SeedResidentialStreet(int team){
            RtsBuilding first=null,last=null;foreach(var b in Buildings)if(b.team==team&&b.definition.housingCapacity>0){if(first==null)first=b;last=b;}
            if(first!=null&&last!=null)Roads.Connect(first,last,out _,true);
        }
        void Seed(string id,Vector2 offset,int team=0){var d=Catalog.Buildings[id];for(int ring=0;ring<8;ring++){var direction=TeamOffset(team,offset+new Vector2(0,-ring*15));int rotation=Civilisation==null||id=="command"?0:EntranceRotation(d,direction,team);if(Validate(d,direction,rotation,false,team).valid){Create(d,direction,rotation,true,team);return;}}throw new InvalidOperationException("No valid starter footprint for "+id);}
        public PlacementResult Validate(BuildingDefinition d,Vector3 direction,int rotation,bool requireAccess=true,int team=0)
        {
            direction=SnapBuilding(d,direction,rotation);var area=Grid.Area(d,direction,rotation);var Faction=Factions[team];
            if(requireAccess&&Faction.tier<d.tier)return new PlacementResult(false,"Requires command tier "+d.tier);
            if(requireAccess&&!Faction.HasTech(d.requiredTech))return new PlacementResult(false,"Requires "+d.requiredTech);
            if(requireAccess&&!ConstructionCost(team,direction,d.cost,false))return new PlacementResult(false,"Insufficient resources");
            float radius=Mathf.Sqrt(d.footprintWidth*d.footprintWidth+d.footprintDepth*d.footprintDepth)*4;
            if(!GridClear(area,out var gridReason,d))return new PlacementResult(false,gridReason);
            if(requireAccess&&!CivilianExpansion(d)&&!ProtectionAccess(d,direction,team)&&!Buildings.Any(b=>b.team==team&&b.Complete&&b.definition.buildRadius>0&&Distance(direction,CubeSphere.Decode(b.position))<b.definition.buildRadius))return new PlacementResult(false,"Outside construction radius");
            float min=float.MaxValue,max=float.MinValue;bool land=false,water=false;
            for(int i=0;i<5;i++){var p=i==4?direction:Grid.Point(area,i&1,(i>>1)&1);float height=surface.SampleHeight(p);min=Mathf.Min(min,height);max=Mathf.Max(max,height);land|=height>=fixture.definition.oceanHeight;water|=height<fixture.definition.oceanHeight;}
            if(d.terrain==TerrainRule.Coast){if(!land||!water)return new PlacementResult(false,"Requires a coastline");}
            else if(water)return new PlacementResult(false,"Footprint must be on dry land");
            if(max-min>8)return new PlacementResult(false,"Footprint slope is too steep");
            if(d.terrain==TerrainRule.Node){foreach(var node in Nodes)if(node.claimedBy<0&&node.remaining>0&&!RecoveryNodeReserved(node.id,team)&&Array.IndexOf(d.nodes,node.kind)>=0&&string.Equals(node.elementId??"",d.extractedResource??"",StringComparison.Ordinal)&&Distance(direction,CubeSphere.Decode(node.position))<12)return new PlacementResult(true,"",node.id);var natural=Natural?.At(direction,d);if(natural!=null&&natural.claimedBy<0&&natural.remaining>0)return new PlacementResult(true,"",-natural.proceduralKey-1);return new PlacementResult(false,"Requires a matching unclaimed resource deposit");}
            return new PlacementResult(true,"");
        }
        static float FootprintRadius(BuildingDefinition d)=>Mathf.Sqrt(d.footprintWidth*d.footprintWidth+d.footprintDepth*d.footprintDepth)*4;
        RtsBuilding Create(BuildingDefinition d,Vector3 direction,int rotation,bool complete=false,int team=0)
        {
            direction=SnapBuilding(d,direction,rotation);var b=new RtsBuilding{team=team,index=Buildings.Count,definition=d,MinimumFoodEfficiency=CivilisationRuntime.RecoveryEssential(d)?.25f:0,position=CubeSphere.Encode(direction),rotation=rotation,health=d.health,construction=complete?d.time:0,rally=CubeSphere.Encode((direction+right*(50/fixture.definition.radius)).normalized)};
            b.visual=new GameObject((team==1?"Enemy ":"")+d.name);b.visual.transform.SetParent(root.transform,false);var model=art.Find(d.id)??art.Find(d.mesh);if(model==null)throw new InvalidOperationException("Missing current building model "+d.id);
            if(!d.id.StartsWith("civil_farm_")||model.customLods)AddMesh(b.visual.transform,d.housingCapacity>0&&model.far!=null?model.far:model.near,model.material,"Current RTS shell");if(d.housingCapacity>0)foreach(var renderer in b.visual.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;var collision=b.visual.AddComponent<BoxCollider>();collision.center=model.near.bounds.center;collision.size=Vector3.Max(model.near.bounds.size,Vector3.one*.5f);if(d.id.StartsWith("civil_farm_"))collision.enabled=false;if(model.gateLeaf!=null)AddMesh(b.visual.transform,BuildingCube(model),model.material,"Gate leaf");
            var scale=ModelScale(d);foreach(Transform child in b.visual.transform)child.localScale=scale;collision.center=Vector3.Scale(collision.center,scale);collision.size=Vector3.Scale(collision.size,scale);Buildings.Add(b);RegisterPlanningSite(b);RegisterBuildingView(b,model);Roads?.RefreshBuildings();Rails?.RefreshBuildings();Revision++;return b;
        }
        void AddMesh(Transform parent,Mesh mesh,Material material,string name){var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;properties.Clear();properties.SetBuffer("_StructureAim",aim);properties.SetFloat("_HasTurret",0);properties.SetColor("_TeamColor",parent.name.StartsWith("Enemy ")?new Color(1,.12f,.04f):new Color(.02f,.72f,1));renderer.SetPropertyBlock(properties);}
        Vector3 ModelScale(BuildingDefinition definition){if(art.Find(definition.id)?.customLods==true)return Vector3.one;if(!definition.id.StartsWith("civil_")||!Catalog.Buildings.TryGetValue(definition.mesh,out var source))return Vector3.one;float x=definition.footprintWidth/(float)source.footprintWidth,z=definition.footprintDepth/(float)source.footprintDepth;return new Vector3(x,Mathf.Min(x,z),z);}
        public void BeginPlacement(BuildingDefinition definition){if(definition.id=="civil_road"||definition.id=="civil_rail"){BeginRoadPlacement(definition.id=="civil_rail");return;}CancelRoadPlacement();CancelPlacement();Placing=definition;ghostDirection=fixture.Rig.FocusDirection;manualPlacementRotation=false;PlacementRotation=Civilisation==null?0:EntranceRotation(definition,ghostDirection,0);var model=art.Find(definition.id)??art.Find(definition.mesh);ghost=new GameObject("Planet building preview");ghost.transform.SetParent(root.transform,false);AddMesh(ghost.transform,model.near,ghostMaterial,"Footprint shell");foreach(Transform child in ghost.transform)child.localScale=ModelScale(definition);Notice="Place "+definition.name+" on the terrain • R rotate • right-click cancel";Revision++;}
        public void Rotate(){manualPlacementRotation=true;PlacementRotation=(PlacementRotation+1)&3;if(Placing!=null)ghostDirection=SnapBuilding(Placing,ghostDirection,PlacementRotation);Revision++;}
        public void CancelPlacement(){Placing=null;if(ghost!=null)constructionPoses.Remove(ghost.transform);ArmyLifetime.Destroy(ghost);ghost=null;Revision++;}
        public void Preview(Vector3 direction){if(ghost==null)return;if(!manualPlacementRotation&&Civilisation!=null){int rotation=PlacementRotation;AlignResidentialPreview(Placing,ref direction,ref rotation);PlacementRotation=rotation;}direction=SnapBuilding(Placing,direction,PlacementRotation);ghostDirection=direction;PlaceTransform(ghost.transform,Placing,direction,PlacementRotation);var valid=Validate(Placing,direction,PlacementRotation);ghostMaterial.SetColor("_Tint",valid.valid?new Color(.05f,1,.3f,.35f):new Color(1,.08f,.04f,.35f));Notice=valid.valid?"Click to construct "+Placing.name:valid.reason;}
        public bool Construct(Vector3 direction){if(Placing==null)return false;if(!manualPlacementRotation&&Civilisation!=null){int rotation=PlacementRotation;AlignResidentialPreview(Placing,ref direction,ref rotation);PlacementRotation=rotation;}direction=SnapBuilding(Placing,direction,PlacementRotation);var valid=Validate(Placing,direction,PlacementRotation);if(!valid.valid){Notice=valid.reason;return false;}
            var d=Placing;if(!fixture.IsFootprintClear(direction,FootprintRadius(d))){Notice="Units occupy this footprint";return false;}if(!ConstructionCost(0,direction,d.cost,true))return false;var b=Create(d,direction,PlacementRotation);b.node=ResolveResourceNode(valid.node);if(b.node>=0)Nodes[b.node].claimedBy=b.index;Selected=b;CancelPlacement();fixture.RefreshBuildingCosts(Buildings);Rebase();Notice="Constructing "+d.name;return true;}
        public bool Pick(Vector2 pixel){var ray=fixture.View.ScreenPointToRay(pixel);var centre=fixture.PlanetRenderCentre;float best=float.MaxValue;RtsBuilding picked=null;
            foreach(var b in Buildings){if(b.removed)continue;var p=fixture.RenderBodyPosition(surface.GetSurfacePositionPrecise(CubeSphere.Decode(b.position)));float t=Vector3.Dot(p-ray.origin,ray.direction);if(t<0||t>=best)continue;float radius=FootprintRadius(b.definition);if((ray.GetPoint(t)-p).sqrMagnitude>radius*radius)continue;
                // Reject infrastructure on the opposite side of the planet.
                if(fixture.Rig.GroundPoint(pixel,out var ground)&&Distance(ground,CubeSphere.Decode(b.position))>radius+15)continue;best=t;picked=b;}
            if(picked==null)return false;if(picked.team!=0&&fixture.sessionMode!=PlanetGameMode.AiTest){Notice="Enemy "+picked.definition.name;return true;}Selected=picked;Revision++;Notice=picked.definition.name;return true;
        }
        public bool QueueUnit(RtsBuilding building,string id){if(Civilisation!=null&&!Civilisation.Logistics.Connected(this,building)){Notice="Production requires a warehouse connection or air cargo";return false;}var Faction=Factions[building.team];var d=Catalog.Units[id];if(!building.Complete||Array.IndexOf(building.definition.production,id)<0||!Faction.HasTech(d.requiredTech)||building.level<d.producerLevel||building.queue.Count>=16||!ConstructionCost(building.team,CubeSphere.Decode(building.position),d.cost,true))return false;
            building.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time});Revision++;return true;}
        public bool Research(RtsBuilding building,string id){var Faction=Factions[building.team];var d=Catalog.Technologies[id];if(Civilisation!=null&&!Civilisation.Orbitals.CanResearch(Civilisation.BodyId(fixture.definition.bodyId),building.team,id))return false;if(!building.Complete||d.requiredBuilding!=building.definition.id||building.level<d.requiredBuildingLevel||Faction.HasTech(id)||Faction.researching.Contains(id)||!d.prerequisites.All(Faction.HasTech)||!ConstructionCost(building.team,CubeSphere.Decode(building.position),d.cost,true))return false;
            Faction.researching.Add(id);building.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time,research=true});Revision++;return true;}
        public void CancelQueue(RtsBuilding b,int index){if(index<0||index>=b.queue.Count)return;var Faction=Factions[b.team];var q=b.queue[index];RefundConstruction(b.team,CubeSphere.Decode(b.position),q.paid*(1-q.Fraction));if(q.research)Faction.researching.Remove(q.id);b.queue.RemoveAt(index);Revision++;}
        public void Upgrade(RtsBuilding b){if(!b.Complete||b.Upgrading||b.level>=b.definition.maxLevel)return;var Faction=Factions[b.team];var cost=b.definition.cost*(.65f*b.level);if(!ConstructionCost(b.team,CubeSphere.Decode(b.position),cost,true))return;b.upgradeTime=b.definition.time;b.upgradeProgress=0;Revision++;}
        public void Damage(RtsBuilding b,uint damage,bool hostile=true){if(b.removed||damage==0)return;if(hostile)RecordSiteAttack(b,damage>=b.health);b.health=Mathf.Max(0,b.health-damage);Revision++;if(b.health>0)return;b.removed=true;InvalidatePlacementOccupancy();b.visual.SetActive(false);Roads?.RefreshBuildings();Rails?.RefreshBuildings();if(b.node>=0)Nodes[b.node].claimedBy=-1;if(Selected==b)Selected=null;for(int i=b.queue.Count-1;i>=0;i--)CancelQueue(b,i);fixture.RefreshBuildingCosts(Buildings);Notice=b.definition.name+" destroyed";if(b.definition.id=="command"){if(fixture.sessionMode==PlanetGameMode.AiTest)Outcome="COMMAND CENTRE LOST · SPECTATOR SIMULATION CONTINUES";else if(b.team==0){Outcome="DEFEAT · COMMAND CENTRE DESTROYED";fixture.Paused=true;}else if(fixture.sessionMode==PlanetGameMode.Rts&&!Buildings.Any(other=>other.team==b.team&&!other.removed&&other.definition.id=="command")){Outcome="VICTORY - ENEMY COMMAND DESTROYED";fixture.Paused=true;}}}
        public void NotifyChanged()=>Revision++;
        public void CancelUpgrade(RtsBuilding b){if(!b.Upgrading)return;var Faction=Factions[b.team];RefundConstruction(b.team,CubeSphere.Decode(b.position),b.definition.cost*(.65f*b.level)*(1-b.UpgradeFraction));b.upgradeTime=b.upgradeProgress=0;Revision++;}
        public void Sell(RtsBuilding b){if(b.removed)return;var Faction=Factions[b.team];if(b.awaitingProjectMaterials)RefundPendingProject(b);else RefundConstruction(b.team,CubeSphere.Decode(b.position),b.definition.cost*(b.Complete?.45f:.8f));bool wasCommand=b.definition.id=="command";CancelSiteRecovery(b);Damage(b,(uint)Mathf.CeilToInt(b.health),false);if(wasCommand)fixture.Paused=false;}
        public void Tick(float dt){
            for(int team=0;team<2;team++){var Faction=Factions[team];
            Faction.tier=Buildings.Where(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.id=="command").Select(b=>b.level).DefaultIfEmpty(1).Max();
            RefreshSettlementPower(team);
            Faction.efficiency=Faction.demand<=0?1:Mathf.Clamp(Faction.generation/Faction.demand,Civilisation==null?.25f:0,1);
            foreach(var b in Buildings){if(b.team!=team||b.removed)continue;if(!b.Complete){if(b.awaitingProjectMaterials&&!ConsumeProjectMaterials(b))continue;b.construction=Mathf.Min(b.definition.time,b.construction+dt*(Civilisation==null?Mathf.Max(.35f,Faction.efficiency):(b.definition.generation>0?Mathf.Max(.25f,Faction.efficiency):Faction.efficiency)*Mathf.Min(1,b.constructionWorkers)));if(b.Complete){Revision++;Roads?.RefreshBuildings();Rails?.RefreshBuildings();}continue;}
                if(b.paused||!b.powered)continue;b.Efficiency=Faction.efficiency*(Civilisation==null?b.FoodEfficiency:Mathf.Max(b.MinimumFoodEfficiency,b.FoodEfficiency))*(Civilisation==null?1:b.WorkforceEfficiency);
                if(b.node>=0&&Civilisation==null){var node=Nodes[b.node];if(string.IsNullOrEmpty(node.elementId)){float extracted=Mathf.Min(node.remaining,b.definition.extraction*node.rateMultiplier*b.level*b.Efficiency*dt);node.remaining-=Faction.wallet.Deposit(node.resource,extracted);}}
                if(b.definition.researchRate>0)Faction.wallet.Deposit(ResourceKind.Research,b.definition.researchRate*b.Efficiency*dt);
                if(b.Upgrading){b.upgradeProgress+=dt*b.Efficiency;if(b.upgradeProgress>=b.upgradeTime){b.level++;if(b.definition.id=="command")Faction.tier=b.level;b.upgradeTime=0;b.health=b.MaxHealth;Revision++;}}
                if(b.repairing&&b.health<b.MaxHealth&&(Civilisation==null||b.constructionWorkers>0)&&Faction.wallet.Spend(new ResourceCost{materials=dt}))b.health=Mathf.Min(b.MaxHealth,b.health+25*dt);
                if(b.queue.Count==0)continue;var q=b.queue[0];q.progress=Mathf.Min(q.duration,q.progress+dt*b.Efficiency);if(q.progress<q.duration)continue;
                bool done;if(q.research){Faction.technologies.Add(q.id);Faction.researching.Remove(q.id);done=true;}else done=SpawnNear(b,Catalog.Units[q.id]);
                if(done){b.queue.RemoveAt(0);if(b.repeat&&!q.research)QueueUnit(b,q.id);Revision++;}else Notice="Production waiting for a clear compatible exit or free army capacity";
            }
            }
            UpdateGateVisuals();
            TickSiteProtection();
            ThinkPlanetAI(dt);
        }
        bool SpawnNear(RtsBuilding b,UnitDefinition d){var direction=CubeSphere.Decode(b.position);var tangent=Vector3.Cross(Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up,direction).normalized;var north=Vector3.Cross(direction,tangent);
            for(int ring=0;ring<6;ring++)for(int j=0;j<16;j++){float angle=j*Mathf.PI/8;var candidate=(direction+(tangent*Mathf.Cos(angle)+north*Mathf.Sin(angle))*(FootprintRadius(b.definition)+12+ring*12)/fixture.definition.radius).normalized;
                if(Buildings.Any(other=>Distance(candidate,CubeSphere.Decode(other.position))<FootprintRadius(other.definition)+d.radius+4))continue;if(fixture.TrySpawnUnit(Catalog.TypeIndex(d.id),candidate,b.team))return true;}return false;}
        void PlaceTransform(Transform transform,BuildingDefinition d,Vector3 direction,int rotation)=>PlaceConstruction(transform,d,direction,rotation);
        public void Rebase()=>RebaseInfrastructure();
        public void Dispose(){Natural?.Dispose();CancelPlacement();DisposeBuildingViews();ArmyLifetime.Destroy(root);ArmyLifetime.Destroy(nodeMaterial);ArmyLifetime.Destroy(ghostMaterial);aim.Dispose();}
    }
}









