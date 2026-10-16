#ifndef MASSIVE_PLANET_STAMPS
#define MASSIVE_PLANET_STAMPS
Texture2DArray<float> _PlanetStampMaps;SamplerState sampler_PlanetStampMaps;uint _PlanetUseStamps;
struct PlanetStampCornerData {float4 u,v,appearance;};StructuredBuffer<PlanetStampCornerData> _PlanetStampCorners;
float2 PlanetStampCorner(float3 d,uint face,int2 lattice){const int points=25;PlanetStampCornerData c=_PlanetStampCorners[(face*points+lattice.y)*points+lattice.x];float2 uv=float2(dot(d,c.u.xyz)+c.u.w,dot(d,c.v.xyz)+c.v.w);
 float edge=saturate((.5-max(abs(uv.x),abs(uv.y)))*10);edge=edge*edge*(3-2*edge);return float2((_PlanetStampMaps.SampleLevel(sampler_PlanetStampMaps,float3(uv+.5,c.appearance.x),0)*c.appearance.y+c.appearance.z)*edge,c.appearance.w);
}
float2 PlanetStampedTerrain(float3 d){const int grid=24;float3 a=abs(d);uint face=a.x>=a.y&&a.x>=a.z?(d.x>=0?0:1):a.y>=a.z?(d.y>=0?2:3):(d.z>=0?4:5);
 float2 uv=float2(dot(d,PlanetU[face]),dot(d,PlanetV[face]))/dot(d,PlanetN[face]);float2 q=(uv+1)*(grid*.5);int2 cell=clamp((int2)floor(q),0,grid-1);float2 f=q-cell;f=f*f*(3-2*f);
 float2 h00=PlanetStampCorner(d,face,cell),h10=PlanetStampCorner(d,face,cell+int2(1,0)),h01=PlanetStampCorner(d,face,cell+int2(0,1)),h11=PlanetStampCorner(d,face,cell+1);
 return lerp(lerp(h00,h10,f.x),lerp(h01,h11,f.x),f.y);
}
#endif
