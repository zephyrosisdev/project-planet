// One spatial lookup per visible terrain pixel; no loop over resource deposits.
TEXTURE2D(_ResourceNodeMap);
TEXTURE2D_ARRAY(_ResourceNodeTextures);SAMPLER(sampler_ResourceNodeTextures);
TEXTURE2D_ARRAY(_ResourceNodeNormals);
float4 _ResourceNodeNormalNeutral[7];
struct ResourceDeposit { float4 positionSize;float4 appearance; };
StructuredBuffer<ResourceDeposit> _ResourceNodeData;
float _ResourceNodeResolution,_ResourceNodesVisible;
float4 ResourceDecal(float2 position,out float2 relief)
{
    relief=0;
    float2 dx=ddx(position),dy=ddy(position);
    [branch]if(_ResourceNodesVisible<.5)return 0;
    uint2 cell=(uint2)(frac(position/_WorldSize+.5)*_ResourceNodeResolution);
    uint index=(uint)round(LOAD_TEXTURE2D(_ResourceNodeMap,cell).r*65535);
    [branch]if(index==0)return 0;
    ResourceDeposit deposit=_ResourceNodeData[index-1];
    [branch]if(deposit.appearance.y<.5)return 0;
    float2 delta=position-deposit.positionSize.xy;
    delta-=floor(delta/_WorldSize+.5)*_WorldSize;
    float2 uv=delta/deposit.positionSize.zw+.5;
    [branch]if(any(uv<0)||any(uv>1))return 0;
    // Explicit gradients keep mip selection valid across the sparse spatial branches.
    float2 gx=dx/deposit.positionSize.zw,gy=dy/deposit.positionSize.zw;
    float4 colour=SAMPLE_TEXTURE2D_ARRAY_GRAD(_ResourceNodeTextures,sampler_ResourceNodeTextures,uv,deposit.appearance.x,gx,gy);
    if(colour.a>.0001){float3 n=SAMPLE_TEXTURE2D_ARRAY_GRAD(_ResourceNodeNormals,sampler_ResourceNodeTextures,uv,deposit.appearance.x,gx,gy).xyz;
        relief=(n.xy-_ResourceNodeNormalNeutral[(uint)deposit.appearance.x].xy)*2/max(.25,n.z*2-1)*colour.a;}
    return colour;
}
