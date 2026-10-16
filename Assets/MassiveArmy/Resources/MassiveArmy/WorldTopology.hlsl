#ifndef RTS_WORLD_TOPOLOGY
#define RTS_WORLD_TOPOLOGY
float WrapAxis(float p,float size){return p-floor(p/size+.5)*size;}
float2 WrapPosition(float2 p,float size){return p-floor(p/size+.5)*size;}
float2 WorldDelta(float2 from,float2 to,float size){float2 d=to-from;if(all(abs(d)<size*.5))return d;return WrapPosition(d,size);}
float WorldDistance(float2 a,float2 b,float size){return length(WorldDelta(a,b,size));}
// Every GPU topology grid is a power of two (validated by BattleSettings).
int2 WrapCell(int2 c,int n){return c&(n-1);}
float2 WorldUV(float2 p,float size){return frac(p/size+.5);}
float3 NearestWorld(float3 p,float3 reference,float size){p.xz=reference.xz+WorldDelta(reference.xz,p.xz,size);return p;}
#endif
