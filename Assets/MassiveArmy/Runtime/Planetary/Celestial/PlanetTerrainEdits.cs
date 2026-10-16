using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Bounded canonical terrain edits. No unit records or per-unit CPU work.</summary>
    public sealed class PlanetTerrainEdits:IDisposable
    {
        public const int Capacity=32,Grid=8;
        readonly PlanetDefinition definition;
        readonly Vector4[] craters=new Vector4[Capacity],profiles=new Vector4[Capacity],masks=new Vector4[6*Grid*Grid],plateaus=new Vector4[2];
        readonly Dictionary<ComputeShader,int> computes=new Dictionary<ComputeShader,int>();readonly Dictionary<Material,int> materials=new Dictionary<Material,int>();
        public int Count {get;private set;}public int Revision {get;private set;}public float MaximumDepth {get;private set;}
        int plateauCount;Vector4 editCap;GraphicsBuffer editBuffer;Vector4[] editData;int bufferRevision=-1;static readonly int EditBufferId=Shader.PropertyToID("PlanetTerrainEditData");
        GraphicsBuffer DataBuffer(){if(editBuffer==null){editBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Constant,450,16);editData=new Vector4[450];}if(bufferRevision!=Revision){Array.Copy(masks,0,editData,0,384);Array.Copy(craters,0,editData,384,32);Array.Copy(profiles,0,editData,416,32);Array.Copy(plateaus,0,editData,448,2);editBuffer.SetData(editData);bufferRevision=Revision;}return editBuffer;}
        public void Dispose(){editBuffer?.Dispose();editBuffer=null;editData=null;bufferRevision=-1;computes.Clear();materials.Clear();}
        public PlanetTerrainEdits(PlanetDefinition definition){this.definition=definition;}
        public static float MountainWeight(PlanetDefinition d,Vector3 p){if(d.stampTerrain&&d.radius>=20000)return PlanetStampLibrary.Shared.Terrain(p,d).y;float s=(d.terrainSeed%1009)*.017f;float t=Mathf.Clamp01((Mathf.Sin(p.x*19+p.y*13+p.z*17+s)-.1f)/.7f);return t*t*(3-2*t);}
        // Directional fields have no cube-face seams. Physical wavelengths preserve local relief on large worlds.
        // Keep the compact-world fixtures' original field; the matching GPU expression lives in PlanetSurface.hlsl.
        public static float BaseHeight(PlanetDefinition d,Vector3 direction){if(d.terrainHeight==0)return 0;var p=direction.normalized;float s=(d.terrainSeed%1009)*.017f;
            float continent=Mathf.Sin(p.x*8+s)*Mathf.Sin(p.y*7-s)*Mathf.Cos(p.z*9+s);
            if(d.radius<20000)return d.terrainHeight*(continent*.65f+Mathf.Sin(p.x*19+p.y*13+p.z*17+s)*.2f);
            if(d.stampTerrain){var terrain=PlanetStampLibrary.Shared.Terrain(p,d);return d.terrainHeight*(continent*.30f-.06f+terrain.x*(.02f+terrain.y*.70f));}
            float mountain=MountainWeight(d,p);
            float ridge=Mathf.Sin((p.x+p.y*.63f+p.z*.37f)*(d.radius/1400)+s+continent*2);ridge=1-ridge*ridge;
            float hill=Mathf.Sin((p.x*.31f-p.y*.8f+p.z)*(d.radius/700)+s);
            return d.terrainHeight*(continent*.30f-.06f+mountain*ridge*ridge*.60f+(1-mountain)*hill*.008f);
        }
        public static float ReliefCurvature(PlanetDefinition d)=>d.radius>=20000?Mathf.Abs(d.terrainHeight)*(.0000018f):80*Mathf.Abs(d.terrainHeight)/Mathf.Max(1,d.radius*d.radius);
        public void PrepareStartingAreas(Vector3 a,Vector3 b){plateauCount=2;for(int i=0;i<2;i++){var d=(i==0?a:b).normalized;plateaus[i]=new Vector4(d.x,d.y,d.z,Mathf.Max(definition.oceanHeight+35,BaseHeight(definition,d)));}Changed();}
        static float Profile(float q,float depth){float bowl=q<1?-depth*Mathf.Pow(1-q*q,2):0;float rim=Mathf.Max(0,1-Mathf.Abs(q-1)/.25f);return bowl+depth*.18f*rim*rim;}
        public float Height(Vector3 direction){var d=direction.normalized;float h=BaseHeight(definition,d);for(int i=0;i<plateauCount;i++){var p=plateaus[i];float distance=(d-new Vector3(p.x,p.y,p.z)).magnitude*definition.radius;float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(260,540,distance));h=Mathf.Lerp(p.w,h,blend);}return h+Delta(d);}
        public float Delta(Vector3 direction){if(Count==0)return 0;uint bits=Mask(direction);float low=0,high=0;for(int i=0;i<Count;i++)if((bits&(1u<<i))!=0){var c=craters[i];float q=(direction-new Vector3(c.x,c.y,c.z)).magnitude*definition.radius/c.w;float delta=Profile(q,profiles[i].x);low=Mathf.Min(low,delta);high=Mathf.Max(high,delta);}return low+high;}
        public float FeatureCurvature(Vector3 center,float bound){float value=0;for(int i=0;i<Count;i++){var c=craters[i];if(Vector3.Distance(center,new Vector3(c.x,c.y,c.z)*definition.radius)<=bound+c.w*1.25f)value=Mathf.Max(value,8*profiles[i].x/(c.w*c.w));}return value;}
        uint Mask(Vector3 direction){var cell=CubeSphere.Cell(definition.bodyId,direction,Grid);var v=masks[cell.Index(Grid)];return (uint)v.x|((uint)v.y<<16);}
        public bool AddCrater(Vector3 direction,float radius){if(radius<=0||direction.sqrMagnitude<.5f)return false;direction.Normalize();int index=-1;
            for(int i=0;i<Count;i++)if((direction-new Vector3(craters[i].x,craters[i].y,craters[i].z)).magnitude*definition.radius<Mathf.Min(radius,craters[i].w)*.2f){index=i;break;}
            if(index<0){if(Count==Capacity)return false;index=Count++;}
            radius=Mathf.Clamp(Mathf.Max(radius,craters[index].w),8,definition.radius*.1f);float depth=Mathf.Min(80,radius*.16f);craters[index]=new Vector4(direction.x,direction.y,direction.z,radius);profiles[index]=new Vector4(depth,0,0,0);MaximumDepth=Mathf.Max(MaximumDepth,depth);
            Array.Clear(masks,0,masks.Length);
            for(int face=0;face<6;face++)for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++){var center=CubeSphere.Direction((CubeFace)face,new Vector2((x+.5f)*2/Grid-1,(y+.5f)*2/Grid-1));float bound=0;for(int cy=0;cy<2;cy++)for(int cx=0;cx<2;cx++)bound=Mathf.Max(bound,Vector3.Angle(center,CubeSphere.Direction((CubeFace)face,new Vector2((x+cx)*2f/Grid-1,(y+cy)*2f/Grid-1)))*Mathf.Deg2Rad);uint bits=0;for(int i=0;i<Count;i++){var c=craters[i];float cap=2*Mathf.Asin(Mathf.Min(1,c.w*1.25f/(2*definition.radius)));if(Vector3.Angle(center,new Vector3(c.x,c.y,c.z))*Mathf.Deg2Rad<=bound+cap+.0001f)bits|=1u<<i;}masks[(face*Grid+y)*Grid+x]=new Vector4(bits&65535,bits>>16,0,0);}
            Changed();return true;
        }
        void UpdateCap(){Vector3 sum=Vector3.zero;for(int i=0;i<Count;i++)sum+=(Vector3)craters[i];for(int i=0;i<plateauCount;i++)sum+=(Vector3)plateaus[i];var center=sum.sqrMagnitude>1e-8f?sum.normalized:Vector3.up;float angle=0;
            for(int i=0;i<Count;i++)angle=Mathf.Max(angle,Vector3.Angle(center,(Vector3)craters[i])*Mathf.Deg2Rad+2*Mathf.Asin(Mathf.Min(1,craters[i].w*1.25f/(2*definition.radius))));
            for(int i=0;i<plateauCount;i++)angle=Mathf.Max(angle,Vector3.Angle(center,(Vector3)plateaus[i])*Mathf.Deg2Rad+2*Mathf.Asin(Mathf.Min(1,540/(2*definition.radius))));
            editCap=new Vector4(center.x,center.y,center.z,Mathf.Cos(Mathf.Min(Mathf.PI,angle+.0001f)));}
        void Changed(){UpdateCap();Revision++;foreach(var shader in new List<ComputeShader>(computes.Keys))if(shader!=null)Upload(shader);foreach(var material in new List<Material>(materials.Keys))if(material!=null)Upload(material);}
        public void Bind(ComputeShader shader){if(!computes.TryGetValue(shader,out int version)||version!=Revision)Upload(shader);}
        public void Bind(Material material){if(!materials.TryGetValue(material,out int version)||version!=Revision)Upload(material);}
        void Upload(ComputeShader shader){PlanetStampLibrary.Shared.Bind(shader,definition);var keyword=new LocalKeyword(shader,"PLANET_TERRAIN_EDITS");if(keyword.isValid){bool single=Count==1&&plateauCount==0;shader.SetKeyword(keyword,Count+plateauCount>0&&!single);shader.SetKeyword(new LocalKeyword(shader,"PLANET_SINGLE_CRATER"),single);}shader.SetVector("_PlanetSingleCrater",craters[0]);shader.SetVector("_PlanetSingleProfile",profiles[0]);shader.SetVector("_PlanetEditCap",editCap);shader.SetInt("_PlanetCraterCount",Count);shader.SetInt("_PlanetPlateauCount",plateauCount);shader.SetFloat("_PlanetReliefBound",definition.ReliefBound);if(plateauCount>0||Count>1||(!keyword.isValid&&Count>0))shader.SetConstantBuffer(EditBufferId,DataBuffer(),0,7200);computes[shader]=Revision;}
        void Upload(Material material){PlanetStampLibrary.Shared.Bind(material,definition);var keyword=new LocalKeyword(material.shader,"PLANET_TERRAIN_EDITS");if(keyword.isValid){bool single=Count==1&&plateauCount==0;material.SetKeyword(keyword,Count+plateauCount>0&&!single);material.SetKeyword(new LocalKeyword(material.shader,"PLANET_SINGLE_CRATER"),single);}material.SetVector("_PlanetSingleCrater",craters[0]);material.SetVector("_PlanetSingleProfile",profiles[0]);material.SetVector("_PlanetEditCap",editCap);material.SetInt("_PlanetCraterCount",Count);material.SetInt("_PlanetPlateauCount",plateauCount);material.SetFloat("_PlanetReliefBound",definition.ReliefBound);if(plateauCount>0||Count>1||(!keyword.isValid&&Count>0))material.SetConstantBuffer(EditBufferId,DataBuffer(),0,7200);materials[material]=Revision;}
    }
}
