using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly Dictionary<long,GameObject> entityViews=new Dictionary<long,GameObject>();
        readonly Dictionary<long,Double3> entityPositions=new Dictionary<long,Double3>();
        Material playerMaterial,machineMaterial,depositMaterial,shipMaterial;Mesh proxyCube;
        readonly Dictionary<string,Mesh> combinedShips=new Dictionary<string,Mesh>();
        readonly Dictionary<long,int> renderedShipDoors=new Dictionary<long,int>();
        readonly Dictionary<long,ShipAssembly> renderedShipLayouts=new Dictionary<long,ShipAssembly>();
        int shipMeshesBuiltThisFrame;
        Mesh ShipMesh(ShipAssembly layout,SurvivalShipState state){
            var key=new System.Text.StringBuilder();foreach(var module in layout.modules){key.Append(module.definition).Append(':').Append(module.pose.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(module.pose.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(module.pose.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(module.pose.yaw.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(';');}
            key.Append(state?.rearOpen==true).Append(state?.innerOpen==true);string fingerprint=key.ToString();if(combinedShips.TryGetValue(fingerprint,out var mesh))return mesh;
            if(shipMeshesBuiltThisFrame>=1)return null;shipMeshesBuiltThisFrame++;
            var geometry=new ModularGeometry();foreach(var module in layout.modules){var asset=ShipPrototypeLibrary.Find(module.definition);if(asset!=null)geometry.AppendMesh(asset.Mesh("Shared network ship "+module.definition),new Vector3((float)module.pose.x,(float)module.pose.y,(float)module.pose.z),Quaternion.Euler(0,module.pose.yaw,0));}
            if(state!=null){var interior=Interior(state,layout);foreach(var fitting in interior.fittings)if(fitting.visible)geometry.Box(fitting.centre,fitting.size,new Color(.25f,.33f,.4f),fitting.rotation);}
            mesh=geometry.Build("Shared replicated ship layout");combinedShips.Add(fingerprint,mesh);return mesh;
        }
        void RenderEntities(){
            if(clientWorld==null||!clientWorld.Ready)return;
            if(playerMaterial==null){var shader=Shader.Find("HDRP/Lit");playerMaterial=new Material(shader);playerMaterial.SetColor("_BaseColor",Color.cyan);machineMaterial=new Material(shader);machineMaterial.SetColor("_BaseColor",new Color(.35f,.4f,.45f));depositMaterial=new Material(shader);depositMaterial.SetColor("_BaseColor",new Color(.7f,.4f,.15f));}
            shipMeshesBuiltThisFrame=0;PrepareShipPoses();
            foreach(var pair in visible){var entity=pair.Value;if(entity.kind=="machine"&&clientMachines.TryGetValue(pair.Key/8,out var virtualMachine)&&virtualMachine.integratedLogistics)continue;if(entity.pose.body!=clientWorld.definition.bodyId||(entity.kind=="ship-layout"||entity.kind=="factory-link"||entity.kind=="building"))continue;
                if(!entityViews.TryGetValue(pair.Key,out var go)){
                    go=GameObject.CreatePrimitive(entity.kind=="player"?PrimitiveType.Capsule:PrimitiveType.Cube);go.name="Remote "+entity.kind+" "+pair.Key;go.transform.SetParent(clientWorld.transform);Destroy(go.GetComponent<Collider>());
                    var filter=go.GetComponent<MeshFilter>();if(entity.kind!="player"&&proxyCube==null)proxyCube=filter.sharedMesh;
                    go.GetComponent<Renderer>().sharedMaterial=entity.kind=="player"?playerMaterial:entity.kind=="deposit"?depositMaterial:machineMaterial;
                    go.transform.localScale=entity.kind=="player"?new Vector3(.7f,.9f,.7f):entity.kind=="deposit"?new Vector3(1,.3f,1):entity.kind=="respawn-point"?new Vector3(1.5f,.4f,2.5f):entity.kind=="loot"?Vector3.one*.8f:new Vector3(2,2.5f,2);entityViews.Add(pair.Key,go);
                }
                var target=new Double3(entity.pose.x,entity.pose.y,entity.pose.z);Quaternion? craftRotation=null;Vector3? playerUp=null;bool parented=false;
                if(entity.kind=="player"&&ParentPlayerPose(pair.Key,out var crew,out var crewUp,out var crewRotation)){target=crew;playerUp=crewUp;craftRotation=crewRotation;parented=true;}
                if(entity.kind=="ship"||entity.kind=="ship-proxy"){
                    var state=predicted?.ship?.entity==pair.Key/8?predicted.ship:clientShips.GetValueOrDefault(pair.Key/8);
                    if(state!=null&&presentedShips.TryGetValue(pair.Key/8,out var presented)){target=presented.point;craftRotation=presented.rotation;parented=true;}
                    if(entity.kind=="ship"&&clientShipAssemblies.TryGetValue(pair.Key/8,out var layout)){
                        if(!renderedShipLayouts.TryGetValue(pair.Key,out var drawn)||!object.ReferenceEquals(drawn,layout)||!renderedShipDoors.TryGetValue(pair.Key,out int doors)||doors!=((state?.rearOpen==true?1:0)|(state?.innerOpen==true?2:0))){
                            var mesh=ShipMesh(layout,state);if(mesh!=null){go.GetComponent<MeshFilter>().sharedMesh=mesh;go.transform.localScale=Vector3.one;shipMaterial??=ColonyPrototypeLibrary.Material("Network ship modules");go.GetComponent<Renderer>().sharedMaterial=shipMaterial;renderedShipLayouts[pair.Key]=layout;renderedShipDoors[pair.Key]=(state?.rearOpen==true?1:0)|(state?.innerOpen==true?2:0);}
                        }
                    }else{go.GetComponent<MeshFilter>().sharedMesh=proxyCube;go.transform.localScale=new Vector3(52,11,100);go.GetComponent<Renderer>().sharedMaterial=machineMaterial;renderedShipLayouts.Remove(pair.Key);}
                    go.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }else if(entity.kind=="machine"&&clientMachines.TryGetValue(pair.Key/8,out var machine)){
                    go.transform.localScale=new Vector3((float)machine.Definition.Width,2.5f,(float)machine.Definition.Depth);
                    if(machine.pose.frame!=0&&presentedShips.TryGetValue(machine.pose.frame,out var parent)){target=parent.point+Double3.Rotate(parent.rotation,SurvivalVehicleSimulation.Position(machine.pose));craftRotation=parent.rotation*Quaternion.Euler(0,machine.pose.yaw,0);playerUp=parent.rotation*Vector3.up;parented=true;}
                    if(predicted?.vehicle?.entity==machine.id){target=clientWorld.NetworkPresentedFeet;craftRotation=(predicted.ship!=null&&predicted.vehicle.pose.frame==predicted.ship.entity?ShipRotation(predicted.ship):Quaternion.identity)*SurvivalVehicleSimulation.Rotation(predicted.vehicle);}
                }
                if(!parented&&(entity.kind=="player"||entity.kind=="ship"||entity.kind=="machine")&&predicted?.ship?.entity!=pair.Key/8&&predicted?.vehicle?.entity!=pair.Key/8&&entityPositions.TryGetValue(pair.Key,out var previous))target=previous+(target-previous)*(1-System.Math.Exp(-15*Time.unscaledDeltaTime));
                entityPositions[pair.Key]=target;var up=playerUp??PlanetPrecisionPolicy.Direction(target);go.transform.position=clientWorld.RenderBodyPosition(target)+up*(entity.kind=="player"?.9f:entity.kind=="machine"?1.25f:entity.kind=="respawn-point"?.2f:entity.kind=="loot"?.4f:0);go.transform.rotation=craftRotation??Quaternion.FromToRotation(Vector3.up,up)*Quaternion.Euler(0,entity.pose.yaw,0);
            }
            RenderPlacement();RenderFactoryLinks();RenderBuildings();
            var removed=new List<long>();foreach(var pair in entityViews)if(!visible.ContainsKey(pair.Key)){Destroy(pair.Value);removed.Add(pair.Key);}foreach(long id in removed){entityViews.Remove(id);entityPositions.Remove(id);renderedShipLayouts.Remove(id);renderedShipDoors.Remove(id);}
        }
        void ClearEntityViews(){ClearParentPresentations();ClearFactoryViews();ClearBuildings();if(placementGhost!=null)Destroy(placementGhost);placementGhost=null;placingMachine=null;placementPose=null;foreach(var view in entityViews.Values)if(view!=null)Destroy(view);entityViews.Clear();entityPositions.Clear();if(playerMaterial!=null)Destroy(playerMaterial);if(machineMaterial!=null)Destroy(machineMaterial);if(depositMaterial!=null)Destroy(depositMaterial);if(shipMaterial!=null)Destroy(shipMaterial);foreach(var mesh in combinedShips.Values)Destroy(mesh);combinedShips.Clear();renderedShipLayouts.Clear();renderedShipDoors.Clear();proxyCube=null;}
        void OnGUI(){
            if(HeadlessProbeClient||!Online||latest?.self==null)return;
            GUI.Box(new Rect(12,12,550,90),"SURVIVAL ONLINE · "+Status+"\nHealth "+latest.self.health.ToString("0")+" · O₂ "+latest.vitals.oxygen.ToString("0")+" · Food "+latest.vitals.hunger.ToString("0")+" · Water "+latest.vitals.thirst.ToString("0")+"\nEnter text chat · Hold "+Bindings.pushToTalk+" proximity voice · Escape menu");
            string controls=predicted?.vehicle!=null?"DRIVER � W/S drive � A/D steer � Space brake � F exit":predicted?.ship!=null?(latest.vitals.controlledShip!=0?"PILOT · WASD thrust · R/C vertical · Q/E roll · Space land/takeoff · "+Bindings.pilotAssist+" assist · G gear · B brake · F leave seat":"ABOARD · WASD walk · Space jump · F hatch/pilot seat · E disembark"):predicted?.vehicle!=null?"DRIVER · W/S drive · A/D steer · Space brake · F exit":"WASD walk · G gather · F board/drive · I inventory · F4 industry";
            GUI.Label(new Rect(12,104,Screen.width-24,30),string.IsNullOrEmpty(latest.motion.notice)?controls:latest.motion.notice);
            if(placingMachine!=null)GUI.Label(new Rect(12,136,800,30),"PLACE "+placingMachine+" · LMB place · R rotate · RMB cancel");
            if(predicted?.ship==null){foreach(var ship in clientShips.Values)if(ship.owner==ActorId){GUI.Label(new Rect(12,166,800,30),"Your ship boarding ramp: "+latest.self.pose.Distance(ShipEntry(ship)).ToString("0")+" m");break;}}
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!ChatInputCaptured;GameplayGUI();GUI.enabled=enabled;
        }
    }
}
