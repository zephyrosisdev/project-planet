using System;

namespace MassiveArmy.Planetary
{
    public enum VehicleOccupantKind { Empty, Player, AI }
    public struct VehicleSeatOccupant
    {
        public VehicleOccupantKind kind;
        // Optional GPU army ID, never a CPU unit copy. uint.MaxValue denotes built-in crew.
        public uint entityId;
    }
    /// <summary>Bounded crew-role ownership; no crew GameObjects or reconstructed army entities.</summary>
    public sealed class VehicleSeatManager
    {
        readonly VehicleDefinition definition;
        readonly VehicleSeatOccupant[] occupants;
        public int PlayerSeat {get;private set;}=-1;
        public event Action<int> Changed;
        public VehicleSeatManager(VehicleDefinition definition){
            this.definition=definition;occupants=new VehicleSeatOccupant[definition.seats.Length];
            for(int i=0;i<occupants.Length;i++)if(definition.seats[i].aiByDefault)occupants[i]=new VehicleSeatOccupant{kind=VehicleOccupantKind.AI,entityId=uint.MaxValue};
        }
        public VehicleSeatOccupant Occupant(int seat)=>occupants[seat];
        public VehicleSeatPermissions PlayerPermissions=>PlayerSeat<0?VehicleSeatPermissions.None:definition.seats[PlayerSeat].permissions;
        public bool TryEnterPlayer(int seat){
            if(seat<0||seat>=occupants.Length||occupants[seat].kind==VehicleOccupantKind.Player)return false;
            // Built-in AI may yield its seat. An explicitly assigned unit must be removed first.
            if(occupants[seat].kind==VehicleOccupantKind.AI&&occupants[seat].entityId!=uint.MaxValue)return false;
            LeavePlayer();occupants[seat]=new VehicleSeatOccupant{kind=VehicleOccupantKind.Player,entityId=uint.MaxValue};PlayerSeat=seat;Changed?.Invoke(seat);return true;
        }
        public void LeavePlayer(){if(PlayerSeat<0)return;int old=PlayerSeat;PlayerSeat=-1;occupants[old]=new VehicleSeatOccupant{kind=definition.seats[old].aiByDefault?VehicleOccupantKind.AI:VehicleOccupantKind.Empty,entityId=uint.MaxValue};Changed?.Invoke(old);}
        public bool AssignAI(int seat,uint entityId=uint.MaxValue){
            if(seat<0||seat>=occupants.Length||occupants[seat].kind!=VehicleOccupantKind.Empty)return false;
            if(entityId!=uint.MaxValue)for(int i=0;i<occupants.Length;i++)if(occupants[i].kind!=VehicleOccupantKind.Empty&&occupants[i].entityId==entityId)return false;
            occupants[seat]=new VehicleSeatOccupant{kind=VehicleOccupantKind.AI,entityId=entityId};Changed?.Invoke(seat);return true;
        }
        public bool RemoveAI(int seat){if(seat<0||seat>=occupants.Length||occupants[seat].kind!=VehicleOccupantKind.AI)return false;occupants[seat]=default;Changed?.Invoke(seat);return true;}
        public bool AIHas(VehicleSeatPermissions permission){for(int i=0;i<occupants.Length;i++)if(occupants[i].kind==VehicleOccupantKind.AI&&(definition.seats[i].permissions&permission)==permission)return true;return false;}
    }
}
