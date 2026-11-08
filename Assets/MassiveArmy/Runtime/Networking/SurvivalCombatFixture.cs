using System;
using System.IO;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
#if UNITY_EDITOR || UNITY_SERVER || SURVIVAL_GRAPHICS_PROBE
namespace MassiveArmy.Networking {
    [Serializable] internal sealed class SurvivalCombatFixtureSpec {public int version=1;public bool seeded;public string configPath,error;public long shooter,victim,point;}
#if UNITY_EDITOR || UNITY_SERVER
    public sealed partial class SurvivalNetworkSession {
        internal static bool SeedCombatFixture(string path){
            SurvivalCombatFixtureSpec spec=null;SurvivalUniverse model=null;
            try{
                if(!DiagnosticPath(path))throw new InvalidOperationException("Combat fixtures require disposable Live saves");spec=JsonUtility.FromJson<SurvivalCombatFixtureSpec>(File.ReadAllText(path));
                if(spec?.version!=1||spec.seeded||!DiagnosticPath(spec.configPath))throw new InvalidOperationException("Invalid combat fixture");var config=JsonUtility.FromJson<SurvivalServerConfig>(File.ReadAllText(spec.configPath));config.Validate();if(!DiagnosticPath(config.savePath))throw new InvalidOperationException("Invalid fixture save");
                var world=IndustrialAuthority.Load(config.savePath);model=world.universe==null?SurvivalUniverse.Create(config.worldId,config.instanceId,config.seed,ServerDefinition(config.seed)):SurvivalUniverse.Restore(world.universe,config.worldId,config.seed);
                var definition=model.Definition(1);definition.terrainHeight=0;definition.oceanHeight=-100;definition.stampTerrain=false;model.State.bodies.Find(b=>b.body==1).json=JsonUtility.ToJson(definition);
                foreach(long id in new[]{spec.shooter,spec.victim}){var actor=world.Actor(id);var record=world.multiplayer.players.Find(p=>p.actor==id);if(actor==null||record==null)throw new InvalidOperationException("Both fixture characters must already be registered");SurvivalVehicleAuthority.Release(world,record);SurvivalShipAuthority.Release(world,record);record.attachedShip=record.pendingVehicleExit=0;record.interiorWalking=false;record.shipSeat=-1;record.hunger=record.thirst=record.oxygen=100;record.nextAttackTick=0;actor.health=100;actor.dose=0;actor.craft=null;actor.craftQueue.Clear();actor.inventory=new Inventory{capacity=2000,slotLimit=40,activeSlot=0};double z=id==spec.victim?5:0;actor.pose=record.spawn=new FramePose{body=1,y=Math.Sqrt((definition.radius+.05)*(definition.radius+.05)-z*z),z=z};}
                world.Actor(spec.shooter).inventory.AddAt(0,"service-pistol#live",1,0);world.Actor(spec.shooter).inventory.Add("ammo-9mm",6);world.Actor(spec.victim).inventory.Add("copper-bar",3,12);
                world.respawnPoints.RemoveAll(p=>p.owner==spec.shooter||p.owner==spec.victim);spec.point=world.AllocateId();world.respawnPoints.Add(new RespawnPoint{id=spec.point,owner=spec.victim,definition="sleeping-bag",name="Combat recovery fixture",pose=world.Actor(spec.victim).pose});
                using(var authority=new IndustrialAuthority(world,config.savePath)){if(world.universe==null)authority.InitializeSurvivalUniverse(model.State);authority.Save();}spec.seeded=true;return true;
            }catch(Exception error){if(spec!=null)spec.error=error.Message;Debug.LogError("Combat fixture: "+error.Message);return false;}
            finally{model?.Dispose();if(spec!=null)File.WriteAllText(path,JsonUtility.ToJson(spec,true));}
        }
    }
#endif
}
#endif
