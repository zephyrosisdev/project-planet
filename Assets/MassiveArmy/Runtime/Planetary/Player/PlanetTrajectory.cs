using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        const int ArcSamples=160;
        readonly Vector4[] arcPoints=new Vector4[ArcSamples];GraphicsBuffer arcBuffer;Material arcMaterial;
        Double3 arcAnchor;float nextArcRefresh;int arcCount;
        public bool ShowTrajectory=true;
        internal int TrajectoryPointsForValidation=>arcCount;
        void InitializeTrajectory(){arcMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTrajectory"));arcBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,ArcSamples,16);}
        void UpdateTrajectory(){var path=PlanetOrbit.Calculate(pose.position,pose.linearVelocity,world.definition.gravitationalParameter);arcCount=0;arcAnchor=pose.position;
            if(!path.Valid||Landed)return;
            double start=path.CurrentAnomaly+Math.Min(.02,Math.Max(80,Speed*.5)/path.Radius),end=path.Bound?path.CurrentAnomaly+Math.PI*2-.02:Math.Acos(-1/Math.Max(1,path.Eccentricity))-.0001;
            if(end<=start)return;
            for(int i=0;i<ArcSamples;i++){double angle=start+(end-start)*i/(ArcSamples-1);var p=path.PositionAt(angle);double radius=p.Magnitude;if(double.IsNaN(radius)||double.IsInfinity(radius)||radius<1)break;if(i>0&&radius<=world.definition.radius+Math.Max(surface.SampleHeight(p.Float.normalized),world.definition.oceanHeight)+(GearDeployed?8:5))break;var relative=(p-arcAnchor).Float;arcPoints[arcCount++]=new Vector4(relative.x,relative.y,relative.z,1);}
            if(arcCount>1)arcBuffer.SetData(arcPoints,0,0,arcCount);
        }
        public void RenderTrajectory(){if(!active||!ShowTrajectory||FlightAssistEnabled)return;if(Time.unscaledTime>=nextArcRefresh){UpdateTrajectory();nextArcRefresh=Time.unscaledTime+.2f;}if(arcCount<2)return;
            arcMaterial.SetBuffer("_ArcPoints",arcBuffer);arcMaterial.SetVector("_ArcOrigin",origin.RenderPosition(world.definition.position.Add(arcAnchor)));arcMaterial.SetMatrix("_ArcVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);arcMaterial.SetVector("_ArcScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,arcCount));
            int gates=0;while((2<<gates)<arcCount)gates++;
            Graphics.DrawProcedural(arcMaterial,new Bounds(Vector3.zero,Vector3.one*Mathf.Max(10000,(float)pose.position.Magnitude*8)),MeshTopology.Triangles,(arcCount-1)*6+gates*24*6,1,camera,null,ShadowCastingMode.Off,false);
        }
        void DisposeTrajectory(){arcBuffer?.Dispose();ArmyLifetime.Destroy(arcMaterial);}
    }
}
