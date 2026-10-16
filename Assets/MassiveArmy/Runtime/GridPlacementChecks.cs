using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MassiveArmy
{
    /// <summary>Opt-in bounded construction fixtures, shared by Editor and standalone validation.</summary>
    public static class GridPlacementChecks
    {
        public static void Geometry(RtsCatalog catalog,Action<bool,string> check)
        {
            check(catalog.buildings.Length==49&&catalog.buildings.All(b=>b.footprintWidth>0&&b.footprintDepth>0),"All 49 buildings have model footprints");
            foreach(float size in new[]{4096f,16384f})foreach(var d in catalog.buildings)foreach(int turn in new[]{0,1,2,3}){
                Vector2 p=new Vector2(size*.5f-1,-size*.5f+3);var snapped=BuildingGrid.Snap(d,p,size,turn);
                check(BuildingGrid.Snap(d,snapped,size,turn)==snapped,d.id+" rotation "+turn+" snap is stable at "+size);
                check(BuildingGrid.Cells(d,snapped,size,turn).Distinct().Count()==d.footprintWidth*d.footprintDepth,d.id+" wrapped footprint coverage "+turn+" at "+size);
            }
            var gate=catalog.Buildings["gate"];check(BuildingGrid.Dimensions(gate)==new Vector2Int(3,1)&&BuildingGrid.Dimensions(gate,1)==new Vector2Int(1,3),"Gate rotates between 24x8 and 8x24 metres");
            foreach(string id in new[]{"autocannon","at_turret","aa_turret","point_defence","laser"})check(BuildingGrid.Dimensions(catalog.Buildings[id])==Vector2Int.one,id+" occupies one tile");
            var router=new WallPreviewRouter();var route=new List<int>();
            check(router.Find(0,2,512,c=>true,route,out _)&&route.SequenceEqual(new[]{0,1,2}),"Wall preview produces cardinal cells");
            check(router.Find(0,2,512,c=>c!=1,route,out _)&&route.Count==5&&!route.Contains(1),"Wall preview routes around an obstacle");
            check(router.Find(511,1,512,c=>true,route,out _)&&route.SequenceEqual(new[]{511,0,1}),"Wall preview crosses shortest wrapping seam");
            check(!router.Find(0,2,512,c=>c!=2,route,out _)&&route.Count==0,"Blocked endpoint produces no partial route");
            check(!router.Find(0,128,512,c=>true,route,out _)&&route.Count==0,"Overlength wall route is rejected");
        }
        public static RtsBuilding World(RtsWorld world,Action<bool,string> check,int rotation=0)
        {
            var f=world.Factions[0];f.tier=5;foreach(var tech in world.Catalog.technologies)f.technologies.Add(tech.id);
            f.wallet.Capacity=new ResourceCost{materials=100000,fuel=100000,rareElements=100000,isotopes=100000,research=100000};f.wallet.Stock=f.wallet.Capacity;
            for(int i=0;i<16;i++)world.SectorOwner[i]=0;
            var wall=world.Catalog.Buildings["wall"];var home=world.BasePosition(0);Vector2 centre=Vector2.zero;bool found=false;
            Vector2 axis=(rotation&1)==0?Vector2.right:Vector2.up;
            for(int ring=8;ring<35&&!found;ring++)for(int k=0;k<32&&!found;k++){
                var candidate=world.SnapPlacement(wall,home+new Vector2(Mathf.Cos(k*Mathf.PI/16),Mathf.Sin(k*Mathf.PI/16))*ring*8);
                bool valid=true;for(int j=-3;j<=3;j++)if(!world.ValidatePlacement(wall,0,candidate+axis*j*8,true).valid){valid=false;break;}
                Vector2 across=BuildingGrid.Forward(rotation);
                for(int j=-3;j<=3&&valid;j++)if(!world.ValidatePlacement(wall,0,candidate+across*j*8,true).valid)valid=false;
                for(int j=-3;j<=3&&valid;j++)if(world.NavigationCost(candidate+across*j*8,2)>=DeploymentMap.Blocked*.5f)valid=false;
                for(int z=-1;z<=1&&valid;z++)for(int x=-1;x<=1;x++)if(!world.ValidatePlacement(wall,0,candidate+new Vector2(x,z)*8,true).valid){valid=false;break;}
                if(valid){centre=candidate;found=true;}
            }
            check(found,"Stable seven-cell wall foundation found");if(!found)return null;
            int n=BuildingGrid.Resolution(world.Field.Size);var cells=new List<int>();
            for(int j=-3;j<=3;j++){var anchor=BuildingGrid.Anchor(wall,centre+axis*j*8,world.Field.Size);cells.Add(WorldTopology.Index(anchor.x,anchor.y,n));}
            float before=f.wallet.Stock.materials;check(world.ConstructWallChain(cells,0,true),"Wall chain commits atomically");
            check(Mathf.Abs(before-f.wallet.Stock.materials-wall.cost.materials*7)<.01f,"Wall chain charges exact segment cost");
            foreach(int cell in cells){var b=world.BuildingAtCell(cell);b.construction=b.definition.time;b.health=b.MaxHealth;}
            check(world.GateRotation(centre,0,rotation^1)%2==rotation%2,"Gate automatically aligns with existing walls");
            var gateDef=world.Catalog.Buildings["gate"];var replacement=new List<RtsBuilding>();
            check(world.GateReplacement(centre,0,rotation,replacement,out _)&&replacement.Count==3,"Gate replaces exactly three owned straight walls");
            check(!world.GateReplacement(centre,1,rotation,replacement,out _),"Enemy walls cannot be converted into a gate");
            float expected=gateDef.cost.materials-wall.cost.materials*3*.45f;before=f.wallet.Stock.materials;
            var gate=world.Construct("gate",0,centre,true,rotation);check(gate!=null,"Gate replacement succeeds");if(gate==null)return null;
            check(Mathf.Abs(before-f.wallet.Stock.materials-expected)<.01f,"Gate transaction applies 45 percent wall salvage");
            check(cells.Count(cell=>world.BuildingAtCell(cell)?.definition.id=="gate")==3,"Gate reserves its complete three-cell footprint");
            var newCells=new[]{cells[0],cells[1]};before=f.wallet.Stock.materials;check(world.ConstructWallChain(newCells,0,true)&&before==f.wallet.Stock.materials,"Existing friendly walls are reused without charge");
            check(!world.ValidatePlacement(wall,0,centre,true).valid,"Other construction cannot occupy a gate");
            gate.construction=gate.definition.time;world.Update(.25f);
            int fine=BuildingGrid.Cells(gateDef,gate.position,world.Field.Size,rotation).First();
            check(world.Field.StructureCellData[fine]==2,"Completed owner gate blocks only enemy collision");
            gate.powered=false;world.Update(.25f);check(world.Field.StructureCellData[fine]==3,"Unpowered gate blocks both teams");
            gate.powered=true;world.Update(.25f);check(world.Field.StructureCellData[fine]==2,"Power restoration restores owner access");
            return gate;
        }
    }
}
