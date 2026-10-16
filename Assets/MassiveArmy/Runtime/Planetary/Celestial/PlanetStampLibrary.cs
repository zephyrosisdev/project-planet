using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
namespace MassiveArmy.Planetary
{
    [CreateAssetMenu(menuName="Massive Army/Planet Stamp Library")]
    public sealed class PlanetStampLibrary:ScriptableObject
    {
        [Serializable]public sealed class StampDefinition
        {
            public string name;
            [Tooltip("Editor source path; original TIFFs are excluded from player dependencies.")]public string sourceAsset;
            [Range(0,1)]public float heightGain=1;
            [Range(.5f,3)]public float footprint=1.5f;
            [Range(-1,1)]public float heightOffset;
        }
        public const int Resolution=512,Grid=24,Points=Grid+1;
        [StructLayout(LayoutKind.Sequential)]internal struct CornerRecord {public Vector4 u,v,appearance;}
        // The owning PlanetDefinition releases this immutable terrain metadata on unload.
        public sealed class Field:IDisposable
        {
            internal readonly int seed;internal readonly CornerRecord[] corners;GraphicsBuffer buffer;
            internal Field(PlanetStampLibrary library,int seed){this.seed=seed;corners=new CornerRecord[6*Points*Points];for(int f=0;f<6;f++)for(int y=0;y<Points;y++)for(int x=0;x<Points;x++)corners[(f*Points+y)*Points+x]=library.MakeCorner(f,x,y,seed);}
            internal GraphicsBuffer Buffer {get{if(buffer==null){buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,corners.Length,48);buffer.SetData(corners);}return buffer;}}
            public void Dispose(){buffer?.Dispose();buffer=null;}
        }
        public StampDefinition[] definitions;
        static PlanetStampLibrary instance;
        ushort[] heights;Texture2DArray texture,fallbackTexture;GraphicsBuffer fallbackBuffer;
        readonly Dictionary<string,int> kernelCounts=new Dictionary<string,int>();
        public static PlanetStampLibrary Shared=>instance!=null?instance:instance=Resources.Load<PlanetStampLibrary>("MassiveArmy/PlanetStamps/Library");
        public int Count=>definitions.Length;
        float maximumDisplacement=-1;
        public float MaximumDisplacement {get{if(maximumDisplacement>=0)return maximumDisplacement;maximumDisplacement=1;foreach(var d in definitions)maximumDisplacement=Mathf.Max(maximumDisplacement,Mathf.Max(.24f+.72f*(d.heightGain+Mathf.Max(0,d.heightOffset)),.36f+.72f*Mathf.Max(0,-d.heightOffset)));return maximumDisplacement;}}
        void OnValidate(){maximumDisplacement=-1;}
        void Initialize(){if(heights!=null)return;if(Count<1||Count>16)throw new InvalidOperationException("Use one to sixteen planet stamp definitions.");
            var asset=Resources.Load<TextAsset>("MassiveArmy/PlanetStamps/Heights");var bytes=asset.bytes;heights=new ushort[bytes.Length/2];Buffer.BlockCopy(bytes,0,heights,0,bytes.Length);Resources.UnloadAsset(asset);
            if(heights.Length!=Resolution*Resolution*Count)throw new InvalidOperationException("Planet stamp bake does not match the library. Rebuild stamp data.");
        }
        void InitializeGpu(bool enabled){if(kernelCounts.Count==0)foreach(var line in Resources.Load<TextAsset>("MassiveArmy/PlanetStamps/Kernels").text.Split('\n')){var parts=line.Trim().Split(':');if(parts.Length==2)kernelCounts[parts[0]]=int.Parse(parts[1]);}
            if(enabled){Initialize();if(texture==null){texture=new Texture2DArray(Resolution,Resolution,Count,TextureFormat.R16,false,true){name="Canonical planet stamp heightmaps",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};for(int i=0;i<Count;i++)texture.SetPixelData(heights,0,i,i*Resolution*Resolution);texture.Apply(false,true);}}
            else if(fallbackTexture==null){fallbackTexture=new Texture2DArray(1,1,1,TextureFormat.R16,false,true);fallbackTexture.SetPixelData(new ushort[1],0,0);fallbackTexture.Apply(false,true);fallbackBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);fallbackBuffer.SetData(new CornerRecord[1]);}
        }
        Field GetField(PlanetDefinition d){Initialize();if(d.stampField==null||d.stampField.seed!=d.terrainSeed){d.stampField?.Dispose();d.stampField=new Field(this,d.terrainSeed);}return d.stampField;}
        bool Enabled(PlanetDefinition d)=>d.stampTerrain&&d.radius>=20000&&d.terrainHeight!=0;
        public void Bind(ComputeShader shader,PlanetDefinition d){bool enabled=Enabled(d);InitializeGpu(enabled);shader.SetInt("_PlanetUseStamps",enabled?1:0);var source=enabled?GetField(d).Buffer:fallbackBuffer;
            string name=shader.name.Replace("(Clone)","").Trim();if(!kernelCounts.TryGetValue(name,out int count))throw new InvalidOperationException("Unregistered terrain shader "+name);
            for(int i=0;i<count;i++){shader.SetTexture(i,"_PlanetStampMaps",enabled?texture:fallbackTexture);shader.SetBuffer(i,"_PlanetStampCorners",source);}
        }
        public void Bind(Material material,PlanetDefinition d){bool enabled=Enabled(d);InitializeGpu(enabled);material.SetInt("_PlanetUseStamps",enabled?1:0);material.SetTexture("_PlanetStampMaps",enabled?texture:fallbackTexture);material.SetBuffer("_PlanetStampCorners",enabled?GetField(d).Buffer:fallbackBuffer);}
        static uint Hash(Vector3 corner,int seed){unchecked{uint h=(uint)(int)corner.x*1597334677u^(uint)(int)corner.y*3812015801u^(uint)(int)corner.z*2798796415u^(uint)seed;h^=h>>16;h*=2246822519u;h^=h>>13;return h;}}
        // Shared integer cube corners keep seed choice identical across face seams.
        CornerRecord MakeCorner(int face,int x,int y,int seed){var c=CubeSphere.Normal[face]*Grid+CubeSphere.Right[face]*(x*2-Grid)+CubeSphere.Up[face]*(y*2-Grid);uint hash=Hash(c,seed);var center=c.normalized;
            var right=Vector3.Cross(Mathf.Abs(center.y)>.95f?Vector3.right:Vector3.up,center).normalized;var up=Vector3.Cross(center,right);
            // Break regular lattice rows without adding work to terrain queries. Shared edge hashes still agree.
            float jx=((hash>>8)&255)/255f-.5f,jy=((hash>>16)&255)/255f-.5f;
            center=(center+(right*jx+up*jy)*(.7f/Grid)).normalized;
            right=Vector3.ProjectOnPlane(right,center).normalized;up=Vector3.Cross(center,right);
            float angle=((hash>>3)&65535)*(Mathf.PI*2/65535f);var rotated=right*Mathf.Cos(angle)+up*Mathf.Sin(angle);up=Vector3.Cross(center,rotated);right=rotated;
            int slice=(int)(hash%(uint)Count);var def=definitions[slice];float scale=Grid/(2f*def.footprint);right*=scale;up*=scale;
            // Bake rotation/reflection into the two dot products used by every GPU query.
            if((hash&16)!=0)right=-right;for(int k=0;k<(int)((hash>>5)&3);k++){var previous=right;right=-up;up=previous;}
            return new CornerRecord{u=new Vector4(right.x,right.y,right.z,-Vector3.Dot(center,right)),v=new Vector4(up.x,up.y,up.z,-Vector3.Dot(center,up)),appearance=new Vector4(slice,def.heightGain,def.heightOffset,MountainMask(c.normalized,seed))};
        }
        static float MountainMask(Vector3 p,int seed){float s=(seed%1009)*.017f;var q=p*3.1f+new Vector3(s,s*.37f,-s*.61f);float n=PlanetClimate.Noise(q);float t=Mathf.Clamp01((n-.44f)/.28f);return t*t*(3-2*t);}
        float Sample(int slice,float u,float v){float x=Mathf.Clamp(u*Resolution-.5f,0,Resolution-1),y=Mathf.Clamp(v*Resolution-.5f,0,Resolution-1);int ix=(int)x,iy=(int)y,nx=Math.Min(ix+1,Resolution-1),ny=Math.Min(iy+1,Resolution-1),offset=slice*Resolution*Resolution;
            float a=Mathf.Lerp(heights[offset+iy*Resolution+ix],heights[offset+iy*Resolution+nx],x-ix),b=Mathf.Lerp(heights[offset+ny*Resolution+ix],heights[offset+ny*Resolution+nx],x-ix);return Mathf.Lerp(a,b,y-iy)/65535f;
        }
        float Corner(Vector3 p,CornerRecord c){float u=Vector3.Dot(p,(Vector3)c.u)+c.u.w,v=Vector3.Dot(p,(Vector3)c.v)+c.v.w;float edge=Mathf.Clamp01((.5f-Mathf.Max(Mathf.Abs(u),Mathf.Abs(v)))*10);edge=edge*edge*(3-2*edge);return (Sample((int)c.appearance.x,u+.5f,v+.5f)*c.appearance.y+c.appearance.z)*edge;}
        Vector2 Terrain(Vector3 p,int seed,Field field){Initialize();CubeSphere.Project(p,out var face,out var uv);var q=(uv+Vector2.one)*(Grid*.5f);int x=Math.Min(Grid-1,Mathf.FloorToInt(q.x)),y=Math.Min(Grid-1,Mathf.FloorToInt(q.y));float a=q.x-x,b=q.y-y;a=a*a*(3-2*a);b=b*b*(3-2*b);
            CornerRecord At(int px,int py)=>field!=null?field.corners[((int)face*Points+py)*Points+px]:MakeCorner((int)face,px,py,seed);
            var c00=At(x,y);var c10=At(x+1,y);var c01=At(x,y+1);var c11=At(x+1,y+1);
            return new Vector2(Mathf.Lerp(Mathf.Lerp(Corner(p,c00),Corner(p,c10),a),Mathf.Lerp(Corner(p,c01),Corner(p,c11),a),b),Mathf.Lerp(Mathf.Lerp(c00.appearance.w,c10.appearance.w,a),Mathf.Lerp(c01.appearance.w,c11.appearance.w,a),b));
        }
        public Vector2 Terrain(Vector3 p,PlanetDefinition definition)=>Terrain(p,definition.terrainSeed,GetField(definition));
        public float Height(Vector3 p,PlanetDefinition definition)=>Terrain(p,definition).x;
        public float Height(Vector3 p,int seed)=>Terrain(p,seed,null).x;
        void OnDisable(){if(texture!=null)ArmyLifetime.Destroy(texture);if(fallbackTexture!=null)ArmyLifetime.Destroy(fallbackTexture);fallbackBuffer?.Dispose();fallbackBuffer=null;texture=null;fallbackTexture=null;heights=null;}
    }
}
