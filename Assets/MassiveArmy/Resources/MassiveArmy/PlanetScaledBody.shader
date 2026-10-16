Shader "MassiveArmy/Scaled celestial body"
{
 Properties {
  [NoScaleOffset] _SurfaceAlbedo ("Biome albedo array", 2DArray) = "" {}
  [NoScaleOffset] _SurfaceData ("Normal height roughness array", 2DArray) = "" {}
 }
 SubShader {Tags {"RenderType"="Opaque" "Queue"="Geometry-10"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite On
 HLSLPROGRAM
 #pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
 #define PLANET_SPECIALIZE_TERRAIN 1
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #define SCREEN_SPACE_SHADOWS_OFF
 #define PUNCTUAL_SHADOW_LOW
 #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
 #define AREA_SHADOW_MEDIUM
 #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
 #if defined(SHADER_STAGE_VERTEX)
 #include "UnityCG.cginc"
 #else
            #define _DISABLE_DECALS
            #define _DISABLE_SSR
            #define SHADERPASS SHADERPASS_FORWARD
            #define HAS_LIGHTLOOP
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitProperties.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
            #define UNITY_DECLARE_TEX2DARRAY(tex) TEXTURE2D_ARRAY(tex);SAMPLER(sampler##tex)
 #endif
 #include "PlanetTopology.hlsl"
 #include "PlanetSurface.hlsl"
 float4x4 _PlanetVP;float3 _ScaledCentre,_CameraPosition;float _ScaledRadius,_ScaledFactor;
 float4 _ForegroundSphere,_PhysicalBody;
 float _BodyRadius,_BodyOcean,_BodySeed;uint _BodyGrid;int _BodyDebug;
 Texture2DArray<float4> _BodySurface;SamplerState sampler_BodySurface;
 StructuredBuffer<float4> _BiomeColours;
 float4 BodySurface(float3 d){float2 p=PlanetEncode(d);uint f=(uint)floor(p.x);float2 uv=float2(p.x-f,p.y);return _BodySurface.SampleLevel(sampler_BodySurface,float3((uv*128+1)/130,f),0);}
 // Reuse the same material layers, climate, triplanar coordinates and height blending as local terrain.
 // Foreground occlusion keeps its independent surface parameters and stamp descriptors.
 #if !defined(SHADER_STAGE_VERTEX)
 #define _PlanetRadius _BodyRadius
 #define _PlanetOcean _BodyOcean
 #define _PlanetSeed _BodySeed
 #define PlanetClimate(d) PlanetClimateAt(d,_BodySeed)
 #include "PlanetTerrainSurface.hlsl"
 #undef PlanetClimate
 #undef _PlanetRadius
 #undef _PlanetOcean
 #undef _PlanetSeed
 #endif
 struct V {float4 position:SV_POSITION;float3 ray:TEXCOORD0;float3 direction:TEXCOORD1;};
 V Vert(uint id:SV_VertexID){uint n=_BodyGrid,face=id/(n*n*6),cell=(id/6)%(n*n),corner=id%6;
  const uint2 offsets[6]={uint2(0,0),uint2(1,0),uint2(1,1),uint2(0,0),uint2(1,1),uint2(0,1)};
  float3 d=PlanetDirection(face,(float2(cell%n,cell/n)+offsets[corner])*2/n-1);
  float height=max(BodySurface(d).w,_BodyOcean);float3 world=_ScaledCentre+d*(_ScaledRadius+height*_ScaledFactor);
  V o;o.position=mul(_PlanetVP,float4(world,1));o.ray=world-_CameraPosition;o.direction=d;return o;
 }
 float SphereHit(float3 ray,float3 centre,float radius){float along=dot(ray,centre);float3 side=centre-ray*along;float discriminant=radius*radius-dot(side,side);if(discriminant<0)return -1;
  float root=sqrt(discriminant),near=along-root;return near>=0?near:along+root>=0?0:-1;}
 struct Output {float4 colour:SV_Target;float depth:SV_Depth;};
 #if !defined(SHADER_STAGE_VERTEX)
 Output Frag(V i){if(_BodyDebug!=0){Output debug;debug.colour=1;debug.depth=i.position.z;return debug;}float3 ray=normalize(i.ray);float bodyHit=length(i.ray)/max(_ScaledFactor,1e-20);
  if(_ForegroundSphere.w>0){float near=SphereHit(ray,_ForegroundSphere.xyz,_ForegroundSphere.w-_PlanetReliefBound);
   if(near>=0&&near<bodyHit)discard;
   float outer=SphereHit(ray,_ForegroundSphere.xyz,_ForegroundSphere.w+_PlanetReliefBound);
   if(outer>=0&&outer<bodyHit){float t=max(0,dot(ray,_ForegroundSphere.xyz));
    [unroll]for(int k=0;k<3;k++){float3 d=normalize(ray*t-_ForegroundSphere.xyz);float next=SphereHit(ray,_ForegroundSphere.xyz,_ForegroundSphere.w+max(PlanetHeight(d),_PlanetOcean));if(next<0){t=-1;break;}t=next;}
    if(t>=0&&t<bodyHit)discard;
   }
  }
  float3 d=normalize(i.direction);float4 surface=BodySurface(d);float3 normal=normalize(surface.xyz),albedo;float roughness;
  if(surface.w<_BodyOcean){normal=d;albedo=_BiomeColours[0].rgb;}
  else albedo=EvaluatePlanetTerrain(d,surface.w,normal,normal,roughness);
  float3 geometric=normalize(surface.xyz);if(surface.w<_BodyOcean)geometric=d;
  float3 rws=GetCameraRelativePositionWS(i.ray+_CameraPosition),V=GetWorldSpaceNormalizeViewDir(rws);
  PositionInputs position=GetPositionInput(i.position.xy,_ScreenSize.zw,i.position.z,i.position.w,rws,uint2(i.position.xy)/GetTileSize());
  SurfaceData lit;ZERO_INITIALIZE(SurfaceData,lit);lit.materialFeatures=MATERIALFEATUREFLAGS_LIT_STANDARD;lit.baseColor=albedo;lit.normalWS=normal;lit.geomNormalWS=geometric;
  lit.tangentWS=normalize(cross(abs(geometric.y)>.95?float3(1,0,0):float3(0,1,0),geometric));lit.perceptualSmoothness=surface.w<_BodyOcean?.78:1-saturate(roughness);lit.ambientOcclusion=1;lit.specularOcclusion=1;
  BuiltinData builtin;InitBuiltinData(position,1,normal,-geometric,0,0,builtin);builtin.renderingLayers=1u;PostInitBuiltinData(V,position,lit,builtin);
  BSDFData bsdf=ConvertSurfaceDataToBSDFData(i.position.xy,lit);PreLightData prelight=GetPreLightData(V,position,bsdf);
  LightLoopOutput lighting;LightLoop(V,position,prelight,bsdf,builtin,LIGHT_FEATURE_MASK_FLAGS_OPAQUE,lighting);
  Output o;o.colour=float4((lighting.diffuseLighting+lighting.specularLighting)*GetCurrentExposureMultiplier(),1);
  // A common monotonic distance mapping keeps separate scaled planets in physical front/back order.
  float distance=10000*bodyHit/(10000+bodyHit);float4 depthClip=mul(_PlanetVP,float4(_CameraPosition+ray*distance,1));o.depth=depthClip.z/depthClip.w;return o;
 }
 #endif
 ENDHLSL
 }
 }
}
