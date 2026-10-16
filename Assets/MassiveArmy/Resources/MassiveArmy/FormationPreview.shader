Shader "MassiveArmy/Formation ghosts"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ArmyCommon.hlsl"
            StructuredBuffer<float4> _PreviewSlots;
            StructuredBuffer<GroupOrder> _Groups;
            Texture2D<float4> _Terrain;SamplerState sampler_Terrain;
            float _WorldSize;float3 _RenderFocus;float4x4 _BattleVP;
            struct Attributes { float3 vertex:POSITION;float2 uv:TEXCOORD0; };
            struct Varyings { float4 position:SV_POSITION;float2 uv:TEXCOORD0;float blocked:TEXCOORD1; };
            Varyings Vert(Attributes v,uint instance:SV_InstanceID)
            {
                InitIndirectDrawArgs(0);uint index=GetIndirectInstanceID(instance);float4 slot=_PreviewSlots[index];
                GroupOrder g=_Groups[GROUP_COUNT];float2 forward=float2(-g.right.y,g.right.x);
                float2 p=slot.xy+(g.right*v.vertex.x+forward*v.vertex.y)*2.5;
                float height=_Terrain.SampleLevel(sampler_Terrain,p/_WorldSize+.5,0).x+1.7;
                Varyings o;o.position=mul(_BattleVP,float4(NearestWorld(float3(p.x,height,p.y),_RenderFocus,_WorldSize),1));o.uv=v.uv;o.blocked=slot.z;return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float edge=max(abs(i.uv.x-.5),abs(i.uv.y-.5));clip(edge-.34);
                return float4(lerp(float3(.1,1,.75),float3(1,.2,.15),i.blocked),.8);
            }
            ENDHLSL
        }
    }
}
