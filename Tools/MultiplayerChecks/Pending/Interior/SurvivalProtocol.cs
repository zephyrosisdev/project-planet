using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalAdmission {public int protocol=SurvivalServerConfig.Protocol;public string token,name,content=SurvivalServerConfig.Content;}
    [Serializable] internal sealed class SurvivalLeave {public int version=1;}
    [Serializable] internal sealed class SurvivalWelcome {
        public int protocol=SurvivalServerConfig.Protocol;public long actor;public SurvivalServerConfig config;public string definition;
    }
    [Serializable] internal sealed class SurvivalMotion {
        public long actor,tick,epoch;public string name;public uint sequence;public FramePose pose;public float pitch;public double verticalSpeed;
        public float forwardX,forwardY,forwardZ;public bool grounded,interior;public string notice;
        public SurvivalVehicleState vehicle;public string vehicleDefinition;public float viewYaw;
        public SurvivalShipState ship;
        public SurvivalMotion Copy(){var copy=(SurvivalMotion)MemberwiseClone();if(vehicle!=null)copy.vehicle=vehicle.Copy();if(ship!=null)copy.ship=ship.Copy();return copy;}
    }
    [Serializable] internal sealed class SurvivalAction {
        public int version=1;public long sequence,tick,epoch;public uint inputSequence;public string kind;public IndustrialCommand command;public Planetary.PlayerBuildCommand building;
        public InventoryAddress from,to;public long count,entity;public int slot;public string item;
    }
    [Serializable] internal sealed class SurvivalPlayerProxy {
        public int version=2;public long actor,serverTick,parent;public string name,parentKind,parentLocal;[NonSerialized] public FramePose pose;public float pitch,forwardX,forwardY,forwardZ;
        internal static FramePose FingerprintPose(FramePose pose){pose.x=Math.Round(pose.x,3);pose.y=Math.Round(pose.y,3);pose.z=Math.Round(pose.z,3);pose.yaw=(float)Math.Round(pose.yaw,2);return pose;}
        internal string Fingerprint(){var copy=(SurvivalPlayerProxy)MemberwiseClone();copy.serverTick=0;copy.pose=FingerprintPose(pose);if(parent!=0){copy.forwardX=copy.forwardY=copy.forwardZ=0;}copy.pitch=(float)Math.Round(pitch,2);copy.forwardX=(float)Math.Round(forwardX,4);copy.forwardY=(float)Math.Round(forwardY,4);copy.forwardZ=(float)Math.Round(forwardZ,4);return JsonUtility.ToJson(copy);}
        internal bool TryParentPose(out FramePose local){
            local=default;if(parent==0||string.IsNullOrEmpty(parentLocal))return false;var values=parentLocal.Split(',');if(values.Length!=4)return false;var culture=System.Globalization.CultureInfo.InvariantCulture;
            if(!double.TryParse(values[0],System.Globalization.NumberStyles.Float,culture,out local.x)||!double.TryParse(values[1],System.Globalization.NumberStyles.Float,culture,out local.y)||!double.TryParse(values[2],System.Globalization.NumberStyles.Float,culture,out local.z)||!float.TryParse(values[3],System.Globalization.NumberStyles.Float,culture,out local.yaw)||!SurvivalServerConfig.Finite(local.x)||!SurvivalServerConfig.Finite(local.y)||!SurvivalServerConfig.Finite(local.z)||!SurvivalServerConfig.Finite(local.yaw))return false;local.frame=parent;return true;
        }
        internal static SurvivalPlayerProxy From(SurvivalMotion motion,FramePose feet){
            var proxy=new SurvivalPlayerProxy{actor=motion.actor,serverTick=motion.tick,name=motion.name,pose=feet,pitch=motion.pitch,forwardX=motion.forwardX,forwardY=motion.forwardY,forwardZ=motion.forwardZ,parent=motion.ship?.entity??motion.vehicle?.entity??0,parentKind=motion.ship!=null?"ship":motion.vehicle!=null?"vehicle":""};
            if(proxy.parent!=0){FramePose local;Quaternion inverse;
                if(motion.ship!=null){inverse=Quaternion.Inverse(SurvivalShipInteriorModel.Rotation(motion.ship));local=SurvivalShipInteriorModel.Local(motion.ship,feet);}
                else {inverse=Quaternion.Inverse(SurvivalVehicleSimulation.Rotation(motion.vehicle));var point=Double3.Rotate(inverse,new Double3(feet.x-motion.vehicle.pose.x,feet.y-motion.vehicle.pose.y,feet.z-motion.vehicle.pose.z));local=new FramePose{x=point.x,y=point.y,z=point.z};}
                var forward=motion.interior?SurvivalMovement.Forward(motion):inverse*SurvivalMovement.Forward(motion);local.yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
                var culture=System.Globalization.CultureInfo.InvariantCulture;proxy.parentLocal=local.x.ToString("0.###",culture)+","+local.y.ToString("0.###",culture)+","+local.z.ToString("0.###",culture)+","+local.yaw.ToString("0.##",culture);
                if(motion.interior){var world=SurvivalShipInteriorModel.Rotation(motion.ship)*forward;proxy.forwardX=world.x;proxy.forwardY=world.y;proxy.forwardZ=world.z;}
            }return proxy;
        }
    }
    [Serializable] internal sealed class SurvivalEntity {
        public int version=1;public long revision;
        public long id;public string kind,data;public FramePose pose;
        [NonSerialized] internal string fingerprintData;
        [NonSerialized] internal byte[] encoded;
        [NonSerialized] internal FramePose? fingerprintPose;
        [NonSerialized] internal long requiredParent;
    }
    [Serializable] internal sealed class SurvivalSnapshot {
        public bool privateDetails=true;public SurvivalChunk chunk;public long cancelTransfer;
        public long tick;public SurvivalMotion motion;public IndustrialActor self;public SurvivalPlayerRecord vitals;
        public ShipAssembly pilotedShip;
        public long industryTick;public List<RespawnPoint> respawnPoints=new List<RespawnPoint>();
        public long ownedShip;public List<SurvivalShipInvite> shipInvites=new List<SurvivalShipInvite>();
        public List<SurvivalIncomingInvitation> incomingInvitations=new List<SurvivalIncomingInvitation>();
        public List<SurvivalEntity> changed=new List<SurvivalEntity>();public List<long> removed=new List<long>();
        // Unity JSON can materialize an absent inline class as a zero-valued object.
        // Zero is never a valid craft/owner identity; reject those phantom references.
        internal int NormalizeReferences(){int count=0;if(chunk!=null&&chunk.transfer==0)chunk=null;if(motion?.vehicle!=null&&motion.vehicle.entity<=0){motion.vehicle=null;count++;}if(motion?.ship!=null&&motion.ship.entity<=0){motion.ship=null;count++;}if(pilotedShip!=null&&pilotedShip.owner<=0){pilotedShip=null;count++;}return count;}
    }
    [Serializable] internal sealed class SurvivalShipInvite {public long actor;public string name;}
    [Serializable] internal sealed class SurvivalIncomingInvitation {public long ship;public string ownerName;}
    [Serializable] internal sealed class SurvivalActionReply {public long sequence,tick;public bool accepted;public string message;}
    internal static class SurvivalPrediction {
        internal static double MatchingError(Dictionary<uint,SurvivalMotion> history,SurvivalMotion authoritative)=>history.TryGetValue(authoritative.sequence,out var sample)&&sample.epoch==authoritative.epoch&&sample.tick==authoritative.tick&&sample.pose.body==authoritative.pose.body&&sample.pose.frame==authoritative.pose.frame?sample.pose.Distance(authoritative.pose):double.NaN;
        internal static void Replay(SurvivalMotion state,IEnumerable<SurvivalInput> pending,Dictionary<uint,SurvivalMotion> history,float dt,Action<SurvivalMotion,SurvivalInput,float> step){foreach(var input in pending){step(state,input,dt);history[input.sequence]=state.Copy();}}
    }
    internal static class SurvivalMovement {
        public static Vector3 Forward(SurvivalMotion state){var v=new Vector3(state.forwardX,state.forwardY,state.forwardZ);return v.sqrMagnitude>.5f?v.normalized:Vector3.forward;}
        public static Vector3 Tangent(Vector3 preferred,Vector3 up){var forward=Vector3.ProjectOnPlane(preferred,up);if(forward.sqrMagnitude<.1f)forward=Vector3.ProjectOnPlane(Mathf.Abs(up.z)<.8f?Vector3.forward:Vector3.right,up);return forward.normalized;}
        public static void Step(SurvivalMotion state,SurvivalInput input,Planetary.PlanetSurface surface,float dt,double speed){
            if(dt<=0||dt>.101f||surface==null)return;
            var position=new Planetary.Double3(state.pose.x,state.pose.y,state.pose.z);var up=Planetary.PlanetPrecisionPolicy.Direction(position);
            var forward=Tangent(Forward(state),up);
            forward=Quaternion.AngleAxis(input.lookX,up)*forward;state.pitch=Mathf.Clamp(state.pitch-input.lookY,-85,85);
            var right=Vector3.Cross(up,forward).normalized;var move=Vector2.ClampMagnitude(new Vector2(input.x,input.y),1);
            if(state.grounded&&input.jump)state.verticalSpeed=5;
            state.verticalSpeed-=surface.Definition.gravitationalParameter/Math.Max(1,position.Magnitude*position.Magnitude)*dt;
            var next=position+new Planetary.Double3((forward*move.y+right*move.x)*(float)(speed*dt)+up*(float)(state.verticalSpeed*dt));
            double floor=surface.Definition.radius+(double)surface.SampleHeight(next)+.05;state.grounded=next.Magnitude<=floor;
            if(state.grounded){next*=floor/Math.Max(1,next.Magnitude);state.verticalSpeed=0;}
            var nextUp=Planetary.PlanetPrecisionPolicy.Direction(next);forward=Quaternion.FromToRotation(up,nextUp)*forward;
            state.pose.x=next.x;state.pose.y=next.y;state.pose.z=next.z;state.forwardX=forward.x;state.forwardY=forward.y;state.forwardZ=forward.z;
            state.sequence=input.sequence;
        }
    }
}
