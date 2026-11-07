using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class PlanetAtmosphericTides {
        // Presentation-scale approximation, not a coupled atmospheric solver.
        internal static Vector4 GravityAxis(PlanetDevelopment world,PlanetDefinition body){
            var model=world.SystemRuntime?.Galaxy;if(model==null)return new Vector4(0,1,0,0);
            int own=model.NodeFor(body);var centre=body.position.RelativeTo(model.Origin);double best=0;Double3 axis=new Double3(0,1,0);
            for(int i=0;i<model.Nodes.Length;i++){var source=model.Nodes[i];if(i==own||source.kind==CelestialKind.Barycentre||source.mu<=0)continue;var delta=model.Current[i].position-centre;double r=Math.Max(source.radius,delta.Magnitude),force=source.mu/Math.Max(1,r*r*r);if(force>best){best=force;axis=delta*(1/Math.Max(1,r));}}
            double self=body.gravitationalParameter/Math.Pow(Math.Max(1,body.radius),3);return new Vector4((float)axis.x,(float)axis.y,(float)axis.z,(float)Math.Min(1,best/Math.Max(1e-20,self*.00001)));
        }
        internal static float Multiplier(PlanetWeather weather,Vector3 up,Vector3 sun,Vector4 gravity){
            if(weather==null)return 1;float s=Vector3.Dot(up,sun),g=Vector3.Dot(up,new Vector3(gravity.x,gravity.y,gravity.z));return Mathf.Clamp(1+weather.airSolarTide*(1.5f*s*s-.5f)+weather.airGravityTide*gravity.w*(1.5f*g*g-.5f),.8f,1.2f);
        }
    }
}
