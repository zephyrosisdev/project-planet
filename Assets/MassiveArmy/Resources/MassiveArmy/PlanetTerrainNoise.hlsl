#ifndef PLANET_TERRAIN_NOISE_ANCHOR
#define PLANET_TERRAIN_NOISE_ANCHOR
float4 _TerrainNoiseHigh[3],_TerrainNoiseLow[3],_TerrainNoisePhase[3];float _TerrainNoiseAnchored;
// Integer lattice identity is independent of the floating origin. Only the
// small fractional offset enters float arithmetic, including on DX12.
float TerrainAnchoredNoise(float3 local,uint band,int multiple){
 int3 baseCell=((int3)_TerrainNoiseHigh[band].xyz<<16)+(int3)_TerrainNoiseLow[band].xyz;
 int3 cell=(int3)floor(local)+baseCell*multiple;
 return PlanetNoise3Cell(cell,frac(local));
}
float TerrainMaterialNoise(float3 local,uint band,int multiple){
 return _TerrainNoiseAnchored>.5?TerrainAnchoredNoise(local,band,multiple):PlanetNoise3(local);
}
#endif
