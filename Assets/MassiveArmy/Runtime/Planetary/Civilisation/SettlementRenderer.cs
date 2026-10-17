using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Batched land-use decals and shared RTS cargo meshes; no GameObject per road, crop or truck.</summary>
    public sealed class SettlementRenderer:IDisposable {
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Material land,cargo;
        readonly SettlementCargoNavigation navigation;Mesh mesh;IEnumerator rebuild;Mesh pending;
        int roadRevision=-1,fieldSignature=-1,terrainRevision=-1,cargoCount;double nextFields;
        public SettlementRenderer(PlanetDevelopment world,PlanetCatalogRenderer catalogue){
            this.world=world;surface=new PlanetSurface(world.definition);land=new Material(Resources.Load<Shader>("MassiveArmy/SettlementLandUse"));cargo=new Material(Resources.Load<Shader>("MassiveArmy/SettlementCargo"));
            var library=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");land.SetTexture("_SurfaceAlbedo",library.albedo);land.SetTexture("_SurfaceData",library.surfaceData);catalogue.BindArt(cargo);
            navigation=new SettlementCargoNavigation(world);world.Base.CargoNavigation=navigation;
        }
        internal bool ReadyForValidation=>mesh!=null&&rebuild==null&&roadRevision==world.Base.Roads.Revision&&terrainRevision==world.definition.Edits.Revision;
        public void Update(){
            var civilisation=world.SystemRuntime.Civilisation;var local=world.Base;
            if(rebuild!=null){if(!rebuild.MoveNext()){rebuild=null;ArmyLifetime.Destroy(mesh);mesh=pending;pending=null;}}
            if(rebuild==null&&Time.realtimeSinceStartupAsDouble>=nextFields){nextFields=Time.realtimeSinceStartupAsDouble+1;int signature=17;
                foreach(var building in local.Buildings)if(!building.removed)signature=unchecked(signature*31+building.index*7+building.rotation+building.position.GetHashCode());
                foreach(var colony in local.Settlements)if(colony!=null)foreach(var item in colony.installations)if(item.definition.StartsWith("civil_farm_"))signature=unchecked(signature*31+(int)item.id*7+(int)(item.growth*8));
                int terrain=world.definition.Edits.Revision;if(roadRevision!=local.Roads.Revision||fieldSignature!=signature||terrainRevision!=terrain){roadRevision=local.Roads.Revision;fieldSignature=signature;terrainRevision=terrain;rebuild=BuildMesh();}
            }
            if(!world.Paused&&!civilisation.CatchingUp(world.definition.bodyId))navigation.Step(Time.deltaTime);
            cargoCount=navigation.Poses==null?0:navigation.Count;
            if(cargoCount>0)cargo.SetBuffer("_CargoPoses",navigation.Poses);
        }
        IEnumerator BuildMesh(){
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector4>();var colours=new List<Color>();var indices=new List<int>();int work=0;
            var roadSnapshot=new RoadTile[world.Base.Roads.Tiles.Count];for(int i=0;i<roadSnapshot.Length;i++)roadSnapshot[i]=world.Base.Roads.Tiles[i];
            foreach(var tile in roadSnapshot){AddRoad(tile,vertices,normals,uv,colours,indices);if(++work%16==0)yield return null;}
            var civilisation=world.SystemRuntime.Civilisation;
            var buildings=world.Base.Buildings.ToArray();
            foreach(var building in buildings){if(building.removed)continue;
                bool farm=building.definition.id.StartsWith("civil_farm_");float growth=0;Color colour=new Color(.7f,.7f,.7f,0);
                if(farm){colour=Color.green;foreach(var colony in world.Base.Settlements)if(colony!=null){var item=SettlementLogistics.Find(colony,building.persistentId);if(item==null)continue;growth=(float)item.growth;foreach(var crop in civilisation.Catalogue.crops)if(crop.id==item.crop){colour=crop.colour;break;}}}
                AddParcel(building,farm?6:8,growth,colour,vertices,normals,uv,colours,indices);yield return null;
            }
            pending=new Mesh{name="Batched spherical roads and farmland",indexFormat=IndexFormat.UInt32};pending.SetVertices(vertices);pending.SetNormals(normals);pending.SetUVs(0,uv);pending.SetColors(colours);pending.SetTriangles(indices,0);pending.RecalculateBounds();
        }
        void AddRoad(RoadTile tile,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            int start=vertices.Count;
            for(int y=0;y<2;y++)for(int x=0;x<2;x++){var d=world.Base.Roads.Corner(tile,x,y);vertices.Add(surface.GetSurfacePositionPrecise(d,.35).Float);normals.Add(d);uv.Add(new Vector4(x,y,8,tile.connections));colours.Add(Color.white);}
            indices.Add(start);indices.Add(start+2);indices.Add(start+1);indices.Add(start+1);indices.Add(start+2);indices.Add(start+3);
        }
        void AddParcel(RtsBuilding building,int layer,float growth,Color colour,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var grid=world.Base.Grid;var area=world.Base.Area(building);int nx=Math.Max(2,area.width*2),ny=Math.Max(2,area.depth*2),start=vertices.Count;float foundation=world.Base.FoundationRadius(building)+.02f;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){float u=x/(float)nx,v=y/(float)ny;var d=grid.Point(area,u,v);vertices.Add(layer==6?surface.GetSurfacePositionPrecise(d,.15).Float:d*foundation);normals.Add(d);Vector2 texture=area.rotation==0?new Vector2(u,v):area.rotation==1?new Vector2(v,1-u):area.rotation==2?new Vector2(1-u,1-v):new Vector2(1-v,u);uv.Add(new Vector4(texture.x,texture.y,layer,growth));colours.Add(colour);}
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int a=start+y*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;indices.Add(a);indices.Add(c);indices.Add(b);indices.Add(b);indices.Add(c);indices.Add(d);}
        }
        public void Draw(CommandBuffer commands){
            var vp=GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true)*world.View.worldToCameraMatrix;
            land.SetMatrix("_SettlementVP",vp);land.SetVector("_SettlementCenter",world.PlanetRenderCentre);land.SetVector("_SettlementCamera",world.View.transform.position);if(mesh!=null)commands.DrawMesh(mesh,Matrix4x4.identity,land);
            cargo.SetVector("_SettlementCenter",world.PlanetRenderCentre);cargo.SetMatrix("_SettlementVP",vp);cargo.SetVector("_SettlementCamera",world.View.transform.position);if(cargoCount>0)commands.DrawProcedural(Matrix4x4.identity,cargo,0,MeshTopology.Triangles,ArtUnitRenderer.NearTriangles*3,cargoCount);
        }
        public void Dispose(){navigation.Dispose();ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(pending);ArmyLifetime.Destroy(land);ArmyLifetime.Destroy(cargo);}
    }
}

