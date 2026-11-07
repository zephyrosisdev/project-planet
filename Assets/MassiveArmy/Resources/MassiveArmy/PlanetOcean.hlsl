#ifndef PLANET_GERSTNER_OCEAN
#define PLANET_GERSTNER_OCEAN
float4 _OceanWaves,_OceanPhase,_OceanFoam,_OceanFoamPhase;
float _OceanSteepness,_OceanFoamDepth;
static const float3 OceanDirections[4]={float3(.940721,.282216,.188144),float3(-.182574,.912871,.365148),float3(.303046,-.505076,.808122),float3(.744208,.620174,-.248069)};
static const float4 OceanLengths=float4(1,.61,.37,.23),OceanAmplitudes=float4(.5,.27,.15,.08);
float3 OceanWave(float3 local,float3 up,float depth,out float3 normal,out float crest){
    float attenuation=smoothstep(.2,max(.3,_OceanWaves.w),depth);
    float3 axis=abs(up.y)<.95?float3(0,1,0):float3(1,0,0);
    float3 right=normalize(cross(axis,up)),forward=cross(up,right);
    float3 dx=right,dz=forward,offset=0;crest=0;
    // Bound the sum of horizontal derivatives to avoid folded waves.
    float q=saturate(_OceanSteepness)/max(.001,_OceanWaves.x*6.2831853/max(4,_OceanWaves.y)*2.07);
    [unroll]for(uint wave=0;wave<4;wave++){
        float k=6.2831853/(max(4,_OceanWaves.y)*OceanLengths[wave]);
        // Large distant triangles cannot resolve metre-scale waves: fade before aliasing.
        float band=1-smoothstep(600,3000,length(local)*k);
        float amplitude=_OceanWaves.x*OceanAmplitudes[wave]*attenuation*band;
        float3 direction=OceanDirections[wave],tangent=direction-up*dot(up,direction);
        float phase=dot(local,direction)*k+_OceanPhase[wave],s,c;sincos(phase,s,c);
        offset+=amplitude*(up*s+q*tangent*c);
        float3 derivative=amplitude*k*(up*c-q*tangent*s);
        dx+=derivative*dot(right,direction);dz+=derivative*dot(forward,direction);
        crest+=q*amplitude*k*s*dot(tangent,tangent);
    }
    normal=normalize(cross(dx,dz));return offset;
}
float OceanHash(float3 p){p-=floor(p/256)*256;return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
float OceanNoise(float3 p){float3 cell=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(lerp(OceanHash(cell),OceanHash(cell+float3(1,0,0)),f.x),lerp(OceanHash(cell+float3(0,1,0)),OceanHash(cell+float3(1,1,0)),f.x),f.y),lerp(lerp(OceanHash(cell+float3(0,0,1)),OceanHash(cell+float3(1,0,1)),f.x),lerp(OceanHash(cell+float3(0,1,1)),OceanHash(cell+1),f.x),f.y),f.z);
}
float OceanFoam(float3 local,float depth,float crest){
    float shallow=1-smoothstep(.2,max(.3,_OceanFoamDepth),depth);
    if(shallow<.001)return 0;
    float pixels=max(.01,length(ddx(local))+length(ddy(local)));
    float slope=max(.04,(abs(ddx(depth))+abs(ddy(depth)))/pixels);
    float edge=1-smoothstep(0,max(.1,_OceanFoam.x),depth/slope);
    float3 p=local/max(.1,_OceanFoam.z)+_OceanFoamPhase.xyz;
    float noise=OceanNoise(p)*.7+OceanNoise(p*2)*.3;
    float breakup=smoothstep(.42,.68,noise);
    float breaker=smoothstep(.25,.6,crest)*shallow*.45;
    return saturate((edge*.7+breaker)*breakup*_OceanFoam.y);
}
#endif
