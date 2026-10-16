            #ifdef PLANET_CBT
            #include "PlanetCbtTopology.hlsl"
            StructuredBuffer<uint2> _CbtTriangles;
            #endif
            uint _PlanetMeshRes,_OceanOnly;
            StructuredBuffer<float4> _PlanetPatches;
            uint _PlanetPatchGrid;
            float3 _PlanetCenter;
            float4 _TintOverride;
            float4x4 _PlanetVP;
            StructuredBuffer<float4> _BiomeColours;
            struct Varying {float4 position:SV_POSITION;float3 direction:TEXCOORD0;float height:TEXCOORD1;float3 normal:TEXCOORD4;float3 world:TEXCOORD5;
                #ifdef PLANET_CBT_DEBUG
                float3 barycentric:TEXCOORD2;float depth:TEXCOORD3;
                #endif
            };
            Varying Vert(uint id:SV_VertexID,uint instance:SV_InstanceID){
                float skirt=0;
                #if defined(PLANET_STREAMED)
                float4 patch=_PlanetPatches[instance];uint n=_PlanetPatchGrid,cell=id/6,corner=id%6;
                const uint2 offsets[6]={uint2(0,0),uint2(1,0),uint2(1,1),uint2(0,0),uint2(1,1),uint2(0,1)};
                float2 patchVertex;
                if(cell<n*n)patchVertex=float2(cell%n,cell/n)+offsets[corner];
                else {uint edge=(cell-n*n)/n,segment=(cell-n*n)%n;uint2 offset=offsets[corner];
                    float along=segment+offset.x;patchVertex=edge==0?float2(along,0):edge==1?float2(n,along):edge==2?float2(n-along,n):float2(0,n-along);
                    // Overlapping radial skirts cover unequal LOD edges without changing surface samples.
                    float segmentMetres=_PlanetRadius*patch.z/n;
                    skirt=offset.y*(min(abs(_PlanetHeight)*2,segmentMetres*segmentMetres*PlanetReliefCurvature()*2)+_PlanetRadius*patch.z*patch.z/(n*n)+.5);}
                float3 d=PlanetDirection((uint)patch.w,patch.xy+patchVertex*(patch.z/n));
                #if defined(PLANET_SINGLE_CRATER) || defined(PLANET_TERRAIN_EDITS)
                // A neighbouring coarse edge may skip a crater bowl. Keep enough overlap for its full depth,
                // limited to patches intersecting the canonical edit cap rather than the whole planet.
                if(skirt>0&&_PlanetCraterCount>0&&dot(d,_PlanetEditCap.xyz)>=_PlanetEditCap.w-patch.z*2)skirt=max(skirt,100.0);
                #endif
                #elif defined(PLANET_CBT)
                uint2 leaf=_CbtTriangles[id/3];int2 a,b,c;CbtDecode(leaf.x,leaf.y,a,b,c);uint corner=id%3;float3 d=CbtDirection(leaf.x,corner==0?a:corner==1?b:c);
                #else
                uint n=_PlanetMeshRes,face=id/(n*n*6),cell=(id/6)%(n*n),corner=id%6;
                const uint2 offsets[6]={uint2(0,0),uint2(1,0),uint2(1,1),uint2(0,0),uint2(1,1),uint2(0,1)};
                float2 uv=(float2(cell%n,cell/n)+offsets[corner])*2/n-1;float3 d=PlanetDirection(face,uv);
                #endif
                float h=_OceanOnly!=0?_PlanetOcean:PlanetHeight(d);
                Varying o;o.world=_PlanetCenter+d*(_PlanetRadius+h-skirt);o.position=mul(_PlanetVP,float4(o.world,1));o.direction=d;o.height=h;
                #ifdef PLANET_DEPTH_ONLY
                o.normal=0;
                #else
                o.normal=_OceanOnly!=0?d:PlanetSurfaceNormal(d);
                #endif
                #ifdef PLANET_CBT_DEBUG
                uint bary=id%3;o.barycentric=bary==0?float3(1,0,0):bary==1?float3(0,1,0):float3(0,0,1);
                #if defined(PLANET_STREAMED)
                o.depth=log2(2/patch.z)/20.0;
                #elif defined(PLANET_CBT)
                o.depth=firstbithigh(leaf.y)/14.0;
                #else
                o.depth=0;
                #endif
                #endif
                return o;}
