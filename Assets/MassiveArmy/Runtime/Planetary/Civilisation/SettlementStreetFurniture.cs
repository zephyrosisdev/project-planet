using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    internal sealed class SettlementStreetFurniture:IDisposable {
        const int ChunkSize=32,SignsPerChunk=12;
        readonly ModularGeometry scratch=new ModularGeometry();readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Material material;
        readonly Dictionary<(CubeFace,int,int),ModularGeometry> builders=new Dictionary<(CubeFace,int,int),ModularGeometry>();readonly Dictionary<(CubeFace,int,int),int> counts=new Dictionary<(CubeFace,int,int),int>();
        Dictionary<(CubeFace,int,int),Mesh> meshes=new Dictionary<(CubeFace,int,int),Mesh>();readonly HashSet<(CubeFace,int,int)> queried=new HashSet<(CubeFace,int,int)>();
        readonly Dictionary<(CubeFace,int,int),List<RoadTile>> tileChunks=new Dictionary<(CubeFace,int,int),List<RoadTile>>();readonly HashSet<(CubeFace,int,int)> dirty=new HashSet<(CubeFace,int,int)>();int indexed,terrain=-1;
        void Changed(int i){if(i<world.Base.Roads.Tiles.Count){var c=world.Base.Roads.Tiles[i].cell;dirty.Add((c.face,c.x/ChunkSize,c.y/ChunkSize));}}
        void Reset(){indexed=0;tileChunks.Clear();foreach(var key in meshes.Keys)dirty.Add(key);}
        public void Update(){var tiles=world.Base.Roads.Tiles;if(tiles.Count<indexed)Reset();for(;indexed<tiles.Count;indexed++){var tile=tiles[indexed];var key=(tile.cell.face,tile.cell.x/ChunkSize,tile.cell.y/ChunkSize);if(!tileChunks.TryGetValue(key,out var list)){list=new List<RoadTile>();tileChunks.Add(key,list);}list.Add(tile);dirty.Add(key);}
            if(terrain!=world.definition.Edits.Revision){terrain=world.definition.Edits.Revision;foreach(var key in tileChunks.Keys)dirty.Add(key);}
            // Four spatial chunks per presentation frame; unrelated roads and plots are retained.
            for(int work=0;work<4&&dirty.Count>0;work++){var iterator=dirty.GetEnumerator();iterator.MoveNext();var key=iterator.Current;iterator.Dispose();dirty.Remove(key);Begin();if(tileChunks.TryGetValue(key,out var list))foreach(var tile in list)Add(tile);
                if(meshes.TryGetValue(key,out var old)){ArmyLifetime.Destroy(old);meshes.Remove(key);}if(builders.TryGetValue(key,out var g)&&g.Triangles>0){var mesh=g.Build("Road signs");mesh.UploadMeshData(true);meshes[key]=mesh;}builders.Clear();counts.Clear();}}
        public SettlementStreetFurniture(PlanetDevelopment world){this.world=world;world.Base.Roads.TileChanged+=Changed;world.Base.Roads.GraphReset+=Reset;surface=new PlanetSurface(world.definition);material=new Material(Resources.Load<Shader>("MassiveArmy/ModularStructure"));material.SetFloat("_ManualRender",1);}
        public void Begin(){builders.Clear();counts.Clear();scratch.Clear();}
        public void Add(RoadTile tile){if(RtsModelArt.ModularBenchmarkControl)return;
            bool junction=tile.Shape==RoadShape.Tee||tile.Shape==RoadShape.Cross;
            if(!junction&&((uint)(tile.cell.x*73856093^tile.cell.y*19349663)&15)!=0)return;
            var key=(tile.cell.face,tile.cell.x/ChunkSize,tile.cell.y/ChunkSize);counts.TryGetValue(key,out int count);if(count>=SignsPerChunk)return;if(!builders.TryGetValue(key,out var g)){g=builders.Count==0?scratch:new ModularGeometry();builders.Add(key,g);}
            var grid=world.Base.Grid;var up=grid.Direction(tile.cell);var centre=surface.GetSurfacePositionPrecise(up,.38f).Float;
            for(int edge=0;edge<4;edge++){if((tile.connections&(1<<edge))==0||count>=SignsPerChunk)continue;var outward=grid.NeighbourTangent(tile.cell,edge);if(outward.sqrMagnitude<.5f)continue;var travel=-outward;var right=Vector3.Cross(up,travel).normalized;var q=Quaternion.LookRotation(-travel,up);
                var point=centre+outward*(junction?4:0)+right*(grid.CarriagewayWidth*.5f+.7f);g.Box(point+up, new Vector3(.09f,2,.09f),new Color(.4f,.42f,.43f),q);var label=point+up*2;
                g.Box(label,new Vector3(.8f,.65f,.06f),junction?new Color(.65f,.045f,.025f):Color.white,q);g.Text(junction?"STOP":"30",label+q*Vector3.forward*.04f,junction?.17f:.42f,junction?Color.white:new Color(.08f,.09f,.1f),q);count++;
            }counts[key]=count;
        }
        public IEnumerator Finish(){var next=new Dictionary<(CubeFace,int,int),Mesh>();int work=0;foreach(var pair in builders){if(pair.Value.Triangles==0)continue;next.Add(pair.Key,pair.Value.Build("Batched road signs "+pair.Key));if(++work%8==0)yield return null;}foreach(var old in meshes.Values)ArmyLifetime.Destroy(old);meshes=next;builders.Clear();counts.Clear();}
        public void Draw(CommandBuffer commands,Matrix4x4 vp){if(meshes.Count==0)return;var grid=world.Base.Grid;var body=world.View.transform.position-world.PlanetRenderCentre;var cell=grid.Cell(body.normalized);int x=cell.x/ChunkSize,y=cell.y/ChunkSize;queried.Clear();material.SetMatrix("_FurnitureVP",vp);material.SetVector("_FurnitureCentre",world.PlanetRenderCentre);material.SetVector("_FurnitureCamera",world.View.transform.position);
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){var point=grid.Point(cell.face,(x+dx)*ChunkSize+ChunkSize*.5,(y+dy)*ChunkSize+ChunkSize*.5);var nearby=grid.Cell(point);var key=(nearby.face,nearby.x/ChunkSize,nearby.y/ChunkSize);if(queried.Add(key)&&meshes.TryGetValue(key,out var mesh)&&mesh.bounds.SqrDistance(body)<180*180)commands.DrawMesh(mesh,Matrix4x4.identity,material);}
        }
        internal void ReleaseMeshes(){foreach(var mesh in meshes.Values)ArmyLifetime.Destroy(mesh);meshes.Clear();builders.Clear();counts.Clear();queried.Clear();tileChunks.Clear();dirty.Clear();indexed=0;terrain=-1;}
        public void Dispose(){world.Base.Roads.TileChanged-=Changed;world.Base.Roads.GraphReset-=Reset;ReleaseMeshes();ArmyLifetime.Destroy(material);}
    }
}
