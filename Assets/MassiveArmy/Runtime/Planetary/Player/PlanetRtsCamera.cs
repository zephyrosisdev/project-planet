using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Planetary
{
    public sealed class PlanetRtsCamera : MonoBehaviour
    {
        public PlanetSurface Surface {get;private set;}
        public FloatingOrigin Origin {get;private set;}
        public Vector3 FocusDirection=Vector3.forward;
        public float Distance=180,Pitch=55,Sensitivity=1;
        public bool InputEnabled=true;
        Vector3 tangent=Vector3.up;
        Vector2 previousMouse;
        Camera view;
        public void Initialize(PlanetSurface surface,FloatingOrigin origin){Surface=surface;Origin=origin;view=GetComponent<Camera>();view.orthographic=false;view.fieldOfView=48;Place();}
        public void SetFocusDirection(Vector3 direction){var next=direction.normalized;tangent=Quaternion.FromToRotation(FocusDirection,next)*tangent;FocusDirection=next;}
        public void Step()
        {
            var k=Keyboard.current;var m=Mouse.current;float dt=Time.unscaledDeltaTime;
            if(InputEnabled&&k!=null&&m!=null&&Application.isFocused){Vector2 move=Vector2.zero;
                if(k.wKey.isPressed)move.y++;if(k.sKey.isPressed)move.y--;if(k.dKey.isPressed)move.x++;if(k.aKey.isPressed)move.x--;
                var pointer=m.position.ReadValue();if(m.middleButton.isPressed)move-=(pointer-previousMouse)*.005f/Mathf.Max(dt,.001f);previousMouse=pointer;
                float wheel=m.scroll.ReadValue().y;if(InputSystem.settings.scrollDeltaBehavior!=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms)wheel/=120;
                Distance=Mathf.Clamp(Distance*Mathf.Exp(-wheel*.15f*Sensitivity),8,Surface.Definition.radius*12);
                float yaw=(k.qKey.isPressed?-1:0)+(k.eKey.isPressed?1:0);tangent=Quaternion.AngleAxis(yaw*dt*55,FocusDirection)*tangent;
                var right=Vector3.Cross(FocusDirection,tangent).normalized;var delta=(tangent*move.y+right*move.x)*(Distance*.6f*dt*Sensitivity/Surface.Definition.radius);
                if(delta.sqrMagnitude>0){var next=(FocusDirection+delta).normalized;var transport=Quaternion.FromToRotation(FocusDirection,next);tangent=transport*tangent;FocusDirection=next;}
            }
            Place();
        }
        public void RefreshPose()=>Place();
        void Place()
        {
            var up=FocusDirection.normalized;tangent=Vector3.ProjectOnPlane(tangent,up).normalized;
            if(tangent.sqrMagnitude<.5f){CubeSphere.Project(up,out var face,out _);tangent=Vector3.ProjectOnPlane(CubeSphere.Up[(int)face],up).normalized;}
            var rotation=Quaternion.LookRotation(tangent,up)*Quaternion.Euler(Pitch,0,0);
            Double3 local=Surface.GetSurfacePositionPrecise(up)-new Double3(rotation*Vector3.forward)*Distance;
            var observer=Surface.Definition.position.Add(local);Origin.Anchor(observer);
            transform.SetPositionAndRotation(Vector3.zero,rotation);
            view.nearClipPlane=Mathf.Max(.1f,Distance*.0001f);view.farClipPlane=Mathf.Max(30000,Surface.Definition.radius*16);
        }
        public bool GroundPoint(Vector2 screen,out Vector3 direction)
        {
            var ray=view.ScreenPointToRay(screen);ray.origin-=Origin.RenderPosition(Surface.Definition.position);
            bool found=Surface.Raycast(ray,out var hit);direction=found?hit.normalized:default;return found;
        }
    }
}
