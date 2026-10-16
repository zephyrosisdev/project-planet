Shader "MassiveArmy/Procedural terrain"
{
    Properties
    {
        _HeightBlendWidth ("Height transition width", Range(.01,1)) = .22
        _HeightBlendStrength ("Height blending", Range(0,1)) = .8
        _NormalStrength ("Surface relief", Range(0,2)) = .85
        _MacroVariation ("Large scale colour variation", Range(0,.3)) = .12
        [NoScaleOffset] _SurfaceAlbedo ("Biome albedo array", 2DArray) = "" {}
        [NoScaleOffset] _SurfaceData ("Normal XY / Height / Roughness array", 2DArray) = "" {}
        [HideInInspector] _ZClip ("Shadow Z clip", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "DepthForwardOnly" Tags { "LightMode"="DepthForwardOnly" }
            Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            float4 DepthVert(float3 positionOS:POSITION):SV_POSITION { return TransformWorldToHClip(TransformObjectToWorld(positionOS)); }
            float4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "ForwardOnly" Tags { "LightMode"="ForwardOnly" }
            Cull Off ZWrite Off ZTest Equal
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile_fragment SCREEN_SPACE_SHADOWS_OFF SCREEN_SPACE_SHADOWS_ON
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma multi_compile_fragment USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
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
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/ShaderPass/LitSharePass.hlsl"
            #define UNITY_DECLARE_TEX2DARRAY(tex) TEXTURE2D_ARRAY(tex);SAMPLER(sampler##tex)
            #include "TerrainSurface.hlsl"
            #include "ResourceDecals.hlsl"
            #include "CombatDecals.hlsl"
            void GetSurfaceAndBuiltinData(FragInputs input,float3 V,inout PositionInputs posInput,out SurfaceData surfaceData,out BuiltinData builtinData)
            {
                TerrainInput terrain;
                terrain.position=input.positionSS;terrain.uv=input.texCoord0.xy;
                terrain.normal=input.tangentToWorld[2];terrain.world=GetAbsolutePositionWS(input.positionRWS);
                float3 normal;float roughness;float4 sample=EvaluateTerrain(terrain,normal,roughness);
                float3 geometry=normalize(terrain.normal);float2 depositRelief;
                float4 deposit=ResourceDecal(terrain.world.xz,depositRelief);sample.rgb=lerp(sample.rgb,deposit.rgb,deposit.a);
                if(deposit.a>.0001)normal=normalize(geometry+(normal-geometry)*(1-deposit.a)+float3(depositRelief.x,0,depositRelief.y)*max(.1,geometry.y));
                float2 relief;float4 remains=CombatDecal(terrain.world.xz,relief);sample.rgb=lerp(sample.rgb,remains.rgb,remains.a);
                if(remains.a>.0001)normal=normalize(geometry+(normal-geometry)*(1-remains.a)+float3(relief.x,0,relief.y)*max(.1,geometry.y));
                ZERO_INITIALIZE(SurfaceData,surfaceData);
                surfaceData.materialFeatures=MATERIALFEATUREFLAGS_LIT_STANDARD;
                surfaceData.baseColor=sample.rgb;surfaceData.normalWS=normal;
                surfaceData.geomNormalWS=geometry;
                // Generated terrain meshes need no authored tangents: the surface maps use XZ.
                float3 n=surfaceData.geomNormalWS;
                surfaceData.tangentWS=normalize(abs(n.x)<.99?float3(1,0,0)-n*n.x:cross(float3(0,0,1),n));
                surfaceData.perceptualSmoothness=1-saturate(roughness);
                surfaceData.ambientOcclusion=1;surfaceData.specularOcclusion=1;
                InitBuiltinData(posInput,1,normal,-surfaceData.geomNormalWS,input.texCoord1,input.texCoord2,builtinData);
                PostInitBuiltinData(V,posInput,surfaceData,builtinData);
            }
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl"
            ENDHLSL
        }
        // Only explicit offscreen material tests draw this pass. Scene cameras use ForwardOnly.
        Pass
        {
            Name "TerrainValidation" Tags { "LightMode"="TerrainValidation" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ValidationVert
            #pragma fragment ValidationFrag
            #include "UnityCG.cginc"
            #include "TerrainSurface.hlsl"
            TerrainInput ValidationVert(appdata_base v)
            {
                TerrainInput o;o.position=UnityObjectToClipPos(v.vertex);o.uv=v.texcoord;
                o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o;
            }
            float4 ValidationFrag(TerrainInput i):SV_Target
            {
                float3 normal;float roughness;float4 sample=EvaluateTerrain(i,normal,roughness);
                if(_BlendDebug>.5)return sample;
                float3 lightDirection=normalize(float3(.38,.74,-.55));
                return float4(sample.rgb*(.44+.56*saturate(dot(normal,lightDirection))),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ColorMask 0 ZClip [_ZClip]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            float4 ShadowVert(float3 positionOS:POSITION):SV_POSITION { return TransformWorldToHClip(TransformObjectToWorld(positionOS)); }
            float4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
