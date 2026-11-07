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
        readonly SettlementRoadRenderer roads;readonly SettlementFields fields;readonly SettlementStreetFurniture furniture;readonly SettlementCargoNavigation navigation;readonly SettlementCitizens citizens;Mesh mesh;IEnumerator rebuild;Mesh pending;Mesh districtMesh,pendingDistrict;
        readonly List<Vector3> precisionOffsets=new List<Vector3>();
        readonly List<Vector2> roadMetrics=new List<Vector2>();
        readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>(),districtVertices=new List<Vector3>(),districtNormals=new List<Vector3>();
        readonly List<Vector4> uv=new List<Vector4>(),districtUv=new List<Vector4>();readonly List<Color> colours=new List<Color>(),districtColours=new List<Color>();readonly List<int> indices=new List<int>(),districtIndices=new List<int>();
        readonly List<RtsBuilding> buildingSnapshot=new List<RtsBuilding>();readonly List<SettlementLandTile> landSnapshot=new List<SettlementLandTile>();readonly List<PipelineLink> pipeSnapshot=new List<PipelineLink>();
        readonly HashSet<(Vector2,Vector2,string)> drawnPipes=new HashSet<(Vector2,Vector2,string)>();
        int pipeRevision=-1,fieldSignature=-1,terrainRevision=-1,cargoCount;double nextFields;
        public SettlementRenderer(PlanetDevelopment world,PlanetCatalogRenderer catalogue){
            this.world=world;roads=new SettlementRoadRenderer(world);fields=new SettlementFields(world);furniture=new SettlementStreetFurniture(world);surface=new PlanetSurface(world.definition);land=new Material(Resources.Load<Shader>("MassiveArmy/SettlementLandUse"));cargo=new Material(Resources.Load<Shader>("MassiveArmy/SettlementCargo"));
            land.SetFloat("_RoadWidth",world.Base.Grid.CarriagewayWidth);
            district=new Material(land);district.SetFloat("_DistrictOverlay",1);
            var library=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");land.SetTexture("_SurfaceAlbedo",library.albedo);land.SetTexture("_SurfaceData",library.surfaceData);district.SetTexture("_SurfaceAlbedo",library.albedo);district.SetTexture("_SurfaceData",library.surfaceData);catalogue.BindArt(cargo);
            navigation=new SettlementCargoNavigation(world);world.Base.CargoNavigation=navigation;
            citizenMaterial=new Material(Resources.Load<Shader>("MassiveArmy/SettlementCitizens"));catalogue.BindArt(citizenMaterial);citizens=new SettlementCitizens(world);world.Base.Citizens=citizens;
        }
        internal bool ReadyForValidation=>roads.Ready&&mesh!=null&&(!world.Base.DistrictOverlay||districtMesh!=null)&&rebuild==null&&terrainRevision==world.definition.Edits.Revision;
        public void Update(){
            var civilisation=world.SystemRuntime.Civilisation;var local=world.Base;roads.Update();furniture.Update();
            if(rebuild!=null){if(!rebuild.MoveNext()){rebuild=null;ArmyLifetime.Destroy(mesh);mesh=pending;pending=null;ArmyLifetime.Destroy(districtMesh);districtMesh=pendingDistrict;pendingDistrict=null;}}
            if(rebuild==null&&Time.realtimeSinceStartupAsDouble>=nextFields){nextFields=Time.realtimeSinceStartupAsDouble+1;int signature=(local.DistrictOverlay?19:17)^local.LandUse.Revision;
                foreach(var building in local.Buildings)if(!building.removed)signature=unchecked(signature*31+building.index*7+building.rotation+building.position.GetHashCode());
                if(!civilisation.Settings.ambientSettlementLife)foreach(var colony in local.Settlements)if(colony!=null)foreach(var item in colony.installations)if(item.definition.StartsWith("civil_farm_"))signature=unchecked(signature*31+(int)item.id*7+(int)(item.growth*8));
                int terrain=world.definition.Edits.Revision;if(pipeRevision!=civilisation.Planet(world.definition.bodyId).pipelineRevision||fieldSignature!=signature||terrainRevision!=terrain){pipeRevision=civilisation.Planet(world.definition.bodyId).pipelineRevision;fieldSignature=signature;terrainRevision=terrain;rebuild=BuildMesh();}
            }
            if(!world.Paused&&!civilisation.CatchingUp(world.definition.bodyId))navigation.Step(world.SimulationDelta);
            citizens.Step(world.SimulationDelta);fields.Update();
            cargoCount=navigation.Poses==null?0:navigation.Count;
            if(cargoCount>0)cargo.SetBuffer("_CargoPoses",navigation.Poses);
        }
        IEnumerator BuildMesh(){
            roadMetrics.Clear();precisionOffsets.Clear();vertices.Clear();normals.Clear();uv.Clear();colours.Clear();indices.Clear();int work=0;
            var civilisation=world.SystemRuntime.Civilisation;
            drawnPipes.Clear();
            pipeSnapshot.Clear();pipeSnapshot.AddRange(civilisation.Planet(world.definition.bodyId).pipelines);
            foreach(var pipe in pipeSnapshot)for(int segment=1;segment<pipe.route.Count;segment++){
                var first=pipe.route[segment-1];var last=pipe.route[segment];bool ordered=first.x<last.x||first.x==last.x&&first.y<last.y;if(!drawnPipes.Add((ordered?first:last,ordered?last:first,pipe.resource)))continue;
                var a=CubeSphere.Decode(pipe.route[segment-1]);var b=CubeSphere.Decode(pipe.route[segment]);
                int samples=Math.Max(1,Mathf.CeilToInt(CubeSphere.ArcDistance(a,b,world.definition.radius)/32));
                for(int step=0;step<samples;step++)AddPipe(Vector3.Slerp(a,b,step/(float)samples),Vector3.Slerp(a,b,(step+1)/(float)samples),pipe.resource=="methane"?new Color(.95f,.72f,.08f):new Color(.22f,.27f,.31f),vertices,normals,uv,colours,indices);
                if(++work%16==0)yield return null;
            }
            landSnapshot.Clear();foreach(var tile in world.Base.LandUse.Tiles)landSnapshot.Add(tile);foreach(var tile in landSnapshot){AddLandTile(tile,vertices,normals,uv,colours,indices);if(++work%16==0)yield return null;}
            foreach(var camp in civilisation.Planet(world.definition.bodyId).raiders){if(camp.health>0)AddRaiderCamp(camp,vertices,normals,uv,colours,indices);yield return null;}
            districtVertices.Clear();districtNormals.Clear();districtUv.Clear();districtColours.Clear();districtIndices.Clear();bool overlay=world.Base.DistrictOverlay;
            buildingSnapshot.Clear();buildingSnapshot.AddRange(world.Base.Buildings);
            foreach(var building in buildingSnapshot){if(building.removed)continue;
                bool farm=building.definition.id.StartsWith("civil_farm_");if(farm&&civilisation.Settings.ambientSettlementLife&&world.Base.Settlements[building.team]?.installations.Find(x=>x.id==building.persistentId)?.fieldConverted==true)continue;float growth=0;Color colour=new Color(.7f,.7f,.7f,0);
                if(farm){colour=Color.green;foreach(var colony in world.Base.Settlements)if(colony!=null){var item=SettlementLogistics.Find(colony,building.persistentId);if(item==null)continue;growth=(float)item.growth;foreach(var crop in civilisation.Catalogue.crops)if(crop.id==item.crop){colour=crop.colour;break;}}}
                if(overlay)AddDistrict(building,districtVertices,districtNormals,districtUv,districtColours,districtIndices);
                AddParcel(building,farm?6:8,growth,colour,vertices,normals,uv,colours,indices);yield return null;
            }
            pending=new Mesh{name="Batched plots, pipes and farmland",indexFormat=IndexFormat.UInt32};while(roadMetrics.Count<vertices.Count)roadMetrics.Add(Vector2.one);while(precisionOffsets.Count<vertices.Count)precisionOffsets.Add(Vector3.zero);pending.SetVertices(vertices);pending.SetUVs(2,precisionOffsets);pending.SetNormals(normals);pending.SetUVs(0,uv);pending.SetUVs(1,roadMetrics);pending.SetColors(colours);pending.SetTriangles(indices,0);pending.RecalculateBounds();pending.UploadMeshData(true);
            if(overlay){pendingDistrict=new Mesh{name="Batched district overlay",indexFormat=IndexFormat.UInt32};pendingDistrict.SetVertices(districtVertices);pendingDistrict.SetNormals(districtNormals);pendingDistrict.SetUVs(0,districtUv);pendingDistrict.SetColors(districtColours);pendingDistrict.SetTriangles(districtIndices,0);pendingDistrict.RecalculateBounds();pendingDistrict.UploadMeshData(true);}
        }
        void AddLandTile(SettlementLandTile tile,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            int steps=4,start=vertices.Count;
            for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++){double u=x/(double)steps,v=y/(double)steps;var direction=world.Base.Grid.PointPrecise(tile.cell.face,tile.cell.x+u,tile.cell.y+v);var precise=surface.ProjectToSurface(direction,.16);var high=precise.Float;vertices.Add(high);while(precisionOffsets.Count<vertices.Count-1)precisionOffsets.Add(Vector3.zero);precisionOffsets.Add((precise-new Double3(high)).Float);normals.Add(direction.Float);uv.Add(new Vector4((float)u,(float)v,tile.kind==SettlementLandKind.Park?11:6,0));colours.Add(tile.kind==SettlementLandKind.Park?new Color(.35f,.55f,.22f,1):new Color(.35f,.26f,.14f,1));roadMetrics.Add(Vector2.one);}
            for(int y=0;y<steps;y++)for(int x=0;x<steps;x++){int a=start+y*(steps+1)+x;indices.Add(a);indices.Add(a+steps+1);indices.Add(a+1);indices.Add(a+1);indices.Add(a+steps+1);indices.Add(a+steps+2);}
        }
        void AddRaiderCamp(RaiderCamp camp,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var up=CubeSphere.Decode(camp.position);var east=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var north=Vector3.Cross(up,east);
            for(int tent=0;tent<3;tent++){
                var direction=(up+(east*((tent-1)*16)+north*(tent%2*14))/world.definition.radius).normalized;var centre=surface.GetSurfacePositionPrecise(direction,.15).Float;
                var a=centre-east*5-north*7;var b=centre+east*5-north*7;var c=centre+east*5+north*7;var d=centre-east*5+north*7;var ridgeA=centre-north*7+up*5;var ridgeB=centre+north*7+up*5;
                Quad(a,d,ridgeB,ridgeA);Quad(ridgeA,ridgeB,c,b);Triangle(a,ridgeA,b);Triangle(d,c,ridgeB);
            }
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
            void Triangle(Vector3 a,Vector3 b,Vector3 c){int start=vertices.Count;var normal=Vector3.Cross(b-a,c-a).normalized;vertices.Add(a);vertices.Add(b);vertices.Add(c);for(int i=0;i<3;i++){normals.Add(normal);uv.Add(new Vector4(0,0,10,0));colours.Add(new Color(.38f,.22f,.1f));roadMetrics.Add(Vector2.one);}indices.Add(start);indices.Add(start+1);indices.Add(start+2);}
        }
        void AddPipe(Vector3 a,Vector3 b,Color colour,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var up=(a+b).normalized;var side=Vector3.Cross(up,b-a).normalized;
            // Parallel roadside oil (slate) and gas (yellow) pipes, above the ground.
            float offset=world.Base.Grid.CarriagewayWidth*.5f+(colour.r>.5f?2.7f:1.5f);
            a=(a+side*(offset/world.definition.radius)).normalized;b=(b+side*(offset/world.definition.radius)).normalized;
            var pa=surface.GetSurfacePositionPrecise(a,.9).Float;var pb=surface.GetSurfacePositionPrecise(b,.9).Float;
            for(int face=0;face<4;face++){float t=face*Mathf.PI/2,t1=(face+1)*Mathf.PI/2;
                var n0=side*Mathf.Cos(t)+up*Mathf.Sin(t);var n1=side*Mathf.Cos(t1)+up*Mathf.Sin(t1);int start=vertices.Count;
                vertices.Add(pa+n0*.4f);vertices.Add(pb+n0*.4f);vertices.Add(pb+n1*.4f);vertices.Add(pa+n1*.4f);
                for(int k=0;k<4;k++){normals.Add((n0+n1).normalized);uv.Add(new Vector4(0,0,10,0));colours.Add(colour);roadMetrics.Add(Vector2.one);}
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
        }
        void AddParcel(RtsBuilding building,int layer,float growth,Color colour,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var grid=world.Base.Grid;var area=world.Base.Area(building);float foundation=world.Base.FoundationRadius(building)+(world.Base.IsModularBuilding(building)?-.04f:.02f);float span=Mathf.Sqrt(8*foundation*.01f);int nx=layer==6||world.Base.LegacyCityPresentation?Math.Max(2,area.width*2):Math.Max(2,Mathf.CeilToInt(area.width*(world.Base.Grid.TileSize)/span)),ny=layer==6||world.Base.LegacyCityPresentation?Math.Max(2,area.depth*2):Math.Max(2,Mathf.CeilToInt(area.depth*(world.Base.Grid.TileSize)/span)),start=vertices.Count;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){float u=x/(float)nx,v=y/(float)ny;var exact=grid.PointPrecise(area,u,v);var d=exact.Float;var precise=layer==6?surface.ProjectToSurface(exact,.15):world.Base.PlotPointPrecise(building,exact,world.Base.IsModularBuilding(building)?-.04:.02);var high=precise.Float;vertices.Add(high);while(precisionOffsets.Count<vertices.Count-1)precisionOffsets.Add(Vector3.zero);precisionOffsets.Add((precise-new Double3(high)).Float);normals.Add(d);Vector2 texture=area.rotation==0?new Vector2(u,v):area.rotation==1?new Vector2(v,1-u):area.rotation==2?new Vector2(1-u,1-v):new Vector2(1-v,u);uv.Add(new Vector4(texture.x,texture.y,layer,growth));colours.Add(colour);}
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int a=start+y*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;indices.Add(a);indices.Add(c);indices.Add(b);indices.Add(b);indices.Add(c);indices.Add(d);}
            if(layer==6)return;
            // Close the raised foundation down to the real terrain on every edge.
            for(int x=0;x<nx;x++){Skirt(start+x,start+x+1);Skirt(start+ny*(nx+1)+x+1,start+ny*(nx+1)+x);}
            for(int y=0;y<ny;y++){Skirt(start+(y+1)*(nx+1),start+y*(nx+1));Skirt(start+y*(nx+1)+nx,start+(y+1)*(nx+1)+nx);}
            if(world.Base.PlotEntrancePrecise(building,out var pa,out var pb,out var pc,out var pd))QuadPrecise(pa,pb,pc,pd);
            void Skirt(int a,int b){var pa=new Double3(vertices[a])+new Double3(a<precisionOffsets.Count?precisionOffsets[a]:Vector3.zero);var pb=new Double3(vertices[b])+new Double3(b<precisionOffsets.Count?precisionOffsets[b]:Vector3.zero);QuadPrecise(pa,pb,surface.ProjectToSurface(pb,-.25),surface.ProjectToSurface(pa,-.25));}
            void QuadPrecise(Double3 a,Double3 b,Double3 c,Double3 d){int start=vertices.Count;Quad(a.Float,b.Float,c.Float,d.Float);while(precisionOffsets.Count<vertices.Count)precisionOffsets.Add(Vector3.zero);var normal=Vector3.Cross((b-a).Float,(d-a).Float).normalized;if(normal.sqrMagnitude<.1f)normal=PlanetPrecisionPolicy.Direction(a);for(int i=0;i<4;i++){var point=i==0?a:i==1?b:i==2?c:d;precisionOffsets[start+i]=(point-new Double3(vertices[start+i])).Float;normals[start+i]=normal;}}
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){int at=vertices.Count;var n=Vector3.Cross(b-a,d-a).normalized;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);for(int k=0;k<4;k++){normals.Add(n);uv.Add(new Vector4(k==1||k==2?1:0,k>=2?1:0,8,0));colours.Add(colour);}indices.Add(at);indices.Add(at+1);indices.Add(at+2);indices.Add(at);indices.Add(at+2);indices.Add(at+3);}
        }
        void AddDistrict(RtsBuilding b,List<Vector3> vertices,List<Vector3> normals,List<Vector4> uv,List<Color> colours,List<int> indices){
            var area=world.Base.Area(b);int width=area.width+4,depth=area.depth+4,start=vertices.Count;var colour=SettlementDistricts.Colour(b.definition.district);
            for(int y=0;y<=depth;y++)for(int x=0;x<=width;x++){var d=world.Base.Grid.Point(area.face,area.x-2+x,area.y-2+y);vertices.Add(surface.GetSurfacePositionPrecise(d,.65).Float);normals.Add(d);uv.Add(new Vector4(x/(float)width,y/(float)depth,0,0));colours.Add(colour);}
            for(int y=0;y<depth;y++)for(int x=0;x<width;x++){int a=start+y*(width+1)+x;indices.Add(a);indices.Add(a+width+1);indices.Add(a+1);indices.Add(a+1);indices.Add(a+width+1);indices.Add(a+width+2);}
        }
        public void Draw(CommandBuffer commands){
            fields.Draw(commands);var vp=GL.GetGPUProjectionMatrix(world.View.projectionMatrix,true)*world.View.worldToCameraMatrix;
            var centreExact=world.RenderToBody(Vector3.zero)*-1;var centreHigh=centreExact.Float;var centreLow=(centreExact-new Double3(centreHigh)).Float;
            land.SetVector("_SettlementCenterHigh",centreHigh);land.SetVector("_SettlementCenterLow",centreLow);district.SetVector("_SettlementCenterHigh",centreHigh);district.SetVector("_SettlementCenterLow",centreLow);
            citizenMaterial.SetVector("_SettlementCenterHigh",centreHigh);citizenMaterial.SetVector("_SettlementCenterLow",centreLow);cargo.SetVector("_SettlementCenterHigh",centreHigh);cargo.SetVector("_SettlementCenterLow",centreLow);furniture.Draw(commands,vp);land.SetMatrix("_SettlementVP",vp);land.SetVector("_SettlementCenter",world.PlanetRenderCentre);land.SetVector("_SettlementCamera",world.View.transform.position);roads.Draw(commands,land);if(mesh!=null)commands.DrawMesh(mesh,Matrix4x4.identity,land);
            if(world.Base.DistrictOverlay&&districtMesh!=null){district.SetMatrix("_SettlementVP",vp);district.SetVector("_SettlementCenter",world.PlanetRenderCentre);commands.DrawMesh(districtMesh,Matrix4x4.identity,district);}
            if(citizens.Poses!=null&&citizens.Count>0){citizenMaterial.SetBuffer("_CargoPoses",citizens.Poses);citizenMaterial.SetBuffer("_CitizenVisible",citizens.Visible);citizenMaterial.SetVector("_SettlementCenter",world.PlanetRenderCentre);citizenMaterial.SetMatrix("_SettlementVP",vp);citizenMaterial.SetVector("_SettlementCamera",world.View.transform.position);commands.DrawProceduralIndirect(Matrix4x4.identity,citizenMaterial,0,MeshTopology.Triangles,citizens.DrawArguments,0);}
            cargo.SetVector("_SettlementCenter",world.PlanetRenderCentre);cargo.SetMatrix("_SettlementVP",vp);cargo.SetVector("_SettlementCamera",world.View.transform.position);if(cargoCount>0)commands.DrawProcedural(Matrix4x4.identity,cargo,0,MeshTopology.Triangles,144,cargoCount);
        }
        static void ReleaseScratch<T>(List<T> values){values.Clear();values.TrimExcess();}
        internal void ReleaseDormantResources(){
            (rebuild as IDisposable)?.Dispose();rebuild=null;
            ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(pending);ArmyLifetime.Destroy(districtMesh);ArmyLifetime.Destroy(pendingDistrict);mesh=pending=districtMesh=pendingDistrict=null;
            ReleaseScratch(precisionOffsets);ReleaseScratch(roadMetrics);ReleaseScratch(vertices);ReleaseScratch(normals);ReleaseScratch(uv);ReleaseScratch(colours);ReleaseScratch(indices);ReleaseScratch(districtVertices);ReleaseScratch(districtNormals);ReleaseScratch(districtUv);ReleaseScratch(districtColours);ReleaseScratch(districtIndices);ReleaseScratch(buildingSnapshot);ReleaseScratch(landSnapshot);ReleaseScratch(pipeSnapshot);drawnPipes.Clear();drawnPipes.TrimExcess();furniture.ReleaseMeshes();roads.Release();fields.Release();navigation.ReleaseSearchCache();citizens.ReleaseFlowCache();
            fieldSignature=terrainRevision=pipeRevision=-1;nextFields=0;cargoCount=0;
        }
        public void Dispose(){roads.Dispose();fields.Dispose();furniture.Dispose();citizens.Dispose();ArmyLifetime.Destroy(citizenMaterial);navigation.Dispose();ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(pending);ArmyLifetime.Destroy(land);ArmyLifetime.Destroy(cargo);ArmyLifetime.Destroy(district);ArmyLifetime.Destroy(districtMesh);ArmyLifetime.Destroy(pendingDistrict);}
    }
}

