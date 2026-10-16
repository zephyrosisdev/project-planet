Texture2DArray<uint> _CombatIndexMap;
TEXTURE2D_ARRAY(_CombatTextures);SAMPLER(sampler_CombatTextures);
TEXTURE2D_ARRAY(_CombatNormals);
struct CombatStamp {float4 positionSize,appearance,timing;};
StructuredBuffer<CombatStamp> _CombatStamps;
StructuredBuffer<uint> _CombatTileIndices;
StructuredBuffer<uint> _CombatAlphaCoverage;
float4 _CombatNormalNeutral[6];
float _CombatIndexResolution,_CombatVisible,_CombatTime;
float2 _CombatLayersVisible;
Texture2D<float4> _CraterAtlas,_CraterAtlasNormal;SAMPLER(sampler_CraterAtlas);
float _CraterAtlasVisible;
float4 CombatDecal(float2 position,out float2 relief)
{
    float2 dx=ddx(position),dy=ddy(position);float4 result=0;relief=0;
    [branch]if(_CombatVisible<.5&&_CraterAtlasVisible<.5)return result;
    uint2 cell=(uint2)(frac(position/_WorldSize+.5)*_CombatIndexResolution);
    // Front-to-back alpha composition: transparent texels never erase an earlier mark.
    [unroll]for(int layer=1;layer>=0;layer--){
        [branch]if(_CombatLayersVisible[layer]<.5||result.a>.995)continue;
        uint start=_CombatIndexMap.Load(int4(cell,layer,0));if(start==0)continue;
        uint count=min(_CombatTileIndices[start],512u);
        [loop]for(uint item=0;item<count&&result.a<.995;item++){
        CombatStamp stamp=_CombatStamps[_CombatTileIndices[start+1+item]];if(stamp.appearance.w<.5)continue;
        float2 delta=position-stamp.positionSize.xz;delta-=floor(delta/_WorldSize+.5)*_WorldSize;
        float2x2 rotation=float2x2(stamp.appearance.y,stamp.appearance.z,-stamp.appearance.z,stamp.appearance.y);
        float2 uv=mul(rotation,delta)/stamp.positionSize.w+.5;if(any(uv<0)||any(uv>1))continue;
        float2 gx=mul(rotation,dx)/stamp.positionSize.w,gy=mul(rotation,dy)/stamp.positionSize.w;
        uint2 block=min((uint2)(uv*32),31u);
        if(max(length(gx),length(gy))<.03125&&(_CombatAlphaCoverage[(uint)stamp.appearance.x*32+block.y]&(1u<<block.x))==0)continue;
        float4 mark=SAMPLE_TEXTURE2D_ARRAY_GRAD(_CombatTextures,sampler_CombatTextures,uv,stamp.appearance.x,gx,gy);
        mark.a*=saturate((stamp.timing.y-(_CombatTime-stamp.timing.x))/min(5,stamp.timing.y));
        float weight=mark.a*(1-result.a);if(weight<.0001)continue;
        result.rgb+=mark.rgb*weight;result.a+=weight;
        float3 n=SAMPLE_TEXTURE2D_ARRAY_GRAD(_CombatNormals,sampler_CombatTextures,uv,stamp.appearance.x,gx,gy).xyz;
        n=float3((n.xy-_CombatNormalNeutral[(uint)stamp.appearance.x].xy)*2,max(.25,n.z*2-1));
        relief+=mul(transpose(rotation),n.xy/max(.25,n.z))*weight*(stamp.appearance.x==0?.25:1);
        }
    }
    // The small permanent atlas provides distant/overflow coverage behind detailed pooled stamps.
    [branch]if(_CraterAtlasVisible>.5&&result.a<.995){float2 uv=frac(position/_WorldSize+.5);
        float4 baked=_CraterAtlas.Sample(sampler_CraterAtlas,uv);float weight=1-result.a;
        result.rgb+=baked.rgb*weight;result.a+=baked.a*weight;
        relief+=(_CraterAtlasNormal.Sample(sampler_CraterAtlas,uv).xy-128.0/255)*4*weight;}
    // Return straight alpha after composing the sorted crater/remains layers.
    if(result.a>0)result.rgb/=result.a;return result;
}
