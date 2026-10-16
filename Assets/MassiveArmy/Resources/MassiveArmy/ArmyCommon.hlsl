#include "WorldTopology.hlsl"
#ifndef MASSIVE_ARMY_COMMON
#define MASSIVE_ARMY_COMMON
#define ALIVE 1u
#define SELECTED 2u
#define STOPPED 4u
#define ATTACK_MOVE 8u
#define FORMATION 16u
#define DIRECT_ROUTE 32u
#define ARRIVED 64u
#define DETOUR_ROUTE 128u
#define ROUTE_TRACKED 256u
#define FORMATION_ENTRY 512u
#define PASS_RIGHT 1024u
#define PASS_LEFT 2048u
#define STRUCTURE_ORDER 65536u
#define SWARM_UNIT 131072u
#define ZOMBIE_UNIT 262144u
#define ZOMBIE_WANDERING 524288u
#define ZOMBIE_TEAM 2u
#define STRUCTURE_TARGET 0x80000000u
#define INVALID 0xffffffffu
#define GROUP_COUNT 18
#define COARSE_RES 32
#define LOCAL_RES 64
#define INF 1e20
struct UnitState {
    float2 position, previous, velocity, destination;
    float facing, turret, cooldown;
    uint health, packed, flags, target, group;
};
struct UnitArchetype {
    float speed, attackRange, acquisitionRange, cooldown;
    uint health, damage, movement, weapon;
    float turnSpeed, radius, projectileSpeed, aimTolerance;
    uint model, sprite;
    float nearDistance, farDistance;
};
struct UnitTraits {
    float vision,detection,signature,stealth,armour,splash,altitude,fuel;
    uint targetMask,role;float ammunition,supply;float4 scale;
};
struct UnitOperations {float fuel,ammo,timer;uint state;float2 home,mission;uint homeBuilding,carrier,payload,capacity;};
struct Structure {float2 position;float radius,health,armour;uint team;float range,damage;uint flags;float vision,detection,shield;uint level,targetMask;float reload,repair;};
struct GroupOrder {
    float2 destination, localOrigin;
    uint sequence, movement, order, team;
    float2 right; float spacing, width;
    uint count, columns; float arrivalRadius; uint padding;
};
struct ProjectileState {
    float3 position; float lifetime;
    float3 velocity; uint target;
    uint damage, team, alive, owner;
};
uint ArmyHash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); }
float ArmyRandom(uint x) { return (ArmyHash(x) & 0xffffffu) / 16777216.0; }
uint Team(UnitState u) { return u.packed & 255u; }
uint Type(UnitState u) { return (u.packed >> 8) & 255u; }
float2 SafeNormal(float2 x) { return x * rsqrt(max(dot(x,x), 1e-8)); }
float2 FormationSlot(uint rank,GroupOrder g) {
    uint columns=max(1u,g.columns);
    uint row=rank/columns, column=rank%columns;
    uint rowCount=min(columns,g.count-row*columns);
    float2 forward=float2(-g.right.y,g.right.x);
    return g.destination+g.right*((float)column-((float)rowCount-1)*.5)*g.spacing-forward*row*g.spacing;
}
float Turn(float current, float goal, float amount) {
    float d = atan2(sin(goal-current), cos(goal-current));
    return current + clamp(d, -amount, amount);
}
#endif
