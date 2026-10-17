Shader "MassiveArmy/Galaxy backdrop" {SubShader {Tags {"RenderType"="Transparent"}
 HLSLINCLUDE
 #include "UnityCG.cginc"
 float4x4 _SkyVP;float3 _SkyCamera,_SkyRight,_SkyUp,_SkyForward;float4 _SkyScreen;
 float _StarBrightness,_NebulaBrightness,_CloudBlend;uint _SkySeed;
 sampler2D _CloudCurrent,_CloudPrevious;
 struct Varying {float4 position:SV_POSITION;float3 ray:TEXCOORD0;float2 uv:TEXCOORD1;float4 colour:TEXCOORD2;};
 float FarDepth(){
 #if defined(UNITY_REVERSED_Z)
 return 0;
 #else
 return 1;
 #endif
 }
 uint Hash(uint x){x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;return x;}
 float Random(uint x){return (Hash(x)&0xffffffu)/16777216.0;}
 Varying CloudVert(uint id:SV_VertexID){
  Varying o=(Varying)0;float2 uv=float2((id<<1)&2,id&2);o.position=float4(uv*2-1,FarDepth(),1);
  // Render-target projection flips Y; ray reconstruction follows that projection explicitly.
  float2 ndc=o.position.xy;float sy=_SkyVP[1][0]*_SkyUp.x+_SkyVP[1][1]*_SkyUp.y+_SkyVP[1][2]*_SkyUp.z;
  o.ray=_SkyForward+_SkyRight*(ndc.x*_SkyScreen.x*_SkyScreen.y)+_SkyUp*(ndc.y*_SkyScreen.y*(sy<0?-1:1));return o;
 }
 float4 CloudFrag(Varying i):SV_Target{
  float3 ray=normalize(i.ray);float2 uv=float2(atan2(ray.z,ray.x)/6.2831853,asin(clamp(ray.y,-1,1))/3.14159265+.5);
  float4 cloud=lerp(tex2D(_CloudPrevious,uv),tex2D(_CloudCurrent,uv),_CloudBlend);
  return float4(cloud.rgb*_NebulaBrightness*4,1);
 }
 Varying StarVert(uint vertex:SV_VertexID,uint instance:SV_InstanceID){
  uint key=Hash(instance+_SkySeed);float longitude=Random(key)*6.2831853,y=Random(key+1)*2-1;
  // A mixture of uniform sky and thin galactic-band stars; all points are visual background.
  if(Random(key+2)<.55)y*=.12+.18*Random(key+3);
  float radial=sqrt(max(0,1-y*y));float3 dir=float3(radial*cos(longitude),y,radial*sin(longitude));
  const float2 corners[6]={float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
  float2 corner=corners[vertex];float size=lerp(.6,1.5,pow(Random(key+4),5))*2*_SkyScreen.y/max(1,_SkyScreen.w);
  Varying o=(Varying)0;o.position=mul(_SkyVP,float4(_SkyCamera+dir*1000+(_SkyRight*corner.x+_SkyUp*corner.y)*size*1000,1));o.position.z=FarDepth()*o.position.w;
  o.uv=corner;o.colour=float4(lerp(float3(1,.65,.4),float3(.55,.75,1),Random(key+5)),lerp(.14,.9,pow(Random(key+6),3))*_StarBrightness);return o;
 }
 float4 StarFrag(Varying i):SV_Target{float strength=exp(-dot(i.uv,i.uv)*4)*i.colour.a;clip(strength-.008);return float4(i.colour.rgb*strength*1.8,1);}
 ENDHLSL
 Pass {Cull Off ZWrite Off ZTest LEqual Blend One One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex CloudVert
 #pragma fragment CloudFrag
 ENDHLSL
 }
 Pass {Cull Off ZWrite Off ZTest LEqual Blend One One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex StarVert
 #pragma fragment StarFrag
 ENDHLSL
 }
 }}
