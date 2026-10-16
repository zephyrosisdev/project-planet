Shader "MassiveArmy/Planet development terrain"
{
    Properties {
        [HideInInspector] _PlanetRadius ("Planet radius", Float) = 500
        [HideInInspector] _PlanetLightingDebug ("Render validation", Float) = 0
        [NoScaleOffset] _SurfaceAlbedo ("Biome albedo array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceData ("Normal height roughness array", 2DArray) = "" {}
    }
    SubShader {Tags {"RenderType"="Opaque" "Queue"="Geometry"}
        Pass {Name "PlanetLighting" Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
#pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
#define PLANET_SPECIALIZE_TERRAIN 1
            #pragma target 5.0
            #pragma vertex Vert
            #pragma multi_compile_vertex _ PLANET_CBT
            #pragma multi_compile_vertex _ PLANET_STREAMED
            #pragma multi_compile _ PLANET_CBT_DEBUG
            #pragma fragment Frag
            #pragma multi_compile_fragment _ PLANET_SURFACE_TEXTURES
            #pragma multi_compile_fragment _ PLANET_COMBAT_DECALS
            #pragma multi_compile_fragment _ PLANET_RESOURCE_DECALS
            #define SCREEN_SPACE_SHADOWS_OFF
            #define PUNCTUAL_SHADOW_LOW
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #define AREA_SHADOW_MEDIUM
            #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
            #if defined(SHADER_STAGE_VERTEX)
            #include "UnityCG.cginc"
            #include "PlanetTopology.hlsl"
            #include "PlanetSurface.hlsl"
            #include "PlanetTerrainGeometry.hlsl"
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
            #include "PlanetTopology.hlsl"
            #include "PlanetSurface.hlsl"
            #include "PlanetTerrainGeometry.hlsl"
            #ifdef PLANET_SURFACE_TEXTURES
            #include "PlanetTerrainSurface.hlsl"
            #endif
            #ifdef PLANET_COMBAT_DECALS
            #include "PlanetCombatDecals.hlsl"
            #endif
            #ifdef PLANET_RESOURCE_DECALS
            #include "PlanetResourceDecals.hlsl"
            #endif
            float _PlanetLightingDebug;
            float4 Frag(Varying i):SV_Target {float3 d=normalize(i.direction);float h=PlanetHeight(d);float2 climate=PlanetClimate(d);float t=climate.x,m=climate.y;uint biome;
                if(_OceanOnly!=0)biome=0;else if(h<_PlanetOcean)biome=0;else if(h<_PlanetOcean+1)biome=4;else if(t<.14)biome=19;else if(t<.3)biome=16;
                else if(t>.68)biome=m<.38?8:m<.53?7:m<.68?6:5;else biome=m<.3?9:m<.5?10:m>.7?12:11;
                float3 normal=normalize(i.normal),albedo=_TintOverride.a>0?_TintOverride.rgb:_BiomeColours[biome].rgb;float roughness=1;
                #ifdef PLANET_SURFACE_TEXTURES
                if(_OceanOnly==0&&_TintOverride.a==0)albedo=EvaluatePlanetTerrain(d,h,normal,normal,roughness);
                #endif
                #ifdef PLANET_RESOURCE_DECALS
                if(_OceanOnly==0){float4 deposit=PlanetResourceDecal(d,h,normal);albedo=lerp(albedo,deposit.rgb,deposit.a);}
                #endif
                #ifdef PLANET_COMBAT_DECALS
                if(_OceanOnly==0){float4 remains=PlanetRemainsDecal(d,h,normal);albedo=lerp(albedo,remains.rgb,remains.a);albedo*=PlanetNuclearScorch(d,normal);}
                #endif
                if(_PlanetLightingDebug>.5)return float4(10000,10000,10000,1);
                float3 rws=GetCameraRelativePositionWS(i.world),V=GetWorldSpaceNormalizeViewDir(rws);
                PositionInputs position=GetPositionInput(i.position.xy,_ScreenSize.zw,i.position.z,i.position.w,rws,uint2(i.position.xy)/GetTileSize());
                SurfaceData surface;ZERO_INITIALIZE(SurfaceData,surface);
                surface.materialFeatures=MATERIALFEATUREFLAGS_LIT_STANDARD;surface.baseColor=albedo;surface.normalWS=normal;surface.geomNormalWS=normalize(i.normal);
                surface.tangentWS=normalize(cross(abs(surface.geomNormalWS.y)>.95?float3(1,0,0):float3(0,1,0),surface.geomNormalWS));
                surface.perceptualSmoothness=_OceanOnly!=0?.78:1-saturate(roughness);surface.ambientOcclusion=1;surface.specularOcclusion=1;
                BuiltinData builtin;InitBuiltinData(position,1,normal,-surface.geomNormalWS,0,0,builtin);builtin.renderingLayers=1u;PostInitBuiltinData(V,position,surface,builtin);
                BSDFData bsdf=ConvertSurfaceDataToBSDFData(i.position.xy,surface);PreLightData prelight=GetPreLightData(V,position,bsdf);
                LightLoopOutput lighting;LightLoop(V,position,prelight,bsdf,builtin,LIGHT_FEATURE_MASK_FLAGS_OPAQUE,lighting);
                float3 colour=(lighting.diffuseLighting+lighting.specularLighting)*GetCurrentExposureMultiplier();
                #ifdef PLANET_CBT_DEBUG
                float3 widths=max(fwidth(i.barycentric),.00001);float edge=min(i.barycentric.x/widths.x,min(i.barycentric.y/widths.y,i.barycentric.z/widths.z));
                colour=lerp(float3(.08,.3,.9),float3(1,.25,.04),i.depth)*.55+colour*.3;colour=lerp(float3(.1,1,1),colour,smoothstep(0,1.25,edge));
                #endif
                return float4(colour,1);}
            #endif
            ENDHLSL
        }
        Pass {Name "DepthForwardOnly" Tags {"LightMode"="DepthForwardOnly"} Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
#pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
#define PLANET_SPECIALIZE_TERRAIN 1
            #pragma target 5.0
            #pragma vertex Vert
            #pragma multi_compile_vertex _ PLANET_CBT
            #pragma multi_compile_vertex _ PLANET_STREAMED
            #pragma multi_compile _ PLANET_CBT_DEBUG
            #pragma fragment DepthFrag
            #define PLANET_DEPTH_ONLY
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "PlanetTopology.hlsl"
            #include "PlanetSurface.hlsl"
            #include "PlanetTerrainGeometry.hlsl"
            float4 DepthFrag():SV_Target{return 0;}
            ENDHLSL
        }
    }
}
