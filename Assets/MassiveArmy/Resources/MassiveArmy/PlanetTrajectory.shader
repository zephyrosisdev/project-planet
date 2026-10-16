Shader "MassiveArmy/Pilot predicted trajectory" {SubShader {Tags {"Queue"="Transparent+50" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha One
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 StructuredBuffer<float4> _ArcPoints;float3 _ArcOrigin;float4 _ArcScreen;float4x4 _ArcVP;
 struct Varying{float4 position:SV_POSITION;float alpha:TEXCOORD0;};
 Varying Vert(uint id:SV_VertexID){uint segment=id/6,corner=id%6;const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};float3 wa,wb;float width=3,alpha=.48;
 uint lineVertices=((uint)_ArcScreen.w-1)*6;if(id<lineVertices){wa=_ArcOrigin+_ArcPoints[segment].xyz;wb=_ArcOrigin+_ArcPoints[segment+1].xyz;alpha*=1-segment/max(1,_ArcScreen.w-1)*.5;}
 else{uint ringSegment=(id-lineVertices)/6,gate=ringSegment/24,k=ringSegment%24,sampleIndex=2u<<gate;float3 tangent=normalize(_ArcPoints[min(sampleIndex+1,(uint)_ArcScreen.w-1)].xyz-_ArcPoints[sampleIndex-1].xyz);float3 right=normalize(cross(tangent,abs(tangent.y)>.9?float3(1,0,0):float3(0,1,0))),up=cross(tangent,right);float angle=k*6.2831853/24,next=(k+1)*6.2831853/24;float3 center=_ArcOrigin+_ArcPoints[sampleIndex].xyz;wa=center+30*(right*cos(angle)+up*sin(angle));wb=center+30*(right*cos(next)+up*sin(next));width=1.5;alpha=.25;}
 float4 a=mul(_ArcVP,float4(wa,1)),b=mul(_ArcVP,float4(wb,1));Varying o;
 if(a.w<_ArcScreen.z&&b.w<_ArcScreen.z){o.position=float4(2,2,2,1);o.alpha=0;return o;}
 if(a.w<_ArcScreen.z)a=lerp(a,b,(_ArcScreen.z-a.w)/(b.w-a.w));if(b.w<_ArcScreen.z)b=lerp(b,a,(_ArcScreen.z-b.w)/(a.w-b.w));
 float2 delta=(b.xy/b.w-a.xy/a.w)*_ArcScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(length(delta),.001);o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*width/_ArcScreen.xy*o.position.w;o.alpha=alpha;return o;}
 float4 Frag(Varying input):SV_Target{return float4(.08,1,.25,input.alpha);}
 ENDHLSL
 }}}
