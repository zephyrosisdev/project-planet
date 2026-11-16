using System;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    // Replaying hundreds of already-confirmed fixed substeps is unnecessary. This
    // gate only retains the future of a matching acknowledged owner ship state;
    // it never relaxes server input validation or matching-tick snap distances.
    internal static class SurvivalPredictionReuse {
        static bool Near(double a,double b,double tolerance=.001)=>Math.Abs(a-b)<=tolerance;
        static bool Near(Double3 a,Double3 b,double tolerance=.001)=>(a-b).Magnitude<=tolerance;
        static bool Near(Vector3 a,Vector3 b,float tolerance=.00001f)=>(a-b).sqrMagnitude<=tolerance*tolerance;
        static bool Near(Quaternion a,Quaternion b){double minus=0,plus=0;for(int i=0;i<4;i++){double x=a[i]-b[i],y=a[i]+b[i];minus+=x*x;plus+=y*y;}return Math.Min(minus,plus)<=1e-10;}
        internal static bool CanRetain(SurvivalMotion expected,SurvivalMotion authoritative,SurvivalMotion current,int pending){
            if(pending<=0||!SurvivalClientPresentation.SameFrame(expected,authoritative)||!SurvivalClientPresentation.SameFrame(current,authoritative)||expected.actor!=authoritative.actor||current.actor!=authoritative.actor||expected.sequence!=authoritative.sequence||current.sequence<=authoritative.sequence||expected.tick!=authoritative.tick||current.tick!=authoritative.tick+pending||expected.vehicle!=null||authoritative.vehicle!=null||expected.ship==null||authoritative.ship==null||current.ship==null)return false;
            var a=expected.ship;var b=authoritative.ship;
            if(a.owner!=authoritative.actor||b.owner!=authoritative.actor||a.pose.body!=b.pose.body||a.pose.frame!=b.pose.frame||a.pilot!=b.pilot||a.owner!=b.owner||a.landed!=b.landed||a.autoFlight||b.autoFlight||current.ship.autoFlight||a.dockedStation!=b.dockedStation||a.rearOpen!=b.rearOpen||a.innerOpen!=b.innerOpen||a.recalling!=b.recalling||!Near(a.collisionRadius,b.collisionRadius)||string.IsNullOrEmpty(a.physics)||string.IsNullOrEmpty(b.physics))return false;
            if(expected.grounded!=authoritative.grounded||!Near(SurvivalVehicleSimulation.Position(expected.pose),SurvivalVehicleSimulation.Position(authoritative.pose))||!Near(expected.pose.yaw,authoritative.pose.yaw)||!Near(expected.pitch,authoritative.pitch)||!Near(expected.viewYaw,authoritative.viewYaw)||!Near(expected.verticalSpeed,authoritative.verticalSpeed)||!Near(expected.walkVelocityX,authoritative.walkVelocityX)||!Near(expected.walkVelocityY,authoritative.walkVelocityY)||!Near(expected.walkVelocityZ,authoritative.walkVelocityZ)||!Near(new Vector3(expected.forwardX,expected.forwardY,expected.forwardZ),new Vector3(authoritative.forwardX,authoritative.forwardY,authoritative.forwardZ)))return false;
            if(!Near(SurvivalVehicleSimulation.Position(a.pose),SurvivalVehicleSimulation.Position(b.pose))||!Near(new Double3(a.velocityX,a.velocityY,a.velocityZ),new Double3(b.velocityX,b.velocityY,b.velocityZ))||!Near(SurvivalShipInteriorModel.Rotation(a),SurvivalShipInteriorModel.Rotation(b)))return false;
            var predicted=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(a.physics);var server=JsonUtility.FromJson<PlanetShipSimulationSnapshot>(b.physics);
            if(predicted==null||server==null||predicted.pose.frameId!=server.pose.frameId||!Near(predicted.pose.position,server.pose.position)||!Near(predicted.pose.linearVelocity,server.pose.linearVelocity)||!Near(predicted.pose.rotation,server.pose.rotation)||!Near(predicted.pose.angularVelocity,server.pose.angularVelocity)||!Near(predicted.angularVelocity,server.angularVelocity))return false;
            predicted.pose=server.pose;predicted.angularVelocity=server.angularVelocity;
            if(predicted.assist){if(!Near(predicted.heldClearance,server.heldClearance))return false;}predicted.heldClearance=server.heldClearance;
            if(predicted.rotationLocked){if(!Near(predicted.lockNormal,server.lockNormal)||!Near(predicted.lockOffset,server.lockOffset))return false;}predicted.lockNormal=server.lockNormal;predicted.lockOffset=server.lockOffset;
            if(predicted.orbitHold){if(!Near(predicted.orbitHoldRadius,server.orbitHoldRadius)||!Near(predicted.orbitHoldBodyRadius,server.orbitHoldBodyRadius)||!Near(predicted.orbitHoldNormal,server.orbitHoldNormal,.000001))return false;}
            predicted.orbitHoldRadius=server.orbitHoldRadius;predicted.orbitHoldBodyRadius=server.orbitHoldBodyRadius;predicted.orbitHoldNormal=server.orbitHoldNormal;
            // Inactive path data and HUD text do not advance the integrator.
            if(!predicted.autoFlight){predicted.autoStart=server.autoStart;predicted.autoEnd=server.autoEnd;predicted.autoRotation=server.autoRotation;predicted.autoEndRotation=server.autoEndRotation;predicted.autoElapsed=server.autoElapsed;predicted.autoDuration=server.autoDuration;}
            if(!predicted.cruiseActive)predicted.cruiseReturnVelocity=server.cruiseReturnVelocity;
            predicted.orbitTargetSpeed=server.orbitTargetSpeed;predicted.orbitAssistStatus=server.orbitAssistStatus;
            return JsonUtility.ToJson(predicted)==JsonUtility.ToJson(server);
        }
    }
}
