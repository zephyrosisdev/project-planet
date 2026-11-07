#ifndef MASSIVE_PLANET_SURFACE
#define MASSIVE_PLANET_SURFACE
#include "PlanetTopology.hlsl"
#include "PlanetStamps.hlsl"
float _PlanetRadius,_PlanetHeight,_PlanetSeed,_PlanetOcean,_PlanetReliefBound;
uint _PlanetCraterCount;
#if defined(PLANET_SINGLE_CRATER)
float4 _PlanetSingleCrater,_PlanetSingleProfile,_PlanetEditCap;
#elif !defined(PLANET_SPECIALIZE_TERRAIN) || defined(PLANET_TERRAIN_EDITS)
uint _PlanetPlateauCount;
cbuffer PlanetTerrainEditData { float4 _PlanetCraterMasks[384],_PlanetCraters[32],_PlanetCraterProfiles[32],_PlanetPlateaus[2]; };
float4 _PlanetEditCap;
#endif
// Native edit arithmetic is confined to explicitly opted-in rendering/terrain consumers.
#ifdef PLANET_GPU_DOUBLE
#define PLANET_EDIT_VECTOR double3
double3 PlanetEditUnit(double3 value){double square=(value.x*value.x+value.y*value.y+value.z*value.z),inverse=(double)rsqrt((float)square);inverse*=1.5-.5*square*inverse*inverse;inverse*=1.5-.5*square*inverse*inverse;return value*inverse;}
float PlanetEditMetres(double3 direction,float3 centre){double3 delta=direction-PlanetEditUnit((double3)centre);double square=(delta.x*delta.x+delta.y*delta.y+delta.z*delta.z);if(square<=0)return 0;double inverse=(double)rsqrt((float)square);inverse*=1.5-.5*square*inverse*inverse;inverse*=1.5-.5*square*inverse*inverse;return (float)(square*inverse*(double)_PlanetRadius);}
#else
#define PLANET_EDIT_VECTOR float3
float3 PlanetEditUnit(float3 value){return normalize(value);}
float PlanetEditMetres(float3 direction,float3 centre){return length(direction-centre)*_PlanetRadius;}
#endif
float PlanetReliefCurvature(){return _PlanetRadius>=20000?abs(_PlanetHeight)*.0000018:80*abs(_PlanetHeight)/max(1,_PlanetRadius*_PlanetRadius);}
float PlanetBaseHeight(float3 d){if(_PlanetHeight==0)return 0;float s=_PlanetSeed,continent=sin(d.x*8+s)*sin(d.y*7-s)*cos(d.z*9+s);
 if(_PlanetRadius<20000)return _PlanetHeight*(continent*.65+sin(d.x*19+d.y*13+d.z*17+s)*.2);
 if(_PlanetUseStamps!=0){float2 terrain=PlanetStampedTerrain(d);return _PlanetHeight*(continent*.30-.06+terrain.x*(.02+terrain.y*.70));}
 float mountain=saturate((sin(d.x*19+d.y*13+d.z*17+s)-.1)/.7);mountain=mountain*mountain*(3-2*mountain);
 float ridge=sin((d.x+d.y*.63+d.z*.37)*(_PlanetRadius/1400)+s+continent*2);ridge=1-ridge*ridge;
 float hill=sin((d.x*.31-d.y*.8+d.z)*(_PlanetRadius/700)+s);
 return _PlanetHeight*(continent*.30-.06+mountain*ridge*ridge*.60+(1-mountain)*hill*.008);
}
#if !defined(PLANET_SPECIALIZE_TERRAIN) || defined(PLANET_TERRAIN_EDITS)
float PlanetCraterDelta(PLANET_EDIT_VECTOR d){if(_PlanetCraterCount==0)return 0;int2 cell=PlanetPositionCell(PlanetEncode((float3)d),8);float4 m=_PlanetCraterMasks[PlanetIndex(cell,8)];uint bits=(uint)m.x|((uint)m.y<<16);float low=0,high=0;
 [loop]while(bits!=0){uint i=firstbitlow(bits);bits&=bits-1;float4 c=_PlanetCraters[i];float q=PlanetEditMetres(d,c.xyz)/c.w;float bowl=q<1?-_PlanetCraterProfiles[i].x*(1-q*q)*(1-q*q):0;float rim=max(0,1-abs(q-1)/.25);float delta=bowl+_PlanetCraterProfiles[i].x*.18*rim*rim;low=min(low,delta);high=max(high,delta);}return low+high;}
#endif
float PlanetFeatureCurvature(float3 center,float bound){
#if defined(PLANET_SINGLE_CRATER)
 float4 c=_PlanetSingleCrater;return length(center-c.xyz*_PlanetRadius)<=bound+c.w*1.25?8*_PlanetSingleProfile.x/(c.w*c.w):0;
#elif !defined(PLANET_SPECIALIZE_TERRAIN) || defined(PLANET_TERRAIN_EDITS)
 float value=0;[loop]for(uint i=0;i<_PlanetCraterCount;i++){float4 c=_PlanetCraters[i];if(length(center-c.xyz*_PlanetRadius)<=bound+c.w*1.25)value=max(value,8*_PlanetCraterProfiles[i].x/(c.w*c.w));}return value;
#else
 return 0;
#endif
}
float PlanetHeight(PLANET_EDIT_VECTOR direction){PLANET_EDIT_VECTOR d=PlanetEditUnit(direction);float h=PlanetBaseHeight((float3)d);
#if defined(PLANET_SINGLE_CRATER)
 if(dot((float3)d,_PlanetEditCap.xyz)<_PlanetEditCap.w)return h;
 float q=PlanetEditMetres(d,_PlanetSingleCrater.xyz)/_PlanetSingleCrater.w,depth=_PlanetSingleProfile.x;
 float bowl=q<1?-depth*(1-q*q)*(1-q*q):0,rim=max(0,1-abs(q-1)/.25);return h+bowl+depth*.18*rim*rim;
#elif !defined(PLANET_SPECIALIZE_TERRAIN) || defined(PLANET_TERRAIN_EDITS)
 if((_PlanetPlateauCount|_PlanetCraterCount)==0||dot((float3)d,_PlanetEditCap.xyz)<_PlanetEditCap.w)return h;
 [loop]for(uint i=0;i<_PlanetPlateauCount;i++){float distance=PlanetEditMetres(d,_PlanetPlateaus[i].xyz);float t=saturate((distance-260)/280);t=t*t*(3-2*t);h=lerp(_PlanetPlateaus[i].w,h,t);}return h+PlanetCraterDelta(d);
#else
 return h;
#endif
}
float3 PlanetPosition(float3 direction){float3 d=normalize(direction);return d*(_PlanetRadius+PlanetHeight(d));}
float PlanetClimateHash(int3 p){uint h=(uint)p.x*1597334677u^(uint)p.y*3812015801u^(uint)p.z*2798796415u;h^=h>>16;h*=2246822519u;h^=h>>13;return (h&0xffffffu)/16777215.0;}
float PlanetNoise3Cell(int3 c,float3 f){f=f*f*(3-2*f);
 return lerp(lerp(lerp(PlanetClimateHash(c),PlanetClimateHash(c+int3(1,0,0)),f.x),lerp(PlanetClimateHash(c+int3(0,1,0)),PlanetClimateHash(c+int3(1,1,0)),f.x),f.y),lerp(lerp(PlanetClimateHash(c+int3(0,0,1)),PlanetClimateHash(c+int3(1,0,1)),f.x),lerp(PlanetClimateHash(c+int3(0,1,1)),PlanetClimateHash(c+1),f.x),f.y),f.z);}
float PlanetNoise3(float3 p){return PlanetNoise3Cell((int3)floor(p),frac(p));}
float2 PlanetClimateAt(float3 d,float seed){d=normalize(d);float3 p=float3(d.x*.8+d.y*.6,-d.x*.48+d.y*.64+d.z*.6,d.x*.36-d.y*.48+d.z*.8)+float3(seed,seed*.37,-seed*.61);
 return saturate(float2(1-abs(d.y)+(PlanetNoise3(p*3.1+float3(17,43,11))-.5)*.24,PlanetNoise3(p*4.3)*.75+PlanetNoise3(p*9.7+float3(31,7,53))*.25));}
float2 PlanetClimate(float3 d){return PlanetClimateAt(d,_PlanetSeed);}
float3 PlanetSurfaceNormal(PLANET_EDIT_VECTOR direction,float h){float3 d=(float3)direction;float3 t=normalize(cross(abs(d.y)>.95?float3(1,0,0):float3(0,1,0),d)),b=cross(d,t);float e=.0001;
#ifdef PLANET_GPU_DOUBLE
 e=max(1e-10,2.0/_PlanetRadius);
#endif
 // Height differences avoid subtracting two planet-sized positions. This keeps
 // small slopes in single precision with the same four height evaluations.
 float radius=max(1,_PlanetRadius+h);
 float sx=(PlanetHeight(PlanetEditUnit(direction+(PLANET_EDIT_VECTOR)t*e))-PlanetHeight(PlanetEditUnit(direction-(PLANET_EDIT_VECTOR)t*e)))/(2*e*radius);
 float sy=(PlanetHeight(PlanetEditUnit(direction+(PLANET_EDIT_VECTOR)b*e))-PlanetHeight(PlanetEditUnit(direction-(PLANET_EDIT_VECTOR)b*e)))/(2*e*radius);
 return normalize(d-t*sx-b*sy);
}
// Compute consumers which do not already have a sampled height retain this API.
float3 PlanetSurfaceNormal(float3 d){return PlanetSurfaceNormal(d,PlanetHeight(d));}
#endif
