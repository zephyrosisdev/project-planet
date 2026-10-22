using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Batched land-use decals and shared RTS cargo meshes; no GameObject per road, crop or truck.</summary>
    public sealed class SettlementRenderer:IDisposable {
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly Material land,cargo,district,citizenMaterial;
        readonly SettlementCargoNavigation navigation;readonly SettlementCitizens citizens;Mesh mesh;IEnumerator rebuild;Mesh pending;Mesh districtMesh,pendingDistrict;
        readonly List<Vector2> roadMetrics=new List<Vector2>();
        int roadRevision=-1,fieldSignature=-1,terrainRevision=-1,cargoCount;double nextFields;
        public SettlementRenderer(PlanetDevelopment world,PlanetCatalogRenderer catalogue){
            this.world=world;surface=new PlanetSurface(world.definition);land=new Material(Resources.Load<Shader>("MassiveArmy/SettlementLandUse"));cargo=new Material(Resources.Load<Shader>("MassiveArmy/SettlementCargo"));
            land.SetFloat("_RoadWidth",world.Base.Grid.CarriagewayWidth);
            district=new Material(land);district.SetFloat("_DistrictOverlay",1);
            var library=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");land.SetTexture("_SurfaceAlbedo",library.albedo);land.SetTexture("_SurfaceData",library.surfaceData);district.SetTexture("_SurfaceAlbedo",library.albedo);district.SetTexture("_SurfaceData",library.surfaceData);catalogue.BindArt(cargo);
            navigation=new SettlementCargoNavigation(world);world.Base.CargoNavigation=navigation;
            citizenMaterial=new Material(Resources.Load<Shader>("MassiveArmy/SettlementCitizens"));catalogue.BindArt(citizenMaterial);citizens=new SettlementCitizens(world);world.Base.Citizens=citizens;
        }
        internal bool ReadyForValidation=>mesh!=null&&(!world.Base.DistrictOverlay||districtMesh!=null)&&rebuild==null&&roadRevision==(world.Base.Roads.Revision*397^world.Base.Rails.Revision)&&terrainRevision==world.definition.Edits.Revision;
        public void Update(){
            var civilisation=world.SystemRuntime.Civilisation;var local=world.Base;
            if(rebuild!=null){if(!rebuild.MoveNext()){rebuild=null;ArmyLifetime.Destroy(mesh);mesh=pending;pending=null;ArmyLifetime.Destroy(districtMesh);districtMesh=pendingDistrict;pendingDistrict=null;}}
            if(rebuild==null&&Time.realtimeSinceStartupAsDouble>=nextFields){nextFields=Time.realtimeSinceStartupAsDouble+1;int signature=local.DistrictOverlay?19:17;
                foreach(var building in local.Buildings)if(!building.removed)signature=unchecked(signature*31+building.index*7+building.rotation+building.position.GetHashCode());
                foreach(var colony in local.Settlements)if(colony!=null)foreach(var item in colony.installations)if(item.definition.StartsWith("civil_farm_"))signature=unchecked(signature*31+(int)item.id*7+(int)(item.growth*8));
                int terrain=world.definition.Edits.Revision;if(roadRevision!=(local.Roads.Revision*397^local.Rails.Revision)||fieldSignature!=signature||terrainRevision!=terrain){roadRevision=local.Roads.Revision*397^local.Rails.Revision;fieldSignature=signature;terrainRevision=terrain;rebuild=BuildMesh();}
            }
            if(!world.Paused&&!civilisation.CatchingUp(world.definition.bodyId))navigation.Step(world.SimulationDelta);
            citizens.Step(world.SimulationDelta);
            cargoCount=navigation.Poses==null?0:navigation.Count;
            if(cargoCount>0)cargo.SetBuffer("_CargoPoses",navigation.Poses);
        }
        IEnumerator BuildMesh(){
            roadMetrics.Clear();var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector4>();var colours=new List<Color>();var indices=new List<int>();int work=0;
            var roadSnapshot=new RoadTile[world.Base.Roads.Tiles.Count];for(int i=0;i<roadSnapshot.Length;i++)roadSnapshot[i]=world.Base.Roads.Tiles[i];
            foreach(var tile in roadSnapshot){AddRoad(tile,vertices,normals,uv,colours,indices);if(++work%16==0)yield return null;}
            var railSnapshot=new RoadTile[world.Base.Rails.Tiles.Count];for(int i=0;i<railSnapshot.Length;i++)railSnapshot[i]=world.Base.Rails.Tiles[i];foreach(var tile in railSnapshot){AddRoad(tile,vertices,normals,uv,colours,indices,true);if(++work%16==0)yield return null;}
            var civilisation=world.SystemRuntime.Civilisation;
            var districtVertices=new List<Vector3>();var districtNormals=new List<Vector3>();var districtUv=new List<Vector4>();var districtColours=new List<Color>();var districtIndices=new List<int>();bool overlay=world.Base.DistrictOverlay;
            var buildings=world.Base.Buildings.ToArray();
            foreach(var building in buildings){if(building.removed)continue;
                bool farm=building.definition.id.StartsWith("civil_farm_");float growth=0;Color colour=new Color(.7f,.7f,.7f,0);
                if(farm){colour=Color.green;foreach(var colony in world.Base.Settlements)if(colony!=null){var item=SettlementLogistics.Find(colony,building.persistentId);if(item==null)continue;growth=(float)item.growth;foreach(var crop in civilisation.Catalogue.crops)if(crop.id==item.crop){colour=crop.colour;break;}}}
                if(overlay)AddDistrict(building,districtVertices,districtNormals,districtUv,districtColours,districtIndices);
                AddParcel(building,farm?6:8,growth,colour,vertices,normals,uv,colours,indices);yield return null;
            }
            pending=new Mesh{name="Batched spherical roads and farmland",indexFormat=IndexFormat.UInt32};while(roadMetrics.Count<vertices.Count)roadMetrics.Add(Vector2.one);pending.SetVertices(vertices);pending.SetNormals(normals);pending.SetUVs(0,uv);pending.SetUVs(1,roadMetrics);pending.SetColors(colours);pending.SetTriangles(indices,0);pending.RecalculateBounds();
            if(overlay){pendingDistrict=new Mesh{name="Batched district overlay",indexFormat=IndexFormat.UInt32};pendingDistrict.SetVertices(districtVertices);pendingDistrict.SetNormals(districtNormals);pendingDistrict.SetUVs(0,districtUv);pendingDistrict.SetColors(districtColours);pendingDistrict.SetTriangles(districtIndices,0);pendingDistrict.RecalculateBounds();}
        }
        void AddRoad(RoadTile tile,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices,bool rail=false){
            int start=vertices.Count;
            for(int y=0;y<2;y++)for(int x=0;x<2;x++){var d=world.Base.Roads.Corner(tile,x,y);vertices.Add(surface.GetSurfacePositionPrecise(d,.35).Float);normals.Add(d);uv.Add(new Vector4(x,y,rail?9:8,tile.connections));colours.Add(Color.white);}
            var metrics=new Vector2((Vector3.Distance(vertices[start],vertices[start+1])+Vector3.Distance(vertices[start+2],vertices[start+3]))*.5f,(Vector3.Distance(vertices[start],vertices[start+2])+Vector3.Distance(vertices[start+1],vertices[start+3]))*.5f);for(int i=0;i<4;i++)roadMetrics.Add(metrics);
            indices.Add(start);indices.Add(start+2);indices.Add(start+1);indices.Add(start+1);indices.Add(start+2);indices.Add(start+3);
        }
        void AddParcel(RtsBuilding building,int layer,float growth,Color colour,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var grid=world.Base.Grid;var area=world.Base.Area(building);float foundation=world.Base.FoundationRadius(building)+.02f;float span=Mathf.Sqrt(8*foundation*.01f);int nx=layer==6||world.Base.LegacyCityPresentation?Math.Max(2,area.width*2):Math.Max(2,Mathf.CeilToInt(area.width*(world.Base.Grid.TileSize)/span)),ny=layer==6||world.Base.LegacyCityPresentation?Math.Max(2,area.depth*2):Math.Max(2,Mathf.CeilToInt(area.depth*(world.Base.Grid.TileSize)/span)),start=vertices.Count;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){float u=x/(float)nx,v=y/(float)ny;var d=grid.Point(area,u,v);vertices.Add(layer==6?surface.GetSurfacePositionPrecise(d,.15).Float:d*foundation);normals.Add(d);Vector2 texture=area.rotation==0?new Vector2(u,v):area.rotation==1?new Vector2(v,1-u):area.rotation==2?new Vector2(1-u,1-v):new Vector2(1-v,u);uv.Add(new Vector4(texture.x,texture.y,layer,growth));colours.Add(colour);}
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int a=start+y*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;indices.Add(a);indices.Add(c);indices.Add(b);indices.Add(b);indices.Add(c);indices.Add(d);}
        }
        void AddDistrict(RtsBuilding b,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var area=world.Base.Area(b);int width=area.width+4,depth=area.depth+4,start=vertices.Count;var colour=SettlementDistricts.Colour(b.definition.district);
            for(int y=0;y<=depth;y++)for(int x=0;x<=width;x++){var d=world.Base.Grid.Point(area.face,area.x-2+x,area.y-2+y);vertices.Add(surface.GetSurfacePositionPrecise(d,.65).Float);normals.Add(d);uv.Add(new Vector4(x/(float)width,y/(float)depth,0,0));colours.Add(colour);}
            for(int y=0;y<depth;y++)for(int x=0;x<width;x++){int a=start+y*(width+1)+x;indices.Add(a);indices.Add(a+width+1);indices.Add(a+1);indices.Add(a+1);indices.Add(a+width+1);indices.Add(a+width+2);}
        }
        public void Draw(CommandBuffer commands){
            var vp=GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true)*world.View.worldToCameraMatrix;
            land.SetMatrix("_SettlementVP",vp);land.SetVector("_SettlementCenter",world.PlanetRenderCentre);land.SetVector("_SettlementCamera",world.View.transform.position);if(mesh!=null)commands.DrawMesh(mesh,Matrix4x4.identity,land);
            if(world.Base.DistrictOverlay&&districtMesh!=null){district.SetMatrix("_SettlementVP",vp);district.SetVector("_SettlementCenter",world.PlanetRenderCentre);commands.DrawMesh(districtMesh,Matrix4x4.identity,district);}
            if(citizens.Poses!=null&&citizens.Count>0){citizenMaterial.SetBuffer("_CargoPoses",citizens.Poses);citizenMaterial.SetBuffer("_CitizenVisible",citizens.Visible);citizenMaterial.SetVector("_SettlementCenter",world.PlanetRenderCentre);citizenMaterial.SetMatrix("_SettlementVP",vp);citizenMaterial.SetVector("_SettlementCamera",world.View.transform.position);commands.DrawProceduralIndirect(Matrix4x4.identity,citizenMaterial,0,MeshTopology.Triangles,citizens.DrawArguments,0);}
            cargo.SetVector("_SettlementCenter",world.PlanetRenderCentre);cargo.SetMatrix("_SettlementVP",vp);cargo.SetVector("_SettlementCamera",world.View.transform.position);if(cargoCount>0)commands.DrawProcedural(Matrix4x4.identity,cargo,0,MeshTopology.Triangles,144,cargoCount);
        }
        public void Dispose(){citizens.Dispose();ArmyLifetime.Destroy(citizenMaterial);navigation.Dispose();ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(pending);ArmyLifetime.Destroy(land);ArmyLifetime.Destroy(cargo);ArmyLifetime.Destroy(district);ArmyLifetime.Destroy(districtMesh);ArmyLifetime.Destroy(pendingDistrict);}
    }
}

