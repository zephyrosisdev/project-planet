Shader "Synthetic Front/Blueprint preview" {
 Properties {_Valid("Valid placement",Float)=1}
 SubShader {Tags {"Queue"="Transparent" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest LEqual Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 float _Valid;
 struct Input {float3 position:POSITION;float3 normal:NORMAL;};
 struct Varying {float4 position:SV_POSITION;float3 local:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2;};
 Varying Vert(Input i){Varying o;o.position=UnityObjectToClipPos(float4(i.position,1));o.local=i.position;o.normal=UnityObjectToWorldNormal(i.normal);o.world=mul(unity_ObjectToWorld,float4(i.position,1)).xyz;return o;}
 float4 Frag(Varying i):SV_Target{float3 colour=lerp(float3(1,.045,.02),float3(.015,1,.38),step(.5,_Valid));float3 gridDistance=abs(frac(i.local*4-.5)-.5)/max(fwidth(i.local*4),.001);float grid=1-saturate(min(gridDistance.x,min(gridDistance.y,gridDistance.z)));float fresnel=pow(1-abs(dot(normalize(i.normal),normalize(_WorldSpaceCameraPos-i.world))),3);float scan=smoothstep(.84,1,frac(i.local.y*.45-_Time.y*.4));return float4(colour*(.6+grid*.65+fresnel*.9+scan*.25),saturate(.18+grid*.2+fresnel*.4+scan*.12));}
 ENDHLSL
 }}}
