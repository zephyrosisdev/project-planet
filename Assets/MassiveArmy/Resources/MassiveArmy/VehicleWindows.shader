Shader "MassiveArmy/Vehicle windows" {
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 struct V {float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 world:TEXCOORD1;};
 V Vert(float4 p:POSITION,float3 n:NORMAL){V o;o.p=UnityObjectToClipPos(p);o.world=mul(unity_ObjectToWorld,p).xyz;o.n=UnityObjectToWorldNormal(n);return o;}
 float4 Frag(V i):SV_Target {float edge=pow(1-abs(dot(normalize(i.n),normalize(_WorldSpaceCameraPos-i.world))),4);return float4(.22,.44,.5,.035+edge*.15);}
 ENDHLSL
 }}}
