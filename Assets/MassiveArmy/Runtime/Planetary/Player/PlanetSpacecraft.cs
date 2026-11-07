using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
namespace MassiveArmy.Planetary
{
    /// <summary>One player capital ship. Double planet-local simulation, bounded meshes, no army mirrors.</summary>
    public sealed partial class PlanetSpacecraft:IDisposable
    {
        readonly PlanetDevelopment world;readonly PlanetSurface surface;readonly FloatingOrigin origin;readonly Camera camera;readonly GameObject hull;readonly Material steel,glass,engine;
        readonly ReferenceFrameManager frames;readonly ReferenceFrame shipFrame;SpacePose pose;bool active;Vector3 angularVelocity;
        public bool GearDeployed=true;public bool Landed {get;private set;}=true;
        public float Speed=>(float)pose.linearVelocity.Magnitude;
        public Vector3 Direction=>pose.position.Float.normalized;
        internal GameObject VisualForValidation=>hull;
        public SpacePose Pose=>pose;public ReferenceFrameManager Frames=>frames;
        public PlanetSpacecraft(PlanetDevelopment world,PlanetSurface surface,FloatingOrigin origin,Camera camera,Transform parent){
            this.world=world;this.surface=surface;this.origin=origin;this.camera=camera;frames=new ReferenceFrameManager{Origin=world.definition.position};var up=world.Base!=null?world.Base.Offset(new Vector2(0,-240)):(world.Rig.FocusDirection-Vector3.Cross(Vector3.up,world.Rig.FocusDirection).normalized*(240/world.definition.radius)).normalized;var east=Vector3.Cross(Vector3.up,up).normalized;
            pose=new SpacePose{position=surface.GetSurfacePositionPrecise(up,8),rotation=Quaternion.LookRotation(Vector3.Cross(up,east),up)};shipFrame=new ReferenceFrame(1,0,FrameKind.Ship,pose);frames.Register(shipFrame);
            hull=new GameObject("Pilotable capital ship");hull.transform.SetParent(parent);var shader=Shader.Find("HDRP/Lit");steel=new Material(shader);steel.SetColor("_BaseColor",new Color(.28f,.32f,.4f));steel.SetFloat("_Metallic",.35f);steel.SetFloat("_Smoothness",.4f);glass=new Material(shader);glass.SetColor("_BaseColor",new Color(.02f,.32f,.48f));glass.SetFloat("_Smoothness",.9f);engine=new Material(shader);engine.SetColor("_BaseColor",new Color(.04f,.5f,.9f));
            BuildInterior();InitializePilotHud();InitializeTrajectory();InitializeWeapons();InitializeLanding();Rebase();
        }
        public void SetActive(bool value){if(!value){EndCruise();OrbitalVelocityAssistActive=false;}active=value;Cursor.lockState=value?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!value;if(value){nextLandingRefresh=0;nextArcRefresh=0;PresentCamera();}}
        public void StepUnpiloted(float dt){if(world.RemoteView)return;if(!active&&!world.Paused){AdvanceFlight(dt,Vector3.zero,Vector3.zero);}}
        public void Step(float dt){if(!active)return;if(world.Paused){EndCruise();PresentCamera();return;}var k=CockpitPowered?Keyboard.current:null;var mouse=Mouse.current;Vector3 thrust=Vector3.zero,turn=Vector3.zero;bool brake=false,boost=false,cruise=false,matchOrbit=false;
            if(k!=null&&Application.isFocused&&!world.PersonalMenuOpen){thrust.z=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);thrust.x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0);thrust.y=(k.rKey.isPressed?1:0)-(k.cKey.isPressed?1:0);if(k.spaceKey.wasPressedThisFrame)TryLandingOrTakeoff();if(k.vKey.wasPressedThisFrame)SetFlightAssistance(!FlightAssistEnabled);turn.z=(k.qKey.isPressed?1:0)-(k.eKey.isPressed?1:0);brake=k.bKey.isPressed;boost=k.leftShiftKey.isPressed;if(k.oKey.wasPressedThisFrame)ShowCelestialOrbits=!ShowCelestialOrbits;if(k.pKey.wasPressedThisFrame)CycleApproachTarget();if(k.gKey.wasPressedThisFrame&&!Landed&&!AutoFlightActive)GearDeployed=!GearDeployed;if(k.lKey.wasPressedThisFrame)ToggleRotationLock();if(k.jKey.wasPressedThisFrame)ToggleDock();cruise=k.kKey.isPressed;if(k.nKey.wasPressedThisFrame)ToggleOrbitalVelocityHold();if(mouse!=null){var delta=mouse.delta.ReadValue();turn.x=-delta.y*.035f;turn.y=delta.x*.035f;}}
            if(EnginePower<=0)thrust=Vector3.zero;turn*=ThrusterPower;cruise&=EnginePower>0;AdvanceFlight(dt,thrust,turn,brake,boost,cruise,matchOrbit);PresentCamera();Rebase();
        }
        // Time acceleration shares the celestial clock, but contacts and controllers
        // still run in <=50 ms steps. Read edge-triggered input only once per frame.
        internal void AdvanceFlight(float elapsed,Vector3 thrust,Vector3 turn,bool brake=false,bool boost=false,bool cruise=false,bool matchOrbit=false){
            float duration=FlightStepDuration(elapsed,cruise,brake)*world.SimulationSpeed;
            if(duration<=0)return;
            int count=Mathf.Max(1,Mathf.CeilToInt(duration/.05f));float step=duration/count;
            double end=world.SystemRuntime?.Galaxy?.Time??duration;
            for(int i=0;i<count;i++){
                Integrate(step,thrust,turn,brake,boost,cruise,matchOrbit,end-duration+(i+1)*step);
                StepReentry(step);
            }
        }
        internal void FollowCelestialFrame(){frames.Origin=world.definition.position;frames.OriginVelocity=world.definition.velocity;}
        void PresentCamera(){FollowCelestialFrame();var eye=frames.Resolve(new SpacePose{frameId=1,position=new Double3(PilotLocalEye),rotation=PilotLocalRotation});origin.Anchor(eye.position);camera.transform.SetPositionAndRotation(Vector3.zero,eye.rotation);VehicleCameraSettings.ApplyOrDefault(world.PilotCameraSettings,camera,(eye.position.RelativeTo(world.definition.position)).Magnitude,.1f);
        }
        internal void HoldForArrival(Double3 position,Double3 velocity){EndCruise();pose.position=position;pose.linearVelocity=velocity;shipFrame.Pose=pose;nextArcRefresh=0;}
        public void Integrate(float dt,Vector3 thrust,Vector3 turn,bool brake=false,bool boost=false,bool cruise=false,bool matchOrbit=false,double simulationEndTime=double.NaN){if(dt<=0)return;OrbitalVelocityAssistActive=false;OrbitalVelocityAssistStatus="";if(!cruise)cruiseInhibited=false;else if(cruiseInhibited)cruise=false;if(world.SystemRuntime?.HoldArrival(this)==true)return;if(!cruise||brake||Landed||Docked||Docking||AutoFlightActive)EndCruise();if(OrbitalVelocityHoldEnabled&&(brake||cruise||thrust.sqrMagnitude>.001f||Landed||Docked||Docking||AutoFlightActive))StopOrbitalVelocityHold();if(matchOrbit||OrbitalVelocityHoldEnabled){if(brake||cruise)OrbitalVelocityAssistStatus="Release brake / warp to match orbit";else ApplyOrbitalVelocityAssist(dt,boost,ref thrust);}if(IntegrateDocked(dt))return;if(AutoFlightActive){IntegrateAutoFlight(dt);return;}if(Landed){pose.linearVelocity=new Double3();pose.angularVelocity=Vector3.zero;angularVelocity=Vector3.zero;shipFrame.Pose=pose;return;}if(cruise&&!brake&&IntegrateCruise(dt,turn))return;EndCruise();if(FlightAssistEnabled){IntegrateAssisted(dt,thrust,turn,brake,boost);return;}var p=pose.position;double radius=p.Magnitude;var up=p.Float.normalized;var velocity=pose.linearVelocity;
            if(RotationLocked){angularVelocity=Vector3.zero;pose.rotation=LockedAttitude(p);}else{angularVelocity=Vector3.MoveTowards(angularVelocity,turn*35,70*dt);pose.rotation=(pose.rotation*Quaternion.Euler(angularVelocity*dt)).normalized;}var acceleration=pose.rotation*Vector3.ClampMagnitude(thrust,1)*((boost?160:40)*ShipThrustScale);
            var proposed=p;var galaxy=world.SystemRuntime?.Galaxy;
            if(galaxy!=null){galaxy.Integrate(ref proposed,ref velocity,galaxy.NodeFor(world.definition),Math.Max(0,(double.IsNaN(simulationEndTime)?galaxy.Time:simulationEndTime)-dt),dt,new Double3(acceleration));}
            else {double mu=world.definition.gravitationalParameter;var a=new Double3(acceleration)-p*(mu/(radius*radius*radius));proposed=p+velocity*dt+a*(dt*dt*.5);double nextRadius=proposed.Magnitude;var b=new Double3(acceleration)-proposed*(mu/(nextRadius*nextRadius*nextRadius));velocity+=(a+b)*(dt*.5);}
            if(brake)velocity=velocity*Math.Max(0,1-dt*2);var d=proposed.Float.normalized;double floor=world.definition.radius+Mathf.Max(surface.SampleHeight(d),world.definition.oceanHeight)+(GearDeployed?8:5);
            if(proposed.Magnitude<floor){proposed=new Double3(d)*floor;float inward=Vector3.Dot(velocity.Float,d);if(inward<0)velocity-=new Double3(d)*inward;if(GearDeployed&&velocity.Magnitude<15){Landed=true;velocity=new Double3();RotationLocked=false;angularVelocity=Vector3.zero;pose.rotation=Quaternion.FromToRotation(pose.rotation*Vector3.up,d)*pose.rotation;}}
            StationCollision(p,ref proposed,ref velocity);pose.position=proposed;pose.linearVelocity=velocity;if(RotationLocked)pose.rotation=LockedAttitude(proposed);pose.angularVelocity=RotationLocked?Double3.Cross(proposed,velocity).Float/(float)(proposed.Magnitude*proposed.Magnitude):pose.rotation*(angularVelocity*Mathf.Deg2Rad);shipFrame.Pose=pose;
        }
        internal void ResetForSystemValidation(SpacePose state){ResetFlightState(state);PresentCamera();}
        internal void Arrive(SpacePose state,bool assist,bool gear,bool inhibitCruise=false){
            ResetFlightState(state);cruiseInhibited=inhibitCruise;GearDeployed=gear;SetFlightAssistance(assist);PresentCamera();world.Orbitals?.Tick();Rebase();
        }
        public void Rebase(){PresentReentry();bool visible=!world.RemoteView&&(!world.IsSurvival||world.HasRespawnCarrier);if(hull.activeSelf!=visible)hull.SetActive(visible);pilotUi?.Tick();UpdateInterior();hull.transform.SetPositionAndRotation(origin.RenderPosition(world.definition.position.Add(pose.position)),pose.rotation);}
        public void Dispose(){ReleaseReentryView();SetActive(false);DisposeTrajectory();DisposeWeapons();DisposeLanding();pilotUi?.Dispose();ArmyLifetime.Destroy(hull);DisposeInterior();ArmyLifetime.Destroy(steel);ArmyLifetime.Destroy(glass);ArmyLifetime.Destroy(engine);}
    }
}
