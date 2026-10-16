using System;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    // Temporary casualty ring plus persistent nuclear surface paint; no CPU death list.
    public sealed class PlanetCombatDecals : IDisposable
    {
        const int Resolution=128,Capacity=512;
        readonly PlanetNuclearScorch scorch;readonly GraphicsBuffer stamps,indices,counter;readonly Texture2DArray colours,normals;readonly Material material;
        public PlanetCombatDecals(Material material,ComputeShader combat)
        {
            this.material=material;stamps=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,48);stamps.SetData(new Vector4[Capacity*3]);indices=new GraphicsBuffer(GraphicsBuffer.Target.Structured,6*Resolution*Resolution,4);indices.SetData(new uint[6*Resolution*Resolution]);counter=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);counter.SetData(new uint[1]);
            foreach(string name in new[]{"ApplyDamage"}){int kernel=combat.FindKernel(name);combat.SetBuffer(kernel,"_Remains",stamps);combat.SetBuffer(kernel,"_RemainsIndex",indices);combat.SetBuffer(kernel,"_RemainsCounter",counter);combat.SetInt("_RemainsResolution",Resolution);}
            colours=Load("Albedo",false);normals=Load("Normal",true);material.SetBuffer("_PlanetRemains",stamps);material.SetBuffer("_PlanetRemainsIndex",indices);material.SetInt("_PlanetRemainsResolution",Resolution);material.SetTexture("_PlanetRemainsColours",colours);material.SetTexture("_PlanetRemainsNormals",normals);material.SetVectorArray("_PlanetRemainsNeutral",Resources.Load<CombatSurfaceLibrary>("MassiveArmy/CombatArt/SurfaceLibrary").normalNeutral);material.EnableKeyword("PLANET_COMBAT_DECALS");scorch=new PlanetNuclearScorch(material,colours,normals,Resources.Load<CombatSurfaceLibrary>("MassiveArmy/CombatArt/SurfaceLibrary").normalNeutral[0]);
        }
        static Texture2DArray Load(string suffix,bool linear){string[] names={"NukeCrater","ZombieSplatter","HumanRemains","TankWreck","VehicleWreck","BuildingRubble"};var first=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[0]+suffix);var result=new Texture2DArray(first.width,first.height,names.Length,first.format,true,linear){name="Planet combat "+suffix,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};result.Apply(false,true);for(int i=0;i<names.Length;i++){var source=Resources.Load<Texture2D>("MassiveArmy/CombatArt/"+names[i]+suffix);if(source==null||source.format!=first.format)throw new InvalidOperationException("Combat decal import mismatch: "+names[i]);for(int mip=0;mip<source.mipmapCount;mip++)Graphics.CopyTexture(source,0,mip,result,i,mip);}return result;}
        internal PlanetNuclearScorch ScorchForValidation=>scorch;
        public void StampNuke(Vector3 direction,float radius,float planetRadius)=>scorch.Paint(direction,radius,planetRadius);
        internal uint CountForValidation(){var value=new uint[1];counter.GetData(value);return value[0]+(uint)scorch.Count;}
        public void Dispose(){material.DisableKeyword("PLANET_COMBAT_DECALS");scorch.Dispose();stamps.Dispose();indices.Dispose();counter.Dispose();ArmyLifetime.Destroy(colours);ArmyLifetime.Destroy(normals);}
    }
}
