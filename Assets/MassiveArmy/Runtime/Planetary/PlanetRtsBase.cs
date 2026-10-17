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
        public SettlementRoadNetwork Roads {get;internal set;}
        public ColonyState[] Settlements {get;internal set;}
        public readonly RtsFaction[] Factions={new RtsFaction(),new RtsFaction()};
        public RtsFaction Faction=>Factions[0];
        public readonly Vector3[] Homes=new Vector3[2];
        public readonly List<RtsBuilding> Buildings=new List<RtsBuilding>();
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
        readonly Vector3 anchor,right,forward;
        public PlanetRtsBase(PlanetDevelopment fixture,PlanetSurface surface)
        {
            this.fixture=fixture;this.surface=surface;Grid=new PlanetConstructionGrid(fixture.definition.bodyId,fixture.definition.radius,Civilisation?.Settings.roadTileSize??8);anchor=fixture.Rig.FocusDirection;right=Vector3.Cross(Vector3.up,anchor).normalized;forward=Vector3.Cross(anchor,right);
            Civilisation?.Catalogue.Extend(Catalog);
            art=Resources.Load<RtsModelArt>("MassiveArmy/ModelArt/Catalogue");if(art==null)throw new InvalidOperationException("Missing current RTS model art");
            root=new GameObject("Planet RTS infrastructure");root.transform.SetParent(fixture.transform,false);aim=new GraphicsBuffer(GraphicsBuffer.Target.Structured,128,16);aim.SetData(new Vector4[128]);
            ghostMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Ghost"));nodeMaterial=new Material(Resources.Load<Shader>("MassiveArmy/Structure"));nodeMaterial.SetColor("_Tint",new Color(.65f,.48f,.04f));
            Factions[0].team=0;Factions[1].team=1;foreach(var faction in Factions){faction.wallet.Capacity=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};faction.wallet.Stock=new ResourceCost{materials=1800,fuel=350,rareElements=160,research=100};}
            Homes[0]=anchor;Homes[1]=fixture.OpposingHome.sqrMagnitude>.5f?fixture.OpposingHome:Offset(new Vector2(Mathf.Min(4500,fixture.definition.radius*.9f),0));
            bool starter=Civilisation==null||Civilisation.IsHome(fixture.definition.bodyId);if(starter){Seed("command",new Vector2(-65,-55));Seed("barracks",new Vector2(30,-75));Seed("light_depot",new Vector2(90,-30));Seed("solar",new Vector2(-90,15));if(Civilisation!=null){Seed("civil_warehouse",new Vector2(-10,40));Seed("civil_housing",new Vector2(65,55));}}else foreach(var faction in Factions)faction.wallet.Stock=new ResourceCost();
            SeedResourceNodes(0);
            if(starter&&fixture.StrategyEnabled&&fixture.sessionMode!=PlanetGameMode.Swarm){FindEnemyHome();Seed("command",new Vector2(-65,-55),1);Seed("barracks",new Vector2(30,-75),1);Seed("light_depot",new Vector2(90,-30),1);Seed("solar",new Vector2(-90,15),1);if(Civilisation!=null){Seed("civil_warehouse",new Vector2(-10,40),1);Seed("civil_housing",new Vector2(65,55),1);}SeedResourceNodes(1);}
            InitializeGeology();Civilisation?.Attach(this,fixture.definition.bodyId);
            Selected=Buildings.FirstOrDefault();fixture.RefreshBuildingCosts(Buildings);Rebase();
        }
        public Vector3 Offset(Vector2 metres)=> (anchor+(right*metres.x+forward*metres.y)/fixture.definition.radius).normalized;
        float Distance(Vector3 a,Vector3 b)=>CubeSphere.ArcDistance(a,b,fixture.definition.radius);
        void Seed(string id,Vector2 offset,int team=0){var d=Catalog.Buildings[id];for(int ring=0;ring<8;ring++){var direction=TeamOffset(team,offset+new Vector2(0,-ring*15));if(Validate(d,direction,0,false,team).valid){Create(d,direction,0,true,team);return;}}throw new InvalidOperationException("No valid starter footprint for "+id);}
        public PlacementResult Validate(BuildingDefinition d,Vector3 direction,int rotation,bool requireAccess=true,int team=0)
        {
            direction=SnapBuilding(d,direction,rotation);var area=Grid.Area(d,direction,rotation);var Faction=Factions[team];
            if(Buildings.Count>=128)return new PlacementResult(false,"Building capacity reached");
            if(requireAccess&&Faction.tier<d.tier)return new PlacementResult(false,"Requires command tier "+d.tier);
            if(requireAccess&&!Faction.HasTech(d.requiredTech))return new PlacementResult(false,"Requires "+d.requiredTech);
            if(requireAccess&&!Faction.wallet.CanAfford(d.cost))return new PlacementResult(false,"Insufficient resources");
            float radius=Mathf.Sqrt(d.footprintWidth*d.footprintWidth+d.footprintDepth*d.footprintDepth)*4;
            if(!GridClear(area,out var gridReason))return new PlacementResult(false,gridReason);
            if(requireAccess&&!Buildings.Any(b=>b.team==team&&b.Complete&&b.definition.buildRadius>0&&Distance(direction,CubeSphere.Decode(b.position))<b.definition.buildRadius))return new PlacementResult(false,"Outside construction radius");
            float min=float.MaxValue,max=float.MinValue;bool land=false,water=false;
            for(int i=0;i<5;i++){var p=i==4?direction:Grid.Point(area,i&1,(i>>1)&1);float height=surface.SampleHeight(p);min=Mathf.Min(min,height);max=Mathf.Max(max,height);land|=height>=fixture.definition.oceanHeight;water|=height<fixture.definition.oceanHeight;}
            if(d.terrain==TerrainRule.Coast){if(!land||!water)return new PlacementResult(false,"Requires a coastline");}
            else if(water)return new PlacementResult(false,"Footprint must be on dry land");
            if(max-min>8)return new PlacementResult(false,"Footprint slope is too steep");
            if(d.terrain==TerrainRule.Node){foreach(var node in Nodes)if(node.claimedBy<0&&node.remaining>0&&Array.IndexOf(d.nodes,node.kind)>=0&&string.Equals(node.elementId??"",d.extractedResource??"",StringComparison.Ordinal)&&Distance(direction,CubeSphere.Decode(node.position))<12)return new PlacementResult(true,"",node.id);return new PlacementResult(false,"Requires a matching unclaimed resource deposit");}
            return new PlacementResult(true,"");
        }
        static float FootprintRadius(BuildingDefinition d)=>Mathf.Sqrt(d.footprintWidth*d.footprintWidth+d.footprintDepth*d.footprintDepth)*4;
        RtsBuilding Create(BuildingDefinition d,Vector3 direction,int rotation,bool complete=false,int team=0)
        {
            direction=SnapBuilding(d,direction,rotation);var b=new RtsBuilding{team=team,index=Buildings.Count,definition=d,position=CubeSphere.Encode(direction),rotation=rotation,health=d.health,construction=complete?d.time:0,rally=CubeSphere.Encode((direction+right*(50/fixture.definition.radius)).normalized)};
            b.visual=new GameObject((team==1?"Enemy ":"")+d.name);b.visual.transform.SetParent(root.transform,false);var model=art.Find(d.id)??art.Find(d.mesh);if(model==null)throw new InvalidOperationException("Missing current building model "+d.id);
            if(!d.id.StartsWith("civil_farm_"))AddMesh(b.visual.transform,model.near,model.material,"Current RTS shell");var collision=b.visual.AddComponent<BoxCollider>();collision.center=model.near.bounds.center;collision.size=Vector3.Max(model.near.bounds.size,Vector3.one*.5f);if(d.id.StartsWith("civil_farm_"))collision.enabled=false;if(model.gateLeaf!=null)AddMesh(b.visual.transform,model.gateLeaf,model.material,"Gate leaf");
            var scale=ModelScale(d);foreach(Transform child in b.visual.transform)child.localScale=scale;collision.center=Vector3.Scale(collision.center,scale);collision.size=Vector3.Scale(collision.size,scale);Buildings.Add(b);Roads?.RefreshBuildings();Revision++;return b;
        }
        void AddMesh(Transform parent,Mesh mesh,Material material,string name){var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.On;properties.Clear();properties.SetBuffer("_StructureAim",aim);properties.SetFloat("_HasTurret",0);properties.SetColor("_TeamColor",parent.name.StartsWith("Enemy ")?new Color(1,.12f,.04f):new Color(.02f,.72f,1));renderer.SetPropertyBlock(properties);}
        Vector3 ModelScale(BuildingDefinition definition){if(!definition.id.StartsWith("civil_")||!Catalog.Buildings.TryGetValue(definition.mesh,out var source))return Vector3.one;float x=definition.footprintWidth/(float)source.footprintWidth,z=definition.footprintDepth/(float)source.footprintDepth;return new Vector3(x,Mathf.Min(x,z),z);}
        public void BeginPlacement(BuildingDefinition definition){if(definition.id=="civil_road"){BeginRoadPlacement();return;}CancelRoadPlacement();CancelPlacement();Placing=definition;ghostDirection=fixture.Rig.FocusDirection;PlacementRotation=0;var model=art.Find(definition.id)??art.Find(definition.mesh);ghost=new GameObject("Planet building preview");ghost.transform.SetParent(root.transform,false);AddMesh(ghost.transform,model.near,ghostMaterial,"Footprint shell");foreach(Transform child in ghost.transform)child.localScale=ModelScale(definition);Notice="Place "+definition.name+" on the terrain Ã¢â‚¬Â¢ R rotate Ã¢â‚¬Â¢ right-click cancel";Revision++;}
        public void Rotate(){PlacementRotation=(PlacementRotation+1)&3;if(Placing!=null)ghostDirection=SnapBuilding(Placing,ghostDirection,PlacementRotation);Revision++;}
        public void CancelPlacement(){Placing=null;if(ghost!=null)constructionPoses.Remove(ghost.transform);ArmyLifetime.Destroy(ghost);ghost=null;Revision++;}
        public void Preview(Vector3 direction){if(ghost==null)return;direction=SnapBuilding(Placing,direction,PlacementRotation);ghostDirection=direction;PlaceTransform(ghost.transform,Placing,direction,PlacementRotation);var valid=Validate(Placing,direction,PlacementRotation);ghostMaterial.SetColor("_Tint",valid.valid?new Color(.05f,1,.3f,.35f):new Color(1,.08f,.04f,.35f));Notice=valid.valid?"Click to construct "+Placing.name:valid.reason;}
        public bool Construct(Vector3 direction){if(Placing==null)return false;direction=SnapBuilding(Placing,direction,PlacementRotation);var valid=Validate(Placing,direction,PlacementRotation);if(!valid.valid){Notice=valid.reason;return false;}
            var d=Placing;if(!fixture.IsFootprintClear(direction,FootprintRadius(d))){Notice="Units occupy this footprint";return false;}if(!Faction.wallet.Spend(d.cost))return false;var b=Create(d,direction,PlacementRotation);b.node=valid.node;if(b.node>=0)Nodes[b.node].claimedBy=b.index;Selected=b;CancelPlacement();fixture.RefreshBuildingCosts(Buildings);Rebase();Notice="Constructing "+d.name;return true;}
        public bool Pick(Vector2 pixel){var ray=fixture.View.ScreenPointToRay(pixel);var centre=fixture.PlanetRenderCentre;float best=float.MaxValue;RtsBuilding picked=null;
            foreach(var b in Buildings){if(b.removed)continue;var p=fixture.RenderBodyPosition(surface.GetSurfacePositionPrecise(CubeSphere.Decode(b.position)));float t=Vector3.Dot(p-ray.origin,ray.direction);if(t<0||t>=best)continue;float radius=FootprintRadius(b.definition);if((ray.GetPoint(t)-p).sqrMagnitude>radius*radius)continue;
                // Reject infrastructure on the opposite side of the planet.
                if(fixture.Rig.GroundPoint(pixel,out var ground)&&Distance(ground,CubeSphere.Decode(b.position))>radius+15)continue;best=t;picked=b;}
            if(picked==null)return false;if(picked.team!=0&&fixture.sessionMode!=PlanetGameMode.AiTest){Notice="Enemy "+picked.definition.name;return true;}Selected=picked;Revision++;Notice=picked.definition.name;return true;
        }
        public bool QueueUnit(RtsBuilding building,string id){if(Civilisation!=null&&!Civilisation.Logistics.Connected(this,building)){Notice="Production requires a warehouse connection or air cargo";return false;}var Faction=Factions[building.team];var d=Catalog.Units[id];if(!building.Complete||Array.IndexOf(building.definition.production,id)<0||!Faction.HasTech(d.requiredTech)||building.level<d.producerLevel||building.queue.Count>=16||!Faction.wallet.Spend(d.cost))return false;
            building.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time});Revision++;return true;}
        public bool Research(RtsBuilding building,string id){var Faction=Factions[building.team];var d=Catalog.Technologies[id];if(!building.Complete||d.requiredBuilding!=building.definition.id||building.level<d.requiredBuildingLevel||Faction.HasTech(id)||Faction.researching.Contains(id)||!d.prerequisites.All(Faction.HasTech)||!Faction.wallet.Spend(d.cost))return false;
            Faction.researching.Add(id);building.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time,research=true});Revision++;return true;}
        public void CancelQueue(RtsBuilding b,int index){if(index<0||index>=b.queue.Count)return;var Faction=Factions[b.team];var q=b.queue[index];Faction.wallet.Credit(q.paid*(1-q.Fraction));if(q.research)Faction.researching.Remove(q.id);b.queue.RemoveAt(index);Revision++;}
        public void Upgrade(RtsBuilding b){if(!b.Complete||b.Upgrading||b.level>=b.definition.maxLevel)return;var Faction=Factions[b.team];var cost=b.definition.cost*(.65f*b.level);if(!Faction.wallet.Spend(cost))return;b.upgradeTime=b.definition.time;b.upgradeProgress=0;Revision++;}
        public void Damage(RtsBuilding b,uint damage){if(b.removed)return;b.health=Mathf.Max(0,b.health-damage);Revision++;if(b.health>0)return;b.removed=true;b.visual.SetActive(false);Roads?.RefreshBuildings();if(b.node>=0)Nodes[b.node].claimedBy=-1;if(Selected==b)Selected=null;for(int i=b.queue.Count-1;i>=0;i--)CancelQueue(b,i);fixture.RefreshBuildingCosts(Buildings);Notice=b.definition.name+" destroyed";if(b.definition.id=="command"){if(fixture.sessionMode==PlanetGameMode.AiTest)Outcome="COMMAND CENTRE LOST Ã‚Â· SPECTATOR SIMULATION CONTINUES";else if(b.team==0){Outcome="DEFEAT Ã‚Â· COMMAND CENTRE DESTROYED";fixture.Paused=true;}else if(fixture.sessionMode==PlanetGameMode.Rts&&!Buildings.Any(other=>other.team==b.team&&!other.removed&&other.definition.id=="command")){Outcome="VICTORY - ENEMY COMMAND DESTROYED";fixture.Paused=true;}}}
        public void NotifyChanged()=>Revision++;
        public void CancelUpgrade(RtsBuilding b){if(!b.Upgrading)return;var Faction=Factions[b.team];Faction.wallet.Credit(b.definition.cost*(.65f*b.level)*(1-b.UpgradeFraction));b.upgradeTime=b.upgradeProgress=0;Revision++;}
        public void Sell(RtsBuilding b){if(b.removed)return;var Faction=Factions[b.team];Faction.wallet.Credit(b.definition.cost*(b.Complete?.45f:.8f));bool wasCommand=b.definition.id=="command";Damage(b,(uint)Mathf.CeilToInt(b.health));if(wasCommand)fixture.Paused=false;}
        public void Tick(float dt){
            for(int team=0;team<2;team++){var Faction=Factions[team];
            Faction.tier=Buildings.Where(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.id=="command").Select(b=>b.level).DefaultIfEmpty(1).Max();
            RefreshSettlementPower(team);
            Faction.efficiency=Faction.demand<=0?1:Mathf.Clamp(Faction.generation/Faction.demand,Civilisation==null?.25f:0,1);
            foreach(var b in Buildings){if(b.team!=team||b.removed)continue;if(!b.Complete){b.construction=Mathf.Min(b.definition.time,b.construction+dt*Mathf.Max(.35f,Faction.efficiency));if(b.Complete)Revision++;continue;}
                if(b.paused||!b.powered)continue;b.Efficiency=Faction.efficiency*b.FoodEfficiency;
                if(b.node>=0&&Civilisation==null){var node=Nodes[b.node];if(string.IsNullOrEmpty(node.elementId)){float extracted=Mathf.Min(node.remaining,b.definition.extraction*node.rateMultiplier*b.level*b.Efficiency*dt);node.remaining-=Faction.wallet.Deposit(node.resource,extracted);}}
                if(b.definition.researchRate>0)Faction.wallet.Deposit(ResourceKind.Research,b.definition.researchRate*b.Efficiency*dt);
                if(b.Upgrading){b.upgradeProgress+=dt*b.Efficiency;if(b.upgradeProgress>=b.upgradeTime){b.level++;if(b.definition.id=="command")Faction.tier=b.level;b.upgradeTime=0;b.health=b.MaxHealth;Revision++;}}
                if(b.repairing&&b.health<b.MaxHealth&&Faction.wallet.Spend(new ResourceCost{materials=dt}))b.health=Mathf.Min(b.MaxHealth,b.health+25*dt);
                if(b.queue.Count==0)continue;var q=b.queue[0];q.progress=Mathf.Min(q.duration,q.progress+dt*b.Efficiency);if(q.progress<q.duration)continue;
                bool done;if(q.research){Faction.technologies.Add(q.id);Faction.researching.Remove(q.id);done=true;}else done=SpawnNear(b,Catalog.Units[q.id]);
                if(done){b.queue.RemoveAt(0);if(b.repeat&&!q.research)QueueUnit(b,q.id);Revision++;}else Notice="Production waiting for a clear compatible exit or free army capacity";
            }
            }
            ThinkPlanetAI(dt);
        }
        bool SpawnNear(RtsBuilding b,UnitDefinition d){var direction=CubeSphere.Decode(b.position);var tangent=Vector3.Cross(Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up,direction).normalized;var north=Vector3.Cross(direction,tangent);
            for(int ring=0;ring<6;ring++)for(int j=0;j<16;j++){float angle=j*Mathf.PI/8;var candidate=(direction+(tangent*Mathf.Cos(angle)+north*Mathf.Sin(angle))*(FootprintRadius(b.definition)+12+ring*12)/fixture.definition.radius).normalized;
                if(Buildings.Any(other=>Distance(candidate,CubeSphere.Decode(other.position))<FootprintRadius(other.definition)+d.radius+4))continue;if(fixture.TrySpawnUnit(Catalog.TypeIndex(d.id),candidate,b.team))return true;}return false;}
        void PlaceTransform(Transform transform,BuildingDefinition d,Vector3 direction,int rotation)=>PlaceConstruction(transform,d,direction,rotation);
        public void Rebase(){foreach(var b in Buildings){PlaceTransform(b.visual.transform,b.definition,CubeSphere.Decode(b.position),b.rotation);b.visual.transform.localScale=Vector3.one*(b.Complete?1:Mathf.Lerp(.1f,1,b.construction/b.definition.time));}
            for(int i=0;i<markers.Count;i++){var up=CubeSphere.Decode(Nodes[i].position);markers[i].transform.SetPositionAndRotation(fixture.RenderBodyPosition(surface.GetSurfacePositionPrecise(up)),Quaternion.FromToRotation(Vector3.up,up));}if(ghost!=null)PlaceTransform(ghost.transform,Placing,ghostDirection,PlacementRotation);}
        public void Dispose(){CancelPlacement();ArmyLifetime.Destroy(root);ArmyLifetime.Destroy(nodeMaterial);ArmyLifetime.Destroy(ghostMaterial);aim.Dispose();}
    }
}









