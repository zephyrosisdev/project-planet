#ifndef ARMY_TERRAIN
#define ARMY_TERRAIN
#ifdef BUILDING_GRID
#include "BuildingObstacles.hlsl"
#else
void SetTraversalTeam(uint team){}
#endif
uint TerrainIndex(float2 p) {
    uint2 c=(uint2)(WorldUV(p,_WorldSize)*_NavRes);
    return c.x+c.y*_NavRes;
}
float4 Ground(float2 p) {
    float2 f=WorldUV(p,_WorldSize)*_NavRes-.5;
    int2 a=WrapCell((int2)floor(f),_NavRes),b=WrapCell(a+1,_NavRes);float2 t=frac(f);
    return lerp(lerp(_Terrain.Load(int3(a,0)),_Terrain.Load(int3(b.x,a.y,0)),t.x),lerp(_Terrain.Load(int3(a.x,b.y,0)),_Terrain.Load(int3(b,0)),t.x),t.y);
}
float Cost(float2 p,uint movement) {
#ifdef BUILDING_GRID
    movement+=traversalTeam*5;
#endif
    return _Costs[movement*_NavRes*_NavRes+TerrainIndex(p)];
}
float CellCost(int2 c,uint movement) {
#ifdef BUILDING_GRID
    movement+=traversalTeam*5;
#endif
    c=WrapCell(c,_NavRes);
    return _Costs[movement*_NavRes*_NavRes+c.x+c.y*_NavRes];
}
float2 CellWorld(int2 c) { return ((WrapCell(c,_NavRes)+.5)/_NavRes-.5)*_WorldSize; }
bool SafeTerrainPosition(float2 p,uint movement) {
#ifdef BUILDING_GRID
    if(!BuildingPointClear(p,movement))return false;
    float hull=movement==0?.4:2.2;
    if(!BuildingPointClear(p+float2(hull,hull),movement)||!BuildingPointClear(p-float2(hull,hull),movement)||
        !BuildingPointClear(p+float2(hull,-hull),movement)||!BuildingPointClear(p+float2(-hull,hull),movement))return false;
#endif
    if(Cost(p,movement)>=INF*.5)return false;
    if(movement>=3)return true;
    // Hull clearance at the bank, not just a point at the tank's centre.
    float r=movement==0?.4:2.2;
    return Cost(p+float2(r,r),movement)<INF*.5 && Cost(p+float2(-r,r),movement)<INF*.5 &&
        Cost(p+float2(r,-r),movement)<INF*.5 && Cost(p-float2(r,r),movement)<INF*.5;
}
// Exact grid traversal with diagonal corner checks. Long untested segments never count as clear.
bool ClearTerrainSegment(float2 a,float2 b,uint movement) {
#ifdef BUILDING_GRID
    if(!ClearBuildingSegment(a,b,movement))return false;
#endif
    if(!SafeTerrainPosition(a,movement)||!SafeTerrainPosition(b,movement))return false;
    b=a+WorldDelta(a,b,_WorldSize);
    float2 p=(a/_WorldSize+.5)*_NavRes,q=(b/_WorldSize+.5)*_NavRes,d=q-p;
    int2 c=(int2)floor(p),end=(int2)floor(q);
    if(abs(c.x-end.x)+abs(c.y-end.y)>96)return false;
    int2 step=int2(d.x>=0?1:-1,d.y>=0?1:-1);
    float2 delta=1/max(abs(d),1e-8);
    float2 next=float2(d.x>=0?c.x+1-p.x:p.x-c.x,d.y>=0?c.y+1-p.y:p.y-c.y)*delta;
    [loop] for(uint k=0;k<100;k++) {
        if(CellCost(c,movement)>=INF*.5)return false;
        if(all(c==end))return true;
        // An endpoint on a cell edge must not step beyond the segment on a negative axis.
        if(min(next.x,next.y)>=1-1e-6)return CellCost(end,movement)<INF*.5;
        if(abs(next.x-next.y)<1e-6) {
            if(CellCost(c+int2(step.x,0),movement)>=INF*.5 || CellCost(c+int2(0,step.y),movement)>=INF*.5)return false;
            c+=step;next+=delta;
        } else if(next.x<next.y) { c.x+=step.x;next.x+=delta.x; }
        else { c.y+=step.y;next.y+=delta.y; }
    }
    return false;
}
float2 TraversableSlot(float2 p,uint movement) {
    p=WrapPosition(p,_WorldSize);
    if(SafeTerrainPosition(p,movement))return p;
    int2 c=(int2)floor((p/_WorldSize+.5)*_NavRes);
    if(CellCost(c,movement)<INF*.5 && SafeTerrainPosition(CellWorld(c),movement))return CellWorld(c);
    [loop] for(int ring=1;ring<=12;ring++) {
        float best=INF;float2 found=p;
        [loop] for(int edge=-ring;edge<=ring;edge++) {
            int2 candidates[4]={c+int2(edge,-ring),c+int2(edge,ring),c+int2(-ring,edge),c+int2(ring,edge)};
            [unroll] for(int j=0;j<4;j++)if(CellCost(candidates[j],movement)<INF*.5) {
#ifdef BUILDING_GRID
                if(!SafeTerrainPosition(CellWorld(candidates[j]),movement))continue;
#endif
                float2 q=CellWorld(candidates[j]);float2 delta=WorldDelta(p,q,_WorldSize);float d=dot(delta,delta);if(d<best) { best=d;found=q; }
            }
        }
        if(best<INF*.5)return found;
    }
    return p;
}
bool ClearTerrainSegmentForTeam(float2 a,float2 b,uint movement,uint team) {
#ifdef BUILDING_GRID
    uint previous=traversalTeam;SetTraversalTeam(team);
    bool clear=ClearTerrainSegment(a,b,movement);SetTraversalTeam(previous);return clear;
#else
    return ClearTerrainSegment(a,b,movement);
#endif
}
#endif
