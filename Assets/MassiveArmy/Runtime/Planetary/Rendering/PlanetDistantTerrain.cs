using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Small visual-only caches, shared by a system. No navigation, army or simulation allocation.</summary>
    public sealed class PlanetDistantTerrain:IDisposable
    {
        public const int Resolution=128,Extent=Resolution+2;
        public sealed class Preview:IDisposable
        {
            internal PlanetDefinition source,visual;internal int revision,signature;internal ComputeShader baker;
            public RenderTexture Surface {get;internal set;}
            public int BakeCount {get;internal set;}
            internal PlanetDefinition Definition=>visual!=null?visual:source;
            public void Dispose(){if(Surface!=null){Surface.Release();ArmyLifetime.Destroy(Surface);}ArmyLifetime.Destroy(visual);ArmyLifetime.Destroy(baker);}
        }
        readonly Dictionary<int,Preview> previews=new Dictionary<int,Preview>();
        public int Count=>previews.Count;
        static int Signature(PlanetDefinition d){unchecked{return (((d.terrainSeed*397^d.radius.GetHashCode())*397^d.terrainHeight.GetHashCode())*397^d.oceanHeight.GetHashCode())*397^(d.stampTerrain?1:0);}}
        public Preview Get(PlanetDefinition body){
            int signature=Signature(body),revision=body.edits?.Revision??0;
            if(!previews.TryGetValue(body.bodyId,out var preview)){preview=new Preview();previews.Add(body.bodyId,preview);}
            if(preview.Surface!=null&&preview.signature==signature&&preview.revision==revision)return preview;
            ArmyLifetime.Destroy(preview.visual);preview.visual=null;preview.source=body;preview.signature=signature;preview.revision=revision;
            // A preview owns only compact stamp descriptors; unvisited gameplay definitions remain untouched.
            if(body.edits==null){preview.visual=UnityEngine.Object.Instantiate(body);preview.visual.name=body.name+" visual field";}
            if(preview.Surface==null){preview.Surface=new RenderTexture(Extent,Extent,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){name=body.displayName+" coarse normal and height",dimension=TextureDimension.Tex2DArray,volumeDepth=6,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};preview.Surface.Create();}
            if(preview.baker==null)preview.baker=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetDistantBake"));var baker=preview.baker;
            PlanetDevelopment.BindSurface(baker,preview.Definition);baker.SetInt("_PreviewResolution",Resolution);baker.SetTexture(0,"_PreviewSurface",preview.Surface);baker.Dispatch(0,(Extent+7)/8,(Extent+7)/8,6);preview.BakeCount++;
            return preview;
        }
        public void Dispose(){foreach(var preview in previews.Values)preview.Dispose();previews.Clear();}
    }
}
