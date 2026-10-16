#ifndef MASSIVE_PLANET_CBT_TOPOLOGY
#define MASSIVE_PLANET_CBT_TOPOLOGY
int _CbtGrid;
// Twelve compatible right triangles over the six cube faces. Longest edge is A-C.
void CbtDecode(uint root,uint node,out int2 a,out int2 b,out int2 c) {
    if((root&1u)==0){a=int2(0,0);b=int2(_CbtGrid,0);c=int2(_CbtGrid,_CbtGrid);}
    else {a=int2(_CbtGrid,_CbtGrid);b=int2(0,_CbtGrid);c=int2(0,0);}
    int depth=firstbithigh(node);
    for(int bit=depth-1;bit>=0;bit--){int2 midpoint=(a+c)/2,oldB=b;
        if(((node>>bit)&1u)==0){c=a;a=oldB;}else {a=c;c=oldB;}b=midpoint;}
}
int3 CbtCubeVertex(uint root,int2 p){uint f=root/2;return (int3)(PlanetN[f]*_CbtGrid+PlanetU[f]*(p.x*2-_CbtGrid)+PlanetV[f]*(p.y*2-_CbtGrid));}
float3 CbtDirection(uint root,int2 p){return normalize((float3)CbtCubeVertex(root,p));}
float3 CbtDirectionFloat(uint root,float2 p){uint f=root/2;return normalize(PlanetN[f]*_CbtGrid+PlanetU[f]*(p.x*2-_CbtGrid)+PlanetV[f]*(p.y*2-_CbtGrid));}
uint CbtVertexIndex(uint root,int2 p){int3 v=CbtCubeVertex(root,p);uint f=abs(v.x)==_CbtGrid?(v.x>=0?0:1):abs(v.y)==_CbtGrid?(v.y>=0?2:3):(v.z>=0?4:5);
    int x=((int)dot((float3)v,PlanetU[f])+_CbtGrid)/2,y=((int)dot((float3)v,PlanetV[f])+_CbtGrid)/2;
    return f*(_CbtGrid+1)*(_CbtGrid+1)+y*(_CbtGrid+1)+x;
}
#endif
