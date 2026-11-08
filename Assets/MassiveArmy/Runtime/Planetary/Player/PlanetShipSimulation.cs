using System;
using UnityEngine;
using MassiveArmy.Industrial;

namespace MassiveArmy.Planetary {
    internal sealed class PlanetShipSimulationLimits {
        internal float assistSpeed=120,assistBoost=360,acceleration=40,boostAcceleration=160,assistAcceleration=80,assistBraking=160;internal double warpSpeed=100000000;
    }
    [Serializable] public sealed class PlanetShipSimulationSnapshot {
        public SpacePose pose;public Vector3 angularVelocity,lockNormal;public Quaternion lockOffset;
        public bool gear,landed,assist,rotationLocked,wasTerrainFlight,cruiseActive,cruiseInhibited,autoFlight,autoTakeoff;
        public double heldClearance;public Double3 cruiseReturnVelocity,autoStart,autoEnd;
        public Quaternion autoRotation,autoEndRotation;public float autoElapsed,autoDuration;
    }
    public sealed partial class PlanetSpacecraft {
        readonly bool simulationOnly;readonly PlanetDefinition simulationDefinition;readonly GalaxyModel simulationGalaxy;readonly PlanetDefinition[] simulationBodies;
        GalaxyModel FlightGalaxy=>simulationOnly?simulationGalaxy:world?.SystemRuntime?.Galaxy;
        readonly Func<Double3,Double3,Quaternion,bool> simulationHullSweep;
        readonly Func<Double3,Double3,float,double> simulationCelestialSweep;
        readonly PlanetShipSimulationLimits simulationLimits;
        PlanetDefinition SimulationDefinition=>simulationDefinition!=null?simulationDefinition:world.definition;
        // Reuse the live ship integrators without constructing any visual or GPU resource.
        internal PlanetSpacecraft(PlanetDefinition definition,ShipAssembly modules,Func<Double3,Double3,Quaternion,bool> sweep,GalaxyModel galaxy=null,PlanetDefinition[] bodies=null,Func<Double3,Double3,float,double> celestialSweep=null,PlanetShipSimulationLimits limits=null){
            simulationOnly=true;simulationDefinition=definition;simulationGalaxy=galaxy;simulationBodies=bodies;simulationHullSweep=sweep??throw new ArgumentNullException(nameof(sweep));
            simulationCelestialSweep=celestialSweep;
            simulationLimits=limits??new PlanetShipSimulationLimits();
            surface=new PlanetSurface(definition);frames=new ReferenceFrameManager{Origin=definition.position};
            pose=new SpacePose{rotation=Quaternion.identity};shipFrame=new ReferenceFrame(1,0,FrameKind.Ship,pose);frames.Register(shipFrame);assembly=modules;assembly?.UpdatePower();active=true;
        }
        internal PlanetShipSimulationSnapshot CaptureSimulation()=>new PlanetShipSimulationSnapshot{
            pose=pose,angularVelocity=angularVelocity,lockNormal=lockNormal,lockOffset=lockOffset,gear=GearDeployed,landed=Landed,assist=FlightAssistEnabled,rotationLocked=RotationLocked,
            heldClearance=heldClearance,wasTerrainFlight=wasTerrainFlight,cruiseActive=CruiseActive,cruiseInhibited=cruiseInhibited,cruiseReturnVelocity=cruiseReturnVelocity,
            autoFlight=AutoFlightActive,autoTakeoff=autoTakeoff,autoStart=autoStart,autoEnd=autoEnd,autoRotation=autoRotation,autoEndRotation=autoEndRotation,autoElapsed=autoElapsed,autoDuration=autoDuration
        };
        internal void RestoreSimulation(PlanetShipSimulationSnapshot value){
            pose=value.pose;angularVelocity=value.angularVelocity;lockNormal=value.lockNormal;lockOffset=value.lockOffset;GearDeployed=value.gear;Landed=value.landed;FlightAssistEnabled=value.assist;RotationLocked=value.rotationLocked;
            heldClearance=value.heldClearance;wasTerrainFlight=value.wasTerrainFlight;CruiseActive=value.cruiseActive;cruiseInhibited=value.cruiseInhibited;cruiseReturnVelocity=value.cruiseReturnVelocity;
            AutoFlightActive=value.autoFlight;autoTakeoff=value.autoTakeoff;autoStart=value.autoStart;autoEnd=value.autoEnd;autoRotation=value.autoRotation;autoEndRotation=value.autoEndRotation;autoElapsed=value.autoElapsed;autoDuration=value.autoDuration;
            dockStation=0;OrbitalVelocityHoldEnabled=false;shipFrame.Pose=pose;
        }
        internal void StepSimulation(float dt,Vector3 thrust,Vector3 turn,bool brake,bool boost,bool toggleAssist,bool toggleGear,bool land,bool cruise=false,bool matchOrbit=false){
            if(!simulationOnly)throw new InvalidOperationException("Use the live controller for a rendered ship");
            assembly?.UpdatePower();if(!CockpitPowered){thrust=Vector3.zero;turn=Vector3.zero;land=toggleAssist=toggleGear=false;}
            if(toggleAssist)SetFlightAssistance(!FlightAssistEnabled);if(toggleGear&&!Landed&&!AutoFlightActive)GearDeployed=!GearDeployed;
            if(land){RefreshLandingPreview(true);TryLandingOrTakeoff();}
            if(EnginePower<=0)thrust=Vector3.zero;turn*=ThrusterPower;
            int count=Math.Max(1,Mathf.CeilToInt(dt/.05f));for(int i=0;i<count;i++)Integrate(dt/count,thrust,turn,brake,boost,cruise,matchOrbit);
        }
    }
}
