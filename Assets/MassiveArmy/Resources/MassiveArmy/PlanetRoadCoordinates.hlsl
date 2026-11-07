#ifndef PLANET_ROAD_COORDINATES
#define PLANET_ROAD_COORDINATES
uint _PlanetRoadResolution;float _PlanetRoadAnchored;float4 _PlanetRoadAnchor[6];
float2 RoadSum(float a,float b){precise float s=a+b;precise float v=s-a;precise float error=(a-(s-v))+(b-v);return float2(s,error);}
// Compensated projection preserves cell fractions at large planet radii without FP64.
float2 RoadCoordinate(float numerator,float numeratorLow,float denominator,float denominatorLow){
 precise float2 sum=RoadSum(numerator,denominator);sum.y+=numeratorLow+denominatorLow;
 precise float q=sum.x/denominator;
 precise float remainder=mad(-q,denominator,sum.x)+sum.y-q*denominatorLow;
 precise float high=q*(float)_PlanetRoadResolution;
 precise float low=mad(q,(float)_PlanetRoadResolution,-high)+remainder/(denominator+denominatorLow)*(float)_PlanetRoadResolution;
 return float2(high*.5,low*.5);
}
// Project a small fragment-to-anchor displacement instead of dividing planet-sized
// float coordinates. This does not depend on mad being a fused operation.
float4 RoadAnchoredCoordinate(float3 world,uint face){
 float2 reference=_PlanetPrecisionUv[face].xy;
 precise float3 delta=world-_PlanetPrecisionBase[face].xyz;
 precise float normalDelta=dot(delta,PlanetN[face]);
 precise float denominator=_PlanetRadius*rsqrt(1+dot(reference,reference))+normalDelta;
 precise float2 numerator=float2(dot(delta,PlanetU[face]),dot(delta,PlanetV[face]))-reference*normalDelta;
 precise float2 offset=numerator*((.5*(float)_PlanetRoadResolution)/denominator);
 float4 anchor=_PlanetRoadAnchor[face];
 return float4(anchor.x,anchor.z+offset.x,anchor.y,anchor.w+offset.y);
}
#endif
