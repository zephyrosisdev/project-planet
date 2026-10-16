Shader "MassiveArmy/Indirect units"
{
    Properties { _Atlas("Directional tank atlas",2D)="white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ArmyCommon.hlsl"
            #include "UnitGeometry.hlsl"
            #include "ArtLighting.hlsl"
            StructuredBuffer<UnitState> _Units;
            StructuredBuffer<UnitTraits> _Traits;uint _BaseMode;
            StructuredBuffer<ProjectileState> _Projectiles;
            StructuredBuffer<uint> _Visible, _VisibleProjectiles;
            Texture2D<float4> _Terrain,_WaterMap;
            SamplerState sampler_Terrain;
            sampler2D _Atlas;
            float4x4 _BattleVP;
            float _RenderInterpolation;
            float3 _CameraRight, _CameraUp, _CameraPosition,_RenderFocus;
            float _WorldSize, _TimeSeconds;
            uint _Mode, _Capacity;
            struct Attributes { float3 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float4 color:TEXCOORD1; float atlas:TEXCOORD2; };
            float2 Rotate(float2 p,float angle) { float s=sin(angle),c=cos(angle); return float2(c*p.x+s*p.y,-s*p.x+c*p.y); }
            Varyings Vert(Attributes input,uint instance:SV_InstanceID)
            {
                InitIndirectDrawArgs(0); uint index=GetIndirectInstanceID(instance);
                Varyings o; o.uv=input.uv; o.atlas=0;
                float3 world; float3 colour;
                if(_Mode==3) {
                    ProjectileState p=_Projectiles[_VisibleProjectiles[index]];
                    float3 along=normalize(p.velocity), side=normalize(cross(along,normalize(_CameraPosition-p.position)));
                    world=NearestWorld(p.position,_RenderFocus,_WorldSize)+along*input.vertex.y*5+side*input.vertex.x*.8;
                    colour=p.team==0?float3(.05,2.8,5):float3(5,.08,.4);
                }
                else {
                    uint id=_Visible[_Mode*_Capacity+index]; UnitState u=_Units[id];u.position=WrapPosition(u.previous+WorldDelta(u.previous,u.position,_WorldSize)*_RenderInterpolation,_WorldSize);
                    float height=_Terrain.SampleLevel(sampler_Terrain,u.position/_WorldSize+.5,0).x+1;
                    UnitTraits traits=_Traits[Type(u)];if(_BaseMode!=0&&traits.scale.w==4)height=max(height,_WaterMap.SampleLevel(sampler_Terrain,u.position/_WorldSize+.5,0).x+1);if(_BaseMode!=0&&(u.flags&8192)==0)height+=traits.altitude;
                    float3 basePosition=NearestWorld(float3(u.position.x,height,u.position.y),_RenderFocus,_WorldSize);
                    float3 team=Team(u)==0?float3(.015,.65,1):float3(1,.025,.19);
                    bool selected=(u.flags&SELECTED)!=0;
                    if(selected) team=lerp(team,float3(.3,1,.58),.75);
                    if(_Mode==2) {
                        // Eight nearest-neighbour views share one atlas across both factions.
                        float cameraAngle=atan2(_CameraPosition.x-basePosition.x,_CameraPosition.z-basePosition.z);
                        float direction=floor(frac((u.facing-cameraAngle)/6.2831853+1)*8+.5);
                        o.uv=float2((input.uv.x+fmod(direction,8))/8,input.uv.y); o.atlas=1;
                        world=basePosition+_CameraRight*input.vertex.x*4.5+_CameraUp*input.vertex.y*4.5;
                        colour=team*(selected?2.4:1.7)*ArmyImpostorLight();
                    }
                    else {
                        float angle;float3 vertex=input.vertex;ModelVertex(vertex,input.uv,input.color,u,traits,_BaseMode,angle);
                        world=basePosition+vertex;
                        float3 normal=input.normal; normal.xz=Rotate(normal.xz,angle);
                        colour=float3(.075,.11,.15)*ArmyDiffuse(normal)+team*(input.color.r*2.2+.08)*ArmyImpostorLight();
                        if(selected) colour+=team*.4*ArmyImpostorLight();
                    }
                }
                o.position=mul(_BattleVP,float4(world,1)); o.color=float4(colour,1); return o;
            }
            float4 Frag(Varyings input):SV_Target
            {
                float4 c=input.color;
                if(input.atlas>.5) { float4 tex=tex2D(_Atlas,input.uv); clip(tex.a-.35); c.rgb*=tex.rgb; }
                return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ArmyCommon.hlsl"
            #include "UnitGeometry.hlsl"
            StructuredBuffer<UnitState> _Units;StructuredBuffer<UnitTraits> _Traits;StructuredBuffer<uint> _Visible;
            Texture2D<float4> _Terrain,_WaterMap;SamplerState sampler_Terrain;
            uint _Mode,_Capacity,_BaseMode;float _WorldSize,_RenderInterpolation;float3 _RenderFocus;
            struct ShadowAttributes {float3 vertex:POSITION;float2 uv:TEXCOORD0;float4 colour:COLOR;};
            float4 ShadowVert(ShadowAttributes input,uint instance:SV_InstanceID):SV_POSITION{
                InitIndirectDrawArgs(0);uint id=_Visible[_Mode*_Capacity+GetIndirectInstanceID(instance)];UnitState u=_Units[id];u.position=WrapPosition(u.previous+WorldDelta(u.previous,u.position,_WorldSize)*_RenderInterpolation,_WorldSize);UnitTraits traits=_Traits[Type(u)];
                float height=_Terrain.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+1;
                if(_BaseMode!=0&&traits.scale.w==4)height=max(height,_WaterMap.SampleLevel(sampler_Terrain,WorldUV(u.position,_WorldSize),0).x+1);
                if(_BaseMode!=0&&(u.flags&8192)==0)height+=traits.altitude;
                float3 vertex=input.vertex;float angle;ModelVertex(vertex,input.uv,input.colour,u,traits,_BaseMode,angle);
                float3 p=NearestWorld(float3(u.position.x,height,u.position.y),_RenderFocus,_WorldSize)+vertex;
                return TransformWorldToHClip(GetCameraRelativePositionWS(p));
            }
            float4 ShadowFrag():SV_Target{return 0;}
            ENDHLSL
        }
    }
}
