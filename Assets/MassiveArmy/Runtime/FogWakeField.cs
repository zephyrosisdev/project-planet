using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
namespace MassiveArmy
{
    // Fixed resource budget: two 3D half-float fields, one aggregate buffer and cached noise (~16 MiB).
    public sealed class FogWakeField : IDisposable
    {
        public const long ResourceBytes=96L*96*48*(8*2+16)+64L*64*64*8;
        public RenderTexture Noise {get;}
        public RenderTexture Current=>current;
        public RenderTexture Previous=>previous;
        public Vector3 Origin {get;private set;}
        public Vector3 PreviousOrigin {get;private set;}
        public Vector3 Extent {get;private set;}
        public Vector3 PreviousExtent {get;private set;}
        public float Interpolation=>Mathf.Clamp01((Time.realtimeSinceStartup-lastUpdate)*15);
        readonly ComputeShader compute;
        readonly GraphicsBuffer injection;
        RenderTexture current,previous;
        bool initialized;uint lastUnitTick;float terrainBottom,terrainTop;
        float lastUpdate=-1;
        public FogWakeField()
        {
            if(ResourceBytes>64L*1024*1024)throw new InvalidOperationException("Fog wake resource budget exceeded.");
            compute=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/FogWake"));
            Noise=Volume("Cached tileable fog noise",64,64,64);
            current=Volume("Persistent smog wakes A",96,48,96);previous=Volume("Persistent smog wakes B",96,48,96);
            injection=new GraphicsBuffer(GraphicsBuffer.Target.Structured,96*96*48,16);
        }
        static RenderTexture Volume(string name,int x,int y,int z)
        {
            var texture=new RenderTexture(x,y,0){name=name,dimension=TextureDimension.Tex3D,volumeDepth=z,graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=name.Contains("noise")?TextureWrapMode.Repeat:TextureWrapMode.Clamp};texture.Create();return texture;
        }
        public void Record(CommandBuffer cmd,GpuBattle battle,Camera camera,BattleVisualSettings look,float fade)
        {
            if(!initialized){terrainBottom=float.MaxValue;terrainTop=float.MinValue;foreach(float height in battle.Field.Map.Height){terrainBottom=Mathf.Min(terrainBottom,height);terrainTop=Mathf.Max(terrainTop,height);}cmd.SetComputeTextureParam(compute,0,"_NoiseCache",Noise);cmd.DispatchCompute(compute,0,16,16,16);}
            if(initialized&&(fade<.01f||Time.realtimeSinceStartup-lastUpdate<1f/15))return;
            Vector3 focus=WorldTopology.ViewFocus(camera);
            float footprint=camera.orthographic?camera.orthographicSize*2:500;
            Vector3 right=camera.transform.right;Vector2 forward=new Vector2(-right.z,right.x);
            float horizontal=footprint*camera.aspect,vertical=footprint/Mathf.Sin(55*Mathf.Deg2Rad);
            Vector3 extent=new Vector3(Mathf.Max(256,(Mathf.Abs(right.x)*horizontal+Mathf.Abs(forward.x)*vertical)*1.3f),Mathf.Max(128,terrainTop-terrainBottom+look.height*1.7f),Mathf.Max(256,(Mathf.Abs(right.z)*horizontal+Mathf.Abs(forward.y)*vertical)*1.3f));
            bool resized=!initialized||Time.realtimeSinceStartup-lastUpdate>1||Mathf.Abs(extent.x-Extent.x)>Extent.x*.15f||Mathf.Abs(extent.z-Extent.z)>Extent.z*.15f||Mathf.Abs(extent.y-Extent.y)>Extent.y*.15f;
            if(!resized)extent=Extent;
            Vector3 origin=focus-extent*.5f;origin.y=terrainBottom-3;
            PreviousOrigin=Origin;PreviousExtent=Extent;Origin=origin;Extent=extent;
            (current,previous)=(previous,current);
            cmd.SetComputeFloatParam(compute,"_WorldSize",battle.Field.Size);cmd.SetComputeIntParam(compute,"_Active",battle.Settings.activeUnits);cmd.SetComputeIntParam(compute,"_FogRes",GpuBattle.FogResolution);
            cmd.SetComputeFloatParam(compute,"_WakeDelta",1f/15);cmd.SetComputeFloatParam(compute,"_Decay",look.wakeDecay);cmd.SetComputeFloatParam(compute,"_Radius",look.wakeRadius);cmd.SetComputeFloatParam(compute,"_Strength",look.wakeStrength*fade);cmd.SetComputeFloatParam(compute,"_Swirl",look.swirlStrength);
            cmd.SetComputeVectorParam(compute,"_Wind",look.wind);cmd.SetComputeVectorParam(compute,"_Origin",Origin);cmd.SetComputeVectorParam(compute,"_PreviousOrigin",PreviousOrigin);cmd.SetComputeVectorParam(compute,"_Extent",Extent);cmd.SetComputeVectorParam(compute,"_PreviousExtent",PreviousExtent);cmd.SetComputeIntParam(compute,"_First",!initialized||Time.realtimeSinceStartup-lastUpdate>1?1:0);
            cmd.SetComputeBufferParam(compute,1,"_Injection",injection);cmd.DispatchCompute(compute,1,(96*96*48+127)/128,1,1);
            if(fade>.01f&&(battle.Tick!=lastUnitTick||camera.cameraType==CameraType.SceneView)){
                cmd.SetComputeBufferParam(compute,2,"_Injection",injection);cmd.SetComputeBufferParam(compute,2,"_Units",battle.Units);cmd.SetComputeBufferParam(compute,2,"_Types",battle.TypeBuffer);cmd.SetComputeBufferParam(compute,2,"_Traits",battle.TraitsBuffer);cmd.SetComputeBufferParam(compute,2,"_Visibility",battle.FogBuffer);cmd.SetComputeTextureParam(compute,2,"_Terrain",battle.Field.Terrain);cmd.SetComputeTextureParam(compute,2,"_Water",battle.Field.WaterMap);cmd.DispatchCompute(compute,2,(battle.Settings.activeUnits+127)/128,1,1);
            }
            cmd.SetComputeBufferParam(compute,3,"_Injection",injection);cmd.SetComputeTextureParam(compute,3,"_WakeRead",previous);cmd.SetComputeTextureParam(compute,3,"_WakeWrite",current);cmd.DispatchCompute(compute,3,24,12,24);
            if(!initialized){cmd.CopyTexture(current,previous);PreviousOrigin=Origin;PreviousExtent=Extent;}
            lastUnitTick=battle.Tick;initialized=true;lastUpdate=Time.realtimeSinceStartup;
        }
        public void Dispose(){injection.Dispose();foreach(var t in new[]{Noise,current,previous}){t.Release();ArmyLifetime.Destroy(t);}ArmyLifetime.Destroy(compute);}
    }
}
