using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public struct OrbitHoldForecast {
        public bool enabled;public int node;public double radius,acceleration;public Vector3 heading,right;public Double3 planeNormal;
    }
    public sealed partial class PlanetSpacecraft {
        public bool OrbitalVelocityHoldEnabled {get;private set;}
        public double OrbitalVelocityHoldRadius {get;private set;}
        int orbitHoldNode=-1;bool orbitHoldBoost;double orbitHoldBodyRadius;Double3 orbitHoldNormal;
        public bool OrbitalVelocityAssistActive {get;private set;}
        public string OrbitalVelocityAssistStatus {get;private set;}="";
        public double OrbitalVelocityTargetSpeed {get;private set;}
        public const double OrbitalVelocityAssistAcceleration=40;
        public const double OrbitalVelocityAssistBoostAcceleration=160;
        public void ToggleOrbitalVelocityHold(){
            if(OrbitalVelocityHoldEnabled){StopOrbitalVelocityHold();return;}
            if(CruiseActive){OrbitalVelocityAssistStatus="Release warp before enabling orbit hold";return;}
            if(!OrbitReference(out var radial,out var velocity,out double bodyRadius,out double mu,out _))return;
            var tangent=CircularVelocityTarget(radial,velocity,pose.rotation*Vector3.forward,pose.rotation*Vector3.right,mu);orbitHoldNormal=Double3.Cross(radial,tangent);orbitHoldNormal*=1/Math.Max(1e-12,orbitHoldNormal.Magnitude);
            OrbitalVelocityHoldRadius=radial.Magnitude;orbitHoldBodyRadius=bodyRadius;orbitHoldNode=instrumentPlanet;OrbitalVelocityHoldEnabled=true;
            SetFlightAssistance(false);nextOrbitRefresh=0;galaxyPrediction?.Invalidate();
        }
        void StopOrbitalVelocityHold(){if(!OrbitalVelocityHoldEnabled)return;OrbitalVelocityHoldEnabled=false;OrbitalVelocityAssistActive=false;OrbitalVelocityHoldRadius=0;nextOrbitRefresh=0;galaxyPrediction?.Invalidate();}
        internal OrbitHoldForecast OrbitHoldPrediction=>new OrbitHoldForecast{enabled=OrbitalVelocityHoldEnabled&&OrbitalVelocityAssistActive,node=orbitHoldNode,radius=OrbitalVelocityHoldRadius,acceleration=(orbitHoldBoost?OrbitalVelocityAssistBoostAcceleration:OrbitalVelocityAssistAcceleration)*ShipThrustScale,heading=pose.rotation*Vector3.forward,right=pose.rotation*Vector3.right,planeNormal=orbitHoldNormal};
        // Planet-relative circular velocity: preserve the current orbital plane and
        // direction, removing radial motion. From rest/radial flight use hull heading.
        internal static Double3 CircularVelocityTarget(Double3 radial,Double3 velocity,Vector3 heading,Vector3 right,double mu){
            double radius=radial.Magnitude;if(radius<=0||mu<=0)return new Double3();
            var up=radial*(1/radius);var tangent=velocity-up*CruiseDot(velocity,up);
            if(tangent.Magnitude<.1){tangent=new Double3(heading)-up*CruiseDot(new Double3(heading),up);if(tangent.Magnitude<.1)tangent=new Double3(right)-up*CruiseDot(new Double3(right),up);}
            return tangent*(Math.Sqrt(mu/radius)/Math.Max(1e-12,tangent.Magnitude));
        }
        internal static Double3 OrbitHoldTarget(Double3 radial,Double3 velocity,Vector3 heading,Vector3 right,double mu,double heldRadius,Double3 planeNormal=default){
            var target=CircularVelocityTarget(radial,velocity,heading,right,mu);
            if(planeNormal.Magnitude>.5){var tangent=Double3.Cross(planeNormal,radial);target=tangent*(Math.Sqrt(mu/radial.Magnitude)/Math.Max(1e-12,tangent.Magnitude));target-=planeNormal*Math.Max(-100,Math.Min(100,CruiseDot(radial,planeNormal)*.1));}
            if(heldRadius>0){double r=radial.Magnitude;double climb=Math.Max(-100,Math.Min(100,(heldRadius-r)*.1));target+=radial*(climb/Math.Max(1,r));}
            return target;
        }
        internal static Double3 OrbitVelocityImpulse(Double3 target,Double3 velocity,double acceleration,double dt){var error=target-velocity;double length=error.Magnitude;return error*(Math.Min(length,acceleration*dt)/Math.Max(length,1e-12));}
        bool OrbitReference(out Double3 radial,out Double3 relativeVelocity,out double radius,out double mu,out string name){
            radial=relativeVelocity=new Double3();radius=mu=0;name="";
            if(Landed||Docked||Docking||AutoFlightActive){OrbitalVelocityAssistStatus="Unavailable while landed, docking or in automatic flight";return false;}
            FlightReference(out radial,out relativeVelocity,out radius,out name);
            var body=SimulationDefinition;mu=body.gravitationalParameter;var model=FlightGalaxy;var bodies=simulationOnly?simulationBodies:world?.SystemRuntime?.Bodies;
            if(model!=null&&instrumentPlanet>=0){var node=model.Nodes[instrumentPlanet];mu=node.mu;if(node.planet>=0&&bodies!=null&&node.planet<bodies.Length)body=bodies[node.planet];}
            double altitude=radial.Magnitude-radius;
            double minimum=Math.Max(body.ReliefBound+20,body.hasAtmosphere?body.atmosphereHeight:0);
            if(altitude<=minimum||radial.Magnitude>radius*21||mu<=0){OrbitalVelocityAssistStatus=altitude<=minimum?$"Climb above {Distance(minimum)} around {name}":"Move into planetary orbital range";return false;}return true;
        }
        void ApplyOrbitalVelocityAssist(float dt,bool boost,ref Vector3 thrust){
            EndCruise();if(!OrbitReference(out var radial,out var relativeVelocity,out _,out double mu,out string name)){StopOrbitalVelocityHold();return;}
            if(OrbitalVelocityHoldEnabled&&instrumentPlanet!=orbitHoldNode){StopOrbitalVelocityHold();OrbitalVelocityAssistStatus="Orbit reference changed · press N to acquire this planet";return;}
            var target=OrbitHoldTarget(radial,relativeVelocity,pose.rotation*Vector3.forward,pose.rotation*Vector3.right,mu,OrbitalVelocityHoldEnabled?OrbitalVelocityHoldRadius:0,OrbitalVelocityHoldEnabled?orbitHoldNormal:default);
            var error=target-relativeVelocity;double remaining=error.Magnitude;
            if(FlightAssistEnabled)SetFlightAssistance(false);
            // Ordinary bounded thruster impulse; no teleport or instantaneous speed set.
            double acceleration=(simulationOnly?(boost?simulationLimits.boostAcceleration:simulationLimits.acceleration):(boost?OrbitalVelocityAssistBoostAcceleration:OrbitalVelocityAssistAcceleration))*ShipThrustScale;
            pose.linearVelocity+=OrbitVelocityImpulse(target,relativeVelocity,acceleration,dt);orbitHoldBoost=boost;
            thrust=Vector3.zero;OrbitalVelocityAssistActive=true;OrbitalVelocityTargetSpeed=target.Magnitude;
            OrbitalVelocityAssistStatus=$"{name} · {(boost?"BOOST · ":"")}target {FormatFlightSpeed(OrbitalVelocityTargetSpeed)} · Δv {FormatFlightSpeed(remaining)}"+(OrbitalVelocityHoldEnabled?$"\nHOLD ALT {Distance(OrbitalVelocityHoldRadius-orbitHoldBodyRadius)}":"");
        }
    }
}
