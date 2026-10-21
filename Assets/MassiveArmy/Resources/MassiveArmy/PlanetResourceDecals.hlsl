Texture2D<float> _PlanetDepositIndex;
UNITY_DECLARE_TEX2DARRAY(_ResourceNodeTextures);
UNITY_DECLARE_TEX2DARRAY(_ResourceNodeNormals);
struct PlanetDeposit{float4 centre,right,forward,appearance;};StructuredBuffer<PlanetDeposit> _PlanetDeposits;
float4 _ResourceNodeNormalNeutral[7];uint _PlanetDepositResolution,_PlanetDepositFineResolution,_PlanetDepositCellMask;
StructuredBuffer<uint4> _PlanetDepositCells;StructuredBuffer<uint> _PlanetDepositCandidates;
float4 PlanetRecordedResourceDecal(float3 direction,float h,inout float3 normal){
 float3 p=direction*(_PlanetRadius+h),dx=ddx(p),dy=ddy(p);int2 cell=PlanetPositionCell(PlanetEncode(direction),_PlanetDepositResolution);
 if(_PlanetDepositIndex.Load(int3(cell,0))==0)return 0;
 int2 fine=PlanetPositionCell(PlanetEncode(direction),_PlanetDepositFineResolution);uint face=fine.x/_PlanetDepositFineResolution;
 uint key=(face*_PlanetDepositFineResolution+fine.y)*_PlanetDepositFineResolution+fine.x%_PlanetDepositFineResolution+1;
 uint slot=(key*2654435761u)&_PlanetDepositCellMask;uint4 bucket=0;
 [loop]for(uint probe=0;probe<16;probe++){bucket=_PlanetDepositCells[slot];if(bucket.x==0)return 0;if(bucket.x==key)break;slot=(slot+1)&_PlanetDepositCellMask;}
 if(bucket.x!=key)return 0;
 PlanetDeposit deposit=(PlanetDeposit)0;float2 uv=0;bool found=false;
 [loop]for(uint at=0;at<bucket.z;at++){
  deposit=_PlanetDeposits[_PlanetDepositCandidates[bucket.y+at]];if(deposit.appearance.y<.5)continue;
  float3 delta=p-deposit.centre.xyz*(_PlanetRadius+deposit.centre.w);uv=float2(dot(delta,deposit.right.xyz)/deposit.right.w,dot(delta,deposit.forward.xyz)/deposit.forward.w)+.5;
  if(all(uv>=0)&&all(uv<=1)){found=true;break;}
 }
 if(!found)return 0;
 float2 gx=float2(dot(dx,deposit.right.xyz)/deposit.right.w,dot(dx,deposit.forward.xyz)/deposit.forward.w),gy=float2(dot(dy,deposit.right.xyz)/deposit.right.w,dot(dy,deposit.forward.xyz)/deposit.forward.w);
 float4 colour=_ResourceNodeTextures.SampleGrad(sampler_ResourceNodeTextures,float3(uv,deposit.appearance.x),gx,gy);
 float3 bump=_ResourceNodeNormals.SampleGrad(sampler_ResourceNodeNormals,float3(uv,deposit.appearance.x),gx,gy).xyz;float2 relief=(bump.xy-_ResourceNodeNormalNeutral[(uint)deposit.appearance.x].xy)*2/max(.25,bump.z*2-1);
 float3 perturb=deposit.right.xyz*relief.x+deposit.forward.xyz*relief.y;normal=normalize(normal+(perturb-normal*dot(normal,perturb))*colour.a);return colour;
}
// Same integer cell hash/jitter as PlanetProceduralResources; no planet-wide node buffer.
uint _NaturalResolution,_NaturalSeed,_NaturalTypeCount,_NaturalOverrideMask;
float _NaturalHomeCos,_NaturalHomeInnerCos;float3 _NaturalHomeA,_NaturalHomeB;float4 _NaturalTypes[32];StructuredBuffer<uint> _NaturalOverrides;
uint NaturalHash(uint x){x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}
float NaturalFraction(uint x){return (x&0xffffffu)/16777216.0;}
float4 PlanetNaturalResourceDecal(float3 direction,float h,inout float3 normal){
 [branch]if(dot(direction,_NaturalHomeA)>_NaturalHomeInnerCos||dot(direction,_NaturalHomeB)>_NaturalHomeInnerCos)return 0;
 float3 p=direction*(_PlanetRadius+h),dx=ddx(p),dy=ddy(p);
 // A subpixel cosmetic decal needs no geology or texture work.
 if(_NaturalTypeCount==0||max(length(dx),length(dy))>8)return 0;
 int2 cell=PlanetPositionCell(PlanetEncode(direction),_NaturalResolution);uint face=cell.x/_NaturalResolution,x=cell.x%_NaturalResolution;
 uint key=(face*_NaturalResolution+cell.y)*_NaturalResolution+x+1,hash=NaturalHash(key^_NaturalSeed);if(hash%100>=65)return 0;
 float2 uvCell=float2(x+.2+.6*NaturalFraction(NaturalHash(hash+1)),cell.y+.2+.6*NaturalFraction(NaturalHash(hash+2)));
 float3 up=PlanetDirection(face,uvCell*2/_NaturalResolution-1);
 if(dot(up,_NaturalHomeA)>_NaturalHomeCos||dot(up,_NaturalHomeB)>_NaturalHomeCos)return 0;
 float4 type=_NaturalTypes[NaturalHash(hash+3)%_NaturalTypeCount];float3 right=normalize(cross(abs(up.y)>.95?float3(1,0,0):float3(0,1,0),up)),forward=cross(up,right);
 float3 delta=(direction-up)*(_PlanetRadius+h);float2 uv=float2(dot(delta,right)/type.y,dot(delta,forward)/type.z)+.5;
 [branch]if(any(uv<0)||any(uv>1))return 0;
 uint slot=NaturalHash(key)&_NaturalOverrideMask;
 [loop]for(uint probe=0;probe<16;probe++){uint recorded=_NaturalOverrides[slot];if(recorded==key)return 0;if(recorded==0)break;slot=(slot+1)&_NaturalOverrideMask;}
 if(PlanetHeight(up)<_PlanetOcean+.2)return 0;
 float2 gx=float2(dot(dx,right)/type.y,dot(dx,forward)/type.z),gy=float2(dot(dy,right)/type.y,dot(dy,forward)/type.z);
 float4 colour=_ResourceNodeTextures.SampleGrad(sampler_ResourceNodeTextures,float3(uv,type.x),gx,gy);
 float3 bump=_ResourceNodeNormals.SampleGrad(sampler_ResourceNodeNormals,float3(uv,type.x),gx,gy).xyz;float2 relief=(bump.xy-_ResourceNodeNormalNeutral[(uint)type.x].xy)*2/max(.25,bump.z*2-1);
 float3 perturb=right*relief.x+forward*relief.y;normal=normalize(normal+(perturb-normal*dot(normal,perturb))*colour.a);return colour;
}
float4 PlanetResourceDecal(float3 direction,float h,inout float3 normal){
 float4 recorded=PlanetRecordedResourceDecal(direction,h,normal);if(recorded.a>0)return recorded;return PlanetNaturalResourceDecal(direction,h,normal);
}
