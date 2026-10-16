using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MassiveArmy
{
    public enum PointerPhase { Down, Move, Up }
    public struct RtsPointerInput
    {
        public PointerPhase type;
        public int button;
        // Top-left pixels, matching the old gesture coordinate contract.
        public Vector2 mousePosition;
        public bool shift, control;
    }
    public sealed class RtsUiInput : IDisposable
    {
        readonly Queue<RtsPointerInput> pending=new Queue<RtsPointerInput>();
        readonly Action<RtsPointerInput> dispatch;readonly Action cancelGesture;
        bool left,right,middle;
        static int owners;static bool previousMerging;
        Vector2 position;
        public RtsUiInput(Action<RtsPointerInput> dispatch,Action cancelGesture=null)
        {
            this.dispatch=dispatch;this.cancelGesture=cancelGesture;if(owners++==0){previousMerging=InputSystem.settings.disableRedundantEventsMerging;InputSystem.settings.disableRedundantEventsMerging=true;}InputSystem.onEvent+=OnEvent;InputSystem.onDeviceChange+=OnDeviceChange;
        }
        void Enqueue(PointerPhase phase,int button,Vector2 p)
        {
            var keyboard=Keyboard.current;
            pending.Enqueue(new RtsPointerInput{type=phase,button=button,mousePosition=new Vector2(p.x,Screen.height-p.y),
                shift=keyboard?.shiftKey.isPressed??false,control=keyboard?.ctrlKey.isPressed??false});
        }
        void OnDeviceChange(InputDevice device,InputDeviceChange change)
        {
            if(!(device is Mouse))return;
            if(change==InputDeviceChange.SoftReset||change==InputDeviceChange.HardReset||change==InputDeviceChange.Disabled||change==InputDeviceChange.Disconnected||change==InputDeviceChange.Removed)
            {left=right=middle=false;pending.Clear();cancelGesture?.Invoke();}
        }
        void OnEvent(InputEventPtr input,InputDevice device)
        {
            if(!(device is Mouse mouse)||(!input.IsA<StateEvent>()&&!input.IsA<DeltaStateEvent>()))return;
            Vector2 p=position;
            if(mouse.position.ReadValueFromEvent(input,out Vector2 next))p=next;
            if(p!=position&&(left||right))Enqueue(PointerPhase.Move,right?1:0,p);
            if(mouse.leftButton.ReadValueFromEvent(input,out float l))
            {
                bool pressed=l>.5f;if(pressed!=left){Enqueue(pressed?PointerPhase.Down:PointerPhase.Up,0,p);left=pressed;}
            }
            if(mouse.rightButton.ReadValueFromEvent(input,out float r))
            {
                bool pressed=r>.5f;if(pressed!=right){Enqueue(pressed?PointerPhase.Down:PointerPhase.Up,1,p);right=pressed;}
            }
            if(mouse.middleButton.ReadValueFromEvent(input,out float m)){bool pressed=m>.5f;if(pressed!=middle){Enqueue(pressed?PointerPhase.Down:PointerPhase.Up,2,p);middle=pressed;}}
            position=p;
        }
        public void Drain(bool enabled)
        {
            while(pending.Count>0){var input=pending.Dequeue();if(enabled)dispatch(input);}
        }
        public void Clear(){pending.Clear();}
        public void Dispose(){InputSystem.onEvent-=OnEvent;InputSystem.onDeviceChange-=OnDeviceChange;pending.Clear();if(--owners==0)InputSystem.settings.disableRedundantEventsMerging=previousMerging;}
    }
}
