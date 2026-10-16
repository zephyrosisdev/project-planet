Shader "MassiveArmy/Strategic Missiles" {
 SubShader {Tags {"Queue"="Geometry+10" "RenderType"="Opaque"} Pass {
 Tags {"LightMode"="SRPDefaultUnlit"} ZWrite On Cull Back
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArmyCommon.hlsl"
 #include "ArtLighting.hlsl"
 StructuredBuffer<ProjectileState> _Missiles;StructuredBuffer<uint> _Fog;uint _FogRes,_Spectator;float _WorldSize;float3 _RenderFocus;
 struct V{float4 p:SV_POSITION;float4 colour:TEXCOORD0;};
 V Vert(float4 p:POSITION,float3 normal:NORMAL,float4 colour:COLOR,uint instance:SV_InstanceID){
 ProjectileState m=_Missiles[instance];V o;if(m.alive==0){o.p=float4(2,2,2,1);o.colour=0;return o;}float phase=saturate(1-m.lifetime/12);
 float2 delta=WorldDelta(m.position.xz,m.velocity.xz,_WorldSize)/max(.25,m.lifetime);
 float3 up=normalize(float3(delta.x,cos(phase*3.14159265)*92,delta.y)+float3(0,.001,0));
 float3 right=normalize(cross(up,abs(up.y)>.95?float3(0,0,1):float3(0,1,0))),forward=cross(right,up);
 float3 world=NearestWorld(m.position,_RenderFocus,_WorldSize)+right*p.x+up*p.y+forward*p.z;o.p=UnityWorldToClipPos(world);
 float3 n=right*normal.x+up*normal.y+forward*normal.z;float3 shade=ArmyDiffuse(n);
 bool marking=colour.r>.9&&colour.g<.1;float3 tint=marking?(m.team==0?float3(.05,1,1.5):float3(1.5,.1,.06)):colour.rgb;
 uint2 c=(uint2)(WorldUV(m.position.xz,_WorldSize)*_FogRes);bool known=_Spectator!=0||m.team==0||_Fog[c.x+c.y*_FogRes]==2;
 o.colour=float4(tint*shade,m.alive!=0&&known?1:0);return o;}
 float4 Frag(V i):SV_Target{clip(i.colour.a-.5);return i.colour;}
 ENDHLSL
 }}
}
