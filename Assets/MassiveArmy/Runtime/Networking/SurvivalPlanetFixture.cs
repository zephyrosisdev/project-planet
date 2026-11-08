#if UNITY_EDITOR || UNITY_SERVER
using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalPlanetFixtureSpec {public int version=1;public bool seeded;public string configPath,error;public long actor;public int body;}
    public sealed partial class SurvivalNetworkSession {
        internal static bool SeedPlanetFixture(string path){
            SurvivalPlanetFixtureSpec spec=null;SurvivalUniverse model=null;
            try{
                if(!DiagnosticPath(path))throw new InvalidOperationException("Planet fixtures require a disposable Live save");spec=JsonUtility.FromJson<SurvivalPlanetFixtureSpec>(File.ReadAllText(path));
                if(spec?.version!=1||spec.seeded||!DiagnosticPath(spec.configPath))throw new InvalidOperationException("Invalid planet fixture");var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();if(!DiagnosticPath(config.savePath))throw new InvalidOperationException("Invalid fixture save");
                var world=IndustrialAuthority.Load(config.savePath);model=world.universe==null?SurvivalUniverse.Create(config.worldId,config.instanceId,config.seed,ServerDefinition(config.seed)):SurvivalUniverse.Restore(world.universe,config.worldId,config.seed);var actor=world.Actor(spec.actor);var record=world.multiplayer.players.Find(p=>p.actor==spec.actor);if(actor==null||record==null)throw new InvalidOperationException("Fixture character unavailable");
                var definition=model.Bodies[1];var surface=model.Surface(definition.bodyId);Double3 feet=default;bool found=false;
                for(int i=0;i<512;i++){double y=1-2*(i+.5)/512,angle=i*2.3999632297;var direction=new Double3(Math.Sqrt(1-y*y)*Math.Cos(angle),y,Math.Sqrt(1-y*y)*Math.Sin(angle));if(surface.SampleHeight(direction)>definition.oceanHeight+2){feet=surface.ProjectToSurface(direction*definition.radius,.05);found=true;break;}}
                if(!found)throw new InvalidOperationException("Second planet has no dry fixture surface");SurvivalShipAuthority.Release(world,record);SurvivalVehicleAuthority.Release(world,record);record.attachedShip=record.pendingVehicleExit=0;record.interiorWalking=false;record.shipSeat=-1;actor.pose=record.spawn=new FramePose{body=definition.bodyId,x=feet.x,y=feet.y,z=feet.z};actor.health=100;record.oxygen=record.hunger=record.thirst=100;
                using(var authority=new IndustrialAuthority(world,config.savePath)){if(world.universe==null)authority.InitializeSurvivalUniverse(model.State);authority.Save();}spec.body=definition.bodyId;spec.seeded=true;return true;
            }catch(Exception error){if(spec!=null)spec.error=error.Message;Debug.LogError("Planet fixture: "+error.Message);return false;}
            finally{model?.Dispose();if(spec!=null)File.WriteAllText(path,JsonUtility.ToJson(spec,true));}
        }
    }
}
#endif
