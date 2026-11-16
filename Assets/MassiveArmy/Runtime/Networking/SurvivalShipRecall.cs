using System;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        UnityEngine.Vector2 recallMenuScroll;
        bool RecallPathClear(SurvivalShipState ship,Double3 from,Double3 to,float dt){
            if(authority!=null){foreach(var actor in authority.World.actors)if(!authority.World.multiplayer.players.Exists(p=>p.actor==actor.id&&p.attachedShip==ship.entity)&&!actor.networkAbsent&&actor.health>0&&actor.pose.body==ship.pose.body&&SegmentDistance(from,to,SurvivalVehicleSimulation.Position(actor.pose))<ship.collisionRadius+1)return false;}
            else {foreach(var entity in visible.Values)if(entity.kind=="player"&&entity.pose.body==ship.pose.body){var player=UnityEngine.JsonUtility.FromJson<SurvivalPlayerProxy>(entity.data);if(player.parentKind=="ship"&&player.parent==ship.entity||player.parentKind=="vehicle"&&clientMachines.TryGetValue(player.parent,out var cargo)&&cargo.pose.frame==ship.entity)continue;if(SegmentDistance(from,to,SurvivalVehicleSimulation.Position(entity.pose))<ship.collisionRadius+1)return false;}}
            return universe==null||universe.SweepCelestials(ship.pose.body,from,to,dt,out _,true)>=1;
        }
        string RecallShip(Peer peer,long entity){
            var world=authority.World;var ship=world.multiplayer.ships.Find(s=>s.entity==entity);
            if(ship==null||ship.owner!=peer.record.actor)return "Only the ship owner can recall it";
            var actor=world.Actor(peer.record.actor);
            if(actor.health<=0||peer.record.attachedShip!=0||peer.record.controlledEntity!=0)return "Call your ship while alive and on foot on the surface";
            if(ship.pose.body!=actor.pose.body)return "Ship recall currently requires you and your ship to be at the same planet";
            if(ship.dockedStation!=0)return "Undock the ship before recalling it";
            if(ship.pilot!=0||world.multiplayer.players.Exists(p=>p.attachedShip==ship.entity))return "Ship recall requires an empty ship, including disconnected occupants";
            if(ship.recalling||ship.autoFlight)return "Your ship is already on an automatic flight";
            if(ship.recallReadyTick>clock.Tick)return "Ship recall is cooling down";
            var surface=surfaces[actor.pose.body];var feet=SurvivalVehicleSimulation.Position(actor.pose);var ground=surface.ProjectToSurface(feet,.05);
            if((feet-ground).Magnitude>3||surface.SampleHeight(feet)<=surface.Definition.oceanHeight+.2)return "Call your ship from dry ground";
            if(ship.landed&&actor.pose.Distance(ship.pose)<250)return "Your ship is already landed nearby";
            var assembly=world.Ship(ship.owner);assembly?.UpdatePower();
            var simulation=GetShipSimulation(ship,assembly,surface.Definition);Double3 landing=default;UnityEngine.Quaternion rotation=default;bool found=false;
            if(!simulation.ship.RecallPowered)return "Ship recall needs a powered cockpit and engines";
            // Throttle failed site searches too, so repeated commands cannot flood CPU terrain queries.
            ship.recallReadyTick=clock.Tick+Config.tickRate*10;
            // Fixed bounded search; the server chooses the site, never a client-supplied destination.
            for(int i=0;i<128;i++)if(simulation.ship.FindRecallLanding(feet,i,out landing,out rotation)&&RecallPathClear(ship,landing,landing,0)){found=true;break;}
            if(!found)return "No clear, dry, level landing area nearby; move to more open ground";
            if(!SealForTakeoff(ship))return "Clear the exterior hatch before recalling the ship";
            if(!simulation.ship.BeginRecall(landing,rotation,Config.shipRecallSpeed))return "Ship recall needs a powered cockpit and engines";
            ClearCampReservation(ship);ship.rearOpen=ship.innerOpen=false;interiorModels.Remove(ship.entity);ship.recallReadyTick=clock.Tick+Config.tickRate*10;CaptureShip(ship,simulation);return null;
        }
        string CancelShipRecall(Peer peer,long entity){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);if(ship==null||ship.owner!=peer.record.actor)return "Only the ship owner can cancel recall";
            if(!ship.recalling)return "Your ship is not being recalled";
            var simulation=GetShipSimulation(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition);simulation.ship.CancelRecall("RECALL CANCELLED");CaptureShip(ship,simulation);return null;
        }
        void ShipRecallGUI(){
            if(latest.ownedShip==0)return;
            networkUi.Label("YOUR SHIP #"+latest.ownedShip+" · empty ship, same planet");
            if(clientShips.TryGetValue(latest.ownedShip,out var ship)&&!string.IsNullOrEmpty(ship.recallStatus))networkUi.Label(ship.recallStatus);
            networkUi.BeginHorizontal();if(networkUi.Button("CALL SHIP TO LAND NEAR ME"))Submit(new SurvivalAction{kind="ship-recall",entity=latest.ownedShip});
            if(networkUi.Button("CANCEL RECALL"))Submit(new SurvivalAction{kind="ship-recall-cancel",entity=latest.ownedShip});networkUi.EndHorizontal();
        }
    }
}

