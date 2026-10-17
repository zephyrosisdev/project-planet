using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace MassiveArmy
{
    public sealed class RtsBuilding
    {
        public int index,team,level=1,node=-1,rallyUnit=-1,rotation,wallConnections=-1;
        public long persistentId;
        public BuildingDefinition definition;
        public Vector2 position,rally;
        public float health,construction,upgradeProgress,upgradeTime;
        public bool powered=true,paused,repeat,repairing,gateOpen,holdFire,removed,gateTraversable;
        public TargetPriority priority;
        public readonly List<QueueOrder> queue=new List<QueueOrder>();
        public readonly HashSet<QueueOrder> awaitingSpawn=new HashSet<QueueOrder>();
        public readonly List<string> repeatWaiting=new List<string>();
        public GameObject visual;
        public float MaxHealth => definition.health*(1+(level-1)*.4f);
        public float Armour => definition.armour*level;
        public bool Complete => !removed&&construction>=definition.time;
        public int Bays => definition.category==BuildingCategory.Production?level:1;
        public bool Upgrading => upgradeTime>0;
        public float UpgradeFraction => upgradeTime>0?upgradeProgress/upgradeTime:0;
        public float Efficiency=1;
        public float FoodEfficiency=1;
        public float WorkforceEfficiency=1;
        public int WorkersAssigned;
    }
    public sealed class RtsFaction
    {
        public int team;
        public ResourceWallet wallet=new ResourceWallet();
        public readonly HashSet<string> technologies=new HashSet<string>(),researching=new HashSet<string>();
        public float generation,demand,battery,energyCapacity,efficiency=1;
        public int tier=1;
        public bool HasTech(string id) => string.IsNullOrEmpty(id)||technologies.Contains(id);
        public StrategicProfile profile;
        public readonly List<Vector2> knownEnemyBases=new List<Vector2>();
    }
    public readonly struct PlacementResult
    {
        public readonly bool valid,disadvantaged;
        public readonly string reason;
        public readonly int node;
        public PlacementResult(bool valid,string reason,int node=-1,bool disadvantaged=false) {this.valid=valid;this.reason=reason;this.node=node;this.disadvantaged=disadvantaged;}
    }
    /// <summary>CPU base simulation. There is deliberately no CPU list of soldiers or vehicles.</summary>
    public sealed partial class RtsWorld : IDisposable
    {
        public readonly RtsCatalog Catalog;
        public readonly GpuBattle Battle;
        public readonly Battlefield Field;
        public readonly List<RtsBuilding> Buildings=new List<RtsBuilding>();
        public readonly List<ResourceNode> Nodes=new List<ResourceNode>();
        public readonly RtsFaction[] Factions={new RtsFaction{team=0},new RtsFaction{team=1}};
        public readonly int[] SectorOwner=Enumerable.Repeat(-1,16).ToArray();
        public readonly float[] CaptureProgress=new float[16];
        readonly GpuStructure[] gpuBuildings=new GpuStructure[GpuBattle.MaxBuildings];
        readonly float[] obstacleCosts;
        readonly RtsPresentation presentation;
        readonly System.Random random;
        uint[] sectorCounts;
        float tick,aiClock,matchTime;
        bool disposed;
        public string Notice="Select your Command Centre to begin construction.",Outcome="";
        public bool EnemyEnabled=true;
        public float TimeElapsed => matchTime;
        public event Action<RtsBuilding> Changed;
        public RtsWorld(GpuBattle battle,Transform parent,RtsCatalog catalog)
        {
            Battle=battle;Field=battle.Field;Catalog=catalog;random=new System.Random(battle.Settings.seed^0x51a7);
            obstacleCosts=new float[Field.Deployment.Costs.Length*Field.CostTeams];for(int team=0;team<Field.CostTeams;team++)Array.Copy(Field.Deployment.Costs,0,obstacleCosts,team*Field.Deployment.Costs.Length,Field.Deployment.Costs.Length);
            presentation=new RtsPresentation(this,parent);Field.TerrainChanged+=RefreshCraterCosts;
            foreach(var f in Factions) {f.wallet.Capacity=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};f.wallet.Stock=new ResourceCost{materials=1800,fuel=350,rareElements=160,research=100};}
            Factions[1].profile=(StrategicProfile)(unchecked((uint)battle.Settings.seed)%8);
            if(battle.Settings.spectatorMode){Factions[0].profile=(StrategicProfile)Mathf.Clamp(battle.Settings.aiCyanProfile,0,7);Factions[1].profile=(StrategicProfile)Mathf.Clamp(battle.Settings.aiRedProfile,0,7);Notice="SPECTATOR TEST · CYAN AI vs RED AI vs zombies";}
            var prepared=battle.Settings.preview??WorldSetupResult.FromMap(Field.Map,Field.Deployment,catalog:Catalog);
            Vector2 a=prepared.Starts[0],b=prepared.Starts[1];
            SeedBuilding("command",0,a);if(!battle.Settings.swarmMode)SeedBuilding("command",1,b);else EnemyEnabled=false;
            foreach(var source in prepared.Resources)Nodes.Add(new ResourceNode{id=source.id,kind=source.kind,resource=source.resource,position=source.position,capacity=source.capacity,remaining=source.capacity,quality=source.quality,rateMultiplier=source.rateMultiplier});presentation.CreateNodes();
            Battle.ConfigureRoster(catalog.units);Battle.OnStructureDamage=ReceiveDamage;Battle.OnSectorControl=data=>sectorCounts=data;
            foreach(var building in Buildings)SectorOwner[Sector(building.position)]=building.team;
            foreach(int team in battle.Settings.swarmMode?new[]{0}:new[]{0,1})for(int j=0;j<4;j++) {
                Vector2 p=Field.TraversableDestination(Buildings[team].position+new Vector2(40+j*6,0));
                Battle.RequestSpawn(Catalog.TypeIndex(j==0?"engineer":j==1?"scout":"infantry"),team,p,p,null);
            }
            Upload();battle.ConfigureZombies(this);
        }
        public Vector2 BasePosition(int team) => Buildings.FirstOrDefault(b=>!b.removed&&b.team==team&&b.definition.id=="command")?.position??Vector2.zero;
        public void Rebase(Camera camera)=>presentation.Rebase(camera);
        internal int VisibleResourceDecals=>presentation.VisibleResourceDecals;
        internal bool ResourceDecalVisible(int id)=>presentation.ResourceDecalVisible(id);
        public int Sector(Vector2 p) {int x=WorldTopology.Cell(Mathf.FloorToInt((p.x/Field.Size+.5f)*4),4),y=WorldTopology.Cell(Mathf.FloorToInt((p.y/Field.Size+.5f)*4),4);return x+y*4;}
        public Vector2 SectorPosition(int s) => new Vector2((s%4+.5f)/4-.5f,(s/4+.5f)/4-.5f)*Field.Size;
        public Vector2 FindTerrain(Vector2 desired,float radius,bool avoidNodes=false)
        {
            for(int ring=0;ring<100;ring++)for(int k=0;k<Mathf.Max(1,ring*8);k++) {
                float angle=k*Mathf.PI*2/Mathf.Max(1,ring*8);Vector2 p=desired+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*ring*9;
                if(TerrainValid(p,radius,TerrainRule.Land)&&Buildings.All(b=>b.removed||Field.Distance(p,b.position)>radius+b.definition.radius+4)&&(!avoidNodes||Nodes.All(n=>Field.Distance(p,n.position)>radius+n.radius+6)))return Field.Canonical(p);
            }
            // Sea sectors on large maps may have no land within the local search radius.
            // Search immutable map metadata before rejecting the entire match.
            float best=float.MaxValue;Vector2 nearest=Vector2.zero;bool found=false;
            for(int i=0;i<Field.Map.Height.Length;i++){
                Vector2 p=Field.Map.Position(i);float distance=Field.Delta(desired,p).sqrMagnitude;if(distance>=best)continue;
                if(!TerrainValid(p,radius,TerrainRule.Land)||Buildings.Any(b=>!b.removed&&Field.Distance(p,b.position)<=radius+b.definition.radius+4)||(avoidNodes&&Nodes.Any(n=>Field.Distance(p,n.position)<=radius+n.radius+6)))continue;
                best=distance;nearest=p;found=true;
            }
            if(found)return nearest;
            throw new InvalidOperationException("No stable building foundation near "+desired);
        }
        bool TerrainValid(Vector2 p,float radius,TerrainRule rule)
        {
            if(radius*2>=Field.Size)return false;
            float min=float.MaxValue,max=float.MinValue;
            for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++) {
                Vector2 q=p+new Vector2(x,y)*radius*.75f;int i=Field.Map.Index(q);float h=Field.Height(q);min=Mathf.Min(min,h);max=Mathf.Max(max,h);
                if(Field.Map.WaterData(i).g>.8f||Field.Map.Slope[i]>.65f||Field.Map.RockObstacle[i])return false;
                if(Field.Deployment.Costs[2*Field.Resolution*Field.Resolution+i]>=DeploymentMap.Blocked*.5f)return false;
            }
            if(max-min>radius*.9f)return false;
            return rule!=TerrainRule.Coast||WaterExit(p,radius,out _);
        }
        public bool WaterExit(Vector2 p,float radius,out Vector2 exit)
        {
            for(int ring=1;ring<=6;ring++)for(int k=0;k<16;k++) {
                float angle=k*Mathf.PI/8;Vector2 q=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(radius+ring*8);
                if(Field.Deployment.Costs[4*Field.Resolution*Field.Resolution+Field.Map.Index(q)]<DeploymentMap.Blocked*.5f){exit=q;return true;}
            }
            exit=p;return false;
        }
        public PlacementResult ValidatePlacement(BuildingDefinition d,int team,Vector2 p,bool ignoreKnowledge=false,IReadOnlyList<Vector2> chain=null,int rotation=0)
        {
            var valid=ValidateGridPlacement(d,team,p,ignoreKnowledge,rotation);
            if(valid.valid&&chain!=null&&chain.Any(q=>BuildingGrid.Anchor(d,q,Field.Size,rotation)==BuildingGrid.Anchor(d,p,Field.Size,rotation)))return new PlacementResult(false,"Wall segment overlap");
            return valid;
        }
        RtsBuilding SeedBuilding(string id,int team,Vector2 p) {
            var d=Catalog.Buildings[id];var b=Create(d,team,FindSeedFoundation(d,p),-1);b.construction=d.time;b.health=b.MaxHealth;return b;
        }
        RtsBuilding Create(BuildingDefinition d,int team,Vector2 p,int node,int rotation=0)
        {
            p=SnapPlacement(d,p,rotation);int slot=Buildings.FindIndex(b=>b.removed);
            var b=new RtsBuilding{index=slot>=0?slot:Buildings.Count,team=team,definition=d,rotation=rotation&3,position=p,rally=Field.TraversableDestination(p+BuildingGrid.Forward(rotation)*45),health=d.health*.1f,node=node};
            if(slot>=0)Buildings[slot]=b;else Buildings.Add(b);if(node>=0)Nodes[node].claimedBy=b.index;
            RegisterFootprint(b,true);presentation.CreateBuilding(b);SetObstacle(b,true);RefreshWallConnections(b.position);Changed?.Invoke(b);return b;
        }
        public RtsBuilding Construct(string id,int team,Vector2 p,bool ai=false,int rotation=0)
        {
            var d=Catalog.Buildings[id];p=SnapPlacement(d,p,rotation);var valid=ValidatePlacement(d,team,p,ai,rotation:rotation);Notice=valid.reason;if(!valid.valid)return null;
            var replaced=new List<RtsBuilding>(3);if(id=="gate")GateReplacement(p,team,rotation,replaced,out _);
            if(!Factions[team].wallet.Spend(PlacementCost(d,p,team,rotation)))return null;
            BeginObstacleBatch();try{
                foreach(var wall in replaced)Remove(wall,false);
                return Create(d,team,p,valid.node,rotation);
            }finally{EndObstacleBatch();}
        }
        public bool EnqueueUnit(RtsBuilding b,string id)
        {
            if(!CanProduce(b,id,out var reason)){Notice=reason;return false;}var d=Catalog.Units[id];
            if(!Factions[b.team].wallet.Spend(d.cost)){Notice="Insufficient resources";return false;}
            b.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time});return true;
        }
        public bool CanProduce(RtsBuilding b,string id,out string reason)
        {
            reason="";var d=Catalog.Units[id];
            if(!b.Complete||b.Upgrading)reason="Facility is under construction / upgrading";
            else if(!b.definition.production.Contains(id))reason="Wrong producer";
            else if(b.level<d.producerLevel)reason="Requires facility level "+d.producerLevel;
            else if(!Factions[b.team].HasTech(d.requiredTech))reason="Research "+d.requiredTech;
            else if(b.queue.Count>=64)reason="Queue is full";
            return reason.Length==0;
        }
        public bool CanResearch(RtsBuilding b,TechnologyDefinition d,out string reason)
        {
            var f=Factions[b.team];reason="";
            int rank=b.definition.id=="lab"?1:b.definition.id=="advanced_lab"?2:b.definition.id=="strategic_lab"?3:0;
            int required=d.requiredBuilding=="lab"?1:d.requiredBuilding=="advanced_lab"?2:3;
            if(!b.Complete||b.Upgrading)reason="Facility unavailable";
            else if(rank<required||b.level<d.requiredBuildingLevel)reason="Requires "+Catalog.Buildings[d.requiredBuilding].name+" level "+d.requiredBuildingLevel;
            else if(d.tier>f.tier)reason="Upgrade Command Centre to tier "+d.tier;
            else if(f.technologies.Contains(d.id))reason="Completed";
            else if(f.researching.Contains(d.id))reason="Already queued";
            else if(d.prerequisites.Any(p=>!f.HasTech(p)))reason="Requires "+string.Join(", ",d.prerequisites.Where(p=>!f.HasTech(p)));
            else if(b.queue.Count>=64)reason="Queue is full";
            return reason.Length==0;
        }
        public bool EnqueueResearch(RtsBuilding b,string id)
        {
            var d=Catalog.Technologies[id];if(!CanResearch(b,d,out var reason)){Notice=reason;return false;}
            var f=Factions[b.team];if(!f.wallet.Spend(d.cost)){Notice="Insufficient research/resources";return false;}
            f.researching.Add(id);b.queue.Add(new QueueOrder{id=id,paid=d.cost,duration=d.time,research=true});return true;
        }
        public void Cancel(RtsBuilding b,int index)
        {
            if(index<0||index>=b.queue.Count)return;var q=b.queue[index];if(b.awaitingSpawn.Contains(q)){Notice="GPU spawn is committing";return;}
            Factions[b.team].wallet.Credit(q.paid*(1-q.Fraction));if(q.research)Factions[b.team].researching.Remove(q.id);b.queue.RemoveAt(index);
        }
        public void Reorder(RtsBuilding b,int from,int to)
        {
            if(from<0||from>=b.queue.Count||to<0||to>=b.queue.Count)return;if(b.awaitingSpawn.Contains(b.queue[from])||b.awaitingSpawn.Contains(b.queue[to]))return;
            var q=b.queue[from];b.queue.RemoveAt(from);b.queue.Insert(to,q);
        }
        public ResourceCost UpgradeCost(RtsBuilding b) => b.definition.cost*(.65f*b.level);
        public bool Upgrade(RtsBuilding b)
        {
            var f=Factions[b.team];int required=b.definition.id=="command"?b.level:Mathf.Min(3,b.level+1);
            if(!b.Complete||b.Upgrading||b.level>=b.definition.maxLevel||f.tier<required){Notice="Upgrade requires command tier "+required+" or maximum level reached";return false;}
            if(!f.wallet.Spend(UpgradeCost(b))){Notice="Insufficient resources";return false;}b.upgradeTime=15+b.level*10;b.upgradeProgress=0;return true;
        }
        public void CancelUpgrade(RtsBuilding b)
        {
            if(!b.Upgrading)return;Factions[b.team].wallet.Credit(UpgradeCost(b)*(1-b.UpgradeFraction));b.upgradeTime=b.upgradeProgress=0;
        }
        public void Sell(RtsBuilding b) {if(b.removed)return;if(b.awaitingSpawn.Count>0){Notice="Wait for committed GPU production before decommissioning";return;}Factions[b.team].wallet.Credit(b.definition.cost*(b.Complete?.45f:.8f));Remove(b,false);}
        public void Dispose() {disposed=true;Field.TerrainChanged-=RefreshCraterCosts;Battle.OnStructureDamage=null;Battle.OnSectorControl=null;presentation.Dispose();}
    }
}
