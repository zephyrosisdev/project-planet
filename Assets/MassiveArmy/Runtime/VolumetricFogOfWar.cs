using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MassiveArmy
{
    [DisallowMultipleComponent]
    public sealed class VolumetricFogOfWar : MonoBehaviour
    {
        public BattleVisualSettings settings;
        GpuBattle battle;Camera view;Light sun;CustomPassVolume volume;FogPass pass;
        float minimumHeight,maximumHeight;
        Renderer flatFallback;
        public int RenderedFrames {get;private set;}
        public RenderTexture WakeVolume=>pass?.WakeVolume;
        public RenderTexture NoiseVolume=>pass?.NoiseVolume;
        public void Initialize(GpuBattle source,Camera camera,Light light,BattleVisualSettings look)
        {
            battle=source;view=camera;sun=light;settings=look;
            minimumHeight=battle.Field.Map.Height.Min();maximumHeight=battle.Field.Map.Height.Max();
            volume=gameObject.AddComponent<CustomPassVolume>();volume.isGlobal=true;volume.injectionPoint=CustomPassInjectionPoint.BeforePostProcess;
            pass=new FogPass{owner=this,name="Volumetric fog of war",targetColorBuffer=CustomPass.TargetBuffer.None,targetDepthBuffer=CustomPass.TargetBuffer.None};
            volume.customPasses.Add(pass);
        }
        public void RegisterFlatFallback(Renderer renderer){flatFallback=renderer;Update();}
        void Update(){if(flatFallback!=null)flatFallback.enabled=false;}
        public void Detach(){battle=null;if(volume!=null)volume.enabled=false;}
        void OnEnable(){if(volume!=null)volume.enabled=true;Update();}
        void OnDisable(){if(volume!=null)volume.enabled=false;Update();}
        [Serializable] sealed class FogPass : CustomPass
        {
            [NonSerialized] public VolumetricFogOfWar owner;
            public RenderTexture WakeVolume=>wakes?.Current;
            public RenderTexture NoiseVolume=>wakes?.Noise;
            Material material;ComputeShader maskShader;FogWakeField wakes;RTHandle fog,depth,densityMask,darknessMask;int width,height;bool firstMask=true;
            readonly RenderTargetIdentifier[] targets=new RenderTargetIdentifier[2];
            protected override void Setup(ScriptableRenderContext context,CommandBuffer cmd)
            {
                wakes=new FogWakeField();
                material=CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("MassiveArmy/VolumetricFogOfWar"));
                maskShader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/FogOfWar"));
                densityMask=RTHandles.Alloc(GpuBattle.FogResolution,GpuBattle.FogResolution,colorFormat:GraphicsFormat.R16_SFloat,enableRandomWrite:true,filterMode:FilterMode.Bilinear,name:"Smoothed GPU fog of war mask");
                darknessMask=RTHandles.Alloc(GpuBattle.FogResolution,GpuBattle.FogResolution,colorFormat:GraphicsFormat.R16_SFloat,enableRandomWrite:true,filterMode:FilterMode.Bilinear,name:"Reconnaissance darkness");

            }
            protected override void Execute(CustomPassContext ctx)
            {
                if(owner==null||owner.battle==null||owner.settings==null||ctx.hdCamera.camera!=owner.view)return;
                using var fogTiming=UiFogTelemetry.DrawScope();
                var battle=owner.battle;var look=owner.settings;
                ctx.cmd.BeginSample(GpuSamples.Get("Army.Fog"));
                float pixels=5*ctx.hdCamera.actualHeight/(2*Mathf.Max(1,owner.view.orthographicSize))*Mathf.Pow(2,look.detailBias);
                float battleDetail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(3,5,pixels)),closeDetail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,14,pixels));
                wakes.Record(ctx.cmd,battle,owner.view,look,battleDetail);

                ctx.cmd.SetComputeBufferParam(maskShader,0,"_Visibility",battle.FogBuffer);ctx.cmd.SetComputeTextureParam(maskShader,0,"_DensityMask",densityMask);
                ctx.cmd.SetComputeIntParam(maskShader,"_Resolution",GpuBattle.FogResolution);
                float maskDelta=Time.unscaledDeltaTime;
#if UNITY_EDITOR
                if(!Application.isPlaying)maskDelta=1f/30;
#endif
                ctx.cmd.SetComputeVectorParam(maskShader,"_MaskSettings",new Vector4(look.exploredDensity,look.edgeSoftness,firstMask?1:1-Mathf.Exp(-maskDelta/Mathf.Max(.1f,look.mistClearSeconds)),look.visibleMistDensity));
                ctx.cmd.DispatchCompute(maskShader,0,GpuBattle.FogResolution/8,GpuBattle.FogResolution/8,1);
                ctx.cmd.SetComputeBufferParam(maskShader,1,"_Visibility",battle.FogBuffer);ctx.cmd.SetComputeTextureParam(maskShader,1,"_DarknessMask",darknessMask);
                ctx.cmd.SetComputeVectorParam(maskShader,"_DarkSettings",new Vector4(look.unexploredDarkness,look.exploredDarkness,0,0));
                ctx.cmd.DispatchCompute(maskShader,1,GpuBattle.FogResolution/8,GpuBattle.FogResolution/8,1);
                firstMask=false;
                int w=Mathf.Max(1,(ctx.hdCamera.actualWidth+1)/2),h=Mathf.Max(1,(ctx.hdCamera.actualHeight+1)/2);
                if(fog==null||w!=width||h!=height)
                {
                    fog?.Release();depth?.Release();width=w;height=h;
                    fog=RTHandles.Alloc(w,h,colorFormat:GraphicsFormat.R16G16B16A16_SFloat,filterMode:FilterMode.Bilinear,name:"Fog of war half resolution scattering");
                    depth=RTHandles.Alloc(w,h,colorFormat:GraphicsFormat.R32_SFloat,filterMode:FilterMode.Point,name:"Fog of war depth for bilateral upsampling");
                    targets[0]=fog.nameID;targets[1]=depth.nameID;
                }
                var block=ctx.propertyBlock;block.Clear();block.SetBuffer("_Visibility",battle.FogBuffer);block.SetTexture("_TerrainHeight",battle.Field.Terrain);block.SetTexture("_WaterSurface",battle.Field.WaterMap);
                block.SetFloat("_WorldSize",battle.Field.Size);block.SetInt("_FogResolution",GpuBattle.FogResolution);
                block.SetTexture("_DensityMask",densityMask.rt);
                block.SetTexture("_DarknessMask",darknessMask.rt);
                block.SetTexture("_NoiseCache",wakes.Noise);block.SetTexture("_WakeCurrent",wakes.Current);block.SetTexture("_WakePrevious",wakes.Previous);
                block.SetVector("_WakeOrigin",wakes.Origin);block.SetVector("_WakePreviousOrigin",wakes.PreviousOrigin);block.SetVector("_WakeExtent",wakes.Extent);block.SetVector("_WakePreviousExtent",wakes.PreviousExtent);block.SetVector("_FogDetail",new Vector4(battleDetail,closeDetail,wakes.Interpolation,look.fogOfWar?1:0));
                block.SetVector("_FogMotion",new Vector4(look.flowStrength,Mathf.Max(4,look.flowCycle),0,0));
                block.SetVector("_FogShape",new Vector4(look.density,look.height,look.exploredDensity,look.edgeSoftness));
                block.SetFloat("_FlowScale",Mathf.Max(1,look.flowScale));
                block.SetVector("_FogNoise",new Vector4(look.noiseScale,look.noiseStrength,look.wind.x,look.wind.y));
                block.SetVector("_HalfSize",new Vector4(w,h,1f/w,1f/h));block.SetInt("_RaySteps",Mathf.RoundToInt(16+8*battleDetail+8*closeDetail));
                block.SetVector("_FowFullSize",new Vector4(ctx.hdCamera.actualWidth,ctx.hdCamera.actualHeight,1f/ctx.hdCamera.actualWidth,1f/ctx.hdCamera.actualHeight));
                float half=battle.Field.Size;Vector3 focus=(WorldTopology.ViewFocus(owner.view));
                block.SetVector("_FogBoundsMin",new Vector4(focus.x-half,owner.minimumHeight,focus.z-half,0));block.SetVector("_FogBoundsMax",new Vector4(focus.x+half,owner.maximumHeight+look.height*1.8f,focus.z+half,0));
                float strength=owner.sun!=null&&owner.sun.isActiveAndEnabled?owner.sun.intensity:0;
                Color illumination=owner.sun!=null?owner.sun.color.linear:Color.white;
                Color tint=look.fogColour.linear*illumination*Mathf.Max(strength,300)*look.scattering;
                // Radiance is already linear HDR data; SetColor would apply another sRGB conversion.
                block.SetVector("_FogRadiance",new Vector4(tint.r,tint.g,tint.b,1));block.SetVector("_FogLightDirection",owner.sun!=null?-owner.sun.transform.forward:Vector3.up);
                ctx.cmd.SetRenderTarget(targets,BuiltinRenderTextureType.None);ctx.cmd.SetViewport(new Rect(0,0,w,h));
                CoreUtils.DrawFullScreen(ctx.cmd,material,block,0);
                block.SetTexture("_FogScattering",fog.rt);block.SetTexture("_FogDepth",depth.rt);
                CoreUtils.SetRenderTarget(ctx.cmd,ctx.cameraColorBuffer,ClearFlag.None);
                // RTHandle backing buffers can exceed this camera's viewport (e.g. render requests).
                ctx.cmd.SetViewport(new Rect(0,0,ctx.hdCamera.actualWidth,ctx.hdCamera.actualHeight));
                CoreUtils.DrawFullScreen(ctx.cmd,material,block,1);owner.RenderedFrames++;ctx.cmd.EndSample(GpuSamples.Get("Army.Fog"));
            }
            protected override void Cleanup(){wakes?.Dispose();wakes=null;fog?.Release();depth?.Release();densityMask?.Release();darknessMask?.Release();fog=depth=densityMask=darknessMask=null;CoreUtils.Destroy(material);CoreUtils.Destroy(maskShader);material=null;maskShader=null;}
        }
    }
}
