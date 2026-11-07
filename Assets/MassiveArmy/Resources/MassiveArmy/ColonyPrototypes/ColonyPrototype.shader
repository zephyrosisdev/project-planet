Shader "MassiveArmy/Colony prototypes" {
 Properties { _Preview("Placement preview",Float)=0 _PreviewTint("Preview tint",Color)=(.2,1,.5,.5) _ZWrite("Depth writing",Float)=1 _ZTest("Depth test",Float)=4 }
 SubShader { Tags { "RenderType"="Opaque" } Pass {
  Tags {"LightMode"="SRPDefaultUnlit"} Cull Back ZWrite [_ZWrite] ZTest [_ZTest] Blend SrcAlpha OneMinusSrcAlpha
  HLSLPROGRAM
  #pragma target 4.5
  #pragma vertex Vert
  #pragma fragment Frag
  #pragma multi_compile_instancing
  #include "UnityCG.cginc"
  #include "../ArtLighting.hlsl"
  float _Preview;float4 _PreviewTint;
  struct Input {float4 vertex:POSITION;float3 normal:NORMAL;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
  struct Output {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float3 up:TEXCOORD1;float4 color:COLOR;};
  Output Vert(Input v){UNITY_SETUP_INSTANCE_ID(v);Output o;o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);o.up=normalize(mul((float3x3)unity_ObjectToWorld,float3(0,1,0)));o.color=v.color;return o;}
  float4 Frag(Output i):SV_Target {float3 n=normalize(i.normal);float daylight=smoothstep(-.06,.12,dot(i.up,_ArmyLightDirection));float sky=lerp(.12,.35,saturate(dot(n,i.up)*.5+.5));float3 lit=i.color.rgb*(_ArmyAmbientLight*sky+_ArmyDirectLight*saturate(dot(n,_ArmyLightDirection))*daylight)+i.color.rgb*i.color.a*1.3;return _Preview>.5?float4(lerp(lit,_PreviewTint.rgb,.75),_PreviewTint.a):float4(lit,1);}
  ENDHLSL
 } }
}
