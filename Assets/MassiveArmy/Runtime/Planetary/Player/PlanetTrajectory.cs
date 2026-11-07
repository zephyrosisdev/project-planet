using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        const int ArcSamples=160,ArcSubdivisions=4;
        readonly Vector4[] arcPoints=new Vector4[ArcSamples];GraphicsBuffer arcBuffer;Material arcMaterial;
        Double3 arcAnchor;float nextArcRefresh;int arcCount,arcVertices;
        UniversePosition arcUniverseAnchor;
        int arcFrameNode=-1;Double3 arcFrameOffset;
        const int TrailCapacity=48;
        readonly Double3[] markerTrail=new Double3[TrailCapacity];readonly Vector4[] markerTrailData=new Vector4[TrailCapacity];GraphicsBuffer markerTrailBuffer;
        double markerTravel;float markerSpacing=100,markerFirst=100;int markerTrailCount,markerFrame=-2;Double3 previousMarkerPosition;
        void UploadArc(){float distance=0;for(int i=0;i<arcCount;i++){if(i>0)distance+=Vector3.Distance((Vector3)arcPoints[i-1],(Vector3)arcPoints[i]);arcPoints[i].w=distance;}
            var model=world.SystemRuntime?.Galaxy;var current=model!=null&&arcFrameNode>=0?world.definition.position.Add(pose.position).RelativeTo(model.Origin.Add(model.Current[arcFrameNode].position)):pose.position;
            float spacing=(float)Math.Pow(10,Math.Ceiling(Math.Log10(Math.Max(100,Speed))));double moved=(current-previousMarkerPosition).Magnitude;
            if(markerFrame!=arcFrameNode||spacing!=markerSpacing||moved>Math.Max(10000,Speed)){markerTrailCount=0;markerTravel=0;moved=0;}
            markerSpacing=spacing;markerFrame=arcFrameNode;
            if(moved>0){double first=markerSpacing-markerTravel%markerSpacing;int steps=(int)Math.Min(TrailCapacity,Math.Floor((moved-first)/markerSpacing)+1);
                for(int k=0;k<steps;k++){if(markerTrailCount<TrailCapacity)markerTrailCount++;for(int j=markerTrailCount-1;j>0;j--)markerTrail[j]=markerTrail[j-1];markerTrail[0]=previousMarkerPosition+(current-previousMarkerPosition)*((first+k*markerSpacing)/moved);}
                markerTravel+=moved;}
            previousMarkerPosition=current;markerFirst=markerSpacing-(float)(markerTravel%markerSpacing);
            for(int i=0;i<markerTrailCount;i++){var p=(markerTrail[i]-(model!=null?arcFrameOffset:arcAnchor)).Float;markerTrailData[i]=new Vector4(p.x,p.y,p.z,1);}
            if(markerTrailCount>0)markerTrailBuffer.SetData(markerTrailData,0,0,markerTrailCount);arcBuffer.SetData(arcPoints,0,0,arcCount);
        }
        public bool ShowTrajectory=true;
        internal int TrajectoryPointsForValidation=>arcCount;
        internal UniversePosition TrajectoryAnchorForValidation=>arcUniverseAnchor;
        internal int TrajectoryFrameForValidation=>arcFrameNode;
        void InitializeTrajectory(){markerTrailBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,TrailCapacity,16);InitializeLocalOrbit();arcMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTrajectory"));arcBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,ArcSamples,16);}
        void UpdateTrajectory(){var prediction=Prediction();arcCount=0;arcAnchor=pose.position;arcUniverseAnchor=world.definition.position.Add(arcAnchor);
            if(prediction!=null){if(prediction.Count<2||Landed)return;arcUniverseAnchor=prediction.Anchor.Add(prediction.Positions[0]);
                var model=world.SystemRuntime.Galaxy;var numericalStart=prediction.Positions[0]-prediction.PrimaryPositions[0];arcFrameNode=prediction.FrameNode;arcFrameOffset=numericalStart;arcUniverseAnchor=model.Origin.Add(model.Current[arcFrameNode].position+numericalStart);
                for(int i=0;i<prediction.Count;i++){var p=(prediction.Positions[i]-prediction.PrimaryPositions[i]-numericalStart).Float;arcPoints[arcCount++]=new Vector4(p.x,p.y,p.z,1);}UploadArc();return;}
            var path=PlanetOrbit.Calculate(pose.position,pose.linearVelocity,world.definition.gravitationalParameter);
            if(!path.Valid||Landed)return;
            double start=path.CurrentAnomaly+Math.Min(.02,Math.Max(80,Speed*.5)/path.Radius),end=path.Bound?path.CurrentAnomaly+Math.PI*2-.02:Math.Acos(-1/Math.Max(1,path.Eccentricity))-.0001;
            if(end<=start)return;
            for(int i=0;i<ArcSamples;i++){double angle=start+(end-start)*i/(ArcSamples-1);var p=path.PositionAt(angle);double radius=p.Magnitude;if(double.IsNaN(radius)||double.IsInfinity(radius)||radius<1)break;if(i>0&&radius<=world.definition.radius+Math.Max(surface.SampleHeight(p.Float.normalized),world.definition.oceanHeight)+(GearDeployed?8:5))break;var relative=(p-arcAnchor).Float;arcPoints[arcCount++]=new Vector4(relative.x,relative.y,relative.z,1);}
            if(arcCount>1)UploadArc();
        }
        public void RenderTrajectory(){arcVertices=0;if(!active||!ShowTrajectory)return;if(Time.unscaledTime>=nextArcRefresh){UpdateTrajectory();nextArcRefresh=Time.unscaledTime+.2f;}if(arcCount<2)return;
            var movingGalaxy=world.SystemRuntime?.Galaxy;if(movingGalaxy!=null&&arcFrameNode>=0)arcUniverseAnchor=movingGalaxy.Origin.Add(movingGalaxy.Current[arcFrameNode].position+arcFrameOffset);
            arcMaterial.SetColor("_ArcColour",new Color(.08f,1,.25f));arcMaterial.SetInteger("_ArcSubdivisions",ArcSubdivisions);arcMaterial.SetBuffer("_ArcPoints",arcBuffer);arcMaterial.SetVector("_ArcOrigin",origin.RenderPosition(world.SystemRuntime?.Galaxy!=null?arcUniverseAnchor:world.definition.position.Add(arcAnchor)));arcMaterial.SetMatrix("_ArcVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);arcMaterial.SetVector("_ArcScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,arcCount));
            arcMaterial.SetVector("_ArcMarkers",new Vector4(markerSpacing,markerFirst,Mathf.Clamp(markerSpacing*.12f,6,60),0));arcMaterial.SetVector("_ArcMarkerUp",camera.transform.up);arcMaterial.SetBuffer("_ArcTrail",markerTrailBuffer);arcMaterial.SetInteger("_ArcTrailCount",markerTrailCount);
            arcVertices=(arcCount-1)*ArcSubdivisions*6+(48+markerTrailCount)*2*6;
        }
        internal void DrawFlightInstruments(CommandBuffer cmd){if(!active)return;DrawCelestialGuides(cmd);DrawLocalOrbit(cmd);if(arcVertices>0)cmd.DrawProcedural(Matrix4x4.identity,arcMaterial,0,MeshTopology.Triangles,arcVertices);if(landingVertices>0)cmd.DrawProcedural(Matrix4x4.identity,landingMaterial,0,MeshTopology.Triangles,landingVertices);}
        void DisposeTrajectory(){DisposeCelestialGuides();DisposeLocalOrbit();markerTrailBuffer?.Dispose();arcBuffer?.Dispose();ArmyLifetime.Destroy(arcMaterial);}
    }
}
