#ifndef PLANET_STRUCTURE_GRID_INCLUDED
#define PLANET_STRUCTURE_GRID_INCLUDED
// Object-local parameters are static across floating-origin rebases. Flat RTS art
// leaves the enable bit zero and shares the original meshes/materials unchanged.
float4 _ConstructionPlane,_ConstructionX,_ConstructionZ,_ConstructionScale;
float3 PlanetStructurePosition(float3 position){
 if(_ConstructionPlane.z<.5)return position;
 float3 scale=max(_ConstructionScale.xyz,.0001),p=position*scale;
 float3 q=float3(0,_ConstructionPlane.x,0)+_ConstructionX.xyz*p.x+_ConstructionZ.xyz*p.z;
 float3 up=normalize(q);float radius=_ConstructionPlane.y+p.y;float sag=dot(up.xz,up.xz)/(1+up.y)*radius;return float3(up.x*radius,p.y-sag,up.z*radius)/scale;
}
void PlanetStructureVertex(inout float3 position,inout float3 normal,inout float3 tangent){
 if(_ConstructionPlane.z<.5)return;
 float3 scale=max(_ConstructionScale.xyz,.0001),p=position*scale;
 float3 q=float3(0,_ConstructionPlane.x,0)+_ConstructionX.xyz*p.x+_ConstructionZ.xyz*p.z;float lengthQ=length(q);float3 up=q/lengthQ;
 float radius=_ConstructionPlane.y+p.y;
 float3 dx=(_ConstructionX.xyz-up*dot(up,_ConstructionX.xyz))*(radius/lengthQ)*scale.x/scale;
 float3 dz=(_ConstructionZ.xyz-up*dot(up,_ConstructionZ.xyz))*(radius/lengthQ)*scale.z/scale;
 float3 dy=up*scale.y/scale;
 normal=normalize(normal.x*cross(dy,dz)+normal.y*cross(dz,dx)+normal.z*cross(dx,dy));
 tangent=normalize(dx*tangent.x+dy*tangent.y+dz*tangent.z);
 float sag=dot(up.xz,up.xz)/(1+up.y)*radius;position=float3(up.x*radius,p.y-sag,up.z*radius)/scale;
}
#endif
