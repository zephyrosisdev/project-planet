using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>Visual background only. No bodies, gravity sources or army allocations.</summary>
    internal sealed class GalaxyBackdrop:IDisposable
    {
        const int Width=512,Height=256,RowsPerFrame=32;
        readonly GalaxyModel galaxy;readonly Material material;readonly ComputeShader baker;
        readonly RenderTexture[] clouds=new RenderTexture[2];
        UniversePosition bakeAnchor;double lastBake=-1e9;int row,front;bool baking,ready;float blend=1;
        internal int BakeCount {get;private set;}
        internal int StarCount=>Mathf.Clamp(galaxy.Settings.backgroundStars,0,100000);
        internal bool Ready=>ready;
        internal long TextureBytes=>clouds[0]!=null?2L*Width*Height*8:0;
        internal GalaxyBackdrop(GalaxyModel galaxy){
            this.galaxy=galaxy;
            // Standalone comparison switches; never modify an editor asset for a benchmark.
            if(!Application.isEditor){var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-galaxy-background-stars");if(index>=0&&index+1<args.Length&&int.TryParse(args[index+1],out int stars))galaxy.Settings.backgroundStars=Mathf.Clamp(stars,0,100000);if(Array.IndexOf(args,"-galaxy-no-nebula")>=0)galaxy.Settings.nebulae=false;}
            material=new Material(Resources.Load<Shader>("MassiveArmy/GalaxyBackdrop"));
            if(galaxy.Settings.nebulae){
                baker=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/GalaxyNebula"));
                for(int i=0;i<2;i++){clouds[i]=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGBHalf){name="Cached galaxy nebula "+i,enableRandomWrite=true,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};clouds[i].Create();}
            }
        }
        internal bool NeedsBake(UniversePosition observer,double clock)=>baker!=null&&!baking&&blend>=1&&(!ready||clock-lastBake>=Math.Max(10,galaxy.Settings.nebulaRefreshSeconds)&&observer.RelativeTo(bakeAnchor).Magnitude>=Math.Max(1e6,galaxy.Settings.galaxyRadius*.025));
        internal void Update(Camera view,UniversePosition observer){
            double clock=Time.realtimeSinceStartupAsDouble;
            blend=Mathf.Min(1,blend+Time.unscaledDeltaTime);
            if(NeedsBake(observer,clock)){
                bakeAnchor=observer;lastBake=clock;row=0;baking=true;
                var origin=(observer.RelativeTo(galaxy.Origin)*(1/Math.Max(1,galaxy.Settings.galaxyRadius))).Float;
                baker.SetVector("_Observer",origin);baker.SetInt("_Seed",galaxy.Settings.seed);
            }
            if(baking){
                // Eight bounded dispatches, into the hidden buffer. Never raymarch every screen pixel each frame.
                baker.SetInt("_StartRow",row);baker.SetTexture(0,"_Clouds",clouds[1-front]);baker.Dispatch(0,Width/8,RowsPerFrame/8,1);row+=RowsPerFrame;
                if(row>=Height){baking=false;front=1-front;blend=ready?0:1;ready=true;BakeCount++;}
            }
            material.SetMatrix("_SkyVP",GL.GetGPUProjectionMatrix(view.projectionMatrix,true)*view.worldToCameraMatrix);
            material.SetVector("_SkyCamera",view.transform.position);material.SetVector("_SkyRight",view.transform.right);material.SetVector("_SkyUp",view.transform.up);material.SetVector("_SkyForward",view.transform.forward);
            material.SetVector("_SkyScreen",new Vector4(view.pixelWidth/(float)Math.Max(1,view.pixelHeight),Mathf.Tan(view.fieldOfView*Mathf.Deg2Rad*.5f),view.pixelWidth,view.pixelHeight));
            material.SetInt("_SkySeed",galaxy.Settings.seed);material.SetFloat("_StarBrightness",galaxy.Settings.backgroundStarBrightness);
            material.SetFloat("_NebulaBrightness",galaxy.Settings.nebulaBrightness);material.SetFloat("_CloudBlend",blend);
            if(ready){material.SetTexture("_CloudPrevious",clouds[BakeCount>1?1-front:front]);material.SetTexture("_CloudCurrent",clouds[front]);}
        }
        internal void Draw(CommandBuffer cmd){
            if(ready&&galaxy.Settings.nebulae&&galaxy.Settings.nebulaBrightness>0)cmd.DrawProcedural(Matrix4x4.identity,material,0,MeshTopology.Triangles,3);
            if(StarCount>0&&galaxy.Settings.backgroundStarBrightness>0)cmd.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,6,StarCount);
        }
        public void Dispose(){foreach(var tex in clouds)if(tex!=null){tex.Release();ArmyLifetime.Destroy(tex);}ArmyLifetime.Destroy(baker);ArmyLifetime.Destroy(material);}
    }
}
