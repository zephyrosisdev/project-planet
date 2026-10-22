Shader "MassiveArmy/Settlement land use" {SubShader {Tags {"RenderType"="Transparent"}
 Pass {Cull Off ZWrite On ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 UNITY_DECLARE_TEX2DARRAY(_SurfaceAlbedo);UNITY_DECLARE_TEX2DARRAY(_SurfaceData);
 float _DistrictOverlay,_RoadWidth;float4x4 _SettlementVP;float3 _SettlementCenter,_SettlementCamera;
 struct Input {float3 position:POSITION;float3 normal:NORMAL;float4 uv:TEXCOORD0;float4 colour:COLOR;float2 metres:TEXCOORD1;};
 struct Varying {float4 position:SV_POSITION;float3 normal:TEXCOORD0;float4 uv:TEXCOORD1;float4 colour:TEXCOORD2;float3 world:TEXCOORD3;float2 metres:TEXCOORD4;};
 Varying Vert(Input i){Varying o;o.world=i.position+_SettlementCenter;o.position=mul(_SettlementVP,float4(o.world,1));o.normal=i.normal;o.uv=i.uv;o.colour=i.colour;o.metres=i.metres;return o;}
 float4 Frag(Varying i):SV_Target{
  if(_DistrictOverlay>.5){float border=saturate(min(min(i.uv.x,1-i.uv.x),min(i.uv.y,1-i.uv.y))*30);return float4(i.colour.rgb,lerp(.65,i.colour.a,border));}
  bool rail=i.uv.z>8.5;float layer=rail?8:i.uv.z;float2 uv=i.uv.xy;float3 soil=UNITY_SAMPLE_TEX2DARRAY(_SurfaceAlbedo,float3(uv*3,layer)).rgb;
  float4 data=UNITY_SAMPLE_TEX2DARRAY(_SurfaceData,float3(uv*3,layer));float3 n=normalize(i.normal);float3 east=normalize(cross(abs(n.y)>.95?float3(1,0,0):float3(0,1,0),n)),north=cross(n,east);
  float glow=0;float edge=saturate(min(min(uv.x,1-uv.x),min(uv.y,1-uv.y))*40);
  if(layer<7){float furrow=sin(uv.x*100);float crop=smoothstep(.25,.7,furrow)*saturate(i.uv.w*1.5);soil=lerp(soil*.75,i.colour.rgb*(.4+.4*data.b),crop);n=normalize(n+east*(data.r*2-1+cos(uv.x*100)*.14)+north*(data.g*2-1));}
  else if(i.colour.a>.5){uint mask=(uint)round(i.uv.w);float2 p=uv*2-1;float2 width=(rail?3.2:_RoadWidth)/max(i.metres,1);
   float road=step(abs(p.x),width.x)*step(abs(p.y),width.y);
   if((mask&1)!=0)road=max(road,step(p.x,0)*step(abs(p.y),width.y));
   if((mask&2)!=0)road=max(road,step(0,p.x)*step(abs(p.y),width.y));
   if((mask&4)!=0)road=max(road,step(p.y,0)*step(abs(p.x),width.x));
   if((mask&8)!=0)road=max(road,step(0,p.y)*step(abs(p.x),width.x));
   clip(road-.5);edge=1;
   float2 metric=p*i.metres*.5;float markings=0;
   // Two lanes share a dashed centre line; solid edge lines leave a clear shoulder.
   if((mask&3)!=0){float dash=step(.4,frac((uv.x*i.metres.x)/6));markings=max(markings,step(abs(metric.y),.07)*dash*(mask==3?1:step(width.x,abs(p.x))));markings=max(markings,step(abs(abs(metric.y)-_RoadWidth*.5+.3),.07));}
   if((mask&12)!=0){float dash=step(.4,frac((uv.y*i.metres.y)/6));markings=max(markings,step(abs(metric.x),.07)*dash*(mask==12?1:step(width.y,abs(p.y))));markings=max(markings,step(abs(abs(metric.x)-_RoadWidth*.5+.3),.07));}
   if(!rail)glow=markings;
   if(rail){float ties=step(.72,frac((abs(p.x)>abs(p.y)?uv.x:uv.y)*7));float steel=0;
    if((mask&3)!=0)steel=max(steel,1-smoothstep(.025,.055,abs(abs(p.y)-.19)));
    if((mask&12)!=0)steel=max(steel,1-smoothstep(.025,.055,abs(abs(p.x)-.19)));
    soil=lerp(float3(.18,.17,.16),float3(.28,.18,.1),ties);soil=lerp(soil,float3(.65,.68,.7),steel);
   }else soil=lerp(soil,float3(.65,.62,.42),saturate(markings)*.65);
   n=normalize(n+east*(data.r*2-1)*.2+north*(data.g*2-1)*.2);soil*=.75;}
  else {soil*=.65;edge=1;n=normalize(n+east*(data.r*2-1)*.2+north*(data.g*2-1)*.2);}
  return float4(ShadeArmour(i.world,n,soil,float4(0,data.a,0,.05),float3(1,1,1),_SettlementCamera)+float3(.35,.65,1.0)*glow*(1-smoothstep(-.08,.18,dot(normalize(i.normal),_ArmyLightDirection)))*1.2,edge);
 }
 ENDHLSL
 }}}
