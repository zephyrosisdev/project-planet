#ifndef SF_BUILDING_TURRET_POSE
#define SF_BUILDING_TURRET_POSE
StructuredBuffer<float4> _StructureAim;
uint _BuildingIndex;
float _HasTurret,_BuildingYaw;
float2 TurretTurn(float2 p,float angle){float s=sin(angle),c=cos(angle);return float2(c*p.x+s*p.y,-s*p.x+c*p.y);}
float TurretAngle(float joint){
    if(_HasTurret<.5||joint<.5)return 0;
    float4 aim=_StructureAim[_BuildingIndex];return aim.w>.5?aim.x-_BuildingYaw:0;
}
#endif
