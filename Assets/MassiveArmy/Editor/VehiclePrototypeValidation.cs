using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MassiveArmy.Planetary;
using UnityEditor;
using UnityEngine;

namespace MassiveArmy.EditorTools
{
    /// <summary>Asset, kinematic/contact and bounded GPU ownership checks. Does not enter Play mode or move the user's camera.</summary>
    public static class VehiclePrototypeValidation
    {
        [Serializable] sealed class Report {public string status="PASS",error="";public List<string> checks=new List<string>();}
        sealed class Carrier:IVehicleCarrier {
            public string CarrierName=>"Fixture";public SpacePose CarrierPose {get;set;}public IVehicleCarrier ParentCarrier {get;set;}
            public VehicleCarrierBay[] VehicleBays {get;set;}public bool BoardingAvailable=>true;public bool Owns(Collider collider)=>false;
        }
        sealed class InteriorCarrier:IVehicleCarrier,IVehicleDeck,IVehicleInterior {
            public string CarrierName=>"General interior fixture";public SpacePose CarrierPose {get;set;}public IVehicleCarrier ParentCarrier=>null;
            public readonly VehicleCarrierBay Room=new VehicleCarrierBay{interior=new Bounds(new Vector3(0,3,0),new Vector3(50,6,70)),entrance=Vector2.one};
            public VehicleCarrierBay[] VehicleBays=>System.Array.Empty<VehicleCarrierBay>();public bool BoardingAvailable=>false;public bool Owns(Collider c)=>false;
            public bool SampleVehicleDeck(Double3 p,out VehicleGroundContact contact){var parent=CarrierPose;var local=Double3.Rotate(Quaternion.Inverse(parent.rotation),p-parent.position).Float;contact=new VehicleGroundContact{position=parent.position+Double3.Rotate(parent.rotation,new Double3(local.x,0,local.z)),normal=parent.rotation*Vector3.up};return Mathf.Abs(local.x)<25&&Mathf.Abs(local.z)<35&&local.y>=-1.2f;}
            public bool FitsVehicle(VehicleDefinition d,SpacePose p)=>Room.Contains(d,Double3.Rotate(Quaternion.Inverse(CarrierPose.rotation),p.position-CarrierPose.position).Float,Quaternion.Inverse(CarrierPose.rotation)*p.rotation);
        }
        [MenuItem("Synthetic Front/Vehicles/Validate prototype assets and contact math")]
        public static void Run(){
            var report=new Report();void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);report.checks.Add(label);}
            try{
                var profiles=new List<VehicleDefinition>();
                foreach(string id in new[]{"SF-MW1","SF-LUV1"}){
                    var d=Resources.Load<VehicleDefinition>("MassiveArmy/Vehicles/"+id);Check(d!=null&&d.ValidateDefinition()=="",id+" valid editable profile");profiles.Add(d);
                    var rig=d.prefab.GetComponent<VehicleRig>();Check(rig!=null&&rig.legs.Length==d.legs.Length&&rig.wheels.Length==d.wheels.Length,id+" complete rig references");
                    if(id=="SF-LUV1"){
                        bool opaque=false;foreach(var renderer in d.prefab.GetComponentsInChildren<MeshRenderer>())if(renderer.name=="Side glazing"||renderer.name=="Cabin")opaque=true;
                        Check(!opaque,"Jeep has no legacy opaque cabin or side-window panels");
                        var pane=d.prefab.transform.Find("Crew interior/Clear windows").GetComponent<MeshRenderer>();Check(pane.sharedMaterial.shader.name=="MassiveArmy/Vehicle windows"&&pane.sharedMaterial.renderQueue>=3000,"Jeep window panes use the transparent glass material");
                    }
                    var lods=d.prefab.GetComponent<LODGroup>().GetLODs();int Triangles(Renderer[] renderers){int n=0;foreach(var renderer in renderers)n+=renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3;return n;}
                    Check(lods.Length==3&&Triangles(lods[0].renderers)>Triangles(lods[1].renderers)&&Triangles(lods[1].renderers)>Triangles(lods[2].renderers),id+" LOD triangle counts decrease: "+Triangles(lods[0].renderers)+" / "+Triangles(lods[1].renderers)+" / "+Triangles(lods[2].renderers));
                    foreach(var rotation in new[]{Quaternion.identity,Quaternion.Euler(83,45,17),Quaternion.Euler(170,-20,115)})foreach(var leg in d.legs){
                        var hip=rotation*leg.hip;var target=rotation*leg.stance;WalkerKinematics.Solve(hip,target,rotation*leg.pole,rotation*Vector3.up,leg,out var knee,out var foot);
                        Check(Mathf.Abs((knee-hip).magnitude-leg.upperLength)<.001f&&Mathf.Abs((foot-knee).magnitude-leg.lowerLength)<.001f,id+" two-link lengths conserved on rotated planet face");
                    }
                }
                var seats=new VehicleSeatManager(profiles[0]);Check(seats.TryEnterPlayer(0)&&seats.AIHas(VehicleSeatPermissions.Fire),"Player driver and AI gunner occupy independent roles");Check((seats.PlayerPermissions&VehicleSeatPermissions.Fire)==0,"Driver cannot fire another seat's weapon");Check(seats.TryEnterPlayer(1)&&!seats.AIHas(VehicleSeatPermissions.Fire),"Player can take over built-in gunner seat");seats.LeavePlayer();Check(seats.AIHas(VehicleSeatPermissions.Fire),"Built-in gunner resumes after leaving seat");
                var carrier=new Carrier{CarrierPose=new SpacePose{position=new Double3(1e12,3e11,-2e12),rotation=Quaternion.Euler(20,50,70)},VehicleBays=new[]{new VehicleCarrierBay{interior=new Bounds(new Vector3(0,3,0),new Vector3(8,6,20)),entrance=new Vector2(3,3)}}};
                Check(carrier.VehicleBays[0].FitsEntrance(profiles[1])&&!carrier.VehicleBays[0].FitsEntrance(profiles[0]),"Carrier accepts utility and rejects oversized mech");
                Check(VehicleParking.IsCarriedBy(carrier,carrier),"Directly attached cargo is excluded from carrier flight obstacles");
                Check(VehicleParking.IsCarriedBy(new Carrier{ParentCarrier=carrier},carrier),"Nested attached cargo is excluded from carrier flight obstacles");
                Check(!VehicleParking.IsCarriedBy(null,carrier)&&!VehicleParking.IsCarriedBy(new Carrier(),carrier),"Unattached and unrelated vehicles remain external flight obstacles");
                var parking=new VehicleParking();var local=new Vector3(0,0,0);var pose=VehicleParking.Resolve(carrier,local,Quaternion.identity);Check(parking.TryPark(profiles[1],pose,carrier,out _),"Full vehicle envelope fits parking bay");
                var parent=carrier.CarrierPose;parent.position+=new Double3(50000,-8000,12000);parent.rotation=Quaternion.Euler(55,120,19);parent.linearVelocity=new Double3(100,200,-30);parent.angularVelocity=new Vector3(.1f,.2f,0);carrier.CarrierPose=parent;
                var followed=parking.Pose;Check((followed.position-parent.position).Magnitude<.001&&Quaternion.Angle(followed.rotation,parent.rotation)<.01f,"Parked cargo retains carrier-local pose across astronomical translations and rotations");Check((followed.linearVelocity-parent.linearVelocity).Magnitude<.001,"Cargo inherits carrier velocity");
                Check(!parking.TryPark(profiles[1],VehicleParking.Resolve(carrier,new Vector3(3,0,0),Quaternion.Euler(0,45,0)),carrier,out _),"Rotated occupied envelope cannot protrude through bay walls");
                var automatic=new VehicleParking();var moving=VehicleParking.Resolve(carrier,new Vector3(0,-.3f,0),Quaternion.identity);moving.linearVelocity+=new Double3(8,0,0);
                Check(automatic.TryFollow(profiles[1],moving,carrier)&&automatic.Parked&&Mathf.Abs(automatic.LocalPosition.y-.03f)<.001f,"Automatic cargo attachment recovers a small deck penetration without a manual lock or speed gate");
                Check(!new VehicleParking().TryFollow(profiles[1],VehicleParking.Resolve(carrier,new Vector3(20,0,0),Quaternion.identity),carrier),"Vehicles outside the bay are never attached remotely");
                var anywhere=new InteriorCarrier{CarrierPose=new SpacePose{position=new Double3(0,120010,0),rotation=Quaternion.identity}};
                var parkedAnywhere=new VehicleParking();var outsideBay=VehicleParking.Resolve(anywhere,new Vector3(15,0,20),Quaternion.identity);
                Check(parkedAnywhere.TryFollow(profiles[1],outsideBay,anywhere),"Vehicles attach to supported interiors outside named bays, regardless of entrance dimensions or boarding-door state");
                var planetFloor=ScriptableObject.CreateInstance<PlanetDefinition>();planetFloor.radius=120000;planetFloor.terrainHeight=0;planetFloor.gravitationalParameter=9.81*planetFloor.radius*planetFloor.radius;
                try{
                    var contact=new VehicleTerrainContact(new PlanetSurface(planetFloor),p=>p.Float,p=>new Double3(p)){CarrierDeck=anywhere};
                    var carriedVehicle=new VehicleController(null,contact,null,profiles[1],0,followed);
                    Check(carriedVehicle.Parking.TryPark(profiles[1],VehicleParking.Resolve(carrier,Vector3.zero,Quaternion.identity),carrier,out _),"Interaction fixture attaches a real vehicle controller to the carrier");
                    var currentCarrier=carrier.CarrierPose;currentCarrier.position+=new Double3(180,20,-40);currentCarrier.linearVelocity=new Double3(3000,0,0);carrier.CarrierPose=currentCarrier;
                    var interaction=(SpacePose)typeof(VehicleController).GetProperty("InteractionPose",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(carriedVehicle);
                    Check((interaction.position-currentCarrier.position).Magnitude<.001&&(interaction.position-carriedVehicle.Motor.Pose.position).Magnitude>100,"Vehicle interactions resolve the current carrier pose before the motor ticks");
                    Check(interaction.linearVelocity.Magnitude>2999&&carriedVehicle.Parking.Parked,"Carried vehicle interactions retain attachment at orbital speeds");
                    var motor=new WheeledVehicleMotor(profiles[1]);motor.Follow(outsideBay);
                    for(int frame=0;frame<120;frame++){motor.Step(1/60f,new VehicleInput{movement=Vector2.up},contact,anywhere);if(motor.Pose.position.y<120010.025)throw new InvalidOperationException("Carrier floor penetrated outside the cargo bay");}
                    Check(true,"Vehicle drives over a general carrier floor for 120 steps without relying on a deck collider ray hit");
                    contact.CarrierDeck=null;
                    var falling=new WalkerVehicleMotor(profiles[0]);
                    falling.Release(new SpacePose{position=new Double3(0,121000,0),rotation=Quaternion.identity,linearVelocity=new Double3(100,0,0)});
                    for(int frame=0;frame<60;frame++)falling.StepFlight(1/60f,contact);
                    Check(falling.Pose.position.y<120999&&falling.Pose.linearVelocity.y< -9&&!falling.Grounded,"Unoccupied airborne walker falls continuously without walking input");
                    Check(falling.Pose.linearVelocity.x>99.9,"Airborne motor preserves inherited horizontal velocity without ground braking");
                    var crest=new WheeledVehicleMotor(profiles[1]);crest.Follow(new SpacePose{position=new Double3(0,120000.3,0),rotation=Quaternion.identity,linearVelocity=new Double3(0,0,10)});
                    crest.Step(1/60f,new VehicleInput{movement=Vector2.up},contact);
                    Check(!crest.Grounded&&crest.Pose.position.y>120000.29,"Vehicle above a descending hill enters airtime instead of snapping down to terrain");
                    var platform=new GameObject("Temporary vehicle landing platform"){hideFlags=HideFlags.HideAndDontSave};
                    var embedded=new GameObject("Temporary embedded chassis"){hideFlags=HideFlags.HideAndDontSave};
                    try{
                        platform.transform.position=new Vector3(0,120009.5f,0);var platformBox=platform.AddComponent<BoxCollider>();platformBox.size=new Vector3(30,1,30);
                        var box=embedded.AddComponent<BoxCollider>();box.center=profiles[1].chassisCentre;box.size=profiles[1].chassisSize;embedded.transform.position=new Vector3(0,120009,0);Physics.SyncTransforms();
                        contact.IgnoredCollider=box;
                        var recovered=contact.RecoverPenetration(new Double3(embedded.transform.position),Quaternion.identity,profiles[1]);
                        Check(!Physics.ComputePenetration(box,recovered.Float,Quaternion.identity,platformBox,platform.transform.position,Quaternion.identity,out _,out _),"Embedded chassis is separated from solid platform geometry");
                        Check(contact.TryRecovery(new Double3(embedded.transform.position),Quaternion.identity,profiles[1],out _),"Recovery finds a nearby unobstructed chassis position");
                        UnityEngine.Object.DestroyImmediate(embedded);contact.IgnoredCollider=null;
                        var settling=new WheeledVehicleMotor(profiles[1]);settling.Follow(new SpacePose{position=new Double3(0,120009.6,0),rotation=Quaternion.identity});
                        settling.Step(1/60f,default,contact);
                        Check(settling.Grounded&&Math.Abs(settling.Pose.linearVelocity.y)<.1,"Terrain height correction does not inject upward launch velocity");
                        double highest=settling.Pose.position.y;
                        for(int frame=0;frame<180;frame++){settling.Step(1/60f,default,contact);highest=Math.Max(highest,settling.Pose.position.y);}
                        Check(highest<120010.1&&settling.Grounded,"Stationary vehicle settles on platform without repeated bouncing");
                        var landed=new WheeledVehicleMotor(profiles[1]);landed.Release(new SpacePose{position=new Double3(0,120010.5,0),rotation=Quaternion.identity,linearVelocity=new Double3(0,-30,0)});
                        for(int frame=0;frame<10&&!landed.Grounded;frame++)landed.StepFlight(1/60f,contact);
                        var start=landed.Pose.position;for(int frame=0;frame<60;frame++)landed.Step(1/60f,new VehicleInput{movement=Vector2.up},contact);
                        Check(landed.Grounded&&landed.Pose.position.z>start.z+.5,"Collider landing returns control to driving without an airborne deadlock");
                    }finally{if(embedded!=null)UnityEngine.Object.DestroyImmediate(embedded);UnityEngine.Object.DestroyImmediate(platform);contact.IgnoredCollider=null;}
                    foreach(var profile in profiles){
                        using(var boosted=new VehicleController(null,contact,null,profile,0,new SpacePose{position=new Double3(0,121000,0),rotation=Quaternion.identity})){
                            boosted.Seats.TryEnterPlayer(0);var start=boosted.Motor.Pose.position;
                            for(int frame=0;frame<30;frame++)boosted.Tick(1/60f,new VehicleInput{boost=true},null);
                            Check(boosted.Airborne&&boosted.Motor.Pose.position.y>start.y&&boosted.BoostCharge<.8,profile.id+" jump booster lifts vehicle and consumes charge");
                            for(int frame=0;frame<90;frame++)boosted.Tick(1/60f,new VehicleInput{boost=true},null);
                            float spent=boosted.BoostCharge;Check(!boosted.Boosting&&spent>.4,profile.id+" held booster stops at the burst limit");
                            for(int frame=0;frame<30;frame++)boosted.Tick(1/60f,default,null);
                            Check(Mathf.Abs(boosted.BoostCharge-spent)<.001,profile.id+" booster observes recharge delay");
                            for(int frame=0;frame<360;frame++)boosted.Tick(1/60f,default,null);
                            Check(boosted.BoostCharge>.999,profile.id+" booster fully recharges after release and delay");
                        }
                    }
                    double radius=125000,speed=Math.Sqrt(planetFloor.gravitationalParameter/radius);
                    var orbit=new WheeledVehicleMotor(profiles[1]);orbit.Release(new SpacePose{position=new Double3(0,radius,0),rotation=Quaternion.identity,linearVelocity=new Double3(speed,0,0)});
                    for(int frame=0;frame<3600;frame++)orbit.StepFlight(1/60f,contact);
                    Check(Math.Abs(orbit.Pose.position.Magnitude-radius)<.1&&!orbit.Grounded,"Released vehicle maintains a circular gravity orbit for 60 simulated seconds");
                    var handoff=new VehicleParking();handoff.TryFollow(profiles[1],outsideBay,anywhere);
                    var exitPose=outsideBay;exitPose.position+=new Double3(0,0,40);handoff.FollowSupport(anywhere,exitPose);handoff.Release(out var released);
                    Check((released.position-exitPose.position).Magnitude<.00001,"Detachment uses the current exit position rather than the previous parked pose");
                }finally{UnityEngine.Object.DestroyImmediate(planetFloor);}
                Check(PlanetVehiclePool.ShowStrategicMarker(true,0,12)&&!PlanetVehiclePool.ShowStrategicMarker(true,0,64),"Friendly vehicle icons appear only when models are small on screen");
                Check(!PlanetVehiclePool.ShowStrategicMarker(false,0,12)&&!PlanetVehiclePool.ShowStrategicMarker(true,1,12),"Destroyed and non-owned vehicles do not receive friendly vehicle markers");
                ValidateViewLook(Check);ValidateRigPool(profiles,Check);ValidateMotor(profiles,Check);ValidatePlantedFeet(profiles[0],Check);ValidateGpu(Check);
            }catch(Exception e){report.status="FAIL";report.error=e.ToString();Debug.LogException(e);}
            Directory.CreateDirectory("TestResults/Vehicles");File.WriteAllText("TestResults/Vehicles/validation.json",JsonUtility.ToJson(report,true));Debug.Log("Vehicle asset/contact validation "+report.status+": "+report.checks.Count+" checks");
        }
        static void ValidateRigPool(List<VehicleDefinition> profiles,Action<bool,string> check){
            var root=new GameObject("Temporary rig budget validation"){hideFlags=HideFlags.HideAndDontSave};root.transform.position=Vector3.one*1000000;
            try{using(var pool=new VehicleRigPool(root.transform)){
                pool.Limit=1;var first=pool.Acquire(profiles[0]);check(first!=null&&pool.Allocated==1&&pool.Acquire(profiles[1])==null,"Rig allocation obeys a one-rig limit");pool.Release(first);var reused=pool.Acquire(profiles[0]);check(reused==first,"Dormant compatible rig is reused");pool.Release(reused);
                var other=pool.Acquire(profiles[1]);check(other!=null&&pool.Allocated==1,"Replacing a cached profile stays within the budget");pool.Release(other);
                pool.Limit=4;var rigs=new List<VehicleRig>();foreach(var profile in new[]{profiles[0],profiles[1],profiles[0],profiles[1]})rigs.Add(pool.Acquire(profile));check(pool.Allocated==4,"Rig budget can increase live");
                pool.Limit=1;foreach(var rig in rigs)pool.Release(rig);check(pool.Allocated==1,"Lowering the limit evicts surplus dormant rigs");
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static void ValidateViewLook(Action<bool,string> check){
            foreach(var heading in new[]{Quaternion.identity,Quaternion.Euler(72,131,26),Quaternion.Euler(175,40,-91)})foreach(float pitch in new[]{-25f,25f}){
                var expected=heading*Quaternion.Euler(pitch,0,0);var angles=VehicleAttitude.SuspensionAngles(heading,expected*Vector3.up);var actual=heading*Quaternion.Euler(angles.x,0,-angles.y);
                check(Vector3.Dot(actual*Vector3.forward,expected*Vector3.forward)>.9999f,"Vehicle pitch follows uphill/downhill ramp normal on rotated planet faces");
            }
            var look=new VehicleViewLook();look.Step(new Vector2(200,-80),false,true,100,35,1/60f);var aim=look.ForwardRotation;
            look.Step(new Vector2(400,150),true,true,100,35,1/60f);
            check(Quaternion.Angle(aim,look.ForwardRotation)<.001f&&Mathf.Abs(look.HeadYaw)>20,"Alt free look rotates the head without changing weapon aim");
            float before=Mathf.Abs(look.HeadYaw);look.Step(Vector2.zero,false,true,100,35,1/60f);
            check(Mathf.Abs(look.HeadYaw)>0&&Mathf.Abs(look.HeadYaw)<before,"Releasing Alt starts a smooth return rather than an instantaneous snap");
            for(int i=0;i<20;i++)look.Step(Vector2.zero,false,true,100,35,1/60f);
            check(Mathf.Abs(look.HeadYaw)<.1f&&Quaternion.Angle(aim,look.ForwardRotation)<.001f,"Head returns quickly to seat aim without moving the aim itself");
            look.Reset();look.Step(new Vector2(100,100),false,false,100,35,1/60f);check(Quaternion.Angle(look.ViewRotation,Quaternion.identity)<.001f,"Driver camera stays forward unless free look is held");
        }
        static void ValidatePlantedFeet(VehicleDefinition profile,Action<bool,string> check){
            var planet=ScriptableObject.CreateInstance<PlanetDefinition>();planet.radius=120000;planet.terrainHeight=0;planet.gravitationalParameter=9.81*planet.radius*planet.radius;
            var host=new GameObject("Temporary planted foot regression"){hideFlags=HideFlags.HideAndDontSave};var camera=new GameObject("Temporary vehicle camera").AddComponent<Camera>();camera.enabled=false;
            var world=host.AddComponent<PlanetDevelopment>();world.definition=planet;var origin=new FloatingOrigin(planet.position.Add(new Double3(-10000,0,0)));
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(PlanetDevelopment).GetField("origin",flags).SetValue(world,origin);typeof(PlanetDevelopment).GetField("view",flags).SetValue(world,camera);
            var surface=new PlanetSurface(planet);var terrain=new VehicleTerrainContact(world,surface);var initial=surface.GetSurfacePositionPrecise(Vector3.up,.025);camera.transform.position=world.RenderBodyPosition(initial)+new Vector3(0,8,-8);
            var pool=new VehicleRigPool(host.transform);var vehicle=new VehicleController(world,terrain,pool,profile,0,new SpacePose{position=initial,rotation=Quaternion.identity});vehicle.Seats.TryEnterPlayer(0);vehicle.SetDetail(true);
            try{
                var rig=host.GetComponentInChildren<VehicleRig>();var previous=new Double3[rig.legs.Length];var planted=new bool[rig.legs.Length];int stable=0,steps=0;vehicle.FootPlanted+=(leg,p,speed)=>steps++;
                for(int frame=0;frame<360;frame++){
                    vehicle.Tick(1/60f,new VehicleInput{movement=new Vector2(frame<180?0:.25f,1)},null);
                    if(frame%20==0)origin.Anchor(planet.position.Add(vehicle.Motor.Pose.position+new Double3(-10000,0,0)));vehicle.Rebase();
                    for(int i=0;i<rig.legs.Length;i++){
                        bool now=rig.legs[i].state==WalkerFootState.Planted;var position=world.RenderToBody(rig.legs[i].foot.position);
                        if(now&&planted[i]){if((position-previous[i]).Magnitude>.02)throw new InvalidOperationException("Planted foot moved during chassis movement/rebase: "+(position-previous[i]).Magnitude);stable++;}
                        previous[i]=position;planted[i]=now;
                    }
                }
                check(stable>100&&steps>=4&&(vehicle.Motor.Pose.position-initial).Magnitude>3,"Biped alternates steps and keeps planted feet fixed while driving, turning and rebasing");
            }finally{vehicle.Dispose();pool.Dispose();world.definition=null;UnityEngine.Object.DestroyImmediate(host);if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(planet);}
        }
        static void ValidateMotor(List<VehicleDefinition> profiles,Action<bool,string> check){
            var planet=ScriptableObject.CreateInstance<PlanetDefinition>();planet.radius=120000;planet.terrainHeight=0;planet.gravitationalParameter=9.81*planet.radius*planet.radius;
            try{
                var surface=new PlanetSurface(planet);var contact=new VehicleTerrainContact(surface,p=>p.Float,p=>new Double3(p));
                foreach(var d in profiles)foreach(var up in new[]{Vector3.up,Vector3.down,new Vector3(1,.2f,-.7f).normalized}){
                    var tangent=Vector3.ProjectOnPlane(Mathf.Abs(up.y)>.9f?Vector3.forward:Vector3.up,up).normalized;
                    VehicleMotor motor=d.locomotion==VehicleLocomotion.Walker?(VehicleMotor)new WalkerVehicleMotor(d):new WheeledVehicleMotor(d);
                    motor.Follow(new SpacePose{position=surface.GetSurfacePositionPrecise(up,-.5),rotation=Quaternion.LookRotation(tangent,up)});
                    for(int i=0;i<90;i++){motor.Step(1/60f,new VehicleInput{movement=new Vector2(.2f,1)},contact);if(motor.Pose.position.Magnitude<planet.radius-.0001)throw new InvalidOperationException("Vehicle penetrated analytic terrain: "+d.id);}
                    check(motor.Pose.position.Magnitude>=planet.radius&&motor.Pose.linearVelocity.Magnitude>0,d.id+" recovers initial penetration and drives without falling through sphere at "+up);
                }
            }finally{UnityEngine.Object.DestroyImmediate(planet);}
        }
        [StructLayout(LayoutKind.Sequential)]struct Command {public Vector4 position,velocity,heading;public uint id,mode,weapons,health;}
        static void ValidateGpu(Action<bool,string> check){
            var proxy=Resources.Load<Shader>("MassiveArmy/PlanetVehicleProxy");var proxyMaterial=new Material(proxy);
            try{check(proxy!=null&&proxy.isSupported&&proxyMaterial.SetPass(0)&&!ShaderUtil.ShaderHasError(proxy),"GPU instanced vehicle proxy shader compiles");}finally{UnityEngine.Object.DestroyImmediate(proxyMaterial);}
            var shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("MassiveArmy/PlanetVehicles"));
            using(var units=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,64))using(var command=new GraphicsBuffer(GraphicsBuffer.Target.Structured,32,64)){
                var state=new[]{new UnitState{position=CubeSphere.Encode(Vector3.up),health=763,packed=5u<<8,flags=1,target=1},new UnitState{position=CubeSphere.Encode(new Vector3(.001f,1,0).normalized),health=450,flags=1,packed=1}};units.SetData(state);
                var c=new Command{id=0,mode=5,weapons=32};var data=new Command[32];data[0]=c;command.SetData(data);int kernel=shader.FindKernel("Control");shader.SetBuffer(kernel,"_PlanetUnits",units);shader.SetBuffer(kernel,"_VehicleCommands",command);shader.SetInt("_VehicleCount",1);shader.SetInt("_PlanetUnitCount",2);shader.Dispatch(kernel,1,1,1);units.GetData(state);
                check(state[0].health==763&&state[0].target==1&&(state[0].packed>>8)==32,"GPU registration preserves health and target while assigning reserved profile");
                data[0]=new Command{id=0,mode=1,weapons=1,position=new Vector4(1,0,0,0),velocity=new Vector4(0,0,8,0),heading=new Vector4(0,0,1,0)};command.SetData(data);shader.Dispatch(kernel,1,1,1);units.GetData(state);
                check(Vector3.Dot(CubeSphere.Decode(state[0].position),Vector3.right)>.9999f&&(state[0].flags&2097152)!=0&&state[0].health==763,"Player ownership updates only bounded pose fields and retains GPU health");
                data[0].mode=0;command.SetData(data);shader.Dispatch(kernel,1,1,1);units.GetData(state);check((state[0].flags&2097152)==0&&(state[0].flags&1048576)!=0,"Returning to RTS restores GPU movement ownership and retains custom proxy");
            }
            UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
 
