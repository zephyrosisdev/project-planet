#ifndef PLANET_AIR_SCATTERING
#define PLANET_AIR_SCATTERING
Texture2DArray<float2> _AirColumns;SamplerState sampler_AirColumns;
// enabled, column slice, Rayleigh strength * pressure, aerosol strength * pressure * haze
float4 _AirScattering;
float4 _AirVariation,_AirGravityTide;
struct AirIntegral {float3 light;float3 transmission;};
AirIntegral EmptyAir(){AirIntegral a;a.light=0;a.transmission=1;return a;}
float AirHeightAlongRay(float3 ray,float t){
 float3 eye=-_WeatherCentre,delta=ray*t;
 return max(0,_WeatherEyeAltitude+(2*dot(eye,delta)+dot(delta,delta))/(length(eye+delta)+length(eye)));
}
float AirTideVariation(float3 up){
 float variation=1;
 float solar=dot(up,_WeatherSun),gravity=dot(up,_AirGravityTide.xyz);
 variation+=_AirVariation.z*(1.5*solar*solar-.5)+_AirVariation.w*_AirGravityTide.w*(1.5*gravity*gravity-.5);
 return clamp(variation,.8,1.2);
}
float3 AirSunTransmission(float3 up,float h,float variation){
 float radius=1+h,mu=dot(up,_WeatherSun),horizon=-sqrt(max(0,h*(2+h)))/radius;
 float visibility=smoothstep(horizon-.004675,horizon+.004675,mu);
 if(visibility<=0)return 0;
 float u=sqrt(saturate((mu-horizon)/max(1e-6,1-horizon))),v=sqrt(saturate(h/max(1e-8,_WeatherAtmosphere.x)));
 float2 grid=float2(u,v)*float2(127,63),f=frac(grid);f=f*f*f*(f*(f*6-15)+10);
 float2 uv=(floor(grid)+f+.5)/float2(128,64);
 float2 columns=_AirColumns.SampleLevel(sampler_AirColumns,float3(uv,_AirScattering.y),0);
 return visibility*exp((-columns.x*float3(.0464,.108,.264)*_AirScattering.z-columns.y*.024*_AirScattering.w)*variation);
}
float3 AirSunTransmission(float3 up,float h){return AirSunTransmission(up,h,AirTideVariation(up));}
AirIntegral ScatterAir(float3 ray,float start,float end,int samples){
 AirIntegral a=EmptyAir();if(end<=start)return a;
 float2 scale=max(float2(1e-8,1e-8),_WeatherAtmosphere.x*float2(.125,.01875));
 float3 betaR=float3(.0464,.108,.264)*_AirScattering.z/scale.x;
 float betaM=.024*_AirScattering.w/scale.y;
 float mu=dot(ray,_WeatherSun),phaseR=.0596831*(1+mu*mu),g=.76;
 float phaseM=.0795775*(1-g*g)/pow(max(.01,1+g*g-2*g*mu),1.5);
 float step=(end-start)/samples;
 // Broad density fields are coherent through the short column. Sample once at
 // closest approach instead of adding a 3D texture fetch to every march step.
 float closest=clamp(dot(_WeatherCentre,ray),start,end);float3 noiseUp=normalize(ray*closest-_WeatherCentre);
 float noiseOffset=0;if(_AirVariation.x>0)noiseOffset=(WeatherNoiseAt(noiseUp*_AirVariation.y+_WeatherWind.z,1)*2-1)*_AirVariation.x;
 [loop]for(int i=0;i<samples;i++){
  float t=start+(i+.5)*step;float3 p=ray*t-_WeatherCentre;
  float h=AirHeightAlongRay(ray,t);float3 up=normalize(p);float variation=clamp(AirTideVariation(up)+noiseOffset,.8,1.2);
  float top=_WeatherAtmosphere.x*min(1,.98+(variation-1)*.25);
  float2 density=exp(-h/scale)*variation*(1-smoothstep(top*.7,top,h));
  float3 extinction=betaR*density.x+betaM*density.y;
  float3 segment=exp(-extinction*step);
  float3 source=betaR*density.x*phaseR+betaM*density.y*.9*phaseM;
  // Analytically integrate constant coefficients within each of the existing six steps.
  float3 integral=(1-segment)/max(extinction,1e-8);
  a.light+=a.transmission*integral*source*AirSunTransmission(up,h,variation)*_WeatherSunColour*20;
  a.transmission*=segment;
 }
 return a;
}
#endif
