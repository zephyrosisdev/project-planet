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
        OrbitalAssetState followedOrbital;CivilisationRuntime orbitalSimulation;
        public void Initialize(PlanetSurface surface,FloatingOrigin origin){Surface=surface;Origin=origin;view=GetComponent<Camera>();view.orthographic=false;view.fieldOfView=48;Place();}
        void Aim(Vector3 direction){var next=direction.normalized;tangent=Quaternion.FromToRotation(FocusDirection,next)*tangent;FocusDirection=next;}
        public void SetFocusDirection(Vector3 direction){followedOrbital=null;orbitalSimulation=null;Aim(direction);}
        public void FollowOrbital(OrbitalAssetState asset,CivilisationRuntime simulation){followedOrbital=asset;orbitalSimulation=simulation;Distance=asset.kind==OrbitalPayloadKind.SurveySatellite?120:600;Pitch=30;Place();}
        public void Step(bool pointerOverUi=false)
        {
            var k=Keyboard.current;var m=Mouse.current;float dt=Time.unscaledDeltaTime;
            if(InputEnabled&&k!=null&&m!=null&&Application.isFocused){Vector2 move=Vector2.zero;
                if(k.wKey.isPressed)move.y++;if(k.sKey.isPressed)move.y--;if(k.dKey.isPressed)move.x++;if(k.aKey.isPressed)move.x--;
                var pointer=m.position.ReadValue();if(!pointerOverUi&&m.middleButton.isPressed)move-=(pointer-previousMouse)*.005f/Mathf.Max(dt,.001f);previousMouse=pointer;
                if(move.sqrMagnitude>0&&followedOrbital!=null){followedOrbital=null;orbitalSimulation=null;Distance=180;Pitch=55;}
                float wheel=pointerOverUi?0:m.scroll.ReadValue().y;if(InputSystem.settings.scrollDeltaBehavior!=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms)wheel/=120;
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
            bool following=followedOrbital!=null&&orbitalSimulation!=null;
            Double3 focus=following?followedOrbital.Pose(orbitalSimulation.RenderTime).position:default;
            if(following)Aim(focus.Float.normalized);
            var up=FocusDirection.normalized;tangent=Vector3.ProjectOnPlane(tangent,up).normalized;
            if(tangent.sqrMagnitude<.5f){CubeSphere.Project(up,out var face,out _);tangent=Vector3.ProjectOnPlane(CubeSphere.Up[(int)face],up).normalized;}
            var rotation=Quaternion.LookRotation(tangent,up)*Quaternion.Euler(Pitch,0,0);
            var target=following?focus:new Double3(up)*(Surface.Definition.radius+(double)Mathf.Max(Surface.Definition.oceanHeight,Surface.SampleHeight(up)));
            Double3 local=target-new Double3(rotation*Vector3.forward)*Distance;
            if(!following){
                // Map navigation can land over deep ocean or on the uphill side of a ridge.
                // Keep the observer above the visible surface, then continue aiming at the focus.
                var cameraUp=local.Float.normalized;
                double minimum=Surface.Definition.radius+(double)Mathf.Max(Surface.Definition.oceanHeight,Surface.SampleHeight(cameraUp))+2;
                if(local.Magnitude<minimum){local=new Double3(cameraUp)*minimum;rotation=Quaternion.LookRotation((target-local).Float,up);}
            }
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
