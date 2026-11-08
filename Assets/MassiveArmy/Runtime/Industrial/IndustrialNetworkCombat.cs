using System;
using System.IO;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalWeaponAttack {
        public long actor,victim,sequence,nextAttackTick;public string weapon,ammo;public double damage;
    }
    public sealed partial class IndustrialAuthority {
        // Called after server-only catalogue, aim, obstruction and clock checks.
        // Ammunition, damage, receipt and cooldown share one durable mutation.
        public string ExecuteSurvivalWeapon(long actor,long victim,long sequence,string weapon,string ammo,double damage,double range,long nextAttackTick){
            lock(gate){
                var from=World.Actor(actor);var record=World.multiplayer?.players.Find(p=>p.actor==actor);var to=victim==0?null:World.Actor(victim);
                if(from==null||record==null||from.health<=0||sequence<=from.playerCombatSequence||string.IsNullOrEmpty(weapon)||from.inventory.AtSlot(from.inventory.activeSlot)?.item!=weapon)return "Weapon is unavailable";
                if(double.IsNaN(damage)||double.IsInfinity(damage)||damage<=0||damage>1000||double.IsNaN(range)||double.IsInfinity(range)||range<=0||range>4000||nextAttackTick<=World.multiplayer.serverTick)return "Invalid weapon definition or clock";
                if(record.nextAttackTick>World.multiplayer.serverTick)return "Weapon is cooling down";
                if(victim!=0&&(victim==actor||to==null||to.health<=0||from.pose.Distance(to.pose)>range+2))return "Invalid weapon target";
                string policy=World.CombatPolicyError(actor);if(policy!=null)return policy;
                if(victim!=0&&!World.government.CanAttack(actor,victim))return "Friendly-fire protection: same realm or allied tribes";
                if(!string.IsNullOrEmpty(ammo)&&from.inventory.Count(ammo)<1)return "No ammunition";
                var attack=new SurvivalWeaponAttack{actor=actor,victim=victim,sequence=sequence,weapon=weapon,ammo=ammo,damage=damage,nextAttackTick=nextAttackTick};
                Append(new IndustrialJournalEntry{weaponAttack=attack});ApplySurvivalWeapon(World,attack);return null;
            }
        }
        static void ApplySurvivalWeapon(IndustrialWorld world,SurvivalWeaponAttack attack){
            var actor=world.Actor(attack.actor);var record=world.multiplayer?.players.Find(p=>p.actor==attack.actor);
            if(actor==null||record==null)throw new InvalidDataException("Missing weapon attacker");
            if(attack.sequence<=actor.playerCombatSequence)return;
            if(!string.IsNullOrEmpty(attack.ammo)&&!actor.inventory.Remove(attack.ammo,1))throw new InvalidDataException("Weapon journal overdraw");
            if(attack.victim!=0){var victim=world.Actor(attack.victim)??throw new InvalidDataException("Missing weapon victim");victim.health=Math.Max(0,victim.health-attack.damage);victim.playerCombatRevision++;}
            actor.playerCombatSequence=attack.sequence;record.nextAttackTick=attack.nextAttackTick;
        }
    }
}
