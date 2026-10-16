using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
namespace MassiveArmy.Planetary
{
    // Persistent surface paint, independent of the transient GPU casualty ring.
    // Fixed memory and two texture samples in painted cells, regardless of impact count.
    public sealed class PlanetNuclearScorch : IDisposable
    {
        const int Size=1024,Grid=32,Block=Size/Grid,CellResolution=128;
        readonly Material material;readonly ComputeShader shader;readonly RenderTexture colour,relief;
        readonly GraphicsBuffer cells;readonly uint[] painted=new uint[6*CellResolution*CellResolution];
        static readonly Vector3[] centres=TileCentres();
        public int Count {get;private set;}
        public const long AtlasBytes=6L*Size*Size*4*2;
        public PlanetNuclearScorch(Material material,Texture2DArray colours,Texture2DArray normals,Vector4 neutral)
        {
            this.material=material;shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetNuclearScorch"));
            colour=Atlas("Persistent nuclear colour");relief=Atlas("Persistent nuclear normals");
            cells=new GraphicsBuffer(GraphicsBuffer.Target.Structured,painted.Length,4);cells.SetData(painted);
            foreach(string name in new[]{"Clear","Paint"}){int k=shader.FindKernel(name);shader.SetTexture(k,"_ScorchColour",colour);shader.SetTexture(k,"_ScorchRelief",relief);}
            int paint=shader.FindKernel("Paint");shader.SetTexture(paint,"_NukeColours",colours);shader.SetTexture(paint,"_NukeNormals",normals);shader.SetVector("_NukeNeutral",neutral);shader.SetInt("_ScorchSize",Size);shader.SetInt("_SourceSize",colours.width);
            shader.Dispatch(shader.FindKernel("Clear"),Size/8,Size/8,6);
            material.SetTexture("_PlanetScorchColour",colour);material.SetTexture("_PlanetScorchRelief",relief);material.SetBuffer("_PlanetScorchCells",cells);material.SetInt("_PlanetScorchActive",0);
        }
        static RenderTexture Atlas(string name){var rt=new RenderTexture(new RenderTextureDescriptor(Size,Size){graphicsFormat=GraphicsFormat.R8G8B8A8_UNorm,depthBufferBits=0,dimension=TextureDimension.Tex2DArray,volumeDepth=6,enableRandomWrite=true,msaaSamples=1,useMipMap=false}){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};rt.Create();return rt;}
        static Vector3[] TileCentres(){var values=new Vector3[6*Grid*Grid];for(int f=0;f<6;f++)for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++)values[f*Grid*Grid+y*Grid+x]=CubeSphere.Direction(new PlanetNavCoordinate(0,(CubeFace)f,x,y),Grid);return values;}
        public void Paint(Vector3 direction,float radius,float planetRadius)
        {
            direction.Normalize();radius=Mathf.Max(1,radius);var right=Vector3.Cross(Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up,direction).normalized;
            shader.SetVector("_Impact",new Vector4(direction.x,direction.y,direction.z,radius));shader.SetVector("_ImpactRight",right);shader.SetFloat("_ScorchRadius",planetRadius);
            // The normalized cube map is Lipschitz with constant one on each face.
            // 4/Grid safely encloses a tile, plus the farthest corner of the stamp.
            float reach=Mathf.Min(2,radius*1.42f/planetRadius+4f/Grid),reach2=reach*reach;
            int kernel=shader.FindKernel("Paint");
            for(int f=0;f<6;f++){
                int minX=Grid,minY=Grid,maxX=-1,maxY=-1;
                for(int y=0;y<Grid;y++)for(int x=0;x<Grid;x++)if((centres[f*Grid*Grid+y*Grid+x]-direction).sqrMagnitude<=reach2){minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y);}
                if(maxX<0)continue;
                shader.SetInts("_ScorchRect",minX*Block,minY*Block,(maxX-minX+1)*Block,(maxY-minY+1)*Block);shader.SetInt("_ScorchFace",f);shader.Dispatch(kernel,(maxX-minX+1)*Block/8,(maxY-minY+1)*Block/8,1);
                for(int y=minY*4;y<(maxY+1)*4;y++)for(int x=minX*4;x<(maxX+1)*4;x++)painted[f*CellResolution*CellResolution+y*CellResolution+x]=1;
            }
            cells.SetData(painted);Count++;material.SetInt("_PlanetScorchActive",1);
        }
        public RenderTexture ColourForValidation=>colour;
        public RenderTexture ReliefForValidation=>relief;
        public void Dispose(){material.SetInt("_PlanetScorchActive",0);cells.Dispose();colour.Release();relief.Release();ArmyLifetime.Destroy(colour);ArmyLifetime.Destroy(relief);ArmyLifetime.Destroy(shader);}
    }
}
