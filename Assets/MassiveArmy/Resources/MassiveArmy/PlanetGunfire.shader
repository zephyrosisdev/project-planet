Shader "MassiveArmy/Planet gunfire" {SubShader {Tags {"RenderType"="Transparent"}
Pass {Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha One
HLSLPROGRAM
#pragma target 5.0
#pragma vertex Vert
#pragma fragment Frag
struct Shot {float4 start,end,data;};StructuredBuffer<Shot> _Shots;
float _ShotTick,_ShotRadius;float3 _ShotCentre;float4x4 _ShotVP;float4 _ShotScreen;
struct V {float4 position:SV_POSITION;float fade:TEXCOORD0;};
V Vert(uint id:SV_VertexID){Shot shot=_Shots[id/6];V o;o.fade=0;o.position=float4(2,2,2,1);float age=_ShotTick-shot.data.x;if(shot.data.z==0||age<0||age>6)return o;
float4 a=mul(_ShotVP,float4(_ShotCentre+shot.start.xyz*(_ShotRadius+shot.start.w),1)),b=mul(_ShotVP,float4(_ShotCentre+shot.end.xyz*(_ShotRadius+shot.end.w),1));if(a.w<=0||b.w<=0)return o;
const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};uint corner=id%6;float2 delta=(b.xy/b.w-a.xy/a.w)*_ShotScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(.001,length(delta));o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*1.8/_ShotScreen.xy*o.position.w;o.fade=1-age/7;return o;}
float4 Frag(V i):SV_Target{return float4(4,2.3,.6,i.fade*.8);}
ENDHLSL
}}}
