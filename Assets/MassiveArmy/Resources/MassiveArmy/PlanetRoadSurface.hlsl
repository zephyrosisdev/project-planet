// Road paint shares terrain depth, lighting and texture arrays; no overlay geometry.
StructuredBuffer<uint4> _PlanetRoadCells;
Texture2D<float> _PlanetRoadCoverage;
uint _PlanetRoadMask,_PlanetRoadEnabled;float _PlanetRoadWidth;
uint RoadCellHash(uint f,uint x,uint y){uint h=f*73856093u^x*19349663u^y*83492791u;h^=h>>16;h*=0x7feb352du;return h^(h>>15);}
#include "PlanetRoadCoordinates.hlsl"
float3 ApplyPlanetRoad(float3 colour,float3 d,float3 world,float terrainTextureHeight,inout float3 normal,inout float roughness){
 if(_PlanetRoadEnabled==0)return colour;
 float3 high=_PlanetObserverHigh,low=_PlanetObserverLow+world,body=high+low,a=abs(body);
 uint face=a.x>=a.y&&a.x>=a.z?(body.x>=0?0:1):a.y>=a.z?(body.y>=0?2:3):(body.z>=0?4:5);
 float n=dot(high,PlanetN[face]),nl=dot(low,PlanetN[face]);
 // Observer may lie in a face's equatorial plane. Use the fragment itself as the high part there.
 if(abs(n)<abs(nl)*.5){high=body;low=0;n=dot(high,PlanetN[face]);nl=0;}
 float2 cx,cy;
 if(_PlanetRoadAnchored>.5){float4 projected=RoadAnchoredCoordinate(world,face);cx=projected.xy;cy=projected.zw;}
 else {cx=RoadCoordinate(dot(high,PlanetU[face]),dot(low,PlanetU[face]),n,nl);cy=RoadCoordinate(dot(high,PlanetV[face]),dot(low,PlanetV[face]),n,nl);}
 #ifdef PLANET_GPU_DOUBLE
 double3 exactBody=(double3)_PlanetObserverHigh+(double3)_PlanetObserverLow+(double3)world;
 double denominator=exactBody.x*(double)PlanetN[face].x+exactBody.y*(double)PlanetN[face].y+exactBody.z*(double)PlanetN[face].z;
 double u=exactBody.x*(double)PlanetU[face].x+exactBody.y*(double)PlanetU[face].y+exactBody.z*(double)PlanetU[face].z;
 double v=exactBody.x*(double)PlanetV[face].x+exactBody.y*(double)PlanetV[face].y+exactBody.z*(double)PlanetV[face].z;
 double2 exactCell=(double2(u,v)/denominator+1.0)*(.5*(double)_PlanetRoadResolution);
 uint2 whole=(uint2)exactCell;
 cx=float2((float)whole.x,(float)(exactCell.x-(double)whole.x));cy=float2((float)whole.y,(float)(exactCell.y-(double)whole.y));
 #endif
 float2 base=floor(float2(cx.x,cy.x)),fraction=float2(frac(cx.x)+cx.y,frac(cy.x)+cy.y);
 float2 adjust=floor(fraction);base+=adjust;fraction-=adjust;
 uint2 cell=(uint2)clamp(base,0,(float)_PlanetRoadResolution-1);
 uint2 coarse=min(cell*64u/_PlanetRoadResolution,63u);
 if(_PlanetRoadCoverage.Load(int3(face*64+coarse.x,coarse.y,0))==0)return colour;
 uint slot=RoadCellHash(face+1,cell.x,cell.y)&_PlanetRoadMask;uint4 entry=0;
 // Table is <= 50% occupied; empty slot terminates misses. No silently truncated collision chains.
 [loop]for(uint probe=0;probe<=_PlanetRoadMask;probe++){entry=_PlanetRoadCells[slot];if(entry.x==0)return colour;if(entry.x==face+1&&entry.y==cell.x&&entry.z==cell.y)break;slot=(slot+1)&_PlanetRoadMask;}
 uint mask=entry.w;float2 uv=fraction,p=uv-.5;
 float2 span=2*_PlanetRadius/(float)_PlanetRoadResolution*dot(d,PlanetN[face])*sqrt(saturate(1-float2(pow(dot(d,PlanetU[face]),2),pow(dot(d,PlanetV[face]),2))));span=max(span,.001);
 float2 metric=p*span;float halfWidth=_PlanetRoadWidth*.5;
 float distance=max(abs(metric.x),abs(metric.y))-halfWidth;
 if((mask&1)!=0&&p.x<=0)distance=min(distance,abs(metric.y)-halfWidth);
 if((mask&2)!=0&&p.x>=0)distance=min(distance,abs(metric.y)-halfWidth);
 if((mask&4)!=0&&p.y<=0)distance=min(distance,abs(metric.x)-halfWidth);
 if((mask&8)!=0&&p.y>=0)distance=min(distance,abs(metric.x)-halfWidth);
 float aa=max(.03,max(length(ddx(metric)),length(ddy(metric))));
 float edge=1-smoothstep(-.5-aa,.5+aa,distance);if(edge<=0)return colour;
 float3 relative=world-_PlanetTextureCamera;float3 dx=ddx(relative),dy=ddy(relative);
 float3 pos=TerrainBandPosition(relative,0,8);PlanetLayer data=PlanetLayerData(pos,normal,dx,dy,8);
 float3 asphalt=PlanetLayerColour(pos,normal,dx,dy,8);
 // Surface height modulates only the shoulder: the carriageway remains continuous.
 edge=saturate(edge+(data.height-terrainTextureHeight)*_HeightBlendStrength*4*edge*(1-edge));
 float lines=0;
 if((mask&3)!=0){float dash=smoothstep(.35,.45,frac(uv.x*span.x/6));lines=max(lines,(1-smoothstep(.07,.07+aa,abs(metric.y)))*dash*(mask==3?1:step(halfWidth,abs(metric.x))));}
 if((mask&12)!=0){float dash=smoothstep(.35,.45,frac(uv.y*span.y/6));lines=max(lines,(1-smoothstep(.07,.07+aa,abs(metric.x)))*dash*(mask==12?1:step(halfWidth,abs(metric.y))));}
 asphalt=lerp(asphalt,float3(.7,.68,.51),lines*.75);roughness=lerp(roughness,data.roughness,edge);
 float3 perturb=data.perturb-normal*dot(normal,data.perturb);normal=normalize(normal+perturb*_NormalStrength*edge);
 return lerp(colour,asphalt,edge);
}
