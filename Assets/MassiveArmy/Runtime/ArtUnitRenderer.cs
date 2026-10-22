using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassiveArmy
{
    /// <summary>One shared vertex bank per LOD, indexed on the GPU by catalogue type. No unit objects.</summary>
    public sealed class ArtUnitRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Vertex {public Vector4 position,normal,tangent,uv;}
        public const int NearTriangles=512,FarTriangles=128;
        readonly GraphicsBuffer near,far,bounds;
        public readonly Mesh NearMesh,FarMesh;
        readonly RtsModelArt art;
        public ArtUnitRenderer(bool zombies=true,RtsModelArt catalogue=null)
        {
            art=catalogue??RtsModelArt.Load();
            if(art==null||art.unitIds.Length!=31)throw new InvalidOperationException("Complete model art catalogue is required for base-building rendering");
            near=Bank(art.unitNear,NearTriangles,zombies);far=Bank(art.unitFar,FarTriangles,zombies);
            var sizes=new Vector4[art.unitBounds.Length+(zombies?1:0)];Array.Copy(art.unitBounds,sizes,art.unitBounds.Length);if(zombies)sizes[art.unitBounds.Length]=new Vector4(3.4f,1.05f,0,0);
            bounds=new GraphicsBuffer(GraphicsBuffer.Target.Structured,sizes.Length,16);bounds.SetData(sizes);
            NearMesh=Template(NearTriangles);FarMesh=Template(FarTriangles);
        }
        static GraphicsBuffer Bank(TextAsset[] files,int triangles,bool zombies)
        {
            var vertices=new Vertex[(files.Length+(zombies?1:0))*triangles*3];
            for(int i=0;i<files.Length;i++){
                byte[] bytes=files[i].bytes;
                if(bytes.Length!=triangles*3*64)throw new InvalidOperationException("Malformed model vertex bank: "+files[i].name);
                var handle=GCHandle.Alloc(vertices,GCHandleType.Pinned);
                try{Marshal.Copy(bytes,0,IntPtr.Add(handle.AddrOfPinnedObject(),i*bytes.Length),bytes.Length);}finally{handle.Free();}
            }
            if(zombies){var zombie=ZombieGeometry.Create();var zp=zombie.vertices;var zn=zombie.normals;var zu=zombie.uv;var zi=zombie.triangles;
            if(zi.Length>triangles*3)throw new InvalidOperationException("Zombie shared shell exceeds its vertex bank allocation");
            for(int i=0;i<zi.Length;i++){int at=zi[i];vertices[files.Length*triangles*3+i]=new Vertex{position=new Vector4(zp[at].x,zp[at].y,zp[at].z,10+zu[at].y),normal=zn[at],tangent=new Vector4(1,0,0,1),uv=new Vector4(.5f,.5f,zu[at].x,0)};}
            ArmyLifetime.Destroy(zombie);}
            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,vertices.Length,64){name="Catalogue shell vertices "+triangles};buffer.SetData(vertices);return buffer;
        }
        static Mesh Template(int triangles)
        {
            var mesh=new Mesh{name="GPU catalogue vertex slots "+triangles};var vertices=new Vector3[triangles*3];var indices=new int[vertices.Length];
            for(int i=0;i<indices.Length;i++)indices[i]=i;
            mesh.vertices=vertices;mesh.triangles=indices;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*128);return mesh;
        }
        public void Bind(Material material,int mode)
        {
            material.SetBuffer("_ArtVertices",mode==0?near:far);material.SetBuffer("_ArtBounds",bounds);
            material.SetInt("_ArtStride",(mode==0?NearTriangles:FarTriangles)*3);
            material.SetTexture("_ArtAlbedo",art.albedo);material.SetTexture("_ArtMetallic",art.metallic);material.SetTexture("_ArtNormal",art.normal);material.SetTexture("_ArtImpostors",art.impostors);
        }
        public void Dispose(){near.Dispose();far.Dispose();bounds.Dispose();ArmyLifetime.Destroy(NearMesh);ArmyLifetime.Destroy(FarMesh);}
    }
}
