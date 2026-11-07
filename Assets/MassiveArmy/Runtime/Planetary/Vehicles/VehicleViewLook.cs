using UnityEngine;

namespace MassiveArmy.Planetary
{
    /// <summary>Seat aim and temporary head rotation are independent. Releasing Alt recentres only the head.</summary>
    public sealed class VehicleViewLook
    {
        public float AimYaw {get;private set;}
        public float AimPitch {get;private set;}
        public float HeadYaw {get;private set;}
        public float HeadPitch {get;private set;}
        public Quaternion ForwardRotation=>Quaternion.Euler(AimPitch,AimYaw,0);
        public Quaternion ViewRotation=>ForwardRotation*Quaternion.Euler(HeadPitch,HeadYaw,0);
        public void Reset(){AimYaw=AimPitch=HeadYaw=HeadPitch=0;}
        public void Step(Vector2 mouse,bool freeLook,bool canAim,float yawLimit,float pitchLimit,float dt){
            if(freeLook){HeadYaw=Mathf.Clamp(HeadYaw+mouse.x*.12f,-120,120);HeadPitch=Mathf.Clamp(HeadPitch-mouse.y*.12f,-75,75);}
            else {
                float blend=1-Mathf.Exp(-20*Mathf.Max(0,dt));HeadYaw=Mathf.Lerp(HeadYaw,0,blend);HeadPitch=Mathf.Lerp(HeadPitch,0,blend);
                if(Mathf.Abs(HeadYaw)<.01f)HeadYaw=0;if(Mathf.Abs(HeadPitch)<.01f)HeadPitch=0;
                if(canAim){AimYaw=Mathf.Clamp(AimYaw+mouse.x*.12f,-yawLimit,yawLimit);AimPitch=Mathf.Clamp(AimPitch-mouse.y*.12f,-pitchLimit,pitchLimit);}
            }
        }
    }
}
