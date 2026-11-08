#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        SurvivalFactoryFixtureSpec factorySpec;string factoryState;int factoryStep;bool factorySubscribed;SurvivalAction factoryRequest,factoryBuiltAction;SurvivalActionReply factoryReply;double factoryReplayAt;long factoryAcceptedTick;
        FramePose? factoryWalkGoal,factoryStartPose;bool factoryReturnApproached;
        bool FactoryOwner=>report.actor==factorySpec.owner;
        string FactoryMarker(string name)=>Path.Combine(factoryState,"factory-"+report.factoryPhase+"-"+name+".ack");
        bool StartFactoryProbe(){
            string specPath=Argument("-survival-probe-factory");if(specPath==null)return true;
            try{
                report.factoryRequired=true;report.factoryPhase=Argument("-survival-probe-factory-phase");factoryState=Argument("-survival-probe-factory-state");
                string allowed=Path.GetFullPath("TestResults/Multiplayer/Live")+Path.DirectorySeparatorChar;
                if(report.chatRequired||!Path.GetFullPath(specPath).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||factoryState==null||!Path.GetFullPath(factoryState).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||(report.factoryPhase!="create"&&report.factoryPhase!="recover"))throw new InvalidOperationException("Invalid isolated factory probe arguments");
                factorySpec=JsonUtility.FromJson<SurvivalFactoryFixtureSpec>(File.ReadAllText(specPath));if(factorySpec?.seeded!=true||factorySpec.version!=1)throw new InvalidOperationException("Factory fixture was not prepared offline");
                if(factorySpec.shipyard||factorySpec.buildings)deadline+=600;
                report.scope+=" Includes factory commands through normal transport, private endpoint checks and diagnostic file barriers between local clients. Fixture resources were prepared offline; GUI and survival progression are unverified.";return true;
            }catch(Exception e){_ = Finish(false,e.Message);return false;}
        }
        void StopFactoryProbe(){if(factorySubscribed&&session!=null)session.ActionReplied-=FactoryReply;factorySubscribed=false;}
        void FactoryReply(SurvivalActionReply reply){if(factoryRequest!=null&&reply.sequence==factoryRequest.sequence)factoryReply=reply;}
        void FactorySend(IndustrialCommand command){factoryReply=null;factoryRequest=new SurvivalAction{kind="industrial",command=command};session.Submit(factoryRequest);}
        bool FactoryReplied(bool accepted){
            if(factoryReply==null)return false;if(factoryReply.accepted!=accepted){_ = Finish(false,"Unexpected factory reply: "+factoryReply.message);return false;}
            if(accepted)factoryAcceptedTick=factoryReply.tick;
            bool shipyard=factoryRequest?.command?.kind?.StartsWith("ship-",StringComparison.Ordinal)==true;
            if(factoryRequest?.kind=="building"){if(accepted)report.buildingAccepted++;else report.buildingRejections++;}else if(shipyard){if(accepted)report.shipyardAccepted++;else report.shipyardRejections++;}else if(accepted)report.factoryAccepted++;else report.factoryOwnershipRejections++;factoryReply=null;return true;
        }
        Dictionary<long,IndustrialMachine> FactoryMachines(){var result=new Dictionary<long,IndustrialMachine>();foreach(var entity in session.Visible.Values)if(entity.kind=="machine")result[entity.id/8]=JsonUtility.FromJson<IndustrialMachine>(entity.data);return result;}
        List<SurvivalFactoryLink> FactoryLinks(){var result=new List<SurvivalFactoryLink>();foreach(var entity in session.Visible.Values)if(entity.kind=="factory-link")result.Add(JsonUtility.FromJson<SurvivalFactoryLink>(entity.data));return result;}
        bool FactoryFull(List<SurvivalFactoryLink> links,Dictionary<long,IndustrialMachine> machines){
            bool full=links.Count==3&&links.Exists(l=>l.kind=="power"&&l.source==factorySpec.generator&&l.target==factorySpec.consumer)&&links.Exists(l=>l.kind=="water"&&l.source==factorySpec.pump&&l.target==factorySpec.waterTarget)&&links.Exists(l=>l.kind=="belt"&&l.source==factorySpec.source&&l.target==factorySpec.target);
            full&=machines.ContainsKey(factorySpec.source)&&machines.ContainsKey(factorySpec.target)&&machines.ContainsKey(factorySpec.generator)&&machines.ContainsKey(factorySpec.consumer)&&machines.ContainsKey(factorySpec.pump)&&machines.ContainsKey(factorySpec.waterTarget);
            if(full&&!report.headlessClient){if(!session.FactoryVisualsMatch(3))return false;report.factoryRendered=true;}if(full)report.factoryReplication=true;return full;
        }
        bool FactoryCargo(Dictionary<long,IndustrialMachine> machines)=>machines.TryGetValue(factorySpec.target,out var target)&&target.input.Count("iron-bar")==factorySpec.cargo&&Math.Abs(target.input.ActivityOf("iron-bar",factorySpec.cargo)-factorySpec.cargoActivity)<1e-8;
        bool FactoryStock(bool recovered)=>session.Latest.self.inventory.Count("plate")==factorySpec.initialPlates-(recovered?0:factorySpec.beltCost)&&session.Latest.self.inventory.Count("gear")==factorySpec.initialGears-(recovered?0:factorySpec.beltCost)-(factorySpec.buildings&&report.factoryPhase=="recover"?1:0);
        bool FactoryPrivate(Dictionary<long,IndustrialMachine> machines){
            if(FactoryOwner)return true;
            foreach(long id in new[]{factorySpec.source,factorySpec.target,factorySpec.generator,factorySpec.consumer,factorySpec.pump,factorySpec.waterTarget}){
                if(!machines.TryGetValue(id,out var machine))return false;
                if(machine.input.Used!=0||machine.output.Used!=0||machine.constructionPaid.Count!=0){_ = Finish(false,"Foreign factory inventory or receipt leaked through endpoint subscription");return false;}
            }report.factoryPrivacy=true;return true;
        }
        void UpdateFactoryProbe(){
            if(!session.PrivateStateReady)return;
            if(!factoryStartPose.HasValue)factoryStartPose=session.Latest.self.pose;
            if(!factorySubscribed){session.ActionReplied+=FactoryReply;factorySubscribed=true;}
            if(report.actor!=factorySpec.owner&&report.actor!=factorySpec.observer){_ = Finish(false,"Unexpected authenticated factory character");return;}
            var machines=FactoryMachines();var links=FactoryLinks();if(!FactoryPrivate(machines))return;
            if(report.factoryPhase=="create"){
                if(FactoryOwner)FactoryCreateOwner(machines,links);else FactoryCreateObserver(machines,links);
            }else{
                if(FactoryOwner)FactoryRecoverOwner(machines,links);else FactoryRecoverObserver(machines,links);
            }
        }
        bool FactoryReturnWalk(){
            if(factorySpec.buildings&&!factoryReturnApproached){var surface=new MassiveArmy.Planetary.PlanetSurface(session.ClientSimulationDefinition);var origin=new MassiveArmy.Planetary.Double3(factorySpec.buildingGround.x,factorySpec.buildingGround.y,factorySpec.buildingGround.z);var up=MassiveArmy.Planetary.PlanetPrecisionPolicy.Direction(origin);var around=surface.ProjectToSurface(origin+new MassiveArmy.Planetary.Double3(Quaternion.FromToRotation(Vector3.up,up)*new Vector3(8,0,4)),.05);var goal=new FramePose{body=factorySpec.buildingGround.body,x=around.x,y=around.y,z=around.z};if(!FactoryWalkTo(goal))return false;factoryReturnApproached=true;}
            return FactoryWalkTo(factorySpec.factoryReturn);
        }
        bool FactoryWalkTo(FramePose goal){if(session.Latest.self.pose.Distance(goal)<=8){factoryWalkGoal=null;return true;}factoryWalkGoal=goal;return false;}
        SurvivalInput FactoryControls(SurvivalInput input){
            if(!factoryWalkGoal.HasValue||session?.Predicted==null)return input;
            var control=session.ProbeWalkingControls(factoryWalkGoal.Value);input.x=control.x;input.y=control.y;return input;
        }
        void FactoryCreateOwner(Dictionary<long,IndustrialMachine> machines,List<SurvivalFactoryLink> links){
            switch(factoryStep){
                case 0:
                    if(!report.remotePlayerSeen||machines.Count<6)return;
                    if(links.Count!=0||!FactoryStock(true)){_ = Finish(false,"Factory create did not start from its prepared state");return;}
                    FactorySend(new IndustrialCommand{kind="power",source=factorySpec.generator,target=factorySpec.consumer,route=factorySpec.powerRoute});factoryStep=1;break;
                case 1:if(!FactoryReplied(true))return;FactorySend(new IndustrialCommand{kind="water",source=factorySpec.pump,target=factorySpec.waterTarget,route=factorySpec.waterRoute});factoryStep=2;break;
                case 2:if(!FactoryReplied(true))return;FactorySend(new IndustrialCommand{kind="belt",source=factorySpec.source,target=factorySpec.target,route=factorySpec.beltRoute});factoryBuiltAction=factoryRequest;factoryStep=3;break;
                case 3:if(!FactoryReplied(true))return;session.RepeatProbeAction(factoryBuiltAction);factoryReplayAt=Time.realtimeSinceStartupAsDouble;factoryStep=4;break;
                case 4:
                    if(!FactoryFull(links,machines)||!FactoryCargo(machines)||Time.realtimeSinceStartupAsDouble-factoryReplayAt<1)return;
                    if(session.PrivateStateTick<factoryAcceptedTick)return;
                    if(!FactoryStock(false)){_ = Finish(false,"Factory replay charged materials more than once");return;}report.factoryReplaySafe=report.factoryConserved=true;
                    if(!File.Exists(FactoryMarker("observer-ready")))return;lastPose=session.Latest.self.pose;phase=3;factoryStep=5;_ = Reconnect();break;
                case 5:
                    if(phase!=4||!FactoryFull(links,machines)||!FactoryCargo(machines))return;
                    if(session.Latest.self.id!=report.actor||lastPose.Distance(session.Latest.self.pose)>session.Config.walkSnap||!FactoryStock(false)){_ = Finish(false,"Factory reconnect changed character, position or paid inventory");return;}
                    report.reconnected=report.factoryReconnect=true;if(factorySpec.shipyard){factoryStep=6;break;}if(factorySpec.buildings){factoryStep=8;break;}FactoryOwnerDone();break;
                case 6:if(!ShipyardOwner(false))return;factoryStep=7;break;
                case 7:if(!FactoryWalkTo(factorySpec.factoryReturn)||!FactoryCargo(machines))return;if(factorySpec.buildings){factoryStep=8;break;}FactoryOwnerDone();break;
                case 8:if(!BuildingOwner(false))return;factoryStep=9;break;
                case 9:if(!FactoryWalkTo(factorySpec.factoryReturn)||!FactoryCargo(machines))return;FactoryOwnerDone();break;
            }
        }
        void FactoryCreateObserver(Dictionary<long,IndustrialMachine> machines,List<SurvivalFactoryLink> links){
            switch(factoryStep){
                case 0:if(!FactoryFull(links,machines))return;FactorySend(new IndustrialCommand{kind="power",source=factorySpec.generator,target=factorySpec.consumer,route=factorySpec.powerRoute});factoryStep=1;break;
                case 1:if(!FactoryReplied(false))return;var belt=links.Find(l=>l.kind=="belt");if(belt==null){_ = Finish(false,"Owner belt disappeared during ownership checks");return;}FactorySend(new IndustrialCommand{kind="remove-belt",target=belt.entity});factoryStep=2;break;
                case 2:if(!FactoryReplied(false))return;lastPose=session.Latest.self.pose;phase=3;factoryStep=3;_ = Reconnect();break;
                case 3:if(phase!=4||!FactoryFull(links,machines))return;report.reconnected=report.factoryReconnect=true;File.WriteAllText(FactoryMarker("observer-ready"),"foreign edits rejected; restored after reconnect");factoryStep=4;break;
                case 4:if(factorySpec.shipyard&&!ShipyardObserver(false))return;if(factorySpec.buildings&&!BuildingObserver(false))return;if(File.Exists(FactoryMarker("owner-done")))_ = Finish(true,null);break;
            }
        }
        void FactoryRecoverOwner(Dictionary<long,IndustrialMachine> machines,List<SurvivalFactoryLink> links){
            switch(factoryStep){
                case 0:
                    if(factorySpec.buildings&&!FactoryWalkTo(factorySpec.factoryReturn))return;
                    if(!FactoryFull(links,machines)||!FactoryCargo(machines)||factorySpec.buildings&&ProbeBuildings().Count!=3)return;
                    if(!FactoryStock(false)){_ = Finish(false,"Restart changed paid factory inventory");return;}report.factoryRestartRestored=true;
                    if(!File.Exists(FactoryMarker("observer-ready")))return;FactorySend(new IndustrialCommand{kind="remove-power",target=factorySpec.consumer});factoryStep=1;break;
                case 1:if(!FactoryReplied(true))return;factoryStep=2;break;
                case 2:
                    if(!machines.TryGetValue(factorySpec.pump,out var pump)||!FactoryWalkTo(pump.pose))return;
                    FactorySend(new IndustrialCommand{kind="remove-water",target=factorySpec.pump});factoryStep=3;break;
                case 3:if(!FactoryReplied(true))return;factoryStep=4;break;
                case 4:
                    if(!machines.TryGetValue(factorySpec.source,out var source)||!FactoryWalkTo(source.pose))return;
                    var belt=links.Find(l=>l.kind=="belt");if(belt==null){_ = Finish(false,"Persisted belt unavailable for recovery");return;}FactorySend(new IndustrialCommand{kind="remove-belt",target=belt.entity});factoryStep=5;break;
                case 5:if(!FactoryReplied(true))return;factoryStep=6;break;
                case 6:
                    if(!machines.TryGetValue(factorySpec.target,out var target)||!FactoryWalkTo(target.pose))return;
                    if(links.Count!=0||!FactoryStock(true)||!FactoryCargo(machines)||!File.Exists(FactoryMarker("observer-removed")))return;
                    if(!FactoryRenderGone())return;
                    report.factoryRemoved=report.factoryConserved=true;if(factorySpec.shipyard){factoryStep=7;break;}if(factorySpec.buildings){factoryStep=9;break;}_ = Finish(true,null);break;
                case 7:if(!ShipyardOwner(true))return;factoryStep=8;break;
                case 8:if(!FactoryReturnWalk()||!FactoryCargo(machines))return;if(factorySpec.buildings){factoryStep=9;break;}_ = Finish(true,null);break;
                case 9:if(!BuildingOwner(true))return;_ = Finish(true,null);break;
            }
        }
        void FactoryRecoverObserver(Dictionary<long,IndustrialMachine> machines,List<SurvivalFactoryLink> links){
            if(factoryStep==0){if(!FactoryFull(links,machines))return;report.factoryRestartRestored=true;File.WriteAllText(FactoryMarker("observer-ready"),"restart restored three private-filtered links");factoryStep=1;}
            else if(links.Count==0&&FactoryRenderGone()){report.factoryRemoved=true;File.WriteAllText(FactoryMarker("observer-removed"),"removal replicated");if(factorySpec.shipyard&&!ShipyardObserver(true))return;if(factorySpec.buildings&&!BuildingObserver(true))return;_ = Finish(true,null);}
        }
        void FactoryOwnerDone(){File.WriteAllText(FactoryMarker("owner-done"),"created and reconnected");_ = Finish(true,null);}
        bool FactoryRenderGone(){if(report.headlessClient)return true;if(!session.FactoryVisualsMatch(0))return false;report.factoryRenderCleared=true;return true;}
    }
}

#endif
