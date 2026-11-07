using System;
using System.Collections;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        PlayerDeathScreen deathScreen;RespawnPointMenu respawnPointMenu;RespawnPointView respawnView;bool deathPresented;
        internal bool LocalPlayerDead=>IsSurvival&&player?.Environment!=null&&player.Environment.Health<=0;
        internal bool RespawnPending {get;private set;}
        internal bool RespawnPointMenuOpen=>respawnPointMenu?.Open==true;
        internal bool RespawnPlacementActive=>respawnView?.Placing!=null;
        internal string RespawnPlacementPrompt=>RespawnPlacementActive?respawnView.Notice:"";
        internal bool HasRespawnCarrier=>spacecraft!=null&&(SystemRuntime==null||SystemRuntime.RespawnCarrierWorld==this);
        internal void AttachDeathUi(VisualElement parent){if(!IsSurvival)return;deathScreen?.Dispose();deathScreen=new PlayerDeathScreen(this,parent);respawnPointMenu?.Dispose();respawnPointMenu=new RespawnPointMenu(this,parent);if(LocalPlayerDead)deathScreen.Show();}
        internal void CloseRespawnPointMenu()=>respawnPointMenu?.Close();
        internal void CancelRespawnPlacement()=>respawnView?.Cancel();
        internal void BeginRespawnPlacement(string item){if(LocalPlayerDead||!IndustrialWorld.RespawnItem(item))return;if(!IsFirstPerson||IndustrialDriving){SurvivalNotice="Leave the vehicle before placing a respawn point";return;}industry?.StopConstructionTools();CloseIndustryMenu();personalHud?.Close();respawnView??=new RespawnPointView(this);respawnView.Begin(item);SetPersonalCursor(false);SurvivalNotice="LMB place · R rotate · Esc cancel";}
        void UpdateRespawnGameplay(){
            if(!IsSurvival||IndustrialAuthority==null)return;respawnView??=new RespawnPointView(this);if(RespawnPointMenuOpen){if(UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)respawnPointMenu.Close();else respawnPointMenu.Refresh();}
            if(LocalPlayerDead){
                if(!deathPresented){deathPresented=true;equippedConstructionItem=null;equippedConstructionSlot=-2;inventorySlotUi?.Cancel();industry.StopForPlayerDeath();groundVehicles?.ReleaseControl();respawnPointMenu?.Close();personalHud?.Close();respawnView.Cancel();usingItem=0;if(heldItem!=null)heldItem.SetActive(false);player.SetActive(false);spacecraft?.SetActive(false);IndustrialAuthority.RecordPlayerDeath(1);deathScreen?.Show();}
                SetPersonalCursor(true);deathScreen?.Refresh();
            }else if(deathPresented){deathPresented=false;deathScreen?.Hide();}
            if(!LocalPlayerDead&&respawnView.Placing!=null)SurvivalNotice=respawnView.Notice;
        }
        internal FramePose RespawnPose(Double3 point,float yaw=0)=>new FramePose{body=definition.bodyId,x=point.x,y=point.y,z=point.z,yaw=yaw};
        internal bool TryResolveRespawnPose(FramePose pose,out Double3 position,out Quaternion rotation){
            if(pose.frame==1){if(!HasRespawnCarrier){position=default;rotation=Quaternion.identity;return false;}position=spacecraft.LocalToBodyPrecise(new Vector3((float)pose.x,(float)pose.y,(float)pose.z));rotation=spacecraft.Rotation*Quaternion.Euler(0,pose.yaw,0);return true;}
            position=new Double3(pose.x,pose.y,pose.z);rotation=Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(position))*Quaternion.Euler(0,pose.yaw,0);return pose.frame==0&&pose.body==definition.bodyId;
        }
        internal FramePose RespawnBodyPose(FramePose pose){if(!TryResolveRespawnPose(pose,out var body,out _))throw new InvalidOperationException("Respawn frame is not resident");return RespawnPose(body);}
        internal string RespawnLocationName(RespawnPoint point){if(point.pose.frame==1)return "Aboard your ship · follows its movement";if(point.pose.body==definition.bodyId)return definition.displayName;var bodies=SystemRuntime?.Bodies;if(bodies!=null)foreach(var body in bodies)if(body.bodyId==point.pose.body)return body.displayName;return "Planet "+point.pose.body;}
        internal string RespawnAvailability(RespawnPoint point){if(point==null)return null;if(point.pose.frame==1)return SystemRuntime?.RespawnCarrierWorld?.ShipForValidation!=null||HasRespawnCarrier?null:"Ship unavailable";if(point.pose.body==definition.bodyId)return null;var bodies=SystemRuntime?.Bodies;if(bodies!=null)foreach(var body in bodies)if(body.bodyId==point.pose.body)return null;return "Planet unavailable";}
        internal string RequestPlayerRespawn(long id){
            if(!LocalPlayerDead||RespawnPending)return "Respawn is not available";var point=IndustrialAuthority.World.RespawnPoint(id);if(id!=0&&(point==null||point.owner!=1))return "Respawn point was removed";if(point!=null&&point.readyAtTick>IndustrialAuthority.World.tick)return "This respawn point is cooling down";var error=RespawnAvailability(point);if(error!=null)return error;RespawnPending=true;StartCoroutine(RespawnPlayer(id));return null;
        }
        IEnumerator RespawnPlayer(long id){
            var destination=this;string failure=null;var point=IndustrialAuthority.World.RespawnPoint(id);
            if(point!=null&&SystemRuntime!=null){
                if(point.pose.frame==1)destination=SystemRuntime.RespawnCarrierWorld;
                else{SystemRuntime.PrepareRespawnPlanet(point.pose.body);float deadline=Time.realtimeSinceStartup+90;while((destination=SystemRuntime.PreparedRespawnPlanet(point.pose.body))==null&&Time.realtimeSinceStartup<deadline)yield return null;if(destination==null)failure="Destination could not be prepared";}
            }
            if(failure==null){
                if(destination!=this)SystemRuntime.SelectRespawnWorld(destination);
                destination.RespawnPending=true;destination.EnsureRespawnIndustry();
                FramePose anchor=point?.pose??destination.RespawnPose(destination.surface.GetSurfacePositionPrecise(destination.rig.FocusDirection,.05));
                if(!destination.TryResolveRespawnPose(anchor,out var body,out var rotation))failure="Respawn frame is unavailable";
                else{
                    // Move only the dead observer while nearby collision chunks are prepared.
                    destination.modes.Switch(PlayerControlMode.FirstPerson);destination.PlaceRespawnObserver(anchor,body,rotation);destination.player.SetActive(false);destination.playerConstruction?.PrepareRespawnCollision();destination.spacecraft?.Rebase();Physics.SyncTransforms();yield return null;
                    destination.playerConstruction?.PrepareRespawnCollision();Physics.SyncTransforms();
                    if(!destination.TryRespawnExit(anchor,point?.definition,out var feet,out rotation))failure="Respawn exit is blocked or has no safe floor. Clear the area or choose another point.";
                    else{
                        var resolved=destination.RespawnPose(feet);failure=destination.IndustrialAuthority.UseRespawnPoint(1,id,resolved);
                        if(failure==null){var state=destination.player.Environment;state.Health=state.Oxygen=state.Hunger=state.Thirst=100;destination.player.WalkSpeed=7;destination.PlaceRespawnObserver(anchor,feet,rotation);destination.AutomaticPlayerStep=true;destination.deathPresented=false;destination.deathScreen?.Hide();destination.SetPersonalCursor(false);destination.IndustrialAuthority.Save();destination.SurvivalNotice="Respawned";}
                    }
                }
                destination.RespawnPending=false;if(failure!=null){destination.player.SetActive(false);destination.SetPersonalCursor(true);destination.deathScreen?.Show();destination.deathScreen?.Feedback(failure);}
            }
            RespawnPending=false;if(failure!=null)deathScreen?.Feedback(failure);
        }
        void EnsureRespawnIndustry(){industry??=new PlanetIndustry(this,surface);}
        void PlaceRespawnObserver(FramePose anchor,Double3 feet,Quaternion rotation){if(anchor.frame==1)player.EnterShip(spacecraft,spacecraft.BodyToLocal(feet),Quaternion.Inverse(spacecraft.Rotation)*(rotation*Vector3.forward));else player.EnterBodyPosition(feet,rotation*Vector3.forward);spacecraft?.Rebase();playerConstruction?.RebaseColliders();}
        readonly Collider[] respawnObstructions=new Collider[32];
        bool TryRespawnExit(FramePose anchor,string type,out Double3 feet,out Quaternion rotation){
            TryResolveRespawnPose(anchor,out var centre,out rotation);var up=rotation*Vector3.up;
            var size=type==null?Vector3.zero:RespawnPointView.Size(type);
            var offsets=type==null?new[]{Vector3.zero,new Vector3(2,0,0),new Vector3(-2,0,0)}:new[]{new Vector3(size.x*.5f+.65f,0,0),new Vector3(-size.x*.5f-.65f,0,0),new Vector3(0,0,-size.z*.5f-.65f),new Vector3(0,0,size.z*.5f+.65f)};
            foreach(var offset in offsets){var body=centre+new Double3(rotation*offset);var at=RenderBodyPosition(body);Double3 floor;
                if(Physics.Raycast(at+up*1.7f,-up,out var hit,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&Vector3.Dot(hit.normal,up)>.65f&&(anchor.frame==0||spacecraft.Owns(hit.collider)))floor=RenderToBody(hit.point)+new Double3(up)*.06;
                else if(anchor.frame==0){floor=surface.GetSurfacePositionPrecise(PlanetPrecisionPolicy.Direction(body),.06);if(type!=null&&Math.Abs((floor.x-centre.x)*up.x+(floor.y-centre.y)*up.y+(floor.z-centre.z)*up.z)>.6)continue;}else continue;
                var render=RenderBodyPosition(floor);int count=Physics.OverlapCapsuleNonAlloc(render+up*.4f,render+up*1.45f,.34f,respawnObstructions,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);bool blocked=count==respawnObstructions.Length;
                for(int i=0;i<count&&!blocked;i++)if(!player.OwnsCollider(respawnObstructions[i]))blocked=true;
                if(!blocked){feet=floor;return true;}
            }feet=default;return false;
        }
        internal bool InteractRespawnPoint(){var point=respawnView?.Aim();if(point==null)return false;CloseIndustryMenu();personalHud?.Close();respawnPointMenu?.Show(point);return true;}

        internal bool RespawnSurfaceHit(Ray ray,out Double3 body){for(double t=.3;t<=10;t+=.1){var p=RenderToBody(ray.GetPoint((float)t));if(p.Magnitude<=definition.radius+surface.SampleHeight(p)){body=surface.GetSurfacePositionPrecise(PlanetPrecisionPolicy.Direction(p),0);return true;}}body=default;return false;}
        void DisposeRespawn(){deathScreen?.Dispose();respawnPointMenu?.Dispose();respawnView?.Dispose();}
    }
}







