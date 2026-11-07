using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetWeatherRenderer {
        const int AirWidth=128,AirHeight=64,AirSlices=17;
        readonly PlanetDefinition[] airBodies=new PlanetDefinition[AirSlices];
        readonly float[] airHeights=new float[AirSlices];
        readonly Vector4[] airTides=new Vector4[AirSlices];readonly double[] nextAirTide=new double[AirSlices];
        readonly RenderTargetIdentifier[] airTargets=new RenderTargetIdentifier[2];
        RenderTexture airColumns,airTransmission;ComputeShader airCompute;int nextAirSlot;
        internal bool PhysicalAirEnabled=true;
        long AirTextureBytes=>(airColumns!=null?(long)AirWidth*AirHeight*AirSlices*8:0)+(airTransmission!=null?(long)airTransmission.width*airTransmission.height*8:0);
        int AirSlot(PlanetDefinition body){for(int i=0;i<airBodies.Length;i++)if(airBodies[i]==body)return i;return -1;}
        void PrepareAir(CommandBuffer cmd){
            bool wanted=false;for(int i=0;i<VisibleBodies;i++)wanted|=bodies[i].weather==null||bodies[i].weather.physicalAtmosphere;
            wanted&=PhysicalAirEnabled;
            if(!wanted){if(airTransmission!=null){airTransmission.Release();ArmyLifetime.Destroy(airTransmission);airTransmission=null;}return;}
            airCompute??=Resources.Load<ComputeShader>("MassiveArmy/PlanetAirOpticalDepth");
            if(airColumns==null){airColumns=new RenderTexture(AirWidth,AirHeight,0,RenderTextureFormat.RGFloat,RenderTextureReadWrite.Linear){name="Atmosphere density columns (17 bounded planet slots)",dimension=TextureDimension.Tex2DArray,volumeDepth=AirSlices,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};airColumns.Create();}
            if(airTransmission==null||airTransmission.width!=weather.width||airTransmission.height!=weather.height){
                if(airTransmission!=null){airTransmission.Release();ArmyLifetime.Destroy(airTransmission);}
                airTransmission=new RenderTexture(weather.width,weather.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name="Atmosphere spectral transmission",filterMode=FilterMode.Bilinear};airTransmission.Create();
            }
            airTargets[0]=weather;airTargets[1]=airTransmission;cmd.SetGlobalTexture("_AirColumns",airColumns);
            // Nearest body first. At most one 128x64 slice is built per frame;
            // queued bodies keep legacy haze until their own slice is initialized.
            for(int i=0;i<VisibleBodies;i++){
                var body=bodies[i];if(body.weather!=null&&!body.weather.physicalAtmosphere)continue;
                float ratio=body.atmosphereHeight/body.radius;int slot=AirSlot(body);
                if(slot>=0&&airHeights[slot]==ratio)continue;
                if(slot<0){slot=nextAirSlot++%AirSlices;airBodies[slot]=body;nextAirTide[slot]=0;}
                airHeights[slot]=ratio;
                cmd.SetComputeFloatParam(airCompute,"_AirHeight",ratio);cmd.SetComputeIntParam(airCompute,"_AirSlice",slot);
                cmd.SetComputeTextureParam(airCompute,0,"_AirColumnsWrite",airColumns);cmd.DispatchCompute(airCompute,0,AirWidth/8,AirHeight/8,1);break;
            }
        }
        void SetAirProperties(MaterialPropertyBlock block,PlanetDefinition body){
            int slot=AirSlot(body);var settings=body.weather;bool ready=PhysicalAirEnabled&&settings.physicalAtmosphere&&slot>=0&&airHeights[slot]==body.atmosphereHeight/body.radius;
            float pressure=Mathf.Clamp(body.surfacePressure,0,10);
            block.SetVector("_AirScattering",new Vector4(ready?1:0,Math.Max(0,slot),Mathf.Max(0,settings.rayleighStrength)*pressure,Mathf.Max(0,settings.aerosolStrength)*pressure*Mathf.Max(0,settings.haze)/.45f));
            if(slot>=0&&Time.unscaledTimeAsDouble>=nextAirTide[slot]){airTides[slot]=PlanetAtmosphericTides.GravityAxis(world,body);nextAirTide[slot]=Time.unscaledTimeAsDouble+1;}
            block.SetVector("_AirVariation",new Vector4(settings.airDensityNoise,settings.airDensityNoiseScale,settings.airSolarTide,settings.airGravityTide));block.SetVector("_AirGravityTide",slot>=0?airTides[slot]:Vector4.zero);
        }
        void ReleaseAir(){if(airTransmission!=null){airTransmission.Release();ArmyLifetime.Destroy(airTransmission);}if(airColumns!=null){airColumns.Release();ArmyLifetime.Destroy(airColumns);}airTransmission=airColumns=null;Array.Clear(airBodies,0,airBodies.Length);Array.Clear(airHeights,0,airHeights.Length);Array.Clear(nextAirTide,0,nextAirTide.Length);Array.Clear(airTides,0,airTides.Length);nextAirSlot=0;}
    }
}
