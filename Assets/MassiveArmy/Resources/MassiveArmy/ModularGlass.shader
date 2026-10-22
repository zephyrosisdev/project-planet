Shader "MassiveArmy/Modular glass" {SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"} Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 view:TEXCOORD1;};
 V Vert(float4 p:POSITION,float3 n:NORMAL){V o;o.p=UnityObjectToClipPos(p);o.n=UnityObjectToWorldNormal(n);o.view=_WorldSpaceCameraPos-mul(unity_ObjectToWorld,p).xyz;return o;}
 float4 Frag(V i):SV_Target{float f=pow(1-saturate(abs(dot(normalize(i.n),normalize(i.view)))),3);float night=1-smoothstep(-.08,.18,dot(normalize(mul((float3x3)unity_ObjectToWorld,float3(0,1,0))),_ArmyLightDirection));return float4(lerp(float3(.25,.48,.58),float3(1,.64,.25),night),lerp(.12+.28*f,.65,night));}
 ENDHLSL
 }}}
