#ifndef ZOMBIE_HEARING_COORDINATES
#define ZOMBIE_HEARING_COORDINATES
// Same identity-as-float convention as the published building field. Two
// 16-bit wrapping coordinates preserve sub-cell precision without a new stride.
float PackNoisePosition(float2 p,float size){uint2 q=(uint2)(WorldUV(p,size)*65535);return asfloat(q.x|(q.y<<16));}
float2 NoisePosition(float packed,float size){uint word=asuint(packed);return (float2(word&65535u,word>>16)/65535.0-.5)*size;}
#endif
