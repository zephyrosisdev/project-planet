// Port of Battle.compute's predictive passing, queue constraint and finite acceleration.
// All vectors are body-local world tangents; face-local velocity is decoded before pair tests.
StructuredBuffer<float> _NavField;float _MaxContactRadius;
StructuredBuffer<GroupOrder> _Groups;
float3 PlanetRouteVelocity(UnitState u,float3 d,float speed){
 if(u.group>=GROUP_COUNT||(u.flags&STOPPED)!=0)return 0;
 float3 goal=PlanetDecode(_Groups[u.group].destination),target=d;int2 c=PlanetPositionCell(u.position,_Res);uint offset=u.group*6*_Res*_Res;float best=_NavField[offset+PlanetIndex(c,_Res)];
 if(best>=INF*.5){
 #ifdef PLANET_REGIONAL_NAV
 float3 localTarget;if(RegionalRoute(u.group,d,localTarget)){float3 delta=localTarget-d*dot(localTarget,d);return CrowdNormal(delta)*min(speed,PlanetArc(d,localTarget,_PlanetRadius)/max(.001,_Dt));}
 #endif
 return 0;}if(best<.001)target=goal;
 else{const int2 steps[4]={int2(-1,0),int2(1,0),int2(0,-1),int2(0,1)};[unroll]for(uint k=0;k<4;k++){int2 q=PlanetNeighbour(c,steps[k],_Res);float value=_NavField[offset+PlanetIndex(q,_Res)];if(value<best){best=value;target=PlanetCellDirection(q,_Res);}}}
 #ifdef PLANET_REGIONAL_NAV
 float3 regionalTarget;if(RegionalRoute(u.group,d,regionalTarget))target=regionalTarget;
#endif
 float3 delta=target-d*dot(target,d);return CrowdNormal(delta)*min(speed,PlanetArc(d,target,_PlanetRadius)/max(.001,_Dt));
}
bool PassingLand(float3 d,float3 side,float distance,uint movement){float3 candidate=normalize(d+side*distance/_PlanetRadius);uint cell=PlanetIndex(PlanetPositionCell(PlanetEncode(candidate),_Res),_Res);return _NavCosts[movement*6*_Res*_Res+cell]<INF*.5;}
float3 AvoidPlanetTraffic(inout UnitState u,uint i,float3 desired){
 UnitArchetype a=_Types[Type(u)];if((u.flags&STOPPED)!=0)return 0;if(a.movement>=3)return desired;
 float3 d=PlanetDecode(u.position),direction=CrowdNormal(desired),side=cross(d,direction),separation=0,yielding=0,queueNormal=0;
 float speedScale=1,passing=0,queueLower=0,queueViolation=0;bool rightOccupied=false,leftOccupied=false,nearTraffic=false;
 int2 cell=PlanetPositionCell(u.position,_Res),contact=PlanetPositionCell(u.position,_ContactRes);uint examined=0;int rings=clamp((int)ceil((a.radius+_MaxContactRadius+4.4)/max(1,2*_PlanetRadius/_ContactRes*.45)),1,4);
 for(int ring=0;ring<=rings;ring++)for(int y=-ring;y<=ring;y++)for(int x=-ring;x<=ring;x++){if(max(abs(x),abs(y))!=ring||examined>=96)continue;int2 neighbour=contact;for(int sx=0;sx<abs(x);sx++)neighbour=PlanetNeighbour(neighbour,int2(x<0?-1:1,0),_ContactRes);for(int sy=0;sy<abs(y);sy++)neighbour=PlanetNeighbour(neighbour,int2(0,y<0?-1:1),_ContactRes);uint p=_ContactHeadsRead[PlanetIndex(neighbour,_ContactRes)];
  for(uint k=0;k<32&&p!=INVALID&&examined<96;k++,examined++){UnitState v=_SnapshotRead[p];uint other=p;p=_ContactNextRead[p];if(other==i||_Types[Type(v)].movement>=3)continue;
   float3 delta=(d-PlanetDecode(v.position))*_PlanetRadius;delta-=d*dot(delta,d);float distance=length(delta),radius=a.radius+_Types[Type(v)].radius+.4;
   if(distance>=radius+4)continue;float3 normal=distance>1e-5?delta/distance:CrowdNormal(cross(d,abs(d.y)<.9?float3(0,1,0):float3(1,0,0)))*(i<other?1:-1);
   float3 velocity=CrowdVelocity(v);velocity-=d*dot(velocity,d);nearTraffic=true;
   rightOccupied=rightOccupied||length(delta+side*(radius+1))<radius+.3;
   leftOccupied=leftOccupied||length(delta-side*(radius+1))<radius+.3;
   if(dot(direction,-normal)>.4&&(length(velocity)<.2||dot(direction,CrowdNormal(velocity))>.3)){
    float lower=dot(velocity,normal)-max(0,distance-(radius+.15))/_Dt;float violation=lower-dot(desired,normal);
    if(violation>queueViolation){queueViolation=violation;queueNormal=normal;queueLower=lower;}
   }
   if((u.flags&ARRIVED)!=0&&Team(u)==Team(v)&&(v.flags&(ARRIVED|STOPPED))==0){float3 traffic=PlanetDecode(v.destination);traffic=CrowdNormal(traffic-d*dot(traffic,d));float3 lateral=cross(d,traffic);float ahead=dot(delta,traffic),offset=dot(delta,lateral);if(ahead>0&&abs(offset)<radius)yielding+=lateral*(offset>=0?1:-1)*(1-distance/(radius+4));}
   if(distance<radius*1.25)separation+=normal*(1-distance/(radius*1.25));
   float3 relative=desired-velocity;float closest=clamp(-dot(delta,relative)/max(dot(relative,relative),.001),0,.35);
   if(dot(delta,relative)<0&&length(delta+relative*closest)<radius*1.15){passing+=.9;speedScale=min(speedScale,saturate((distance-radius-.15)/2.5));}
  }
 }
 if(!nearTraffic)u.flags&=~(PASS_RIGHT|PASS_LEFT);
 bool rightRoom=false,leftRoom=false;
 [branch]if(passing>0||(u.flags&(PASS_RIGHT|PASS_LEFT))!=0){rightRoom=!rightOccupied&&PassingLand(d,side,a.radius*2+1.4,a.movement);leftRoom=!leftOccupied&&PassingLand(d,-side,a.radius*2+1.4,a.movement);}
 if((u.flags&PASS_RIGHT)!=0&&!rightRoom)u.flags&=~PASS_RIGHT;if((u.flags&PASS_LEFT)!=0&&!leftRoom)u.flags&=~PASS_LEFT;
 if((u.flags&(PASS_RIGHT|PASS_LEFT))==0&&passing>0){if(rightRoom)u.flags|=PASS_RIGHT;else if(leftRoom)u.flags|=PASS_LEFT;}
 if(passing>0)separation+=side*((u.flags&PASS_RIGHT)!=0?.9:(u.flags&PASS_LEFT)!=0?-.9:0);
 float3 steering=direction*speedScale+separation*1.7;float3 result=CrowdNormal(steering)*min(1,length(steering))*length(desired);
 if(length(desired)<.01)result=CrowdNormal(separation)*min(1,length(separation))*a.speed*.25;
 if(length(yielding)>.01)result=CrowdNormal(yielding)*a.speed*.5*min(1,length(yielding)*2);
 float4 crowd=_CrowdRead[PlanetIndex(cell,_Res)];float3 gradient=crowd.yzw;gradient-=d*dot(gradient,d);float3 normal=CrowdNormal(gradient);
 [branch]if(dot(gradient,gradient)>0){result-=normal*max(0,dot(result,normal))*saturate((crowd.x-.85)/.4);
 float3 pressure=-gradient*(.12/_Dt);result+=CrowdNormal(pressure)*min(length(pressure),a.speed*.8);result=CrowdNormal(result)*min(length(result),a.speed);}
 if(queueViolation>0&&dot(result,queueNormal)<queueLower)result+=queueNormal*(queueLower-dot(result,queueNormal));
 float3 current=CrowdVelocity(u),change=lerp(current,result,saturate(_Dt*7))-current;
 float3 axis=length(direction)>.01?direction:CrowdNormal(current);float along=dot(change,axis);float3 sideways=change-axis*along;
 result=current+axis*clamp(along,-a.speed*1.5*_Dt,a.speed*1.5*_Dt)+CrowdNormal(sideways)*min(length(sideways),a.speed*.5*_Dt);
 if(queueViolation>0&&dot(result,queueNormal)<queueLower)result+=queueNormal*(queueLower-dot(result,queueNormal));
 return CrowdNormal(result)*min(length(result),a.speed);
}
