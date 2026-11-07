namespace MassiveArmy.Planetary {
    public sealed partial class VehicleController {
        // The GPU already stores RTS destinations, formations and stop orders.
        // Proximity/detail must never take movement authority away from its flow map.
        public bool LocalMovement=>Controlled&&(Seats.PlayerPermissions&VehicleSeatPermissions.Movement)!=0;
    }
}
