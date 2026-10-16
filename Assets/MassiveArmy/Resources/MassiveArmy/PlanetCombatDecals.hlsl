struct PlanetRemains {float4 centre,tangent,metadata;};
StructuredBuffer<PlanetRemains> _PlanetRemains;StructuredBuffer<uint> _PlanetRemainsIndex;uint _PlanetRemainsResolution;
UNITY_DECLARE_TEX2DARRAY(_PlanetRemainsColours);UNITY_DECLARE_TEX2DARRAY(_PlanetRemainsNormals);float4 _PlanetRemainsNeutral[6];
float4 PlanetRemainsDecal(float3 d,float height,inout float3 normal){
 float3 p=d*(_PlanetRadius+height),dx=ddx(p),dy=ddy(p);int2 cell=PlanetPositionCell(PlanetEncode(d),_PlanetRemainsResolution);uint serial=_PlanetRemainsIndex[PlanetIndex(cell,_PlanetRemainsResolution)];if(serial==0)return 0;PlanetRemains stamp=_PlanetRemains[(serial-1)%512];if((uint)stamp.metadata.w!=serial)return 0;
 float3 right=stamp.tangent.xyz,forward=cross(stamp.centre.xyz,right),delta=p-PlanetPosition(stamp.centre.xyz);float scale=stamp.centre.w;float2 uv=float2(dot(delta,right),dot(delta,forward))/scale+.5;if(any(uv<0)||any(uv>1))return 0;
 float2 gx=float2(dot(dx,right),dot(dx,forward))/scale,gy=float2(dot(dy,right),dot(dy,forward))/scale;uint kind=(uint)stamp.metadata.x;float4 colour=_PlanetRemainsColours.SampleGrad(sampler_PlanetRemainsColours,float3(uv,kind),gx,gy);float3 bump=_PlanetRemainsNormals.SampleGrad(sampler_PlanetRemainsNormals,float3(uv,kind),gx,gy).xyz;float2 relief=(bump.xy-_PlanetRemainsNeutral[kind].xy)*2/max(.25,bump.z*2-1);float3 perturb=right*relief.x+forward*relief.y;normal=normalize(normal+(perturb-normal*dot(perturb,normal))*colour.a);return colour;
}

Texture2DArray<float4> _PlanetScorchColour,_PlanetScorchRelief;
SamplerState sampler_PlanetScorchColour;
StructuredBuffer<uint> _PlanetScorchCells;uint _PlanetScorchActive;
float3 PlanetNuclearScorch(float3 d,inout float3 normal){
 if(_PlanetScorchActive==0)return 1;
 float2 encoded=PlanetEncode(d);int2 cell=PlanetPositionCell(encoded,128);
 if(_PlanetScorchCells[PlanetIndex(cell,128)]==0)return 1;
 uint face=(uint)encoded.x;float2 uv=float2(encoded.x-face,encoded.y);
 // Derive gradients on this face so a face-ID discontinuity cannot choose a bad footprint.
 float denominator=dot(d,PlanetN[face]);float3 dx=ddx(d),dy=ddy(d);
 float2 gx=(float2(dot(dx,PlanetU[face]),dot(dx,PlanetV[face]))-((uv*2-1)*dot(dx,PlanetN[face])))/denominator*.5;
 float2 gy=(float2(dot(dy,PlanetU[face]),dot(dy,PlanetV[face]))-((uv*2-1)*dot(dy,PlanetN[face])))/denominator*.5;
 float3 relief;
 // RGBA8 stores exact neutral as 128, avoiding a whole-surface normal bias.
 relief=(_PlanetScorchRelief.SampleGrad(sampler_PlanetScorchColour,float3(uv,face),gx,gy).xyz-128.0/255)*4;
 normal=normalize(normal+relief-normal*dot(relief,normal));
 return _PlanetScorchColour.SampleGrad(sampler_PlanetScorchColour,float3(uv,face),gx,gy).rgb;
}
