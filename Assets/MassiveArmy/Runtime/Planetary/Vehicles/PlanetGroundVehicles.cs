using UnityEngine;

namespace MassiveArmy.Planetary
{
    public sealed partial class PlanetDevelopment
    {
        PlanetVehiclePool groundVehicles;
        [Header("Ground vehicle detail")]
        [Tooltip("Maximum nearby articulated vehicle rigs. The controlled vehicle has priority; remaining vehicles retain GPU movement and instance-rendered proxies. Higher limits increase local contact/IK and draw cost.")]
        [Range(1,32)] public int detailedVehicleRigLimit=4;
        public bool IsDriving=>modes!=null&&modes.Mode==PlayerControlMode.GroundVehicle;
        public PlanetVehiclePool GroundVehicles=>groundVehicles;
        internal void EnableVehicleSimulation(){movement.EnableKeyword("PLANET_VEHICLES");encounter?.EnableVehicles();catalogRenderer?.EnableVehicles();commands?.EnableVehicles();}
        string VehicleInteractionPrompt=>groundVehicles?.InteractionPrompt??"";
        void InitializeVehicleControl(){modes.Bind(PlayerControlMode.GroundVehicle,enabled=>{if(enabled){player.SetActive(false);Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}else groundVehicles?.ReleaseControl();});}
        internal void ActivateGroundVehicle(){modes.Switch(PlayerControlMode.GroundVehicle,groundVehicles.Controlled.EntityId);}
        internal void AnchorVehicleObserver(Double3 position){origin.Anchor(definition.position.Add(position));playerConstruction?.RebaseColliders();spacecraft?.Rebase();Base?.Rebase();Orbitals?.Tick();}
        internal int VehicleSpawnCapacity=>capacity-unitCount;
        internal uint ConfigureVehicleArchetype(int slot,VehicleDefinition profile,int backing){
            var original=Base.Catalog.units[backing];var a=original.Gpu;var t=original.Traits;
            a.speed=profile.locomotion==VehicleLocomotion.Walker?WalkerKinematics.WalkingSpeed(profile):profile.maximumSpeed;a.turnSpeed=profile.turnSpeed*Mathf.Deg2Rad;a.radius=Mathf.Sqrt(profile.size.x*profile.size.x+profile.size.z*profile.size.z)*.5f;
            a.movement=(uint)(profile.mass<6000?MovementClass.LightVehicle:MovementClass.HeavyVehicle);t.scale=new Vector4(profile.size.x,profile.size.y,profile.size.z,a.movement);
            uint type=(uint)(Base.Catalog.units.Length+1+slot);archetypes.SetData(new[]{a},0,(int)type,1);traits.SetData(new[]{t},0,(int)type,1);return type;
        }
        internal uint AppendVehicleBacking(int type,Vector3 direction){
            if(unitCount>=capacity)throw new System.InvalidOperationException("Army GPU capacity exhausted");uint slot=(uint)unitCount;
            movement.SetInt("_PlanetSpawnFlags",0);int spawn=movement.FindKernel("SpawnCatalog");movement.SetBuffer(spawn,"_PlanetUnits",units);movement.SetBuffer(spawn,"_Archetypes",archetypes);movement.SetInt("_PlanetSpawnSlot",unitCount);movement.SetInt("_PlanetSpawnType",type);movement.SetInt("_PlanetSpawnTeam",0);movement.SetInt("_PlanetSpawnGroup",0);movement.SetVector("_PlanetStart",CubeSphere.Encode(direction));movement.Dispatch(spawn,1,1,1);SetArmyCount(unitCount+1);return slot;
        }
        internal void TransferParkedVehiclesTo(PlanetDevelopment destination){if(groundVehicles?.HasParked!=true)return;destination.spacecraft?.SetAssembly(IndustrialAuthority?.World.Ship(1));destination.groundVehicles??=new PlanetVehiclePool(destination,destination.surface);groundVehicles.TransferParkedTo(destination.groundVehicles);}
        internal void LeaveGroundVehicle(Double3 feet,Vector3 heading,IVehicleCarrier carrier){
            modes.Switch(PlayerControlMode.FirstPerson);
            if(carrier==spacecraft&&spacecraft!=null)player.EnterShip(spacecraft,spacecraft.BodyToLocal(feet),Quaternion.Inverse(spacecraft.Rotation)*heading);
            else player.EnterBodyPosition(feet,heading);
            spacecraft?.Rebase();Base?.Rebase();Orbitals?.Tick();Physics.SyncTransforms();
        }
        void VehicleToRts(){
            var direction=groundVehicles.Controlled.Motor.Pose.position.Float.normalized;groundVehicles.ReleaseControl();rig.SetFocusDirection(direction);modes.Switch(PlayerControlMode.RTS);rig.RefreshPose();
        }
        [Tooltip("Deploy one owned imported tank near the homeworld starting ship.")]
        public bool spawnStartingTank=true;
        void DeployStartingTank(){
            if(!spawnStartingTank||!StrategyEnabled||Base==null||RemoteView||SystemRuntime?.Civilisation!=null&&!SystemRuntime.Civilisation.IsHome(definition.bodyId))return;
            var profile=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/SF-TK1");if(profile==null||profile.ValidateDefinition().Length>0)return;
            groundVehicles??=new PlanetVehiclePool(this,surface);if(groundVehicles.ContainsDefinition(profile.id))return;
            int type=System.Array.FindIndex(Base.Catalog.units,u=>u.id=="tank");if(type<0)return;
            for(int attempt=0;attempt<12;attempt++){
                var local=new Vector3(28+attempt%4*12,-8,-68-attempt/4*12);
                var direction=spacecraft!=null?spacecraft.LocalToBodyPrecise(local).Float.normalized:Base.Offset(new Vector2(local.x,local.z));
                uint id=(uint)unitCount;if(!TrySpawnUnit(type,direction))continue;
                var forward=Vector3.ProjectOnPlane(spacecraft!=null?spacecraft.Rotation*Vector3.forward:Vector3.forward,direction).normalized;
                if(forward.sqrMagnitude<.01f)forward=Vector3.Cross(direction,Vector3.right).normalized;
                groundVehicles.Register(profile,id,new SpacePose{position=surface.GetSurfacePositionPrecise(direction,.025f),rotation=Quaternion.LookRotation(forward,direction)},backingType:type);break;
            }
        }
        // Two explicit test vehicles; these are not army entities or an army mirror.
        void DeployPersonalTestVehicles(){
            if(!IsSurvival||spacecraft==null)return;
            if(!spacecraft.Landed&&!spacecraft.Docked){SurvivalNotice="Land before deploying the quad and jeep (F8)";return;}
            groundVehicles??=new PlanetVehiclePool(this,surface);int added=0;
            string[] ids={"SF-QB1","SF-LUV1"};
            for(int i=0;i<ids.Length;i++){
                if(groundVehicles.ContainsDefinition(ids[i]))continue;
                var profile=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/"+ids[i]);
                if(profile==null||profile.ValidateDefinition().Length>0){SurvivalNotice="Missing quad/jeep prototype assets";continue;}
                var at=spacecraft.LocalToBodyPrecise(new Vector3((i==0?-1:1)*9,-8,-64));var up=at.Float.normalized;
                var forward=Vector3.ProjectOnPlane(spacecraft.Rotation*Vector3.forward,up).normalized;
                var state=new SpacePose{position=surface.GetSurfacePositionPrecise(up,.025f),rotation=Quaternion.LookRotation(forward,up)};
                if(groundVehicles.Register(profile,groundVehicles.NextPersonalId(),state))added++;
            }
            SurvivalNotice=added>0?"Quad / jeep deployed behind the ship. F enters, WASD drives; open both rear doors to board.":"Quad and jeep already deployed";
        }
        /// <summary>Development fixture only. Production armies continue using their existing GPU catalogue and economy.</summary>
        public void DeployVehiclePrototypes(){
            if(!Ready||RemoteView)return;if(IsSurvival){DeployPersonalTestVehicles();return;}if(Base==null)return;
            groundVehicles??=new PlanetVehiclePool(this,surface);
            string[] ids={"SF-MW1","SF-LUV1","SF-QB1","SF-TK1"};string[] backing={"heavy_tank","apc","scout","tank"};int added=0;
            for(int i=0;i<ids.Length;i++){
                if(groundVehicles.ContainsDefinition(ids[i]))continue;
                var d=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/"+ids[i]);if(d==null||d.ValidateDefinition().Length>0){Base.Notice="Generate the vehicle prototype assets from Synthetic Front > Vehicles first";continue;}
                int type=System.Array.FindIndex(Base.Catalog.units,u=>u.id==backing[i]);if(type<0)continue;
                Vector3 direction=spacecraft!=null?spacecraft.LocalToBodyPrecise(new Vector3((i-(ids.Length-1)*.5f)*9,-8,-68)).Float.normalized:Base.Offset(new Vector2((i-(ids.Length-1)*.5f)*10,-180));
                uint id=(uint)unitCount;if(!TrySpawnUnit(type,direction))continue;
                Vector3 forward=Vector3.ProjectOnPlane(spacecraft!=null?spacecraft.Rotation*Vector3.forward:view.transform.forward,direction).normalized;
                if(forward.sqrMagnitude<.01f)forward=Vector3.Cross(direction,Vector3.right).normalized;
                if(groundVehicles.Register(d,id,new SpacePose{position=surface.GetSurfacePositionPrecise(direction,.025f),rotation=Quaternion.LookRotation(forward,direction)},backingType:type))added++;
            }
            Base.Notice=added>0?$"{added} vehicle prototypes deployed behind the capital ship. F enters a seat; cargo follows the ship automatically.":"No clear land for the prototypes near the ship";
        }
    }
}
