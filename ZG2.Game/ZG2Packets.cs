using MPAPI.Interfaces.Packets;

namespace ZG2
{
    /// <summary>
    /// Sent by a client to the host when a ZG2 board is attached to a train car.
    /// </summary>
    public class ZG2BoardAttachedPacket : IPacket
    {
        /// <summary>
        /// The ID of the train car the board was attached to (e.g. "ABC-1234").
        /// </summary>
        public string CarId { get; set; }
    }

    /// <summary>
    /// Sent by a client to the host when a ZG2 board is removed from a train car.
    /// </summary>
    public class ZG2BoardRemovedPacket : IPacket
    {
        /// <summary>
        /// The ID of the train car the board was removed from (e.g. "ABC-1234").
        /// </summary>
        public string CarId { get; set; }
    }

    /// <summary>
    /// Sent by the host to clients to synchronise ZG2 settings.
    /// </summary>
    public class ZG2SettingsSyncPacket : IPacket
    {
        public float PenaltyPercent { get; set; }
        public int BoardsPerStation { get; set; }
        public bool ShowMapMarkers { get; set; }
        public float CheckIntervalSeconds { get; set; }
    }
}
