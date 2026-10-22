Shader "MassiveArmy/Modular structures" {
 Properties {_MainTex("Shared kit atlas",2D)="white"{} }
 SubShader {Tags {"RenderType"="Opaque"} Pass {Tags {"LightMode"="SRPDefaultUnlit"} Cull Back ZWrite On
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 sampler2D _MainTex;float _ManualRender;float4x4 _FurnitureVP;float3 _FurnitureCentre,_FurnitureCamera;
 struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;float4 colour:COLOR;};
 V Vert(float4 p:POSITION,float3 n:NORMAL,float2 uv:TEXCOORD0,float4 colour:COLOR){V o;o.world=mul(unity_ObjectToWorld,p).xyz;o.n=UnityObjectToWorldNormal(n);if(_ManualRender>.5)o.world=p.xyz+_FurnitureCentre;o.p=_ManualRender>.5?mul(_FurnitureVP,float4(o.world,1)):UnityObjectToClipPos(p);o.uv=uv;o.colour=colour;return o;}
 float4 Frag(V i):SV_Target{
  float3 n=normalize(i.n);float3 camera=_ManualRender>.5?_FurnitureCamera:_WorldSpaceCameraPos;
  float3 colour=ShadeArmour(i.world,n,tex2D(_MainTex,i.uv).rgb*i.colour.rgb,float4(0,.8,0,.1),float3(.1,.2,.3),camera);
  // Restrained rim illumination makes the shell readable without per-house lights.
  float rim=pow(1-saturate(abs(dot(n,normalize(camera-i.world)))),4);
  float3 up=_ManualRender>.5?normalize(i.world-_FurnitureCentre):normalize(mul((float3x3)unity_ObjectToWorld,float3(0,1,0)));
  float night=1-smoothstep(-.08,.18,dot(up,_ArmyLightDirection));
  return float4(colour+float3(.3,.6,1)*rim*night,1);
 }
 ENDHLSL
 }}}
