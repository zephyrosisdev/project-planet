Shader "MassiveArmy/Ghost" {
 Properties {_Tint("Tint",Color)=(0,1,0,.3)}
 SubShader {Tags {"Queue"="Transparent+90" "RenderType"="Transparent"} Pass {
 Tags {"LightMode"="SRPDefaultUnlit"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "PlanetStructureGrid.hlsl"
 float4 _Tint;
 struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;};
 V Vert(float4 p:POSITION){p.xyz=PlanetStructurePosition(p.xyz);V o;o.p=UnityObjectToClipPos(p);o.world=mul(unity_ObjectToWorld,p).xyz;return o;}
 float4 Frag(V i):SV_Target{return float4(_Tint.rgb*(1+step(.85,frac(i.world.y*.4))),.35);}
 ENDHLSL
 }}
}
