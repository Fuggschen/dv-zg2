namespace ZG2
{
    /// <summary>
    /// Interface for multiplayer integration operations.
    /// Implemented by the ZG2.MP assembly when the multiplayer mod is present.
    /// ZG2.Game only interacts with MP through this interface — no direct MPAPI references.
    /// </summary>
    public interface IZG2MPIntegration
    {
        /// <summary>Returns true when the multiplayer mod is loaded AND a session is connected.</summary>
        bool IsMultiplayerActive { get; }

        /// <summary>Returns true when in single-player OR when this instance is the host.</summary>
        bool IsSinglePlayerOrHost { get; }

        /// <summary>Returns true when connected as a client (not host).</summary>
        bool IsClient { get; }

        /// <summary>Checks whether any client has reported a ZG2 board on the given car.</summary>
        bool HasClientReportedBoard(string carId);

        /// <summary>Client sends a notification to the host that a board was attached.</summary>
        void NotifyBoardAttached(string carId);

        /// <summary>Client sends a notification to the host that a board was removed.</summary>
        void NotifyBoardRemoved(string carId);

        /// <summary>Host broadcasts current settings to all connected clients.</summary>
        void BroadcastSettings(float penaltyPercent, int boardsPerStation,
            bool showMapMarkers, bool spawnBoardsAtStations, float checkIntervalSeconds);

        /// <summary>Initialise MP integration (subscribe to lifecycle events, register packets).</summary>
        void Initialise();

        /// <summary>Shut down MP integration (unsubscribe, clear state).</summary>
        void Shutdown();
    }
}