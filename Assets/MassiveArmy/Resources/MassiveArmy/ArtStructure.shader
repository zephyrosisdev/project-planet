Shader "MassiveArmy/UV armour structures" {
 Properties {_Albedo("Albedo",2D)="white"{} _Metallic("Metal AO emission smoothness",2D)="white"{} _Normal("Tangent normal",2D)="bump"{} _TeamColor("Team",Color)=(.02,.7,1,1)}
 SubShader {Tags {"RenderType"="Opaque"} Pass {
 Tags {"LightMode"="SRPDefaultUnlit"} Cull Off
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 #include "ArtLighting.hlsl"
 #include "BuildingTurretPose.hlsl"
 #include "PlanetStructureGrid.hlsl"
 sampler2D _Albedo,_Metallic,_Normal;float4 _TeamColor;
 struct A{float4 p:POSITION;float3 n:NORMAL;float4 t:TANGENT;float2 uv:TEXCOORD0;float2 joint:TEXCOORD1;};
 struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float3 n:TEXCOORD2;float3 t:TEXCOORD3;float3 b:TEXCOORD4;};
 V Vert(A i){float angle=TurretAngle(i.joint.x);i.p.xz=TurretTurn(i.p.xz,angle);i.n.xz=TurretTurn(i.n.xz,angle);i.t.xz=TurretTurn(i.t.xz,angle);PlanetStructureVertex(i.p.xyz,i.n,i.t.xyz);V o;o.p=UnityObjectToClipPos(i.p);o.world=mul(unity_ObjectToWorld,i.p).xyz;o.uv=i.uv;o.n=UnityObjectToWorldNormal(i.n);o.t=UnityObjectToWorldDir(i.t.xyz);o.b=cross(o.n,o.t)*i.t.w*unity_WorldTransformParams.w;return o;}
 float4 Frag(V i):SV_Target{float3 n=tex2D(_Normal,i.uv).xyz*2-1;n=normalize(i.t*n.x+i.b*n.y+i.n*n.z);return float4(ShadeArmour(i.world,n,tex2D(_Albedo,i.uv).rgb,tex2D(_Metallic,i.uv),_TeamColor.rgb,_WorldSpaceCameraPos)+float3(.3,.6,1)*pow(1-saturate(abs(dot(n,normalize(_WorldSpaceCameraPos-i.world)))),4)*(1-smoothstep(-.08,.18,dot(normalize(mul((float3x3)unity_ObjectToWorld,float3(0,1,0))),_ArmyLightDirection))),1);}
 ENDHLSL
 }
 Pass {Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} Cull Off ZWrite On ColorMask 0
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex ShadowVert
 #pragma fragment ShadowFrag
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
 #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
 #include "BuildingTurretPose.hlsl"
 #include "PlanetStructureGrid.hlsl"
 float4 ShadowVert(float3 vertex:POSITION,float2 joint:TEXCOORD1):SV_POSITION{vertex.xz=TurretTurn(vertex.xz,TurretAngle(joint.x));vertex=PlanetStructurePosition(vertex);return TransformWorldToHClip(TransformObjectToWorld(vertex));}
 float4 ShadowFrag():SV_Target{return 0;}
 ENDHLSL
 }
 }
}
