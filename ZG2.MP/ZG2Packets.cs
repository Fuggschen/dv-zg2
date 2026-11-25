using MPAPI.Interfaces.Packets;

namespace ZG2.MP
{
    /// <summary>Client → host: a ZG2 board was mounted (pose relative to the car).</summary>
    public class ZG2BoardAttachedPacket : IPacket
    {
        public string CarId { get; set; }
        /// <summary>Client-side identifier of the gadget (unique per player).</summary>
        public int BoardKey { get; set; }
        public float PX { get; set; }
        public float PY { get; set; }
        public float PZ { get; set; }
        public float FX { get; set; }
        public float FY { get; set; }
        public float FZ { get; set; }
    }

    /// <summary>Client → host: a ZG2 board was removed.</summary>
    public class ZG2BoardRemovedPacket : IPacket
    {
        public string CarId { get; set; }
        public int BoardKey { get; set; }
    }

    /// <summary>Host → clients: a car gained or lost its last ZG2 board.</summary>
    public class ZG2MarkerStatePacket : IPacket
    {
        public string CarId { get; set; }
        public bool Marked { get; set; }
    }

    /// <summary>Host → clients: all host-controlled settings as one string.</summary>
    public class ZG2SettingsSyncPacket : IPacket
    {
        public string Data { get; set; }
    }

    /// <summary>Host → clients: checkpoint protocol of a job (for the job report page).</summary>
    public class ZG2JobLogPacket : IPacket
    {
        public string JobId { get; set; }
        public string Data { get; set; }
    }
}
