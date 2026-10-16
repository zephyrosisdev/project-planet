Shader "MassiveArmy/Structure" {
 Properties { _Tint("Tint",Color)=(.1,.2,.3,1) }
 SubShader {Tags {"RenderType"="Opaque"} Pass {
 Tags {"LightMode"="SRPDefaultUnlit"} HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 float4 _Tint;
 struct V {float4 p:SV_POSITION;float3 shade:TEXCOORD0;};
 V Vert(float4 p:POSITION,float3 n:NORMAL){V o;o.p=UnityObjectToClipPos(p);o.shade=ArmyDiffuse(UnityObjectToWorldNormal(n));return o;}
 float4 Frag(V i):SV_Target{return float4(_Tint.rgb*i.shade,1);}
 ENDHLSL
 }}
}
