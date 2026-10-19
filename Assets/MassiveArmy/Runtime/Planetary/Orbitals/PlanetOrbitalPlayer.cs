using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetFirstPersonController {
        OrbitalStationView station;public PlanetOrbitalPresentation Orbitals;public bool AttachedToStation=>station!=null;
        public long StationId=>station?.State.id??0;
        public void EnterStation(OrbitalStationView value,Vector3 feet,Vector3 facing){attached=false;station=value;shipPosition=feet;forward=Vector3.ProjectOnPlane(facing,Vector3.up).normalized;if(forward.sqrMagnitude<.1f)forward=Vector3.forward;pitch=verticalSpeed=0;carrierVelocity=Vector3.zero;grounded=true;SetActive(true);PresentStation();}
        bool AttachStation(Collider support){var found=Orbitals?.Support(support);if(found==null)return false;if(attached)Detach();var feet=found.BodyToLocal(position);var facing=Quaternion.Inverse(found.Rotation)*forward;EnterStation(found,feet,facing);return true;}
        void StepStation(float dt,Vector3 horizontal){verticalSpeed-=9.81f*dt;var foot=station.LocalToRender(shipPosition);var result=collision.Move(foot,station.Rotation*Vector3.up,station.Rotation*(horizontal+Vector3.up*verticalSpeed)*dt,verticalSpeed<=0,out var floor);shipPosition+=Quaternion.Inverse(station.Rotation)*(result-foot);grounded=floor!=null;if(grounded&&verticalSpeed<0)verticalSpeed=0;
            if(ship!=null&&ship.Owns(floor)){position=station.LocalToBody(shipPosition);forward=station.Rotation*forward;station=null;Attach();PresentShip();return;}
            if(!station.NearDeck(shipPosition)){position=station.LocalToBody(shipPosition);forward=station.Rotation*forward;carrierVelocity=station.VelocityAt(shipPosition);station=null;verticalSpeed=0;PresentPlanet();return;}
            Environment.Pressurized=station.Pressurized(shipPosition);Environment.Step(surface.Definition,(float)(station.Pose.position.Magnitude-surface.Definition.radius),dt);PresentStation();
        }
        void PresentStation(){var feet=station.LocalToBody(shipPosition);var eye=station.LocalToBody(shipPosition+Vector3.up*EyeHeight);origin.Anchor(surface.Definition.position.Add(eye));body.position=origin.RenderPosition(surface.Definition.position.Add(feet));body.rotation=station.Rotation*Quaternion.LookRotation(forward,Vector3.up);camera.transform.SetPositionAndRotation(Vector3.zero,body.rotation*Quaternion.Euler(pitch,0,0));camera.nearClipPlane=.05f;camera.farClipPlane=Mathf.Max(30000,(float)station.Pose.position.Magnitude*3);}
    }
    public sealed partial class PlanetDevelopment {
        public PlanetOrbitalPresentation Orbitals {get;private set;}
        internal PlanetSurface Surface=>surface;
        void UpdateOrbitals(){if(PlanetOrbitalStress.Control)return;var sim=Base?.Civilisation;if(sim==null)return;sim.Orbitals.Observe(this);if(Orbitals==null&&sim.Orbitals.Assets(sim.BodyId(definition.bodyId)).Count>0){Orbitals=new PlanetOrbitalPresentation(this);player.Orbitals=Orbitals;}Orbitals?.Tick();}
        bool InteractStation(){if(!IsFirstPerson||Orbitals==null)return false;if(Orbitals.DoorInteraction(view.transform.position,view.transform.forward,out var station)<0)return false;bool result=station.ToggleDoor();if(!result)Base.Notice="Airlock obstructed";return true;}
        string StationInteractionPrompt(){if(IsFirstPerson&&Orbitals!=null&&Orbitals.DoorInteraction(view.transform.position,view.transform.forward,out var s)>=0)return s.State.airlockOpen?"F - CLOSE CREW AIRLOCK":"F - OPEN CREW AIRLOCK";return "";}
    }
}
