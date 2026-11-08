#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Planetary;
using MassiveArmy.Industrial;
using UnityEngine;

namespace MassiveArmy.Networking {
    internal sealed partial class SurvivalSessionProbe {
        int buildingStep;double buildingReplayAt;bool buildingReturnApproached;
        List<SurvivalBuildingPart> ProbeBuildings(){var parts=new List<SurvivalBuildingPart>();foreach(var entity in session.Visible.Values)if(entity.kind=="building"){if(entity.data.Contains("constructionPaid")||entity.data.Contains("constructionActivity")){_ = Finish(false,"Construction receipts leaked into public building state");return parts;}var part=JsonUtility.FromJson<SurvivalBuildingPart>(entity.data);if(part.owner==factorySpec.owner)parts.Add(part);}return parts;}
        void BuildingSend(PlayerBuildCommand command){factoryReply=null;factoryRequest=new SurvivalAction{kind="building",building=command};session.Submit(factoryRequest);}
        bool BuildingStock(bool recovered){var inventory=session.Latest.self.inventory;long belts=report.factoryPhase=="create"?factorySpec.beltCost:0;return inventory.Count("stone")==4-(recovered?0:2)&&inventory.Count("iron-bar")==12-(recovered?0:6)&&inventory.Count("gear")==100-belts-(recovered?0:1)&&Math.Abs(inventory.ActivityOf("iron-bar",inventory.Count("iron-bar"))-(recovered?.5:.25))<1e-8;}
        bool BuildingWalkTo(){
            if(report.factoryPhase=="recover"&&!buildingReturnApproached){
                var surface=new PlanetSurface(session.ClientSimulationDefinition);var origin=SurvivalVehicleSimulation.Position(factorySpec.buildingGround);var up=PlanetPrecisionPolicy.Direction(origin);var rotation=Quaternion.FromToRotation(Vector3.up,up);
                var around=surface.ProjectToSurface(origin+new Double3(rotation*new Vector3(8,0,4)),.05);var waypoint=new FramePose{body=factorySpec.buildingGround.body,x=around.x,y=around.y,z=around.z};
                if(!FactoryWalkTo(waypoint))return false;buildingReturnApproached=true;
            }
            if(session.Latest.self.pose.Distance(factorySpec.buildingStaging)<=1.5){factoryWalkGoal=null;return true;}factoryWalkGoal=factorySpec.buildingStaging;return false;}
        bool BuildingVisuals(int parts){if(report.headlessClient)return true;if(!session.BuildingVisualsMatch(parts))return false;if(parts==0)report.buildingRenderCleared=true;else report.buildingRendered=true;return true;}
        bool BuildingOwner(bool recover){
            var parts=ProbeBuildings();var root=parts.Find(p=>p.kind==PlayerModuleKind.FoundationSquare);var wall=parts.Find(p=>p.kind==PlayerModuleKind.DoorwaySingle);var door=parts.Find(p=>p.kind==PlayerModuleKind.DoorSingle);
            if(recover){
                switch(buildingStep){
                    case 0:if(parts.Count!=3||door?.open!=true||!BuildingStock(false)||!BuildingVisuals(3))return false;report.buildingRestored=true;if(!BuildingWalkTo()||!File.Exists(FactoryMarker("building-observer-restored")))return false;BuildingSend(new PlayerBuildCommand{operation="remove",site=door.site,module=door.id});buildingStep=1;break;
                    case 1:if(!FactoryReplied(true)||wall==null)return false;BuildingSend(new PlayerBuildCommand{operation="remove",site=wall.site,module=wall.id});buildingStep=2;break;
                    case 2:if(!FactoryReplied(true)||root==null)return false;BuildingSend(new PlayerBuildCommand{operation="remove",site=root.site,module=root.id});buildingStep=3;break;
                    case 3:if(!FactoryReplied(true))return false;buildingStep=4;break;
                    case 4:if(parts.Count!=0||!BuildingStock(true)||!BuildingVisuals(0)||!File.Exists(FactoryMarker("building-observer-removed")))return false;report.buildingRecovered=report.buildingConserved=true;return true;
                }return false;
            }
            switch(buildingStep){
                case 0:if(!BuildingWalkTo())return false;BuildingSend(new PlayerBuildCommand{operation="place",kind=PlayerModuleKind.FoundationSquare,pose=factorySpec.buildingGround});buildingStep=1;break;
                case 1:if(!FactoryReplied(true))return false;buildingStep=2;break;
                case 2:if(root==null)return false;BuildingSend(new PlayerBuildCommand{operation="place",site=root.site,parent=root.id,socket=1,input=0,kind=PlayerModuleKind.DoorwaySingle});buildingStep=3;break;
                case 3:if(!FactoryReplied(true))return false;buildingStep=4;break;
                case 4:if(wall==null)return false;BuildingSend(new PlayerBuildCommand{operation="place",site=wall.site,parent=wall.id,socket=2,input=0,kind=PlayerModuleKind.DoorSingle});buildingStep=5;break;
                case 5:if(!FactoryReplied(true))return false;buildingStep=6;break;
                case 6:if(door==null)return false;BuildingSend(new PlayerBuildCommand{operation="door",site=door.site,module=door.id});buildingStep=7;break;
                case 7:if(!FactoryReplied(true))return false;session.RepeatProbeAction(factoryRequest);buildingReplayAt=Time.realtimeSinceStartupAsDouble;buildingStep=8;break;
                case 8:if(parts.Count!=3||door?.open!=true||!BuildingStock(false)||!BuildingVisuals(3)||Time.realtimeSinceStartupAsDouble-buildingReplayAt<1||!File.Exists(FactoryMarker("building-observer-created")))return false;lastPose=session.Latest.self.pose;phase=3;_ = Reconnect();buildingStep=9;break;
                case 9:if(phase!=4||parts.Count!=3||door?.open!=true||!BuildingStock(false)||!BuildingVisuals(3))return false;if(lastPose.Distance(session.Latest.self.pose)>session.Config.walkSnap){_ = Finish(false,"Building reconnect changed the authoritative position");return false;}report.buildingCreated=report.buildingConserved=report.buildingReconnect=true;return true;
            }return false;
        }
        bool BuildingObserver(bool recover){
            var parts=ProbeBuildings();var root=parts.Find(p=>p.kind==PlayerModuleKind.FoundationSquare);var door=parts.Find(p=>p.kind==PlayerModuleKind.DoorSingle);report.buildingPrivacy=true;
            if(recover){if(parts.Count==3&&door?.open==true&&BuildingVisuals(3)){report.buildingRestored=true;File.WriteAllText(FactoryMarker("building-observer-restored"),"open door and structural state survived restart");}if(report.buildingRestored&&parts.Count==0&&BuildingVisuals(0)){report.buildingRecovered=true;File.WriteAllText(FactoryMarker("building-observer-removed"),"building removal replicated");return true;}return false;}
            switch(buildingStep){
                case 0:if(parts.Count!=3||door?.open!=true||!BuildingVisuals(3))return false;BuildingSend(new PlayerBuildCommand{operation="remove",site=root.site,module=root.id});buildingStep=1;break;
                case 1:if(!FactoryReplied(false))return false;if(door==null)return false;BuildingSend(new PlayerBuildCommand{operation="door",site=door.site,module=door.id});buildingStep=2;break;
                case 2:if(!FactoryReplied(false))return false;buildingStep=3;break;
                case 3:if(parts.Count!=3||door?.open!=true)return false;report.buildingCreated=true;File.WriteAllText(FactoryMarker("building-observer-created"),"foreign edits rejected; public structure received");return true;
            }return false;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        internal bool BuildingVisualsMatch(int parts){if(clientBuildings.Count!=parts||dirtyBuildingChunks.Count!=0)return false;if(parts==0)return buildingViews.Count==0&&buildingMeshes.Count==0;foreach(var mesh in buildingMeshes.Values)if(mesh==null||mesh.vertexCount==0)return false;return buildingViews.Count>0&&buildingViews.Count==buildingMeshes.Count;}
    }
}
#endif
