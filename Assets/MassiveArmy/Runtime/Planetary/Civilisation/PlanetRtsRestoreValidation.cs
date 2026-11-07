using System;
using System.Collections;
using System.Linq;
using UnityEngine;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetColonyValidation {
        IEnumerator ValidateRestoredRts(){
            var system=home.SystemRuntime;var sim=home.Base.Civilisation;var physical=system.ActiveWorld;var ship=physical.ShipForValidation.Pose;
            Check(system.ViewSettlement(0),"Owned home settlement can be viewed remotely");yield return null;yield return null;
            Check(system.ActiveWorld==home&&system.IsRemoteView&&system.PhysicalWorld==physical,"Remote view preserves physical destination");
            home.SetFirstPerson(true);home.SetShipControl(true);Check(!home.IsFirstPerson&&!home.IsPiloting,"Remote planet restricts first person and ship control");
            Check(system.CycleSettlement(1),"Right bracket cycle finds the physical planet");yield return null;yield return null;
            Check(system.ActiveWorld==physical&&!system.IsRemoteView&&physical.ShipForValidation.Pose.position.Equals(ship.position),"Return restores physical ship without teleportation");
            var command=home.Base.Catalog.Buildings["command"];var enemy=home.Base.Buildings.First(b=>b.team==1&&b.definition.id=="command");
            var denied=home.Base.Validate(command,CubeSphere.Decode(enemy.position),0,true,0);Check(!denied.valid&&denied.reason.Contains("neutral land"),"Command centre cannot be placed in enemy claimed land");
            var local=physical.Base;physical.SetShipControl(false);physical.Paused=true;
            int oldCount=physical.unitCount;var backup=new UnitState[2];physical.UnitsForValidation.GetData(backup,0,0,2);
            var up=physical.Rig.FocusDirection;var a=local.Offset(new Vector2(100,200));var b=local.Offset(new Vector2(180,200));
            var test=new[]{new UnitState{position=CubeSphere.Encode(up),previous=CubeSphere.Encode(up),flags=3,health=1000,target=uint.MaxValue},new UnitState{position=CubeSphere.Encode(up),previous=CubeSphere.Encode(up),flags=3,health=1000,target=uint.MaxValue}};
            physical.UnitsForValidation.SetData(test,0,0,2);physical.SetArmyCount(2);
            Check(physical.Commands.Move(physical.NavigationForValidation,a,formationEnd:b),"Selected army accepts a dragged formation");
            physical.UnitsForValidation.GetData(test,0,0,2);Check(test.All(u=>(u.flags&16)!=0)&&(test[0].destination-test[1].destination).sqrMagnitude>1e-12,"GPU assigns distinct formation slots");
            var slot=test[0].destination;physical.EncounterForValidation.MoveUnits();physical.UnitsForValidation.GetData(test,0,0,2);Check(test[0].destination==slot,"Movement preserves individual formation destinations");
            Check(physical.Commands.Move(physical.NavigationForValidation,a),"Normal right-click move remains available");physical.UnitsForValidation.GetData(test,0,0,2);Check(test.All(u=>(u.flags&16)==0),"Normal movement clears formation slots");
            test[0].position=CubeSphere.Encode(up);test[1].position=CubeSphere.Encode(Vector3.Slerp(up,a,.01f));test[0].flags=test[1].flags=1;test[0].packed=0;test[1].packed=1;test[0].cooldown=test[1].cooldown=0;test[0].health=test[1].health=1000;
            physical.UnitsForValidation.SetData(test,0,0,2);physical.TickCivicCombatForValidation();Check(physical.EncounterForValidation.ShotCountForValidation>0,"Real army combat emits GPU gunshot records");
            physical.UnitsForValidation.SetData(backup,0,0,2);physical.SetArmyCount(oldCount);
            Check(system.ViewSettlement(0),"Home RTS camera can be inspected again");yield return null;yield return null;
            home.Rig.Distance=600;home.Rig.SetFocusDirection(home.Base.Offset(new Vector2(1300,900)));home.Rig.RefreshPose();yield return null;yield return null;
            CubeSphere.Project(home.Rig.FocusDirection,out var face,out var uv);float span=2;foreach(var patch in home.StreamedTerrain.ReadPatchesForValidation())if((int)patch.w==(int)face&&uv.x>=patch.x&&uv.x<=patch.x+patch.z&&uv.y>=patch.y&&uv.y<=patch.y+patch.z)span=Mathf.Min(span,patch.z);
            Check(home.definition.radius*span/PlanetStreamedTerrain.Grid<4,"RTS terrain refines at the camera's ground focus");Check(home.StreamedTerrain.BufferBytes==34848,"RTS terrain keeps a fixed patch/history memory budget");
            Check(system.ViewSettlement(Array.IndexOf(system.Bodies,physical.definition)),"Return to physical world remains available");yield return null;yield return null;
            var launch=home.Base.Buildings.First(b=>b.team==0&&b.definition.id=="civil_launch_pad");launch.powered=true;launch.paused=false;launch.roadSuspended=false;launch.Efficiency=1;home.Base.Faction.wallet.Stock.materials=3000;home.Base.Settlements[0].inventory.Deposit("food",500);
            Vector3 neutral=Vector3.zero;foreach(var candidate in PlanetStartingRegion.Candidates(home.definition,-home.Base.Homes[0]))if(home.Base.SettlementTerritoryOwner(candidate)<0&&home.Base.PlanColonyKit(candidate,0,0,out _,out _)){neutral=candidate;break;}
            Check(neutral.sqrMagnitude>.9f,"Homeworld has neutral ground for another settlement");if(neutral.sqrMagnitude<.9f)yield break;
            int commandCount=home.Base.Buildings.Count(b=>b.team==0&&b.definition.id=="command");Check(sim.RequestColony(home.Base,neutral,0,0,out _),"Same-planet command centre dispatches a paid launchpad kit");
            for(int i=0;i<20;i++)sim.Advance(1);Check(home.Base.Buildings.Count(b=>b.team==0&&b.definition.id=="command")==commandCount+1,"Same-planet delivery creates another settlement command");home.Base.SetAutoPilot(false);
            home.Base.SetAutoPilot(true);launch.Efficiency=1;home.Base.Faction.wallet.Stock.materials=3000;home.Base.Settlements[0].inventory.Deposit("food",500);int deliveries=sim.State.colonyDeliveries.Count;
            for(int i=0;i<4;i++){sim.State.time+=16;sim.PlanColonisation();}
            double until=Time.realtimeSinceStartupAsDouble+90;while(sim.State.colonyDeliveries.Count==deliveries&&Time.realtimeSinceStartupAsDouble<until){sim.State.time+=.5;sim.PlanColonisation();yield return null;}
            Check(sim.State.colonyDeliveries.Count>deliveries,"Automatic planner funds and launches a command centre to another planet");
            for(int i=0;i<20;i++)sim.Advance(1);Check(sim.State.colonyDeliveries.Last().delivered,"AI off-world colony kit reaches its destination");home.Base.SetAutoPilot(false);
        }
    }
}
