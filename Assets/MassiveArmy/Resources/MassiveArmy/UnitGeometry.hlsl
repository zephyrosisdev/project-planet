#ifndef ARMY_UNIT_GEOMETRY
#define ARMY_UNIT_GEOMETRY
float2 UnitRotate(float2 p,float angle){float s=sin(angle),c=cos(angle);return float2(c*p.x+s*p.y,-s*p.x+c*p.y);}
void ModelVertex(inout float3 vertex,float2 uv,float4 colour,UnitState u,UnitTraits traits,uint baseMode,out float angle){
                        angle=uv.x>.5?u.turret:u.facing;
                        if(baseMode!=0){
                            uint part=(uint)round(colour.g),movement=(uint)traits.scale.w;
                            float3 centre=part==0?float3(0,.5,0):part==1?float3(-1,.4,0):part==2?float3(1,.4,0):part==3?float3(0,1.1,0):float3(0,1.15,1.6);
                            float3 size=part==0?float3(1.8,.65,3):part<=2?float3(.45,.7,3.4):part==3?float3(1.3,.55,1.4):float3(.17,.17,2.2);
                            float3 local=(vertex-centre)/size;
                            if(movement==0){
                                vertex=part==0?local*float3(.65,1.1,.4)+float3(0,1.4,0):part<=2?local*float3(.23,.9,.35)+float3(part==1?-.2:.2,.45,0):part==3?local*.5+float3(0,2.2,0):local*float3(.12,.12,1)+float3(.4,1.45,.55);
                                if(part>4)vertex=0;angle=u.facing;
                            }else if(movement==3){
                                vertex=part==0?local*float3(.8,.6,4):part<=2?local*float3(3.5,.13,1.25)+float3(part==1?-1.65:1.65,0,-.2):part==3?local*float3(.5,.4,1.1)+float3(0,.35,.6):local*float3(.18,.3,1.5)+float3(0,.45,-1.5);
                                if(part>4)vertex=0;angle=u.facing;
                            }else if(movement==4){
                                vertex=part==0?local*float3(2,.8,5):part<=2?local*float3(.45,.65,3.8)+float3(part==1?-1:1,0,-.3):part==3?local*float3(1.1,1.5,1.4)+float3(0,1.1,-.5):local*float3(.23,.23,2)+float3(0,1,1.5);
                                if(part>4)vertex=0;
                            }else if(traits.role>=3&&traits.role<=5){
                                if(part==3)vertex=local*float3(1.5,1.3,1.8)+float3(0,1.2,-.4);
                                if(part==4)vertex=local*float3(.12,1.8,.12)+float3(0,2,0);
                            }else if(traits.role==7&&part==4){vertex.z=1+(vertex.z-1)*1.7;vertex.y+=.5;}
                            vertex*=traits.scale.xyz;
                        }
                        vertex.xz=UnitRotate(vertex.xz,angle);

}
#endif
