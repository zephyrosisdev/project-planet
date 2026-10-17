Shader "MassiveArmy/Generated galaxy sky" {SubShader {Tags {"RenderType"="Transparent"}
 Pass {Cull Off ZWrite On ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 struct Star {float4 position,colour,properties;};StructuredBuffer<Star> _GalaxyStars;
 float4x4 _GalaxyVP;float3 _GalaxyObserverDelta,_GalaxyCamera,_GalaxyRight,_GalaxyUp;float4 _GalaxyScreen,_GalaxyForeground;
 struct Varying {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float4 colour:TEXCOORD1;float4 data:TEXCOORD2;float4 physical:TEXCOORD3;float3 ray:TEXCOORD4;};
 Varying Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID){
  Star star=_GalaxyStars[instance];float3 delta=star.position.xyz-(star.properties.w>.5?0:_GalaxyObserverDelta);
  float distance=max(1,length(delta)),physical=star.position.w/distance;
  float pixel=2*_GalaxyScreen.z/max(1,_GalaxyScreen.y),disc=max(physical,pixel*.65),width=disc*4;
  const float2 corners[6]={float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
  float2 uv=corners[vertex];float shell=10000*distance/(10000+distance);
  Varying o;float3 offset=normalize(delta)*shell+(_GalaxyRight*uv.x+_GalaxyUp*uv.y)*width*shell;o.position=mul(_GalaxyVP,float4(_GalaxyCamera+offset,1));o.ray=offset;o.physical=float4(delta,star.position.w);
  o.uv=uv*4;o.colour=star.colour;o.data=float4(physical/disc,star.properties.x,star.properties.y,instance);return o;
 }
 struct Output {float4 colour:SV_Target;float depth:SV_Depth;};
 Output Frag(Varying i){
  float r=length(i.uv),disc=max(.65,i.data.x);float core=1-smoothstep(disc*.85,disc,r);
  float glow=exp(-r*r*1.8)*.42;float granules=.92+.08*sin(i.uv.x*57+i.data.w)*sin(i.uv.y*43-i.data.w);
  float3 colour=i.colour.rgb*(core*granules+glow);
  float alpha=saturate(core+glow);
  if(i.data.y>.5){float ring=exp(-pow((r-1.5)*6,2));colour=float3(1,.5,.12)*ring;alpha=saturate(core+ring);}
  clip(alpha-.006);float3 ray=normalize(i.ray);float b=-dot(i.physical.xyz,ray),c=dot(i.physical.xyz,i.physical.xyz)-i.physical.w*i.physical.w;
  float hit=max(1,-b-sqrt(max(0,b*b-c)));
  float fb=-dot(_GalaxyForeground.xyz,ray),fc=dot(_GalaxyForeground.xyz,_GalaxyForeground.xyz)-_GalaxyForeground.w*_GalaxyForeground.w,fd=fb*fb-fc;
  if(_GalaxyForeground.w>0&&fd>0){float front=-fb-sqrt(fd);if(front>0&&front<hit)discard;}
  float distance=10000*hit/(10000+hit);float4 clipPosition=mul(_GalaxyVP,float4(_GalaxyCamera+ray*distance,1));
  Output o;o.colour=float4(colour,alpha);o.depth=clipPosition.z/clipPosition.w;
  if(core<.01){
  #if defined(UNITY_REVERSED_Z)
   o.depth=0;
  #else
   o.depth=1;
  #endif
  }
  return o;
 }
 ENDHLSL
 }}}
