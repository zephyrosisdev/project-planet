using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassiveArmy
{
    // Bounded infrastructure data only. Army state remains entirely on the GPU.
    public sealed class ResourceNodeDecals : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Deposit { public Vector4 positionSize,appearance; }
        readonly RtsWorld world;
        readonly ResourceNode[] nodes;
        readonly Deposit[] deposits;
        readonly Bounds[] bounds;
        readonly Plane[] planes=new Plane[6];
        readonly GraphicsBuffer buffer;
        readonly Texture2D indexMap;
        readonly Texture2DArray textures,normals;
        bool uploaded;
        public int VisibleCount {get;private set;}
        internal bool Visible(int id){for(int i=0;i<nodes.Length;i++)if(nodes[i].id==id)return deposits[i].appearance.y>.5f;return false;}
        public ResourceNodeDecals(RtsWorld world)
        {
            this.world=world;nodes=world.Nodes.ToArray();deposits=new Deposit[Math.Max(1,nodes.Length)];bounds=new Bounds[nodes.Length];
            int resolution=BuildingGrid.Resolution(world.Field.Size);var indices=new ushort[resolution*resolution];
            for(int i=0;i<nodes.Length;i++){
                var n=nodes[i];var size=BuildingGrid.ResourceDimensions(n.kind,world.Catalog);var metres=new Vector2(size.x,size.y)*BuildingGrid.TileSize;
                deposits[i].positionSize=new Vector4(n.position.x,n.position.y,metres.x,metres.y);deposits[i].appearance.x=(int)n.kind;
                float min=float.MaxValue,max=float.MinValue;
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){float height=world.Field.Height(n.position+Vector2.Scale(new Vector2(x,z),metres)*.5f);min=Mathf.Min(min,height);max=Mathf.Max(max,height);}
                bounds[i]=new Bounds(new Vector3(n.position.x,(min+max)*.5f,n.position.y),new Vector3(metres.x,max-min+32,metres.y));
                var lo=(n.position-metres*.5f+Vector2.one*(world.Field.Size*.5f))/BuildingGrid.TileSize;
                var hi=(n.position+metres*.5f+Vector2.one*(world.Field.Size*.5f))/BuildingGrid.TileSize;
                for(int z=Mathf.FloorToInt(lo.y);z<=Mathf.FloorToInt(hi.y);z++)for(int x=Mathf.FloorToInt(lo.x);x<=Mathf.FloorToInt(hi.x);x++){
                    int cell=WorldTopology.Index(x,z,resolution);
                    if(indices[cell]!=0&&indices[cell]!=i+1)throw new InvalidOperationException("Overlapping resource decal footprints");
                    indices[cell]=(ushort)(i+1);
                }
            }
            indexMap=new Texture2D(resolution,resolution,TextureFormat.R16,false,true){name="Resource deposit grid index",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
            indexMap.SetPixelData(indices,0);indexMap.Apply(false,true);
            var first=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/MetalAlbedo");
            if(first==null)throw new InvalidOperationException("Missing resource decal artwork");
            textures=new Texture2DArray(first.width,first.height,7,first.format,true,false){name="Resource deposit artwork",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=2};
            textures.Apply(false,true);
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind))){
                var source=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+"Albedo");
                if(source==null||source.width!=first.width||source.height!=first.height||source.format!=first.format||source.mipmapCount!=first.mipmapCount)throw new InvalidOperationException("Resource decal import mismatch: "+kind);
                for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,textures,(int)kind,mip);
            }
            var firstNormal=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/MetalNormal");
            normals=new Texture2DArray(firstNormal.width,firstNormal.height,7,firstNormal.format,true,true){name="Resource deposit tangent normals",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=2};normals.Apply(false,true);
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind))){var source=Resources.Load<Texture2D>("MassiveArmy/ResourceDecals/"+kind+"Normal");if(source==null||source.width!=firstNormal.width||source.height!=firstNormal.height||source.format!=firstNormal.format)throw new InvalidOperationException("Resource normal import mismatch: "+kind);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,normals,(int)kind,mip);}
            buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,deposits.Length,Marshal.SizeOf<Deposit>());buffer.SetData(deposits);
            world.Field.BindResourceDecals(indexMap,textures,normals,Resources.Load<CombatSurfaceLibrary>("MassiveArmy/ResourceDecals/SurfaceLibrary").normalNeutral,buffer,resolution,0);
        }
        public void Update(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera,planes);var focus=WorldTopology.ViewFocus(camera);bool changed=!uploaded;int visible=0;
            for(int i=0;i<nodes.Length;i++){
                var n=nodes[i];var image=bounds[i];image.center=world.Field.RenderPosition(image.center,focus);
                bool show=n.remaining>0&&GeometryUtility.TestPlanesAABB(planes,image)&&world.Battle.IsVisible(n.position,0,true);
                float flag=show?1:0;if(flag!=deposits[i].appearance.y){deposits[i].appearance.y=flag;changed=true;}if(show)visible++;
            }
            VisibleCount=visible;
            if(changed){buffer.SetData(deposits);uploaded=true;world.Field.SetResourceDecalVisibility(visible);}
        }
        public void Dispose(){world.Field.SetResourceDecalVisibility(0);buffer?.Dispose();ArmyLifetime.Destroy(indexMap);ArmyLifetime.Destroy(textures);ArmyLifetime.Destroy(normals);}
    }
}
