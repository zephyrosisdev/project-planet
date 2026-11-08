#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        static readonly float[] ProbeAvoidAngles={30,-30,60,-60,90,-90,120,-120,180};
        internal Vector2 ProbeWalkingControls(FramePose goal){
            if(predicted==null||predicted.ship!=null||predicted.vehicle!=null)return Vector2.zero;
            var position=SurvivalVehicleSimulation.Position(predicted.pose);var up=PlanetPrecisionPolicy.Direction(position);var desired=Vector3.ProjectOnPlane((SurvivalVehicleSimulation.Position(goal)-position).Float,up).normalized;
            var forward=SurvivalMovement.Tangent(SurvivalMovement.Forward(predicted),up);var right=Vector3.Cross(up,forward).normalized;var surface=new PlanetSurface(ClientSimulationDefinition);double start=predicted.pose.Distance(goal),speed=latest.self.dose>=50?4:latest.self.dose>=25?6:Config.walkSpeed;
            Vector2 Controls(Vector3 direction)=>new Vector2(Vector3.Dot(direction,right),Vector3.Dot(direction,forward));
            double Progress(Vector2 control){var probe=predicted.Copy();for(int step=0;step<10;step++)StepWalking(probe,new SurvivalInput{sequence=(uint)(step+1),x=control.x,y=control.y},surface,1f/Config.tickRate,speed,false);return start-probe.pose.Distance(goal);}
            var selected=Controls(desired);double best=Progress(selected);if(best>speed/Config.tickRate*8)return selected;
            foreach(float angle in ProbeAvoidAngles){var control=Controls(Quaternion.AngleAxis(angle,up)*desired);double progress=Progress(control);if(progress>best+.02){best=progress;selected=control;}}
            return selected;
        }
    }
}
#endif
