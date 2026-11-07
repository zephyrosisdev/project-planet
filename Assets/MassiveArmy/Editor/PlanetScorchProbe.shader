Shader "Hidden/MassiveArmy/Scorch probe" {SubShader {Pass {Cull Off ZWrite Off ZTest Always
HLSLPROGRAM
#pragma target 5.0
#pragma vertex Vert
#pragma fragment Frag
#include "UnityCG.cginc"
#include "../Resources/MassiveArmy/PlanetSurface.hlsl"
#include "../Resources/MassiveArmy/PlanetCombatDecals.hlsl"
struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
V Vert(uint id:SV_VertexID){V o;o.uv=float2((id<<1)&2,id&2);o.position=float4(o.uv*2-1,0,1);return o;}
float4 Frag(V i):SV_Target {float3 up=normalize(float3(1,(i.uv.y-.5)*600/_PlanetRadius,(i.uv.x-.5)*600/_PlanetRadius));float3 normal=up;return float4(PlanetNuclearScorch(up,normal),1);}
ENDHLSL
}}}
