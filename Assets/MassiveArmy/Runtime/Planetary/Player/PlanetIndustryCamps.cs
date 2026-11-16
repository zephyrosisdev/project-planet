using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Networking;
using UnityEngine;
namespace MassiveArmy.Planetary {
    internal sealed partial class PlanetIndustry {
        readonly Dictionary<long,SurvivalCampView> offlineCampViews=new Dictionary<long,SurvivalCampView>();SurvivalCampMenu offlineCampMenu;bool campAttempted;long offlineLandingCamp;int offlineLandingPad=-1;
        void UpdateOfflineCamps(){
            if(!campAttempted){campAttempted=true;if(!Authority.World.camps.Exists(c=>c.pose.body==world.definition.bodyId)){
                bool Clear(Double3 at){var pose=Pose(at);foreach(var machine in Authority.World.machines)if(Authority.World.InteractionDistance(machine.pose,pose)<350)return false;if(world.ShipForValidation!=null&&(world.ShipForValidation.Pose.position-at).Magnitude<350)return false;return !new PlayerBuildAuthority(Authority,body=>body==world.definition.bodyId?surface:null).Overlaps(world.definition.bodyId,at,SurvivalCampGeneration.Rotation(pose),SurvivalCampGeneration.ExclusionVolume,400);}
                var camp=SurvivalCampGeneration.Generate(surface,actor.pose,Authority.World.nextId,world.terrainSeed,Clear);if(camp!=null)Authority.InitializeSurvivalCamp(camp);
            }}
            foreach(var camp in Authority.World.camps)if(camp.pose.body==world.definition.bodyId){double distance=actor.pose.Distance(camp.pose);if(distance<1000&&!offlineCampViews.ContainsKey(camp.id))offlineCampViews.Add(camp.id,new SurvivalCampView(world,SurvivalCampPresentation.Public(camp)));if(offlineCampViews.TryGetValue(camp.id,out var view)){if(distance>1600){view.Dispose();offlineCampViews.Remove(camp.id);}else view.Present();}}
            var ship=world.ShipForValidation;if(ship!=null&&offlineLandingCamp!=0&&!ship.AutoFlightActive&&!ship.Landed){offlineLandingCamp=0;offlineLandingPad=-1;}
            if(MenuOpen&&uiMode=="camps")offlineCampMenu?.Refresh();
        }
        SurvivalCampMenuState ReadOfflineCampMenu(){var ship=world.ShipForValidation;var visible=new Dictionary<long,SurvivalCampPublic>();var beacons=new List<SurvivalCampBeacon>();var shipPose=ship==null?actor.pose:Pose(ship.Pose.position);
            foreach(var camp in Authority.World.camps)if(camp.pose.body==world.definition.bodyId){visible.Add(camp.id,SurvivalCampPresentation.Public(camp));var beacon=new SurvivalCampBeacon{id=camp.id,name=camp.name,pose=camp.pose,pads=camp.pads};foreach(var pad in camp.pads)beacon.occupied.Add(ship!=null&&(shipPose.Distance(pad.pose)<85||offlineLandingCamp==camp.id&&offlineLandingPad==pad.index)?1:0);beacons.Add(beacon);}
            return new SurvivalCampMenuState{beacons=beacons,visible=visible,player=actor.pose,inventory=actor.inventory,ship=ship==null?0:1,shipPose=shipPose,camp=offlineLandingCamp,pilot=world.IsPiloting,landed=ship?.Landed==true,automatic=ship?.AutoFlightActive==true,onFoot=world.IsFirstPerson&&!world.PlayerForValidation.AttachedToShip,ready=!world.LocalPlayerDead,status=message};
        }
        void BuildCampMenu(){offlineCampMenu=new SurvivalCampMenu(menu,ReadOfflineCampMenu,(id,pad)=>{var camp=Authority.World.Camp(id);if(!world.IsPiloting||camp==null||pad<0||pad>=camp.pads.Count){message="Pilot your ship before choosing a pad";return;}var target=camp.pads[pad].pose;string error=world.ShipForValidation.BeginPadLanding(SurvivalCampGeneration.Point(target),SurvivalCampGeneration.Rotation(target));message=error??"Automatic pad landing engaged";if(error==null){offlineLandingCamp=id;offlineLandingPad=pad;}},()=>{message=world.IsPiloting?(world.ShipForValidation.BeginPadLaunch()??"Launching and clearing the pad"):"Pilot your own landed ship first";},()=>{if(world.IsPiloting&&offlineLandingCamp!=0){world.ShipForValidation.CancelRecall("PAD APPROACH CANCELLED");offlineLandingCamp=0;message="Pad approach cancelled";}},(id,shop,offer,count)=>Execute(new IndustrialCommand{sequence=actor.lastSequence+1,kind="camp-barter",target=id,source=shop,item=offer,count=count}));offlineCampMenu.Refresh(true);}
        void DisposeOfflineCamps(){foreach(var view in offlineCampViews.Values)view.Dispose();offlineCampViews.Clear();offlineCampMenu=null;}
    }
}


