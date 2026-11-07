Shader "MassiveArmy/Planet vehicle proxy" {
 Properties {_MainTex("Vehicle albedo",2D)="white"{}}
 SubShader {Tags {"RenderType"="Opaque"} Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Back ZWrite On
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
 #define PLANET_SPECIALIZE_TERRAIN 1
 #include "UnityCG.cginc"
 #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
 #include "UnityIndirect.cginc"
 #define PLANET_VEHICLES 1
#include "ArmyCommon.hlsl"
 #include "ArtLighting.hlsl"
 #include "PlanetTopology.hlsl"
 #include "PlanetSurface.hlsl"
 StructuredBuffer<float4x4> _VehiclePoses;StructuredBuffer<UnitState> _PlanetUnits;StructuredBuffer<uint> _VehicleInstances;
 sampler2D _MainTex;
 float3 _PlanetCenter;float4x4 _VehicleVP;
 struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float3 up:TEXCOORD1;float4 colour:COLOR;float2 uv:TEXCOORD2;};
 V Vert(float3 p:POSITION,float3 n:NORMAL,float4 colour:COLOR,float2 uv:TEXCOORD0,uint instance:SV_InstanceID){
  InitIndirectDrawArgs(0);uint slot=GetIndirectInstanceID(instance);UnitState u=_PlanetUnits[_VehicleInstances[slot]];
  float4x4 pose=_VehiclePoses[slot];float3 right=mul((float3x3)pose,float3(1,0,0)),up=mul((float3x3)pose,float3(0,1,0)),forward=mul((float3x3)pose,float3(0,0,1));
  float3 world=mul(pose,float4(p,1)).xyz;
  V o;o.p=mul(_VehicleVP,float4(world,1));if((u.flags&ALIVE)==0||(u.flags&VEHICLE_EMBARKED)!=0)o.p=float4(2,2,2,1);
  o.n=right*n.x+up*n.y+forward*n.z;o.up=up;o.colour=colour;o.uv=uv;return o;
 }
 float4 Frag(V i):SV_Target {float day=smoothstep(-.06,.12,dot(i.up,_ArmyLightDirection));return float4(tex2D(_MainTex,i.uv).rgb*i.colour.rgb*(_ArmyAmbientLight*.22+_ArmyDirectLight*saturate(dot(normalize(i.n),_ArmyLightDirection))*day),1);}
 ENDHLSL
 }}}
