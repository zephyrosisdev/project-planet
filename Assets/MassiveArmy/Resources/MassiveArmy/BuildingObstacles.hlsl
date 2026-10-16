#ifndef BUILDING_OBSTACLES
#define BUILDING_OBSTACLES
// Only compiled into base-building variants. The army solver and terrain grid stay unchanged.
StructuredBuffer<uint> _StructureCells;
uint _StructureRes;
static uint traversalTeam=0;
void SetTraversalTeam(uint team){traversalTeam=team;}
uint TraversalMask(){return traversalTeam<2?(1u<<traversalTeam):3u;}
bool BuildingPointClear(float2 p,uint movement) {
    if(_StructureRes==0||movement==3)return true;
    uint2 c=(uint2)(WorldUV(p,_WorldSize)*_StructureRes);
    return (_StructureCells[c.x+c.y*_StructureRes]&TraversalMask())==0;
}
bool BuildingRayClear(float2 a,float2 b) {
    b=a+WorldDelta(a,b,_WorldSize);
    float2 p=(a/_WorldSize+.5)*_StructureRes,q=(b/_WorldSize+.5)*_StructureRes,d=q-p;
    int2 c=(int2)floor(p),end=(int2)floor(q),step=int2(d.x>=0?1:-1,d.y>=0?1:-1);
    if(abs(c.x-end.x)+abs(c.y-end.y)>384)return false;
    float2 delta=1/max(abs(d),1e-8),next=float2(d.x>=0?c.x+1-p.x:p.x-c.x,d.y>=0?c.y+1-p.y:p.y-c.y)*delta;
    [loop]for(uint k=0;k<388;k++) {
        int2 wrapped=WrapCell(c,_StructureRes);
        if((_StructureCells[wrapped.x+wrapped.y*_StructureRes]&TraversalMask())!=0)return false;
        if(all(c==end))return true;
        if(min(next.x,next.y)>=1-1e-6){int2 last=WrapCell(end,_StructureRes);return (_StructureCells[last.x+last.y*_StructureRes]&TraversalMask())==0;}
        if(abs(next.x-next.y)<1e-6) {
            int2 x=WrapCell(c+int2(step.x,0),_StructureRes),y=WrapCell(c+int2(0,step.y),_StructureRes);
            if(((_StructureCells[x.x+x.y*_StructureRes]|_StructureCells[y.x+y.y*_StructureRes])&TraversalMask())!=0)return false;
            c+=step;next+=delta;
        }else if(next.x<next.y){c.x+=step.x;next.x+=delta.x;}else{c.y+=step.y;next.y+=delta.y;}
    }return false;
}
bool ClearBuildingSegment(float2 a,float2 b,uint movement) {
    if(_StructureRes==0||movement==3)return true;
    float r=movement==0?.4:2.2;
    [loop]for(uint ray=0;ray<5;ray++){
        uint corner=ray-1;float2 offset=ray==0?0:float2((corner&1)!=0?r:-r,(corner&2)!=0?r:-r);
        if(!BuildingRayClear(a+offset,b+offset))return false;
    }return true;
}
#endif
