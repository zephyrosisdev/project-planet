// Bounded tactical patches. Coordinates remain body-local across poles, seams and rebases.
StructuredBuffer<float4> _RegionalFrames,_RegionalBuildings;
StructuredBuffer<float> _RegionalField,_RegionalCosts;
StructuredBuffer<uint> _RegionalPick;
uint _RegionalResolution,_RegionalBuildingCount;float _RegionalCellSize;
bool RegionalCell(uint group,float3 direction,out int2 cell){
 cell=0;if(group>=GROUP_COUNT)return false;float4 anchor=_RegionalFrames[group*3];if(anchor.w<.5)return false;
 float denominator=dot(direction,anchor.xyz);if(denominator<.99)return false;
 float2 p=float2(dot(direction,_RegionalFrames[group*3+1].xyz),dot(direction,_RegionalFrames[group*3+2].xyz))*(_PlanetRadius/(denominator*_RegionalCellSize))+_RegionalResolution*.5;
 cell=(int2)floor(p);return all(cell>=0)&&all(cell<(int)_RegionalResolution);
}
uint RegionalIndex(uint group,int2 c){return group*_RegionalResolution*_RegionalResolution+c.x+c.y*_RegionalResolution;}
float3 RegionalDirection(uint group,int2 c){float2 metres=(float2(c)+.5-_RegionalResolution*.5)*_RegionalCellSize;return normalize(_RegionalFrames[group*3].xyz+(_RegionalFrames[group*3+1].xyz*metres.x+_RegionalFrames[group*3+2].xyz*metres.y)/_PlanetRadius);}
bool RegionalRoute(uint group,float3 d,out float3 target){
 target=d;int2 c;if(!RegionalCell(group,d,c))return false;float best=_RegionalField[RegionalIndex(group,c)];if(best>=INF*.5)return false;
 if(best<.001){uint pick=_RegionalPick[group]&65535u;int2 chosen=int2(pick%_RegionalResolution,pick/_RegionalResolution);target=RegionalDirection(group,chosen);return true;}
 [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){if(x==0&&y==0)continue;int2 q=c+int2(x,y);if(any(q<0)||any(q>=(int)_RegionalResolution))continue;
  if(x!=0&&y!=0&&(_RegionalCosts[RegionalIndex(group,c+int2(x,0))]>=INF*.5||_RegionalCosts[RegionalIndex(group,c+int2(0,y))]>=INF*.5))continue;
  float value=_RegionalField[RegionalIndex(group,q)];if(value<best){best=value;target=RegionalDirection(group,q);}
 }
 return true;
}
bool RegionalTerrainClear(uint group,float3 d,uint movement){
 if(movement==3)return true;float height=PlanetHeight(d);if(movement==4?height>=_PlanetOcean:height<_PlanetOcean)return false;
 // A coarse planet cell must never turn a 30 m building into a kilometre obstacle.
 for(uint i=0;i<_RegionalBuildingCount;i++){float4 b=_RegionalBuildings[i];float3 delta=(d-b.xyz)*_PlanetRadius;if(b.w>0&&dot(delta,delta)<b.w*b.w)return false;}
 return true;
}
