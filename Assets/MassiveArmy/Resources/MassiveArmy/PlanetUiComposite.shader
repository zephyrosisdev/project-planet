Shader "Hidden/MassiveArmy/Planet UI capture" {Properties{_MainTex("World",2D)="black"{} _Ui("UI",2D)="black"{}}
 SubShader{Pass{ZTest Always Cull Off ZWrite Off
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment Frag
 #include "UnityCG.cginc"
 sampler2D _MainTex,_Ui;
 float4 Frag(v2f_img i):SV_Target{float4 world=tex2D(_MainTex,i.uv),ui=tex2D(_Ui,i.uv);return float4(world.rgb*(1-ui.a)+ui.rgb,1);}
 ENDCG
 }}}
