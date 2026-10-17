using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassiveArmy.Planetary
{
    public enum FrameKind { Universe,SolarSystem,Planet,Surface,Vehicle,Ship }
    [Serializable] public struct SpacePose
    {
        public int frameId;
        public Double3 position,linearVelocity;
        public Quaternion rotation;
        public Vector3 angularVelocity; // radians/second in parent axes
    }
    public sealed class ReferenceFrame
    {
        public readonly int Id,ParentId;
        public readonly FrameKind Kind;
        public SpacePose Pose;
        public ReferenceFrame(int id,int parent,FrameKind kind,SpacePose pose){Id=id;ParentId=parent;Kind=kind;Pose=pose;Pose.frameId=parent;}
    }
    public struct UniversePose
    {
        public UniversePosition position;
        public Quaternion rotation;
        public Double3 linearVelocity;
        public Vector3 angularVelocity;
    }
    /// <summary>Stable IDs; bounded hierarchy, with no registration or updates per army unit.</summary>
    public sealed class ReferenceFrameManager
    {
        readonly Dictionary<int,ReferenceFrame> frames=new Dictionary<int,ReferenceFrame>();
        public UniversePosition Origin;
        public Double3 OriginVelocity;
        public void Register(ReferenceFrame frame)
        {
            if(frame.Id<=0||frames.ContainsKey(frame.Id))throw new ArgumentException("Frame ID must be unique and positive");
            if(frame.ParentId!=0&&!frames.ContainsKey(frame.ParentId))throw new ArgumentException("Register parent first");
            frames.Add(frame.Id,frame);
        }
        public UniversePose Resolve(SpacePose pose)
        {
            var parent=ResolveFrame(pose.frameId,0);
            Double3 offset=Double3.Rotate(parent.rotation,pose.position);
            return new UniversePose{position=parent.position.Add(offset),rotation=parent.rotation*pose.rotation,
                linearVelocity=parent.linearVelocity+Double3.Rotate(parent.rotation,pose.linearVelocity)+Double3.Cross(new Double3(parent.angularVelocity),offset),
                angularVelocity=parent.angularVelocity+parent.rotation*pose.angularVelocity};
        }
        UniversePose ResolveFrame(int id,int depth)
        {
            if(id==0)return new UniversePose{position=Origin,rotation=Quaternion.identity,linearVelocity=OriginVelocity};
            if(depth>=64)throw new InvalidOperationException("Reference frame hierarchy exceeds 64 levels");
            var frame=frames[id];var p=ResolveFrame(frame.ParentId,depth+1);var local=frame.Pose;
            var offset=Double3.Rotate(p.rotation,local.position);
            return new UniversePose{position=p.position.Add(offset),rotation=p.rotation*local.rotation,
                linearVelocity=p.linearVelocity+Double3.Rotate(p.rotation,local.linearVelocity)+Double3.Cross(new Double3(p.angularVelocity),offset),
                angularVelocity=p.angularVelocity+p.rotation*local.angularVelocity};
        }
        public SpacePose ChangeFrame(SpacePose pose,int frameId)
        {
            var world=Resolve(pose);var parent=ResolveFrame(frameId,0);var inverse=Quaternion.Inverse(parent.rotation);
            var offset=world.position.RelativeTo(parent.position);
            return new SpacePose{frameId=frameId,position=Double3.Rotate(inverse,offset),rotation=inverse*world.rotation,
                linearVelocity=Double3.Rotate(inverse,world.linearVelocity-parent.linearVelocity-Double3.Cross(new Double3(parent.angularVelocity),offset)),
                angularVelocity=inverse*(world.angularVelocity-parent.angularVelocity)};
        }
    }
    public sealed class FloatingOrigin
    {
        public UniversePosition Origin {get;private set;}
        public double Threshold=2048;
        public event Action<Vector3> Rebased;
        public FloatingOrigin(UniversePosition origin){Origin=origin;}
        public bool Update(UniversePosition observer)
        {
            var displacement=observer.RelativeTo(Origin);if(displacement.Magnitude<=Threshold)return false;
            return Anchor(observer);
        }
        /// <summary>Client-local presentation only. Never shifts authoritative simulation records.</summary>
        public bool Anchor(UniversePosition observer)
        {
            var displacement=observer.RelativeTo(Origin);
            if(displacement.x==0&&displacement.y==0&&displacement.z==0)return false;
            Origin=observer;Rebased?.Invoke(-displacement.Float);return true;
        }
        public UniversePosition RenderToUniverse(Vector3 render)=>Origin.Add(new Double3(render));
        public Vector3 RenderPosition(UniversePosition p)=>p.RelativeTo(Origin).Float;
    }
}
