using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        const int ArcSamples=160;
        readonly Vector4[] arcPoints=new Vector4[ArcSamples];GraphicsBuffer arcBuffer;Material arcMaterial;
        Double3 arcAnchor;float nextArcRefresh;int arcCount,arcVertices;
        UniversePosition arcUniverseAnchor;
        int arcFrameNode=-1;Double3 arcFrameOffset;
        public bool ShowTrajectory=true;
        internal int TrajectoryPointsForValidation=>arcCount;
        internal UniversePosition TrajectoryAnchorForValidation=>arcUniverseAnchor;
        internal int TrajectoryFrameForValidation=>arcFrameNode;
        void InitializeTrajectory(){arcMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTrajectory"));arcBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,ArcSamples,16);}
        void UpdateTrajectory(){var prediction=Prediction();arcCount=0;arcAnchor=pose.position;arcUniverseAnchor=world.definition.position.Add(arcAnchor);
            if(prediction!=null){if(prediction.Count<2||Landed)return;arcUniverseAnchor=prediction.Anchor.Add(prediction.Positions[0]);
                var model=world.SystemRuntime.Galaxy;var numericalStart=prediction.Positions[0]-prediction.PrimaryPositions[0];arcFrameNode=prediction.FrameNode;arcFrameOffset=numericalStart;arcUniverseAnchor=model.Origin.Add(model.Current[arcFrameNode].position+numericalStart);
                for(int i=0;i<prediction.Count;i++){var p=(prediction.Positions[i]-prediction.PrimaryPositions[i]-numericalStart).Float;arcPoints[arcCount++]=new Vector4(p.x,p.y,p.z,1);}arcBuffer.SetData(arcPoints,0,0,arcCount);return;}
            var path=PlanetOrbit.Calculate(pose.position,pose.linearVelocity,world.definition.gravitationalParameter);
            if(!path.Valid||Landed)return;
            double start=path.CurrentAnomaly+Math.Min(.02,Math.Max(80,Speed*.5)/path.Radius),end=path.Bound?path.CurrentAnomaly+Math.PI*2-.02:Math.Acos(-1/Math.Max(1,path.Eccentricity))-.0001;
            if(end<=start)return;
            for(int i=0;i<ArcSamples;i++){double angle=start+(end-start)*i/(ArcSamples-1);var p=path.PositionAt(angle);double radius=p.Magnitude;if(double.IsNaN(radius)||double.IsInfinity(radius)||radius<1)break;if(i>0&&radius<=world.definition.radius+Math.Max(surface.SampleHeight(p.Float.normalized),world.definition.oceanHeight)+(GearDeployed?8:5))break;var relative=(p-arcAnchor).Float;arcPoints[arcCount++]=new Vector4(relative.x,relative.y,relative.z,1);}
            if(arcCount>1)arcBuffer.SetData(arcPoints,0,0,arcCount);
        }
        public void RenderTrajectory(){arcVertices=0;if(!active||!ShowTrajectory||FlightAssistEnabled)return;if(Time.unscaledTime>=nextArcRefresh){UpdateTrajectory();nextArcRefresh=Time.unscaledTime+.2f;}if(arcCount<2)return;
            var movingGalaxy=world.SystemRuntime?.Galaxy;if(movingGalaxy!=null&&arcFrameNode>=0)arcUniverseAnchor=movingGalaxy.Origin.Add(movingGalaxy.Current[arcFrameNode].position+arcFrameOffset);
            arcMaterial.SetBuffer("_ArcPoints",arcBuffer);arcMaterial.SetVector("_ArcOrigin",origin.RenderPosition(world.SystemRuntime?.Galaxy!=null?arcUniverseAnchor:world.definition.position.Add(arcAnchor)));arcMaterial.SetMatrix("_ArcVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);arcMaterial.SetVector("_ArcScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,arcCount));
            int gates=0;while((2<<gates)<arcCount)gates++;
            arcVertices=(arcCount-1)*6+gates*24*6;
        }
        internal void DrawFlightInstruments(CommandBuffer cmd){if(!active)return;if(arcVertices>0)cmd.DrawProcedural(Matrix4x4.identity,arcMaterial,0,MeshTopology.Triangles,arcVertices);if(landingVertices>0)cmd.DrawProcedural(Matrix4x4.identity,landingMaterial,0,MeshTopology.Triangles,landingVertices);}
        void DisposeTrajectory(){arcBuffer?.Dispose();ArmyLifetime.Destroy(arcMaterial);}
    }
}
