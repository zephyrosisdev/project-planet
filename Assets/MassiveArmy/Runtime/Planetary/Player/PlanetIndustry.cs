using System;
using System.Collections.Generic;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MassiveArmy.Planetary {
    public sealed partial class PlanetDevelopment {
        PlanetIndustry industry;
        internal IndustrialAuthority IndustrialAuthority=>industry?.Authority;
        internal void UpdateIndustry(){if(!IsSurvival)return;industry??=new PlanetIndustry(this,surface);SyncVehicleInteractionState();industry.Update();UpdateVehicleInventoryUi();UpdateShipyard();}
        internal bool IndustrialDriving=>industry?.Driving==true;
        internal bool IndustrialMenuOpen=>industry?.MenuOpen==true;
        internal bool IndustrialPlacementActive=>industry?.PlacementActive==true;
        internal void AttachIndustryUi(UnityEngine.UIElements.VisualElement parent)=>industry?.AttachUi(parent);
        internal void ShowIndustrialCrafting()=>industry?.OpenMenu("crafting");
        internal void ShowIndustrialConstruction()=>industry?.OpenMenu("construction");
        internal void CloseIndustryMenu()=>industry?.CloseMenu();
        internal string IndustrialInteractionPrompt=>industry?.InteractionPrompt??"";
        internal void SetIndustryCursor(bool menu){if(menu){CloseGroundLoot();personalHud?.Close();CloseRespawnPointMenu();}SetPersonalCursor(menu);}
        internal bool ConsumeSurvivalMissile()=>industry!=null&&industry.ConsumeMissile();
    }
    /// <summary>Offline adapter for the authoritative industry model. Presentation owns no inventory.</summary>
    internal sealed partial class PlanetIndustry:IDisposable {
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly IndustrialActor actor;
        readonly Material structure,beltMaterial,earth,prototypeMaterial,placementMaterial;readonly Dictionary<string,Material> itemMaterials=new Dictionary<string,Material>();readonly Dictionary<string,List<Matrix4x4>> itemBatches=new Dictionary<string,List<Matrix4x4>>();readonly Mesh cube;readonly Matrix4x4[] instances=new Matrix4x4[1023];
        readonly VehicleViewLook driverLook=new VehicleViewLook();IndustrialMachine driver;VehicleDefinition driverProfile;VehicleMotor driverMotor;VehicleTerrainContact vehicleContact;double driverRecordElapsed;string driveBlockReason="";Vector2 driveRequested;
        public bool Driving=>driver!=null;
        readonly GameObject host;readonly MeshCollider contact;Mesh contactMesh;Double3 contactAnchor;long contactHash=long.MinValue;readonly MeshCollider[] pileContacts=new MeshCollider[16];
        readonly Dictionary<long,Mesh> pileMeshes=new Dictionary<long,Mesh>();readonly Dictionary<long,int> pileVersions=new Dictionary<long,int>();
        readonly List<IndustrialMachine> nearby=new List<IndustrialMachine>();
        readonly Dictionary<string,List<Matrix4x4>> prototypeNear=new Dictionary<string,List<Matrix4x4>>(),prototypeFar=new Dictionary<string,List<Matrix4x4>>();
        public readonly IndustrialAuthority Authority;readonly bool ownsAuthority;
        public bool MenuOpen {get;private set;}double accumulator,nextSave;long selected,source;string filter="",message="G collect resources / I inventory / F4 crafting";
        long observedPlayerCombatRevision;int palette;readonly string[] buildNames;float nextDig;
        public PlanetIndustry(PlanetDevelopment world,PlanetSurface surface){
            this.world=world;this.surface=surface;
            if(world.SystemRuntime?.Industry!=null)Authority=world.SystemRuntime.Industry;
            else {string path=Path.Combine(Application.persistentDataPath,"Survival",world.terrainSeed.ToString(),"industry.xml");Authority=new IndustrialAuthority(IndustrialAuthority.Load(path),path);ownsAuthority=world.SystemRuntime==null;if(world.SystemRuntime!=null)world.SystemRuntime.Industry=Authority;}
            var p=ObserverPosition();actor=Authority.World.Register(1,Pose(p));actor.pose=Pose(p);Authority.EnableMiningWorkflow();world.PlayerForValidation.Inventory.Bind(Authority,1,world.PersonalItems);Authority.World.InventoryPoseResolver=pose=>world.TryResolveRespawnPose(pose,out _,out _)?(FramePose?)world.RespawnBodyPose(pose):null;
            if(surface!=null)Authority.ReprojectSurfaceDeposits(world.definition.bodyId,pose=>SurfaceDepositPose(surface,pose));
            SpreadLegacyDeposits();
            if(!Authority.World.deposits.Exists(x=>x.pose.body==world.definition.bodyId))SeedDeposits(world.Rig.FocusDirection);else SeedDeposits(world.Rig.FocusDirection,true);
            observedPlayerCombatRevision=actor.playerCombatRevision;world.PlayerStatus.Health=Mathf.Min(world.PlayerStatus.Health,(float)actor.health);Authority.Save();nextSave=Time.realtimeSinceStartupAsDouble+30;
            buildNames=new string[IndustrialCatalog.Machines.Count];IndustrialCatalog.Machines.Keys.CopyTo(buildNames,0);
            prototypeMaterial=ColonyPrototypeLibrary.Material("Industrial prototypes");placementMaterial=ColonyPrototypeLibrary.Material("Industrial placement");placementMaterial.SetFloat("_Preview",1);placementMaterial.SetFloat("_ZWrite",0);placementMaterial.renderQueue=3000;foreach(var name in buildNames)if(ColonyPrototypeLibrary.Find("machine-"+name)!=null){prototypeNear.Add(name,new List<Matrix4x4>());prototypeFar.Add(name,new List<Matrix4x4>());}cube=CreateCube();var shader=Shader.Find("HDRP/Lit")??Shader.Find("Standard");structure=Mat(shader,new Color(.25f,.34f,.4f));beltMaterial=Mat(shader,new Color(.08f,.1f,.13f));foreach(var item in IndustrialCatalog.Items.Keys){uint hash=2166136261;foreach(char c in item)hash=(hash^c)*16777619;itemMaterials[item]=Mat(shader,Color.HSVToRGB((hash%360)/360f,.65f,.9f));itemBatches[item]=new List<Matrix4x4>();}earth=Mat(shader,new Color(.28f,.17f,.08f));
            host=new GameObject("Chunked industry contacts");host.transform.SetParent(world.transform,false);contact=host.AddComponent<MeshCollider>();for(int i=0;i<pileContacts.Length;i++){var go=new GameObject("Nearby stockpile contact");go.transform.SetParent(world.transform);pileContacts[i]=go.AddComponent<MeshCollider>();go.SetActive(false);}

        }
        Material Mat(Shader shader,Color color){var m=new Material(shader){enableInstancing=true};m.SetColor("_BaseColor",color);return m;}
        FramePose Pose(Double3 p)=>new FramePose{body=world.definition.bodyId,x=p.x,y=p.y,z=p.z};
        Double3 Body(FramePose p)=>new Double3(p.x,p.y,p.z);
        // Destination on-foot controllers have no feet position until the pilot exits.
        // Actor state follows the controlled ship; deposits use a stable surface anchor.
        internal static Double3 SelectObserverPosition(bool piloting,Double3 feet,Double3 ship)=>piloting?ship:feet;
        Double3 ObserverPosition()=>world.IsDriving?world.ControlledInventoryPosition:SelectObserverPosition(world.IsPiloting,world.PlayerForValidation.PreciseBodyPosition,world.IsPiloting?world.ShipForValidation.Pose.position:default);
        internal static FramePose SurfaceDepositPose(PlanetSurface surface,FramePose pose){var at=surface.GetSurfacePositionPrecise(PlanetPrecisionPolicy.Direction(new Double3(pose.x,pose.y,pose.z)));pose.x=at.x;pose.y=at.y;pose.z=at.z;return pose;}
        IndustrialCommand Command(string kind,long from=0,long to=0,string item=null,long count=0)=>new IndustrialCommand{sequence=actor.lastSequence+1,kind=kind,source=from,target=to,item=item,count=count};
        void Execute(IndustrialCommand command){if(command.kind=="belt"&&command.route!=null){string error=Authority.World.ConveyorError(actor.id,command,out _)??ConveyorObstruction(command);if(error!=null){message=error;return;}}if(command.kind=="power"||command.kind=="water"){if(command.route==null){var from=Authority.World.Machine(command.source);var to=Authority.World.Machine(command.target);if(from!=null&&to!=null)command.route=IndustrialCablePath.WithSocketClearance(from,to,command.kind,null);}string error=Authority.World.ConnectionError(actor.id,command)??CableObstruction(command);if(error!=null){message=error;return;}}var result=Authority.Execute(actor.id,command);message=result.message;if(result.accepted&&result.entity!=0)selected=result.entity;}
        public bool ConsumeMissile(){if(actor.inventory.Count("missile")<1){message="Manufacture and load a missile first";return false;}var result=Authority.Execute(actor.id,Command("use-item",item:"missile",count:1));message=result.message;return result.accepted;}
        bool IsVehicle(IndustrialMachine m)=>m.definition=="truck"||m.definition=="loader"||m.definition=="excavator";
        void EnterVehicle(IndustrialMachine machine){
            if(driver!=null||machine==null||!IsVehicle(machine))return;if(machine.owner!=actor.id){message="This vehicle belongs to another player";return;}if(actor.pose.Distance(machine.pose)>12){message="Move within 12 m of the vehicle to enter";return;}
            CancelRoutePlanning(machine.id);if(machine.haul?.active==true){Execute(Command("haul-stop",to:machine.id));if(machine.haul.active)return;}
            if(machine.integrity<=0){message="Vehicle is damaged and cannot be driven";return;}if(!machine.enabled){Execute(Command("toggle",to:machine.id));if(!machine.enabled)return;}
            driver=machine;driverProfile=ScriptableObject.CreateInstance<VehicleDefinition>();driverProfile.cameraSettings=Resources.Load<VehicleCameraSettings>("MassiveArmy/VehicleCameras/"+machine.definition);driverProfile.locomotion=VehicleLocomotion.Wheeled;driverProfile.size=new Vector3(2.5f,3,5);driverProfile.chassisSize=new Vector3(2.3f,1.6f,4.5f);driverProfile.chassisCentre=new Vector3(0,1.5f,0);driverProfile.maximumSpeed=machine.definition=="truck"?10:5;
            driverProfile.wheels=new[]{new VehicleWheelDefinition{radius=.55f,position=new Vector3(-1,.6f,-1.5f)},new VehicleWheelDefinition{radius=.55f,position=new Vector3(1,.6f,-1.5f)},new VehicleWheelDefinition{radius=.55f,position=new Vector3(-1,.6f,1.5f),steering=true},new VehicleWheelDefinition{radius=.55f,position=new Vector3(1,.6f,1.5f),steering=true}};
            driverMotor=new WheeledVehicleMotor(driverProfile);driverMotor.Follow(new SpacePose{position=Body(machine.pose),rotation=Rotation(machine.pose)});vehicleContact=new VehicleTerrainContact(world,surface);world.AutomaticPlayerStep=false;CloseMenu();world.PlayerForValidation.SetActive(false);driverLook.Reset();world.SetPersonalCursor(false);driveBlockReason=Authority.World.creativeMode?"Creative driving · no fuel required":"Ready · W/S drive, A/D steer";if(contact!=null){contactHash=long.MinValue;RebuildContacts();}PresentIndustrialDriver();driverRecordElapsed=.1;
        }
        public bool ExitVehicle(){
            if(driver==null)return false;var pose=driverMotor.Pose;var up=pose.rotation*Vector3.up;Double3 exit=default;bool clear=false;Physics.SyncTransforms();
            foreach(var offset in new[]{Vector3.right*4,Vector3.left*4,Vector3.back*5,Vector3.right*6,Vector3.left*6}){var point=pose.position+Double3.Rotate(pose.rotation,new Double3(offset));var feet=surface.ProjectToSurface(point,.05);if(vehicleContact.Probe(point,up,2,6,out var support))feet=support.position+new Double3(support.normal)*.05;if(world.PlayerForValidation.Collision.Clear(world.RenderBodyPosition(feet),up)){exit=feet;clear=true;break;}}
            if(!clear){message="Vehicle exit is blocked; move to a clear area";return false;}
            var finalPose=Pose(pose.position);finalPose.yaw=(Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(pose.position)))*pose.rotation).eulerAngles.y;Authority.MoveIndustrialVehicle(actor.id,driver.id,finalPose);world.PlayerForValidation.EnterBodyPosition(exit,pose.rotation*Vector3.forward);world.AutomaticPlayerStep=true;driver=null;driverMotor=null;ArmyLifetime.Destroy(driverProfile);driverProfile=null;return true;
        }
        VehicleInput ReadDriveInput(bool focused){
            var input=new VehicleInput{brake=true};var keys=Keyboard.current;driveRequested=Vector2.zero;
            if(!focused){driveBlockReason="Click the Game view to capture driving controls";return input;}
            if(world.Paused){driveBlockReason="Game paused";return input;}if(MenuOpen||world.PersonalMenuOpen){driveBlockReason="Close the menu to drive";return input;}
            if(actor.health<=0){driveBlockReason="Player has no health";return input;}if(keys==null||!keys.enabled){driveBlockReason="Keyboard unavailable · focus the Game view";return input;}
            input.movement=new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));input.brake=keys.spaceKey.isPressed;driveRequested=input.movement;driveBlockReason=input.brake?"Brake held (Space)":input.movement.sqrMagnitude>0?"Driving":"Ready · W/S drive, A/D steer";return input;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string lastDriveTrace;
        void TraceDriving(VehicleInput input){string state=$"vehicle={driver.id} input={input.movement} brake={input.brake} creative={Authority.World.creativeMode} enabled={driver.enabled} fuel={driver.input.Count("fuel")} moving={Mathf.Abs(driverMotor.ForwardSpeed)>.05f} reason={driveBlockReason}";if(state==lastDriveTrace)return;lastDriveTrace=state;Debug.Log("[INDUSTRIAL DRIVE] "+state);}
#endif
        void Drive(){
            var input=ReadDriveInput(Application.isFocused);var k=Keyboard.current;
            if(Application.isFocused&&!MenuOpen&&!world.PersonalMenuOpen)driverLook.Step(Mouse.current?.delta.ReadValue()??Vector2.zero,k?.leftAltKey.isPressed==true,false,100,60,Time.deltaTime);
            DriveStep(input,Time.deltaTime);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TraceDriving(input);
#endif
        }
        void DriveStep(VehicleInput input,float elapsed){
            float dt=VehicleMotor.LocalStepDuration(elapsed);if(!driver.enabled||driver.integrity<=0){input.brake=true;driveBlockReason=driver.integrity<=0?"Vehicle damaged":"Engine switched off · use F4 to enable";}if(!Authority.World.creativeMode&&driver.progress==0&&!input.brake&&input.movement.sqrMagnitude>.0001f){var fuel=Authority.Execute(actor.id,Command("vehicle-fuel",to:driver.id));if(!fuel.accepted){message=fuel.message;driveBlockReason="Needs fuel in vehicle input · F4 to refuel";input.movement=Vector2.zero;}}
            input.brake|=!Authority.World.creativeMode&&driver.progress==0;driverProfile.maximumSpeed=(driver.definition=="truck"?10:5)*(1-.4f*(float)driver.input.Used/driver.input.capacity);driverMotor.Step(dt,input,vehicleContact);if(input.movement.sqrMagnitude>.001f&&Mathf.Abs(driverMotor.ForwardSpeed)<.05f&&vehicleContact.LastObstacle!=null)driveBlockReason="Blocked by "+vehicleContact.LastObstacle.name;
            var p=driverMotor.Pose.position;var state=Pose(p);state.yaw=(Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(p)))*driverMotor.Pose.rotation).eulerAngles.y;driverRecordElapsed+=dt;
            if(driverRecordElapsed>=.1){Authority.MoveIndustrialVehicle(actor.id,driver.id,state);driverRecordElapsed=0;}
            world.PlayerForValidation.FollowVehicleObserver(p+new Double3(driverMotor.Pose.rotation*new Vector3(0,1.3f,1.1f)),driverMotor.Pose.rotation*Vector3.forward);PresentIndustrialDriver();world.PlayerStatus.Step(world.definition,(float)(p.Magnitude-world.definition.radius),dt);
        }
        void PresentIndustrialDriver(){if(driver==null)return;var pose=driverMotor.Pose;var seat=driver.definition=="truck"?new Vector3(0,2.3f,2.22f):new Vector3(driver.definition=="excavator"?-.55f:0,2.3f,1.22f);world.AnchorVehicleObserver(pose.position+Double3.Rotate(pose.rotation,new Double3(seat)));world.View.transform.SetPositionAndRotation(Vector3.zero,pose.rotation*driverLook.ViewRotation);VehicleCameraSettings.ApplyOrDefault(driverProfile.cameraSettings,world.View,pose.position.Magnitude);if(host!=null){var delta=world.RenderBodyPosition(contactAnchor)-host.transform.position;host.transform.position+=delta;foreach(var collider in pileContacts)if(collider!=null&&collider.gameObject.activeSelf)collider.transform.position+=delta;}}
        public void Update(){
            if(observedPlayerCombatRevision!=actor.playerCombatRevision){world.PlayerStatus.Health=Mathf.Min(world.PlayerStatus.Health,(float)actor.health);observedPlayerCombatRevision=actor.playerCombatRevision;}
            if(driver!=null&&!world.Paused&&!world.LocalPlayerDead)Drive();actor.pose=Pose(ObserverPosition());
            actor.health=world.PlayerStatus.Health;actor.collisionRadius=Driving?2.8:.35;var navigationShip=world.ShipForValidation;actor.vehicleObstacleRadius=navigationShip==null?0:65;if(navigationShip!=null){actor.vehicleObstacle=Pose(navigationShip.Pose.position);actor.vehicleObstacle.yaw=(Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(navigationShip.Pose.position)))*navigationShip.Pose.rotation).eulerAngles.y;}
            if(!world.Paused){var sun=world.LightingForValidation?.Sun;var light=sun!=null?-sun.transform.forward:Vector3.zero;float wind=world.definition.hasAtmosphere?Mathf.Clamp01((world.definition.weather?.windSpeed??0)/25f):0;Authority.World.SetPowerEnvironment(world.definition.bodyId,light.x,light.y,light.z,wind);accumulator+=Time.deltaTime;int steps=(int)(accumulator/.1);if(steps>0){int batch=Math.Min(steps,100);Authority.Step(batch);accumulator-=batch*.1;}}
            world.PlayerStatus.Health=Mathf.Min(world.PlayerStatus.Health,(float)actor.health);world.PlayerForValidation.WalkSpeed=world.PlayerStatus.Health<=0?0:actor.dose>=50?4:actor.dose>=25?6:7;
            var keys=Keyboard.current;if(!world.LocalPlayerDead&&Application.isFocused&&keys!=null&&(world.IsFirstPerson||world.IsDriving)){if(keys.f4Key.wasPressedThisFrame){if(MenuOpen)CloseMenu();else OpenMenu("crafting");}else if(MenuOpen&&keys.escapeKey.wasPressedThisFrame)CloseMenu();if(world.IsFirstPerson&&!MenuOpen&&!world.PersonalMenuOpen&&keys.gKey.isPressed&&Time.time>=nextDig){nextDig=Time.time+.4f;SurfaceDeposit best=null;double range=5;foreach(var deposit in Authority.World.deposits){double r=actor.pose.Distance(deposit.pose);if(r<range&&deposit.remaining.litres>0){range=r;best=deposit;}}if(best!=null)Execute(Command("dig",best.id,count:Math.Min(50,best.remaining.litres)));else message="Move within 5m of a deposit marker";}}
            if(Time.realtimeSinceStartupAsDouble>=nextSave){Authority.Save();nextSave=Time.realtimeSinceStartupAsDouble+30;}
            RecoverTruckRoutes();UpdatePlacement();if(Driving)PresentIndustrialDriver();Render();RenderCables();UpdateCableTool();DrawPlacement();UpdateToolkit();
        }
        void Draw(Mesh mesh,Material mat,Matrix4x4 matrix,Bounds bounds){Graphics.RenderMesh(new RenderParams(mat){camera=world.View,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},mesh,0,matrix);}
        Quaternion Rotation(FramePose p)=>Quaternion.FromToRotation(Vector3.up,PlanetPrecisionPolicy.Direction(Body(p)))*Quaternion.Euler(0,p.yaw,0);
        Matrix4x4 Transform(FramePose p,Vector3 offset,Vector3 size)=>Matrix4x4.TRS(world.RenderBodyPosition(Body(p))+Rotation(p)*offset,Rotation(p),size);
        void Render(){
            nearby.Clear();foreach(var batch in prototypeNear.Values)batch.Clear();foreach(var batch in prototypeFar.Values)batch.Clear();var bounds=new Bounds(world.View.transform.position,Vector3.one*2000);long hash=17;
            foreach(var machine in Authority.World.machines){if(machine.integratedLogistics){if(actor.pose.Distance(machine.pose)<=12)nearby.Add(machine);continue;}if(machine.pose.body!=world.definition.bodyId||actor.pose.Distance(machine.pose)>600)continue;var q=Rotation(machine.pose);var at=world.RenderBodyPosition(Body(machine.pose));
                Vector3 size=machine.definition=="sluice"?new Vector3(2,1,4):machine.Definition.storage?new Vector3(2,2,2):IsVehicle(machine)?new Vector3(2.5f,2.5f,5):new Vector3(2,2.5f,2);
                var prototype=ColonyPrototypeLibrary.Find("machine-"+machine.definition);if(prototype!=null){bool distant=actor.pose.Distance(machine.pose)>60;(distant?prototypeFar:prototypeNear)[machine.definition].Add(machine==driver?Matrix4x4.TRS(world.RenderBodyPosition(driverMotor.Pose.position),driverMotor.Pose.rotation,Vector3.one):Transform(machine.pose,Vector3.zero,Vector3.one));}else Draw(cube,structure,Transform(machine.pose,Vector3.up*size.y*.5f,size),bounds);
                if(IsVehicle(machine)&&machine.input.bulk.litres+IndustrialWorld.TailingsCount(machine.input)>0){float fill=Mathf.Clamp01((float)(machine.input.bulk.litres+IndustrialWorld.TailingsCount(machine.input))/machine.input.capacity);var local=machine.definition=="truck"?new Vector3(0,2.1f+fill*.5f,-1):new Vector3(machine.definition=="excavator"?.6f:0,.65f+fill*.2f,4.65f);var sizeCargo=machine.definition=="truck"?new Vector3(1.8f,fill,2.4f):new Vector3(1.5f,fill*.4f,.9f);var cargoMatrix=machine==driver?Matrix4x4.TRS(world.RenderBodyPosition(driverMotor.Pose.position),driverMotor.Pose.rotation,Vector3.one):Transform(machine.pose,Vector3.zero,Vector3.one);Draw(cube,earth,cargoMatrix*Matrix4x4.TRS(local,Quaternion.identity,sizeCargo),bounds);}

                if(actor.pose.Distance(machine.pose)<=12)nearby.Add(machine);
                if(machine!=driver&&actor.pose.Distance(machine.pose)<(machine.Definition.Vehicle?15:80))unchecked{hash=hash*31+machine.id;hash=hash*31+machine.pose.x.GetHashCode();hash=hash*31+machine.pose.y.GetHashCode();hash=hash*31+machine.pose.z.GetHashCode();hash=hash*31+machine.pose.yaw.GetHashCode();}

            }
            DrawPrototypeBatches(prototypeNear,false,bounds);DrawPrototypeBatches(prototypeFar,true,bounds);
            if(hash!=contactHash||(Body(actor.pose)-contactAnchor).Magnitude>20){contactHash=hash;contactAnchor=Body(actor.pose);RebuildContacts();}host.transform.position=world.RenderBodyPosition(contactAnchor);

            foreach(var batch in itemBatches.Values)batch.Clear();
            RenderConveyorRuns(bounds);RenderTailings(bounds);
            foreach(var pair in itemBatches){var batch=pair.Value;for(int offset=0;offset<batch.Count;offset+=instances.Length){int count=Math.Min(instances.Length,batch.Count-offset);batch.CopyTo(offset,instances,0,count);Flush(count,bounds,itemMaterials[pair.Key]);}}
            int pileContact=0;foreach(var pile in Authority.World.piles){if(pile.pose.body!=world.definition.bodyId||actor.pose.Distance(pile.pose)>600)continue;if(!pileVersions.TryGetValue(pile.id,out int version)||version!=pile.revision){if(pileMeshes.TryGetValue(pile.id,out var old)){foreach(var collider in pileContacts)if(collider.sharedMesh==old)collider.sharedMesh=null;ArmyLifetime.Destroy(old);}pileMeshes[pile.id]=PileMesh(pile);pileVersions[pile.id]=pile.revision;}Draw(pileMeshes[pile.id],earth,Transform(pile.pose,Vector3.zero,Vector3.one),bounds);if(actor.pose.Distance(pile.pose)<24&&pileContact<pileContacts.Length){var collider=pileContacts[pileContact++];collider.gameObject.SetActive(true);collider.transform.SetPositionAndRotation(world.RenderBodyPosition(Body(pile.pose)),Rotation(pile.pose));collider.sharedMesh=pileMeshes[pile.id];}}
            while(pileContact<pileContacts.Length)pileContacts[pileContact++].gameObject.SetActive(false);
        }
        void DrawPrototypeBatches(Dictionary<string,List<Matrix4x4>> batches,bool distant,Bounds bounds){foreach(var pair in batches){var batch=pair.Value;if(batch.Count==0)continue;var model=ColonyPrototypeLibrary.Find("machine-"+pair.Key);var mesh=distant?model.lod.Mesh(model.id+" distant"):model.Mesh(model.id);for(int offset=0;offset<batch.Count;offset+=instances.Length){int count=Math.Min(instances.Length,batch.Count-offset);batch.CopyTo(offset,instances,0,count);Graphics.RenderMeshInstanced(new RenderParams(prototypeMaterial){camera=world.View,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},mesh,0,instances,count);}}}
        Vector3 ConveyorPoint(IndustrialMachine machine,bool output){var offset=Vector3.up*.65f;var prototype=ColonyPrototypeLibrary.Find("machine-"+machine.definition);if(prototype!=null)foreach(var socket in prototype.sockets)if(socket.kind==(output?"ConveyorOutput":"ConveyorInput")){offset=socket.position;break;}return world.RenderBodyPosition(Body(machine.pose))+Rotation(machine.pose)*offset;}
        void RebuildContacts(){
            contact.sharedMesh=null;ArmyLifetime.Destroy(contactMesh);var vertices=new List<Vector3>();var indices=new List<int>();var shape=cube.vertices;var triangles=cube.triangles;
            foreach(var machine in Authority.World.machines){if(machine.integratedLogistics||machine==driver||machine.pose.body!=world.definition.bodyId||actor.pose.Distance(machine.pose)>=(machine.Definition.Vehicle?15:80))continue;var prototype=ColonyPrototypeLibrary.Find("machine-"+machine.definition);var mesh=prototype?.Collision()??cube;var size=prototype!=null?Vector3.one:new Vector3(2.5f,2.5f,5);var offset=prototype!=null?Vector3.zero:Vector3.up*1.25f;var p=(Body(machine.pose)-contactAnchor).Float;var q=Rotation(machine.pose);int start=vertices.Count;foreach(var v in mesh.vertices)vertices.Add(p+q*(Vector3.Scale(v,size)+offset));foreach(var i in mesh.triangles)indices.Add(start+i);}
            if(vertices.Count==0)return;contactMesh=new Mesh{name="Industry contact chunk",indexFormat=IndexFormat.UInt32};contactMesh.SetVertices(vertices);contactMesh.SetTriangles(indices,0);contactMesh.RecalculateBounds();contact.sharedMesh=contactMesh;
        }
        void Flush(int count,Bounds bounds,Material material)=>Graphics.RenderMeshInstanced(new RenderParams(material){camera=world.View,worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off},cube,0,instances,count);
        static Mesh PileMesh(DirtPile pile){var vertices=new Vector3[17*17];var triangles=new int[16*16*6];int at=0;for(int z=0;z<17;z++)for(int x=0;x<17;x++){float height=0;int samples=0;for(int dz=-1;dz<=0;dz++)for(int dx=-1;dx<=0;dx++){int cx=x+dx,cz=z+dz;if(cx<0||cz<0||cx>=16||cz>=16)continue;height+=pile.cells[cz*16+cx]/1000f;samples++;}vertices[z*17+x]=new Vector3(x-8,samples>0?height/samples:0,z-8);}for(int z=0;z<16;z++)for(int x=0;x<16;x++){int n=z*17+x;triangles[at++]=n;triangles[at++]=n+17;triangles[at++]=n+1;triangles[at++]=n+1;triangles[at++]=n+17;triangles[at++]=n+18;}var mesh=new Mesh{name="Conserved dirt pile"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
        static Mesh CreateCube(){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);ArmyLifetime.Destroy(go);return mesh;}
        string InventoryText(Inventory inv){var s=new System.Text.StringBuilder();foreach(var stack in inv.items)s.Append(stack.item).Append(": ").Append(stack.count).Append("  ");if(inv.bulk.litres>0)s.Append("Pay dirt: ").Append(inv.bulk.litres).Append(" L");return s.ToString();}
        static string CostText(ItemStack[] items){var text=new System.Text.StringBuilder();foreach(var item in items)text.Append(item.count).Append(" ").Append(item.item).Append("  ");return text.ToString();}
        void Place(bool pile){var eye=world.RenderToBody(world.View.transform.position);var ray=new Double3(world.View.transform.forward);Double3 point=eye+ray*7;bool hit=false;for(double t=.5;t<=12;t+=.25){var p=eye+ray*t;if(p.Magnitude<=world.definition.radius+surface.SampleHeight(p)){point=surface.GetSurfacePositionPrecise(p.Float.normalized,0);hit=true;break;}}if(!hit){message="Aim at nearby clear terrain";return;}var c=Command(pile?"pile":"place");c.pose=Pose(point);c.definition=buildNames[palette];Execute(c);}
        public void Dispose(){CancelRoutePlanning();DisposeTailings();DisposeConveyors();DisposeCables();ReleaseUi();if(driver!=null){world.AutomaticPlayerStep=true;driver=null;driverMotor=null;ArmyLifetime.Destroy(driverProfile);}Authority.Save();if(ownsAuthority)Authority.Dispose();foreach(var mesh in pileMeshes.Values)ArmyLifetime.Destroy(mesh);ArmyLifetime.Destroy(contactMesh);ArmyLifetime.Destroy(host);foreach(var collider in pileContacts)ArmyLifetime.Destroy(collider.gameObject);ArmyLifetime.Destroy(cube);ArmyLifetime.Destroy(prototypeMaterial);ArmyLifetime.Destroy(placementMaterial);ArmyLifetime.Destroy(structure);ArmyLifetime.Destroy(beltMaterial);foreach(var material in itemMaterials.Values)ArmyLifetime.Destroy(material);ArmyLifetime.Destroy(earth);}
    }
}





