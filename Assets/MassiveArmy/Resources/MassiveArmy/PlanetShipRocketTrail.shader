Shader "MassiveArmy/Ship rocket exhaust" {SubShader {Tags {"Queue"="Transparent+40" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 StructuredBuffer<float4> _RocketPoints;float3 _RocketOrigin;float4 _RocketScreen;float4x4 _RocketVP;
 struct Varying{float4 position:SV_POSITION;float alpha:TEXCOORD0;};
 Varying Vert(uint id:SV_VertexID){uint segment=id/6,corner=id%6;const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};float4 tail=_RocketPoints[segment*2],head=_RocketPoints[segment*2+1];float4 a=mul(_RocketVP,float4(_RocketOrigin+tail.xyz,1)),b=mul(_RocketVP,float4(_RocketOrigin+head.xyz,1));Varying o;
 if(a.w<_RocketScreen.z&&b.w<_RocketScreen.z){o.position=float4(2,2,2,1);o.alpha=0;return o;}
 if(a.w<_RocketScreen.z)a=lerp(a,b,(_RocketScreen.z-a.w)/(b.w-a.w));if(b.w<_RocketScreen.z)b=lerp(b,a,(_RocketScreen.z-b.w)/(a.w-b.w));
 float2 delta=(b.xy/b.w-a.xy/a.w)*_RocketScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(length(delta),.001);o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*3/_RocketScreen.xy*o.position.w;o.alpha=ends[corner]==0?tail.w:head.w;return o;}
 float4 Frag(Varying input):SV_Target{return float4(2,.55,.04,input.alpha);}
 ENDHLSL
 }}}
