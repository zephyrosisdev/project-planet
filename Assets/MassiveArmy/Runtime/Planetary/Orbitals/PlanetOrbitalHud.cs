using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace MassiveArmy.Planetary {
    public sealed partial class PlanetRtsHud {
        bool orbitalsView;long orbitalSelection,orbitalDestination,orbitalPad;string orbitalResource="propellant";double orbitalAmount=40;
        void OpenOrbitals(){crimeView=false;residentsView=false;resourcesView=false;orbitalsView=true;if(civilisationOpen)civilisationOpen=false;ToggleCivilisation();}
        void LaunchControls(VisualElement parent,RtsBuilding pad){if(world.Civilisation==null||pad.definition.id!="civil_launch_pad")return;
            Label(parent,"ORBITAL PAYLOADS","subheading");Label(parent,"Road cargo must deliver orbital kits and launch fuel here. Preparation pauses without food, power or workers. Watch the 20-second powered ascent curve into a circular orbit. Use FOLLOW LAUNCH in the orbital manifest; move the camera to return to the surface.","muted");
            foreach(OrbitalPayloadKind kind in Enum.GetValues(typeof(OrbitalPayloadKind))){var selected=kind;Button(parent,$"LAUNCH {kind} · {OrbitalEconomy.Kits(kind):0} kits / {OrbitalEconomy.Fuel(kind):0} fuel",()=>{world.Civilisation.Orbitals.Launch(world,pad,selected,0,out string reason);world.Notice=reason;revision=-1;},"orbital-launch-"+kind);}
            Button(parent,"CANCEL PREPARATION",()=>{world.Civilisation.Orbitals.Cancel(world,pad.persistentId,0,out string reason);world.Notice=reason;revision=-1;},"orbital-cancel");
            Button(parent,"ORBITAL ASSETS & CARGO",OpenOrbitals,"orbital-pad-manifest");
        }
        void RefreshOrbitals(CivilisationRuntime sim,float scroll){
            var orbital=sim.Orbitals;bool observer=fixture.sessionMode==PlanetGameMode.AiTest;string body=sim.BodyId(fixture.definition.bodyId);var choices=new List<OrbitalAssetState>();
            Label(civilisationContent,"ORBITAL INFRASTRUCTURE","heading");Label(civilisationContent,"Launch pads use the settlement road network. Survey satellites reveal local geology. Research stations generate research and unlock Orbital Science. Warehouses hold separate finite inventories; flights reserve cargo and destination space. Distant bodies keep timestamps until visited.","muted");
            foreach(var job in sim.State.orbitalLaunches)if(job.faction==inspectedFaction)Label(civilisationContent,$"PAD #{job.pad} · {job.kind} · {job.progress:0}/{job.seconds:0}s preparation · {sim.BodyName(job.body)}","body");
            if(!observer)foreach(var order in sim.State.orbitalLaunches)if(order.faction==0&&order.body==body){long padId=order.pad;Button(civilisationContent,"CANCEL PAD #"+padId+" PREPARATION",()=>{orbital.Cancel(world,padId,0,out string reason);world.Notice=reason;nextCivilisationRefresh=0;});}
            foreach(var planet in sim.State.planets)foreach(var a in orbital.Assets(planet.body)){if(a.faction!=inspectedFaction)continue;choices.Add(a);bool local=a.body==body;Label(civilisationContent,$"{a.Name} #{a.id} · {(a.Ready(sim.State.time)?"IN ORBIT":"ASCENDING")} · {sim.BodyName(a.body)}"+(local?"":" · DORMANT SNAPSHOT"),"subheading").name="orbital-asset-"+a.id;
                Label(civilisationContent,$"Orbit radius {a.radius/1000:0.0} km · period {2*Math.PI*Math.Sqrt(a.radius*a.radius*a.radius/a.mu)/60:0.0} min · cargo {orbital.Mass(a):0.0}/{a.Capacity:0} mass · pending research {a.pendingResearch:0.0}","body");
                foreach(var stock in a.inventory.stock)if(stock.amount>.0001)Label(civilisationContent,stock.resource+": "+stock.amount.ToString("0.0"),"muted");
                long id=a.id;Button(civilisationContent,"SELECT #"+id,()=>{orbitalSelection=id;nextCivilisationRefresh=0;},"orbital-select-"+id);
                if(local)Button(civilisationContent,a.Ready(sim.RenderTime)?"LOCATE IN SKY":"FOLLOW LAUNCH",()=>fixture.Rig.FollowOrbital(a,sim),"orbital-locate-"+a.id);
            }
            if(choices.Count==0)Label(civilisationContent,"No payloads yet. Research Rocket Engines, construct a Launch Pad, then research Basic Satellites. Orbital warehouses and docking stations require Orbital Construction.","body");
            foreach(var shipment in sim.State.orbitalShipments)if(shipment.faction==inspectedFaction)Label(civilisationContent,$"CARGO #{shipment.id}: {shipment.amount:0.0} {shipment.resource} · {(shipment.arrival>sim.State.time?$"{shipment.arrival-sim.State.time:0}s to arrival":"ARRIVED / WAITING FOR DESTINATION")} · {sim.BodyName(shipment.body)}","muted");
            if(!observer&&choices.Count>0){
                var selected=orbital.Find(orbitalSelection);if(selected==null||selected.faction!=0){selected=choices[0];orbitalSelection=selected.id;}Label(civilisationContent,"CARGO DISPATCH · #"+selected.id,"heading");
                var resources=new List<string>();foreach(var r in sim.Catalogue.resources)if(r.mass>0&&r.id!="research")resources.Add(r.id);if(!resources.Contains(orbitalResource))orbitalResource=resources[0];var resource=new DropdownField("Resource",resources,resources.IndexOf(orbitalResource));resource.RegisterValueChangedCallback(e=>orbitalResource=e.newValue);civilisationContent.Add(resource);
                var amount=new DoubleField("Amount per flight"){value=orbitalAmount};amount.RegisterValueChangedCallback(e=>orbitalAmount=e.newValue);civilisationContent.Add(amount);
                var pads=new List<RtsBuilding>();var names=new List<string>();foreach(var p in world.Buildings)if(p.team==0&&!p.removed&&p.Complete&&p.definition.id=="civil_launch_pad"){pads.Add(p);names.Add("Pad #"+p.persistentId);}int index=pads.FindIndex(p=>p.persistentId==orbitalPad);if(index<0)index=0;
                if(pads.Count>0){orbitalPad=pads[index].persistentId;var field=new DropdownField("Surface shuttle pad",names,index);field.RegisterValueChangedCallback(e=>orbitalPad=pads[names.IndexOf(e.newValue)].persistentId);civilisationContent.Add(field);
                    Button(civilisationContent,"LOAD TO ORBIT · 80 MASS / 10 FUEL",()=>{orbital.Shuttle(world,world.FindBuilding(orbitalPad),orbitalSelection,orbitalResource,orbitalAmount,false,0,out string reason);world.Notice=reason;nextCivilisationRefresh=0;},"orbital-load");
                    Button(civilisationContent,"UNLOAD TO PAD · 80 MASS / 10 FUEL",()=>{orbital.Shuttle(world,world.FindBuilding(orbitalPad),orbitalSelection,orbitalResource,orbitalAmount,true,0,out string reason);world.Notice=reason;nextCivilisationRefresh=0;},"orbital-unload");}
                var destinations=choices.FindAll(a=>a.Capacity>0&&a.id!=orbitalSelection&&a.body!=selected.body&&sim.State.Faction(0).knowledge.Knows(a.body));if(destinations.Count>0){var labels=new List<string>();foreach(var a in destinations)labels.Add(a.Name+" #"+a.id+" · "+sim.BodyName(a.body));int at=destinations.FindIndex(a=>a.id==orbitalDestination);if(at<0)at=0;orbitalDestination=destinations[at].id;var field=new DropdownField("Destination warehouse",labels,at);field.RegisterValueChangedCallback(e=>orbitalDestination=destinations[labels.IndexOf(e.newValue)].id);civilisationContent.Add(field);Button(civilisationContent,"DISPATCH INTERPLANETARY FREIGHT · 20 ONBOARD FUEL",()=>{orbital.Transfer(orbitalSelection,orbitalDestination,orbitalResource,orbitalAmount,0,out string reason);world.Notice=reason;nextCivilisationRefresh=0;},"orbital-transfer");}
            }
            ((ScrollView)civilisationContent).scrollOffset=new Vector2(0,scroll);
        }
    }
}
