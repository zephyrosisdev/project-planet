using System;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetSpacecraft {
        long dockStation;float dockProgress;Vector3 dockStart;Quaternion dockRotation;
        public bool Docked=>dockStation!=0&&dockProgress>=1;public bool Docking=>dockStation!=0&&dockProgress<1;
        public long DockedStationId=>dockStation;
        public string DockingStatus {
            get {if(world.Orbitals==null)return "";if(dockStation!=0)return (Docked?"DOCKED · J TO UNDOCK · F TO WALK":"DOCKING · "+(dockProgress*100).ToString("0")+"%");var v=world.Orbitals.Nearest(pose.position,out double distance);if(v==null||distance>200000)return "";double relative=(pose.linearVelocity-v.Pose.linearVelocity).Magnitude;return $"{v.State.Name} #{v.State.id}\nRANGE {distance/1000:0.00} km · RELATIVE {relative:0.0} m/s\nASSIST: B matches orbital velocity\nJ: dock inside forward hangar (<25 m/s)";}
        }
        public bool ToggleDock(){
            if(dockStation!=0){var old=world.Orbitals?.Find(dockStation);dockStation=0;dockProgress=0;SetFlightAssistance(false);if(old!=null){pose.linearVelocity=old.Pose.linearVelocity+new Double3(old.Rotation*Vector3.back)*12;pose.angularVelocity=old.Pose.angularVelocity;shipFrame.Pose=pose;}return true;}
            if(Landed||AutoFlightActive||world.Orbitals==null)return false;var station=world.Orbitals.Nearest(pose.position,out double distance);
            if(station==null||station.State.faction!=0||distance>250||(pose.linearVelocity-station.Pose.linearVelocity).Magnitude>25)return false;
            var p=station.BodyToLocal(pose.position);if(Mathf.Abs(p.x)>26||p.y<-1||p.y>18||p.z<-220||p.z>40)return false;
            var end=station.LocalToBody(station.DockPosition);Physics.SyncTransforms();if(!HullSweep(pose.position,end,station.Rotation)||!HullSweep(pose.position,end,pose.rotation))return false;
            GearDeployed=true;dockStation=station.State.id;dockStart=p;dockRotation=Quaternion.Inverse(station.Rotation)*pose.rotation;dockProgress=0;RotationLocked=false;Landed=false;angularVelocity=Vector3.zero;return true;
        }
        bool IntegrateDocked(float dt){if(dockStation==0)return false;var station=world.Orbitals?.Find(dockStation);if(station==null){dockStation=0;return false;}
            dockProgress=Mathf.Min(1,dockProgress+dt/3);float t=dockProgress*dockProgress*(3-2*dockProgress);var local=Vector3.Lerp(dockStart,station.DockPosition,t);var next=station.LocalToBody(local);var rotation=station.Rotation*Quaternion.Slerp(dockRotation,Quaternion.identity,t);
            if(!Docked){Physics.SyncTransforms();if(!HullSweep(pose.position,next,rotation)){dockStation=0;pose.linearVelocity=station.Pose.linearVelocity;shipFrame.Pose=pose;return true;}}
            pose.position=next;pose.rotation=rotation;pose.linearVelocity=new Double3(station.VelocityAt(local));pose.angularVelocity=station.Pose.angularVelocity;shipFrame.Pose=pose;return true;
        }
        void StationCollision(Double3 from,ref Double3 next,ref Double3 velocity){if(world.Orbitals==null||!world.Orbitals.NearSegment(from,next,450))return;Physics.SyncTransforms();if(!HullSweep(from,next,pose.rotation)){next=from;var station=world.Orbitals.Nearest(from,out _);velocity=station==null?new Double3():station.Pose.linearVelocity;}}
    }
}
