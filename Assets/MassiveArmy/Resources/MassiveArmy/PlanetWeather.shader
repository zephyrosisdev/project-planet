Shader "MassiveArmy/Planet weather" {
 SubShader {Tags {"RenderType"="Transparent"}
 HLSLINCLUDE
 #include "UnityCG.cginc"
 // HDRP can allocate camera depth as an array even for a non-XR camera.
 #if defined(WEATHER_DEPTH_ARRAY)
 Texture2DArray<float> _WeatherSceneDepth;
 float SceneDepth(int2 pixel){return _WeatherSceneDepth.Load(int4(pixel,0,0));}
 #else
 Texture2D<float> _WeatherSceneDepth;
 float SceneDepth(int2 pixel){return _WeatherSceneDepth.Load(int3(pixel,0));}
 #endif
 Texture2D<float> _WeatherLocalDepth;
 Texture2D<float4> _WeatherIntegrated;Texture3D<float> _WeatherNoise;SamplerState sampler_WeatherNoise;
 float4 _WeatherFullSize,_WeatherHalfSize,_WeatherView,_WeatherBounds,_WeatherAtmosphere,_WeatherCloud,_WeatherWind,_WeatherDepthProjection;
 float3 _WeatherRight,_WeatherUp,_WeatherForward,_WeatherCentre,_WeatherSun,_WeatherAirColour,_WeatherCloudColour;
 float _WeatherRadius,_WeatherLocal,_WeatherNoiseLod;
 float4 _WeatherCabinEye;float4x4 _WeatherWorldToCabin;
 float3 _WeatherAftMin,_WeatherAftMax,_WeatherBridgeMin,_WeatherBridgeMax;
 struct V {float4 position:SV_POSITION;float2 ndc:TEXCOORD0;};
 V Full(uint id:SV_VertexID){V o;float2 uv=float2((id<<1)&2,id&2);o.ndc=uv*2-1;o.position=float4(o.ndc,0,1);return o;}
 // Bounds are computed in camera view coordinates. HDRP's render-target projection
 // can invert Y; both the rasterized quad and its ray coordinates must use that flip.
 V Body(uint id:SV_VertexID){const float2 c[6]={float2(0,0),float2(1,0),float2(1,1),float2(0,0),float2(1,1),float2(0,1)};V o;o.ndc=lerp(_WeatherBounds.xy,_WeatherBounds.zw,c[id]);o.ndc.y*=_WeatherView.z;o.position=float4(o.ndc,0,1);return o;}
 bool HasDepth(float d){
 #if defined(UNITY_REVERSED_Z)
 return d>0;
 #else
 return d<1;
 #endif
 }
 // Decode using the same projection that wrote the procedural scene depth.
 // UnityCG's legacy camera constant buffer is not HDRP's ShaderVariablesGlobal buffer.
 float Eye(float d){return HasDepth(d)?abs(_WeatherDepthProjection.y/(d*_WeatherDepthProjection.z+_WeatherDepthProjection.w+_WeatherDepthProjection.x)):1e20;}
 float NearDepth(int2 halfPixel){
  int2 p=halfPixel*2,limit=int2(_WeatherFullSize.xy)-1;
  float nearest=1e20;
  [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++)nearest=min(nearest,Eye(_WeatherLocalDepth.Load(int3(min(p+int2(x,y),limit),0))));
  return nearest;
 }
 float CopyDepth(V i):SV_Target{return SceneDepth(int2(i.position.xy));}
 float2 RoomRange(float3 inverseRay,float3 low,float3 high){
  float3 a=(low-_WeatherCabinEye.xyz)*inverseRay,b=(high-_WeatherCabinEye.xyz)*inverseRay;
  float3 enter=min(a,b),leave=max(a,b);return float2(max(enter.x,max(enter.y,enter.z)),min(leave.x,min(leave.y,leave.z)));
 }
 float CabinExit(float3 ray){
  [branch]if(_WeatherCabinEye.w<.5)return 0;
  float3 direction=mul((float3x3)_WeatherWorldToCabin,ray);
  float3 inverseRay=rcp(lerp(-1,1,step(0,direction))*max(abs(direction),1e-6));
  float2 aft=RoomRange(inverseRay,_WeatherAftMin,_WeatherAftMax),bridge=RoomRange(inverseRay,_WeatherBridgeMin,_WeatherBridgeMax);
  if(aft.x<=0&&aft.y>0)return bridge.x<=aft.y+.001&&bridge.y>0?max(aft.y,bridge.y):aft.y;
  if(bridge.x<=0&&bridge.y>0)return aft.x<=bridge.y+.001&&aft.y>0?max(bridge.y,aft.y):bridge.y;
  return 0;
 }
 bool Sphere(float3 ray,float radius,out float2 range){float along=dot(ray,_WeatherCentre);float3 side=_WeatherCentre-ray*along;float disc=radius*radius-dot(side,side);range=0;if(disc<0)return false;float root=sqrt(disc);range=float2(along-root,along+root);return range.y>0;}
 float Noise(float3 p){return _WeatherNoise.SampleLevel(sampler_WeatherNoise,p/64,_WeatherNoiseLod);}
 float3 WeatherPoint(float3 p){return float3(p.x*_WeatherWind.y-p.z*_WeatherWind.x,p.y,p.x*_WeatherWind.x+p.z*_WeatherWind.y);}
 float Coverage(float3 up){float3 p=WeatherPoint(up);float3 offset=float3(_WeatherWind.z,_WeatherWind.w,11.3);float n=Noise(p*8.2+offset)*.5+Noise(p*18.1+offset)*.3+Noise(p*41.7+offset)*.2;float threshold=lerp(.76,.24,_WeatherCloud.x);return smoothstep(threshold-.055,threshold+.055,n);}
 float Day(float3 p){float r=length(p),horizon=-sqrt(saturate(1-1/(r*r)));return smoothstep(horizon-.025,horizon+.055,dot(p/r,_WeatherSun));}
 float3 CloudLight(float3 p,float layer,float3 ray){float sun=Day(p)*(.16+.84*saturate(dot(normalize(p),_WeatherSun))),mu=saturate(dot(ray,_WeatherSun));return _WeatherCloudColour*(.018+sun*(.35+1.25*saturate(layer)))*(1+pow(mu,12)*.55);}
 float4 Air(float3 ray,float start,float end){
  float step=(end-start)/6,transmission=1;float3 colour=0;float scale=max(.00001,_WeatherAtmosphere.x*.18);
  [unroll]for(int i=0;i<6;i++){float3 p=ray*(start+(i+.5)*step)-_WeatherCentre;float altitude=max(0,length(p)-1);float density=exp(-altitude/scale);float a=1-exp(-density*step/scale*_WeatherAtmosphere.y*.32);
   float day=Day(p),mu=dot(ray,_WeatherSun);float3 lit=_WeatherAirColour*(.018+day*(.65+.35*mu*mu))+float3(1,.73,.42)*day*pow(saturate(mu),24)*.35;
   colour+=transmission*a*lit*3;transmission*=1-a;
  }return float4(colour,1-transmission);
 }
 float4 Clouds(float3 ray,float begin,float stop,float jitter){
  if(_WeatherCloud.w<.5)return 0;float bottom=1+_WeatherAtmosphere.z,top=bottom+_WeatherAtmosphere.w;float2 outer,inner;
  if(!Sphere(ray,top,outer))return 0;float start=max(begin,outer.x),end=min(stop,outer.y);
  if(Sphere(ray,bottom,inner)){if(inner.x>start)end=min(end,inner.x);else if(inner.y>start)start=inner.y;}
  if(end<=start)return 0;
  float middle=(start+end)*.5;float3 middlePoint=ray*middle-_WeatherCentre,up=normalize(middlePoint);float mask=Coverage(up);float distance=end-start;
  float farAlpha=1-exp(-mask*_WeatherCloud.y*distance*.58);float4 farCloud=float4(CloudLight(middlePoint,.65,ray)*farAlpha,farAlpha);
  [branch]if(_WeatherCloud.z<=.001)return farCloud;
  float transmission=1,step=distance/12;float3 colour=0;
  [loop]for(int i=0;i<12;i++){
   float t=start+(i+jitter)*step;float3 p=ray*t-_WeatherCentre;float layer=saturate((length(p)-bottom)/_WeatherAtmosphere.w);
   float vertical=smoothstep(0,.18,layer)*(1-smoothstep(.65,1,layer));
   float cover=Coverage(normalize(p));if(cover<.001||vertical<.001)continue;float3 detail=WeatherPoint(p)*_WeatherRadius/350+float3(_WeatherWind.z,_WeatherWind.w,0);
   float erosion=Noise(detail)*.65+Noise(detail*2.1)*.35;float density=saturate(cover-erosion*.42*_WeatherCloud.z)*vertical;
   float a=1-exp(-density*_WeatherCloud.y*step);colour+=transmission*a*CloudLight(p,layer,ray);transmission*=1-a;if(transmission<.015)break;
  }
  return lerp(farCloud,float4(colour,1-transmission),_WeatherCloud.z);
 }
 float4 Integrate(V i):SV_Target{
  float3 ray=normalize(_WeatherForward+_WeatherRight*(i.ndc.x*_WeatherView.x*_WeatherView.y)+_WeatherUp*(i.ndc.y*_WeatherView.y*_WeatherView.z));
  float2 atmosphere;if(!Sphere(ray,1+_WeatherAtmosphere.x,atmosphere))return 0;float start=max(CabinExit(ray)/_WeatherRadius,max(0,atmosphere.x)),end=atmosphere.y;
  int2 pixel=clamp(int2(i.position.xy*2),int2(0,0),int2(_WeatherFullSize.xy)-1);float forward=max(.001,dot(ray,_WeatherForward));
  float rawLocal=_WeatherLocalDepth.Load(int3(pixel,0));float stop=NearDepth(int2(i.position.xy))/forward;
  {float raw=SceneDepth(pixel);if(HasDepth(raw)&&abs(raw-rawLocal)>1e-10){float scaled=Eye(raw)/forward;float physical=scaled<10000?10000*scaled/max(.0001,10000-scaled):1e20;stop=min(stop,physical);}}
  end=min(end,stop/_WeatherRadius);float2 ground;if(Sphere(ray,1,ground)&&ground.x>0)end=min(end,ground.x);if(end<=start)return 0;
  uint key=(uint)pixel.x*1973u+(uint)pixel.y*9277u+89173u;key^=key>>16;key*=0x7feb352du;key^=key>>15;float jitter=(key&65535u)/65536.0;
  float4 air=Air(ray,start,end),cloud=Clouds(ray,start,end,jitter);return cloud+air*(1-cloud.a);
 }
 float4 Composite(V i):SV_Target{
  int2 pixel=int2(i.position.xy);float current=Eye(_WeatherLocalDepth.Load(int3(pixel,0)));float2 hp=(pixel+.5)*.5-.5;int2 base=(int2)floor(hp);float2 f=frac(hp);float4 colour=0;float total=0;
  [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++){int2 at=clamp(base+int2(x,y),int2(0,0),int2(_WeatherHalfSize.xy)-1);float d=NearDepth(at);
   float weight=(x?f.x:1-f.x)*(y?f.y:1-f.y)*exp(-abs(d-current)/max(.5,current*.01));colour+=_WeatherIntegrated.Load(int3(at,0))*weight;total+=weight;
  // Never fall back to a background-cloud sample on a thin foreground silhouette.
  }return total>1e-5?colour/total:0;
 }
 ENDHLSL
 Pass {Cull Off ZWrite Off ZTest Always
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Full
 #pragma fragment CopyDepth
 #pragma multi_compile_local _ WEATHER_DEPTH_ARRAY
 ENDHLSL
 }
 Pass {Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Body
 #pragma fragment Integrate
 #pragma multi_compile_local _ WEATHER_DEPTH_ARRAY
 ENDHLSL
 }
 Pass {Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Full
 #pragma fragment Composite
 ENDHLSL
 }
 }}
