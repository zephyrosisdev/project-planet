Shader "MassiveArmy/Inland water"
{
    SubShader { Tags { "RenderType"="Opaque" "Queue"="Geometry+5" }
        Pass { Tags { "LightMode"="SRPDefaultUnlit" } Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float _WorldSize;float Frequency(float f){return max(1,round(f*_WorldSize/6.2831853))*6.2831853/_WorldSize;}
            struct Varyings { float4 position:SV_POSITION;float3 world:TEXCOORD0;float4 colour:COLOR; };
            Varyings Vert(appdata_full v) { Varyings o;o.position=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.colour=v.color;return o; }
            float4 Frag(Varyings i):SV_Target { float ripple=sin(i.world.x*Frequency(.34)+i.world.z*Frequency(.21)+_Time.y*1.4)*sin(i.world.x*Frequency(.13)-i.world.z*Frequency(.41)+_Time.y*.8);
                float glint=pow(saturate(ripple),10);return float4(i.colour.rgb*(.8+ripple*.12)+glint*float3(.08,.16,.18),1); }
            ENDHLSL
        }
    }
}
