using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetSpacecraft
    {
        GalaxyPrediction galaxyPrediction;int predictionFrame=-1;
        internal GalaxyPrediction PredictionForValidation=>galaxyPrediction;
        Double3 HudVelocity(){var model=world.SystemRuntime?.Galaxy;return model!=null&&galaxyPrediction!=null&&galaxyPrediction.Count>0?pose.linearVelocity+world.definition.velocity-model.Current[galaxyPrediction.FrameNode].velocity:pose.linearVelocity;}
        GalaxyPrediction Prediction(){var model=world.SystemRuntime?.Galaxy;if(model==null)return null;
            if(galaxyPrediction==null)galaxyPrediction=new GalaxyPrediction(model);
            if(predictionFrame!=Time.frameCount){predictionFrame=Time.frameCount;galaxyPrediction.Tick(world.definition,pose,Landed);}
            return galaxyPrediction;
        }
        bool RefreshGalaxyOrbit(){var model=world.SystemRuntime?.Galaxy;if(model==null)return false;var path=Prediction();
            var absolute=model.Current[model.NodeFor(world.definition)].position+pose.position;
            int primary=model.Strongest(absolute,model.Current,out double share,out int second);var a=model.Nodes[primary];var b=model.Nodes[second];
            bool stale=path.Count>0&&model.Time-path.Timestamp>Math.Max(30,path.Horizon*.15);
            orbitStatus=CruiseActive?$"K · WARP {CruiseSpeedReadout} · RELEASE TO RETURN":Landed?"LANDED":stale?"PREDICTION AGING · RECALCULATING":path.Status;
            if(!CruiseActive&&!Landed&&FlightAssistEnabled)orbitStatus="ASSISTED · COAST PREVIEW";
            orbitColor=Landed?Color.gray:path.Impact?new Color(1,.4f,.2f):new Color(.25f,1,.75f);
            var r=absolute-model.Current[primary].position;var v=pose.linearVelocity+world.definition.velocity-model.Current[primary].velocity;
            orbitReadout=$"PRIMARY   {a.name} ({share*100:0}% influence)\nSECONDARY {b.name}\nALTITUDE  {Distance(pose.position.Magnitude-world.definition.radius)}\nSPEED     {SpeedReadout} (planet frame)\nL · ATTITUDE HOLD {(RotationLocked?"ON":"OFF")}\nFORECAST  {Duration(path.Horizon)}\nSummed gravity · moving bodies\nFinite forecast; long-term stability unproven.";
            if(path.Count==0)orbitReadout+="\nBuilding numerical prediction…";
            // The numerical track is plotted in the current camera's orbital-plane projection.
            mapExtent=Math.Max(a.radius*2,r.Magnitude*1.3);
            if(share<.95)mapExtent=Math.Max(mapExtent,(model.Current[second].position-model.Current[primary].position).Magnitude*1.3);
            if(path.Count>1)for(int i=0;i<path.Count;i++)mapExtent=Math.Max(mapExtent,Math.Min(r.Magnitude*6,(path.Positions[i]-path.PrimaryPositions[i]).Magnitude)*1.05);
            return true;
        }
        bool DrawGalaxyOrbitMap(){var model=world.SystemRuntime?.Galaxy;if(model==null)return false;var path=Prediction();
            mapDrawing.Lines.Clear();peLabel.style.display=apLabel.style.display=DisplayStyle.None;
            var absolute=model.Current[model.NodeFor(world.definition)].position+pose.position;int primary=model.Strongest(absolute,model.Current,out _,out int second);
            var centerBody=model.Current[path.Count>0?path.FrameNode:primary].position;var r=absolute-centerBody;
            var x=r.Float.normalized;var relativeVelocity=pose.linearVelocity+world.definition.velocity-model.Current[primary].velocity;
            var normal=Double3.Cross(r,relativeVelocity).Float.normalized;if(normal.sqrMagnitude<.5f)normal=camera.transform.up;
            var y=Vector3.Cross(normal,x).normalized;var center=new Vector2(167,140);const float size=126;
            Vector2 Project(Double3 p){p=p-centerBody;return center+new Vector2(Vector3.Dot(p.Float,x),-Vector3.Dot(p.Float,y))*(float)(size/Math.Max(1,mapExtent));}
            for(int i=0;i<model.Nodes.Length;i++){
                var body=model.Nodes[i];if(body.kind==CelestialKind.Barycentre)continue;
                var p=Project(model.Current[i].position);if((p-center).magnitude>size)continue;
                float radius=Mathf.Max(2,(float)(body.radius/mapExtent)*size);var colour=body.kind==CelestialKind.Planet?new Color(.3f,.6f,1):body.colour;
                for(int k=0;k<24;k++){float a=k*Mathf.PI/12,b=(k+1)*Mathf.PI/12;MapLine(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,colour);}
                if(i==primary||i==second){var label=i==primary?peLabel:apLabel;label.text=body.name;label.style.display=DisplayStyle.Flex;label.style.left=p.x+5;label.style.top=p.y+5;label.style.fontSize=10;}
            }
            if(path.Count>1)for(int i=1;i<path.Count;i++){var a=Project(centerBody+path.Positions[i-1]-path.PrimaryPositions[i-1]);var b=Project(centerBody+path.Positions[i]-path.PrimaryPositions[i]);if((a-center).magnitude<size&&(b-center).magnitude<size)MapLine(a,b,orbitColor);}
            MapDot(Project(absolute),Color.white);mapDrawing.MarkDirtyRepaint();return true;
        }
    }
}
