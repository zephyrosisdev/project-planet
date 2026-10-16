Shader "MassiveArmy/Fog" {
 SubShader {Tags {"Queue"="Transparent+100" "RenderType"="Transparent"} Pass {
 Tags {"LightMode"="SRPDefaultUnlit"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 StructuredBuffer<uint> _Fog;uint _FogRes;float _WorldSize;
 struct V {float4 p:SV_POSITION;float2 world:TEXCOORD0;};
 V Vert(float4 p:POSITION){V o;o.p=UnityObjectToClipPos(p);o.world=mul(unity_ObjectToWorld,p).xz;return o;}
 float4 Frag(V i):SV_Target{uint2 c=(uint2)clamp((i.world/_WorldSize+.5)*_FogRes,0,_FogRes-1);uint state=_Fog[c.x+c.y*_FogRes];return float4(.007,.018,.035,state==0?.97:state==1?.65:0);}
 ENDHLSL
 }}
}
