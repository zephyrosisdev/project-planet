Shader "Hidden/MassiveArmy/Model pack bake" {
 Properties {_MainTex("Texture",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1) _NormalMode("Normal",Float)=0}
 SubShader {Pass {Cull Off ZWrite Off ZTest Always
 HLSLPROGRAM
 #pragma vertex vert_img
 #pragma fragment Read
 #include "UnityCG.cginc"
 sampler2D _MainTex;float4 _Tint;float _NormalMode;
 float4 Read(v2f_img i):SV_Target {float4 c=tex2D(_MainTex,i.uv);return _NormalMode>.5?float4(UnpackNormal(c)*.5+.5,1):c*_Tint;}
 ENDHLSL
 }
 Pass {Cull Off ZWrite On ZTest LEqual
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "UnityCG.cginc"
 sampler2D _MainTex;float4x4 _BakeVP;
 struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;float light:TEXCOORD1;};
 V Vert(float4 p:POSITION,float3 n:NORMAL,float2 uv:TEXCOORD0){V o;o.p=mul(_BakeVP,p);o.uv=uv;o.light=.4+.6*saturate(dot(normalize(n),normalize(float3(.4,1,.5))));return o;}
 float4 Frag(V i):SV_Target {float4 c=tex2D(_MainTex,i.uv);clip(c.a-.3);return float4(c.rgb*i.light,c.a);}
 ENDHLSL
 }
 Pass {Cull Off ZWrite Off ZTest Always
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment ArrayRead
 #include "UnityCG.cginc"
 UNITY_DECLARE_TEX2DARRAY(_Array);float _Layer;
 float4 ArrayRead(v2f_img i):SV_Target{return UNITY_SAMPLE_TEX2DARRAY_LOD(_Array,float3(i.uv,_Layer),0);}
 ENDHLSL
 }}
}
