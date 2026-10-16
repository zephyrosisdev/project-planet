            sampler2D _Splat,_LayersA,_LayersB;
            UNITY_DECLARE_TEX2DARRAY(_SurfaceAlbedo);
            UNITY_DECLARE_TEX2DARRAY(_SurfaceData);
            #define UNITY_SAMPLE_TEX2DARRAY_GRAD(tex,coord,dx,dy) tex.SampleGrad(sampler##tex,coord,dx,dy)
            float _WorldSize,_HeightBlendWidth,_HeightBlendStrength,_NormalStrength,_MacroVariation;
            float4 _BiomeBlend,_BiomeDetail,_LayersA_TexelSize;
            float _BlendSeed,_BlendDebug,_GeographyGuided;
            float4 _LayerParameters[9]; // Tile size in metres, normal strength, reserved, reserved.
            struct TerrainInput { float4 position:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float3 world:TEXCOORD2; };
            float ArmyHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p) { float2 f=frac(p);f=f*f*(3-2*f);float2 c=floor(p);return lerp(lerp(ArmyHash(c),ArmyHash(c+float2(1,0)),f.x),lerp(ArmyHash(c+float2(0,1)),ArmyHash(c+1),f.x),f.y); }
            float2 Gradient(float2 p) {
                uint2 cell=(uint2)(int2)p;
                uint h=cell.x*1597334677u^cell.y*3812015801u;h^=h>>16;h*=2246822519u;h^=h>>13;
                float2 g=float2((h&1u)?1:-1,(h&2u)?1:-1);
                if(h&4u)g.x=0;else if(h&8u)g.y=0;
                return g;
            }
            float2 PeriodicGradient(float2 p,float period){return Gradient(p-floor(p/period)*period);}
            float OrganicNoise(float2 p,float period) {
                float2 c=floor(p),f=frac(p),u=f*f*f*(f*(f*6-15)+10);
                return lerp(lerp(dot(PeriodicGradient(c,period),f),dot(PeriodicGradient(c+float2(1,0),period),f-float2(1,0)),u.x),
                    lerp(dot(PeriodicGradient(c+float2(0,1),period),f-float2(0,1)),dot(PeriodicGradient(c+1,period),f-1),u.x),u.y);
            }
            float FractalNoise(float2 p,float period) {
                return OrganicNoise(p,period)*.72+OrganicNoise(p*2+float2(17,43),period*2)*.36;
            }
            // Each projection uses the same UVs and derivatives for all maps. Explicit gradients
            // keep mip selection well-defined even when an absent layer skips its texture reads.
            float4 Data(float3 world,float3 n,int layer) {
                float size=_WorldSize/max(1,round(_WorldSize/max(_LayerParameters[layer].x,.1)));
                float2 uv=world.xz/size,dx=ddx(world.xz)/size,dy=ddy(world.xz)/size;
                float4 top=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceData,float3(uv,layer),dx,dy);
                if(layer!=4)return top;
                // Rock also projects onto cliffs, avoiding stretched horizontal textures.
                float3 weights=pow(abs(n),4);weights/=max(weights.x+weights.y+weights.z,.0001);
                float4 sideX=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceData,float3(world.zy/size,layer),ddx(world.zy)/size,ddy(world.zy)/size);
                float4 sideZ=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceData,float3(world.xy/size,layer),ddx(world.xy)/size,ddy(world.xy)/size);
                // Convert side-map slopes to the XZ surface basis. Top retains its full detail.
                float2 slopes=(top.rg*2-1)*weights.y;
                slopes+=float2(-(sideX.g*2-1)*sign(n.x),(sideX.r*2-1))*weights.x;
                slopes+=float2(sideZ.r*2-1,-(sideZ.g*2-1)*sign(n.z))*weights.z;
                return float4(slopes*.5+.5,top.ba*weights.y+sideX.ba*weights.x+sideZ.ba*weights.z);
            }
            float3 Albedo(float3 world,float3 n,int layer) {
                float size=_WorldSize/max(1,round(_WorldSize/max(_LayerParameters[layer].x,.1)));
                float3 top=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceAlbedo,float3(world.xz/size,layer),ddx(world.xz)/size,ddy(world.xz)/size).rgb;
                if(layer!=4)return top;
                float3 weights=pow(abs(n),4);weights/=max(weights.x+weights.y+weights.z,.0001);
                float3 sideX=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceAlbedo,float3(world.zy/size,layer),ddx(world.zy)/size,ddy(world.zy)/size).rgb;
                float3 sideZ=UNITY_SAMPLE_TEX2DARRAY_GRAD(_SurfaceAlbedo,float3(world.xy/size,layer),ddx(world.xy)/size,ddy(world.xy)/size).rgb;
                return top*weights.y+sideX*weights.x+sideZ*weights.z;
            }
            float4 EvaluateTerrain(TerrainInput i,out float3 normal,out float roughness) { normal=float3(0,1,0);roughness=1;
                float4 localA=tex2D(_LayersA,i.uv),localB=tex2D(_LayersB,i.uv),s=tex2D(_Splat,i.uv);
                float2 seed=float2(_BlendSeed*.137,_BlendSeed*.071);
                float blendPeriod=max(1,round(_WorldSize/max(_BiomeBlend.z,1)));float2 p=i.world.xz/_WorldSize*blendPeriod+seed;
                // Two frequencies bend the region boundaries without moving height or navigation data.
                float2 warp=float2(FractalNoise(p,blendPeriod),FractalNoise(p+float2(37,81),blendPeriod))*1.8;
                float2 uv=frac(i.uv+warp*_BiomeBlend.y*lerp(1,.3,_GeographyGuided)/max(_WorldSize,1));
                // Generated mip chains form a prefiltered hierarchy of splat masks. World-space
                // width makes transitions independent of map resolution and allows live tuning.
                float cell=max(_WorldSize*_LayersA_TexelSize.x,.001);
                float lod=max(0,log2(max(_BiomeBlend.x*lerp(1,.5,_GeographyGuided)/cell,1)));
                float4 a=tex2Dlod(_LayersA,float4(uv,0,lod)),b=tex2Dlod(_LayersB,float4(uv,0,lod));
                // Mud follows the filtered, warped coverage even at water beds.
                // Replacing it with categorical water cells stamped rounded squares
                // over its transition. Other bed materials and local cliffs stay protected.
                float mud=b.b;
                float submerged=smoothstep(.25,.9,s.a);
                a=lerp(a,localA,submerged);b=lerp(b,localB,submerged);
                // Keep the filtered mud fraction after restoring the other channels.
                // Otherwise normalization can turn a tiny mud sample into a solid patch.
                float otherScale=(1-mud)/max(dot(a,1)+b.r+b.g+b.a,.00001);
                a*=otherScale;b.rga*=otherScale;
                b.b=mud;
                float rock=max(b.r,localB.r),nonRock=dot(a,1)+b.g+b.b+b.a;
                float rescale=(1-rock)/max(nonRock,.00001);
                a*=rescale;b.gba*=rescale;b.r=rock;
                float road=smoothstep(.02,.95,s.b);
                float detailPeriod=max(1,round(_WorldSize/max(_BiomeDetail.x,1)));float2 detail=i.world.xz/_WorldSize*detailPeriod+seed;
                detail+=warp*1.3;
                // Fade fine noise under minification to avoid sparkling at distant camera zooms.
                float detailFade=1-smoothstep(.35,1.5,max(length(ddx(detail)),length(ddy(detail))));
                float3 patch=float3(FractalNoise(detail,detailPeriod),FractalNoise(detail+float2(41,19),detailPeriod),FractalNoise(p*2+31,blendPeriod*2))*1.8;
                patch.xy*=detailFade;
                float shoulder=4*road*(1-road)*(1-smoothstep(.75,.98,road));
                road=saturate(road+patch.x*shoulder*_BiomeDetail.y*.45);
                float original[8]={a.r,a.g,a.b,a.a,b.r,b.g,b.b,b.a};
                float varied[8];float weightSum=0;
                [unroll]for(int v=0;v<8;v++) {
                    // Positive multiplicative competition cannot invent unrelated materials.
                    float3 direction=normalize(float3(ArmyHash(float2(v,7))*2-1,ArmyHash(float2(v,23))*2-1,ArmyHash(float2(v,53))*2-1));
                    varied[v]=original[v]*exp2(dot(patch,direction)*_BiomeBlend.w*3*(1-submerged)*lerp(1,.2,_GeographyGuided));
                    weightSum+=varied[v];
                }
                a=float4(varied[0],varied[1],varied[2],varied[3])/max(weightSum,.00001);
                b=float4(varied[4],varied[5],varied[6],varied[7])/max(weightSum,.00001);
                a*=1-road;b*=1-road;
                if(_BlendDebug>.5)return float4(a.r,a.g,b.r,1); // Validation: grass/sand/rock coverage.
                float weights[9]={a.r,a.g,a.b,a.a,b.r,b.g,b.b,b.a,road};
                float4 data[9];float raised[9];float highest=-1;
                float3 geometric=normalize(i.normal);
                [unroll]for(int layer=0;layer<9;layer++) {
                    data[layer]=float4(.5,.5,.5,1);
                    raised[layer]=-1;
                    [branch]if(weights[layer]>.0001) {
                        data[layer]=Data(i.world,geometric,layer);
                        raised[layer]=data[layer].b*weights[layer];
                        highest=max(highest,raised[layer]);
                    }
                }
                float refined[9];float total=0;
                [unroll]for(int j=0;j<9;j++) {
                    refined[j]=max(raised[j]-highest+max(_HeightBlendWidth,.01),0)*weights[j];
                    total+=refined[j];
                }
                float3 colour=0;float2 normalXY=0;roughness=0;float sum=0;
                [unroll]for(int k=0;k<9;k++) {
                    float weight=lerp(weights[k],refined[k]/max(total,.00001),_HeightBlendStrength);
                    [branch]if(weight>.0001) {
                        colour+=Albedo(i.world,geometric,k)*weight;
                        normalXY+=(data[k].rg*2-1)*_LayerParameters[k].y*weight;
                        roughness+=data[k].a*weight;sum+=weight;
                    }
                }
                colour/=max(sum,.00001);normalXY/=max(sum,.00001);roughness/=max(sum,.00001);
                float3 tangent=normalize(float3(1,0,0)-geometric*geometric.x);
                // Avoid a singular basis on a vertical face whose normal points exactly along X.
                if(abs(geometric.x)>.99)tangent=normalize(cross(float3(0,0,1),geometric));
                float3 bitangent=normalize(cross(tangent,geometric));
                float normalZ=sqrt(max(1-dot(normalXY,normalXY),.001));
                normal=normalize(geometric*normalZ+tangent*normalXY.x*_NormalStrength+bitangent*normalXY.y*_NormalStrength);
                float macro=((OrganicNoise(i.world.xz/_WorldSize*max(1,round(_WorldSize*.012)),max(1,round(_WorldSize*.012)))+.5)-.5)*_MacroVariation*2;
                colour*=1+macro;
                return float4(colour,1);
            }
