Shader "MassiveArmy/Settlement land use" {SubShader {Tags {"RenderType"="Transparent"}
 Pass {Cull Off ZWrite On ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 UNITY_DECLARE_TEX2DARRAY(_SurfaceAlbedo);UNITY_DECLARE_TEX2DARRAY(_SurfaceData);
 float _DistrictOverlay;float4x4 _SettlementVP;float3 _SettlementCenter,_SettlementCamera;
 struct Input {float3 position:POSITION;float3 normal:NORMAL;float4 uv:TEXCOORD0;float4 colour:COLOR;};
 struct Varying {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float4 uv:TEXCOORD1;float4 colour:TEXCOORD2;float3 world:TEXCOORD3;};
 Varying Vert(Input i){Varying o;o.world=i.position+_SettlementCenter;o.position=mul(_SettlementVP,float4(o.world,1));o.normal=i.normal;o.uv=i.uv;o.colour=i.colour;return o;}
 float4 Frag(Varying i):SV_Target{
  if(_DistrictOverlay>.5){float border=saturate(min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y))*30);return float4(i.colour.rgb,lerp(.65,i.colour.a,border));}
  float layer=i.uv.z;float2 uv=i.uv.xy;float3 soil=UNITY_SAMPLE_TEX2DARRAY(_SurfaceAlbedo,float3(uv*3,layer)).rgb;
  float4 data=UNITY_SAMPLE_TEX2DARRAY(_SurfaceData,float3(uv*3,layer));float3 n=normalize(i.normal);float3 east=normalize(cross(abs(n.y)>.95?float3(1,0,0):float3(0,1,0),n)),north=cross(n,east);
  float edge=saturate(min(min(uv.x,1-uv.x),min(uv.y,1-uv.y))*40);
  if(layer<7){float furrow=sin(uv.x*100);float crop=smoothstep(.25,.7,furrow)*saturate(i.uv.w*1.5);soil=lerp(soil*.75,i.colour.rgb*(.4+.4*data.b),crop);n=normalize(n+east*(data.r*2-1+cos(uv.x*100)*.14)+north*(data.g*2-1));}
  else if(i.colour.a>.5){uint mask=(uint)round(i.uv.w);float2 p=uv*2-1;
   float road=step(abs(p.x),.34)*step(abs(p.y),.34);
   if((mask&1)!=0)road=max(road,step(p.x,0)*step(abs(p.y),.34));
   if((mask&2)!=0)road=max(road,step(0,p.x)*step(abs(p.y),.34));
   if((mask&4)!=0)road=max(road,step(p.y,0)*step(abs(p.x),.34));
   if((mask&8)!=0)road=max(road,step(0,p.y)*step(abs(p.x),.34));
   clip(road-.5);edge=1;
   float markings=step(abs(p.x),.025)*step(.48,abs(p.y))+step(abs(p.y),.025)*step(.48,abs(p.x));
   soil=lerp(soil,float3(.65,.62,.42),saturate(markings)*.65);
   n=normalize(n+east*(data.r*2-1)*.2+north*(data.g*2-1)*.2);soil*=.75;}
  else {soil*=.65;edge=1;n=normalize(n+east*(data.r*2-1)*.2+north*(data.g*2-1)*.2);}
  return float4(ShadeArmour(i.world,n,soil,float4(0,data.a,0,.05),float3(1,1,1),_SettlementCamera),edge);
 }
 ENDHLSL
 }}}
