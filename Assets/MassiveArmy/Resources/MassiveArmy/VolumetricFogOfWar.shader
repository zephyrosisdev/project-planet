Shader "Hidden/MassiveArmy/Volumetric fog of war"
{
    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "WorldTopology.hlsl"
    TEXTURE2D(_TerrainHeight);TEXTURE2D(_WaterSurface);TEXTURE2D(_DensityMask);TEXTURE2D(_FogScattering);TEXTURE2D(_FogDepth);TEXTURE2D(_DarknessMask);
    TEXTURE3D(_NoiseCache);TEXTURE3D(_WakeCurrent);TEXTURE3D(_WakePrevious);
    float4 _WakeOrigin,_WakePreviousOrigin,_WakeExtent,_WakePreviousExtent,_FogDetail;
    float _WorldSize,_FlowScale;float4 _FogShape,_FogNoise,_HalfSize,_FowFullSize,_FogBoundsMin,_FogBoundsMax,_FogRadiance;
    float3 _FogLightDirection;float4 _FogMotion;int _RaySteps;
    float ArmyHash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
    float3 World(float2 uv,float rawDepth){return GetAbsolutePositionWS(ComputeWorldSpacePosition(uv,rawDepth,UNITY_MATRIX_I_VP));}
    float EyeDepth(float rawDepth)
    {
        if(unity_OrthoParams.w>.5){
#if UNITY_REVERSED_Z
            rawDepth=1-rawDepth;
#endif
            return lerp(_ProjectionParams.y,_ProjectionParams.z,rawDepth);
        }
        return LinearEyeDepth(rawDepth,_ZBufferParams);
    }
    float Density(float3 p)
    {
        float2 uv=frac(p.xz/_WorldSize+.5);
        float ground=SAMPLE_TEXTURE2D_LOD(_TerrainHeight,s_linear_repeat_sampler,uv,0).r;
        float4 water=SAMPLE_TEXTURE2D_LOD(_WaterSurface,s_linear_repeat_sampler,uv,0);ground=water.g>.01?max(ground,water.r):ground;
        float above=p.y-max(ground,0);if(above< -3||above>_FogShape.y*1.7)return 0;
        float hidden=SAMPLE_TEXTURE2D_LOD(_DensityMask,s_linear_repeat_sampler,uv,0).r;
        if(hidden<.001||_FogDetail.w<.5)return 0;
        float3 wakeUV=(NearestWorld(p,_WakeOrigin.xyz+_WakeExtent.xyz*.5,_WorldSize)-_WakeOrigin.xyz)/_WakeExtent.xyz;
        float3 previousUV=(NearestWorld(p,_WakePreviousOrigin.xyz+_WakePreviousExtent.xyz*.5,_WorldSize)-_WakePreviousOrigin.xyz)/_WakePreviousExtent.xyz;
        float4 wake=0;
        if(_FogDetail.x>.001&&all(wakeUV>=0)&&all(wakeUV<=1))wake=SAMPLE_TEXTURE3D_LOD(_WakeCurrent,s_linear_clamp_sampler,wakeUV,0);
        if(_FogDetail.x>.001&&all(previousUV>=0)&&all(previousUV<=1))wake=lerp(SAMPLE_TEXTURE3D_LOD(_WakePrevious,s_linear_clamp_sampler,previousUV,0),wake,_FogDetail.z);
        float tiles=max(1,round(_WorldSize/max(_FogNoise.x*16,1)));
        float3 q=float3(p.x/_WorldSize*tiles,p.y/max(_FogNoise.x*16,1),p.z/_WorldSize*tiles);
        q-=float3(_FogNoise.z,.06,_FogNoise.w)*(_Time.y/max(_FogNoise.x*16,1));
        float flowTiles=max(1,round(_WorldSize/(_FlowScale*16)));float3 flow=float3(p.x/_WorldSize*flowTiles,p.y/(_FlowScale*16)+_Time.y/(_FogMotion.y*16),p.z/_WorldSize*flowTiles);
        float4 base=SAMPLE_TEXTURE3D_LOD(_NoiseCache,s_linear_repeat_sampler,flow,0);
        float3 curl=base.rgb;
        // Distortion is measured in metres, rather than fractions of an entire
        // noise tile. Ambient motion stays gentle; passing units dominate it.
        q+=(curl*_FogMotion.x+wake.xyz*_FogDetail.x*.6)/max(_FogNoise.x*16,1);
        float n=SAMPLE_TEXTURE3D_LOD(_NoiseCache,s_linear_repeat_sampler,q,0).a;
        [branch]if(_FogDetail.x>.001)n=lerp(n,n*.7+SAMPLE_TEXTURE3D_LOD(_NoiseCache,s_linear_repeat_sampler,q*2+float3(.31,.53,.17),0).a*.3,_FogDetail.x);
        [branch]if(_FogDetail.y>.001)n=lerp(n,n*.85+SAMPLE_TEXTURE3D_LOD(_NoiseCache,s_linear_repeat_sampler,q*4,0).a*.15,_FogDetail.y);
        above+=wake.y*_FogDetail.x*.15;
        float thickness=_FogShape.y*(.55+base.a*1.1);
        float top=1-smoothstep(thickness*.35,thickness,above);
        float bottom=smoothstep(-1,.75,above);
        float cloud=lerp(1,smoothstep(.18,.8,n)*2.2,_FogNoise.y);
        float groundLayer=exp(-max(above,0)/max(2,_FogShape.y*.65));
        // Persistent unit channels disperse the mist locally, with curling
        // edges from the advected vector field. Darkness remains independent.
        float channel=1-exp(-wake.w*1.3*_FogDetail.x);
        return hidden*cloud*top*bottom*groundLayer*_FogShape.x*(1-channel*.85);
    }
    struct FogOutput{float4 scatter:SV_Target0;float depth:SV_Target1;};
    FogOutput March(Varyings input)
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv=input.positionCS.xy*_HalfSize.zw;
        uint2 pixel=min((uint2)(uv*_FowFullSize.xy),(uint2)_FowFullSize.xy-1);
        float rawDepth=CustomPassLoadCameraDepth(pixel);
        float3 endPoint=World(uv,rawDepth),origin=World(uv,UNITY_NEAR_CLIP_VALUE);
        float distance=length(endPoint-origin);float3 direction=(endPoint-origin)/max(distance,.001);
        float2 groundUV=frac(endPoint.xz/_WorldSize+.5);
        float darkness=SAMPLE_TEXTURE2D_LOD(_DarknessMask,s_linear_repeat_sampler,groundUV,0).r;
        FogOutput o;o.scatter=float4(0,0,0,darkness);o.depth=EyeDepth(rawDepth);
        float3 safeDirection=direction;safeDirection.x=abs(direction.x)<.00001?.00001:direction.x;safeDirection.y=abs(direction.y)<.00001?.00001:direction.y;safeDirection.z=abs(direction.z)<.00001?.00001:direction.z;
        float3 a=(_FogBoundsMin.xyz-origin)/safeDirection,b=(_FogBoundsMax.xyz-origin)/safeDirection;
        float3 lo=min(a,b),hi=max(a,b);
        float start=max(0,max(lo.x,max(lo.y,lo.z))),finish=min(distance,min(hi.x,min(hi.y,hi.z)));
        // Spend the fixed ray budget on the shallow layer near the visible
        // surface, rather than skipping over it inside the full terrain-height
        // bounding box. Concealment still applies if there is no mist sample.
        start=max(start,distance-(_FogShape.y*2+12)/max(.2,abs(direction.y)));
        if(finish<=start)return o;
        float stepSize=(finish-start)/_RaySteps;
        float jitter=ArmyHash(float3(pixel,19));float transmission=1,scattering=0;
        [loop]for(int i=0;i<_RaySteps;i++)
        {
            float3 p=origin+direction*(start+(i+jitter)*stepSize);
            float density=Density(p);
            float opacity=1-exp(-density*stepSize);
            // Approximate local cloud shading without additional shadow-map or noise samples.
            float illumination=.35+.65*exp(-density*_FogShape.y*.5);
            scattering+=transmission*opacity*illumination;
            transmission*=1-opacity;
            if(transmission<.008)break;
        }
        float angle=saturate(dot(direction,normalize(_FogLightDirection)));
        float scatter=.75+.25*pow(angle,4);
        float opacity=1-transmission;
        float3 colour=_FogRadiance.rgb*scatter*GetCurrentExposureMultiplier()*scattering/max(opacity,.0001);
        // Conceal the ground independently while retaining readable moving cloud folds.
        colour*=lerp(1,.35,darkness);
        o.scatter=float4(colour,1-(1-opacity)*(1-darkness));
        return o;
    }
    float4 Composite(Varyings input):SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv=input.positionCS.xy*_FowFullSize.zw;
        float sceneDepth=EyeDepth(CustomPassLoadCameraDepth((uint2)input.positionCS.xy));
        float2 grid=uv*_HalfSize.xy-.5,base=floor(grid),fraction=frac(grid);
        float4 result=0;float total=0;
        [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++)
        {
            float2 sampleUV=(base+float2(x,y)+.5)*_HalfSize.zw;
            float d=SAMPLE_TEXTURE2D_LOD(_FogDepth,s_point_clamp_sampler,sampleUV,0).r;
            float weight=(x?fraction.x:1-fraction.x)*(y?fraction.y:1-fraction.y);
            weight*=exp(-abs(d-sceneDepth)/max(1,sceneDepth*.02));
            result+=SAMPLE_TEXTURE2D_LOD(_FogScattering,s_point_clamp_sampler,sampleUV,0)*weight;total+=weight;
        }
        if(total<.00001)return SAMPLE_TEXTURE2D_LOD(_FogScattering,s_point_clamp_sampler,uv,0);
        return result/total;
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" }
        Pass { Name "Raymarch" ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment March
            ENDHLSL
        }
        Pass { Name "Composite" ZWrite Off ZTest Always Cull Off Blend SrcAlpha OneMinusSrcAlpha ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}

