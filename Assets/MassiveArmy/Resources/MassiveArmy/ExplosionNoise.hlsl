#ifndef EXPLOSION_NOISE
#define EXPLOSION_NOISE
#ifdef ZOMBIES
RWStructuredBuffer<float4> _NoiseEvents;
RWStructuredBuffer<uint> _NoiseCount;
// Acoustic reach grows with blast energy (damage) and explosive volume (splash).
// Strategic impacts dominate small shells; melee and ordinary bullets emit none.
float ExplosionHearingRadius(float damage,float blast){return min(_WorldSize*.49,40+16*sqrt(max(0,damage))+12*max(0,blast));}
void EmitExplosionNoise(float2 position,float damage,float blast){
 uint slot;
 // Strategic projectiles have a separate bounded lane, independent of shell load.
 if(blast>=100){InterlockedAdd(_NoiseCount[1],1,slot);if(slot<32)_NoiseEvents[992+slot]=float4(WrapPosition(position,_WorldSize),ExplosionHearingRadius(damage,blast),36);}
 else {InterlockedAdd(_NoiseCount[0],1,slot);if(slot<992)_NoiseEvents[slot]=float4(WrapPosition(position,_WorldSize),ExplosionHearingRadius(damage,blast),24);}
}
#else
void EmitExplosionNoise(float2 position,float damage,float blast){}
#endif
#endif
