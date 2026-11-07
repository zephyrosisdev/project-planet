using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
namespace MassiveArmy.Planetary {
    [DefaultExecutionOrder(-230)]public sealed partial class PlanetColonyValidation:MonoBehaviour {
        [Serializable]sealed class Report {
            public string status="PASS",error="";public List<string> checks=new List<string>();
            public int runtimeErrors,prebuilt,colonies;public long backgroundTicks,allocatedBytes;
            public double maximumBackgroundSliceMs;
        }
        readonly Report report=new Report();PlanetDevelopment home;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-colony-check")>=0)new GameObject("Colony delivery and dormant economy checks").AddComponent<PlanetColonyValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);home=FindAnyObjectByType<PlanetDevelopment>();home.currentRtsContent=true;home.Paused=true;home.ambientZombies=0;home.requestedCapacity=8192;home.countryCount=2;Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.runtimeErrors++;report.error+=message+"; ";}}
        void Check(bool ok,string name){report.checks.Add((ok?"PASS ":"FAIL ")+name);if(!ok){report.status="FAIL";report.error+=name+"; ";}}
        IEnumerator Start(){
            double deadline=Time.realtimeSinceStartupAsDouble+180;while(!home.Ready&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(!home.Ready){Check(false,"Home world ready");Finish();yield break;}
            var sim=home.Base.Civilisation;var system=home.SystemRuntime;Check(sim!=null,"Strategic settlement simulation enabled");if(sim==null){Finish();yield break;}
            foreach(var f in sim.State.factions)f.autonomous=false;
            var wallet=home.Base.Faction.wallet.Stock;int before=home.Base.Buildings.Count;
            for(int i=0;i<24;i++)home.Base.PrebuildStep(i);
            report.prebuilt=home.Base.Buildings.Count-before;Check(report.prebuilt>0,"Prebuild expands through the normal placement system");
            Check(home.Base.Buildings.Skip(before).All(b=>b.Complete),"Prebuilt structures are immediately complete");
            bool unchanged=true;for(int i=0;i<5;i++)unchanged&=wallet[i]==home.Base.Faction.wallet.Stock[i];Check(unchanged,"Prebuild consumes no settlement wallet resources");
            system.PreparePlanet(1);while(!system.IsPreparedForValidation(1)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Check(system.IsPreparedForValidation(1),"A second planet prepares");if(!system.IsPreparedForValidation(1)){Finish();yield break;}
            var target=system.WorldForColonyValidation(1);var local=target.Base;
            Check(local.Settlements[0]==null&&local.Buildings.Count==0,"Foreign world begins uncolonised");
            Check(!sim.CanDeliverColony(local,0,out _),"Colonisation requires a homeworld launch pad");
            foreach(var tech in home.Base.Catalog.technologies)home.Base.Faction.technologies.Add(tech.id);
            home.Base.Faction.tier=5;home.Base.Faction.wallet.Stock=new ResourceCost{materials=10000,fuel=10000,rareElements=10000,isotopes=10000,research=10000};
            Check(home.Base.TryConstructInfrastructure("civil_launch_pad",0),"Homeworld can place a launch pad");
            var pad=home.Base.Buildings.LastOrDefault(b=>b.definition.id=="civil_launch_pad");if(pad==null){Finish();yield break;}
            pad.construction=pad.definition.time;var warehouse=home.Base.Buildings.First(b=>b.team==0&&b.definition.id=="civil_warehouse");
            home.Base.Roads.Connect(pad,warehouse,out _,true);sim.Advance(1);pad.Efficiency=1;
            home.Base.Settlements[0].inventory.Deposit("food",1000);
            home.Base.Faction.wallet.Stock=new ResourceCost{materials=3000,fuel=1800,rareElements=900,isotopes=200,research=2000};
            Check(sim.CanDeliverColony(local,0,out string availability),"Connected homeworld launch pad can deliver: "+availability);
            Vector3 landing=Vector3.zero;
            foreach(var p in PlanetStartingRegion.Candidates(target.definition,target.Base.Homes[0]))if(local.PlanColonyKit(p,0,0,out _,out _)){landing=p;break;}
            Check(landing.sqrMagnitude>.9f,"Find a valid dry colony kit landing site");
            float materials=home.Base.Faction.wallet.Stock.materials;
            local.BeginPlacement(local.Catalog.Buildings["command"]);Check(local.Construct(landing),"Normal construction UI path dispatches colony delivery");
            Check(home.Base.Faction.wallet.Stock.materials<materials&&local.Faction.wallet.Stock.materials==0,"Only the homeworld pays before arrival");
            Check(local.Settlements[0]==null,"No operational colony before delivery");
            Check(!sim.CanDeliverColony(local,0,out _),"Duplicate colony delivery rejected");
            uint ticks=home.SimulationTicks;
            for(int i=0;i<24;i++)sim.Advance(1);
            Check(local.Settlements[0]!=null,"Delivery starts colony without visiting it");if(local.Settlements[0]!=null){Check(local.AutoPilotEnabled,"New colonies auto-build by default");local.SetAutoPilot(false);}
            if(local.Settlements[0]==null){Finish();yield break;}
            Check(PlanetRtsBase.ColonyKit.All(id=>local.Buildings.Any(b=>b.definition.id==id&&b.Complete))&&local.Buildings.Count(b=>b.definition.housingCapacity>0)==4,"Delivery includes completed command, power, warehouse and four houses");
            Check(local.Faction.wallet.Stock.materials>0&&local.Settlements[0].cargoVehicles.Count>0,"Starter supplies and cargo transport arrive");
            Check(home.SimulationTicks==ticks,"Strategic updates do not dispatch army simulation");
            var command=local.Buildings.First(b=>b.definition.id=="command");command.Efficiency=1;
            command.queue.Add(new QueueOrder{id="colony-test-research-a",duration=2,research=true});command.queue.Add(new QueueOrder{id="colony-test-research-b",duration=2,research=true});
            local.AdvanceDormantInfrastructure(5,true);Check(command.queue.Count==0&&local.Faction.technologies.Contains("colony-test-research-b"),"Dormant queue carries elapsed time across multiple research orders");
            command.queue.Add(new QueueOrder{id=home.Base.Catalog.units[0].id,duration=2});local.AdvanceDormantInfrastructure(5,true);
            Check(command.queue.Count==1&&command.queue[0].progress==2&&target.unitCount==0,"Completed dormant unit order waits for GPU spawn on return");command.queue.Clear();
            long backgroundBefore=sim.BackgroundTicks;var solar=local.Buildings.First(b=>b.definition.id=="solar");solar.construction=0;
            for(int i=0;i<24;i++)sim.Advance(1);
            Check(sim.BackgroundTicks>backgroundBefore&&solar.Complete,"Distant world advances and completes paid construction instantly");
            var mine=home.Base.Buildings.FirstOrDefault(b=>b.team==0&&b.node>=0);
            if(mine!=null){home.SetShipControl(true);system.EnterPlanet(1);target.Paused=true;
                var pose=target.ShipForValidation.Pose;pose.position=new Double3(landing)*(target.definition.radius+1000);pose.linearVelocity=new Double3();target.ShipForValidation.Arrive(pose,true,false);
                mine.Efficiency=mine.WorkforceEfficiency=mine.FoodEfficiency=1;mine.powered=true;mine.roadSuspended=false;
                var installation=home.Base.Settlements[0].installations.First(x=>x.id==mine.persistentId);installation.inventory.Deposit("food",1000);double remaining=home.Base.Nodes[mine.node].remaining;CivilisationRuntime.ResourceTotals(home.Base.Settlements[0],"construction_materials",out double storedStart,out double localStart,out double transitStart);
                for(int i=0;i<40;i++)sim.Advance(1);
                CivilisationRuntime.ResourceTotals(home.Base.Settlements[0],"construction_materials",out double storedEnd,out double localEnd,out double transitEnd);
                Check(storedEnd+localEnd+transitEnd>storedStart+localStart+transitStart&&home.Base.Nodes[mine.node].remaining==remaining,"Homeworld extraction continues without depleting its infinite deposit while player is on another planet");
                Check(home.SimulationTicks==ticks,"Homeworld army stays frozen during background production");
                home.Base.Faction.wallet.Stock.materials=3000;
                var colony=home.Base.Settlements[0];foreach(var site in colony.installations)if(site.definition=="civil_warehouse")site.inventory.Deposit("construction_materials",1000);
                CivilisationRuntime.ResourceTotals(colony,"construction_materials",out double storedBefore,out double siteBefore,out _);
                bool built=false;for(int attempt=0;attempt<24&&!built;attempt++)built=home.Base.TryConstructInfrastructure("wind",0);
                CivilisationRuntime.ResourceTotals(colony,"construction_materials",out double storedAfter,out double siteAfter,out _);
                Check(built&&home.Base.Buildings.Last().Complete&&storedAfter+siteAfter<storedBefore+siteBefore,"New remote construction is instant and still paid (placed="+built+", spent="+(storedBefore+siteBefore-storedAfter-siteAfter)+")");
            }else Check(false,"Prebuild supplies a mine for the background production check");
            Check(sim.State.colonyDeliveries.Single().delivered,"Delivery is recorded exactly once");
            var saved=CivilisationRuntime.Deserialize(sim.Serialize(),sim.State.galaxySeed,sim.Catalogue);Check(saved.colonyDeliveries.Count==1&&saved.colonies.Count==sim.State.colonies.Count,"Strategic save preserves delivery and colony data");
            var restore=ValidateRestoredRts();while(true){object next;try{if(!restore.MoveNext())break;next=restore.Current;}catch(Exception e){Check(false,"RTS restoration checks: "+e);break;}yield return next;}report.backgroundTicks=sim.BackgroundTicks;report.maximumBackgroundSliceMs=sim.MaximumBackgroundSliceMs;report.colonies=sim.State.colonies.Count;Check(report.runtimeErrors==0,"No runtime errors");Finish();
        }
        void Finish(){if(report.runtimeErrors>0)report.status="FAIL";report.allocatedBytes=Profiler.GetTotalAllocatedMemoryLong();File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);}
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
    [DefaultExecutionOrder(-230)]public sealed class PlanetPrebuildValidation:MonoBehaviour {
        [Serializable]sealed class Report {public string status;public bool progressSeen,ready,resourcesUnchanged,armyFrozen;public int buildings,runtimeErrors;public double elapsedSeconds;}
        readonly Report report=new Report();PlanetDevelopment world;string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){if(!Application.isEditor&&Array.IndexOf(Environment.GetCommandLineArgs(),"-planet-prebuild-check")>=0)new GameObject("Starting settlement prebuild check").AddComponent<PlanetPrebuildValidation>();}
        void Awake(){var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-planet-output")+1];Directory.CreateDirectory(output);world=FindAnyObjectByType<PlanetDevelopment>();world.countryCount=2;world.currentRtsContent=true;world.ambientZombies=0;world.requestedCapacity=8192;world.Paused=true;Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.runtimeErrors++;}
        IEnumerator Start(){
            double started=Time.realtimeSinceStartupAsDouble,deadline=started+120;ResourceCost[] stocks=null;uint ticks=0;
            while(!world.Ready&&Time.realtimeSinceStartupAsDouble<deadline){
                if(world.Prebuilding&&!report.progressSeen){report.progressSeen=true;ticks=world.SimulationTicks;stocks=world.Base.Factions.Select(f=>f.wallet.Stock).ToArray();ScreenCapture.CaptureScreenshot(Path.Combine(output,"prebuild-progress.png"));}
                yield return null;
            }
            report.ready=world.Ready;report.elapsedSeconds=Time.realtimeSinceStartupAsDouble-started;report.buildings=world.Base?.Buildings.Count??0;report.resourcesUnchanged=stocks!=null;report.armyFrozen=world.SimulationTicks==ticks;
            if(stocks!=null)for(int team=0;team<stocks.Length;team++)for(int resource=0;resource<5;resource++)report.resourcesUnchanged&=stocks[team][resource]==world.Base.Factions[team].wallet.Stock[resource];
            report.status=report.ready&&report.progressSeen&&report.resourcesUnchanged&&report.armyFrozen&&report.buildings>22&&report.runtimeErrors==0?"PASS":"FAIL";
            File.WriteAllText(Path.Combine(output,"validation.json"),JsonUtility.ToJson(report,true));Application.Quit(report.status=="PASS"?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
