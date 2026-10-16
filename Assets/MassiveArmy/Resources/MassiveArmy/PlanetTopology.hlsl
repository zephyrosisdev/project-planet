#ifndef MASSIVE_PLANET_TOPOLOGY
#define MASSIVE_PLANET_TOPOLOGY
static const float3 PlanetN[6]={float3(1,0,0),float3(-1,0,0),float3(0,1,0),float3(0,-1,0),float3(0,0,1),float3(0,0,-1)};
static const float3 PlanetU[6]={float3(0,0,-1),float3(0,0,1),float3(1,0,0),float3(1,0,0),float3(1,0,0),float3(-1,0,0)};
static const float3 PlanetV[6]={float3(0,1,0),float3(0,1,0),float3(0,0,-1),float3(0,0,1),float3(0,1,0),float3(0,1,0)};
static const uint PlanetFaces[24]={4,5,3,2,5,4,3,2,1,0,4,5,1,0,5,4,1,0,3,2,0,1,3,2};
static const uint PlanetEdges[24]={1,0,1,1,1,0,0,0,3,3,3,3,2,2,2,2,1,0,3,2,1,0,2,3};
static const uint PlanetReverse[24]={0,0,1,0,0,0,0,1,1,0,0,1,0,1,1,0,0,0,0,0,0,0,1,1};
float3 PlanetDirection(uint face,float2 uv){return normalize(PlanetN[face]+PlanetU[face]*uv.x+PlanetV[face]*uv.y);}
float2 PlanetEncode(float3 d){float3 a=abs(d);uint f=a.x>=a.y&&a.x>=a.z?(d.x>=0?0:1):a.y>=a.z?(d.y>=0?2:3):(d.z>=0?4:5);float2 uv=float2(dot(d,PlanetU[f]),dot(d,PlanetV[f]))/dot(d,PlanetN[f]);return float2(f+clamp((uv.x+1)*.5,0,1-1e-6),saturate((uv.y+1)*.5));}
float3 PlanetDecode(float2 p){uint f=min((uint)max(0,floor(p.x)),5u);return PlanetDirection(f,float2((p.x-f)*2-1,p.y*2-1));}
uint PlanetIndex(int2 c,uint n){return (c.x/n)*n*n+c.y*n+c.x%n;}
int2 PlanetCell(uint index,uint n){uint f=index/(n*n),i=index%(n*n);return int2(f*n+i%n,i/n);}
int2 PlanetPositionCell(float2 p,uint n){uint f=min((uint)max(0,floor(p.x)),5u);int2 xy=clamp((int2)(float2(p.x-f,p.y)*n),0,(int)n-1);return int2(f*n+xy.x,xy.y);}
float3 PlanetCellDirection(int2 c,uint n){uint f=c.x/n;return PlanetDirection(f,(float2(c.x%n,c.y)+.5)*2/n-1);}
int2 PlanetNeighbour(int2 c,int2 step,uint n){int f=c.x/n,x=c.x%n+step.x,y=c.y+step.y;if(x>=0&&x<(int)n&&y>=0&&y<(int)n)return int2(f*n+x,y);uint e=x<0?0:x>=(int)n?1:y<0?2:3,k=f*4+e;int t=e<2?c.y:c.x%n;if(PlanetReverse[k]!=0)t=n-1-t;uint edge=PlanetEdges[k];return int2(PlanetFaces[k]*n+(edge<2?(edge==0?0:n-1):t),edge>=2?(edge==2?0:n-1):t);}
float PlanetArc(float3 a,float3 b,float radius){return atan2(length(cross(a,b)),dot(a,b))*radius;}
#endif
