Shader "MassiveArmy/Indirect biome scenery"
{
    SubShader { Tags { "RenderType"="Opaque" }
        Pass { Tags { "LightMode"="SRPDefaultUnlit" } Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "WorldTopology.hlsl"
            #include "ArtLighting.hlsl"
            float _WorldSize;float3 _RenderFocus;
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            struct Instance { float4 positionScale;float4 tintYaw; };
            StructuredBuffer<Instance> _Instances;StructuredBuffer<uint> _Visible;
            float4x4 _BattleVP;
            struct Varyings { float4 position:SV_POSITION;float3 colour:COLOR; };
            Varyings Vert(appdata_full v,uint instance:SV_InstanceID) {
                InitIndirectDrawArgs(0);Instance p=_Instances[_Visible[GetIndirectInstanceID(instance)]];
                float c=cos(p.tintYaw.w),s=sin(p.tintYaw.w);float3 vertex=v.vertex.xyz;vertex.xz=float2(c*vertex.x-s*vertex.z,s*vertex.x+c*vertex.z);
                float3 normal=v.normal;normal.xz=float2(c*normal.x-s*normal.z,s*normal.x+c*normal.z);
                Varyings o;o.position=mul(_BattleVP,float4(NearestWorld(p.positionScale.xyz,_RenderFocus,_WorldSize)+vertex*p.positionScale.w,1));
                o.colour=p.tintYaw.rgb*ArmyDiffuse(normal)*lerp(.6,1.15,v.color.r);return o;
            }
            float4 Frag(Varyings i):SV_Target { return float4(i.colour,1); }
            ENDHLSL
        }
        Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "WorldTopology.hlsl"
            struct Instance {float4 positionScale;float4 tintYaw;};
            StructuredBuffer<Instance> _Instances;StructuredBuffer<uint> _Visible;float _WorldSize;float3 _RenderFocus;
            float4 ShadowVert(float3 vertex:POSITION,uint instance:SV_InstanceID):SV_POSITION{
                InitIndirectDrawArgs(0);Instance p=_Instances[_Visible[GetIndirectInstanceID(instance)]];
                float c=cos(p.tintYaw.w),s=sin(p.tintYaw.w);vertex.xz=float2(c*vertex.x-s*vertex.z,s*vertex.x+c*vertex.z);
                float3 world=NearestWorld(p.positionScale.xyz,_RenderFocus,_WorldSize)+vertex*p.positionScale.w;
                return TransformWorldToHClip(GetCameraRelativePositionWS(world));
            }
            float4 ShadowFrag():SV_Target{return 0;}
            ENDHLSL
        }
    }
}
