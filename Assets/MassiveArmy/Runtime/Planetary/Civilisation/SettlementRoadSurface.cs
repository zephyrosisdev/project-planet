using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary {
    // Sparse cell paint: storage follows road count, never planet surface area.
    internal sealed class SettlementRoadSurface:IDisposable {
        internal struct Cell {public uint face,x,y,mask;}
        readonly PlanetDevelopment world;readonly HashSet<int> dirty=new HashSet<int>();
        readonly List<int> dirtySlots=new List<int>();readonly byte[] coverage=new byte[6*64*64];
        Cell[] table=Array.Empty<Cell>();GraphicsBuffer buffer;Texture2D coarse;bool reset=true;
        internal SettlementRoadSurface(PlanetDevelopment world){this.world=world;world.Base.Roads.TileChanged+=Changed;world.Base.Roads.GraphReset+=Reset;}
        void Changed(int index)=>dirty.Add(index);
        void Reset(){reset=true;}
        internal static uint Hash(uint face,uint x,uint y){unchecked{uint h=face*73856093u^x*19349663u^y*83492791u;h^=h>>16;h*=0x7feb352du;return h^(h>>15);}}
        internal static int Slot(Cell[] cells,uint face,uint x,uint y){int slot=(int)(Hash(face,x,y)&(uint)(cells.Length-1));while(cells[slot].face!=0&&(cells[slot].face!=face||cells[slot].x!=x||cells[slot].y!=y))slot=(slot+1)&(cells.Length-1);return slot;}
        internal void Update(){var network=world.Base.Roads;int required=Mathf.NextPowerOfTwo(Math.Max(16,network.Tiles.Count*2));if(table.Length<required)reset=true;
            if(reset){buffer?.Dispose();table=new Cell[required];Array.Clear(coverage,0,coverage.Length);buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,required,16);dirty.Clear();for(int i=0;i<network.Tiles.Count;i++)dirty.Add(i);}
            if(dirty.Count==0&&!reset)return;dirtySlots.Clear();int resolution=world.Base.Grid.Resolution;
            foreach(int i in dirty){if(i>=network.Tiles.Count)continue;var tile=network.Tiles[i];uint face=(uint)tile.cell.face+1,x=(uint)tile.cell.x,y=(uint)tile.cell.y;int slot=Slot(table,face,x,y);table[slot]=new Cell{face=face,x=x,y=y,mask=(uint)tile.connections};dirtySlots.Add(slot);int cx=(int)((long)x*64/resolution),cy=(int)((long)y*64/resolution);coverage[((int)face-1)*4096+cy*64+cx]=255;}
            if(reset)buffer.SetData(table);else {dirtySlots.Sort();for(int i=0;i<dirtySlots.Count;){int start=dirtySlots[i],end=start+1;while(++i<dirtySlots.Count&&dirtySlots[i]<=end)end=Math.Max(end,dirtySlots[i]+1);buffer.SetData(table,start,start,end-start);}}
            if(coarse==null){coarse=new Texture2D(64*6,64,TextureFormat.R8,false,true){name="Sparse road coverage",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};}
            // Texture rows are face strips; the CPU coverage is face-major.
            for(int f=0;f<6;f++)for(int y=0;y<64;y++)for(int x=0;x<64;x++)textureBytes[y*384+f*64+x]=coverage[f*4096+y*64+x];
            coarse.LoadRawTextureData(textureBytes);coarse.Apply(false,false);
            var material=world.TerrainMaterial;material.SetBuffer("_PlanetRoadCells",buffer);material.SetTexture("_PlanetRoadCoverage",coarse);material.SetInt("_PlanetRoadMask",table.Length-1);material.SetInt("_PlanetRoadResolution",resolution);material.SetFloat("_PlanetRoadWidth",world.Base.Grid.CarriagewayWidth);material.SetInt("_PlanetRoadEnabled",network.Tiles.Count>0?1:0);dirty.Clear();reset=false;
        }
        readonly byte[] textureBytes=new byte[6*64*64];
        internal void Release(){world.TerrainMaterial?.SetInt("_PlanetRoadEnabled",0);buffer?.Dispose();buffer=null;ArmyLifetime.Destroy(coarse);coarse=null;table=Array.Empty<Cell>();dirty.Clear();dirtySlots.Clear();reset=true;}
        public void Dispose(){world.Base.Roads.TileChanged-=Changed;world.Base.Roads.GraphReset-=Reset;Release();}
    }
}
