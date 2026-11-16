using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;
namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        readonly HashSet<int> attemptedCampBodies=new HashSet<int>();readonly HashSet<long> indexedCamps=new HashSet<long>();
        readonly Dictionary<long,SurvivalCampPublic> clientCamps=new Dictionary<long,SurvivalCampPublic>();
        readonly Dictionary<long,SurvivalCampView> campViews=new Dictionary<long,SurvivalCampView>();
        internal List<SurvivalCampBeacon> NavigationCamps=>latest?.campBeacons;
        static long CampEntity(long id)=>checked(id*8+7);
        void EnsureCampNear(FramePose near){
            if(universe==null||!universe.Owns(near.body,Config.instanceId)||!attemptedCampBodies.Add(near.body)||authority.World.camps.Exists(c=>c.pose.body==near.body)||!surfaces.TryGetValue(near.body,out var surface))return;
            bool Clear(Double3 point){var pose=new FramePose{body=near.body,x=point.x,y=point.y,z=point.z};foreach(var actor in authority.World.actors)if(actor.pose.Distance(pose)<350)return false;foreach(var deposit in authority.World.deposits)if(deposit.pose.Distance(pose)<300)return false;return !BuildingAuthority.Overlaps(near.body,point,SurvivalCampGeneration.Rotation(pose),SurvivalCampGeneration.ExclusionVolume,400)&&ShipPathClear(-1,near.body,point,point,300);}
            var camp=SurvivalCampGeneration.Generate(surface,near,authority.World.nextId,Config.seed,Clear);if(camp==null){Debug.LogWarning("No suitable dry camp site near the arrival region on body "+near.body);return;}authority.InitializeSurvivalCamp(camp);Debug.Log("Generated "+camp.name+" with two landing pads and three barter shops");
        }
        void UpdateCamps(){
            foreach(var peer in peers.Values){var actor=authority.World.Actor(peer.record.actor);if(actor.health>0)EnsureCampNear(actor.pose);}
            foreach(var camp in authority.World.camps)if(indexedCamps.Add(camp.id))serverContacts.Put(SurvivalCampGeometry.Contact(SurvivalCampPresentation.Public(camp)));
            foreach(var ship in authority.World.multiplayer.ships)if(ship.landingCamp!=0){var camp=authority.World.Camp(ship.landingCamp);if(camp==null||camp.pose.body!=ship.pose.body||ship.landingPad<0||ship.landingPad>=camp.pads.Count||!ship.autoFlight&&(!ship.landed||ship.pose.Distance(camp.pads[ship.landingPad].pose)>45))ClearCampReservation(ship);}
        }
        static void ClearCampReservation(SurvivalShipState ship){ship.landingCamp=0;ship.landingPad=-1;}
        long PadOccupant(SurvivalCampState camp,int pad){foreach(var ship in authority.World.multiplayer.ships)if(ship.pose.body==camp.pose.body&&(ship.landingCamp==camp.id&&ship.landingPad==pad||ship.pose.Distance(camp.pads[pad].pose)<85))return ship.entity;return 0;}
        List<SurvivalCampBeacon> CampBeacons(int body){var result=new List<SurvivalCampBeacon>();foreach(var camp in authority.World.camps)if(camp.pose.body==body){var beacon=new SurvivalCampBeacon{id=camp.id,name=camp.name,pose=camp.pose,pads=camp.pads};for(int i=0;i<camp.pads.Count;i++)beacon.occupied.Add(PadOccupant(camp,i));result.Add(beacon);}return result;}
        void PublishCamps(){foreach(var camp in authority.World.camps)AddEntity(CampEntity(camp.id),"camp",camp.pose,JsonUtility.ToJson(SurvivalCampPresentation.Public(camp)),280);}
        string LandAtCamp(Peer peer,long campId,int pad){
            var ship=peer.motion.ship;var camp=authority.World.Camp(campId);if(ship==null||ship.owner!=peer.record.actor||ship.pilot!=peer.record.actor||peer.record.controlledShip!=ship.entity)return "Only the owner in the pilot seat can request pad landing";
            if(camp==null||camp.pose.body!=ship.pose.body||pad<0||pad>=camp.pads.Count)return "Landing pad unavailable";
            if(ship.landed||ship.autoFlight||ship.dockedStation!=0||ship.collisionRadius>60)return "Use a free-flying ship that fits the 120 m pad";
            long occupant=PadOccupant(camp,pad);if(occupant!=0&&occupant!=ship.entity)return "Pad is occupied or reserved by another ship";
            if(ship.recallReadyTick>clock.Tick)return "Landing requests are cooling down";
            if(!SealForTakeoff(ship))return "Clear the rear hatch and ramp before automatic flight";
            var target=camp.pads[pad].pose;var location=SurvivalCampGeneration.Point(target);if(!RecallPathClear(ship,location,location,0))return "Clear all people from the landing footprint";
            var simulation=GetShipSimulation(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition);string error=simulation.ship.BeginPadLanding(location,SurvivalCampGeneration.Rotation(target));if(error!=null)return error;
            ship.landingCamp=camp.id;ship.landingPad=pad;ship.rearOpen=ship.innerOpen=false;interiorModels.Remove(ship.entity);ship.recallReadyTick=clock.Tick+Config.tickRate*2;CaptureShip(ship,simulation);ResetShipPrediction(ship);return null;
        }
        string CampFlightAction(Peer peer,bool launch){
            var ship=peer.motion.ship;if(ship==null||ship.owner!=peer.record.actor||ship.pilot!=peer.record.actor||peer.record.controlledShip!=ship.entity)return "Pilot your own ship first";
            if(ship.landingCamp==0)return "Your ship has no active pad landing or launch berth";
            var simulation=GetShipSimulation(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition);
            if(launch){if(!ship.landed||ship.autoFlight)return "Land before requesting launch";if(!SealForTakeoff(ship))return "Clear the hatch and ramp before launch";string launchError=simulation.ship.BeginPadLaunch();if(launchError!=null)return launchError;}
            else {if(!ship.autoFlight||ship.landed)return "There is no active automatic pad approach";simulation.ship.CancelRecall("PAD APPROACH CANCELLED");ClearCampReservation(ship);}
            CaptureShip(ship,simulation);ResetShipPrediction(ship);return null;
        }
        void ReceiveCamps(SurvivalSnapshot packet){
            foreach(long id in packet.removed)if(id%8==7){long camp=id/8;clientCamps.Remove(camp);clientContacts.Remove(id);if(campViews.Remove(camp,out var view))view.Dispose();}
            foreach(var entity in packet.changed)if(entity.kind=="camp"){var data=JsonUtility.FromJson<SurvivalCampPublic>(entity.data);if(!clientCamps.ContainsKey(data.id))clientContacts.Put(SurvivalCampGeometry.Contact(data));clientCamps[data.id]=data;}
        }
        void PresentCamps(){if(clientWorld==null||!clientWorld.Ready)return;foreach(var camp in clientCamps.Values)if(camp.pose.body==clientWorld.definition.bodyId){if(!campViews.TryGetValue(camp.id,out var view))campViews.Add(camp.id,view=new SurvivalCampView(clientWorld,camp));view.Present();}}
        void DisposeCamps(){foreach(var view in campViews.Values)view.Dispose();campViews.Clear();clientCamps.Clear();indexedCamps.Clear();attemptedCampBodies.Clear();}
    }
}




