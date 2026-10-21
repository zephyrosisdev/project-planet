using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    // A coarse occupancy map rejects empty terrain; sparse fine cells retain nearby deposits. Only
    // bounded resource metadata is uploaded; army state is never read by this renderer.
    public sealed class PlanetResourceDecals : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]struct Deposit{public Vector4 centre,right,forward,appearance;}
        const int Resolution=128;
        readonly Material material;readonly PlanetRtsBase world;GraphicsBuffer deposits,cells,candidates;readonly PlanetDefinition definition;readonly PlanetSurface surface;int terrainRevision;
        readonly Texture2D indices;readonly Texture2DArray colours,normals;Deposit[] data;ushort[] map;int mappedCount;double nextUpdate;
        public PlanetResourceDecals(Material material,PlanetRtsBase world,PlanetDefinition definition,Lookup lookup=null)
        {
            this.material=material;this.world=world;this.definition=definition;surface=new PlanetSurface(definition);terrainRevision=definition.Edits.Revision;data=new Deposit[Mathf.NextPowerOfTwo(Math.Max(1,world.Nodes.Count))];mappedCount=world.Nodes.Count;
            for(int i=0;i<world.Nodes.Count;i++){var node=world.Nodes[i];var up=CubeSphere.Decode(node.position);var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(up,right);var dimensions=BuildingGrid.ResourceDimensions(node.kind,world.Catalog);
                data[i]=new Deposit{centre=new Vector4(up.x,up.y,up.z,surface.SampleHeight(up)),right=new Vector4(right.x,right.y,right.z,dimensions.x*BuildingGrid.TileSize),forward=new Vector4(forward.x,forward.y,forward.z,dimensions.y*BuildingGrid.TileSize),appearance=new Vector4((int)AppearanceKind(node),1,0,0)};}
            if(lookup==null){lookup=new Lookup();var prepare=lookup.Prepare(world,definition);while(prepare.MoveNext()){} }map=lookup.map;UploadLookup(lookup);
            indices=new Texture2D(Resolution*6,Resolution,TextureFormat.R16,false,true){name="Planet resource face lookup",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};indices.SetPixelData(map,0);indices.Apply(false,false);
            colours=Array("Albedo",false);normals=Array("Normal",true);deposits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Length,64);deposits.SetData(data);
            material.SetBuffer("_PlanetDeposits",deposits);material.SetTexture("_PlanetDepositIndex",indices);material.SetTexture("_ResourceNodeTextures",colours);material.SetTexture("_ResourceNodeNormals",normals);material.SetInt("_PlanetDepositResolution",Resolution);material.SetVectorArray("_ResourceNodeNormalNeutral",Resources.Load<CombatSurfaceLibrary>("MassiveArmy/ResourceDecals/SurfaceLibrary").normalNeutral);world.Natural.Bind(material);material.EnableKeyword("PLANET_RESOURCE_DECALS");
        }
        [StructLayout(LayoutKind.Sequential)]internal struct CellSlot{public uint key,start,count,padding;}
        public sealed class Lookup
        {
            internal ushort[] map;internal CellSlot[] slots;internal uint[] nodes;internal int fineResolution;
            static uint Hash(uint key)=>unchecked(key*2654435761u);
            internal static uint Key(PlanetNavCoordinate cell,int resolution)=>(uint)(((int)cell.face*resolution+cell.y)*resolution+cell.x+1);
            internal bool Contains(Vector3 direction,int node){uint key=Key(CubeSphere.Cell(0,direction,fineResolution),fineResolution);int mask=slots.Length-1,at=(int)(Hash(key)&(uint)mask);for(int probe=0;probe<16;probe++,at=(at+1)&mask){var slot=slots[at];if(slot.key==0)return false;if(slot.key!=key)continue;for(uint i=0;i<slot.count;i++)if(nodes[slot.start+i]==node)return true;return false;}return false;}
            public System.Collections.IEnumerator Prepare(PlanetRtsBase world,PlanetDefinition definition){
                fineResolution=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(definition.radius/8)),Resolution,16384);
                map=new ushort[6*Resolution*Resolution];var buckets=new Dictionary<uint,List<int>>();
                for(int i=0;i<world.Nodes.Count;i++){
                    var node=world.Nodes[i];var up=CubeSphere.Decode(node.position);var dimensions=(Vector2)BuildingGrid.ResourceDimensions(node.kind,world.Catalog)*BuildingGrid.TileSize;
                    int reach=Mathf.CeilToInt(dimensions.magnitude*.5f*fineResolution*1.75f/definition.radius)+1;var centre=CubeSphere.Cell(0,up,fineResolution);
                    for(int y=-reach;y<=reach;y++)for(int x=-reach;x<=reach;x++){
                        var cell=centre;for(int step=0;step<Math.Abs(x);step++)cell=CubeSphere.Neighbour(cell,Math.Sign(x),0,fineResolution);for(int step=0;step<Math.Abs(y);step++)cell=CubeSphere.Neighbour(cell,0,Math.Sign(y),fineResolution);
                        uint key=Key(cell,fineResolution);if(!buckets.TryGetValue(key,out var list)){list=new List<int>();buckets.Add(key,list);}if(list.Count==0||list[list.Count-1]!=i)list.Add(i);
                        int scale=fineResolution/Resolution;map[(int)cell.face*Resolution+cell.x/scale+(cell.y/scale)*Resolution*6]=1;
                    }
                    if(i%16==15)yield return null;
                }
                var flattened=new List<uint>();var packed=new List<CellSlot>();foreach(var pair in buckets){uint start=(uint)flattened.Count;foreach(int node in pair.Value)flattened.Add((uint)node);packed.Add(new CellSlot{key=pair.Key,start=start,count=(uint)pair.Value.Count});}
                nodes=flattened.Count==0?new uint[1]:flattened.ToArray();int capacity=Mathf.NextPowerOfTwo(Math.Max(16,buckets.Count*2));
                // Match the shader's bounded probe count; grow instead of losing a crowded hash entry.
                while(true){slots=new CellSlot[capacity];bool fits=true;foreach(var slot in packed){int at=(int)(Hash(slot.key)&(uint)(capacity-1)),probe=0;while(slots[at].key!=0&&probe<16){at=(at+1)&(capacity-1);probe++;}if(probe==16){fits=false;break;}slots[at]=slot;}if(fits)break;capacity*=2;}
            }
        }
        void UploadLookup(Lookup lookup){
            cells?.Dispose();candidates?.Dispose();cells=new GraphicsBuffer(GraphicsBuffer.Target.Structured,lookup.slots.Length,16);cells.SetData(lookup.slots);candidates=new GraphicsBuffer(GraphicsBuffer.Target.Structured,lookup.nodes.Length,4);candidates.SetData(lookup.nodes);
            material.SetBuffer("_PlanetDepositCells",cells);material.SetBuffer("_PlanetDepositCandidates",candidates);material.SetInt("_PlanetDepositCellMask",lookup.slots.Length-1);material.SetInt("_PlanetDepositFineResolution",lookup.fineResolution);
        }
        internal static NodeKind AppearanceKind(ResourceNode node)=>node.elementId=="uranium"?NodeKind.Uranium:node.elementId=="rare_earth"?NodeKind.RareEarth:node.elementId=="crude_oil"?NodeKind.Oil:node.elementId=="methane"?NodeKind.Gas:node.kind;
        static Texture2DArray Array(string suffix,bool linear){var first=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/Metal"+suffix);if(first==null)throw new InvalidOperationException("Missing original resource decal textures");
            var array=new Texture2DArray(first.width,first.height,7,first.format,true,linear){name="Planet original resource "+suffix,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=2};array.Apply(false,true);
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind))){var source=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+suffix);if(source==null||source.format!=first.format||source.width!=first.width||source.mipmapCount!=first.mipmapCount)throw new InvalidOperationException("Resource decal import mismatch: "+kind);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,array,(int)kind,mip);}return array;
        }
        void AddDiscovered(){
            if(world.Nodes.Count<=mappedCount)return;
            if(world.Nodes.Count>data.Length){System.Array.Resize(ref data,Mathf.NextPowerOfTwo(world.Nodes.Count));deposits.Dispose();deposits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Length,64);material.SetBuffer("_PlanetDeposits",deposits);}
            for(int i=mappedCount;i<world.Nodes.Count;i++){var node=world.Nodes[i];var up=CubeSphere.Decode(node.position);var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(up,right);var dimensions=BuildingGrid.ResourceDimensions(node.kind,world.Catalog);
                data[i]=new Deposit{centre=new Vector4(up.x,up.y,up.z,surface.SampleHeight(up)),right=new Vector4(right.x,right.y,right.z,dimensions.x*BuildingGrid.TileSize),forward=new Vector4(forward.x,forward.y,forward.z,dimensions.y*BuildingGrid.TileSize),appearance=new Vector4((int)AppearanceKind(node),node.remaining>0?1:0,0,0)};
            }
            var lookup=new Lookup();var prepare=lookup.Prepare(world,definition);while(prepare.MoveNext()){}map=lookup.map;UploadLookup(lookup);
            mappedCount=world.Nodes.Count;indices.SetPixelData(map,0);indices.Apply(false,false);deposits.SetData(data);
        }
        public void Update(){if(Time.realtimeSinceStartupAsDouble<nextUpdate)return;nextUpdate=Time.realtimeSinceStartupAsDouble+.25;world.Natural.Update();AddDiscovered();bool terrainChanged=terrainRevision!=definition.Edits.Revision;terrainRevision=definition.Edits.Revision;bool changed=terrainChanged;for(int i=0;i<world.Nodes.Count;i++){if(terrainChanged)data[i].centre.w=surface.SampleHeight(CubeSphere.Decode(world.Nodes[i].position));float visible=world.Nodes[i].remaining>0?1:0;if(data[i].appearance.y!=visible){data[i].appearance.y=visible;changed=true;}}if(changed)deposits.SetData(data);}
        public void Dispose(){material.DisableKeyword("PLANET_RESOURCE_DECALS");deposits.Dispose();cells.Dispose();candidates.Dispose();ArmyLifetime.Destroy(indices);ArmyLifetime.Destroy(colours);ArmyLifetime.Destroy(normals);}
    }
}
