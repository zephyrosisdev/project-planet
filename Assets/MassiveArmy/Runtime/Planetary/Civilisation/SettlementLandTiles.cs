using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public enum SettlementLandKind {Park,Farm}
    [Serializable]public sealed class SettlementLandTile {
        public long id,settlementId;public int faction;public PlanetNavCoordinate cell;public SettlementLandKind kind;public string crop="wheat";
        public SurfaceInstallation field=new SurfaceInstallation();public double lastTick,harvestAt=-1;
    }
    public sealed class SettlementLandUse {
        readonly PlanetRtsBase local;readonly PlanetState state;readonly Dictionary<long,SettlementLandTile> tiles=new Dictionary<long,SettlementLandTile>();
        readonly Dictionary<int,int> parkCounts=new Dictionary<int,int>();
        readonly Dictionary<(int,string),int> crops=new Dictionary<(int,string),int>();public readonly List<SettlementLandTile> Parks=new List<SettlementLandTile>();int cursor,planTurn;
        public IReadOnlyList<SettlementLandTile> Tiles=>state.landTiles;public int Revision=>state.landRevision;
        readonly Dictionary<int,SettlementLandTile> harvesters=new Dictionary<int,SettlementLandTile>();readonly Dictionary<int,double> nextHarvester=new Dictionary<int,double>();
        internal SettlementLandUse(PlanetRtsBase local){this.local=local;state=local.Civilisation.Planet(local.World.definition.bodyId);state.landTiles??=new List<SettlementLandTile>();foreach(var t in state.landTiles){Index(t);if(t.harvestAt>=0){if(!harvesters.ContainsKey(t.faction)&&harvesters.Count<local.Civilisation.Settings.maximumHarvesters)harvesters[t.faction]=t;else t.harvestAt=-1;}}}
        internal static long Key(PlanetNavCoordinate c)=>((long)(int)c.face<<48)|((long)c.x<<24)|(uint)c.y;
        void Index(SettlementLandTile tile){tiles[Key(tile.cell)]=tile;if(tile.kind==SettlementLandKind.Park){Parks.Add(tile);parkCounts.TryGetValue(tile.faction,out int n);parkCounts[tile.faction]=n+1;}else{var key=(tile.faction,tile.crop);crops.TryGetValue(key,out int n);crops[key]=n+1;}}
        public bool Occupied(PlanetNavCoordinate cell)=>tiles.ContainsKey(Key(cell));
        public SettlementLandTile Find(PlanetNavCoordinate cell)=>tiles.TryGetValue(Key(cell),out var tile)?tile:null;
        public int ParkCount(int team)=>parkCounts.TryGetValue(team,out int n)?n:0;
        public int CropCount(int team,string crop)=>crops.TryGetValue((team,crop),out int count)?count:0;
        public bool Place(Vector3 first,Vector3 last,int team,SettlementLandKind kind,string crop,out string reason,bool free=false){
            if(team<0||team>=local.Factions.Length){reason="Unknown settlement owner";return false;}if(kind==SettlementLandKind.Farm&&(!local.Catalog.Buildings.TryGetValue("civil_farm_"+crop,out var profile)||!local.Factions[team].HasTech(profile.requiredTech)||local.Factions[team].tier<profile.tier)){reason="This crop is not unlocked";return false;}
            var a=CubeSphere.Cell(local.World.definition.bodyId,first,local.Grid.Resolution);var b=CubeSphere.Cell(local.World.definition.bodyId,last,local.Grid.Resolution);
            if(a.face!=b.face){reason="Place each area on one planet grid face";return false;}int x=Math.Min(a.x,b.x),y=Math.Min(a.y,b.y),w=Math.Abs(a.x-b.x)+1,h=Math.Abs(a.y-b.y)+1;
            if((long)w*h>256){reason="Paint at most 256 tiles at a time";return false;}var additions=new List<PlanetNavCoordinate>();var surface=new PlanetSurface(local.World.definition);
            for(int v=0;v<h;v++)for(int u=0;u<w;u++){var cell=new PlanetNavCoordinate(local.World.definition.bodyId,a.face,x+u,y+v);if(tiles.TryGetValue(Key(cell),out var existing)){if(existing.faction==team&&existing.kind==kind&&existing.crop==crop)continue;reason="Another land use already occupies that tile";return false;}
                var d=local.Grid.Direction(cell);if(local.World.PlayerBuildCellReserved(cell)||local.TerritoryOwner(d)!=team||surface.SampleHeight(d)<=local.World.definition.oceanHeight+.2||local.Roads.HasRoad(cell)||local.Rails.HasRoad(cell)||!local.Roads.FootprintCellClear(cell)){reason="Use dry, clear land in your territory, beside roads";return false;}if(kind==SettlementLandKind.Farm&&!local.DistrictClear(local.Catalog.Buildings["civil_farm_"+crop],d,team)){reason="Keep fields away from industrial zones";return false;}additions.Add(cell);}
            if(Tiles.Count+additions.Count>local.Civilisation.Settings.maximumLandTiles){reason="Land tile limit reached";return false;}
            if(!free&&!local.FreePrebuild&&!local.Factions[team].wallet.Spend(new ResourceCost{materials=additions.Count*local.Civilisation.Settings.landTileMaterialCost})){reason="Insufficient construction materials";return false;}
            foreach(var cell in additions)Add(cell,team,kind,crop);reason=additions.Count+" "+(kind==SettlementLandKind.Park?"park":"farm")+" tiles painted";return additions.Count>0;
        }
        internal void Add(PlanetNavCoordinate cell,int team,SettlementLandKind kind,string crop,double growth=0){if(Occupied(cell))return;var sim=local.Civilisation;var tile=new SettlementLandTile{id=sim.State.AllocateId(),settlementId=local.PlanningSettlement?.id??0,cell=cell,faction=team,kind=kind,crop=crop,lastTick=sim.SimulationTime};tile.field.crop=crop;tile.field.growth=growth;tile.field.planted=growth>0;tile.field.definition="civil_farm_"+crop;tile.field.position=CubeSphere.Encode(local.Grid.Direction(cell));state.landTiles.Add(tile);Index(tile);state.landRevision++;}
        internal bool Plan(int team,string crop,bool park=false){
            SettlementLandTile anchor=null;for(int i=Tiles.Count-1;i>=Math.Max(0,Tiles.Count-128);i--)if((local.PlanningSettlement==null||Tiles[i].settlementId==local.PlanningSettlement.id)&&Tiles[i].faction==team&&Tiles[i].kind==(park?SettlementLandKind.Park:SettlementLandKind.Farm)){anchor=Tiles[i];break;}
            var hq=park?local.Buildings.Find(b=>b.team==team&&!b.removed&&b.Complete&&b.definition.housingCapacity>0):null;hq??=local.Buildings.Find(b=>b.team==team&&!b.removed&&b.definition.id=="command");if(local.PlanningSettlement!=null)hq=local.FindBuilding(local.PlanningSettlement.hub);if(hq==null)return false;
            var cell=anchor?.cell??CubeSphere.Cell(local.World.definition.bodyId,CubeSphere.Decode(hq.position),local.Grid.Resolution);uint seed=SettlementDailySchedule.Hash((ulong)(state.landRevision+team*397+planTurn++*31));
            for(int attempt=0;attempt<8;attempt++){int x=cell.x,y=cell.y;if(anchor!=null){int side=(int)((seed+(uint)attempt)%4);x+=side==0?-1:side==1?1:0;y+=side==2?-1:side==3?1:0;}else{x-=(park?3:10)+(int)((seed+(uint)attempt*7)%(park?3u:12u));y+=(park?3:10)+(int)((seed/13+(uint)attempt*3)%(park?3u:12u));}if(x<0||y<0||x>=local.Grid.Resolution||y>=local.Grid.Resolution)continue;var d=local.Grid.Direction(new PlanetNavCoordinate(local.World.definition.bodyId,cell.face,x,y));if(Place(d,d,team,park?SettlementLandKind.Park:SettlementLandKind.Farm,crop,out _))return true;}return false;
        }
        internal void Tick(ColonyState colony){
            var sim=local.Civilisation;int count=Tiles.Count;double now=sim.SimulationTime;bool finished=false;
            if(harvesters.TryGetValue(colony.faction,out var active)){
                Advance(active,now>=active.harvestAt+sim.Settings.harvestSweepSeconds);
                if(active.field.growth<1){active.harvestAt=-1;harvesters.Remove(colony.faction);nextHarvester[colony.faction]=now+1;finished=true;}
            }
            // A completed tractor sweep harvests the ripe fields in this bounded
            // page, not just one tile per 20 seconds for the entire country.
            for(int i=0;i<Math.Min(64,count);i++){var tile=state.landTiles[(cursor+i)%Math.Max(1,count)];if(tile.faction!=colony.faction||tile.kind!=SettlementLandKind.Farm||tile==active)continue;Advance(tile,finished);
                if(tile.field.growth>=1&&!finished&&!harvesters.ContainsKey(colony.faction)&&harvesters.Count<sim.Settings.maximumHarvesters&&(!nextHarvester.TryGetValue(colony.faction,out double next)||now>=next)){tile.harvestAt=now;harvesters[colony.faction]=tile;}
            }
            void Advance(SettlementLandTile tile,bool harvest){double elapsed=Math.Max(0,now-tile.lastTick);tile.lastTick=now;tile.field.inventory.Bind(sim.Catalogue);tile.field.inventory.Share(colony.inventory);sim.GrowCrop(tile.field,state,elapsed,harvest);}
            // Every colony sees the same page before moving it on the last faction.
            if(colony.faction==local.Settlements.Length-1||colony==LastColony())cursor=count==0?0:(cursor+64)%count;
        }
        ColonyState LastColony(){for(int i=local.Settlements.Length-1;i>=0;i--)if(local.Settlements[i]!=null)return local.Settlements[i];return null;}
        internal void Migrate(ColonyState c){bool changed=false;
            foreach(var b in local.Buildings)if(b.team==c.faction&&!b.removed&&b.definition.id.StartsWith("civil_farm_")){var item=SettlementLogistics.Find(c,b.persistentId);if(item==null)continue;
                if(!item.fieldConverted){var area=local.Area(b);if(Tiles.Count+area.width*area.depth>local.Civilisation.Settings.maximumLandTiles)continue;for(int y=0;y<area.depth;y++)for(int x=0;x<area.width;x++)Add(new PlanetNavCoordinate(local.World.definition.bodyId,area.face,area.x+x,area.y+y),c.faction,SettlementLandKind.Farm,b.definition.id.Substring(11),item.growth);item.fieldConverted=true;}
                local.RetireFarmAnchor(b);changed=true;
            }
            if(changed)local.RefreshAfterFarmMigration();
        }

    }
    public sealed partial class PlanetRtsBase {
        internal void RetireFarmAnchor(RtsBuilding b){b.removed=true;b.paused=true;b.powered=false;if(b.visual!=null)b.visual.SetActive(false);if(Selected==b)Selected=null;}
        internal void RefreshAfterFarmMigration(){InvalidatePlacementOccupancy();Roads?.RefreshBuildings();Rails?.RefreshBuildings();fixture.RefreshBuildingCosts(Buildings,true);NotifyChanged();}
        public SettlementLandTile SelectedLandTile {get;private set;}
        public void SelectLandTile(SettlementLandTile tile){ClearSelection();SelectedLandTile=tile;Revision++;}
        bool PickLand(Vector2 pixel){if(landUse==null||!fixture.Rig.GroundPoint(pixel,out var ground))return false;var tile=landUse.Find(Grid.Cell(ground));if(tile==null)return false;SelectLandTile(tile);Notice=tile.kind==SettlementLandKind.Park?"Park":"Farm field · "+tile.crop;return true;}
        SettlementLandUse landUse;public SettlementLandUse LandUse=>landUse??=new SettlementLandUse(this);
        internal bool LandCellOccupied(PlanetNavCoordinate cell)=>landUse!=null&&landUse.Occupied(cell);
        bool landPlacing;SettlementLandKind landKind;string landCrop;
        public void BeginLandPlacement(SettlementLandKind kind,string crop="wheat"){BeginRoadPlacement();landPlacing=true;landKind=kind;landCrop=crop;Notice="Paint "+kind+": click opposite corners (up to 256 tiles). Right-click cancels.";}
    }
    public sealed partial class CivilisationRuntime {internal void TickLandTiles(PlanetRtsBase local,ColonyState c){if(!Settings.ambientSettlementLife)return;local.LandUse.Migrate(c);local.LandUse.Tick(c);}}
}
