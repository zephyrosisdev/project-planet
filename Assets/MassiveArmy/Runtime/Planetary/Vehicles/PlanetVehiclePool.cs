using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary
{
    /// <summary>Bounded explicit registrations, not a CPU army mirror. Small GPU gathers; no scan/readback of the army.</summary>
    public sealed partial class PlanetVehiclePool:IDisposable
    {
        public const int Capacity=32;
        [StructLayout(LayoutKind.Sequential)] struct Command
        {
            public Vector4 position,velocity,heading;public uint id,mode,weapons,health;
        }
        readonly PlanetDevelopment world;readonly VehicleTerrainContact terrain;
        readonly VehicleRigPool rigPool;
        readonly VehicleController[] vehicles=new VehicleController[Capacity];
        readonly Command[] commands=new Command[Capacity];readonly UnitState[] snapshots=new UnitState[Capacity];
        sealed class ProxyBatch:IDisposable {
            public Material material;public Mesh mesh;public readonly uint[] ids=new uint[Capacity];public readonly Matrix4x4[] poses=new Matrix4x4[Capacity];
            public readonly GraphicsBuffer.IndirectDrawIndexedArgs[] draw=new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            public GraphicsBuffer indices=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,4),args=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawIndexedArgs.size),posesBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,64);
            public void Dispose(){ArmyLifetime.Destroy(material);indices.Dispose();args.Dispose();posesBuffer.Dispose();}
        }
        readonly System.Collections.Generic.Dictionary<VehicleDefinition,ProxyBatch> batches=new System.Collections.Generic.Dictionary<VehicleDefinition,ProxyBatch>();
        readonly ComputeShader shader;readonly GraphicsBuffer commandBuffer,stateBuffer,targetBuffer;
        readonly int controlKernel,gatherKernel;
        readonly Vector4[] targets=new Vector4[Capacity];
        readonly bool[] detailed=new bool[Capacity];
        readonly float[] nextAiOrder=new float[Capacity];
        bool pending,disposed;float nextGather;int controlled=-1,revision;readonly VehicleViewLook seatLook=new VehicleViewLook();
        public VehicleController Controlled=>controlled<0?null:vehicles[controlled];
        public int Count {get;private set;}
        public int DetailedCount {get;private set;}
        public string Notice {get;private set;}="";
        public PlanetVehiclePool(PlanetDevelopment world,PlanetSurface surface){
            this.world=world;terrain=new VehicleTerrainContact(world,surface);rigPool=new VehicleRigPool(world.transform);if(world.IsSurvival)return;world.EnableVehicleSimulation();
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetVehicles"));controlKernel=shader.FindKernel("Control");gatherKernel=shader.FindKernel("Gather");
            commandBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,64);stateBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,64);targetBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Capacity,16);
            foreach(int kernel in new[]{controlKernel,gatherKernel}){shader.SetBuffer(kernel,"_PlanetUnits",world.UnitsForValidation);shader.SetBuffer(kernel,"_VehicleCommands",commandBuffer);}
            shader.SetBuffer(gatherKernel,"_VehicleSnapshots",stateBuffer);shader.SetBuffer(gatherKernel,"_VehicleTargets",targetBuffer);
        }
        public bool ContainsDefinition(string id){for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].Definition.id==id)return true;return false;}
        int FreeSlot(){for(int i=0;i<Count;i++)if(!vehicles[i].Alive)return i;return Count<Capacity?Count:-1;}
        public bool Register(VehicleDefinition definition,uint entityId,SpacePose pose,uint health=0,int backingType=5){
            int slot=FreeSlot();if(slot<0||definition==null||definition.ValidateDefinition().Length>0)return false;
            if(slot==Count)Count++;else vehicles[slot].Dispose();
            revision++;vehicles[slot]=new VehicleController(world,terrain,rigPool,definition,entityId,pose){BackingType=backingType};commands[slot]=new Command{id=entityId,mode=5,health=health,weapons=world.IsSurvival?0:world.ConfigureVehicleArchetype(slot,definition,backingType)};
            int registeredSlot=slot;vehicles[slot].MovementRequested+=input=>OrderAI(registeredSlot,input);
            if(!world.IsSurvival&&!batches.ContainsKey(definition)){
                var rig=definition.prefab.GetComponent<VehicleRig>();var batch=new ProxyBatch{mesh=rig.distantProxy.GetComponent<MeshFilter>().sharedMesh,material=new Material(Resources.Load<Shader>("MassiveArmy/PlanetVehicleProxy")){enableInstancing=true}};
                var material=batch.material;material.SetTexture("_MainTex",rig.distantProxy.GetComponent<MeshRenderer>().sharedMaterial.mainTexture??Texture2D.whiteTexture);material.SetBuffer("_PlanetUnits",world.UnitsForValidation);material.SetBuffer("_VehicleInstances",batch.indices);material.SetBuffer("_VehiclePoses",batch.posesBuffer);material.SetFloat("_PlanetRadius",world.definition.radius);material.SetFloat("_PlanetHeight",world.definition.terrainHeight);material.SetFloat("_PlanetSeed",(world.definition.terrainSeed%1009)*.017f);material.SetFloat("_PlanetOcean",world.definition.oceanHeight);world.definition.Edits.Bind(material);batches.Add(definition,batch);
            }
            var unused=new System.Collections.Generic.List<VehicleDefinition>();foreach(var pair in batches){bool used=false;for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].Definition==pair.Key){used=true;break;}if(!used)unused.Add(pair.Key);}foreach(var key in unused){batches[key].Dispose();batches.Remove(key);}
            Upload();commands[slot].mode=0;return true;
        }
        void OrderAI(int slot,VehicleInput input){
            var v=vehicles[slot];if(world.IsSurvival||v.LocalPhysics||!v.Alive||Time.unscaledTime<nextAiOrder[slot])return;
            var pose=v.Motor.Pose;var up=pose.position.Float.normalized;var heading=pose.rotation*new Vector3(input.movement.x,0,input.movement.y);
            var direction=(up+heading*(100/world.definition.radius)).normalized;
            if(world.Commands.MoveVehicle(world.NavigationForValidation,v.EntityId,v.Definition.mass<6000?MovementClass.LightVehicle:MovementClass.HeavyVehicle,direction,input.movement.sqrMagnitude<.001f))nextAiOrder[slot]=Time.unscaledTime+1;
        }
        public void Step(float dt){
            if(disposed||Count==0)return;
            var input=ReadInput();var ship=world.ShipForValidation;
            ChooseDetailedRigs();
            rigPool.Limit=world.detailedVehicleRigLimit;
            for(int i=0;i<Count;i++)if(!detailed[i])vehicles[i].SetDetail(false);
            for(int i=0;i<Count;i++)if(detailed[i]){vehicles[i].SetDetail(true);vehicles[i].Rebase();}
            if(DetailedCount>0)Physics.SyncTransforms();
            for(int i=0;i<Count;i++){
                var v=vehicles[i];if(!v.Alive){v.SetDetail(false);commands[i]=new Command{id=uint.MaxValue};continue;}
                IVehicleCarrier deck=v.Parking.Carrier;
                if(deck==null&&ship!=null&&ship.SampleVehicleDeck(v.Motor.Pose.position,out var shipContact)&&Mathf.Abs(Vector3.Dot((v.Motor.Pose.position-shipContact.position).Float,ship.Rotation*Vector3.up))<1.25f)deck=ship;
                if(deck==null)foreach(var carrier in VehicleCarrierVolume.Active){if(carrier.world==world&&!v.IsOwnCarrier(carrier)&&carrier.SampleVehicleDeck(v.Motor.Pose.position,out var contact)&&Mathf.Abs(Vector3.Dot((v.Motor.Pose.position-contact.position).Float,carrier.CarrierPose.rotation*Vector3.up))<1.25f){deck=carrier;break;}}
                v.Tick(world.Paused?0:dt,i==controlled?input:default,deck);
                var p=v.Motor.Pose;bool weaponAI=v.Seats.AIHas(VehicleSeatPermissions.Fire),manualFire=i==controlled&&(v.Seats.PlayerPermissions&VehicleSeatPermissions.Fire)!=0&&input.fire;
                var aim=i==controlled?input.aim:Vector3.forward;var velocity=p.linearVelocity.Float;var heading=p.rotation*Vector3.forward;
                commands[i]=new Command{id=v.EntityId,mode=v.Parking.Parked?2u:v.LocalPhysics?1u:0u,weapons=manualFire?2u:weaponAI?1u:0u,position=new Vector4((float)p.position.x,(float)p.position.y,(float)p.position.z,aim.x),velocity=new Vector4(velocity.x,velocity.y,velocity.z,aim.y),heading=new Vector4(heading.x,heading.y,heading.z,aim.z)};
            }
            Upload();
            if(Controlled!=null)PresentCamera();
            if(shader!=null&&!pending&&Time.unscaledTime>=nextGather){nextGather=Time.unscaledTime+(Controlled!=null?.05f:.15f);Gather();}
        }
        void ChooseDetailedRigs(){
            Array.Clear(detailed,0,detailed.Length);int budget=Mathf.Clamp(world.detailedVehicleRigLimit,1,Capacity);DetailedCount=0;
            if(controlled>=0&&vehicles[controlled].Alive){detailed[controlled]=true;DetailedCount++;}
            while(DetailedCount<budget){
                int nearest=-1;float best=float.MaxValue;
                for(int i=0;i<Count;i++){
                    var v=vehicles[i];if(detailed[i]||!v.Alive)continue;
                    float distance=(world.RenderBodyPosition(v.Motor.Pose.position)-world.View.transform.position).magnitude;
                    float limit=v.Definition.detailDistances.z+(v.Detailed?35:0);
                    // Retain an occupied detail slot with a small bias to avoid exchanging rigs every frame at equal distances.
                    float priority=distance*(v.Detailed?.9f:1);
                    if(distance<limit&&priority<best){best=priority;nearest=i;}
                }
                if(nearest<0)break;detailed[nearest]=true;DetailedCount++;
            }
        }
        VehicleInput ReadInput(){
            var v=Controlled;if(v==null)return default;var k=Keyboard.current;var mouse=Mouse.current;var input=new VehicleInput{aim=world.View.transform.forward};
            if(!Application.isFocused||world.Paused||world.LocalPlayerDead||world.PersonalMenuOpen){input.brake=true;return input;}
            var permission=v.Seats.PlayerPermissions;
            if(k!=null){
                if((permission&VehicleSeatPermissions.Movement)!=0){input.movement=new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));input.brake=k.spaceKey.isPressed;input.boost=k.leftShiftKey.isPressed;input.recover=k.rKey.wasPressedThisFrame;}
                
                
                if(k.digit1Key.wasPressedThisFrame)SwitchSeat(0);if(k.digit2Key.wasPressedThisFrame)SwitchSeat(1);if(k.digit3Key.wasPressedThisFrame)SwitchSeat(2);if(k.digit4Key.wasPressedThisFrame)SwitchSeat(3);
            }
            seatLook.Step(mouse!=null?mouse.delta.ReadValue():Vector2.zero,k!=null&&k.leftAltKey.isPressed,(v.Seats.PlayerPermissions&VehicleSeatPermissions.Aim)!=0,v.Definition.torsoYaw,v.Definition.torsoPitch,Time.deltaTime);
            input.aim=v.VisualRotation*seatLook.ForwardRotation*Vector3.forward;v.PlayerAimDirection=input.aim;input.fire=mouse!=null&&mouse.leftButton.isPressed;
            return input;
        }
        void Upload(){if(shader==null)return;commandBuffer.SetData(commands);shader.SetInt("_VehicleCount",Count);shader.SetInt("_PlanetUnitCount",world.unitCount);shader.Dispatch(controlKernel,1,1,1);}
        void Gather(){
            pending=true;int requestedRevision=revision;shader.Dispatch(gatherKernel,1,1,1);
            AsyncGPUReadback.Request(targetBuffer,request=>{if(disposed||requestedRevision!=revision||request.hasError)return;var data=request.GetData<Vector4>();for(int i=0;i<Count;i++)targets[i]=data[i];});
            AsyncGPUReadback.Request(stateBuffer,request=>{
                pending=false;if(disposed||requestedRevision!=revision||request.hasError)return;var data=request.GetData<UnitState>();
                for(int i=0;i<Count;i++){snapshots[i]=data[i];vehicles[i].FollowGpu(data[i]);vehicles[i].TargetDirection=targets[i].w>.5f?(Vector3)targets[i]:Vector3.zero;}
                if(Controlled!=null&&!Controlled.Alive){Controlled.Motor.Stop();if(!Exit())world.SetFirstPerson(false);}
            });
        }
        public void Render(){
            var camera=world.View;var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
            foreach(var pair in batches){
                var batch=pair.Value;int count=0;for(int i=0;i<Count;i++){var v=vehicles[i];if(v.Alive&&!v.Detailed&&!v.Parking.Parked&&v.Definition==pair.Key){batch.ids[count]=v.EntityId;batch.poses[count]=Matrix4x4.TRS(world.RenderBodyPosition(v.Motor.Pose.position),v.VisualRotation,Vector3.one);count++;}}if(count==0)continue;
                batch.indices.SetData(batch.ids,0,0,count);batch.posesBuffer.SetData(batch.poses,0,0,count);batch.draw[0]=new GraphicsBuffer.IndirectDrawIndexedArgs{indexCountPerInstance=batch.mesh.GetIndexCount(0),instanceCount=(uint)count,startIndex=batch.mesh.GetIndexStart(0),baseVertexIndex=batch.mesh.GetBaseVertex(0)};batch.args.SetData(batch.draw);
                var material=batch.material;material.SetMatrix("_VehicleVP",vp);material.SetVector("_PlanetCenter",world.PlanetRenderCentre);
                Graphics.RenderMeshIndirect(new RenderParams(material){camera=camera,worldBounds=ProxyBounds(batch,count),shadowCastingMode=ShadowCastingMode.Off},batch.mesh,batch.args);
            }
        }
        static Bounds ProxyBounds(ProxyBatch batch,int count){
            var bounds=new Bounds(batch.poses[0].GetColumn(3),Vector3.one*100);
            for(int i=1;i<count;i++)bounds.Encapsulate(new Bounds(batch.poses[i].GetColumn(3),Vector3.one*100));
            return bounds;
        }
        public void Rebase(){for(int i=0;i<Count;i++)vehicles[i].Rebase();}
        internal void Resume(){if(Controlled==null)return;Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;PresentCamera();}
        internal bool BlocksShipHatch(float z){
            var ship=world.ShipForValidation;if(ship==null)return false;
            for(int i=0;i<Count;i++){
                var v=vehicles[i];if(!v.Alive)continue;var local=ship.BodyToLocal(v.Motor.Pose.position);var half=v.Definition.size*.5f;
                if(Mathf.Abs(local.z-z)<half.z+.6f&&Mathf.Abs(local.x)<half.x+1.6f&&local.y+v.Definition.size.y>-4&&local.y<-.8f)return true;
            }
            return false;
        }
        // Only registered, attached cargo belongs to a carrier's flight collision envelope.
        // Nearby unparked vehicles must continue to obstruct flight.
        public bool IsCargoCollider(Collider collider,IVehicleCarrier carrier){
            if(collider==null||carrier==null)return false;
            for(int i=0;i<Count;i++){
                var v=vehicles[i];
                if(v.Alive&&v.OwnsCollider(collider)&&VehicleParking.IsCarriedBy(v.Parking.Carrier,carrier))return true;
            }
            return false;
        }
        internal bool HasParked {get{for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].Parking.Carrier==world.ShipForValidation)return true;return false;}}
        internal void TransferParkedTo(PlanetVehiclePool destination){
            int needed=0,free=Capacity-destination.Count;for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].Parking.Carrier==world.ShipForValidation)needed++;
            for(int i=0;i<destination.Count;i++)if(!destination.vehicles[i].Alive)free++;
            if(needed>free||!world.IsSurvival&&destination.world.VehicleSpawnCapacity<needed)throw new InvalidOperationException("Destination vehicle capacity is full; cargo was retained on its source ship");
            // Rare, explicit ownership transfer reads only the embarked records, never a unit array/mirror.
            var record=new UnitState[1];var added=new System.Collections.Generic.List<int>(needed);
            try{
                for(int i=0;i<Count;i++){
                    var source=vehicles[i];if(!source.Alive||source.Parking.Carrier!=world.ShipForValidation)continue;
                    if(!world.IsSurvival){world.UnitsForValidation.GetData(record,0,(int)source.EntityId,1);if((record[0].flags&1)==0)continue;}
                    var parking=source.Parking;var nextPose=VehicleParking.Resolve(destination.world.ShipForValidation,parking.LocalPosition,parking.LocalRotation);
                    uint id=destination.world.IsSurvival?destination.NextPersonalId():destination.world.AppendVehicleBacking(source.BackingType,nextPose.position.Float.normalized);int slot=destination.FreeSlot();
                    if(!destination.Register(source.Definition,id,nextPose,record[0].health,source.BackingType))throw new InvalidOperationException("Could not transfer vehicle cargo");
                    var target=destination.vehicles[slot];target.SurvivalInventoryId=source.SurvivalInventoryId;target.Parking.Restore(destination.world.ShipForValidation,parking.Bay,parking.LocalPosition,parking.LocalRotation);target.Motor.Follow(target.Parking.Pose);added.Add(slot);
                }
            }catch{
                foreach(int slot in added){destination.commands[slot].mode=4;destination.vehicles[slot].Alive=false;destination.vehicles[slot].Dispose();}destination.Upload();throw;
            }
            for(int i=0;i<Count;i++)if(vehicles[i].Alive&&vehicles[i].Parking.Carrier==world.ShipForValidation){commands[i].mode=4;vehicles[i].Alive=false;vehicles[i].Dispose();}
            revision++;Upload();destination.Step(0);
        }
        internal uint NextPersonalId(){uint id=1;for(int i=0;i<Count;i++)id=System.Math.Max(id,vehicles[i].EntityId+1);return id;}
        int FindSeat(out int seat,VehicleController only=null,bool requireAim=true){
            seat=-1;if(!world.IsFirstPerson)return -1;float best=4;int result=-1;var eye=world.View.transform.position;
            for(int i=0;i<Count;i++){
                var v=vehicles[i];if(only!=null&&v!=only||!v.Alive||!v.Parking.Parked&&v.Motor.Pose.linearVelocity.Magnitude>2)continue;var interaction=v.InteractionPose;
                for(int s=0;s<v.Definition.seats.Length;s++){
                    var occupant=v.Seats.Occupant(s);if(occupant.kind==VehicleOccupantKind.Player||occupant.kind==VehicleOccupantKind.AI&&occupant.entityId!=uint.MaxValue)continue;
                    var p=world.RenderBodyPosition(interaction.position+Double3.Rotate(interaction.rotation,new Double3(v.Definition.seats[s].entry)));var delta=p-eye;float distance=delta.magnitude;
                    if(distance>=best||requireAim&&Vector3.Dot(delta.normalized,world.View.transform.forward)<.3f)continue;
                    if(Physics.Linecast(eye,p,out var hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&Vector3.Distance(hit.point,p)>.8f)continue;
                    best=distance;result=i;seat=s;
                }
            }
            return result;
        }
        public string InteractionPrompt {get{if(Controlled!=null)return "F leave vehicle · Hold E for inventory / controls · 1–4 change seats";int index=FindSeat(out int seat);return index<0?"":"F enter "+vehicles[index].Definition.displayName+" · Hold E for inventory / controls";}}
        public bool Interact(){
            if(Controlled!=null)return Exit();int index=FindSeat(out int seat);return index>=0&&EnterSeat(index,seat);
        }
        internal bool EnterInventoryVehicle(VehicleController target){int index=FindSeat(out int seat,target,false);if(index<0){Notice="Move within reach of an unobstructed seat";return false;}return EnterSeat(index,seat);}
        bool EnterSeat(int index,int seat){
            var vehicle=vehicles[index];if((vehicle.Definition.seats[seat].permissions&VehicleSeatPermissions.Movement)==0)for(int driver=0;driver<vehicle.Definition.seats.Length;driver++)if((vehicle.Definition.seats[driver].permissions&VehicleSeatPermissions.Movement)!=0&&vehicle.Seats.TryEnterPlayer(driver)){seat=driver;break;}
            if(vehicle.Seats.PlayerSeat!=seat&&!vehicle.Seats.TryEnterPlayer(seat))return false;controlled=index;vehicles[index].SetDetail(true);seatLook.Reset();world.ActivateGroundVehicle();Upload();PresentCamera();return true;
        }
        void SwitchSeat(int seat){var v=Controlled;if(v==null||!v.Seats.TryEnterPlayer(seat))return;if(!v.Airborne)v.Motor.Stop();seatLook.Reset();Notice="Seated: "+v.Definition.seats[seat].name;}
        public bool Exit(){
            var v=Controlled;if(v==null)return false;
            if(v.Motor.Pose.linearVelocity.Magnitude>3&&!v.Parking.Parked){Notice="Stop before leaving the vehicle";return false;}
            if(!v.SafeExit(out var feet)){Notice="Exit obstructed; reposition the vehicle";return false;}
            var heading=v.Motor.Pose.rotation*Vector3.forward;var carrier=v.Parking.Carrier;v.Seats.LeavePlayer();v.Motor.Stop();controlled=-1;
            world.LeaveGroundVehicle(feet,heading,carrier);return true;
        }
        public void ReleaseControl(){var v=Controlled;if(v!=null){v.Seats.LeavePlayer();if(!v.Airborne)v.Motor.Stop();}controlled=-1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        void PresentCamera(){
            var v=Controlled;if(v==null)return;var pose=v.Motor.Pose;var seat=v.Definition.seats[v.Seats.PlayerSeat];var rotation=v.VisualRotation*seatLook.ViewRotation;
            var bodyEye=pose.position+new Double3(pose.rotation*Vector3.up)*v.BodyHeave+Double3.Rotate(v.VisualRotation,new Double3(seat.eye));
            world.AnchorVehicleObserver(bodyEye);world.View.transform.SetPositionAndRotation(Vector3.zero,rotation);VehicleCameraSettings.ApplyOrDefault(v.Definition.cameraSettings,world.View,bodyEye.Magnitude);Rebase();
        }
        public void DrawHud(){var v=Controlled;if(v==null)return;if((v.Seats.PlayerPermissions&VehicleSeatPermissions.Aim)!=0&&Mathf.Abs(seatLook.HeadYaw)<.5f&&Mathf.Abs(seatLook.HeadPitch)<.5f)GUI.Label(new Rect(Screen.width*.5f-4,Screen.height*.5f-10,20,20),"+");GUI.Label(new Rect(12,12,1100,80),$"{v.Definition.displayName} · {v.Seats.PlayerSeat+1}: {v.Definition.seats[v.Seats.PlayerSeat].role} · {v.Motor.ForwardSpeed*3.6f:0} km/h · {v.Motor.State} · BOOST {v.BoostCharge*100:0}%{(v.Boosting?" FIRING":"")}\nW/S drive · A/D steer · Space brake · Hold Shift jump boost · R recover · Hold Left Alt free look · 1–4 seats · Automatic cargo support · F exit · Tab RTS\n{(v.MotionBlocked?"Blocked by "+v.BlockingObject+" � R recover":Notice)}");GUI.Box(new Rect(12,94,180,16),GUIContent.none);var previousColour=GUI.color;GUI.color=v.Boosting?new Color(1,.65f,.15f):new Color(.2f,.8f,1);GUI.DrawTexture(new Rect(14,96,176*v.BoostCharge,12),Texture2D.whiteTexture);GUI.color=previousColour;}
        public void Dispose(){DisposeMarkers();disposed=true;ReleaseControl();for(int i=0;i<Count;i++)vehicles[i].Dispose();foreach(var batch in batches.Values)batch.Dispose();batches.Clear();rigPool.Dispose();commandBuffer?.Dispose();stateBuffer?.Dispose();targetBuffer?.Dispose();ArmyLifetime.Destroy(shader);}
    }
}
