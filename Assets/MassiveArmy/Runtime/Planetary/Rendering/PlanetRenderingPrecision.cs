using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    internal static class PlanetTerrainNoiseAnchor {
        internal static void Fill(Double3 eye,float seed,float boundary,float detail,Vector4[] high,Vector4[] low,Vector4[] phase){
            var seeds=new Vector3(seed,seed*.37f,-seed*.61f);
            for(int band=0;band<3;band++){double span=band==0?System.Math.Max(1,boundary):band==1?System.Math.Max(1,detail):96;var h=Vector4.zero;var l=Vector4.zero;var p=Vector4.zero;
                for(int axis=0;axis<3;axis++){double value=(axis==0?eye.x:axis==1?eye.y:eye.z)/span+seeds[axis],cell=System.Math.Floor(value);int integer=(int)cell;h[axis]=integer>>16;l[axis]=integer&65535;p[axis]=(float)(value-cell);}high[band]=h;low[band]=l;phase[band]=p;
            }
        }
    }
    public sealed partial class PlanetDevelopment {
        TerrainSurfaceLibrary renderingSurfaceLibrary;
        readonly Vector4[] texturePhases=new Vector4[81],noiseHigh=new Vector4[3],noiseLow=new Vector4[3],noisePhase=new Vector4[3];
        internal bool NativePlanetDoubles=>NativePlanetDoublesFor(definition);
        internal bool NativePlanetDoublesFor(PlanetDefinition body)=>body.experimentalGpuDoublePrecision&&body.radius>=body.gpuDoubleMinimumRadius&&(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D12);
        internal void BindInstancePrecision(Material material){var observer=origin.Origin.RelativeTo(definition.position);var high=observer.Float;material.SetVector("_PlanetObserverHigh",high);material.SetVector("_PlanetObserverLow",(observer-new Double3(high)).Float);material.SetVectorArray("_PlanetPrecisionUv",terrainPrecisionUv);material.SetVectorArray("_PlanetPrecisionBase",terrainPrecisionBase);}
        static Vector3 OceanWaveDirection(int wave)=>wave==0?new Vector3(.940721f,.282216f,.188144f):wave==1?new Vector3(-.182574f,.912871f,.365148f):wave==2?new Vector3(.303046f,-.505076f,.808122f):new Vector3(.744208f,.620174f,-.248069f);
        static float OceanWaveLength(int wave)=>wave==0?1f:wave==1?.61f:wave==2?.37f:.23f;
        void BindRenderingPrecision(Material material){
            if(NativePlanetDoubles)material.EnableKeyword("PLANET_GPU_DOUBLE");else material.DisableKeyword("PLANET_GPU_DOUBLE");
            var observer=origin.Origin.RelativeTo(definition.position);var high=observer.Float;
            material.SetVector("_PlanetObserverHigh",high);material.SetVector("_PlanetObserverLow",(observer-new Double3(high)).Float);
            renderingSurfaceLibrary??=Resources.Load<TerrainSurfaceLibrary>("MassiveArmy/TerrainArt/SurfaceLibrary");if(renderingSurfaceLibrary==null)return;
            // Wrapped phases are computed before converting to float. Sample positions
            // stay camera-relative even on a 1000x body, without sliding the textures.
            var eye=RenderToBody(view.transform.position);
            for(int band=0;band<9;band++)for(int layer=0;layer<9;layer++){
                double span=System.Math.Max(.1,renderingSurfaceLibrary.layerParameters[layer].x)*System.Math.Max(.01,renderingSurfaceLibrary.textureScale)*System.Math.Pow(System.Math.Max(1.5,renderingSurfaceLibrary.textureBandScale),band);
                float Phase(double value)=>(float)(value/span-System.Math.Floor(value/span));
                texturePhases[band*9+layer]=new Vector4(Phase(eye.x),Phase(eye.y),Phase(eye.z),0);
            }
            PlanetTerrainNoiseAnchor.Fill(eye,(definition.terrainSeed%1009)*.017f,renderingSurfaceLibrary.boundaryNoiseScale,renderingSurfaceLibrary.patchScale,noiseHigh,noiseLow,noisePhase);
            material.SetVectorArray("_TerrainNoiseHigh",noiseHigh);material.SetVectorArray("_TerrainNoiseLow",noiseLow);material.SetVectorArray("_TerrainNoisePhase",noisePhase);material.SetFloat("_TerrainNoiseAnchored",1);
            material.SetVector("_OceanWaves",new Vector4(definition.oceanWaveHeight,definition.oceanWaveLength,definition.oceanWaveSpeed,definition.oceanShoreAttenuationDepth));
            material.SetFloat("_OceanFoamDepth",definition.oceanFoamDepth);material.SetFloat("_OceanSteepness",definition.oceanWaveSteepness);
            material.SetVector("_OceanFoam",new Vector4(definition.oceanFoamWidth,definition.oceanFoamStrength,definition.oceanFoamNoiseScale,0));
            var oceanPhase=new Vector4();
            for(int wave=0;wave<4;wave++){
                var direction=OceanWaveDirection(wave);
                // Match the shader's float wave number before wrapping in double;
                // otherwise rebasing a very large planet introduces a phase jump.
                double k=6.2831853f/(Mathf.Max(4,definition.oceanWaveLength)*OceanWaveLength(wave));
                double phase=(observer.x*direction.x+observer.y*direction.y+observer.z*direction.z-Time.timeAsDouble*definition.oceanWaveSpeed*System.Math.Sqrt(OceanWaveLength(wave)))*k;
                double period=System.Math.PI*2;oceanPhase[wave]=(float)(phase-System.Math.Floor(phase/period)*period);
            }
            material.SetVector("_OceanPhase",oceanPhase);
            double foamSpan=System.Math.Max(.1,definition.oceanFoamNoiseScale)*256;
            float FoamPhase(double value)=>(float)((value/foamSpan-System.Math.Floor(value/foamSpan))*256);
            material.SetVector("_OceanFoamPhase",new Vector4(FoamPhase(observer.x),FoamPhase(observer.y),FoamPhase(observer.z),0));
            material.SetVectorArray("_TerrainTexturePhases",texturePhases);material.SetVector("_PlanetTextureCamera",view.transform.position);
        }
    }
}
