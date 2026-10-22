Shader "MassiveArmy/Settlement cargo" {SubShader {Tags {"RenderType"="Opaque"}
 Pass {Cull Off ZWrite On ZTest LEqual
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 struct ArtVertex {float4 position,normal,tangent,uv;};StructuredBuffer<ArtVertex> _ArtVertices;
 struct Pose {float4 position,right,up,forward,properties;};StructuredBuffer<Pose> _CargoPoses;
 UNITY_DECLARE_TEX2DARRAY(_ArtAlbedo);uint _ArtStride;float4x4 _SettlementVP;float3 _SettlementCamera,_SettlementCenter;
 struct Varying {float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 uv:TEXCOORD2;float3 team:TEXCOORD3;float train:TEXCOORD4;};
 Varying Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID){Pose pose=_CargoPoses[instance];uint type=(uint)pose.properties.x;ArtVertex v=(ArtVertex)0;
  uint car=vertex/36,corner=vertex%36;static const uint box[36]={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};uint index=box[corner];float3 q=float3((index&1)?1:-1,(index&2)?1:-1,(index&4)?1:-1);
  bool hearse=type>=32;bool train=pose.properties.z>.5;bool shown=car<(train||hearse?4:1);float3 size=train?float3(1.2,car==0?1.6:1.2,2.7):float3(1.2,1.4,3);
  if(hearse)size=car==0?float3(1.15,.5,2.8):car==1?float3(1.05,.6,1):car==2?float3(1.05,.55,1.65):float3(.45,.3,1.2);
  v.position=float4(shown?q*size+float3(0,train?1.8:1.4,train?3-(float)car*7:0):float3(0,0,0),1);if(hearse&&shown)v.position.xyz=q*size+float3(0,car==0?.8:car==3?1.7:1.8,car==1?1.7:car==0?0:-.9);v.normal=float4(normalize(q),0);v.uv=0;
  Varying o;o.train=hearse?2:pose.properties.z;o.world=_SettlementCenter+pose.position.xyz+pose.right.xyz*v.position.x+pose.up.xyz*v.position.y+pose.forward.xyz*v.position.z;o.position=mul(_SettlementVP,float4(o.world,1));o.normal=pose.right.xyz*v.normal.x+pose.up.xyz*v.normal.y+pose.forward.xyz*v.normal.z;o.uv=float3(v.uv.xy,type);o.team=pose.properties.y<.5?float3(.02,.72,1):float3(1,.035,.12);return o;}
 float4 Frag(Varying i):SV_Target{float3 albedo=i.train>1.5?float3(.035,.04,.05):float3(.35,.38,.4);return float4(ShadeArmour(i.world,normalize(i.normal),albedo,float4(0,.7,0,.08),i.team,_SettlementCamera),1);}
 ENDHLSL
 }}}
