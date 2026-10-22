Shader "MassiveArmy/Settlement citizens" {SubShader {Tags {"RenderType"="Opaque"}
 Pass {Cull Off ZWrite On ZTest LEqual
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 struct ArtVertex {float4 position,normal,tangent,uv;};StructuredBuffer<ArtVertex> _ArtVertices;
 struct Pose {float4 position,right,up,forward,properties;};StructuredBuffer<Pose> _CargoPoses;StructuredBuffer<uint> _CitizenVisible;
 UNITY_DECLARE_TEX2DARRAY(_ArtAlbedo);uint _ArtStride;float4x4 _SettlementVP;float3 _SettlementCamera,_SettlementCenter;
 struct Varying {float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 uv:TEXCOORD2;float3 team:TEXCOORD3;float police:TEXCOORD4;};
 Varying Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID){Pose pose=_CargoPoses[_CitizenVisible[instance]];uint type=(uint)pose.properties.x;bool police=type>=32;type%=32;ArtVertex v=_ArtVertices[type*_ArtStride+vertex];Varying o;o.police=police?1:0;float phase=pose.properties.z;if(v.position.y<1&&phase>0)v.position.z+=sin(phase+(v.position.x<0?3.14159:0))*.18*(1-saturate(v.position.y));o.world=_SettlementCenter+pose.position.xyz+pose.right.xyz*v.position.x+pose.up.xyz*v.position.y+pose.forward.xyz*v.position.z;o.position=pose.properties.w>0?mul(_SettlementVP,float4(o.world,1)):float4(0,0,0,0);o.normal=pose.right.xyz*v.normal.x+pose.up.xyz*v.normal.y+pose.forward.xyz*v.normal.z;o.uv=float3(v.uv.xy,type);o.team=pose.properties.y<.5?float3(.02,.72,1):float3(1,.035,.12);return o;}
 float4 Frag(Varying i):SV_Target{float3 albedo=UNITY_SAMPLE_TEX2DARRAY(_ArtAlbedo,i.uv).rgb;if(i.police>.5)albedo=lerp(albedo,float3(.025,.07,.18),.8);return float4(ShadeArmour(i.world,normalize(i.normal),albedo,float4(0,.7,0,.08),i.team,_SettlementCamera),1);}
 ENDHLSL
 }}}
