using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsBase {
        internal bool FrontierRetainedForValidation(){var frontier=new PlanningFrontier();var exterior=new RtsBuilding[6];for(int face=0;face<6;face++){exterior[face]=new RtsBuilding();frontier.Add(exterior[face],new PlanetConstructionGrid.Footprint((CubeFace)face,2000,2000,2,2,0));}
            for(int i=0;i<1200;i++)frontier.Add(new RtsBuilding(),new PlanetConstructionGrid.Footprint((CubeFace)(i%6),100+i%31,100+(i/31)%31,2,2,0));
            for(int face=0;face<6;face++){bool found=false;for(int i=0;i<8;i++)found|=frontier.GetEdge(face*8+i)==exterior[face];if(!found)return false;}for(int i=0;i<frontier.count;i++)foreach(var old in exterior)if(frontier.Get(i)==old)return false;return frontier.count==128;
        }
    }

    public sealed partial class SettlementHouseholdValidation {
        void ValidateFailedDispatchReuse(){
            var local=world.Base;var colony=local.Settlements[0];var logistics=local.Civilisation.Logistics;
            var warehouse=colony.installations.Find(i=>i.definition=="civil_warehouse");var vehicles=colony.cargoVehicles;var orders=colony.cargoOrders;
            Check(warehouse!=null&&!local.Buildings.Exists(b=>b.definition.id=="civil_freight_station"),"Dispatch fixture has storage but no rail freight terminals");
            var test=new System.Collections.Generic.List<CargoVehicleState>();
            CargoVehicleState Idle(int tier)=>new CargoVehicleState{id=-7000-test.Count,warehouse=warehouse.id,freight=true,train=true,tier=tier};
            test.Add(Idle(1));test.Add(Idle(1));
            // A live mission conservatively invalidates failed searches even before arrival.
            var active=new CargoVehicleState{id=-8000,warehouse=warehouse.id,source=warehouse.id,destination=warehouse.id,resource="food",phase=CargoVehiclePhase.Returning,arrival=double.MaxValue};test.Add(active);
            test.Add(Idle(1));test.Add(Idle(1));test.Add(Idle(2));test.Add(Idle(2));
            long before=logistics.ReusedDispatchFailures;
            try{colony.cargoVehicles=test;colony.cargoOrders=new System.Collections.Generic.List<CargoVehicleOrder>();logistics.Tick(local,colony,0);
                Check(logistics.ReusedDispatchFailures-before==3,"Failed searches are shared only between identical idle carriers and reset around active missions and different capacities");
                Check(test.TrueForAll(v=>v==active?v.phase==CargoVehiclePhase.Returning:v.phase==CargoVehiclePhase.Idle),"Failure reuse does not invent freight jobs without terminals");
                before=logistics.ReusedDispatchFailures;logistics.Tick(local,colony,0);Check(logistics.ReusedDispatchFailures-before==3,"Failed dispatch results expire at the next economy tick");
            }finally{colony.cargoVehicles=vehicles;colony.cargoOrders=orders;}
        }
        void ValidateQueuedGoals(){var navigation=world.NavigationForValidation;Check(navigation.Regional!=null,"Queued goal test exercises regional navigation");var expected=new GroupOrder[1];var actual=new GroupOrder[1];
            foreach(MovementClass movement in System.Enum.GetValues(typeof(MovementClass)))for(int face=0;face<6;face++){
                var point=CubeSphere.Direction((CubeFace)face,new Vector2(.37f,-.24f));navigation.SetGoal(14,point,movement,true);navigation.SetGoalQueued(15,point,movement);
                navigation.Groups.GetData(expected,0,14,1);navigation.Groups.GetData(actual,0,15,1);
                Check((expected[0].destination-actual[0].destination).sqrMagnitude<1e-12f&&(expected[0].localOrigin-actual[0].localOrigin).sqrMagnitude<1e-12f&&expected[0].movement==actual[0].movement,"Queued GPU goal agrees with synchronous resolution for "+movement+" on "+face);
                // A different group's CPU update must not overwrite GPU-resolved origins.
                navigation.SetGoal(13,-point,movement,true);navigation.Groups.GetData(actual,0,15,1);Check((expected[0].localOrigin-actual[0].localOrigin).sqrMagnitude<1e-12f,"Independent group update preserves queued origin");
            }
        }
        void ValidateFrontierRetention(){Check(world.Base.FrontierRetainedForValidation(),"Planner retains old exterior support points after more than 1,024 interior placements on every cube face");}
        void ValidatePlacementCaches(){
            for(int mask=0;mask<16;mask++)for(int edge=0;edge<4;edge++)Check(SettlementRoadNetwork.CrossingEdgeAllowed(mask,edge)==(mask==3&&edge>=2||mask==12&&edge<2),"Rail/road crossing excludes parallel edges, bends, ends and junctions: "+mask+"/"+edge);
            var local=world.Base;var b=local.Buildings[0];var position=b.position;int rotation=b.rotation,width=b.definition.footprintWidth;
            void Verify(string label){var expected=local.Grid.Area(b.definition,CubeSphere.Decode(b.position),b.rotation);var actual=local.Area(b);Check(actual.face==expected.face&&actual.x==expected.x&&actual.y==expected.y&&actual.width==expected.width&&actual.depth==expected.depth&&actual.rotation==expected.rotation,label+": cached footprint matches direct grid calculation");var port=local.Grid.Direction(local.Grid.Port(expected));Check((local.Roads.Port(b)-port).sqrMagnitude<1e-12f,label+": road entrance matches direct grid calculation");int node=local.Roads.Node(port);Check(local.Roads.Connected(b,b)==(node>=0),label+": cached road node matches current graph");}
            try{Verify("Initial placement");Verify("Repeated query");b.rotation=(rotation+1)&3;Verify("Rotated placement");b.position=CubeSphere.Encode(local.Grid.Point(local.Area(b).face,local.Area(b).x+30,local.Area(b).y+30));Verify("Moved placement");b.definition.footprintWidth=width+1;Verify("Changed footprint dimensions");}
            finally{b.position=position;b.rotation=rotation;b.definition.footprintWidth=width;}
            Verify("Restored placement");
            var occupied=new PlanetRtsBase.ConstructionOccupancy(local.Grid);foreach(var building in local.Buildings)if(!building.removed)occupied.Add(local.Area(building));
            var random=new System.Random(75291);int compared=0;
            foreach(var building in local.Buildings){var origin=local.Area(building);for(int i=0;i<16;i++){var p=local.Grid.Point(origin.face,origin.x+random.Next(-24,25),origin.y+random.Next(-24,25));var area=local.Grid.Area(building.definition,p,i&3);bool expected=local.GridClear(area,out _,building.definition);bool actual=local.GridClear(area,out _,building.definition,occupied);if(expected!=actual)throw new System.InvalidOperationException("Indexed placement disagrees with full building/entrance/road/rail validation");compared++;}}
            Check(compared>0,"Indexed placement matches the original full scan around every real building, including both teams");
            var grid=new PlanetConstructionGrid(1,120000,8);var index=new PlanetRtsBase.ConstructionOccupancy(grid);var areas=new System.Collections.Generic.List<PlanetConstructionGrid.Footprint>();
            // Concentrated blocks and face-edge entrances, not mostly empty random planet cells.
            for(int face=0;face<6;face++)for(int i=0;i<96;i++){int x=i<32?random.Next(0,128):i<64?grid.Resolution-128+random.Next(0,96):grid.Resolution/2+random.Next(-64,65);int y=i%3==0?0:i%3==1?grid.Resolution-24:grid.Resolution/2+random.Next(-64,65);var area=new PlanetConstructionGrid.Footprint((CubeFace)face,x,y,random.Next(1,25),random.Next(1,25),i&3);areas.Add(area);index.Add(area);}
            int blocked=0,clear=0;foreach(var origin in areas)for(int i=0;i<12;i++){int x=System.Math.Clamp(origin.x+random.Next(-30,31),0,grid.Resolution-24),y=System.Math.Clamp(origin.y+random.Next(-30,31),0,grid.Resolution-24);var area=new PlanetConstructionGrid.Footprint(origin.face,x,y,random.Next(1,25),random.Next(1,25),i&3);bool expected=true;var port=grid.Port(area);foreach(var other in areas)if(area.Overlaps(other)||area.Contains(grid.Port(other))||other.Contains(port)){expected=false;break;}if(expected!=index.Clear(area))throw new System.InvalidOperationException("Spatial placement index disagrees at a block/face boundary");if(expected)clear++;else blocked++;}
            Check(blocked>0&&clear>0,"Spatial placement agrees with exhaustive overlap and entrance checks for 6,912 queries on all faces, seams and bucket boundaries");
            foreach(var origin in areas)for(int i=0;i<8;i++){var cell=new PlanetNavCoordinate(grid.Planet,origin.face,System.Math.Clamp(origin.x+random.Next(-20,21),0,grid.Resolution-1),System.Math.Clamp(origin.y+random.Next(-20,21),0,grid.Resolution-1));bool expected=true;foreach(var other in areas)if(other.Contains(cell)){expected=false;break;}if(expected!=index.RoadCellClear(cell))throw new System.InvalidOperationException("Indexed road obstacle test disagrees with exhaustive footprints");}
            Check(true,"Road occupancy matches exhaustive infrastructure obstacles for 4,608 cells");
            var gateIndex=new PlanetRtsBase.ConstructionOccupancy(local.Grid);var gate=local.Catalog.Buildings["gate"];for(int turn=0;turn<4;turn++){var area=local.Grid.Area(gate,Vector3.up,turn);gateIndex=new PlanetRtsBase.ConstructionOccupancy(local.Grid);gateIndex.Add(area,true);for(int y=0;y<area.depth;y++)for(int x=0;x<area.width;x++){var cell=new PlanetNavCoordinate(local.Grid.Planet,area.face,area.x+x,area.y+y);if(gateIndex.RoadCellClear(cell)!=PlanetRtsBase.GatePassage(area,cell))throw new System.InvalidOperationException("Indexed road obstacles block an open gate passage or allow its pillars");}}
            Check(true,"All four gate rotations preserve road passage and pillar exclusion");
        }
    }
}
