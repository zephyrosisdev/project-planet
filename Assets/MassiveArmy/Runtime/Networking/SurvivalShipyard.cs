using System;
using System.Collections.Generic;
using MassiveArmy.Industrial;
using MassiveArmy.Planetary;
using UnityEngine;

namespace MassiveArmy.Networking {
    public sealed partial class SurvivalNetworkSession {
        static bool ShipConstruction(string kind)=>kind=="ship-place"||kind=="ship-remove"||kind=="ship-wire"||kind=="ship-unwire";
        static Double3 ShipPoint(SurvivalShipState ship,FramePose local)=>SurvivalVehicleSimulation.Position(ship.pose)+Double3.Rotate(ShipRotation(ship),SurvivalVehicleSimulation.Position(local));
        static Vector3 ShipLocal(SurvivalShipState ship,FramePose global)=>Double3.Rotate(Quaternion.Inverse(ShipRotation(ship)),SurvivalVehicleSimulation.Position(global)-SurvivalVehicleSimulation.Position(ship.pose)).Float;
        static bool ModuleContains(ShipModuleRecord module,Vector3 point,float radius=0){var spec=ShipModules.Definitions[module.definition];var local=Quaternion.Inverse(Quaternion.Euler(0,module.pose.yaw,0))*(point-SurvivalVehicleSimulation.Position(module.pose).Float);return Math.Abs(local.x)<=spec.width*.5+radius&&Math.Abs(local.y)<=spec.height*.5+radius&&Math.Abs(local.z)<=spec.depth*.5+radius;}
        string ValidateShipConstruction(IndustrialActor actor,IndustrialCommand command){
            if(!ShipConstruction(command.kind))return null;
            string policy=authority.World.ConstructionPolicyError(actor.id);if(policy!=null)return policy;
            var state=authority.World.multiplayer.ships.Find(ship=>ship.owner==actor.id);var assembly=authority.World.Ship(actor.id);
            if(state==null||assembly==null)return "Owned ship unavailable";
            if(!state.landed||state.autoFlight||state.pilot!=0)return "Land and release the pilot seat before ship construction";
            if(actor.pose.body!=state.pose.body||actor.pose.frame!=0||!surfaces.ContainsKey(state.pose.body))return "Ship region unavailable";
            var source=assembly.Module(command.source);if(source==null)return "Ship section unavailable";
            FramePose socket=ShipModules.Socket(source,command.kind=="ship-place"?command.item:"power")?.pose??default;
            var point=ShipPoint(state,ShipModules.Transform(source.pose,socket));var feet=SurvivalVehicleSimulation.Position(actor.pose);
            var target=assembly.Module(command.target);
            bool near=(point-feet).Magnitude<=24;
            if(!near&&(command.kind=="ship-wire"||command.kind=="ship-unwire")&&target!=null)near=(ShipPoint(state,ShipModules.Transform(target.pose,ShipModules.Socket(target,"power")?.pose??default))-feet).Magnitude<=24;
            if(!near)return "Move within 24 m of the ship socket or connection";
            if(command.kind=="ship-wire"&&command.route!=null){
                if(command.route.Count>8)return "At most eight route points";
                foreach(var route in command.route)if(route.body!=state.pose.body||route.frame!=state.entity||!SurvivalServerConfig.Finite(route.x)||!SurvivalServerConfig.Finite(route.y)||!SurvivalServerConfig.Finite(route.z)||!SurvivalServerConfig.Finite(route.yaw))return "Use canonical routing points in this ship frame";
                var normalized=new List<FramePose>();foreach(var route in command.route)normalized.Add(new FramePose{x=route.x,y=route.y,z=route.z,yaw=route.yaw});command.route=normalized;
            }
            if(command.kind=="ship-remove"){
                foreach(var occupant in authority.World.actors)if(!occupant.networkAbsent&&occupant.health>0&&occupant.pose.body==state.pose.body&&ModuleContains(source,ShipLocal(state,occupant.pose),.4f))return "Leave the section before dismantling it";
                foreach(var vehicle in authority.World.multiplayer.vehicles)if(vehicle.pose.body==state.pose.body&&ModuleContains(source,ShipLocal(state,vehicle.pose),3))return "Unload vehicles before removing this section";
                foreach(var pointRecord in authority.World.respawnPoints)if(pointRecord.pose.frame==state.entity&&ModuleContains(source,SurvivalVehicleSimulation.Position(pointRecord.pose).Float,.4f))return "Recover respawn points from this section first";
                foreach(var machine in authority.World.machines)if(machine.pose.frame==state.entity&&ModuleContains(source,SurvivalVehicleSimulation.Position(machine.pose).Float,(float)machine.Definition.Radius))return "Recover machinery from this section first";
                foreach(var loot in authority.World.droppedItems)if(loot.pose.frame==state.entity&&ModuleContains(source,SurvivalVehicleSimulation.Position(loot.pose).Float,.5f))return "Recover loose inventory from this section first";
            }
            if(command.kind=="ship-place"){
                if(!ShipModules.Definitions.TryGetValue(command.definition??"",out var spec)||ShipModules.Socket(source,command.item)==null||Array.Find(spec.sockets,input=>input.name==command.operationRole)==null)return "Choose compatible structural sockets";
                var placement=ShipModules.Attach(source,command.item,command.definition,command.operationRole);var centre=ShipPoint(state,placement);var rotation=ShipRotation(state)*Quaternion.Euler(0,placement.yaw,0);var surface=surfaces[state.pose.body];
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){var corner=centre+Double3.Rotate(rotation,new Double3(x*spec.width*.5,y*spec.height*.5,z*spec.depth*.5));if(corner.Magnitude<surface.Definition.radius+surface.SampleHeight(corner)+.05)return "Ship section intersects terrain";}
                double radius=Math.Sqrt(spec.width*spec.width+spec.height*spec.height+spec.depth*spec.depth)*.5;
                if(BuildingAuthority.Overlaps(state.pose.body,centre,rotation,new[]{BuildSolid.Box((float)-spec.width*.5f,(float)spec.width*.5f,(float)-spec.depth*.5f,(float)spec.depth*.5f,(float)-spec.height*.5f,(float)spec.height*.5f)},radius))return "Ship section intersects a building";
                foreach(var other in authority.World.multiplayer.ships)if(other.entity!=state.entity&&other.pose.body==state.pose.body&&(centre-SurvivalVehicleSimulation.Position(other.pose)).Magnitude<radius+other.collisionRadius)return "Ship section overlaps another ship";
                foreach(var machine in authority.World.machines)if(machine.pose.body==state.pose.body&&machine.pose.frame==0&&(centre-SurvivalVehicleSimulation.Position(machine.pose)).Magnitude<radius+machine.Definition.Radius)return "Keep ship construction clear of machinery";
                foreach(var person in authority.World.actors)if(!person.networkAbsent&&person.health>0&&person.pose.body==state.pose.body){var box=new Bounds(Vector3.zero,new Vector3((float)spec.width,(float)spec.height,(float)spec.depth));var up=PlanetPrecisionPolicy.Direction(SurvivalVehicleSimulation.Position(person.pose));for(int i=0;i<3;i++){var local=Double3.Rotate(Quaternion.Inverse(rotation),SurvivalVehicleSimulation.Position(person.pose)+new Double3(up)*(i*.9)-centre).Float;if(box.SqrDistance(local)<.16)return "Keep ship construction clear of players";}}
            }
            return null;
        }
        void RefreshShipConstruction(long owner){var state=authority.World.multiplayer.ships.Find(ship=>ship.owner==owner);var assembly=authority.World.Ship(owner);if(state==null||assembly==null)return;assembly.UpdatePower();state.collisionRadius=SurvivalShipAuthority.CollisionRadius(assembly);if(shipSimulations.TryGetValue(state.entity,out var simulation))simulation.ship.SetAssembly(assembly);}
        long yardModule,yardPowerSource;string yardDefinition="corridor";readonly List<FramePose> yardRoute=new List<FramePose>();
        void ShipyardGUI(){
            if(latest.ownedShip==0||!clientShipAssemblies.TryGetValue(latest.ownedShip,out var assembly)||!clientShips.TryGetValue(latest.ownedShip,out var state))return;
            GUILayout.Space(12);GUILayout.Label("OWNED SHIP · ASSEMBLY & POWER");
            bool before=GUI.enabled;GUI.enabled=before&&state.landed&&!state.autoFlight&&state.pilot==0;
            foreach(var module in assembly.modules){GUILayout.BeginHorizontal();GUILayout.Label("#"+module.id+" "+module.definition);if(GUILayout.Button("SELECT",GUILayout.Width(70)))yardModule=module.id;if(module.parent!=0&&GUILayout.Button("RECOVER",GUILayout.Width(80)))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="ship-remove",source=module.id}});GUILayout.EndHorizontal();}
            var selected=assembly.Module(yardModule);
            if(selected!=null){
                GUILayout.Label("Selected #"+selected.id+" · choose a section, then a structural socket");
                foreach(var definition in ShipModules.Definitions.Values)if(definition.id!="hangar"&&GUILayout.Button((yardDefinition==definition.id?"✓ ":"")+definition.id))yardDefinition=definition.id;
                var specification=ShipModules.Definitions[yardDefinition];string cost="Cost: ";foreach(var item in specification.cost)cost+=item.count+" "+item.item+"  ";GUILayout.Label(cost);
                foreach(var output in ShipModules.Definitions[selected.definition].sockets){if(assembly.Occupied(selected.id,output.name)||selected.parent!=0&&selected.inputSocket==output.name)continue;var input=Array.Find(specification.sockets,pin=>pin.kind==output.kind&&!pin.kind.StartsWith("Power",StringComparison.Ordinal));if(input!=null&&GUILayout.Button("BUILD "+yardDefinition+" AT "+output.name))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="ship-place",source=selected.id,item=output.name,definition=yardDefinition,operationRole=input.name}});}
                if(ShipModules.Definitions[selected.definition].generation>0&&GUILayout.Button("SELECT THIS POWER CORE")){yardPowerSource=selected.id;yardRoute.Clear();}
                if(yardPowerSource!=0){GUILayout.Label("Power core #"+yardPowerSource+" · "+yardRoute.Count+" routing points");if(yardRoute.Count<8&&GUILayout.Button("ADD ROUTING POINT AT MY POSITION")){var p=ShipLocal(state,latest.self.pose);yardRoute.Add(new FramePose{body=state.pose.body,frame=state.entity,x=p.x,y=p.y+1,z=p.z});}if(yardRoute.Count>0&&GUILayout.Button("UNDO ROUTING POINT"))yardRoute.RemoveAt(yardRoute.Count-1);if(ShipModules.Definitions[selected.definition].power>0&&GUILayout.Button("CONNECT CORE TO SELECTED SYSTEM")){Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="ship-wire",source=yardPowerSource,target=selected.id,route=new List<FramePose>(yardRoute)}});yardRoute.Clear();}}
                foreach(var cable in assembly.wires)if(cable.target==selected.id&&GUILayout.Button("RECOVER POWER CABLE FROM #"+cable.source))Submit(new SurvivalAction{kind="industrial",command=new IndustrialCommand{kind="ship-unwire",source=cable.source,target=cable.target}});
            }
            GUI.enabled=before;
        }
    }
}
