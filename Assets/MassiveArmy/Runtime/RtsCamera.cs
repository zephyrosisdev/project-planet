using UnityEngine;
using UnityEngine.InputSystem;

namespace MassiveArmy
{
    public sealed class RtsCamera : MonoBehaviour
    {
        public Battlefield Field;
        public bool InputEnabled=true;
        public bool Spectator;
        public bool PointerInputEnabled=true;
        public bool PanInputEnabled=true;
        public float Sensitivity=1;
        public Vector3 Focus=new Vector3(0,0,0);
        float distance=1400,yaw=0,pitch=55;
        float halfHeight=240;
        float shakeStart,shakeUntil,shakeStrength;
        public void ShakeFromBlast(Vector2 position,float width)
        {
            float range=Mathf.Max(400,width*5),d=Field!=null?Field.Delta(new Vector2(Focus.x,Focus.z),position).magnitude:Vector2.Distance(new Vector2(Focus.x,Focus.z),position);
            float strength=Mathf.Clamp01(1-d/range)*Mathf.Min(2.5f,width*.015f);if(strength<=0)return;
            if(Time.unscaledTime>=shakeUntil)shakeStrength=0;
            shakeStart=Time.unscaledTime;shakeUntil=shakeStart+2.4f;shakeStrength=Mathf.Max(strength,shakeStrength*.5f);
        }
        public Vector3 Nearest(Vector3 p)=>Field!=null?Field.RenderPosition(p,Focus):p;
        Vector2 previousMouse;
        Camera view;
        void Awake() { view=GetComponent<Camera>();view.nearClipPlane=.5f;view.farClipPlane=7000;view.fieldOfView=48;view.orthographic=true;view.orthographicSize=halfHeight;Place(); }
        public void FocusSector(Vector2 location) { location=Field!=null?Field.Canonical(location):location;Focus=new Vector3(location.x,Field!=null?Field.Height(location):0,location.y);Place(); }
        public void FocusBase(Vector2 location) { halfHeight=110;pitch=55;FocusSector(location); }
        float MaxHalfHeight {get{view??=GetComponent<Camera>();return Field!=null?(Spectator?Field.Size*.5f/Mathf.Min(1,view.aspect):Field.Size*.20f/Mathf.Max(1,view.aspect)):480;}}
        public void FullMap(){if(Field==null)return;halfHeight=Field.Size*.5f;pitch=55;FocusSector(Vector2.zero);}
        public void ResetOverview(){halfHeight=Mathf.Min(240,MaxHalfHeight);yaw=0;pitch=55;FocusSector(Field!=null?Field.StartPosition(0):Vector2.zero);}
        // Called before simulation classification and drawing, so every subsystem shares the same camera image.
        public void UpdateForFrame()
        {
            if(!InputEnabled || !Application.isFocused){UpdateShakeOnly();return;}
            var k=Keyboard.current;var m=Mouse.current;if(k==null || m==null){UpdateShakeOnly();return;}
            float dt=Time.unscaledDeltaTime,speed=halfHeight*1.5f*Sensitivity;
            Vector2 move=Vector2.zero;
            if(k.wKey.isPressed || k.upArrowKey.isPressed)move.y++;
            if(k.sKey.isPressed || k.downArrowKey.isPressed)move.y--;
            if(k.aKey.isPressed || k.leftArrowKey.isPressed)move.x--;
            if(k.dKey.isPressed || k.rightArrowKey.isPressed)move.x++;
            if(k.qKey.isPressed)yaw-=dt*55;if(k.eKey.isPressed)yaw+=dt*55;
            Vector2 mouse=m.position.ReadValue();
            if(m.middleButton.isPressed&&PanInputEnabled)
            {
                Vector2 delta=mouse-previousMouse;move-=delta*.008f/Mathf.Max(dt,.001f);
            }
            previousMouse=mouse;
            var rotation=Quaternion.Euler(0,yaw,0);
            Focus+=rotation*new Vector3(move.x,0,move.y)*speed*dt;
            if(Field!=null){Vector2 wrapped=Field.Canonical(new Vector2(Focus.x,Focus.z));Focus.x=wrapped.x;Focus.z=wrapped.y;Focus.y=Field.Height(wrapped);}
            // Current Input System defaults to one unit per notch, rather than Windows' native 120.
            float wheel=PointerInputEnabled?m.scroll.ReadValue().y:0;
            if(InputSystem.settings.scrollDeltaBehavior!=InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms &&
                (Application.platform==RuntimePlatform.WindowsPlayer || Application.platform==RuntimePlatform.WindowsEditor))wheel/=120;
            halfHeight=Mathf.Clamp(halfHeight*Mathf.Exp(-wheel*.15f*Sensitivity),24,MaxHalfHeight);
            Place();
        }
        void UpdateShakeOnly(){if(shakeUntil<=0)return;Place();if(Time.unscaledTime>=shakeUntil)shakeUntil=0;}
        void Place() { view??=GetComponent<Camera>();view.orthographic=true;view.orthographicSize=Mathf.Min(halfHeight,MaxHalfHeight);
            // Keep the entire orthographic near plane above terrain at large footprints.
            float radians=pitch*Mathf.Deg2Rad;
            distance=Mathf.Max(1400,(view.orthographicSize*Mathf.Cos(radians)+Mathf.Max(0,(Field?.MaximumHeight??0)-Focus.y)+128)/Mathf.Sin(radians));
            if(Spectator)view.farClipPlane=Mathf.Max(7000,distance*2+view.orthographicSize*2);
            transform.rotation=Quaternion.Euler(pitch,yaw,0);transform.position=Focus-transform.forward*distance;
            if(Time.unscaledTime<shakeUntil){float t=Time.unscaledTime-shakeStart,envelope=Mathf.Pow(Mathf.Clamp01((shakeUntil-Time.unscaledTime)/2.4f),2);
                transform.position+=(transform.right*Mathf.Sin(t*43)+transform.up*Mathf.Sin(t*57+1))*shakeStrength*envelope;}}
        public bool GroundPoint(Vector2 screen,out Vector2 point)
        {
            Ray ray=view.ScreenPointToRay(screen);point=Vector2.zero;
            if(ray.direction.y>=-.01f) return false;
            float t=-ray.origin.y/ray.direction.y;
            for(int i=0;i<8;i++)
            {
                Vector3 p=ray.GetPoint(t);float h=Field!=null?Field.Height(new Vector2(p.x,p.z)):0;
                t=(h-ray.origin.y)/ray.direction.y;
            }
            Vector3 hit=ray.GetPoint(t);point=new Vector2(hit.x,hit.z);
            if(Field!=null)point=Field.Canonical(point);return t>0;
        }
    }
}
