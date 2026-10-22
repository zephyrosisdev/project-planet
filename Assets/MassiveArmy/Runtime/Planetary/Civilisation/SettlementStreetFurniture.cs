using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    internal sealed class SettlementStreetFurniture:IDisposable {
        const int ChunkSize=32,SignsPerChunk=12;
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Material material;
        readonly Dictionary<(CubeFace,int,int),ModularGeometry> builders=new Dictionary<(CubeFace,int,int),ModularGeometry>();readonly Dictionary<(CubeFace,int,int),int> counts=new Dictionary<(CubeFace,int,int),int>();
        Dictionary<(CubeFace,int,int),Mesh> meshes=new Dictionary<(CubeFace,int,int),Mesh>();readonly HashSet<(CubeFace,int,int)> queried=new HashSet<(CubeFace,int,int)>();
        public SettlementStreetFurniture(PlanetDevelopment world){this.world=world;surface=new PlanetSurface(world.definition);material=new Material(Resources.Load<Shader>("MassiveArmy/ModularStructure"));material.SetFloat("_ManualRender",1);}
        public void Begin(){builders.Clear();counts.Clear();}
        public void Add(RoadTile tile){if(RtsModelArt.ModularBenchmarkControl)return;
            bool junction=tile.Shape==RoadShape.Tee||tile.Shape==RoadShape.Cross;
            if(!junction&&((uint)(tile.cell.x*73856093^tile.cell.y*19349663)&15)!=0)return;
            var key=(tile.cell.face,tile.cell.x/ChunkSize,tile.cell.y/ChunkSize);counts.TryGetValue(key,out int count);if(count>=SignsPerChunk)return;if(!builders.TryGetValue(key,out var g)){g=new ModularGeometry();builders.Add(key,g);}
            var grid=world.Base.Grid;var up=grid.Direction(tile.cell);var centre=surface.GetSurfacePositionPrecise(up,.38f).Float;
            for(int edge=0;edge<4;edge++){if((tile.connections&(1<<edge))==0||count>=SignsPerChunk)continue;var other=grid.Direction(grid.Neighbour(tile.cell,edge));var outward=Vector3.ProjectOnPlane(other-up,up).normalized;var travel=-outward;var right=Vector3.Cross(up,travel).normalized;var q=Quaternion.LookRotation(-travel,up);
                var point=centre+outward*(junction?4:0)+right*(grid.CarriagewayWidth*.5f+.7f);g.Box(point+up, new Vector3(.09f,2,.09f),new Color(.4f,.42f,.43f),q);var label=point+up*2;
                g.Box(label,new Vector3(.8f,.65f,.06f),junction?new Color(.65f,.045f,.025f):Color.white,q);g.Text(junction?"STOP":"30",label+q*Vector3.forward*.04f,junction?.17f:.42f,junction?Color.white:new Color(.08f,.09f,.1f),q);count++;
            }counts[key]=count;
        }
        public IEnumerator Finish(){var next=new Dictionary<(CubeFace,int,int),Mesh>();int work=0;foreach(var pair in builders){if(pair.Value.Triangles==0)continue;next.Add(pair.Key,pair.Value.Build("Batched road signs "+pair.Key));if(++work%8==0)yield return null;}foreach(var old in meshes.Values)ArmyLifetime.Destroy(old);meshes=next;builders.Clear();counts.Clear();}
        public void Draw(CommandBuffer commands,Matrix4x4 vp){if(meshes.Count==0)return;var grid=world.Base.Grid;var body=world.View.transform.position-world.PlanetRenderCentre;var cell=grid.Cell(body.normalized);int x=cell.x/ChunkSize,y=cell.y/ChunkSize;queried.Clear();material.SetMatrix("_FurnitureVP",vp);material.SetVector("_FurnitureCentre",world.PlanetRenderCentre);material.SetVector("_FurnitureCamera",world.View.transform.position);
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){var point=grid.Point(cell.face,(x+dx)*ChunkSize+ChunkSize*.5,(y+dy)*ChunkSize+ChunkSize*.5);var nearby=grid.Cell(point);var key=(nearby.face,nearby.x/ChunkSize,nearby.y/ChunkSize);if(queried.Add(key)&&meshes.TryGetValue(key,out var mesh)&&mesh.bounds.SqrDistance(body)<180*180)commands.DrawMesh(mesh,Matrix4x4.identity,material);}
        }
        public void Dispose(){foreach(var mesh in meshes.Values)ArmyLifetime.Destroy(mesh);meshes.Clear();ArmyLifetime.Destroy(material);}
    }
}
