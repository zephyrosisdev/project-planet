Shader "MassiveArmy/Celestial orbit guides" {SubShader {Tags {"RenderType"="Transparent"}
 Pass {Cull Off ZWrite Off ZTest Always Blend SrcAlpha One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 StructuredBuffer<float4> _GuidePoints;float3 _GuideOrigin;float4 _GuideScreen,_GuideColour;float4x4 _GuideVP;
 struct V {float4 position:SV_POSITION;float edge:TEXCOORD0;};
 float3 Point(uint segment,float t){uint count=(uint)_GuideScreen.w;float3 a=_GuidePoints[segment].xyz,b=_GuidePoints[segment+1].xyz;
 float3 before=_GuidePoints[segment>0?segment-1:0].xyz,after=_GuidePoints[min(segment+2,count-1)].xyz;
 float3 ta=(b-before)*.5,tb=(after-a)*.5;float span=length(b-a);ta*=min(1,span/max(.001,length(ta)));tb*=min(1,span/max(.001,length(tb)));
 float t2=t*t,t3=t2*t;float3 p=_GuideOrigin+(2*t3-3*t2+1)*a+(t3-2*t2+t)*ta+(-2*t3+3*t2)*b+(t3-t2)*tb;
 // Camera-relative coordinates, compressed radially like distant body depth.
 // This retains angular direction without clipping stellar-scale orbit paths.
 return p*(10000/(10000+length(p)));}
 V Vert(uint id:SV_VertexID){const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};uint stroke=id/6,node=stroke/4,corner=id%6;float t=(stroke%4)*.25;
 float4 a=mul(_GuideVP,float4(Point(node,t),1)),b=mul(_GuideVP,float4(Point(node,t+.25),1));V o;o.edge=sides[corner];
 if(a.w<_GuideScreen.z&&b.w<_GuideScreen.z){o.position=float4(2,2,2,1);return o;}
 if(a.w<_GuideScreen.z)a=lerp(a,b,(_GuideScreen.z-a.w)/(b.w-a.w));if(b.w<_GuideScreen.z)b=lerp(b,a,(_GuideScreen.z-b.w)/(a.w-b.w));
 float2 delta=(b.xy/b.w-a.xy/a.w)*_GuideScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(length(delta),.001);o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*3.2/_GuideScreen.xy*o.position.w;return o;}
 float4 Frag(V i):SV_Target {float alpha=saturate((1-abs(i.edge))/max(fwidth(i.edge),.001));return float4(_GuideColour.rgb,_GuideColour.a*alpha);}
 ENDHLSL
 }}}
