#ifndef MASSIVE_ARMY_COMBAT_EVENTS
#define MASSIVE_ARMY_COMBAT_EVENTS
struct CombatVisualEvent {float4 positionSize;uint kind,team,seed,rotation;};
RWStructuredBuffer<CombatVisualEvent> _CombatEvents;
StructuredBuffer<uint> _VisualFog;
float4x4 _VisualVP;float3 _VisualFocus;float _VisualCaptureRadius;uint _VisualEnabled,_VisualUseFog;
// Keep terrain/type work out of the army-wide damage kernel. Finalize at most 128 records later.
void EmitCombatDeath(UnitState u,uint seed)
{
    if(_VisualEnabled==0)return;float2 delta=WorldDelta(_VisualFocus.xz,u.position,_WorldSize);
    if(dot(delta,delta)>_VisualCaptureRadius*_VisualCaptureRadius)return;
    if(_VisualUseFog!=0){uint2 c=(uint2)(WorldUV(u.position,_WorldSize)*128);if(_VisualFog[c.x+c.y*128]!=2)return;}
    uint slot;InterlockedAdd(_CombatEvents[0].team,1,slot);if(slot>=128)return;
    CombatVisualEvent e;e.positionSize=float4(u.position.x,0,u.position.y,(float)Type(u));
    e.kind=(u.flags&ZOMBIE_UNIT)!=0?2:8;e.team=Team(u);e.seed=seed;e.rotation=asuint(u.facing);_CombatEvents[129+slot]=e;
}
// 128 impacts, 128 deaths and all 32 strategic missile slots. No unit-state readback.
void EmitCombatVisual(float3 position,float size,uint kind,uint team,uint seed,float rotation)
{
    if(_VisualEnabled==0&&kind!=1)return;
    if(kind!=1){
        float3 image=NearestWorld(position,_VisualFocus,_WorldSize);float4 clip=mul(_VisualVP,float4(image,1));
        if(clip.w<=0||any(abs(clip.xy)>clip.w*1.08)||clip.z<0||clip.z>clip.w)return;
        if(_VisualUseFog!=0){uint2 c=(uint2)(WorldUV(position.xz,_WorldSize)*128);if(_VisualFog[c.x+c.y*128]!=2)return;}
    }
    uint slot;
    if(kind==1){InterlockedAdd(_CombatEvents[0].seed,1,slot);if(slot>=256)return;slot+=257;}
    else if(kind>=2){InterlockedAdd(_CombatEvents[0].team,1,slot);if(slot>=128)return;slot+=129;}
    else {InterlockedAdd(_CombatEvents[0].kind,1,slot);if(slot>=128)return;slot+=1;}
    CombatVisualEvent e;e.positionSize=float4(WrapPosition(position.xz,_WorldSize).x,position.y,WrapPosition(position.xz,_WorldSize).y,size);
    e.kind=kind;e.team=team;e.seed=seed;e.rotation=asuint(rotation);_CombatEvents[slot]=e;
}
#endif
