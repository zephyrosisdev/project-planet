using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassiveArmy
{
    /// <summary>One seed produces render heights, independent navigation metadata and strategic sites.</summary>
    public sealed class Battlefield : IDisposable
    {
        public readonly int Resolution;
        public readonly float Size;
        public readonly float MaximumHeight;
        public readonly Texture2D Terrain, Splat, Minimap, BiomeMap, LayersA, LayersB, WaterMap;
        public readonly WorldMap Map;
        public readonly DeploymentMap Deployment;
        public readonly GraphicsBuffer Costs,StructureCells;
        public readonly uint[] StructureCellData;
        public readonly int StructureResolution,CostTeams;
        public readonly Vector2[] StrategicSites;
        readonly Vector2[] settingsStarts;
        readonly float[] heights;
        readonly Color[] metadata;
        public struct Crater {public Vector2 position;public float width,angle;public bool ignoreFog;}
        readonly List<Crater> craterHistory=new List<Crater>();
        readonly Dictionary<int,List<int>> craterCells=new Dictionary<int,List<int>>();
        readonly List<int> craterQueryCells=new List<int>();
        public IReadOnlyList<Crater> Craters=>craterHistory;
        readonly List<Crater> queuedCraters=new List<Crater>(32);
        float[] cuts,rims,dynamicCosts;Texture2D craterHeight;
        public int TerrainRevision {get;private set;}
        public event Action<Bounds> TerrainChanged;
        readonly GameObject root;
        readonly Material material;
        int combatLayerMask=-1;
        readonly TerrainNode tree;
        readonly Mesh waterMesh;
        readonly Material waterMaterial;
        float lastOrthographicSize=-1;
        Vector3 lastCamera = new Vector3(float.MaxValue, 0, 0);
        public const float SeaLevel = 0;

        public Battlefield(BattleSettings settings, Transform parent)
        {
            // Verify actual usable capacity, not just nominal map area. An unsuitable size fails before allocating GPU resources.
            Deployment=DeploymentMap.Create(settings,out var slots);Map=Deployment.Map;Resolution=Map.Resolution;Size=Map.Size;
            InitialSlots=slots;settingsStarts=settings.preview?.Starts;heights=(float[])Map.Height.Clone();
            metadata=new Color[heights.Length];var splat=new Color[heights.Length];var costs=Deployment.Costs;
            var overview=new Color[heights.Length];var biomes=new Color[heights.Length];var a=new Color[heights.Length];var b=new Color[heights.Length];var water=new Color[heights.Length];
            for(int i=0;i<heights.Length;i++)
            {
                MaximumHeight=Mathf.Max(MaximumHeight,heights[i]);
                float slope=Map.Slope[i],forest=Map.Forest[i],road=Map.Road[i];
                metadata[i]=new Color(heights[i],slope,forest,road);
                splat[i]=new Color(Mathf.Clamp01(slope),forest,road,Map.Water[i]>0?1:0);
                Map.SurfaceLayers(i,out a[i],out b[i]);water[i]=Map.WaterData(i);
                biomes[i]=Map.MapColour(i);overview[i]=biomes[i]*(.65f+.35f*Mathf.Clamp01(heights[i]/200));overview[i].a=1;
                if(road>.25f)overview[i]=Color.Lerp(overview[i],new Color(.6f,.58f,.48f),road*.65f);
            }
            Terrain=Texture("Shared height slope forest road",metadata,TextureFormat.RGBAFloat);
            Splat=Texture("Terrain slope forest road water",splat,TextureFormat.RGBA32);
            Minimap=Texture("Strategic terrain overview",overview,TextureFormat.RGBA32,false);
            BiomeMap=Texture("Climate biome regions",biomes,TextureFormat.RGBA32,false);BiomeMap.filterMode=FilterMode.Point;
            LayersA=Texture("Grass sand dry grass forest floor",a,TextureFormat.RGBA32,mipmaps:true);
            LayersB=Texture("Rock snow mud gravel",b,TextureFormat.RGBA32,mipmaps:true);
            WaterMap=Texture("Hydrology surface depth kind",water,TextureFormat.RGBAFloat);
            CostTeams=settings.ZombiesEnabled?3:settings.baseBuilding?2:1;
            Costs=new GraphicsBuffer(GraphicsBuffer.Target.Structured,costs.Length*CostTeams,4){name="Movement cost layers by team"};
            for(int team=0;team<CostTeams;team++)Costs.SetData(costs,0,team*costs.Length,costs.Length);
            StructureResolution=settings.baseBuilding?BuildingGrid.Resolution(Size):0;
            StructureCellData=new uint[StructureResolution>0?StructureResolution*StructureResolution:1];
            StructureCells=new GraphicsBuffer(GraphicsBuffer.Target.Structured,StructureCellData.Length,4){name="Exact construction obstacles by team"};StructureCells.SetData(StructureCellData);
            StrategicSites=Map.Sites;
            root=new GameObject("Procedural biome battlefield");root.transform.SetParent(parent,false);
            material=new Material(Resources.Load<Shader>("MassiveArmy/Terrain")){name="Height blended biome surfaces"};
            material.SetTexture("_Terrain",Terrain);material.SetTexture("_Splat",Splat);material.SetTexture("_LayersA",LayersA);material.SetTexture("_LayersB",LayersB);material.SetFloat("_WorldSize",Size);
            material.SetFloat("_BlendSeed",Map.Seed % 100003);
            material.SetFloat("_GeographyGuided",1);
            var surfaces=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");
            if(surfaces==null)throw new InvalidOperationException("Missing terrain art. Run Massive Army/Art/Rebuild Terrain Textures.");
            surfaces.Bind(material);
            waterMaterial=new Material(Resources.Load<Shader>("MassiveArmy/InlandWater")){name="Lakes ponds and river surfaces"};
            waterMaterial.SetFloat("_WorldSize",Size);waterMesh=BuildWater();var inland=new GameObject("Inland hydrology");inland.transform.SetParent(root.transform,false);
            RepeatMesh(inland,waterMesh,waterMaterial);
            tree = new TerrainNode(this, Vector2.zero, Size, 0);
            tree.Show();
        }
        void RepeatMesh(GameObject parent,Mesh mesh,Material material)
        {
            for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){
                var image=new GameObject("World image "+x+","+z);image.transform.SetParent(parent.transform,false);image.transform.localPosition=new Vector3(x*Size,0,z*Size);
                image.AddComponent<MeshFilter>().sharedMesh=mesh;image.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
        }
        public Vector2[] InitialSlots {get;private set;}
        public Vector2 StartPosition(int team)=>settingsStarts!=null?settingsStarts[team]:StrategicSites[team*8];
        public Vector2[] TakeDeployment(int count)
        {
            var slots=InitialSlots!=null&&InitialSlots.Length==count?InitialSlots:Deployment.Slots(count,settingsStarts);
            InitialSlots=null;
            return slots??throw new InvalidOperationException("Army exceeds the connected deployment capacity.");
        }
        Texture2D Texture(string name,Color[] pixels,TextureFormat format,bool linear=true,bool mipmaps=false)
        {
            var texture=new Texture2D(Resolution,Resolution,format,mipmaps,linear){name=name,wrapMode=TextureWrapMode.Repeat,filterMode=mipmaps?FilterMode.Trilinear:FilterMode.Bilinear};
            texture.SetPixels(pixels);texture.Apply(mipmaps,name!="Shared height slope forest road");return texture;
        }
        Mesh BuildWater()
        {
            float riverWidth=Map.Configuration.riverWidth;var vertices=new List<Vector3>();var colours=new List<Color>();var indices=new List<int>();
            void Polygon(List<Vector3> polygon,Color colour) {
                if(polygon.Count<3)return;int first=vertices.Count;
                foreach(var point in polygon) { vertices.Add(point);colours.Add(colour); }
                for(int j=1;j<polygon.Count-1;j++)indices.AddRange(new[]{first,first+j,first+j+1});
            }
            void Clip(Vector3[] points,float[] depth,int a,int b,int c,Color colour) {
                var polygon=new List<Vector3>();int[] triangle={a,b,c};
                for(int k=0;k<3;k++) {
                    int from=triangle[k],to=triangle[(k+1)%3];bool inside=depth[from]>0,next=depth[to]>0;
                    if(inside)polygon.Add(points[from]);
                    if(inside!=next)polygon.Add(Vector3.Lerp(points[from],points[to],depth[from]/(depth[from]-depth[to])));
                }
                Polygon(polygon,colour);
            }
            // Clip shoreline triangles between sample centres instead of drawing square water-cell tiles.
            for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++) {
                int[] cells={Map.Index(Map.Position(x+y*Resolution)),WorldTopology.Index(x,y+1,Resolution),WorldTopology.Index(x+1,y+1,Resolution),WorldTopology.Index(x+1,y,Resolution)};
                var levels=new List<float>();
                foreach(int source in cells) {
                    if(Map.Water[source]!=2&&Map.Water[source]!=3)continue;float level=Map.WaterLevel[source];
                    if(levels.Exists(h=>Mathf.Abs(h-level)<.01f))continue;levels.Add(level);
                    var points=new Vector3[4];var depth=new float[4];
                    for(int k=0;k<4;k++) {
                        int cell=cells[k];Vector2 p=Map.Position(cells[0])+Delta(Map.Position(cells[0]),Map.Position(cell));points[k]=new Vector3(p.x,level+.12f,p.y);
                        bool same=(Map.Water[cell]==2||Map.Water[cell]==3)&&Mathf.Abs(Map.WaterLevel[cell]-level)<.01f;
                        depth[k]=same?Mathf.Max(.2f,level-Map.Height[cell]):-Mathf.Max(.2f,Mathf.Abs(Map.Height[cell]-level));
                    }
                    Color colour=Map.Water[source]==3?new Color(.045f,.19f,.16f):new Color(.025f,.16f,.24f);
                    Clip(points,depth,0,1,2,colour);Clip(points,depth,0,2,3,colour);
                }
            }
            // Continuous ribbons follow the drainage graph; rounded junctions join diagonal bends and tributaries.
            for(int i=0;i<heights.Length;i++)if(Map.RiverCentre[i]) {
                int j=Map.Downstream[i];if(j<0)continue;
                Vector2 p=Map.Position(i),q=p+Delta(p,Map.Position(j)),side=new Vector2(-(q-p).y,(q-p).x).normalized;
                float width=riverWidth*.5f,h=Map.WaterLevel[i]+.14f;
                float end=(Map.Water[j]>0?Map.WaterLevel[j]:Map.Height[j])+.14f;
                Color colour=new Color(.035f,.23f,.29f);
                Polygon(new List<Vector3>{new Vector3(p.x-side.x*width,h,p.y-side.y*width),new Vector3(q.x-side.x*width,end,q.y-side.y*width),new Vector3(q.x+side.x*width,end,q.y+side.y*width),new Vector3(p.x+side.x*width,h,p.y+side.y*width)},colour);
                var circle=new List<Vector3>();for(int k=0;k<8;k++) { float angle=-k*Mathf.PI/4;circle.Add(new Vector3(p.x+Mathf.Cos(angle)*width,h,p.y+Mathf.Sin(angle)*width)); }Polygon(circle,colour);
            }
            var mesh=new Mesh{name="Shared freshwater shorelines and river ribbons",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetColors(colours);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        float HeightCell(int x, int y) => heights[WorldTopology.Index(x,y,Resolution)];
        public float Height(Vector2 p)
        {
            Vector2 f = (p / Size + Vector2.one * .5f) * Resolution - Vector2.one * .5f;
            int x = Mathf.FloorToInt(f.x), y = Mathf.FloorToInt(f.y);
            return Mathf.Lerp(Mathf.Lerp(HeightCell(x, y), HeightCell(x + 1, y), f.x - x), Mathf.Lerp(HeightCell(x, y + 1), HeightCell(x + 1, y + 1), f.x - x), f.y - y);
        }
        public Color Metadata(Vector2 p)
        {
            return metadata[Map.Index(p)];
        }
        public float MovementCost(int movement,Vector2 p)=>(dynamicCosts??Deployment.Costs)[movement*heights.Length+Map.Index(p)];
        public void QueueCrater(Vector2 position,float width,float angle,bool ignoreFog=false)
        {
            position=Canonical(position);
            foreach(var pending in queuedCraters)if(pending.position==position&&pending.width>=width&&pending.angle==angle)return;
            var crater=new Crater{position=position,width=width,angle=angle,ignoreFog=ignoreFog};
            int id=craterHistory.Count;craterHistory.Add(crater);
            int grid=Mathf.CeilToInt(Size/256),x=Mathf.FloorToInt((position.x+Size*.5f)/256),z=Mathf.FloorToInt((position.y+Size*.5f)/256),key=x+z*grid;
            if(!craterCells.TryGetValue(key,out var list))craterCells.Add(key,list=new List<int>());list.Add(id);
            queuedCraters.Add(crater);
        }
        public void QueryCraters(Vector2 centre,float radius,List<int> result)
        {
            result.Clear();if(craterHistory.Count==0)return;craterQueryCells.Clear();int grid=Mathf.CeilToInt(Size/256);
            int x0=Mathf.FloorToInt((centre.x-radius+Size*.5f)/256),z0=Mathf.FloorToInt((centre.y-radius+Size*.5f)/256);
            int x1=Mathf.Min(x0+grid-1,Mathf.FloorToInt((centre.x+radius+Size*.5f)/256)),z1=Mathf.Min(z0+grid-1,Mathf.FloorToInt((centre.y+radius+Size*.5f)/256));
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){int key=WorldTopology.Cell(x,grid)+WorldTopology.Cell(z,grid)*grid;if(craterCells.ContainsKey(key))craterQueryCells.Add(key);}
            Vector2 CellCentre(int key)=>new Vector2((key%grid+.5f)*256-Size*.5f,(key/grid+.5f)*256-Size*.5f);
            craterQueryCells.Sort((a,b)=>Delta(centre,CellCentre(a)).sqrMagnitude.CompareTo(Delta(centre,CellCentre(b)).sqrMagnitude));
            // Detailed rendering is bounded even after years of impacts in one region.
            // Older/overflow records remain in history and the permanent terrain atlas.
            foreach(int key in craterQueryCells){var list=craterCells[key];for(int i=list.Count-1;i>=Mathf.Max(0,list.Count-64);i--){result.Add(list[i]);if(result.Count>=1024)return;}}
        }
        public void ApplyQueuedCraters(GpuNavigation navigation)
        {
            if(queuedCraters.Count==0)return;
            if(craterHeight==null)craterHeight=Resources.Load<Texture2D>("MassiveArmy/CombatArt/NukeCraterHeight");
            if(craterHeight==null)throw new InvalidOperationException("Missing readable nuke crater height map");
            if(cuts==null){cuts=new float[heights.Length];rims=new float[heights.Length];dynamicCosts=(float[])Deployment.Costs.Clone();}
            float cell=Size/Resolution;
            var regions=new List<Bounds>(32);
            using(var command=new CommandBuffer{name="Event-driven crater terrain and navigation"}){
                int batch=Mathf.Min(32,queuedCraters.Count);
                for(int craterIndex=0;craterIndex<batch;craterIndex++){var crater=queuedCraters[craterIndex];
                    float extent=crater.width*.72f,depth=Mathf.Clamp(crater.width*.045f,2,12),cos=Mathf.Cos(crater.angle),sin=Mathf.Sin(crater.angle);
                    int x0=Mathf.FloorToInt((crater.position.x-extent+Size*.5f)/cell)-1,z0=Mathf.FloorToInt((crater.position.y-extent+Size*.5f)/cell)-1;
                    int x1=Mathf.CeilToInt((crater.position.x+extent+Size*.5f)/cell)+1,z1=Mathf.CeilToInt((crater.position.y+extent+Size*.5f)/cell)+1;
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                        int i=WorldTopology.Index(x,z,Resolution);Vector2 delta=Delta(crater.position,Map.Position(i));
                        Vector2 uv=new Vector2(cos*delta.x+sin*delta.y,-sin*delta.x+cos*delta.y)/crater.width+Vector2.one*.5f;
                        if(uv.x<0||uv.y<0||uv.x>1||uv.y>1)continue;
                        float offset=craterHeight.GetPixelBilinear(uv.x,uv.y).r*depth;
                        // Union overlapping bowls instead of repeatedly deepening the same impact.
                        cuts[i]=Mathf.Min(cuts[i],offset);rims[i]=Mathf.Max(rims[i],offset);
                        heights[i]=Map.Height[i]+(cuts[i]<-.01f?cuts[i]:rims[i]);
                    }
                    var region=new Bounds(new Vector3(crater.position.x,0,crater.position.y),new Vector3(extent*2+cell*4,1000,extent*2+cell*4));
                    // Merge nearby footprints so a simultaneous volley rebuilds each affected
                    // patch and obstacle region once. Rebase seam neighbours before merging.
                    for(int i=0;i<regions.Count;i++){
                        var other=regions[i];Vector2 delta=Delta(new Vector2(region.center.x,region.center.z),new Vector2(other.center.x,other.center.z));
                        if(Mathf.Abs(delta.x)>region.extents.x+other.extents.x||Mathf.Abs(delta.y)>region.extents.z+other.extents.z)continue;
                        other.center=new Vector3(region.center.x+delta.x,0,region.center.z+delta.y);region.Encapsulate(other);Vector2 centre=Canonical(new Vector2(region.center.x,region.center.z));region.center=new Vector3(centre.x,0,centre.y);regions.RemoveAt(i);i=-1;
                    }
                    regions.Add(region);
                }
                foreach(var region in regions){
                    int x0=Mathf.FloorToInt((region.min.x+Size*.5f)/cell),z0=Mathf.FloorToInt((region.min.z+Size*.5f)/cell);
                    int x1=Mathf.Min(x0+Resolution-1,Mathf.CeilToInt((region.max.x+Size*.5f)/cell)),z1=Mathf.Min(z0+Resolution-1,Mathf.CeilToInt((region.max.z+Size*.5f)/cell));
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){
                        int i=WorldTopology.Index(x,z,Resolution);float sx=(HeightCell(x+1,z)-HeightCell(x-1,z))/(2*cell),sz=(HeightCell(x,z+1)-HeightCell(x,z-1))/(2*cell);
                        float slope=Mathf.Sqrt(sx*sx+sz*sz);metadata[i].r=heights[i];metadata[i].g=slope;
                        // Preserve original water/rock topology. The shallow profile introduces
                        // terrain cost, while buildings continue to own their obstacle layers.
                        for(int m=0;m<3;m++)if(Deployment.Costs[m*heights.Length+i]<DeploymentMap.Blocked*.5f)
                            dynamicCosts[m*heights.Length+i]=Deployment.Costs[m*heights.Length+i]+Mathf.Max(0,slope-Map.Slope[i])*2;
                    }
                    tree.Refresh(region);
                    if(TerrainChanged!=null)TerrainChanged(region);else navigation?.InvalidateLocal(command,region);
                }
                if(TerrainChanged==null)for(int team=0;team<CostTeams;team++)command.SetBufferData(Costs,dynamicCosts,0,team*dynamicCosts.Length,dynamicCosts.Length);
                Terrain.SetPixels(metadata);Terrain.Apply(false,false);TerrainRevision++;
                Graphics.ExecuteCommandBuffer(command);
            }
            queuedCraters.RemoveRange(0,Mathf.Min(32,queuedCraters.Count));
        }
        public Vector2 Canonical(Vector2 p)=>WorldTopology.Wrap(p,Size);
        public Vector2 Delta(Vector2 a,Vector2 b)=>WorldTopology.Delta(a,b,Size);
        public float Distance(Vector2 a,Vector2 b)=>WorldTopology.Distance(a,b,Size);
        public Vector3 RenderPosition(Vector3 p,Vector3 focus)=>WorldTopology.Nearest(p,focus,Size);
        public Vector2 TraversableDestination(Vector2 p)
        {
            p = Canonical(p);
            if (Deployment.Safe(p)) return p;
            // Small CPU query against immutable terrain, never mass-unit state.
            for (int ring = 1; ring < 40; ring++) for (int j = 0; j < 16; j++)
            {
                float a = j * Mathf.PI / 8; Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring * Size / Resolution;
                if (Deployment.Safe(q)) return Canonical(q);
            }
            return Vector2.zero;
        }
        public void UpdateLod(Camera camera, bool visible)
        {
            root.SetActive(visible);
            if (!visible || Vector3.Distance(lastCamera, camera.transform.position) < 30&&Mathf.Abs(lastOrthographicSize-camera.orthographicSize)<1) return;
            lastCamera = camera.transform.position;lastOrthographicSize=camera.orthographicSize;var focus=WorldTopology.ViewFocus(camera);tree.Update(focus+Vector3.up*camera.orthographicSize*1.5f);
        }
        public void BindResourceDecals(Texture index,Texture artwork,Texture normals,Vector4[] neutral,GraphicsBuffer data,int resolution,int visible)
        {
            material.SetTexture("_ResourceNodeNormals",normals);material.SetVectorArray("_ResourceNodeNormalNeutral",neutral);
            material.SetTexture("_ResourceNodeMap",index);material.SetTexture("_ResourceNodeTextures",artwork);material.SetBuffer("_ResourceNodeData",data);
            material.SetFloat("_ResourceNodeResolution",resolution);SetResourceDecalVisibility(visible);
        }
        public void SetResourceDecalVisibility(int visible)=>material.SetFloat("_ResourceNodesVisible",visible);
        public void BindCombatDecals(Texture index,Texture artwork,Texture normals,GraphicsBuffer data,GraphicsBuffer indices,GraphicsBuffer coverage,Vector4[] neutral,int resolution)
        {
            material.SetBuffer("_CombatAlphaCoverage",coverage);material.SetVectorArray("_CombatNormalNeutral",neutral);
            material.SetTexture("_CombatIndexMap",index);material.SetTexture("_CombatTextures",artwork);material.SetTexture("_CombatNormals",normals);material.SetBuffer("_CombatTileIndices",indices);material.SetBuffer("_CombatStamps",data);material.SetFloat("_CombatIndexResolution",resolution);SetCombatDecalVisibility(0,0);
        }
        public void BindCraterAtlas(Texture colour,Texture normal){material.SetTexture("_CraterAtlas",colour);material.SetTexture("_CraterAtlasNormal",normal);}
        public void ShowCraterAtlas(bool show){material.SetFloat("_CraterAtlasVisible",show?1:0);}
        public void SetCombatDecalVisibility(int visible,float clock,int craters=0){material.SetFloat("_CombatVisible",visible);material.SetFloat("_CombatTime",clock);
            int mask=(craters>0?1:0)|(visible>craters?2:0);if(mask!=combatLayerMask){combatLayerMask=mask;material.SetVector("_CombatLayersVisible",new Vector4((mask&1)!=0?1:0,(mask&2)!=0?1:0,0,0));}}
        public void Dispose() { TerrainSurfaceLibrary.Unbind(material); tree.Dispose(); Costs.Dispose();StructureCells.Dispose(); ArmyLifetime.Destroy(root); ArmyLifetime.Destroy(material); ArmyLifetime.Destroy(Terrain); ArmyLifetime.Destroy(Splat);ArmyLifetime.Destroy(Minimap);ArmyLifetime.Destroy(BiomeMap);ArmyLifetime.Destroy(LayersA);ArmyLifetime.Destroy(LayersB);ArmyLifetime.Destroy(WaterMap);ArmyLifetime.Destroy(waterMesh);ArmyLifetime.Destroy(waterMaterial); }

        sealed class TerrainNode : IDisposable
        {
            readonly Battlefield field;
            readonly Vector2 centre;
            readonly float size;
            readonly int depth;
            GameObject patch;
            Mesh mesh;
            TerrainNode[] children;
            public TerrainNode(Battlefield f, Vector2 centre, float size, int depth) { field = f; this.centre = centre; this.size = size; this.depth = depth; }
            public void Show()
            {
                if (patch == null)
                {
                    patch = new GameObject($"Terrain {depth} {centre}"); patch.transform.SetParent(field.root.transform, false);
                    mesh = BuildMesh();field.RepeatMesh(patch,mesh,field.material);
                }
                patch.SetActive(true);
            }
            Mesh BuildMesh()
            {
                const int segments = 16;
                var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
                for (int z = 0; z <= segments; z++) for (int x = 0; x <= segments; x++)
                {
                    Vector2 p = centre + new Vector2((float)x / segments - .5f, (float)z / segments - .5f) * size;
                    vertices.Add(new Vector3(p.x, field.Height(p), p.y)); uv.Add(p / field.Size + Vector2.one * .5f);
                }
                for (int z = 0; z < segments; z++) for (int x = 0; x < segments; x++)
                {
                    int i = x + z * (segments + 1);
                    indices.AddRange(new[] { i, i + segments + 1, i + 1, i + 1, i + segments + 1, i + segments + 2 });
                }
                // Skirts seal transitions between neighbours at different quadtree depths.
                for (int edge = 0; edge < 4; edge++) for (int j = 0; j < segments; j++)
                {
                    int a, b;
                    if (edge == 0) { a = j; b = j + 1; }
                    else if (edge == 1) { a = j * 17 + 16; b = (j + 1) * 17 + 16; }
                    else if (edge == 2) { a = 288 - j; b = 287 - j; }
                    else { a = (16 - j) * 17; b = (15 - j) * 17; }
                    int k = vertices.Count;
                    vertices.Add(vertices[a]); vertices.Add(vertices[b]); vertices.Add(vertices[a] - Vector3.up * 18); vertices.Add(vertices[b] - Vector3.up * 18);
                    uv.Add(uv[a]); uv.Add(uv[b]); uv.Add(uv[a]); uv.Add(uv[b]);
                    indices.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                }
                var result = new Mesh { name = "Terrain patch with skirts" };
                result.SetVertices(vertices); result.SetUVs(0, uv); result.SetTriangles(indices, 0); result.RecalculateNormals();var normals=result.normals;for(int i=0;i<vertices.Count;i++){var v=vertices[i];float d=field.Size/field.Resolution;normals[i]=new Vector3(field.Height(new Vector2(v.x-d,v.z))-field.Height(new Vector2(v.x+d,v.z)),2*d,field.Height(new Vector2(v.x,v.z-d))-field.Height(new Vector2(v.x,v.z+d))).normalized;}result.normals=normals;result.RecalculateBounds();
                return result;
            }
            public void Update(Vector3 camera)
            {
                float distance = Vector3.Distance(camera,field.RenderPosition(new Vector3(centre.x, field.Height(centre), centre.y),camera));
                bool split = depth < (field.Resolution>256?5:4) && distance < size * 1.8f;
                if (split)
                {
                    if (children == null)
                    {
                        children = new TerrainNode[4];
                        for (int i = 0; i < 4; i++) children[i] = new TerrainNode(field, centre + new Vector2(i % 2 == 0 ? -.25f : .25f, i / 2 == 0 ? -.25f : .25f) * size, size * .5f, depth + 1);
                    }
                    if (patch != null) patch.SetActive(false);
                    foreach (var child in children) { child.Show(); child.Update(camera); }
                }
                else { HideChildren(); Show(); }
            }
            public void Refresh(Bounds region)
            {
                Vector2 delta=field.Delta(new Vector2(region.center.x,region.center.z),centre);
                if(Mathf.Abs(delta.x)>region.extents.x+size*.5f||Mathf.Abs(delta.y)>region.extents.z+size*.5f)return;
                if(mesh!=null){var vertices=mesh.vertices;var normals=mesh.normals;float d=field.Size/field.Resolution;
                    for(int i=0;i<vertices.Length;i++){var v=vertices[i];v.y=field.Height(new Vector2(v.x,v.z))-(i>=289&&(i-289)%4>=2?18:0);vertices[i]=v;
                        normals[i]=new Vector3(field.Height(new Vector2(v.x-d,v.z))-field.Height(new Vector2(v.x+d,v.z)),2*d,field.Height(new Vector2(v.x,v.z-d))-field.Height(new Vector2(v.x,v.z+d))).normalized;}
                    mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();
                }
                if(children!=null)foreach(var child in children)child.Refresh(region);
            }
            void HideChildren() { if (children == null) return; foreach (var child in children) { if (child.patch != null) child.patch.SetActive(false); child.HideChildren(); } }
            public void Dispose() { if (children != null) foreach (var child in children) child.Dispose(); if (mesh != null) ArmyLifetime.Destroy(mesh); }
        }
    }
}
