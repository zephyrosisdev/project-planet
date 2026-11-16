using System;
using System.Collections.Generic;
namespace MassiveArmy.Industrial {
    [Serializable] public sealed class SurvivalStationState {
        public long entity,owner,dockedShip;public int body;
        public double radius,mu,epoch,ux,uy,uz,vx,vy,vz;
        public List<long> invitedActors=new List<long>();
        public FramePose Position(double time){
            double angle=Math.IEEERemainder(Math.Sqrt(mu/(radius*radius*radius))*(time-epoch),Math.PI*2),c=Math.Cos(angle)*radius,s=Math.Sin(angle)*radius;
            return new FramePose{body=body,x=ux*c+vx*s,y=uy*c+vy*s,z=uz*c+vz*s};
        }
        public bool Valid {
            get {
                foreach(double n in new[]{radius,mu,epoch,ux,uy,uz,vx,vy,vz})if(!SurvivalServerConfig.Finite(n))return false;
                return body>0&&radius>=100&&radius<=1e12&&mu>0&&mu<=1e30&&epoch>=0&&Math.Abs(ux*ux+uy*uy+uz*uz-1)<1e-6&&Math.Abs(vx*vx+vy*vy+vz*vz-1)<1e-6&&Math.Abs(ux*vx+uy*vy+uz*vz)<1e-6;
            }
        }
    }
    public sealed partial class IndustrialAuthority {
        // Only the simulation constructs orbital parameters; there is no raw client station record command.
        internal string DeploySurvivalStation(long actorId,SurvivalStationState station){lock(gate){
            var actor=World.Actor(actorId);var player=World.multiplayer?.players.Find(p=>p.actor==actorId);
            var ship=player==null?null:World.multiplayer.ships.Find(s=>s.entity==player.controlledShip&&s.owner==actorId&&s.pilot==actorId);
            if(actor==null||actor.health<=0||ship==null||ship.landed||ship.autoFlight)return "Pilot your ship in orbit to deploy a station";
            if(station==null||!station.Valid||station.entity!=0||station.owner!=actorId||station.body!=ship.pose.body||station.dockedShip!=0||station.invitedActors==null||station.invitedActors.Count!=0||station.Position(World.multiplayer.simulationSeconds).Distance(ship.pose)>1000)return "Invalid station orbit";
            World.multiplayer.stations??=new List<SurvivalStationState>();
            if(World.multiplayer.stations.Count>=256||World.multiplayer.stations.FindAll(s=>s.owner==actorId).Count>=8)return "Station capacity reached (8 per owner, 256 per world)";
            if(actor.inventory.Count("docking-station-kit")<1)return "Craft and carry a docking station kit first";
            EnsureRespawnSnapshot();station.entity=World.nextId;
            Append(new IndustrialJournalEntry{stationDeployment=station});ApplyStationDeployment(World,station);return null;
        }}
        static void ApplyStationDeployment(IndustrialWorld world,SurvivalStationState station){
            var actor=world.Actor(station.owner);if(!station.Valid||actor==null||world.multiplayer==null||station.entity!=world.nextId||!actor.inventory.Remove("docking-station-kit",1))throw new InvalidOperationException("Invalid station deployment receipt");
            world.multiplayer.stations??=new List<SurvivalStationState>();world.multiplayer.stations.Add(station);world.AllocateId();
        }
    }
}
