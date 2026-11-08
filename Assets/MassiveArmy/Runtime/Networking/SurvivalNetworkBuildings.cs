using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalBuildingPart {
        public long site,owner,id,parent;public int parentSocket;public Double3 anchor;public Quaternion rotation;
        public PlayerModuleKind kind;public Vector3 position;public float yaw;public bool open;
        internal PlayerBuildSite Site()=>new PlayerBuildSite{id=site,owner=owner,anchor=anchor,rotation=rotation};
        internal PlayerModuleRecord Record()=>new PlayerModuleRecord{id=id,parent=parent,parentSocket=parentSocket,kind=kind,position=position,yaw=yaw,open=open};
    }
    public sealed partial class SurvivalNetworkSession {
        PlayerBuildAuthority buildings;
        PlayerBuildAuthority BuildingAuthority=>buildings??=new PlayerBuildAuthority(authority,body=>surfaces.GetValueOrDefault(body));
        readonly Dictionary<int,string> publishedBuildJson=new Dictionary<int,string>();
        readonly Dictionary<int,List<(long id,FramePose pose,string json,double extent)>> publishedBuildings=new Dictionary<int,List<(long,FramePose,string,double)>>();
        readonly Dictionary<long,SurvivalBuildingPart> clientBuildings=new Dictionary<long,SurvivalBuildingPart>();
        readonly Dictionary<long,int> clientBuildingBodies=new Dictionary<long,int>();
        PlayerModuleProfile[] buildingProfiles;PlayerModuleProfile[] BuildingProfiles=>buildingProfiles??=PlayerBuildingProfiles.Create(true);
        readonly Dictionary<(long site,Vector3Int chunk),GameObject> buildingViews=new Dictionary<(long,Vector3Int),GameObject>();
        readonly Dictionary<(long site,Vector3Int chunk),Mesh> buildingMeshes=new Dictionary<(long,Vector3Int),Mesh>();
        readonly HashSet<(long site,Vector3Int chunk)> dirtyBuildingChunks=new HashSet<(long,Vector3Int)>();
        Material buildingMaterial;long selectedBuilding;PlayerModuleKind buildKind;int buildTurn;
        static Vector3Int BuildingChunk(Vector3 p)=>new Vector3Int(Mathf.FloorToInt(p.x/12),Mathf.FloorToInt(p.y/12),Mathf.FloorToInt(p.z/12));
        string ExecuteBuilding(long actor,PlayerBuildCommand command){string error=BuildingAuthority.Execute(actor,command);if(error==null)UpdateServerContacts();return error;}
        void PublishBuildings(){
            foreach(var saved in authority.World.buildStates){
                if(!publishedBuildJson.TryGetValue(saved.body,out var previous)||previous!=saved.json){
                    var parts=new List<(long,FramePose,string,double)>();foreach(var site in BuildingAuthority.Storage(saved.body).playerBuildSites)foreach(var module in site.modules){
                        var part=new SurvivalBuildingPart{site=site.id,owner=site.owner,id=module.id,parent=module.parent,parentSocket=module.parentSocket,anchor=site.anchor,rotation=site.rotation,kind=module.kind,position=module.position,yaw=module.yaw,open=module.open};
                        var profile=BuildingProfiles[(int)part.kind];parts.Add((checked(-module.id*4),PlayerBuildAuthority.Pose(saved.body,site,module.position),JsonUtility.ToJson(part),profile.interactionBounds.center.magnitude+profile.interactionBounds.extents.magnitude));
                    }
                    publishedBuildings[saved.body]=parts;publishedBuildJson[saved.body]=saved.json;
                }
                foreach(var part in publishedBuildings[saved.body])AddEntity(part.id,"building",part.pose,part.json,part.extent);
            }
        }
        void ReceiveBuildings(SurvivalSnapshot snapshot){
            foreach(long id in snapshot.removed)if(clientBuildings.Remove(id,out var part)){dirtyBuildingChunks.Add((part.site,BuildingChunk(part.position)));clientBuildingBodies.Remove(id);}
            foreach(var entity in snapshot.changed)if(entity.kind=="building"){
                var part=JsonUtility.FromJson<SurvivalBuildingPart>(entity.data);if(clientBuildings.TryGetValue(entity.id,out var old))dirtyBuildingChunks.Add((old.site,BuildingChunk(old.position)));
                clientBuildings[entity.id]=part;clientBuildingBodies[entity.id]=entity.pose.body;dirtyBuildingChunks.Add((part.site,BuildingChunk(part.position)));
            }
        }
        void RenderBuildings(){
            // At most one mesh rebuild per frame; render objects are per chunk.
            (long site,Vector3Int chunk)? next=null;foreach(var key in dirtyBuildingChunks){next=key;break;}
            if(next.HasValue){var key=next.Value;dirtyBuildingChunks.Remove(key);var geometry=new ModularGeometry();SurvivalBuildingPart first=null;
                foreach(var pair in clientBuildings){var part=pair.Value;if(part.site!=key.site||BuildingChunk(part.position)!=key.chunk||clientBuildingBodies[pair.Key]!=clientWorld.definition.bodyId)continue;first??=part;PlayerBuildingWorld.Append(BuildingProfiles[(int)part.kind],part.open,false,part.position-(Vector3)key.chunk*12,Quaternion.Euler(0,part.yaw,0),geometry);}
                if(buildingMeshes.Remove(key,out var prior))Destroy(prior);
                if(first==null){if(buildingViews.Remove(key,out var removed))Destroy(removed);}
                else{
                    if(!buildingViews.TryGetValue(key,out var go)){go=new GameObject("Replicated building chunk");go.transform.SetParent(clientWorld.transform);go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();buildingViews[key]=go;}
                    buildingMaterial??=ColonyPrototypeLibrary.Material("Replicated construction");var mesh=geometry.Build("Replicated construction chunk");buildingMeshes[key]=mesh;go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=buildingMaterial;
                }
            }
            foreach(var pair in buildingViews){SurvivalBuildingPart part=null;int body=0;foreach(var entry in clientBuildings)if(entry.Value.site==pair.Key.site){part=entry.Value;body=clientBuildingBodies[entry.Key];break;}if(part==null)continue;pair.Value.SetActive(body==clientWorld.definition.bodyId);if(body==clientWorld.definition.bodyId)pair.Value.transform.SetPositionAndRotation(clientWorld.RenderBodyPosition(part.anchor+Double3.Rotate(part.rotation,new Double3((Vector3)pair.Key.chunk*12))),part.rotation);}
        }
        void ClearBuildings(){ClearContacts();buildings=null;publishedBuildJson.Clear();publishedBuildings.Clear();clientBuildings.Clear();clientBuildingBodies.Clear();dirtyBuildingChunks.Clear();foreach(var go in buildingViews.Values)Destroy(go);foreach(var mesh in buildingMeshes.Values)Destroy(mesh);buildingViews.Clear();buildingMeshes.Clear();if(buildingMaterial!=null)Destroy(buildingMaterial);buildingMaterial=null;selectedBuilding=0;}
        void BuildingGUI(){
            GUILayout.Space(12);GUILayout.Label("SURFACE CONSTRUCTION · owner edits · 12 m reach");
            GUILayout.BeginHorizontal();if(GUILayout.Button("Previous module"))buildKind=(PlayerModuleKind)(((int)buildKind+BuildingProfiles.Length-1)%BuildingProfiles.Length);GUILayout.Label(buildKind.ToString());if(GUILayout.Button("Next module"))buildKind=(PlayerModuleKind)(((int)buildKind+1)%BuildingProfiles.Length);GUILayout.EndHorizontal();
            string cost="Cost: ";foreach(var stack in IndustrialCatalog.ModuleCost(buildKind.ToString()))cost+=stack.count+" "+stack.item+"  ";GUILayout.Label(cost);
            if(GUILayout.Button("ROTATE ("+(buildTurn*90)+"°)"))buildTurn=(buildTurn+1)%4;
            if(BuildingProfiles[(int)buildKind].Foundation||BuildingProfiles[(int)buildKind].Room)if(GUILayout.Button("START SITE 6 m IN FRONT")){
                var position=new Double3(latest.self.pose.x,latest.self.pose.y,latest.self.pose.z);var up=PlanetPrecisionPolicy.Direction(position);var forward=Vector3.ProjectOnPlane(SurvivalMovement.Forward(predicted),up).normalized;var ground=position+new Double3(forward)*6;
                Submit(new SurvivalAction{kind="building",building=new PlayerBuildCommand{operation="place",kind=buildKind,pose=new FramePose{body=latest.self.pose.body,x=ground.x,y=ground.y,z=ground.z,yaw=(Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up,up))*Quaternion.LookRotation(forward,up)).eulerAngles.y+buildTurn*90}}});
            }
            foreach(var pair in clientBuildings){var part=pair.Value;if(part.owner!=ActorId||clientBuildingBodies[pair.Key]!=latest.self.pose.body||PlayerBuildAuthority.Pose(latest.self.pose.body,part.Site(),part.position).Distance(latest.self.pose)>12)continue;
                GUILayout.BeginHorizontal();if(GUILayout.Button("SELECT #"+part.id+" "+part.kind))selectedBuilding=pair.Key;
                if(BuildingProfiles[(int)part.kind].Door&&GUILayout.Button(part.open?"CLOSE":"OPEN"))Submit(new SurvivalAction{kind="building",building=new PlayerBuildCommand{operation="door",site=part.site,module=part.id}});
                if(GUILayout.Button("RECOVER"))Submit(new SurvivalAction{kind="building",building=new PlayerBuildCommand{operation="remove",site=part.site,module=part.id}});GUILayout.EndHorizontal();
            }
            if(clientBuildings.TryGetValue(selectedBuilding,out var selected)&&selected.owner==ActorId){var source=BuildingProfiles[(int)selected.kind];var target=BuildingProfiles[(int)buildKind];for(int s=0;s<source.outputs.Length;s++){bool occupied=false;foreach(var part in clientBuildings.Values)if(part.site==selected.site&&part.parent==selected.id&&part.parentSocket==s){occupied=true;break;}if(occupied)continue;for(int i=0;i<target.inputs.Length;i++)if(source.outputs[s].kind==target.inputs[i].kind&&GUILayout.Button("BUILD "+buildKind+" AT #"+selected.id+" socket "+s+" / input "+i))Submit(new SurvivalAction{kind="building",building=new PlayerBuildCommand{operation="place",site=selected.site,parent=selected.id,socket=s,input=i,turn=buildTurn,kind=buildKind}});
                    var socket=source.outputs[s];if(socket.kind!=BuildSocketKind.MachineMount&&!(socket.kind==BuildSocketKind.FloorCenter&&(source.Foundation||selected.kind==PlayerModuleKind.CeilingSquare)))continue;
                    foreach(var stack in latest.self.inventory.items){if(!stack.item.StartsWith("deploy-",StringComparison.Ordinal))continue;string machine=stack.item.Substring(7);if(!IndustrialCatalog.Machines.ContainsKey(machine))continue;if(GUILayout.Button("MOUNT "+machine+" AT #"+selected.id+" socket "+s)){
                        if(stack.slot!=0)Submit(new SurvivalAction{kind="inventory-move",from=new InventoryAddress{slot=stack.slot},to=new InventoryAddress{slot=0},count=stack.count});Submit(new SurvivalAction{kind="slot",slot=0});
                        var q=Quaternion.Euler(0,selected.yaw,0);var point=PlayerBuildAuthority.Point(selected.Site(),selected.position+q*socket.point);var rotation=selected.rotation*Quaternion.Euler(0,selected.yaw+socket.yaw+buildTurn*90,0);float yaw=(Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(point)))*rotation).eulerAngles.y;
                        Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="place",definition=machine,attachmentSite=selected.site,attachmentModule=selected.id,attachmentSocket=s,pose=new FramePose{body=latest.self.pose.body,x=point.x,y=point.y,z=point.z,yaw=yaw}}});
                    }}
                }}
        }
    }
}
