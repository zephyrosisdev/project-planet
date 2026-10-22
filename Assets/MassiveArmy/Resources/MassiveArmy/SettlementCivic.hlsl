#ifndef SETTLEMENT_CIVIC_INCLUDED
#define SETTLEMENT_CIVIC_INCLUDED
// 48 bytes. Health, damage, patrol progress and the live/death pose are GPU authoritative.
struct CivicAgent {uint health,damage,generation,flags;float cooldown;uint patrolIndex,lastHitTick,pad;float4 body;};
#endif
