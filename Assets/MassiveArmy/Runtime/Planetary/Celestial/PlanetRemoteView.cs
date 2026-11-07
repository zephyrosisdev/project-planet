using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSystemRuntime {
        PlanetDevelopment physicalWorld;int remotePending=-1;Double3 physicalOffset;
        internal bool PreparationFailed(int index)=>failed[index];
        public PlanetDevelopment PhysicalWorld=>physicalWorld??ActiveWorld;
        public bool IsRemoteView=>physicalWorld!=null&&ActiveWorld!=physicalWorld;
        bool CanView(int index){if(worlds[index]==PhysicalWorld)return true;var colony=Civilisation?.Colony(Civilisation.BodyId(Bodies[index].bodyId),0);return colony!=null&&colony.installations.Exists(b=>b.definition=="command");}
        public bool CycleSettlement(int step){
            if(ActiveWorld==null||remotePending>=0)return false;
            int current=Array.IndexOf(worlds,ActiveWorld);
            for(int n=1;n<Bodies.Length;n++){int index=(current+n*(step<0?-1:1)+Bodies.Length)%Bodies.Length;if(!CanView(index))continue;return ViewSettlement(index);}return false;
        }
        public bool ViewSettlement(int index){
            if(index<0||index>=Bodies.Length||!CanView(index))return false;
            if(worlds[index]==ActiveWorld)return true;
            remotePending=index;PreparePlanet(index);return true;
        }
        void UpdateRemoteView(){
            if(remotePending>=0){int index=remotePending;if(failed[index])remotePending=-1;else if(worlds[index]?.Ready==true){
                if(physicalWorld==null)physicalOffset=ActiveWorld.ObserverPosition.RelativeTo(ActiveWorld.definition.position);
                physicalWorld=PhysicalWorld;var previous=ActiveWorld;var next=worlds[index];previous.Base?.CancelPlacement();previous.SetResidentActive(false);ActiveWorld=next;next.CopyRemoteClock(previous);next.SetResidentActive(true);
                if(next!=physicalWorld)next.EnterRemoteRts();else {next.ResumePhysicalView();physicalWorld=null;}
                remotePending=-1;
            }}
            if(IsRemoteView)physicalWorld.FollowCelestialFrame(physicalOffset);
            var keyboard=Keyboard.current;if(ActiveWorld?.Ready!=true||!Application.isFocused||ActiveWorld.RemoteInputBlocked||keyboard==null)return;
            if(keyboard.leftBracketKey.wasPressedThisFrame)CycleSettlement(-1);else if(keyboard.rightBracketKey.wasPressedThisFrame)CycleSettlement(1);
        }
    }
    public sealed partial class PlanetDevelopment {
        Vector3? TerrainDetailFocus {get {
            if(modes?.Mode==PlayerControlMode.RTS){var up=rig.FocusDirection.normalized;return up*(definition.radius+Mathf.Max(definition.oceanHeight,surface.SampleHeight(up))+Mathf.Min(24,rig.Distance*.025f));}
            if(view!=null&&surface!=null&&(IsPiloting||IsDriving||IsFirstPerson&&player?.AttachedToShip==true)&&PlanetTerrainGaze.TryFocus(surface,RenderToBody(view.transform.position),view.transform.forward,out var focus))return focus;
            return null;
        }}
        internal bool RemoteInputBlocked=>hud?.TextFocused==true||hud?.ModalOpen==true;
        internal bool RemoteView=>SystemRuntime?.IsRemoteView==true;
        internal void CopyRemoteClock(PlanetDevelopment other){Paused=other.Paused;simulationSpeedIndex=other.simulationSpeedIndex;}
        internal void EnterRemoteRts(){player?.SetActive(false);modes.Switch(PlayerControlMode.RTS);var command=Base?.Buildings.Find(b=>b.team==0&&!b.removed&&b.definition.id=="command");if(command!=null)rig.SetFocusDirection(CubeSphere.Decode(command.position));rig.RefreshPose();Base?.Rebase();}
        internal void ResumePhysicalView(){if(IsDriving)groundVehicles?.Resume();else if(IsPiloting)spacecraft?.SetActive(true);else if(IsFirstPerson)player?.Resume();else rig.RefreshPose();Base?.Rebase();}
    }
}
