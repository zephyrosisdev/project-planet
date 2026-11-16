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
        public bool orbitHold,orbitHoldBoost,orbitAssistActive;public int orbitHoldNode=-1;public double orbitHoldRadius,orbitHoldBodyRadius,orbitTargetSpeed;public Double3 orbitHoldNormal;public string orbitAssistStatus="";
        public double heldClearance;public Double3 cruiseReturnVelocity,autoStart,autoEnd;
        public Quaternion autoRotation,autoEndRotation;public float autoElapsed,autoDuration;
        public bool recallActive;public int recallStage;public Double3 recallDestination;public Quaternion recallRotation;public double recallCruiseRadius;public float recallSpeed,recallMaxSpeed;
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
            orbitHold=OrbitalVelocityHoldEnabled,orbitHoldRadius=OrbitalVelocityHoldRadius,orbitHoldBodyRadius=orbitHoldBodyRadius,orbitHoldNode=orbitHoldNode,orbitHoldNormal=orbitHoldNormal,orbitHoldBoost=orbitHoldBoost,orbitAssistActive=OrbitalVelocityAssistActive,orbitTargetSpeed=OrbitalVelocityTargetSpeed,orbitAssistStatus=OrbitalVelocityAssistStatus,
            pose=pose,angularVelocity=angularVelocity,lockNormal=lockNormal,lockOffset=lockOffset,gear=GearDeployed,landed=Landed,assist=FlightAssistEnabled,rotationLocked=RotationLocked,
            heldClearance=heldClearance,wasTerrainFlight=wasTerrainFlight,cruiseActive=CruiseActive,cruiseInhibited=cruiseInhibited,cruiseReturnVelocity=cruiseReturnVelocity,
            autoFlight=AutoFlightActive,autoTakeoff=autoTakeoff,autoStart=autoStart,autoEnd=autoEnd,autoRotation=autoRotation,autoEndRotation=autoEndRotation,autoElapsed=autoElapsed,autoDuration=autoDuration,
            recallActive=recallActive,recallStage=recallStage,recallDestination=recallDestination,recallRotation=recallRotation,recallCruiseRadius=recallCruiseRadius,recallSpeed=recallSpeed,recallMaxSpeed=recallMaxSpeed
        };
        internal void RestoreSimulation(PlanetShipSimulationSnapshot value){
            pose=value.pose;angularVelocity=value.angularVelocity;lockNormal=value.lockNormal;lockOffset=value.lockOffset;GearDeployed=value.gear;Landed=value.landed;FlightAssistEnabled=value.assist;RotationLocked=value.rotationLocked;
            heldClearance=value.heldClearance;wasTerrainFlight=value.wasTerrainFlight;CruiseActive=value.cruiseActive;cruiseInhibited=value.cruiseInhibited;cruiseReturnVelocity=value.cruiseReturnVelocity;
            AutoFlightActive=value.autoFlight;autoTakeoff=value.autoTakeoff;autoStart=value.autoStart;autoEnd=value.autoEnd;autoRotation=value.autoRotation;autoEndRotation=value.autoEndRotation;autoElapsed=value.autoElapsed;autoDuration=value.autoDuration;
            recallActive=value.recallActive;recallStage=value.recallStage;recallDestination=value.recallDestination;recallRotation=value.recallRotation;recallCruiseRadius=value.recallCruiseRadius;recallSpeed=value.recallSpeed;recallMaxSpeed=value.recallMaxSpeed;
            dockStation=0;OrbitalVelocityHoldEnabled=value.orbitHold;OrbitalVelocityHoldRadius=value.orbitHoldRadius;orbitHoldBodyRadius=value.orbitHoldBodyRadius;orbitHoldNode=value.orbitHoldNode;orbitHoldNormal=value.orbitHoldNormal;orbitHoldBoost=value.orbitHoldBoost;OrbitalVelocityAssistActive=value.orbitAssistActive;OrbitalVelocityTargetSpeed=value.orbitTargetSpeed;OrbitalVelocityAssistStatus=value.orbitAssistStatus??"";shipFrame.Pose=pose;
        }
        internal void StepSimulation(float dt,Vector3 thrust,Vector3 turn,bool brake,bool boost,bool toggleAssist,bool toggleGear,bool land,bool cruise=false,bool matchOrbit=false,bool toggleLock=false,bool toggleOrbit=false){
            if(!simulationOnly)throw new InvalidOperationException("Use the live controller for a rendered ship");
            assembly?.UpdatePower();if(!CockpitPowered){thrust=Vector3.zero;turn=Vector3.zero;land=toggleAssist=toggleGear=false;}
            if(toggleLock&&CockpitPowered)ToggleRotationLock();
            if(toggleOrbit&&CockpitPowered&&EnginePower>0)ToggleOrbitalVelocityHold();
            if(toggleAssist)SetFlightAssistance(!FlightAssistEnabled);if(toggleGear&&!Landed&&!AutoFlightActive)GearDeployed=!GearDeployed;
            if(land){RefreshLandingPreview(true);TryLandingOrTakeoff();}
            if(EnginePower<=0)thrust=Vector3.zero;turn*=ThrusterPower;
            int count=Math.Max(1,Mathf.CeilToInt(dt/.05f));for(int i=0;i<count;i++){
                if(recallActive&&!RecallPowered)CancelRecall("RECALL STOPPED · NO POWER");
                if(recallActive&&recallStage<3)StepRecall(dt/count);else Integrate(dt/count,thrust,turn,brake,boost,cruise,matchOrbit);
                if(recallActive&&recallStage==3&&!AutoFlightActive)recallActive=false;
            }
        }
    }
}
