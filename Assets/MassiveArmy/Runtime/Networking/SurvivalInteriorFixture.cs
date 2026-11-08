using System;
using System.IO;
using MassiveArmy.Industrial;
using UnityEngine;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalInteriorFixtureSpec {public int version=1;public bool seeded;public string configPath,error;public long owner,passenger,ship;}
#if UNITY_EDITOR || UNITY_SERVER
    public sealed partial class SurvivalNetworkSession {
        internal static bool SeedInteriorFixture(string path){
            SurvivalInteriorFixtureSpec spec=null;
            try{
                if(!DiagnosticPath(path))throw new InvalidOperationException("Interior fixtures must use disposable Live saves");spec=JsonUtility.FromJson<SurvivalInteriorFixtureSpec>(File.ReadAllText(path));
                if(spec==null||spec.version!=1||spec.seeded||!DiagnosticPath(spec.configPath))throw new InvalidOperationException("Invalid interior fixture");
                var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();if(!DiagnosticPath(config.savePath))throw new InvalidOperationException("Fixture save escaped Live");
                var world=IndustrialAuthority.Load(config.savePath);var owner=world.Actor(spec.owner);var passenger=world.Actor(spec.passenger);var ship=world.multiplayer?.ships.Find(s=>s.owner==spec.owner);
                if(owner==null||passenger==null||ship==null||!ship.landed||world.Ship(spec.owner)==null)throw new InvalidOperationException("Fixture needs two registered characters and the owner's landed ship");
                foreach(long actor in new[]{spec.owner,spec.passenger}){var player=world.multiplayer.players.Find(p=>p.actor==actor);player.attachedShip=player.controlledShip=player.controlledEntity=player.pendingVehicleExit=0;player.interiorWalking=false;player.shipSeat=-1;world.Actor(actor).pose=ShipEntry(ship);}
                ship.pilot=0;ship.rearOpen=ship.innerOpen=false;if(!ship.invitedActors.Contains(spec.passenger))ship.invitedActors.Add(spec.passenger);spec.ship=ship.entity;
                using(var authority=new IndustrialAuthority(world,config.savePath))authority.Save();spec.seeded=true;spec.error="";return true;
            }catch(Exception error){if(spec!=null)spec.error=error.Message;Debug.LogError("Interior fixture: "+error.Message);return false;}
            finally{if(spec!=null)File.WriteAllText(path,JsonUtility.ToJson(spec,true));}
        }
    }
#endif
}
#endif
