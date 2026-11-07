#include "PlanetTerrainNoise.hlsl"
// Reuses TerrainSurfaceLibrary's arrays and height competition. All projections
// use metre-space body coordinates and explicit shared derivatives on every face.
UNITY_DECLARE_TEX2DARRAY(_SurfaceAlbedo);
UNITY_DECLARE_TEX2DARRAY(_SurfaceData);
float4 _LayerParameters[9];float _HeightBlendWidth,_HeightBlendStrength,_NormalStrength,_MacroVariation;
float4 _BiomeBlend,_BiomeDetail,_TerrainDistanceTuning,_TerrainTextureScale;float _BiomeAltitudeCooling;float3 _PlanetTextureCamera;float4 _TerrainTexturePhases[81];
float3 TerrainBandPosition(float3 relative,int band,int layer){return relative/(max(.1,_LayerParameters[layer].x)*max(.01,_TerrainTextureScale.x)*pow(max(1.5,_TerrainTextureScale.y),band))+_TerrainTexturePhases[band*9+layer].xyz;}

struct PlanetLayer {float height,roughness;float3 perturb;};
PlanetLayer PlanetLayerData(float3 p,float3 n,float3 dx,float3 dy,int layer){
 float tile=max(.1,_LayerParameters[layer].x)*max(.01,_TerrainTextureScale.x);dx/=tile;dy/=tile;float3 signs=sign(n),w=pow(abs(n),4);w/=max(dot(w,1),.00001);
 float4 x=_SurfaceData.SampleGrad(sampler_SurfaceData,float3(-p.z*signs.x,p.y,layer),float2(-dx.z*signs.x,dx.y),float2(-dy.z*signs.x,dy.y));
 float4 y=_SurfaceData.SampleGrad(sampler_SurfaceData,float3(p.x,-p.z*signs.y,layer),float2(dx.x,-dx.z*signs.y),float2(dy.x,-dy.z*signs.y));
 float4 z=_SurfaceData.SampleGrad(sampler_SurfaceData,float3(p.x*signs.z,p.y,layer),float2(dx.x*signs.z,dx.y),float2(dy.x*signs.z,dy.y));
 float2 a=x.rg*2-1,b=y.rg*2-1,c=z.rg*2-1;a/=max(.25,sqrt(saturate(1-dot(a,a))));b/=max(.25,sqrt(saturate(1-dot(b,b))));c/=max(.25,sqrt(saturate(1-dot(c,c))));
 PlanetLayer result;result.height=dot(float3(x.b,y.b,z.b),w);result.roughness=dot(float3(x.a,y.a,z.a),w);
 result.perturb=(float3(0,a.y,-a.x*signs.x)*w.x+float3(b.x,0,-b.y*signs.y)*w.y+float3(c.x*signs.z,c.y,0)*w.z)*_LayerParameters[layer].y;return result;
}
float3 PlanetLayerColour(float3 p,float3 n,float3 dx,float3 dy,int layer){
 float tile=max(.1,_LayerParameters[layer].x)*max(.01,_TerrainTextureScale.x);dx/=tile;dy/=tile;float3 s=sign(n),w=pow(abs(n),4);w/=max(dot(w,1),.00001);
 return _SurfaceAlbedo.SampleGrad(sampler_SurfaceAlbedo,float3(-p.z*s.x,p.y,layer),float2(-dx.z*s.x,dx.y),float2(-dy.z*s.x,dy.y)).rgb*w.x+
 _SurfaceAlbedo.SampleGrad(sampler_SurfaceAlbedo,float3(p.x,-p.z*s.y,layer),float2(dx.x,-dx.z*s.y),float2(dy.x,-dy.z*s.y)).rgb*w.y+
 _SurfaceAlbedo.SampleGrad(sampler_SurfaceAlbedo,float3(p.x*s.z,p.y,layer),float2(dx.x*s.z,dx.y),float2(dy.x*s.z,dy.y)).rgb*w.z;
}
float3 EvaluatePlanetTerrain(float3 d,float h,float3 geometric,float3 relativeWorld,out float3 normal,out float roughness,out float textureHeight){
 // Body-local metre coordinates keep the original controls stable across seams and rebases.
 float3 body=d*(_PlanetRadius+h),seed=float3(_PlanetSeed,_PlanetSeed*.37,-_PlanetSeed*.61);
 float3 boundary=_TerrainNoiseAnchored>.5?relativeWorld/max(1,_BiomeBlend.z)+_TerrainNoisePhase[0].xyz:body/max(1,_BiomeBlend.z)+seed;
 float boundaryFootprint=max(length(ddx(boundary)),length(ddy(boundary))),boundaryFade=1-smoothstep(.35,1.5,boundaryFootprint);
 float3 warp=0;[branch]if(boundaryFade>0)warp=(float3(TerrainMaterialNoise(boundary,0,1),TerrainMaterialNoise(boundary+float3(37,81,19),0,1),TerrainMaterialNoise(boundary+float3(71,13,43),0,1))*2-1)*boundaryFade;
 float3 warped=normalize(body+warp*_BiomeBlend.y);
 float2 climate=PlanetClimate(warped);climate.x=saturate(climate.x-max(0,h-_PlanetOcean)/max(1000,abs(_PlanetHeight)*1.7)*_BiomeAltitudeCooling*.65);
 float width=max(.000001,min(.12,_BiomeBlend.x*5/max(_PlanetRadius,500)));
 width=max(width,max(fwidth(climate.x),fwidth(climate.y))*.5);
 float snow=1-smoothstep(.20-width,.20+width,climate.x),desert=1-smoothstep(.38-width,.38+width,climate.y),forest=smoothstep(.60-width,.60+width,climate.y)*(1-snow)*(1-desert);
 float shore=1-smoothstep(.5,3,h-_PlanetOcean),rock=smoothstep(_TerrainDistanceTuning.z,_TerrainDistanceTuning.w,1-saturate(dot(geometric,d)));
 float weights[9]={max(.02,1-forest)*(1-desert)*(1-snow),max(shore,desert*(1-snow)),(1-snow)*(1-shore)*(1-forest)*(.08+.3*(1-climate.y)),forest,rock*3,snow,shore*(1-desert)*.3,(1-snow)*(.06+(1-climate.x)*.12),0};
 float3 detail=(_TerrainNoiseAnchored>.5?relativeWorld/max(1,_BiomeDetail.x)+_TerrainNoisePhase[1].xyz:body/max(1,_BiomeDetail.x)+seed)+warp*1.3;
 float fade=1-smoothstep(.35,1.5,max(length(ddx(detail)),length(ddy(detail))));
 float3 patch=0;[branch]if(fade>0)patch.xy=(float2(TerrainMaterialNoise(detail,1,1),TerrainMaterialNoise(detail+float3(41,19,73),1,1))*2-1)*fade;
 float coarseFade=1-smoothstep(.35,1.5,boundaryFootprint*2);[branch]if(coarseFade>0)patch.z=(TerrainMaterialNoise(boundary*2+31,0,2)*2-1)*coarseFade;
 [unroll]for(int material=0;material<9;material++){
  float3 axis=normalize(float3(PlanetClimateHash(int3(material,7,1)),PlanetClimateHash(int3(material,23,2)),PlanetClimateHash(int3(material,53,3)))*2-1);
  weights[material]*=exp2(dot(patch,axis)*_BiomeBlend.w*3);
 }
 int layers[3]={0,0,0};float chosen[3]={0,0,0};
 [unroll]for(int i=0;i<9;i++){float w=weights[i];if(w>chosen[0]){chosen[2]=chosen[1];layers[2]=layers[1];chosen[1]=chosen[0];layers[1]=layers[0];chosen[0]=w;layers[0]=i;}else if(w>chosen[1]){chosen[2]=chosen[1];layers[2]=layers[1];chosen[1]=w;layers[1]=i;}else if(w>chosen[2]){chosen[2]=w;layers[2]=i;}}
 float bandValue=_TerrainDistanceTuning.x>.5?clamp(log2(max(1,length(relativeWorld)/max(1,_TerrainDistanceTuning.y)))/log2(max(1.5,_TerrainTextureScale.y)),0,7.99):0;
 int band=(int)floor(bandValue);float bandBlend=smoothstep(.25,.85,frac(bandValue));float bandScale=pow(max(1.5,_TerrainTextureScale.y),band);
 float sum=max(chosen[0]+chosen[1]+chosen[2],.00001);float3 p=relativeWorld,dx=ddx(p)/bandScale,dy=ddy(p)/bandScale;PlanetLayer data[3];float raised[3];float highest=-1;
 [unroll]for(int j=0;j<3;j++){chosen[j]/=sum;data[j]=PlanetLayerData(TerrainBandPosition(p,band,layers[j]),geometric,dx,dy,layers[j]);raised[j]=data[j].height*chosen[j];highest=max(highest,raised[j]);}
 float refined[3];float total=0;[unroll]for(int k=0;k<3;k++){refined[k]=max(raised[k]-highest+max(_HeightBlendWidth,.01),0)*chosen[k];total+=refined[k];}
 float3 colour=0,perturb=0;roughness=0;textureHeight=0;[unroll]for(int v=0;v<3;v++){float w=lerp(chosen[v],refined[v]/max(total,.00001),_HeightBlendStrength);float3 sampleColour=PlanetLayerColour(TerrainBandPosition(p,band,layers[v]),geometric,dx,dy,layers[v]);
 [branch]if(bandBlend>.001)sampleColour=lerp(sampleColour,PlanetLayerColour(TerrainBandPosition(p,band+1,layers[v]),geometric,dx/max(1.5,_TerrainTextureScale.y),dy/max(1.5,_TerrainTextureScale.y),layers[v]),bandBlend);
 colour+=sampleColour*w;perturb+=data[v].perturb*w;roughness+=data[v].roughness*w;textureHeight+=data[v].height*w;}
 normal=normalize(geometric+(perturb-geometric*dot(perturb,geometric))*_NormalStrength);float3 macro=_TerrainNoiseAnchored>.5?relativeWorld/96+_TerrainNoisePhase[2].xyz:body/96+seed;float macroFade=1-smoothstep(.35,1.5,max(length(ddx(macro)),length(ddy(macro))));if(macroFade>0)colour*=1+(TerrainMaterialNoise(macro,2,1)-.5)*_MacroVariation*2*macroFade;return colour;
}

// Distant bodies reuse the terrain evaluator without road painting.
float3 EvaluatePlanetTerrain(float3 d,float h,float3 geometric,float3 relativeWorld,out float3 normal,out float roughness){float textureHeight;return EvaluatePlanetTerrain(d,h,geometric,relativeWorld,normal,roughness,textureHeight);}
