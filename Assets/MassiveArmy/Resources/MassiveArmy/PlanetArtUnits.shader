Shader "MassiveArmy/Planet catalogue units" {
 SubShader {Tags {"RenderType"="Opaque" "Queue"="Geometry+10"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite On
 HLSLPROGRAM
#pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
#define PLANET_SPECIALIZE_TERRAIN 1
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
 #include "UnityIndirect.cginc"
 #include "ArmyCommon.hlsl"
 #include "ArtLighting.hlsl"
 #include "PlanetTopology.hlsl"
 #include "PlanetSurface.hlsl"
 struct ArtVertex{float4 position,normal,tangent,uv;};
 StructuredBuffer<ArtVertex> _ArtVertices;StructuredBuffer<float4> _ArtBounds;
 StructuredBuffer<UnitState> _PlanetUnits;StructuredBuffer<UnitTraits> _Traits;StructuredBuffer<uint> _Visible;
 UNITY_DECLARE_TEX2DARRAY(_ArtAlbedo);UNITY_DECLARE_TEX2DARRAY(_ArtMetallic);UNITY_DECLARE_TEX2DARRAY(_ArtNormal);UNITY_DECLARE_TEX2DARRAY(_ArtImpostors);
 uint _Mode,_Capacity,_ArtStride;float _TimeSeconds;float3 _PlanetCenter,_CameraPosition,_CameraRight,_CameraUp;float4x4 _PlanetVP;
 float2 Rotate(float2 p,float a){float s=sin(a),c=cos(a);return float2(c*p.x+s*p.y,-s*p.x+c*p.y);}
 struct A{float3 p:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 uv:TEXCOORD1;float3 n:TEXCOORD2;float3 t:TEXCOORD3;float3 b:TEXCOORD4;float3 team:TEXCOORD5;float zombie:TEXCOORD6;};
 V Vert(A input,uint instance:SV_InstanceID,uint vertexId:SV_VertexID){
  InitIndirectDrawArgs(0);UnitState u=_PlanetUnits[_Visible[_Mode*_Capacity+GetIndirectInstanceID(instance)]];uint type=Type(u);UnitTraits traits=_Traits[type];
  float3 up=PlanetDecode(u.position);uint face=min((uint)floor(u.position.x),5u);float3 right=normalize(PlanetU[face]-up*dot(PlanetU[face],up)),forward=cross(right,up);
  float height=PlanetHeight(up);if(traits.scale.w==4)height=max(height,_PlanetOcean);height+=traits.altitude;
  float3 centre=_PlanetCenter+up*(_PlanetRadius+height+.12);
  V o;o.zombie=0;o.team=Team(u)==0?float3(.02,.72,1):Team(u)==2?float3(.34,.55,.10):float3(1,.035,.12);if((u.flags&SELECTED)!=0)o.team=lerp(o.team,float3(.25,1,.55),.75);
  o.n=up;o.t=right;o.b=forward;
  if(_Mode==2){float3 delta=_CameraPosition-centre;float angle=atan2(dot(delta,right),dot(delta,forward));uint direction=(uint)floor(frac((angle-u.facing)/6.2831853+1)*8+.5)%8;
   float4 bounds=_ArtBounds[type];o.world=centre+up*bounds.y+_CameraRight*input.p.x*bounds.x+_CameraUp*input.p.y*bounds.x;o.uv=float3(input.uv,type*8+direction);if(Team(u)==2)o.zombie=1;
  }else{
   ArtVertex v=_ArtVertices[type*_ArtStride+vertexId];float angle=v.position.w==1?u.turret:u.facing;if(v.position.w==4)angle+=_TimeSeconds*22;
   float3 p=v.position.xyz;if(Team(u)==2){o.zombie=1+v.uv.z;float phase=_TimeSeconds*12+ArmyRandom(GetIndirectInstanceID(instance))*6.2831853;float stride=saturate(length(u.velocity)/8);p.z+=sin(phase+(v.position.w-10)*1.5708)*.18*stride*saturate(1-p.y/1.1);p.y+=abs(sin(phase))*.035*stride;}p.xz=Rotate(p.xz,angle);o.world=centre+right*p.x+up*p.y+forward*p.z;o.uv=float3(v.uv.xy,type);
   float3 n=v.normal.xyz,t=v.tangent.xyz;n.xz=Rotate(n.xz,angle);t.xz=Rotate(t.xz,angle);o.n=right*n.x+up*n.y+forward*n.z;o.t=right*t.x+up*t.y+forward*t.z;o.b=cross(o.n,o.t)*v.tangent.w;
  }o.p=mul(_PlanetVP,float4(o.world,1));return o;
 }
 float4 Frag(V i):SV_Target{
  if(_Mode==2){if(i.zombie>0){float2 p=(i.uv.xy-.5)*3.4;bool head=length(p-float2(0,.82))<.24;bool torso=abs(p.x)<.31&&p.y>-.1&&p.y<.65;bool legs=(abs(abs(p.x)-.17)<.12)&&p.y>-.95&&p.y<.05;bool arms=abs(abs(p.x)-.45)<.12&&p.y>0&&p.y<.56;clip(head||torso||legs||arms?1:-1);return float4((head||arms?float3(.28,.39,.13):float3(.11,.16,.10))*ArmyImpostorLight(),1);}float4 c=UNITY_SAMPLE_TEX2DARRAY(_ArtImpostors,i.uv);clip(c.a-.3);float tint=saturate((c.b-c.r)*5);c.rgb=lerp(c.rgb,i.team*max(c.g,c.b),tint);return float4(c.rgb*ArmyImpostorLight(),1);}
  if(i.zombie>0){float3 albedo=i.zombie>1.5?float3(.26,.38,.12):float3(.075,.12,.08);return float4(ShadeArmour(i.world,normalize(i.n),albedo,float4(0,1,0,.12),i.team,_CameraPosition),1);}
  float3 n=UNITY_SAMPLE_TEX2DARRAY(_ArtNormal,i.uv).xyz*2-1;n=normalize(i.t*n.x+i.b*n.y+i.n*n.z);
  return float4(ShadeArmour(i.world,n,UNITY_SAMPLE_TEX2DARRAY(_ArtAlbedo,i.uv).rgb,UNITY_SAMPLE_TEX2DARRAY(_ArtMetallic,i.uv),i.team,_CameraPosition),1);
 }
 ENDHLSL
 }}
}
