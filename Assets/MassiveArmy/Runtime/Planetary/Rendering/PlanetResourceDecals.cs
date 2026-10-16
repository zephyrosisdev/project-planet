using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    // One atlas lookup per terrain pixel, just as in ResourceNodeDecals. Only
    // bounded resource metadata is uploaded; army state is never read by this renderer.
    public sealed class PlanetResourceDecals : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]struct Deposit{public Vector4 centre,right,forward,appearance;}
        const int Resolution=128;
        readonly Material material;readonly PlanetRtsBase world;readonly GraphicsBuffer deposits;
        readonly Texture2D indices;readonly Texture2DArray colours,normals;readonly Deposit[] data;double nextUpdate;
        public PlanetResourceDecals(Material material,PlanetRtsBase world,PlanetDefinition definition,Lookup lookup=null)
        {
            this.material=material;this.world=world;data=new Deposit[Math.Max(1,world.Nodes.Count)];
            for(int i=0;i<world.Nodes.Count;i++){var node=world.Nodes[i];var up=CubeSphere.Decode(node.position);var right=Vector3.Cross(Mathf.Abs(up.y)>.95f?Vector3.right:Vector3.up,up).normalized;var forward=Vector3.Cross(up,right);var dimensions=BuildingGrid.ResourceDimensions(node.kind,world.Catalog);
                data[i]=new Deposit{centre=new Vector4(up.x,up.y,up.z,0),right=new Vector4(right.x,right.y,right.z,dimensions.x*BuildingGrid.TileSize),forward=new Vector4(forward.x,forward.y,forward.z,dimensions.y*BuildingGrid.TileSize),appearance=new Vector4((int)node.kind,1,0,0)};}
            if(lookup==null){lookup=new Lookup();var prepare=lookup.Prepare(world,definition);while(prepare.MoveNext()){} }var map=lookup.map;
            indices=new Texture2D(Resolution*6,Resolution,TextureFormat.R16,false,true){name="Planet resource face lookup",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};indices.SetPixelData(map,0);indices.Apply(false,true);
            colours=Array("Albedo",false);normals=Array("Normal",true);deposits=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Length,64);deposits.SetData(data);
            material.SetBuffer("_PlanetDeposits",deposits);material.SetTexture("_PlanetDepositIndex",indices);material.SetTexture("_ResourceNodeTextures",colours);material.SetTexture("_ResourceNodeNormals",normals);material.SetInt("_PlanetDepositResolution",Resolution);material.SetVectorArray("_ResourceNodeNormalNeutral",Resources.Load<CombatSurfaceLibrary>("MassiveArmy/ResourceDecals/SurfaceLibrary").normalNeutral);material.EnableKeyword("PLANET_RESOURCE_DECALS");
        }
        public sealed class Lookup
        {
            internal ushort[] map;
            public System.Collections.IEnumerator Prepare(PlanetRtsBase world,PlanetDefinition definition){
                var centres=new Vector3[world.Nodes.Count];var radii=new float[world.Nodes.Count];
                for(int i=0;i<centres.Length;i++){var node=world.Nodes[i];centres[i]=CubeSphere.Decode(node.position);var size=(Vector2)BuildingGrid.ResourceDimensions(node.kind,world.Catalog)*BuildingGrid.TileSize;radii[i]=Mathf.Sqrt(size.x*size.x+size.y*size.y)*.5f+definition.radius*3/Resolution;}
                map=new ushort[6*Resolution*Resolution];int work=0;
                for(int face=0;face<6;face++)for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++){
                    var d=CubeSphere.Direction(new PlanetNavCoordinate(definition.bodyId,(CubeFace)face,x,y),Resolution);float best=float.MaxValue;int index=0;
                    for(int i=0;i<centres.Length;i++){float distance=(d-centres[i]).sqrMagnitude*definition.radius*definition.radius;if(distance<radii[i]*radii[i]&&distance<best){index=i+1;best=distance;}}
                    map[face*Resolution+x+y*Resolution*6]=(ushort)index;if(++work%512==0)yield return null;
                }
            }
        }
        static Texture2DArray Array(string suffix,bool linear){var first=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/Metal"+suffix);if(first==null)throw new InvalidOperationException("Missing original resource decal textures");
            var array=new Texture2DArray(first.width,first.height,7,first.format,true,linear){name="Planet original resource "+suffix,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=2};array.Apply(false,true);
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind))){var source=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+suffix);if(source==null||source.format!=first.format||source.width!=first.width||source.mipmapCount!=first.mipmapCount)throw new InvalidOperationException("Resource decal import mismatch: "+kind);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,array,(int)kind,mip);}return array;
        }
        public void Update(){if(Time.realtimeSinceStartupAsDouble<nextUpdate)return;nextUpdate=Time.realtimeSinceStartupAsDouble+.25;bool changed=false;for(int i=0;i<world.Nodes.Count;i++){float visible=world.Nodes[i].remaining>0?1:0;if(data[i].appearance.y!=visible){data[i].appearance.y=visible;changed=true;}}if(changed)deposits.SetData(data);}
        public void Dispose(){material.DisableKeyword("PLANET_RESOURCE_DECALS");deposits.Dispose();ArmyLifetime.Destroy(indices);ArmyLifetime.Destroy(colours);ArmyLifetime.Destroy(normals);}
    }
}
