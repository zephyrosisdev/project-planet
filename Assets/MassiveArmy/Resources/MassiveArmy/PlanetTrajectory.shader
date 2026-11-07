Shader "MassiveArmy/Pilot predicted trajectory" {SubShader {Tags {"Queue"="Transparent+50" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 StructuredBuffer<float4> _ArcPoints;float3 _ArcOrigin;float4 _ArcScreen;float4x4 _ArcVP;float4 _ArcColour;uint _ArcSubdivisions;float4 _ArcMarkers;float3 _ArcMarkerUp;StructuredBuffer<float4> _ArcTrail;uint _ArcTrailCount;
 struct Varying{float4 position:SV_POSITION;float alpha:TEXCOORD0;float edge:TEXCOORD1;};
 float3 Curve(uint segment,float t){uint count=(uint)_ArcScreen.w;float3 a=_ArcPoints[segment].xyz,b=_ArcPoints[segment+1].xyz;
 float3 before=_ArcPoints[segment>0?segment-1:0].xyz,after=_ArcPoints[min(segment+2,count-1)].xyz;
 float lengthAB=length(b-a);float3 ta=(b-before)*.5,tb=(after-a)*.5;ta*=min(1,lengthAB/max(.001,length(ta)));tb*=min(1,lengthAB/max(.001,length(tb)));
 float t2=t*t,t3=t2*t;return (2*t3-3*t2+1)*a+(t3-2*t2+t)*ta+(-2*t3+3*t2)*b+(t3-t2)*tb;}
 Varying Vert(uint id:SV_VertexID){uint segment=id/6,corner=id%6;const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};float3 wa,wb;float width=3,alpha=.48;
 uint subdivisions=max(1u,_ArcSubdivisions);uint lineVertices=((uint)_ArcScreen.w-1)*subdivisions*6;if(id<lineVertices){uint node=segment/subdivisions;float t=(segment%subdivisions)/(float)subdivisions;wa=_ArcOrigin+Curve(node,t);wb=_ArcOrigin+Curve(node,t+1.0/subdivisions);alpha*=1-node/max(1,_ArcScreen.w-1)*.5;}
 else{uint bar=(id-lineVertices)/6,gate=bar/2;float distance=_ArcMarkers.y+gate*_ArcMarkers.x;uint count=(uint)_ArcScreen.w;
 if(gate<48&&distance>_ArcPoints[count-1].w){Varying hidden;hidden.position=float4(2,2,2,1);hidden.alpha=0;hidden.edge=0;return hidden;}
 uint lo=0,hi=count-1;[unroll]for(uint search=0;search<8;search++){if(hi>lo+1){uint mid=(lo+hi)/2;if(_ArcPoints[mid].w<distance)lo=mid;else hi=mid;}}
 float t=saturate((distance-_ArcPoints[lo].w)/max(.001,_ArcPoints[hi].w-_ArcPoints[lo].w));float3 tangent=normalize(_ArcPoints[hi].xyz-_ArcPoints[lo].xyz);
 float3 marker=Curve(lo,t);if(gate>=48){uint k=gate-48;marker=_ArcTrail[k].xyz;tangent=k+1<_ArcTrailCount?normalize(_ArcTrail[k].xyz-_ArcTrail[k+1].xyz):tangent;}
 float3 right=cross(tangent,_ArcMarkerUp);if(dot(right,right)<.001)right=cross(tangent,abs(tangent.x)>.9?float3(0,1,0):float3(1,0,0));right=normalize(right);float3 up=normalize(cross(right,tangent));
 float size=_ArcMarkers.z;float3 center=_ArcOrigin+marker+right*((bar%2)==0?-size:size);wa=center-up*size*.45;wb=center+up*size*.45;width=2;alpha=.65;}

 float4 a=mul(_ArcVP,float4(wa,1)),b=mul(_ArcVP,float4(wb,1));Varying o;
 if(a.w<_ArcScreen.z&&b.w<_ArcScreen.z){o.position=float4(2,2,2,1);o.alpha=0;o.edge=0;return o;}
 if(a.w<_ArcScreen.z)a=lerp(a,b,(_ArcScreen.z-a.w)/(b.w-a.w));if(b.w<_ArcScreen.z)b=lerp(b,a,(_ArcScreen.z-b.w)/(a.w-b.w));
 float2 delta=(b.xy/b.w-a.xy/a.w)*_ArcScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(length(delta),.001);o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*width/_ArcScreen.xy*o.position.w;o.alpha=alpha;o.edge=sides[corner];return o;}
 float4 Frag(Varying input):SV_Target{float coverage=saturate((1-abs(input.edge))/max(fwidth(input.edge),.001));return float4(_ArcColour.rgb,input.alpha*coverage);}
 ENDHLSL
 }}}
