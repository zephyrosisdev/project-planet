Shader "MassiveArmy/Landing preview" {SubShader {Tags {"Queue"="Transparent+40" "RenderType"="Transparent"}
 Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 StructuredBuffer<float4> _LandingPoints;float3 _LandingOrigin;float4 _LandingScreen;float4x4 _LandingVP;float4 _LandingColour;
 struct Varying{float4 position:SV_POSITION;float alpha:TEXCOORD0;};
 Varying Vert(uint id:SV_VertexID){uint segment=id/6,corner=id%6;const uint ends[6]={0,0,1,0,1,1};const float sides[6]={-1,1,1,-1,1,-1};float4 tail=_LandingPoints[segment*2],head=_LandingPoints[segment*2+1];float4 a=mul(_LandingVP,float4(_LandingOrigin+tail.xyz,1)),b=mul(_LandingVP,float4(_LandingOrigin+head.xyz,1));Varying o;
 if(a.w<_LandingScreen.z&&b.w<_LandingScreen.z){o.position=float4(2,2,2,1);o.alpha=0;return o;}
 if(a.w<_LandingScreen.z)a=lerp(a,b,(_LandingScreen.z-a.w)/(b.w-a.w));if(b.w<_LandingScreen.z)b=lerp(b,a,(_LandingScreen.z-b.w)/(a.w-b.w));
 float2 delta=(b.xy/b.w-a.xy/a.w)*_LandingScreen.xy;float2 normal=float2(-delta.y,delta.x)/max(length(delta),.001);o.position=ends[corner]==0?a:b;o.position.xy+=normal*sides[corner]*3/_LandingScreen.xy*o.position.w;o.alpha=ends[corner]==0?tail.w:head.w;return o;}
 float4 Frag(Varying input):SV_Target{return float4(_LandingColour.rgb,input.alpha*_LandingColour.a);}
 ENDHLSL
 }}}
