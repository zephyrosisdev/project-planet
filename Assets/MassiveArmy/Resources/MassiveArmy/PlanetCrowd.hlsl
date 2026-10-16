// Spherical adaptation of Crowd.compute's unilateral density constraint.
// Storage is bounded by the regional navigation grid, never the planet's area.
RWStructuredBuffer<uint> _CrowdMass,_CrowdDispatch,_CrowdWork;
StructuredBuffer<uint> _CrowdDispatchRead,_CrowdWorkRead;uint _CrowdTiled;
RWStructuredBuffer<float4> _CrowdGrid;
StructuredBuffer<float4> _CrowdRead;
StructuredBuffer<float> _PressureRead,_PressureOther;
RWStructuredBuffer<float> _PressureWrite;
int2 CrowdOffset(int2 c,int x,int y){return PlanetNeighbour(PlanetNeighbour(c,int2(x,0),_Res),int2(0,y),_Res);}
float CrowdArea(int2 c){float2 uv=(float2(c.x%_Res,c.y)+.5)*2/_Res-1;return 4*_PlanetRadius*_PlanetRadius/(_Res*_Res)*pow(1+dot(uv,uv),-1.5);}
float3 CrowdVelocity(UnitState u){float3 d=PlanetDecode(u.position);uint face=min((uint)floor(u.position.x),5u);float3 right=normalize(PlanetU[face]-d*dot(PlanetU[face],d));return right*u.velocity.x+cross(right,d)*u.velocity.y;}
float3 CrowdNormal(float3 v){return v*rsqrt(max(dot(v,v),1e-12));}
void ClearCrowdCell(uint i){if(i<6*_Res*_Res)_CrowdMass[i]=0;if(i<4)_CrowdDispatch[i]=i==1||i==2?1:0;uint tiles=6*((_Res+15)/16)*((_Res+15)/16);if(i<tiles)_CrowdWork[i]=0;}
[numthreads(128,1,1)]void ClearCrowd(uint3 id:SV_DispatchThreadID){ClearCrowdCell(id.x);}
void SplatCrowdUnit(UnitState u){if(_Types[Type(u)].movement>=3)return;
 // Manual Stop occupies space. Formation parking exceptions remain a separate migration task.
 float3 d=PlanetDecode(u.position);float2 predicted=PlanetEncode(normalize(d+CrowdVelocity(u)*_Dt/_PlanetRadius));
 uint face=min((uint)floor(predicted.x),5u);float2 p=float2(predicted.x-face,predicted.y)*_Res-.5;int2 base=(int2)floor(p);float2 f=frac(p);
 int2 anchor=clamp(base,0,(int)_Res-1);anchor.x+=face*_Res;
 float footprint=(u.flags&ZOMBIE_UNIT)!=0?pow(max(.8,_Types[Type(u)].radius*2)/5.2,2):1;
 [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++){int2 c=CrowdOffset(anchor,base.x+x-clamp(base.x,0,(int)_Res-1),base.y+y-clamp(base.y,0,(int)_Res-1));float w=(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y);InterlockedAdd(_CrowdMass[PlanetIndex(c,_Res)],(uint)round(w*footprint*1024));}
}
[numthreads(128,1,1)]void SplatCrowd(uint3 id:SV_DispatchThreadID){uint i=ActiveId(id.x);if(i>=_Count)return;UnitState u=_SnapshotRead[i];if((u.flags&ALIVE)!=0)SplatCrowdUnit(u);}
// A 16-cell tile plus its eight neighbours contains the complete eight-Jacobi
// stencil and the final gradient stencil. No pressure iteration is dropped.
void MarkCrowdWork(int2 c){
 if(_CrowdTiled==0){InterlockedMax(_CrowdDispatch[0],(6*_Res*_Res+127)/128);return;}
 uint n=_Res/16,total=6*n*n;int2 tile=int2((c.x/_Res)*n+(c.x%_Res)/16,c.y/16);
 [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){uint index=PlanetIndex(PlanetNeighbour(PlanetNeighbour(tile,int2(x,0),n),int2(0,y),n),n),previous;InterlockedExchange(_CrowdWork[index],1,previous);
  if(previous==0){uint rank;InterlockedAdd(_CrowdDispatch[3],1,rank);_CrowdWork[total+rank]=index;InterlockedMax(_CrowdDispatch[0],(rank+1)*2);}
 }
}
uint CrowdWorkIndex(uint i){if(_CrowdTiled==0)return i;uint rank=i/256;if(rank>=_CrowdDispatchRead[3])return INVALID;uint n=_Res/16;int2 tile=PlanetCell(_CrowdWorkRead[6*n*n+rank],n);int2 c=int2((tile.x/n)*_Res+(tile.x%n)*16+i%16,tile.y*16+(i%256)/16);return PlanetIndex(c,_Res);}
[numthreads(128,1,1)]void BuildCrowd(uint3 id:SV_DispatchThreadID){if(id.x>=6*_Res*_Res)return;int2 c=PlanetCell(id.x,_Res);float mass=0;
 [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++)mass+=(float)_CrowdMass[PlanetIndex(CrowdOffset(c,x,y),_Res)]/1024*(x==0?2:1)*(y==0?2:1)/16;
 float4 previous=_CrowdGrid[id.x];float density=mass*5.2*5.2/CrowdArea(c);_CrowdGrid[id.x]=float4(density,previous.yzw);
 if(density>.9||_PressureRead[id.x]>0||_PressureOther[id.x]>0||dot(previous.yzw,previous.yzw)>1e-12)MarkCrowdWork(c);
}
float CrowdPressure(int2 c,float centre){uint j=PlanetIndex(c,_Res);return _NavCosts[j]<INF*.5?_PressureRead[j]:centre;}
[numthreads(128,1,1)]void RelaxCrowd(uint3 id:SV_DispatchThreadID){id.x=CrowdWorkIndex(id.x);if(id.x>=6*_Res*_Res)return;if(_NavCosts[id.x]>=INF*.5){_PressureWrite[id.x]=0;return;}int2 c=PlanetCell(id.x,_Res);float centre=_PressureRead[id.x],area=CrowdArea(c);
 float sum=CrowdPressure(CrowdOffset(c,-1,0),centre)+CrowdPressure(CrowdOffset(c,1,0),centre)+CrowdPressure(CrowdOffset(c,0,-1),centre)+CrowdPressure(CrowdOffset(c,0,1),centre);
 _PressureWrite[id.x]=clamp((sum+(_CrowdRead[id.x].x-.9)*area)*.25,0,area*8);
}
[numthreads(128,1,1)]void FinishCrowd(uint3 id:SV_DispatchThreadID){id.x=CrowdWorkIndex(id.x);if(id.x>=6*_Res*_Res)return;int2 c=PlanetCell(id.x,_Res);float3 d=PlanetCellDirection(c,_Res);float pressure=_PressureRead[id.x];float3 axes[2];float derivatives[2];
 [unroll]for(uint axis=0;axis<2;axis++){int2 a=CrowdOffset(c,axis==0?-1:0,axis==1?-1:0),b=CrowdOffset(c,axis==0?1:0,axis==1?1:0);float3 delta=(PlanetCellDirection(b,_Res)-PlanetCellDirection(a,_Res))*_PlanetRadius;axes[axis]=delta-d*dot(delta,d);derivatives[axis]=CrowdPressure(b,pressure)-CrowdPressure(a,pressure);}
 // Cube-coordinate axes are not orthogonal. Invert the tangent Gram matrix.
 float xx=dot(axes[0],axes[0]),yy=dot(axes[1],axes[1]),xy=dot(axes[0],axes[1]),det=max(1e-8,xx*yy-xy*xy);
 float3 gradient=(axes[0]*(derivatives[0]*yy-derivatives[1]*xy)+axes[1]*(derivatives[1]*xx-derivatives[0]*xy))/det;
 float4 grid=_CrowdGrid[id.x];grid.yzw=_NavCosts[id.x]<INF*.5?lerp(grid.yzw,gradient,.2):0;_CrowdGrid[id.x]=grid;
}
