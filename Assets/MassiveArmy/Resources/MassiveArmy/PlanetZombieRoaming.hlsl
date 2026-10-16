// Shared by detailed and staggered coarse movement: a persistent home range.
StructuredBuffer<float2> _RoamOriginsRead;
float4 _StartingRefuges[2];uint _StartingRefugeCount,_StartingRefugeActive;
bool StartingRefugeClear(float3 d){if(_StartingRefugeActive==0)return true;for(uint i=0;i<_StartingRefugeCount;i++)if(length(d-_StartingRefuges[i].xyz)*_PlanetRadius<_StartingRefuges[i].w)return false;return true;}
float3 RoamDestination(uint id,uint tick){float3 home=PlanetDecode(_RoamOriginsRead[id]);uint seed=ArmyHash(id^(tick/32)*7919u);float angle=ArmyRandom(seed)*6.2831853;float3 east=normalize(cross(abs(home.y)>.95?float3(1,0,0):float3(0,1,0),home));return normalize(home+(east*cos(angle)+cross(home,east)*sin(angle))*(12+sqrt(ArmyRandom(seed+1))*63)/_PlanetRadius);}
