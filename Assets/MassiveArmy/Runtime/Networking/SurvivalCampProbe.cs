#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        SurvivalCampFixtureSpec campSpec;int campStep;double campAt;long campStone,campRations;bool campSubscribed;SurvivalAction campRequest;SurvivalActionReply campReply;
        bool StartCampProbe(){string source=Argument("-survival-probe-camp");if(source==null)return true;try{if(!Path.GetFullPath(source).StartsWith(Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||report.headlessClient)throw new InvalidOperationException("Camp probe requires an isolated graphical fixture");campSpec=JsonUtility.FromJson<SurvivalCampFixtureSpec>(File.ReadAllText(source));if(campSpec?.seeded!=true)throw new InvalidOperationException("Camp fixture is not seeded");deadline+=180;report.campPhase=campSpec.phase;report.scope+=" Includes neutral camps, barter UI or pad flight or EVA through production Relay and authority; phase="+campSpec.phase;return true;}catch(Exception error){_ = Finish(false,error.Message);return false;}}
        bool CampOwner=>report.actor==campSpec.owner;
        string CampMarker(string name)=>Path.Combine(Path.GetDirectoryName(path),"camp-"+name+".ack");
        void CampReply(SurvivalActionReply reply){if(campRequest?.sequence==reply.sequence)campReply=reply;}
        void CampSend(string kind){campReply=null;campRequest=new SurvivalAction{kind=kind,entity=campSpec.ship};session.Submit(campRequest);}
        void StopCampProbe(){if(campSubscribed&&session!=null)session.ActionReplied-=CampReply;}
        void UpdateCampProbe(){
            if(!session.PrivateStateReady||!report.remotePlayerSeen)return;if(!campSubscribed){session.ActionReplied+=CampReply;campSubscribed=true;campAt=Time.realtimeSinceStartupAsDouble;}
            report.campStep=campStep;var motion=session.Latest.motion;var beacon=session.Latest.campBeacons.Find(c=>c.id==campSpec.camp);if(beacon==null)return;
            if(campReply!=null&&!campReply.accepted){_ = Finish(false,"Camp probe command rejected: "+campReply.message);return;}
            if(session.Latest.self.health<=0){_ = Finish(false,"Camp/EVA fixture character died");return;}
            if(campSpec.phase=="trade"){
                var root=session.OnlineInventoryRoot;var actor=session.Latest.self;
                if(campStep==0){campStone=actor.inventory.Count("stone");campRations=actor.inventory.Count("ration");session.OpenCampProbeMenu();campStep=1;return;}
                if(campStep==1){var quantity=root?.Q<IntegerField>("camp-trade-quantity");if(quantity==null)return;quantity.value=999;if(quantity.value!=20){_ = Finish(false,"Camp trade quantity did not clamp");return;}quantity.value=1;if(!ClickCraftingButton(root.Q<Button>("camp-trade-"+campSpec.camp+"-ration")))return;campStep=2;return;}
                if(campStep==2){if(actor.inventory.Count("stone")!=campStone-4||actor.inventory.Count("ration")!=campRations+1)return;report.campTrade=true;StartCoroutine(CapturePresentation(false,".camp-trade.png"));campAt=Time.realtimeSinceStartupAsDouble;campStep=3;return;}
                if(Time.realtimeSinceStartupAsDouble-campAt>2)_ = Finish(true,null);return;
            }
            if(campSpec.phase=="landing"){
                if(!CampOwner){bool reserved=beacon.occupied.Contains(campSpec.ship);if(reserved)report.campReserved=true;foreach(var entity in session.Visible.Values)if(entity.kind=="ship"){var ship=JsonUtility.FromJson<SurvivalShipState>(entity.data);if(ship.entity==campSpec.ship&&ship.landed)report.campLanded=true;}if(report.campReserved&&report.campLanded&&!reserved){report.campLaunched=true;File.WriteAllText(CampMarker("launch-observed"),"seen");_ = Finish(true,null);}return;}
                if(campStep==0){if(!motion.interior)return;CampSend("enter-pilot");campStep=1;return;}
                if(campStep==1){if(session.Latest.vitals.controlledShip!=campSpec.ship)return;session.OpenCampProbeMenu();campStep=2;return;}
                if(campStep==2){if(!ClickCraftingButton(session.OnlineInventoryRoot.Q<Button>("camp-land-"+campSpec.camp+"-0")))return;campStep=3;return;}
                if(campStep==3){if(!beacon.occupied.Contains(campSpec.ship)||motion.ship?.landed!=true||motion.ship.autoFlight)return;report.campReserved=report.campLanded=true;StartCoroutine(CapturePresentation(false,".camp-landed.png"));campAt=Time.realtimeSinceStartupAsDouble;campStep=4;return;}
                if(campStep==4){if(Time.realtimeSinceStartupAsDouble-campAt<3)return;if(!ClickCraftingButton(session.OnlineInventoryRoot.Q<Button>("camp-launch")))return;campStep=5;return;}
                if(campStep==5){if(motion.ship?.autoFlight!=false||motion.ship.landed||beacon.occupied.Contains(campSpec.ship)||!File.Exists(CampMarker("launch-observed")))return;report.campLaunched=true;_ = Finish(true,null);}return;
            }
            if(campSpec.phase=="eva"){
                if(!CampOwner){foreach(var entity in session.Visible.Values)if(entity.kind=="player"){var other=JsonUtility.FromJson<SurvivalPlayerProxy>(entity.data);if(other.actor!=campSpec.owner)continue;if(other.parent==0)report.evaExited=true;if(report.evaExited&&other.parentKind=="ship"&&other.parent==campSpec.ship){report.evaReturned=true;File.WriteAllText(CampMarker("eva-observed"),"seen");_ = Finish(true,null);}}return;}
                if(campStep==0){if(!motion.interior)return;campAt=Time.realtimeSinceStartupAsDouble;campStep=1;return;}
                if(campStep==1){if(!motion.eva)return;report.evaExited=true;campAt=Time.realtimeSinceStartupAsDouble;campStep=2;return;}
                if(campStep==2){if(!motion.eva){_ = Finish(false,"EVA attached before suit thrust was tested");return;}if(Time.realtimeSinceStartupAsDouble-campAt<2)return;double suit=new Double3(motion.evaSuitX,motion.evaSuitY,motion.evaSuitZ).Magnitude;double carrier=new Double3(motion.evaVelocityX,motion.evaVelocityY,motion.evaVelocityZ).Magnitude;if(suit<3||suit>session.Config.evaSpeed+.1||carrier<100){_ = Finish(false,"Suit thrust or orbital velocity was not preserved");return;}report.evaThrust=true;StartCoroutine(CapturePresentation(false,".eva.png"));campStep=3;campAt=Time.realtimeSinceStartupAsDouble;return;}
                if(campStep==3&&motion.interior&&motion.ship?.entity==campSpec.ship){report.evaReturned=true;campStep=4;campAt=Time.realtimeSinceStartupAsDouble;return;}
                if(campStep==4&&File.Exists(CampMarker("eva-observed"))&&Time.realtimeSinceStartupAsDouble-campAt>2)_ = Finish(true,null);
            }
        }
        SurvivalInput CampControls(SurvivalInput input){input.x=input.y=input.z=input.roll=input.lookX=input.lookY=0;input.jump=input.boost=input.toggleAssist=input.toggleGear=input.brake=input.cruise=input.matchOrbit=input.toggleLock=input.toggleOrbit=false;if(campSpec.phase!="eva"||!CampOwner)return input;
            if(campStep==1&&session.Predicted.interior){input.y=1;return input;}
            if(campStep==2){input.z=1;return input;}
            if(campStep!=3||session.Latest.motion.eva!=true)return input;
            // Target and velocity come from the same authoritative snapshot, never a
            // predicted character compared against a delayed, rapidly moving hull.
            var motion=session.Latest.motion;SurvivalShipState ship=null;foreach(var entity in session.Visible.Values)if(entity.kind=="ship"){var candidate=JsonUtility.FromJson<SurvivalShipState>(entity.data);if(candidate.entity==campSpec.ship){ship=candidate;break;}}if(ship==null)return input;
            var local=ShipModules.P(0,-3.9,-38.3);var target=SurvivalShipInteriorModel.World(ship,local);var error=SurvivalVehicleSimulation.Position(target)-SurvivalVehicleSimulation.Position(motion.pose);var velocity=new Double3(motion.evaVelocityX+motion.evaSuitX,motion.evaVelocityY+motion.evaSuitY,motion.evaVelocityZ+motion.evaSuitZ)-SurvivalNetworkSession.ProbeShipPointVelocity(ship,local);var desired=(error*.65-velocity*.8).Float/(float)session.Config.evaSpeed;
            var predicted=session.Predicted;var up=PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(predicted.pose));var forward=SurvivalMovement.Tangent(SurvivalMovement.Forward(predicted),up);var look=Quaternion.AngleAxis(predicted.pitch,Vector3.Cross(up,forward))*forward;var right=Vector3.Cross(up,look).normalized;input.x=Mathf.Clamp(Vector3.Dot(desired,right),-1,1);input.y=Mathf.Clamp(Vector3.Dot(desired,look),-1,1);input.z=Mathf.Clamp(Vector3.Dot(desired,up),-1,1);return input;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        internal void OpenCampProbeMenu()=>OpenCampMenu();
        internal static Double3 ProbeShipPointVelocity(SurvivalShipState ship,FramePose local)=>ShipPointVelocity(ship,local);
    }
}
#endif
