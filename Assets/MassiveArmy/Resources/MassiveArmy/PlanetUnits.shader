Shader "MassiveArmy/Planet GPU units"
{
    SubShader { Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass { Tags { "LightMode"="SRPDefaultUnlit" } Cull Off ZWrite On
            HLSLPROGRAM
#pragma multi_compile _ PLANET_TERRAIN_EDITS PLANET_SINGLE_CRATER
#define PLANET_SPECIALIZE_TERRAIN 1
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ArmyCommon.hlsl"
            #include "PlanetTopology.hlsl"
            #include "PlanetSurface.hlsl"
            StructuredBuffer<UnitState> _PlanetUnits;
            float3 _PlanetCenter;
            float4x4 _PlanetVP;
            struct Vertex {float3 position:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            struct Varying {float4 position:SV_POSITION;float3 colour:TEXCOORD0;};
            Varying Vert(Vertex v,uint instance:SV_InstanceID){InitIndirectDrawArgs(0);UnitState u=_PlanetUnits[GetIndirectInstanceID(instance)];float3 up=PlanetDecode(u.position);uint f=min((uint)floor(u.position.x),5u);
                float3 right=normalize(PlanetU[f]-up*dot(PlanetU[f],up)),forward=cross(right,up);float s=sin(u.facing),c=cos(u.facing);float3 r=right*c-forward*s,z=right*s+forward*c;
                float3 p=PlanetPosition(up)+r*v.position.x+up*(v.position.y+.15)+z*v.position.z;
                Varying o;o.position=mul(_PlanetVP,float4(_PlanetCenter+p,1));float3 normal=r*v.normal.x+up*v.normal.y+z*v.normal.z;
                float3 base=(u.flags&SELECTED)!=0?float3(.15,.65,.12):float3(.04,.3,.4);
                o.colour=base*(.4+.6*saturate(dot(normal,normalize(float3(1,1,-.4)))))+float3(0,.7,1)*v.colour.r;return o;}
            float4 Frag(Varying i):SV_Target{return float4(i.colour,1);}
            ENDHLSL
        }
    }
}
