Shader "Hidden/MassiveArmy/Fog volume slice"
{
    SubShader { Pass { ZWrite Off ZTest Always Cull Off
        HLSLPROGRAM
        #pragma target 4.5
        #pragma vertex vert_img
        #pragma fragment Frag
        #include "UnityCG.cginc"
        sampler3D _Volume;float _Slice,_NoiseSlice;
        float4 Frag(v2f_img i):SV_Target{float4 value=tex3D(_Volume,float3(i.uv.x,_Slice,i.uv.y));return float4(lerp(abs(value.xyz)*.08,value.aaa,_NoiseSlice),1);}
        ENDHLSL
    } }
}
