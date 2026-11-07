#ifndef PLANET_LOCAL_PRECISION
#define PLANET_LOCAL_PRECISION
            float3 _PlanetObserverHigh,_PlanetObserverLow;
            float4 _PlanetPrecisionUv[6],_PlanetPrecisionBase[6];
            float3 PlanetRelativePoint(float3 direction,float height){
#ifdef PLANET_GPU_DOUBLE
                double3 unit=PlanetEditUnit((double3)direction);
                return (float3)(unit*((double)_PlanetRadius+(double)height)-(double3)_PlanetObserverHigh-(double3)_PlanetObserverLow);
#else
                // Retain the product residual before cancelling the observer. No
                // addition of a small elevation to a planet-sized float radius.
                precise float3 high=direction*_PlanetRadius;
                precise float3 low=mad(direction,_PlanetRadius,-high);
                return ((high-_PlanetObserverHigh)+(low-_PlanetObserverLow))+direction*height;
#endif
            }
            float3 StableCubeBase(uint face,float2 uv,float2 uvLow){
                #ifdef PLANET_GPU_DOUBLE
                double3 cube=(double3)PlanetN[face]+(double3)PlanetU[face]*((double)uv.x+(double)uvLow.x)+(double3)PlanetV[face]*((double)uv.y+(double)uvLow.y);
                double square=(cube.x*cube.x+cube.y*cube.y+cube.z*cube.z),inverse=(double)rsqrt((float)square);
                inverse=inverse*(1.5-.5*square*inverse*inverse);inverse=inverse*(1.5-.5*square*inverse*inverse);
                return (float3)(cube*inverse*(double)_PlanetRadius-(double3)_PlanetObserverHigh-(double3)_PlanetObserverLow);
                #else
                float2 reference=_PlanetPrecisionUv[face].xy;
                precise float3 q=PlanetN[face]+PlanetU[face]*reference.x+PlanetV[face]*reference.y;
                precise float3 delta=PlanetU[face]*((uv.x-reference.x)+uvLow.x)+PlanetV[face]*((uv.y-reference.y)+uvLow.y);
                float length0=length(q),length1=length(q+delta);
                // Rationalised length difference retains small offsets rather than
                // subtracting two almost identical normalised planet directions.
                precise float change=(2*dot(q,delta)+dot(delta,delta))/(length0+length1);
                precise float3 directionDelta=(delta-q*(change/length0))/length1;
                return _PlanetPrecisionBase[face].xyz+directionDelta*_PlanetRadius;
                #endif
            }
            float3 StableCubeBase(uint face,float2 uv){return StableCubeBase(face,uv,0);}
#endif
