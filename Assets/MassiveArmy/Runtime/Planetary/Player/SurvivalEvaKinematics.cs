using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal static class SurvivalEvaKinematics {
        // Keep orbital carrier motion separate from bounded suit thrust.
        internal static void Step(ref Double3 position,ref Double3 carrier,ref Double3 suit,Vector3 look,Vector3 up,Vector3 controls,bool boost,float dt,double mu,double speed=10,double boostSpeed=30,double acceleration=8){
            var right=Vector3.Cross(up,look).normalized;if(right.sqrMagnitude<.01f)right=Vector3.Cross(Math.Abs(up.y)>.9?Vector3.right:Vector3.up,up).normalized;
            var desired=Vector3.ClampMagnitude(look*controls.y+right*controls.x+up*controls.z,1)*(float)(boost?boostSpeed:speed);
            suit=new Double3(Vector3.MoveTowards(suit.Float,desired,(float)(acceleration*dt)));
            double radius=position.Magnitude;carrier-=position*(mu/Math.Max(1,radius*radius*radius)*dt);position+=(carrier+suit)*dt;
        }
    }
}
