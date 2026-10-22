using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        const int LocalOrbitSamples=192;
        readonly Vector4[] localOrbitPoints=new Vector4[LocalOrbitSamples];GraphicsBuffer localOrbitBuffer;Material localOrbitMaterial;int localOrbitCount;float nextLocalOrbit;Double3 localOrbitStart;int localOrbitPlanet=-1;
        internal int LocalOrbitPointsForValidation=>localOrbitCount;
        void InitializeLocalOrbit(){localOrbitBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,LocalOrbitSamples,16);localOrbitMaterial=new Material(Resources.Load<Shader>("MassiveArmy/PlanetTrajectory"));}
        void DrawLocalOrbit(CommandBuffer cmd){if(Landed||CruiseActive||!ShowTrajectory)return;var model=world.SystemRuntime?.Galaxy;
            if(Time.unscaledTime>=nextLocalOrbit){nextLocalOrbit=Time.unscaledTime+.5f;localOrbitCount=0;FlightReference(out var radial,out var velocity,out double radius,out _);localOrbitPlanet=instrumentPlanet;if(radial.Magnitude>radius*20||radial.Magnitude<radius+(world.definition.atmosphereHeight))return;
                double mu=model!=null&&localOrbitPlanet>=0?model.Nodes[localOrbitPlanet].mu:world.definition.gravitationalParameter;var conic=PlanetOrbit.Calculate(radial,velocity,mu);if(!conic.Valid)return;localOrbitStart=radial;
                double end=conic.Bound?conic.CurrentAnomaly+Math.PI*2:Math.Acos(-1/Math.Max(1,conic.Eccentricity))-.001;
                for(int i=0;i<LocalOrbitSamples;i++){var p=conic.PositionAt(conic.CurrentAnomaly+(end-conic.CurrentAnomaly)*i/(LocalOrbitSamples-1));if(p.Magnitude<=radius||p.Magnitude>Math.Max(radius*100,radial.Magnitude*30))break;var offset=(p-radial).Float;localOrbitPoints[localOrbitCount++]=new Vector4(offset.x,offset.y,offset.z,1);}
                if(localOrbitCount>1)localOrbitBuffer.SetData(localOrbitPoints,0,0,localOrbitCount);
            }
            if(localOrbitCount<2)return;var anchor=model!=null&&localOrbitPlanet>=0?model.Origin.Add(model.Current[localOrbitPlanet].position+localOrbitStart):world.definition.position.Add(localOrbitStart);
            localOrbitMaterial.SetColor("_ArcColour",new Color(.15f,.6f,1));localOrbitMaterial.SetInteger("_ArcSubdivisions",4);localOrbitMaterial.SetBuffer("_ArcPoints",localOrbitBuffer);localOrbitMaterial.SetVector("_ArcOrigin",origin.RenderPosition(anchor));localOrbitMaterial.SetMatrix("_ArcVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);localOrbitMaterial.SetVector("_ArcScreen",new Vector4(camera.pixelWidth,camera.pixelHeight,camera.nearClipPlane,localOrbitCount));cmd.DrawProcedural(Matrix4x4.identity,localOrbitMaterial,0,MeshTopology.Triangles,(localOrbitCount-1)*4*6);
        }
        void DisposeLocalOrbit(){localOrbitBuffer?.Dispose();ArmyLifetime.Destroy(localOrbitMaterial);}
    }
}
