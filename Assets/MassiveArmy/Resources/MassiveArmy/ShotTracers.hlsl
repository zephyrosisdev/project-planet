#ifndef MASSIVE_ARMY_SHOT_TRACERS
#define MASSIVE_ARMY_SHOT_TRACERS
// Direct-damage weapons use the existing bounded shell pool for cosmetic gunfire.
// INVALID owner distinguishes tracers from shells that resolve gameplay damage.
void EmitShotTracer(float3 origin,float3 destination,uint team,uint seed,float speed) {
    if(_UseVfx==0 || _ProjectileCapacity==0) return;
    destination=NearestWorld(destination,origin,_WorldSize);float3 delta=destination-origin;float travel=length(delta);
    if(travel<.01) return;
    [unroll] for(uint attempt=0;attempt<4;attempt++) {
        uint slot=ArmyHash(seed+attempt*7919u)%_ProjectileCapacity,previous;
        InterlockedCompareExchange(_Projectiles[slot].alive,0,1,previous);
        if(previous!=0) continue;
        ProjectileState p=(ProjectileState)0;
        p.position=origin;p.velocity=delta/travel*max(1,speed);
        p.lifetime=travel/max(1,speed);p.target=INVALID;p.owner=INVALID;
        p.team=team;p.alive=1;_Projectiles[slot]=p;return;
    }
}
#endif
