Shader "MassiveArmy/Settlement fields" {SubShader {Tags {"RenderType"="Opaque"} Pass {Cull Off ZWrite On ZTest LEqual
HLSLPROGRAM
#pragma target 5.0
#pragma vertex Vert
#pragma fragment Frag
#include "UnityCG.cginc"
#include "ArtLighting.hlsl"
struct Field {float4 centre,low,right,forward,up,crop;};StructuredBuffer<Field> _Fields;
float4x4 _VP;float3 _CentreHigh,_CentreLow,_Camera;float _Clock,_HarvestSeconds;uint _Clumps;
struct Varying {float4 position:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float3 colour:TEXCOORD2;float2 uv:TEXCOORD3;float blade:TEXCOORD4;};
Varying Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID){Field f=_Fields[instance];f.crop.y=f.crop.y<0?-1:saturate((_Clock-f.crop.y)/max(1,_HarvestSeconds));Varying o=(Varying)0;uint cropVertices=_Clumps*_Clumps*12;float3 p=0,n=float3(0,1,0);bool shown=true;o.blade=vertex<cropVertices?1:0;
 if(vertex<cropVertices){uint blade=vertex/12,q=vertex%12;uint x=blade%_Clumps,y=blade/_Clumps;static const uint corners[6]={0,2,1,1,2,3};uint corner=corners[q%6];float2 uv=float2(corner&1,(corner>>1)&1);float2 offset=(float2(x+.5,y+.5)/_Clumps*2-1)*f.centre.w;
  float growth=saturate(f.crop.x);shown=growth>.02&&(f.crop.y<0||(y+.5)/_Clumps>f.crop.y);float height=.15+growth*1.15;float width=.45+growth*.5;float sway=sin(_Clock*1.8+blade*.71+instance)*.1*uv.y;
  p=float3(offset.x+(q<6?(uv.x-.5)*width:0)+sway,uv.y*height,offset.y+(q>=6?(uv.x-.5)*width:0));n=q<6?float3(0,0,1):float3(1,0,0);o.uv=uv;o.colour=float3(f.crop.z,f.crop.w,.16)*lerp(.65,1,uv.y);
 }else{uint box=(vertex-cropVertices)/36,v=(vertex-cropVertices)%36;static const uint corners[36]={0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5};uint corner=corners[v];float3 q=float3((corner&1)?1:-1,(corner&2)?1:-1,(corner&4)?1:-1);
  shown=f.crop.y>=0&&f.crop.y<1;float row=floor(f.crop.y*_Clumps);float across=frac(f.crop.y*_Clumps);float side=fmod(row,2)<1?across:1-across;float3 tractor=float3((side*2-1)*f.centre.w,0,((row+.5)/_Clumps*2-1)*f.centre.w);
  float3 size=box==0?float3(.8,.45,1.2):box==1?float3(.55,.65,.55):box==2?float3(1.8,.15,.3):float3(.27,.4,.4);
  float3 shift=box<3?float3(0,box==0?.8:box==1?1.6:.4,box==2?1.5:0):float3((box%2==0?1:-1)*.9,.4,box<5?-.8:.8);
  p=tractor+q*size+shift;n=normalize(q);o.colour=box>=3?float3(.045,.05,.04):box==1?float3(.13,.3,.38):float3(.18,.45,.13);o.uv=0;
 }
 float3 body=f.centre.xyz;float3 offset=f.right.xyz*p.x+f.up.xyz*p.y+f.forward.xyz*p.z;o.world=(body+_CentreHigh)+(f.low.xyz+_CentreLow)+offset;o.normal=f.right.xyz*n.x+f.up.xyz*n.y+f.forward.xyz*n.z;o.position=shown?mul(_VP,float4(o.world,1)):float4(0,0,0,0);return o;
}
float4 Frag(Varying i):SV_Target{if(i.blade>.5)clip((1-i.uv.y)*.55+.13-abs(i.uv.x-.5));return float4(ShadeArmour(i.world,normalize(i.normal),i.colour,float4(0,.85,0,.02),float3(1,1,1),_Camera),1);}
ENDHLSL
}}}
