using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
using UnityEngine.InputSystem;
namespace MassiveArmy.Networking {
    internal static class SurvivalWeaponGeometry {
        static double Dot(Double3 a,Double3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        static double Sphere(Double3 from,Double3 direction,Double3 centre,double radius){var q=from-centre;double c=Dot(q,q)-radius*radius;if(c<=0)return 0;double b=Dot(q,direction),d=b*b-c;return d<0?double.PositiveInfinity:-b-Math.Sqrt(d);}
        internal static double Capsule(Double3 origin,Double3 direction,Double3 feet,Vector3 vertical,double radius=.35){
            var axis=new Double3(vertical.normalized);var bottom=feet+axis*.35;var top=feet+axis*1.45;var q=origin-bottom;double h=Dot(q,axis),along=Dot(direction,axis);
            var horizontal=q-axis*h;var move=direction-axis*along;double a=Dot(move,move),b=Dot(horizontal,move),c=Dot(horizontal,horizontal)-radius*radius;
            double best=double.PositiveInfinity;if(h>=0&&h<=1.1&&c<=0)return 0;
            if(a>1e-12){double d=b*b-a*c;if(d>=0){double t=(-b-Math.Sqrt(d))/a,y=h+t*along;if(t>=0&&y>=0&&y<=1.1)best=t;}}
            foreach(var centre in new[]{bottom,top}){double hit=Sphere(origin,direction,centre,radius);if(hit>=0)best=Math.Min(best,hit);}return best;
        }
        internal static bool TerrainBlocked(PlanetSurface surface,Double3 from,Double3 to){
            var delta=to-from;double distance=delta.Magnitude;int samples=Math.Max(1,(int)Math.Ceiling(distance/.5));
            for(int i=0;i<=samples;i++){var point=from+delta*(i/(double)samples);if(point.Magnitude<surface.Definition.radius+surface.SampleHeight(point)+.02)return true;}return false;
        }
    }
    public sealed partial class SurvivalNetworkSession {
        double nextWeaponInput;
        static readonly Key[] WeaponSlots={Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4,Key.Digit5,Key.Digit6,Key.Digit7,Key.Digit8,Key.Digit9,Key.Digit0};
        string FireWeapon(Peer peer,long sequence){
            if(peer.motion.vehicle!=null||peer.motion.ship!=null&&!peer.motion.interior)return "Leave the control seat before using a personal weapon";
            var actor=authority.World.Actor(peer.record.actor);var equipped=actor.inventory.AtSlot(actor.inventory.activeSlot);
            serverItems??=PersonalItemCatalogue.Load();var definition=serverItems.Find(equipped?.item);
            if(definition==null||definition.kind!=PersonalItemKind.Firearm&&definition.kind!=PersonalItemKind.Melee&&definition.kind!=PersonalItemKind.Bow)return "Equip a weapon";
            if(peer.record.nextAttackTick>authority.World.multiplayer.serverTick)return "Weapon is cooling down";
            double range=Math.Min(4000,Math.Max(0,definition.range));if(range<=0||definition.damage<=0||!SurvivalServerConfig.Finite(definition.useSeconds))return "Invalid weapon catalogue entry";
            var origin=SurvivalVehicleSimulation.Position(actor.pose);var up=peer.motion.interior?ShipRotation(peer.motion.ship)*Vector3.up:PlanetPrecisionPolicy.Direction(origin);origin+=new Double3(up)*1.65;
            var heading=peer.motion.interior?ShipRotation(peer.motion.ship)*SurvivalMovement.Forward(peer.motion):SurvivalMovement.Forward(peer.motion);var forward=SurvivalMovement.Tangent(heading,up);float angle=peer.motion.pitch*Mathf.Deg2Rad;var direction=new Double3((forward*Mathf.Cos(angle)-up*Mathf.Sin(angle)).normalized);
            long victim=0;double nearest=range;
            foreach(var candidate in peers.Values){if(candidate.record.actor==actor.id)continue;var target=authority.World.Actor(candidate.record.actor);if(target.health<=0||target.pose.body!=actor.pose.body||target.pose.frame!=actor.pose.frame)continue;
                var feet=SurvivalVehicleSimulation.Position(target.pose);double hit=SurvivalWeaponGeometry.Capsule(origin,direction,feet,PlanetPrecisionPolicy.Direction(feet),definition.kind==PersonalItemKind.Melee?.7:.35);
                if(hit<nearest){nearest=hit;victim=target.id;}
            }
            if(victim!=0){var end=origin+direction*nearest;
                if(serverContacts.RayBlocked(actor.pose.body,origin,end)||SurvivalWeaponGeometry.TerrainBlocked(surfaces[actor.pose.body],origin,end))victim=0;
                if(victim!=0)foreach(var ship in authority.World.multiplayer.ships)if(ship.pose.body==actor.pose.body&&SegmentDistance(origin,end,SurvivalVehicleSimulation.Position(ship.pose))<ship.collisionRadius){
                    var model=Interior(ship,authority.World.Ship(ship.owner));var from=SurvivalShipInteriorModel.Local(ship,new FramePose{body=actor.pose.body,x=origin.x,y=origin.y,z=origin.z});var to=SurvivalShipInteriorModel.Local(ship,new FramePose{body=actor.pose.body,x=end.x,y=end.y,z=end.z});
                    if(model.contacts.RayBlocked(actor.pose.body,SurvivalVehicleSimulation.Position(from),SurvivalVehicleSimulation.Position(to))){victim=0;break;}
                }
                if(victim!=0&&!authority.World.government.CanAttack(actor.id,victim))victim=0;
            }
            long cooldown=authority.World.multiplayer.serverTick+(long)Math.Ceiling(Math.Max(.05,definition.useSeconds)*Config.tickRate);
            return authority.ExecuteSurvivalWeapon(actor.id,victim,sequence,equipped.item,definition.ammo,definition.damage,range,cooldown);
        }
        void WeaponInput(){
            if(HeadlessProbeClient||predicted==null||ClientMenuOpen||ChatInputCaptured||!Application.isFocused||clientWorld==null||clientWorld.PersonalMenuOpen||placingMachine!=null)return;
            var keys=Keyboard.current;if(keys!=null)for(int i=0;i<10;i++)if(keys[WeaponSlots[i]].wasPressedThisFrame)Submit(new SurvivalAction{kind="slot",slot=i});
            if(Mouse.current?.leftButton.isPressed!=true||Time.realtimeSinceStartupAsDouble<nextWeaponInput)return;
            serverItems??=PersonalItemCatalogue.Load();var stack=latest.self.inventory.AtSlot(latest.self.inventory.activeSlot);var definition=serverItems.Find(stack?.item);
            if(definition==null||definition.kind==PersonalItemKind.Tool||definition.kind==PersonalItemKind.Consumable)return;
            nextWeaponInput=Time.realtimeSinceStartupAsDouble+Math.Max(.05,definition.useSeconds);Submit(new SurvivalAction{kind="shoot"});
        }
    }
}
