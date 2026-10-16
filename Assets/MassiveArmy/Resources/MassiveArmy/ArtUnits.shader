Shader "MassiveArmy/Indirect UV shell units" {
 SubShader {
 Tags {"RenderType"="Opaque" "Queue"="Geometry+10"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite On
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
 #include "UnityIndirect.cginc"
 #include "ArmyCommon.hlsl"
 #include "ArtLighting.hlsl"
 struct ArtVertex{float4 position,normal,tangent,uv;};
 StructuredBuffer<ArtVertex> _ArtVertices;StructuredBuffer<float4> _ArtBounds;
 StructuredBuffer<UnitState> _Units;StructuredBuffer<UnitTraits> _Traits;StructuredBuffer<uint> _Visible;
 Texture2D<float4> _Terrain,_WaterMap;SamplerState sampler_Terrain;
 UNITY_DECLARE_TEX2DARRAY(_ArtAlbedo);UNITY_DECLARE_TEX2DARRAY(_ArtMetallic);UNITY_DECLARE_TEX2DARRAY(_ArtNormal);UNITY_DECLARE_TEX2DARRAY(_ArtImpostors);
 uint _Mode,_Capacity,_ArtStride;float _WorldSize,_RenderInterpolation,_TimeSeconds,_ZombieTime;
 float3 _RenderFocus,_CameraRight,_CameraUp,_CameraPosition;float4x4 _BattleVP;
 float2 Turn(float2 p,float a){float s=sin(a),c=cos(a);return float2(c*p.x+s*p.y,-s*p.x+c*p.y);}
 struct A{float3 p:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 uv:TEXCOORD1;float3 n:TEXCOORD2;float3 t:TEXCOORD3;float3 b:TEXCOORD4;float3 team:TEXCOORD5;float zombie:TEXCOORD6;};
 V Vert(A input,uint instance:SV_InstanceID,uint vertexId:SV_VertexID){
  InitIndirectDrawArgs(0);UnitState u=_Units[_Visible[_Mode*_Capacity+GetIndirectInstanceID(instance)]];uint type=Type(u);
  u.position=WrapPosition(u.previous+WorldDelta(u.previous,u.position,_WorldSize)*_RenderInterpolation,_WorldSize);
  UnitTraits traits=_Traits[type];float height=_Terrain.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+.12;
  if(traits.scale.w==4)height=max(height,_WaterMap.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+.12);
  if((u.flags&8192)==0)height+=traits.altitude;
  float3 centre=NearestWorld(float3(u.position.x,height,u.position.y),_RenderFocus,_WorldSize);
  V o;o.zombie=0;o.team=Team(u)==0?float3(.02,.72,1):Team(u)==2?float3(.34,.55,.10):float3(1,.035,.12);
  if((u.flags&SELECTED)!=0)o.team=lerp(o.team,float3(.25,1,.55),.6);
  o.n=float3(0,1,0);o.t=float3(1,0,0);o.b=float3(0,0,1);
  if(_Mode==2){
   float cameraAngle=atan2(_CameraPosition.x-centre.x,_CameraPosition.z-centre.z);
   uint direction=(uint)floor(frac((cameraAngle-u.facing)/6.2831853+1)*8+.5)%8;
   float4 bounds=_ArtBounds[type];o.world=centre+float3(0,bounds.y,0)+_CameraRight*input.p.x*bounds.x+_CameraUp*input.p.y*bounds.x;o.uv=float3(input.uv,type*8+direction);if(Team(u)==2)o.zombie=1;
  }else{
   ArtVertex v=_ArtVertices[type*_ArtStride+vertexId];float angle=v.position.w==1?u.turret:u.facing;
   if(v.position.w==4)angle+=_TimeSeconds*22;
   float3 p=v.position.xyz;
   if(Team(u)==2){o.zombie=1+v.uv.z;float stride=saturate(length(u.velocity)/8);float phase=_ZombieTime*12+ArmyRandom(_Visible[_Mode*_Capacity+GetIndirectInstanceID(instance)])*6.2831853;float leg=v.position.w-10;p.z+=sin(phase+leg*1.5708)*.18*stride*saturate(1-p.y/1.1);p.y+=abs(sin(phase))*.035*stride;o.uv=float3(.5,.5,0);}else o.uv=float3(v.uv.xy,type);
   p.xz=Turn(p.xz,angle);o.world=centre+p;
   o.n=v.normal.xyz;o.n.xz=Turn(o.n.xz,angle);o.t=v.tangent.xyz;o.t.xz=Turn(o.t.xz,angle);o.b=cross(o.n,o.t)*v.tangent.w;
  }
  o.p=mul(_BattleVP,float4(o.world,1));return o;
 }
 float4 Frag(V i):SV_Target{
  if(_Mode==2){if(i.zombie>0){float2 p=(i.uv.xy-.5)*3.4;bool head=length(p-float2(0,.82))<.24;bool torso=abs(p.x)<.31&&p.y>-.1&&p.y<.65;bool legs=(abs(abs(p.x)-.17)<.12)&&p.y>-.95&&p.y<.05;bool arms=abs(abs(p.x)-.45)<.12&&p.y>0&&p.y<.56;clip(head||torso||legs||arms?1:-1);return float4((head||arms?float3(.28,.39,.13):float3(.11,.16,.10))*ArmyImpostorLight(),1);}float4 c=UNITY_SAMPLE_TEX2DARRAY(_ArtImpostors,i.uv);clip(c.a-.3);float tint=saturate((c.b-c.r)*5);c.rgb=lerp(c.rgb,i.team*max(c.g,c.b),tint);c.rgb*=ArmyImpostorLight();return c;}
  if(i.zombie>0){float3 albedo=i.zombie>1.5?float3(.26,.38,.12):float3(.075,.12,.08);return float4(ShadeArmour(i.world,normalize(i.n),albedo,float4(0,1,0,.12),i.team,_CameraPosition),1);}
  float3 n=UNITY_SAMPLE_TEX2DARRAY(_ArtNormal,i.uv).xyz*2-1;n=normalize(i.t*n.x+i.b*n.y+i.n*n.z);
  return float4(ShadeArmour(i.world,n,UNITY_SAMPLE_TEX2DARRAY(_ArtAlbedo,i.uv).rgb,UNITY_SAMPLE_TEX2DARRAY(_ArtMetallic,i.uv),i.team,_CameraPosition),1);
 }
 ENDHLSL
 }
 Pass {Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} Cull Off ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex ShadowVert
 #pragma fragment ShadowFrag
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
 #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
 #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
 #include "UnityIndirect.cginc"
 #include "ArmyCommon.hlsl"
 struct ArtVertex{float4 position,normal,tangent,uv;};StructuredBuffer<ArtVertex> _ArtVertices;
 StructuredBuffer<UnitState> _Units;StructuredBuffer<UnitTraits> _Traits;StructuredBuffer<uint> _Visible;
 Texture2D<float4> _Terrain,_WaterMap;SamplerState sampler_Terrain;uint _Mode,_Capacity,_ArtStride;float _WorldSize,_RenderInterpolation,_TimeSeconds;float3 _RenderFocus;
 float2 Turn(float2 p,float a){float s=sin(a),c=cos(a);return float2(c*p.x+s*p.y,-s*p.x+c*p.y);}
 float4 ShadowVert(uint instance:SV_InstanceID,uint vertexId:SV_VertexID):SV_POSITION{
  InitIndirectDrawArgs(0);UnitState u=_Units[_Visible[_Mode*_Capacity+GetIndirectInstanceID(instance)]];
  u.position=WrapPosition(u.previous+WorldDelta(u.previous,u.position,_WorldSize)*_RenderInterpolation,_WorldSize);UnitTraits traits=_Traits[Type(u)];
  float height=_Terrain.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+.12;
  if(traits.scale.w==4)height=max(height,_WaterMap.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+.12);
  if((u.flags&8192)==0)height+=traits.altitude;
  ArtVertex v=_ArtVertices[Type(u)*_ArtStride+vertexId];float angle=v.position.w==1?u.turret:u.facing;if(v.position.w==4)angle+=_TimeSeconds*22;
  float3 p=v.position.xyz;p.xz=Turn(p.xz,angle);p+=NearestWorld(float3(u.position.x,height,u.position.y),_RenderFocus,_WorldSize);
  return TransformWorldToHClip(GetCameraRelativePositionWS(p));
 }
 float4 ShadowFrag():SV_Target{return 0;}
 ENDHLSL
 }
 }
}
