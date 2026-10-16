Shader "MassiveArmy/Combat surface validation" {
SubShader { Pass { Cull Off ZWrite Off ZTest Always
HLSLPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment Fragment
#include "UnityCG.cginc"
#define TEXTURE2D_ARRAY(name) Texture2DArray name
#define SAMPLER(name) SamplerState name
#define SAMPLE_TEXTURE2D_ARRAY_GRAD(tex,samp,uv,slice,dx,dy) tex.SampleGrad(samp,float3(uv,slice),dx,dy)
float _WorldSize,_TestNormal;
#include "CombatDecals.hlsl"
float4 Fragment(v2f_img input):SV_Target {float2 relief;float4 colour=CombatDecal((input.uv-.5)*8,relief);return _TestNormal>.5?float4(relief,0,1):colour;}
ENDHLSL
} } }
