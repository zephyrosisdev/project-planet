#ifndef PLANET_WEATHER_DENSITY
#define PLANET_WEATHER_DENSITY
float _WeatherNativeDouble;float _WeatherDetailNoiseLod,_WeatherNoiseSizeScale;float4 _WeatherDetailMotion,_WeatherNoiseTuning,_WeatherOctaves;float _WeatherNoiseFrequency;
// Remap within existing texture cells before hardware trilinear filtering.
// Quintic interpolation removes the visible grid/derivative breaks when a cell
// spans kilometres, without adding texture fetches or increasing the volume.
float WeatherNoiseAt(float3 p,float lod){
 float mip=exp2(floor(lod));float3 grid=p/mip-.5,cell=floor(grid),f=frac(grid);
 f=f*f*f*(f*(f*6-15)+10);
 return _WeatherNoise.SampleLevel(sampler_WeatherNoise,(cell+f+.5)*mip/64,lod);
}
float Noise(float3 p){return WeatherNoiseAt(p,_WeatherNoiseLod);}
float3 WeatherPoint(float3 p){return float3(p.x*_WeatherWind.y-p.z*_WeatherWind.x,p.y,p.x*_WeatherWind.x+p.z*_WeatherWind.y);}
// Coverage and vertical shape reuse the same three noise fetches. The base and
// roof remain inside the conservative shell used by every ray/LOD/shadow pass.
float3 CloudShape(float3 up){float3 p=WeatherPoint(up);float3 offset=float3(_WeatherWind.z,_WeatherWind.w,11.3);
 float broad=Noise(p*8.2+offset),medium=Noise(p*18.1+offset),small=Noise(p*41.7+offset);
 float n=broad*.5+medium*.3+small*.2,threshold=lerp(.76,.24,_WeatherCloud.x);
 float cover=smoothstep(threshold-.055,threshold+.055,n);
 // Reuse existing noise for an irregular, thinner column; no extra fetches.
 float base=.12+.16*(1-small)+.08*medium;
 float thickness=.28+.4*smoothstep(.2,.8,broad*.4+medium*.2+small*.4);
 // Thin the column toward horizontal coverage edges, forming sloped banks.
 thickness*=lerp(.3,1,sqrt(cover));
 float top=min(.96,base+thickness);
 return float3(cover,base,top);
}
float Coverage(float3 up){return CloudShape(up).x;}
float CloudVertical(float layer,float3 shape){float local=(layer-shape.y)/max(.01,shape.z-shape.y);return saturate(min(local/.35,(1-local)/.5));}
// Analytic averaging across a ray step avoids visible sheets when a large
// planet's thin cloud layer is crossed by only a handful of marching samples.
float CloudVerticalIntegral(float x){
 x=saturate(x);
 return x<.35?x*x/.7:x<.5?x-.175:.575-(1-x)*(1-x);
}
float CloudVerticalFiltered(float layer,float3 shape,float halfWidth){
 float span=max(.01,shape.z-shape.y),local=(layer-shape.y)/span,w=max(.00001,halfWidth/span);
 return saturate((CloudVerticalIntegral(local+w)-CloudVerticalIntegral(local-w))/(2*w));
}
float CloudErosion(float3 detail){
 float lod=max(_WeatherNoiseLod,_WeatherDetailNoiseLod);
 float erosion=WeatherNoiseAt(detail,lod)*.65+WeatherNoiseAt(detail*2.1,lod)*.35;
 // Optional fine octave bank. Fade count and amplitude before dropping distant
 // samples; keep the original two base fetches and the default third octave.
 int requested=clamp((int)_WeatherOctaves.x,2,16);
 [branch]if(_WeatherDetailNoiseLod<1&&_WeatherNoiseTuning.y>0&&requested>2){
  float available=(requested-2)*(1-_WeatherDetailNoiseLod);
  float frequency=max(2,_WeatherNoiseTuning.z),amplitude=1,total=0,fine=0;
  [loop]for(int octave=0;octave<14;octave++){
   if(octave>=available||amplitude<.001)break;
   float coordinateMagnitude=max(1,max(abs(detail.x),max(abs(detail.y),abs(detail.z))));
   if(frequency*coordinateMagnitude>1048576)break; // Avoid meaningless float sub-texel octaves.
   float weight=amplitude*saturate(available-octave);
   fine+=WeatherNoiseAt(detail*frequency+float3(17.1,3.7,29.3)*(octave+1),lod)*weight;total+=weight;
   amplitude*=clamp(_WeatherOctaves.y,.1,.85);frequency*=clamp(_WeatherOctaves.z,1.2,4);
  }
  erosion=lerp(erosion,fine/max(.00001,total),saturate(_WeatherNoiseTuning.y)*(1-_WeatherDetailNoiseLod));
 }
 return lerp(erosion,smoothstep(.2,.8,erosion),saturate(_WeatherNoiseTuning.w));
}
// These are ray-marched density lobes, not particle meshes. Larger, softer
// interior lobes reuse the same two detail fetches at a lower spatial frequency.
float3 CloudDetailPoint(float3 p,float interior){
 float3 moving=WeatherPoint(p);
 float sn=_WeatherDetailMotion.x,cs=_WeatherDetailMotion.y;
 moving=float3(moving.x*cs-moving.z*sn,moving.y,moving.x*sn+moving.z*cs);
 return moving*_WeatherRadius*max(.1,_WeatherNoiseFrequency)/(350*max(.05,_WeatherNoiseSizeScale))*(1-saturate(interior)*_WeatherDetailMotion.z)+float3(_WeatherWind.z,_WeatherWind.w,0);
}
float CloudInterior(float vertical,float cover){return saturate(vertical)*smoothstep(.35,.85,cover);}
float CloudSampleDensity(float3 p,float vertical,float cover,float detailWeight){
 float interior=CloudInterior(vertical,cover);
 float erosion=CloudErosion(CloudDetailPoint(p,interior));
 return saturate(cover-erosion*saturate(_WeatherNoiseTuning.x)*detailWeight)*vertical*(1+interior*_WeatherDetailMotion.w);
}
float WeatherLayer(float3 p){
 #ifdef PLANET_GPU_DOUBLE
 if(_WeatherNativeDouble>.5){double square=dot((double3)p,(double3)p),inverse=(double)rsqrt((float)square);inverse*=1.5-.5*square*inverse*inverse;inverse*=1.5-.5*square*inverse*inverse;return (float)((square*inverse-1-(double)_WeatherAtmosphere.z)/(double)_WeatherAtmosphere.w);}
 #endif
 return (length(p)-1-_WeatherAtmosphere.z)/_WeatherAtmosphere.w;
}
float CloudDensity(float3 p){
 float layer=WeatherLayer(p);
 if(layer<=0||layer>=1)return 0;
 float3 shape=CloudShape(normalize(p));float vertical=CloudVertical(layer,shape);
 if(vertical<=0)return 0;
 float cover=shape.x;if(cover<.001)return 0;
 return CloudSampleDensity(p,vertical,cover,1);
}
bool WeatherRayShell(float3 centre,float eyeAltitude,float3 ray,float shellAltitude,out float2 range){
 #ifdef PLANET_GPU_DOUBLE
 if(_WeatherNativeDouble>.5){double alongD=dot((double3)ray,(double3)centre),cD=((double)eyeAltitude-shellAltitude)*(2+(double)eyeAltitude+shellAltitude),disc=alongD*alongD-cD;range=0;if(disc<0)return false;double inverse=(double)rsqrt(max(1e-30,(float)disc));inverse*=1.5-.5*disc*inverse*inverse;inverse*=1.5-.5*disc*inverse*inverse;double root=disc*inverse;range=float2((float)(alongD-root),(float)(alongD+root));return range.y>0;}
 #endif
 float along=dot(ray,centre);
 float c=(eyeAltitude-shellAltitude)*(2+eyeAltitude+shellAltitude);
 float discriminant=along*along-c;range=0;if(discriminant<0)return false;
 float root=sqrt(discriminant),q=along+(along<0?-root:root);
 if(abs(q)<1e-20){range=along;return range.y>0;}
 float other=c/q;range=float2(min(q,other),max(q,other));return range.y>0;
}
bool WeatherSphereFrom(float3 p,float3 ray,float radius,out float2 range){
 float along=-dot(ray,p);float3 side=p+ray*along;float disc=radius*radius-dot(side,side);range=0;
 if(disc<0)return false;float root=sqrt(disc);range=float2(along-root,along+root);return range.y>0;
}
#endif
