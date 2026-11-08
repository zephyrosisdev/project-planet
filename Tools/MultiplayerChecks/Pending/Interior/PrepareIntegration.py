from pathlib import Path
root=Path('Assets/MassiveArmy/Runtime/Networking');pending=Path('Tools/MultiplayerChecks/Pending/Interior')
def change(s,old,new):
    assert old in s,old[:100]
    return s.replace(old,new,1)
def write(name,s): (pending/name).write_text(s,encoding='utf-8')
s=(root/'SurvivalNetworkShips.cs').read_text(encoding='utf-8-sig')
a=s.index('        string BoardPassenger(');b=s.index('        bool RestorePassengerBerth',a)
s=s[:a]+'''        string BoardPassenger(Peer peer,long entity){
            var ship=authority.World.multiplayer.ships.Find(s=>s.entity==entity);var actor=authority.World.Actor(peer.record.actor);
            if(ship==null||actor.health<=0)return "Ship unavailable";
            if(peer.record.attachedShip!=0||peer.record.controlledEntity!=0||peer.record.controlledShip!=0)return "Leave the current seat first";
            if(ship.owner!=actor.id&&!ship.invitedActors.Contains(actor.id))return "The ship owner must invite you aboard";
            if(!ship.landed||ship.autoFlight||actor.pose.Distance(ShipEntry(ship))>12)return "Move within 12 m of the landed ship's boarding ramp";
            if(!ChooseInteriorFeet(ship,actor.id,ShipModules.P(0,-4,-35),out var feet))return "The boarding airlock is occupied";
            if(ship.owner!=actor.id){string error=SurvivalShipAuthority.BoardPassenger(authority.World,peer.record,entity,ShipEntry(ship),connectedActors);if(error!=null)return error;}
            else peer.record.shipSeat=-1;
            BeginInterior(peer,ship,feet);return null;
        }
'''+s[b:]
s=change(s,'motion.ship=state;motion.vehicle=null;motion.pose=state.pose;','if(motion.interior&&!pilot){motion.ship=state;motion.vehicle=null;motion.pose.body=state.pose.body;motion.pose.frame=state.entity;motion.sequence=input.sequence;return;}\n            motion.interior=false;motion.ship=state;motion.vehicle=null;motion.pose=state.pose;')
s=change(s,'authority.World.Actor(player.actor).pose=ActorPose(new SurvivalMotion{actor=player.actor,pose=ship.pose,ship=ship});','authority.World.Actor(player.actor).pose=AttachedActorPose(player,ship);')
s=change(s,'peer.motion.ship=ship;peer.motion.pose=ship.pose;','peer.motion.ship=ship;if(peer.motion.interior){peer.motion.pose.body=ship.pose.body;peer.motion.pose.frame=ship.entity;}else peer.motion.pose=ship.pose;')
s=change(s,'if(pilot)SimulateShip(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition,input,dt);','if(!pilot&&peer.motion.interior){StepInterior(peer,ship,input,dt);return;}\n            if(pilot)SimulateShip(ship,authority.World.Ship(ship.owner),surfaces[ship.pose.body].Definition,input,dt);')
s=change(s,'string error=SurvivalShipAuthority.Enter(authority.World,peer.record,entity,ShipEntry(ship));','if(ship.owner!=peer.record.actor)return "Only the ship owner can pilot";\n            if(!peer.motion.interior||peer.record.attachedShip!=entity||peer.motion.pose.Distance(new FramePose{body=ship.pose.body,frame=ship.entity,x=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).x,y=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).y,z=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),-1).z})>3.5)return "Approach the pilot seat inside your ship";\n            string error=SurvivalShipAuthority.Enter(authority.World,peer.record,entity,ShipEntry(ship));')
s=change(s,'FollowShip(peer.motion,ship,default,true);','peer.motion.interior=peer.record.interiorWalking=false;FollowShip(peer.motion,ship,default,true);')
s=change(s,'SurvivalShipAuthority.Release(authority.World,peer.record);peer.motion.epoch++;peer.input.Clear();return null;','var ship=authority.World.multiplayer.ships.Find(s=>s.entity==peer.record.controlledShip);\n            if(ship==null||!ChooseInteriorFeet(ship,peer.record.actor,PilotExitLocal(authority.World.Ship(ship.owner)),out var feet))return "The pilot exit is blocked";\n            SurvivalShipAuthority.Release(authority.World,peer.record);BeginInterior(peer,ship,feet);return null;')
s=change(s,'peer.record.attachedShip=0;peer.record.shipSeat=-1;peer.motion.ship=null;','peer.record.attachedShip=0;peer.record.shipSeat=-1;peer.record.interiorWalking=peer.motion.interior=false;peer.motion.ship=null;')
s=change(s,'shipSimulations.Clear();clientShips.Clear();','shipSimulations.Clear();interiorModels.Clear();clientShips.Clear();')
write('SurvivalNetworkShips.cs',s)
s=(root/'SurvivalNetworkVehicles.cs').read_text(encoding='utf-8-sig')
s=change(s,'var pose=motion.pose;Double3 offset=default;','if(motion.interior&&motion.ship!=null)return SurvivalShipInteriorModel.World(motion.ship,motion.pose);\n            var pose=motion.pose;Double3 offset=default;')
s=change(s,'FollowShip(motion,state,input,pilot);}','if(motion.interior)PredictInterior(motion,input,dt);else FollowShip(motion,state,input,pilot);}')
write('SurvivalNetworkVehicles.cs',s)
s=(root/'SurvivalNetworkSession.cs').read_text(encoding='utf-8-sig')
s=change(s,'if(aboard!=null)FollowShip(peer.motion,aboard,default,false);else record.attachedShip=0;','if(aboard!=null){if(!RestoreInterior(peer,aboard)){manager.DisconnectClient(id,"No clear space aboard your ship; retry after the area is clear");return;}}else {record.attachedShip=0;record.interiorWalking=false;}')
s=change(s,'case "board-passenger":actionError=BoardPassenger(peer,action.entity);break;','case "board-passenger":actionError=BoardPassenger(peer,action.entity);break;\n                    case "ship-door":actionError=ToggleShipDoor(peer,action.entity,action.slot);break;')
s=change(s,'if(peer.record.attachedShip==0&&!body.BreathableAt((float)altitude))','if(!body.BreathableAt((float)altitude)&&!ShipPressurized(peer))')
s=s.replace('(snapshot.motion.ship!=null?Config.shipSnap:snapshot.motion.vehicle!=null?Config.vehicleSnap:Config.walkSnap)','CorrectionDistance(snapshot.motion)')
s=change(s,'if(!blocked&&keys!=null&&predicted.ship!=null)','if(!blocked&&keys!=null&&predicted.ship!=null&&!predicted.interior)')
write('SurvivalNetworkSession.cs',s)
s=(root/'SurvivalNetworkLoot.cs').read_text(encoding='utf-8-sig')
s=change(s,'var seat=SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),player.shipSeat);','var seat=player.interiorWalking?player.interiorPose:SurvivalShipAuthority.SeatFeet(authority.World.Ship(ship.owner),player.shipSeat);')
write('SurvivalNetworkLoot.cs',s)
s=(root/'SurvivalNetworkRespawn.cs').read_text(encoding='utf-8-sig')
s=change(s,'peer.motion.ship=null;peer.motion.vehicle=null;','peer.record.interiorWalking=peer.motion.interior=false;peer.motion.ship=null;peer.motion.vehicle=null;')
write('SurvivalNetworkRespawn.cs',s)
