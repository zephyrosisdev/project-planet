#ifndef PLANET_WEATHER_DENSITY
#define PLANET_WEATHER_DENSITY
float Noise(float3 p){return _WeatherNoise.SampleLevel(sampler_WeatherNoise,p/64,_WeatherNoiseLod);}
float3 WeatherPoint(float3 p){return float3(p.x*_WeatherWind.y-p.z*_WeatherWind.x,p.y,p.x*_WeatherWind.x+p.z*_WeatherWind.y);}
float Coverage(float3 up){float3 p=WeatherPoint(up);float3 offset=float3(_WeatherWind.z,_WeatherWind.w,11.3);float n=Noise(p*8.2+offset)*.5+Noise(p*18.1+offset)*.3+Noise(p*41.7+offset)*.2;float threshold=lerp(.76,.24,_WeatherCloud.x);return smoothstep(threshold-.055,threshold+.055,n);}
float CloudDensity(float3 p){
 float layer=(length(p)-1-_WeatherAtmosphere.z)/_WeatherAtmosphere.w;
 float vertical=smoothstep(0,.18,layer)*(1-smoothstep(.65,1,layer));
 if(vertical<=0)return 0;
 float cover=Coverage(normalize(p));if(cover<.001)return 0;
 float3 detail=WeatherPoint(p)*_WeatherRadius/350+float3(_WeatherWind.z,_WeatherWind.w,0);
 float erosion=Noise(detail)*.65+Noise(detail*2.1)*.35;
 return saturate(cover-erosion*.42)*vertical;
}
bool WeatherSphereFrom(float3 p,float3 ray,float radius,out float2 range){
 float along=-dot(ray,p);float3 side=p+ray*along;float disc=radius*radius-dot(side,side);range=0;
 if(disc<0)return false;float root=sqrt(disc);range=float2(along-root,along+root);return range.y>0;
}
#endif
