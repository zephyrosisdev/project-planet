Texture2D<float> _PlanetDepositIndex;
UNITY_DECLARE_TEX2DARRAY(_ResourceNodeTextures);
UNITY_DECLARE_TEX2DARRAY(_ResourceNodeNormals);
struct PlanetDeposit{float4 centre,right,forward,appearance;};StructuredBuffer<PlanetDeposit> _PlanetDeposits;
float4 _ResourceNodeNormalNeutral[7];uint _PlanetDepositResolution;
float4 PlanetResourceDecal(float3 direction,float h,inout float3 normal){
 float3 p=direction*(_PlanetRadius+h),dx=ddx(p),dy=ddy(p);int2 cell=PlanetPositionCell(PlanetEncode(direction),_PlanetDepositResolution);
 uint index=(uint)round(_PlanetDepositIndex.Load(int3(cell,0))*65535);if(index==0)return 0;PlanetDeposit deposit=_PlanetDeposits[index-1];if(deposit.appearance.y<.5)return 0;
 float3 delta=p-PlanetPosition(deposit.centre.xyz);float2 uv=float2(dot(delta,deposit.right.xyz)/deposit.right.w,dot(delta,deposit.forward.xyz)/deposit.forward.w)+.5;if(any(uv<0)||any(uv>1))return 0;
 float2 gx=float2(dot(dx,deposit.right.xyz)/deposit.right.w,dot(dx,deposit.forward.xyz)/deposit.forward.w),gy=float2(dot(dy,deposit.right.xyz)/deposit.right.w,dot(dy,deposit.forward.xyz)/deposit.forward.w);
 float4 colour=_ResourceNodeTextures.SampleGrad(sampler_ResourceNodeTextures,float3(uv,deposit.appearance.x),gx,gy);
 float3 bump=_ResourceNodeNormals.SampleGrad(sampler_ResourceNodeNormals,float3(uv,deposit.appearance.x),gx,gy).xyz;float2 relief=(bump.xy-_ResourceNodeNormalNeutral[(uint)deposit.appearance.x].xy)*2/max(.25,bump.z*2-1);
 float3 perturb=deposit.right.xyz*relief.x+deposit.forward.xyz*relief.y;normal=normalize(normal+(perturb-normal*dot(normal,perturb))*colour.a);return colour;
}
