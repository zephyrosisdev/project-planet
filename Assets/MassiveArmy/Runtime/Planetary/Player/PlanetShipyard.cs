using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        PlanetShipyard shipyard;
        internal bool ShipyardOpen=>shipyard?.Open==true;
        internal bool ShipToolActive=>shipyard?.ToolActive==true;
        internal void UpdateShipyard(){if(!IsSurvival||IndustrialAuthority==null||spacecraft==null)return;shipyard??=new PlanetShipyard(this);shipyard.Tick();}
        internal void BeginShipWiring(){shipyard?.BeginWiring();}
        internal void PrepareShipTool(bool clearEquipment=true){industry?.StopConstructionTools();CancelRespawnPlacement();if(clearEquipment&&PersonalInventory!=null)PersonalInventory.EquippedSlot=-1;}
    }
    /// <summary>First-person adapter; mutations use the existing locked, journaled authority.</summary>
    internal sealed class PlanetShipyard:IDisposable {
        readonly PlanetDevelopment world;readonly RtsUiController ui;VisualElement panel;ScrollView sections,circuits;Label feedback;Material preview;Mesh marker;string placing,message="F6 ship assembly · build while landed or docked";ShipModuleRecord hovered;ShipSocket hoverSocket;ShipModuleRecord ghost;long wireSource;bool wiring;double ready;Mesh wirePreview;readonly ModularGeometry wireGeometry=new ModularGeometry();
        readonly List<FramePose> route=new List<FramePose>();
        public bool Open {get;private set;}public bool ToolActive=>placing!=null||wiring;
        IndustrialWorld Data=>world.IndustrialAuthority.World;ShipAssembly Ship=>Data.Ship(1);PlanetSpacecraft Craft=>world.ShipForValidation;
        public PlanetShipyard(PlanetDevelopment world){this.world=world;ui=new RtsUiController(world.transform,Build,SyncVisibility,()=>Open);ui.Attach();preview=ColonyPrototypeLibrary.Material("Ship socket preview");preview.SetFloat("_Preview",1);preview.SetFloat("_ZWrite",0);preview.renderQueue=3000;var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);marker=UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);cube.SetActive(false);ArmyLifetime.Destroy(cube);}
        FramePose Local(Vector3 p)=>ShipModules.P(p.x,p.y,p.z);
        void SyncVisibility(){
            if(ui.Root==null)return;
            ui.Root.style.display=Open?DisplayStyle.Flex:DisplayStyle.None;
            ui.Root.pickingMode=PickingMode.Ignore;
            if(panel!=null)panel.style.display=Open?DisplayStyle.Flex:DisplayStyle.None;
        }
        void Build(VisualElement root){
            // This controller uses the shared Front template; discard its full-screen
            // menu/HUD layers before adding the shipyard's own window.
            root.Clear();root.pickingMode=PickingMode.Ignore;
            root.style.display=Open?DisplayStyle.Flex:DisplayStyle.None;
            panel=new VisualElement{name="shipyard-panel"};panel.style.display=Open?DisplayStyle.Flex:DisplayStyle.None;panel.style.position=Position.Absolute;panel.style.left=Length.Percent(15);panel.style.right=Length.Percent(15);panel.style.top=Length.Percent(8);panel.style.bottom=Length.Percent(8);panel.style.backgroundColor=new Color(.035f,.055f,.075f,.98f);panel.style.paddingLeft=panel.style.paddingRight=18;panel.style.paddingTop=panel.style.paddingBottom=16;root.Add(panel);
            panel.Add(new Label("SHIP ASSEMBLY / POWER CIRCUITS"));panel.Add(new Label("F6 close · sections snap to compatible sockets · resource costs are consumed on placement"));feedback=new Label();feedback.style.whiteSpace=WhiteSpace.Normal;panel.Add(feedback);
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexGrow=1;panel.Add(row);sections=new ScrollView();sections.style.width=Length.Percent(48);circuits=new ScrollView();circuits.style.flexGrow=1;row.Add(sections);row.Add(circuits);panel.Add(new Button(()=>{SetOpen(false);BeginWiring();}){text="WIRE POWER · core → route points → system"});panel.Add(new Button(()=>SetOpen(false)){name="shipyard-close",text="CLOSE [F6 / ESC]"});Refresh();
        }
        void SetOpen(bool value){Open=value;SyncVisibility();if(value){world.PrepareShipTool();world.CloseIndustryMenu();placing=null;wiring=false;world.CancelRespawnPlacement();}if(value)world.SetIndustryCursor(true);else world.SetPersonalCursor(false);if(value)Refresh();ui.Tick();}
        void Refresh(){if(panel==null||Ship==null)return;Ship.UpdatePower();feedback.text=message+$"\n{Ship.modules.Count}/64 sections · {Ship.Mass/1000:0.0} tonnes · "+(Data.creativeMode?"Creative: free construction":"Costs paid from your inventory");sections.Clear();circuits.Clear();
            foreach(var spec in ShipModules.Definitions.Values){string id=spec.id;string cost="";foreach(var stack in spec.cost)cost+=$"{stack.count} {stack.item}  ";var button=new Button(()=>{placing=id;wiring=false;ready=Time.unscaledTimeAsDouble+.2;SetOpen(false);message="Aim at an open "+spec.sockets[0].kind+" socket · LMB place · Esc cancel";}){text="BUILD "+id.ToUpperInvariant()+"\n"+cost};button.style.whiteSpace=WhiteSpace.Normal;sections.Add(button);}
            foreach(var m in Ship.modules){long id=m.id;var def=ShipModules.Definitions[m.definition];var line=new VisualElement();line.style.marginBottom=8;circuits.Add(line);line.Add(new Label($"#{id} {m.definition}  "+(def.generation>0?$"{def.generation:0} power":def.power>0?(m.powered?"POWERED":"NO POWER"):$"{def.mass/1000:0.#} t")));if(m.parent!=0)line.Add(new Button(()=>{Execute(new IndustrialCommand{kind="ship-remove",source=id});Refresh();}){text="DISMANTLE / RECOVER COMPONENTS"});var cable=Ship.wires.Find(w=>w.target==id);if(cable!=null)line.Add(new Button(()=>{Execute(new IndustrialCommand{kind="ship-unwire",source=cable.source,target=id});Refresh();}){text="DISCONNECT POWER"});}
        }
        CommandResult Execute(IndustrialCommand command){var actor=Data.Actor(1);actor.pose=world.RespawnPose(world.PlayerForValidation.PreciseBodyPosition);command.sequence=actor.lastSequence+1;var result=world.IndustrialAuthority.Execute(1,command);message=result.message;if(result.accepted){Craft.SetAssembly(Ship);world.IndustrialAuthority.Save();}world.SurvivalNotice=message;return result;}
        string Validate(IndustrialActor actor,ShipAssembly ship,IndustrialCommand command){
            if(!Craft.Landed&&!Craft.Docked)return "Land or dock before editing the ship";
            if(actor.pose.body!=world.definition.bodyId)return "Wrong planet reference frame";
            var m=ship.Module(command.source);if(m==null)return "Missing source section";
            Vector3 point=command.kind=="ship-place"?Craft.ShipModulePoint(m,ShipModules.Socket(m,command.item)?.pose??default):PlanetSpacecraft.PowerPoint(m);
            if((Craft.LocalToBodyPrecise(point)-world.PlayerForValidation.PreciseBodyPosition).Magnitude>24)return "Move within 24 m of this ship socket";
            if(command.kind=="ship-remove"){
                if(Craft.ModuleContains(m,Craft.BodyToLocal(world.PlayerForValidation.PreciseBodyPosition)))return "Leave the section before dismantling it";
                if(world.GroundVehicles?.AnyVehicleInside(Craft,m)==true)return "Unload vehicles before removing this section";
                foreach(var pointRecord in Data.respawnPoints)if(pointRecord.pose.frame==1&&Craft.ModuleContains(m,new Vector3((float)pointRecord.pose.x,(float)pointRecord.pose.y,(float)pointRecord.pose.z)))return "Recover sleeping bags or pods from this section first";
            }
            return null;
        }
        internal void CloseForOtherMenu(){if(Open)SetOpen(false);CancelTool();}
        internal void CancelTool(){placing=null;wiring=false;wireSource=0;route.Clear();}
        internal void BeginWiring(){world.PrepareShipTool(false);placing=null;wiring=true;wireSource=0;route.Clear();ready=Time.unscaledTimeAsDouble+.2;message="Ship wiring: select a core output, click routing points, then a system input. RMB undo; Esc cancel.";world.SurvivalNotice=message;}
        void Aim(){hovered=null;hoverSocket=null;ghost=null;float best=.8f*.8f;var eye=world.View.transform.position;var forward=world.View.transform.forward;
            foreach(var m in Ship.modules){var def=ShipModules.Definitions[m.definition];foreach(var socket in def.sockets){if(placing!=null){var input=Array.Find(ShipModules.Definitions[placing].sockets,s=>s.kind==socket.kind&&!s.kind.StartsWith("Power",StringComparison.Ordinal));if(input==null||Ship.Occupied(m.id,socket.name)||m.parent!=0&&m.inputSocket==socket.name)continue;}else if(wiring){if(wireSource==0?def.generation<=0||socket.kind!="PowerOutput":def.power<=0||socket.kind!="PowerInput")continue;}else continue;
                    var p=Craft.LocalToRender(Craft.ShipModulePoint(m,socket.pose));var delta=p-eye;float along=Vector3.Dot(delta,forward);if(along<0||along>24)continue;DrawMarker(p,new Color(.1f,.8f,1,.8f));float distance=(delta-forward*along).sqrMagnitude;if(distance>=best)continue;best=distance;hovered=m;hoverSocket=socket;
            }}
            if(hovered!=null)world.SurvivalNotice=message+" · "+hovered.definition+" #"+hovered.id+" / "+hoverSocket.name;
            if(placing!=null&&hovered!=null){var input=Array.Find(ShipModules.Definitions[placing].sockets,s=>s.kind==hoverSocket.kind);ghost=new ShipModuleRecord{definition=placing,pose=ShipModules.Attach(hovered,hoverSocket.name,placing,input.name)};var asset=ShipPrototypeLibrary.Find(placing);string error=world.BuildingPolicyError??Data.ShipCommandError(1,new IndustrialCommand{kind="ship-place",source=hovered.id,item=hoverSocket.name,definition=placing,operationRole=input.name});bool free=error==null;if(error!=null)world.SurvivalNotice=error;preview.SetColor("_PreviewTint",free?new Color(.1f,1,.5f,.45f):new Color(1,.15f,.1f,.45f));if(asset!=null)Graphics.DrawMesh(asset.Mesh(placing),Matrix4x4.TRS(Craft.LocalToRender(new Vector3((float)ghost.pose.x,(float)ghost.pose.y,(float)ghost.pose.z)),Craft.Rotation*Quaternion.Euler(0,ghost.pose.yaw,0),Vector3.one),preview,0,world.View);}
        }
        void DrawMarker(Vector3 point,Color color){preview.SetColor("_PreviewTint",color);Graphics.DrawMesh(marker,Matrix4x4.TRS(point,Quaternion.identity,Vector3.one*.22f),preview,0,world.View);}
        Vector3 AimRoutePoint(){var ray=new Ray(world.View.transform.position,world.View.transform.forward);var p=ray.origin+ray.direction*4;if(Physics.Raycast(ray,out var hit,24,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))p=hit.point+hit.normal*.07f;return Craft.RenderToLocal(p);}
        void WirePreview(){if(!wiring||wireSource==0)return;if(wirePreview==null)wirePreview=new Mesh{name="Ship cable ghost"};var g=wireGeometry;g.Clear();var previous=PlanetSpacecraft.PowerPoint(Ship.Module(wireSource));foreach(var p in route){var next=new Vector3((float)p.x,(float)p.y,(float)p.z);PlanetSpacecraft.ShipCableGeometry(g,previous,next);previous=next;}PlanetSpacecraft.ShipCableGeometry(g,previous,hovered==null?AimRoutePoint():PlanetSpacecraft.PowerPoint(hovered));g.BuildInto(wirePreview);preview.SetColor("_PreviewTint",new Color(1,.5f,.05f,.8f));Graphics.DrawMesh(wirePreview,Matrix4x4.TRS(Craft.LocalToRender(Vector3.zero),Craft.Rotation,Vector3.one),preview,0,world.View);}
        public void Tick(){
            Data.ShipActionValidator=Validate;
            if(Ship==null){Execute(new IndustrialCommand{kind="ship-initialize"});message="F6 ship assembly / wiring · F8 quad and jeep test deployment";world.SurvivalNotice=message;}Craft.SetAssembly(Ship);ui.Tick();if(world.LocalPlayerDead){if(Open)SetOpen(false);placing=null;wiring=false;return;}
            var keys=Keyboard.current;var mouse=Mouse.current;if(!Application.isFocused)return;
            if(keys?.f6Key.wasPressedThisFrame==true&&world.IsFirstPerson&&!world.IndustrialDriving)SetOpen(!Open);
            if(keys?.escapeKey.wasPressedThisFrame==true){if(Open)SetOpen(false);placing=null;wiring=false;route.Clear();}
            if(Open||world.Paused||!world.IsFirstPerson||world.PersonalMenuOpen||!ToolActive)return;Aim();WirePreview();
            if(wiring&&mouse?.rightButton.wasPressedThisFrame==true){if(route.Count>0)route.RemoveAt(route.Count-1);else wireSource=0;}
            if(mouse?.leftButton.wasPressedThisFrame!=true||Time.unscaledTimeAsDouble<ready)return;
            if(placing!=null&&ghost!=null){var input=Array.Find(ShipModules.Definitions[placing].sockets,s=>s.kind==hoverSocket.kind);Execute(new IndustrialCommand{kind="ship-place",source=hovered.id,item=hoverSocket.name,definition=placing,operationRole=input.name});}
            else if(wiring){if(wireSource==0){if(hovered!=null)wireSource=hovered.id;}else if(hovered!=null&&keys?.shiftKey.isPressed!=true){if(Execute(new IndustrialCommand{kind="ship-wire",source=wireSource,target=hovered.id,route=new List<FramePose>(route)}).accepted){wireSource=0;route.Clear();}}else if(route.Count<8)route.Add(Local(AimRoutePoint()));else message="Eight route points maximum";world.SurvivalNotice=message;}
        }
        public void Dispose(){Data.ShipActionValidator=null;ui.Dispose();ArmyLifetime.Destroy(preview);ArmyLifetime.Destroy(marker);ArmyLifetime.Destroy(wirePreview);}
    }
    public sealed partial class PlanetVehiclePool {
        internal bool AnyVehicleInside(PlanetSpacecraft ship,ShipModuleRecord module){for(int i=0;i<Count;i++)if(vehicles[i].Alive&&ship.ModuleContains(module,ship.BodyToLocal(vehicles[i].Motor.Pose.position),1))return true;return false;}
    }
}
